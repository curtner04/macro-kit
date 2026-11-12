using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using MacroKit.Core;

namespace MacroKit.Macros
{
    public sealed class RunProgress
    {
        public int Pass;           // 1-based; 0 for messages sent before the first pass
        public int TotalPasses;    // 0 = continuous
        public int StepIndex;      // 1-based position of the step being run; 0 before the first
        public int StepCount;
        public string Message;
    }

    /// <summary>
    /// Runs a parsed Super Macro in another window: finds and primes the target, repeats the passes, sends the steps
    /// and always lets go of whatever keys and buttons it pressed. Call Run from a background thread.
    /// </summary>
    public sealed class MacroRunner
    {
        const int FocusSettleMs = 750;
        const int PollMs = 25;
        const int ActivateTimeoutMs = 1000;
        const int AltTabActivateTimeoutMs = 500;
        const int ModifierWaitMs = 3000;
        const int MaxPasses = 100000;

        static readonly Regex VariableReference = new Regex(@"\$([A-Za-z0-9_]+)", RegexOptions.Compiled);
        static readonly string[] BuiltInCharacters = { "SMCOLON", "SMDOLLAR", "SMLCB", "SMRCB", "SMSPACE", "SMENTER" };
        static readonly ushort[] ModifiersToRelease = { 0x10, 0x11, 0x12, 0x5B, 0x5C };   // Shift, Ctrl, Alt, left and right Win

        readonly IInputSink input;
        readonly IWindowSystem windows;
        readonly IClock clock;
        readonly IClipboard clipboard;

        public event Action<RunProgress> Progress;

        /// <summary>
        /// Called on the worker thread after the target (and partner) window has been brought to the front and before the first step.
        /// The shell uses it to finish hiding Macro Kit's own windows. The handler may block briefly; it receives the run's cancellation
        /// token and should return promptly when it is cancelled. An exception in it is logged and ignored.
        /// </summary>
        public Action<CancellationToken> TargetReady { get; set; }

        /// <summary>Tells whether Macro Kit itself runs as administrator. Tests replace it.</summary>
        internal Func<bool> ElevationProbe = IsCurrentProcessElevated;

        public MacroRunner(IInputSink input, IWindowSystem windows, IClock clock, IClipboard clipboard)
        {
            this.input = input;
            this.windows = windows;
            this.clock = clock;
            this.clipboard = clipboard;
        }

        /// <summary>Blocking; call from a background thread. Never throws (problems go to RunResult.Error).</summary>
        public RunResult Run(MacroDef macro, ParseResult parsed, RunOrigin origin, CancellationToken token)
        {
            var result = new RunResult();
            var session = new Session(this, macro, parsed, token);
            try
            {
                session.Execute(origin, result);
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
            }
            catch (RunStopped stop)
            {
                result.Error = stop.Message;
            }
            catch (Exception e)
            {
                Log.Error("Macro run failed.", e);
                result.Error = "Couldn't finish the macro: " + e.Message;
            }
            finally
            {
                session.ReleaseHeldInput();
            }
            if (!result.Completed && result.Error == null && token.IsCancellationRequested) result.Cancelled = true;
            return result;
        }

        static bool IsCurrentProcessElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        // A reason to stop the run that the user should read (not a crash).
        sealed class RunStopped : Exception
        {
            public RunStopped(string message) : base(message) { }
        }

        // The state of one Run call: resolved windows, variables and what is currently held down.
        sealed class Session
        {
            readonly MacroRunner owner;
            readonly MacroDef macro;
            readonly ParseResult parsed;
            readonly CancellationToken token;
            readonly Dictionary<string, string> variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            readonly List<ushort> heldKeys = new List<ushort>();
            readonly List<MouseButton> heldButtons = new List<MouseButton>();
            WindowInfo target;
            WindowInfo partner;
            IntPtr otherWindow;               // specific window, no partner: the window the first real Alt+Tab settled on
            bool awaitingOtherWindow;         // a real Alt+Tab was sent and its destination is not known yet
            int pass, totalPasses;

            IInputSink Input { get { return owner.input; } }
            IWindowSystem Windows { get { return owner.windows; } }
            IClock Clock { get { return owner.clock; } }

            public Session(MacroRunner owner, MacroDef macro, ParseResult parsed, CancellationToken token)
            {
                this.owner = owner;
                this.macro = macro;
                this.parsed = parsed;
                this.token = token;
            }

            public void Execute(RunOrigin origin, RunResult result)
            {
                if (macro == null || parsed == null) throw new RunStopped("There is no macro to run.");
                if (!parsed.Ok)
                {
                    ParseError first = parsed.Errors[0];
                    throw new RunStopped("This macro has a problem on line " + first.Line + ": " + first.Message);
                }
                if (parsed.Steps.Count == 0) throw new RunStopped("This macro has nothing to run yet.");

                ResolveTarget(origin);
                ResolvePartner();
                if (origin == RunOrigin.Hotkey) WaitForModifiersReleased();
                PrimeFocus();
                SeedVariables();
                AnnounceTargetReady();

                totalPasses = TotalPasses();
                for (pass = 1; totalPasses == 0 || pass <= totalPasses; pass++)
                {
                    token.ThrowIfCancellationRequested();
                    RunPass();
                    result.PassesDone = pass;
                    bool more = totalPasses == 0 || pass < totalPasses;
                    if (more) Wait(macro.RepeatDelayMs);
                }
                result.Completed = true;
            }

            int TotalPasses()
            {
                switch (macro.Repeat)
                {
                    case RepeatMode.Continuous: return 0;
                    case RepeatMode.Times: return Math.Max(1, Math.Min(MaxPasses, macro.RepeatTimes));
                    default: return 1;
                }
            }

            // ---- Setup ----

            void ResolveTarget(RunOrigin origin)
            {
                if (macro.Target == TargetKind.SpecificWindow)
                {
                    WindowMatch wanted = macro.TargetWindow ?? new WindowMatch();
                    if (wanted.IsEmpty) throw new RunStopped("Choose the window this macro should run in first.");
                    target = Windows.Find(wanted);
                    if (target == null) throw new RunStopped("Can't find the window to run in (" + wanted.Describe() + "). Open it and try again.");
                    return;
                }

                IntPtr handle = IntPtr.Zero;
                if (origin == RunOrigin.Deck || origin == RunOrigin.Hotkey)
                {
                    // The window in front counts only when Alt+Tab would list it: not the taskbar, the desktop, the Start menu or the task switcher.
                    IntPtr foreground = Windows.Foreground;
                    if (foreground != IntPtr.Zero && !Windows.IsOwnWindow(foreground) && Windows.IsSwitchable(foreground)) handle = foreground;
                }
                if (handle == IntPtr.Zero) handle = Windows.LastExternalForeground;
                target = handle == IntPtr.Zero ? null : Windows.Describe(handle);
                if (target == null) throw new RunStopped("Click the window you want the macro to run in first.");
            }

            void ResolvePartner()
            {
                WindowMatch wanted = macro.AltTabPartner;
                if (wanted == null || wanted.IsEmpty) return;
                partner = Windows.Find(wanted);
                if (partner == null) throw new RunStopped("Can't find the Alt+Tab window (" + wanted.Describe() + "). Open it and try again.");
                if (partner.Handle == target.Handle) throw new RunStopped("The Alt+Tab window has to be a different window from the one the macro runs in.");
            }

            // The hotkey that started the run may still be held; its modifiers would turn every key into a shortcut.
            void WaitForModifiersReleased()
            {
                long deadline = Clock.NowMs + ModifierWaitMs;
                while (AnyModifierDown() && Clock.NowMs < deadline) Clock.Sleep(PollMs, token);
            }

            bool AnyModifierDown()
            {
                foreach (ushort vk in ModifiersToRelease)
                {
                    if (Input.IsPhysicallyDown(vk)) return true;
                }
                return false;
            }

            // With a partner, activate it first and the target last so the system's recent-window order is target, partner.
            void PrimeFocus()
            {
                if (partner != null) Activate(partner);
                if (partner != null || Windows.Foreground != target.Handle) Activate(target);
                if (target.IsElevated && !owner.ElevationProbe())
                    Raise("“" + NameOf(target) + "” runs as administrator, so Windows may block the keys and clicks. Run Macro Kit as administrator too if nothing happens.");
            }

            // Lets the shell finish hiding Macro Kit before the first step, so a real Alt+Tab can never land on it. The handler runs on
            // this worker thread and blocks for as long as it needs (it should give up quickly and honor the token). If hiding
            // moved the focus, the target is brought back to the front.
            void AnnounceTargetReady()
            {
                Action<CancellationToken> ready = owner.TargetReady;
                if (ready == null) return;
                try
                {
                    ready(token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Log.Warn("The macro 'target ready' handler failed: " + e.Message);
                }
                token.ThrowIfCancellationRequested();
                if (Windows.Foreground != target.Handle) Activate(target);
            }

            void Activate(WindowInfo window)
            {
                if (!Windows.Activate(window.Handle, ActivateTimeoutMs))
                    throw new RunStopped("Windows didn't let Macro Kit switch to " + NameOf(window) + ".");
            }

            void SeedVariables()
            {
                variables["SMCOLON"] = ":";
                variables["SMDOLLAR"] = "$";
                variables["SMLCB"] = "{";
                variables["SMRCB"] = "}";
                variables["SMSPACE"] = " ";
                variables["SMENTER"] = "\n";
                ScreenPoint cursor = Input.CursorPosition;
                variables["MOUSE_X"] = cursor.X.ToString(CultureInfo.InvariantCulture);
                variables["MOUSE_Y"] = cursor.Y.ToString(CultureInfo.InvariantCulture);
            }

            // ---- Passes and steps ----

            void RunPass()
            {
                int count = parsed.Steps.Count;
                for (int i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    MacroStep step = parsed.Steps[i];
                    string message = IsSmartAltTab(step) ? "Switching to " + NameOf(AltTabDestination()) : step.Describe();
                    Raise(message, i + 1, count);
                    RunStep(step);
                }
            }

            void RunStep(MacroStep step)
            {
                if (step.IsDynamic) step = FillInNumbers(step);
                if (step.SendsInput) CheckFocus();

                switch (step.Kind)
                {
                    case StepKind.Pause:
                        Wait(step.Ms);
                        return;
                    case StepKind.Keys:
                        if (IsSmartAltTab(step)) SwitchWindow(step);
                        else
                        {
                            PressCombo(step.Keys);
                            if (IsAltTabCombo(step) && GuardsSpecificWindowWithoutPartner() && otherWindow == IntPtr.Zero) awaitingOtherWindow = true;
                        }
                        break;
                    case StepKind.KeyDown:
                        Input.KeyDown(step.Keys[0]);
                        Remember(heldKeys, step.Keys[0]);
                        break;
                    case StepKind.KeyUp:
                        Input.KeyUp(step.Keys[0]);
                        heldKeys.RemoveAll(k => KeyNames.BaseVk(k) == KeyNames.BaseVk(step.Keys[0]));
                        break;
                    case StepKind.KeyRepeat:
                        for (int i = 0; i < step.Count; i++)
                        {
                            if (i > 0)
                            {
                                Wait(step.Ms > 0 ? step.Ms : macro.KeyDelayMs);
                                CheckFocus();
                            }
                            PressCombo(step.Keys);
                        }
                        break;
                    case StepKind.Text:
                        Input.TypeText(step.Text);
                        break;
                    case StepKind.MouseMoveTo:
                        Input.MouseMoveTo(step.X, step.Y);
                        break;
                    case StepKind.MouseMoveBy:
                        Input.MouseMoveBy(step.X, step.Y);
                        break;
                    case StepKind.MouseMoveNormalized:
                        Input.MouseMoveNormalized(step.X, step.Y);
                        break;
                    case StepKind.MouseDown:
                        Remember(heldButtons, step.Button);
                        Input.MouseButton(step.Button, true);
                        break;
                    case StepKind.MouseUp:
                        Input.MouseButton(step.Button, false);
                        heldButtons.Remove(step.Button);
                        break;
                    case StepKind.MouseClick:
                        Click(step.Button);
                        break;
                    case StepKind.MouseDoubleClick:
                        Click(step.Button);
                        Click(step.Button);
                        break;
                    case StepKind.MouseWheel:
                        for (int i = 0; i < step.Count; i++) Input.MouseWheel(120 * step.Direction, step.Horizontal);
                        break;
                    case StepKind.SaveMousePos:
                        ScreenPoint here = Input.CursorPosition;
                        variables["MOUSE_X"] = here.X.ToString(CultureInfo.InvariantCulture);
                        variables["MOUSE_Y"] = here.Y.ToString(CultureInfo.InvariantCulture);
                        break;
                    case StepKind.LoadMousePos:
                        MoveToSavedPosition(step);
                        break;
                    case StepKind.SetVar:
                        variables[step.Var] = Substitute(step.Value);
                        break;
                    case StepKind.OutputVar:
                        string text = Lookup(step.Var);
                        if (text.Length > 0) Input.TypeText(text);
                        break;
                    case StepKind.UnsetVar:
                        variables.Remove(step.Var);
                        break;
                    case StepKind.UnsetAllVars:
                        UnsetUserVariables();
                        break;
                    case StepKind.SetClipboard:
                        owner.clipboard.SetText(Substitute(step.Value));
                        break;
                    case StepKind.VarFromClipboard:
                        variables[step.Var] = owner.clipboard.GetText() ?? "";
                        break;
                }
                if (step.SendsInput) Wait(macro.KeyDelayMs);
            }

            void PressCombo(ushort[] keys)
            {
                // Remember the keys first: if the combo is cut short by an error, cleanup lets go of them.
                int before = heldKeys.Count;
                foreach (ushort key in keys) heldKeys.Add(key);
                Input.KeyCombo(keys);
                heldKeys.RemoveRange(before, keys.Length);
            }

            void Click(MouseButton button)
            {
                heldButtons.Add(button);
                Input.MouseButton(button, true);
                Input.MouseButton(button, false);
                heldButtons.Remove(button);
            }

            static void Remember<T>(List<T> list, T item)
            {
                if (!list.Contains(item)) list.Add(item);
            }

            void MoveToSavedPosition(MacroStep step)
            {
                int x, y;
                if (!int.TryParse(Lookup("MOUSE_X"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out x)
                    || !int.TryParse(Lookup("MOUSE_Y"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out y))
                    throw new RunStopped("Line " + step.Line + ": $MOUSE_X and $MOUSE_Y have to be whole numbers to move back to the saved position.");
                Input.MouseMoveTo(x, y);
            }

            // ---- Smart Alt+Tab ----

            static bool IsAltKey(ushort vk)
            {
                ushort k = KeyNames.BaseVk(vk);
                return k == 0x12 || k == 0xA4 || k == 0xA5;
            }

            static bool IsAltTabCombo(MacroStep step)
            {
                return step.Kind == StepKind.Keys && !step.IsDynamic && step.Keys.Length == 2
                    && IsAltKey(step.Keys[0]) && KeyNames.BaseVk(step.Keys[1]) == 0x09;
            }

            bool IsSmartAltTab(MacroStep step)
            {
                return partner != null && IsAltTabCombo(step);
            }

            WindowInfo AltTabDestination()
            {
                return Windows.Foreground == target.Handle ? partner : target;
            }

            // Switches straight to the other window. If Windows refuses, falls back to the real keys and checks where they landed.
            void SwitchWindow(MacroStep step)
            {
                WindowInfo other = AltTabDestination();
                if (Windows.Activate(other.Handle, AltTabActivateTimeoutMs)) return;

                PressCombo(step.Keys);
                long deadline = Clock.NowMs + AltTabActivateTimeoutMs;
                while (Windows.Foreground != other.Handle)
                {
                    if (Clock.NowMs >= deadline) throw new RunStopped("Alt+Tab didn't switch to " + NameOf(other) + ", so the macro stopped before typing in the wrong place.");
                    Clock.Sleep(PollMs, token);
                }
            }

            // ---- Focus guard ----

            bool GuardsSpecificWindowWithoutPartner()
            {
                return macro.Target == TargetKind.SpecificWindow && macro.StopIfFocusLeaves && partner == null;
            }

            // Without a partner, a real Alt+Tab may land on any window except Macro Kit's. The first window it settles on becomes the
            // "other" window; from then on the run may use the target and that one. A third window, or Macro Kit, stops the run.
            bool FocusIsWrong()
            {
                IntPtr foreground = Windows.Foreground;
                if (Windows.IsOwnWindow(foreground)) return true;
                if (macro.Target != TargetKind.SpecificWindow || !macro.StopIfFocusLeaves) return false;
                if (foreground == target.Handle) return false;
                if (partner != null) return foreground != partner.Handle;
                if (otherWindow != IntPtr.Zero) return foreground != otherWindow;
                if (!awaitingOtherWindow) return true;
                // A real Alt+Tab is in progress. No window, or one Alt+Tab would not list (the switcher itself), means focus is still settling.
                if (foreground == IntPtr.Zero || !Windows.IsSwitchable(foreground)) return true;
                otherWindow = foreground;
                awaitingOtherWindow = false;
                return false;
            }

            // Focus may still be settling right after a window switch, so the wrong window gets a short grace period.
            void CheckFocus()
            {
                if (!FocusIsWrong()) return;
                long deadline = Clock.NowMs + FocusSettleMs;
                while (true)
                {
                    Clock.Sleep(PollMs, token);
                    if (!FocusIsWrong()) return;
                    if (Clock.NowMs >= deadline) break;
                }
                WindowInfo culprit = Windows.Describe(Windows.Foreground);
                throw new RunStopped("Stopped because " + (culprit == null ? "another window" : NameOf(culprit)) + " took focus.");
            }

            // ---- Variables ----

            string Lookup(string name)
            {
                string value;
                return variables.TryGetValue(name, out value) && value != null ? value : "";
            }

            string Substitute(string text)
            {
                if (string.IsNullOrEmpty(text) || text.IndexOf('$') < 0) return text ?? "";
                return VariableReference.Replace(text, m => Expand(m.Groups[1].Value));
            }

            // A $name is a defined variable, or one of the built-in $SM... characters followed by plain text ($SMSPACEworld).
            string Expand(string name)
            {
                string value;
                if (variables.TryGetValue(name, out value)) return value ?? "";
                foreach (string builtIn in BuiltInCharacters)
                {
                    if (name.StartsWith(builtIn, StringComparison.OrdinalIgnoreCase)) return Lookup(builtIn) + name.Substring(builtIn.Length);
                }
                return "";
            }

            // Keeps $SM... and $MOUSE_... because the script can't recreate them.
            void UnsetUserVariables()
            {
                var remove = new List<string>();
                foreach (string name in variables.Keys)
                {
                    bool builtIn = name.StartsWith("SM", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("MOUSE_X", StringComparison.OrdinalIgnoreCase) || name.Equals("MOUSE_Y", StringComparison.OrdinalIgnoreCase);
                    if (!builtIn) remove.Add(name);
                }
                foreach (string name in remove) variables.Remove(name);
            }

            MacroStep FillInNumbers(MacroStep step)
            {
                MacroStep built;
                string error = MacroParser.Build(step.Command, step.Args, Substitute, out built);
                if (error != null) throw new RunStopped("Line " + step.Line + ": " + error);
                built.Index = step.Index; built.Length = step.Length; built.Line = step.Line; built.Column = step.Column;
                return built;
            }

            // ---- Helpers ----

            void Wait(int ms)
            {
                if (ms > 0) Clock.Sleep(ms, token);
                else token.ThrowIfCancellationRequested();
            }

            void Raise(string message, int stepIndex = 0, int stepCount = 0)
            {
                Action<RunProgress> handler = owner.Progress;
                if (handler == null) return;
                try
                {
                    handler(new RunProgress { Pass = pass, TotalPasses = totalPasses, StepIndex = stepIndex, StepCount = stepCount, Message = message });
                }
                catch (Exception e)
                {
                    Log.Warn("A macro progress listener failed: " + e.Message);
                }
            }

            static string NameOf(WindowInfo window)
            {
                return string.IsNullOrEmpty(window.Title) ? window.ProcessName : window.Title;
            }

            // Lets go of every key and button still held, newest first. Never throws.
            public void ReleaseHeldInput()
            {
                for (int i = heldKeys.Count - 1; i >= 0; i--)
                {
                    try { Input.KeyUp(heldKeys[i]); }
                    catch (Exception e) { Log.Warn("Couldn't release a key after the macro ended: " + e.Message); }
                }
                heldKeys.Clear();
                for (int i = heldButtons.Count - 1; i >= 0; i--)
                {
                    try { Input.MouseButton(heldButtons[i], false); }
                    catch (Exception e) { Log.Warn("Couldn't release a mouse button after the macro ended: " + e.Message); }
                }
                heldButtons.Clear();
            }
        }
    }
}
