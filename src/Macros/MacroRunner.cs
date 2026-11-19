using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        /// <summary>Opens programs, closes them, plays sounds, sets the volume and locks the PC for {{RUN}}, {{CLOSE}} and friends.</summary>
        public ISystemActions Actions { get; set; }

        /// <summary>Finds another saved macro by name for {{MACRO:name}} (null = none). Called on the worker thread.</summary>
        public Func<string, MacroDef> FindMacro { get; set; }

        /// <summary>
        /// Which choice each {{SWITCH}} block takes next. Share one dictionary between runs so the blocks take turns from run to run
        /// (the shell keeps one for as long as Macro Kit runs). Locked while in use.
        /// </summary>
        public IDictionary<string, int> Turns { get; set; }

        /// <summary>Picks a choice for {{RANDOM}}: 0 to n - 1. Tests replace it.</summary>
        internal Func<int, int> PickRandom = PickAtRandom;

        static readonly Random random = new Random();

        static int PickAtRandom(int choices)
        {
            lock (random) return random.Next(Math.Max(1, choices));
        }

        public MacroRunner(IInputSink input, IWindowSystem windows, IClock clock, IClipboard clipboard)
        {
            this.input = input;
            this.windows = windows;
            this.clock = clock;
            this.clipboard = clipboard;
            Actions = new NoSystemActions();
            Turns = new Dictionary<string, int>();
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
            readonly Dictionary<string, ParseResult> nested = new Dictionary<string, ParseResult>();   // macros started with {{MACRO}}, parsed once
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
                if (origin == RunOrigin.Hotkey || origin == RunOrigin.Schedule) WaitForModifiersReleased();
                PrimeFocus();
                SeedVariables();
                AnnounceTargetReady();
                WaitForReady();

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
                if (origin == RunOrigin.Deck || origin == RunOrigin.Hotkey || origin == RunOrigin.Schedule)
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

            // A program counter walks the steps so loops, conditions and choices can jump. Without them it is a plain pass in order.
            void RunPass()
            {
                RunSteps(parsed.Steps, new Frame { Key = MacroKey(macro), Chain = new List<string> { MacroKey(macro) } });
            }

            // Where a list of steps is running: the macro itself, or one it started with {{MACRO}}.
            sealed class Frame
            {
                public string Key;              // names this list of steps for the take-turns memory
                public List<string> Chain;      // the macros running, outermost first (to catch a macro that starts itself)
                public string Prefix = "";      // "Copy row: " in front of the progress messages of a macro started with {{MACRO}}
                public int ReportIndex;         // for those: the outer step to show the progress at (0 = report this list's own steps)
                public int ReportCount;
            }

            static string MacroKey(MacroDef m)
            {
                return string.IsNullOrEmpty(m.Id) ? "name:" + (m.Name ?? "") : m.Id;
            }

            void RunSteps(IList<MacroStep> steps, Frame frame)
            {
                int count = steps.Count;
                var loops = new Stack<int[]>();   // { LoopStart index, passes left }
                int i = 0;
                while (i < count)
                {
                    token.ThrowIfCancellationRequested();
                    MacroStep step = steps[i];
                    int shownIndex = frame.ReportIndex > 0 ? frame.ReportIndex : i + 1;
                    int shownCount = frame.ReportIndex > 0 ? frame.ReportCount : count;
                    switch (step.Kind)
                    {
                        case StepKind.LoopStart:
                            {
                                int times = LoopCount(step);
                                if (times <= 0) { i = step.Jump + 1; continue; }
                                Raise(frame.Prefix + step.Describe(), shownIndex, shownCount);
                                loops.Push(new[] { i, times });
                                i++;
                                continue;
                            }
                        case StepKind.LoopEnd:
                            {
                                int[] loop = loops.Count > 0 && loops.Peek()[0] == step.Jump ? loops.Peek() : null;
                                if (loop != null && --loop[1] > 0) { i = step.Jump + 1; continue; }
                                if (loop != null) loops.Pop();
                                i++;
                                continue;
                            }
                        case StepKind.If:
                        case StepKind.IfWindow:
                            {
                                bool holds = step.Kind == StepKind.If ? MacroParser.Compare(Substitute(step.Left), step.Op, Substitute(step.Right)) : WindowMatches(step.Value);
                                Raise(frame.Prefix + step.Describe() + (holds ? ": yes" : ": no"), shownIndex, shownCount);
                                i = holds ? i + 1 : step.Jump + 1;
                                continue;
                            }
                        case StepKind.Else:
                            // Reached only by running the IF branch to its end, so skip the ELSE branch.
                            i = step.Jump + 1;
                            continue;
                        case StepKind.SwitchStart:
                        case StepKind.RandomStart:
                            {
                                int choices = step.Branches == null ? 1 : step.Branches.Length;
                                int choice = step.Kind == StepKind.SwitchStart ? NextTurn(frame.Key + "#" + i, choices) : owner.PickRandom(choices);
                                Raise(frame.Prefix + (step.Kind == StepKind.SwitchStart ? "Taking turns: " : "Picked at random: ") + "choice " + (choice + 1) + " of " + choices, shownIndex, shownCount);
                                i = (step.Branches == null ? i : step.Branches[choice]) + 1;
                                continue;
                            }
                        case StepKind.Or:
                            // Reached only by running a choice to its end, so skip the other choices.
                            i = step.Jump + 1;
                            continue;
                        case StepKind.EndIf:
                        case StepKind.EndSwitch:
                        case StepKind.EndRandom:
                            i++;
                            continue;
                        case StepKind.RunMacro:
                            RunOtherMacro(step, frame, shownIndex, shownCount);
                            i++;
                            continue;
                    }
                    string message = IsSmartAltTab(step) ? "Switching to " + NameOf(AltTabDestination()) : step.Describe();
                    Raise(frame.Prefix + message, shownIndex, shownCount);
                    RunStep(step);
                    i++;
                }
            }

            // {{SWITCH}}: the first time choice 1, then 2, ... and round again. Remembered across runs while Macro Kit runs.
            int NextTurn(string key, int choices)
            {
                IDictionary<string, int> turns = owner.Turns;
                lock (turns)
                {
                    int next;
                    if (!turns.TryGetValue(key, out next) || next < 0 || next >= choices) next = 0;
                    turns[key] = (next + 1) % choices;
                    return next;
                }
            }

            // {{MACRO:name}}: runs that macro's script once, right here: same window, same variables, this macro's key delay.
            void RunOtherMacro(MacroStep step, Frame frame, int shownIndex, int shownCount)
            {
                string name = Substitute(step.Value).Trim();
                MacroDef other = owner.FindMacro == null ? null : owner.FindMacro(name);
                if (other == null) throw new RunStopped("Line " + step.Line + ": there is no macro called “" + name + "”.");
                string key = MacroKey(other);
                if (frame.Chain.Contains(key)) throw new RunStopped("“" + other.Name + "” starts itself through {{MACRO}}, so it would never end.");
                if (frame.Chain.Count > MaxNesting) throw new RunStopped("Macros start each other more than " + MaxNesting + " deep through {{MACRO}}.");
                ParseResult inner;
                if (!nested.TryGetValue(key, out inner))
                {
                    inner = MacroParser.Parse(other.Script, other.NewLineIsEnter);
                    nested[key] = inner;
                }
                if (!inner.Ok) throw new RunStopped("“" + other.Name + "” has a problem on line " + inner.Errors[0].Line + ": " + inner.Errors[0].Message);
                Raise(frame.Prefix + step.Describe(), shownIndex, shownCount);
                var chain = new List<string>(frame.Chain) { key };
                RunSteps(inner.Steps, new Frame
                {
                    Key = frame.Key + ">" + key,
                    Chain = chain,
                    Prefix = frame.Prefix + other.Name + ": ",
                    ReportIndex = shownIndex,
                    ReportCount = shownCount
                });
            }

            const int MaxNesting = 8;

            // An action that couldn't be done stops the run with its reason.
            static void Fail(MacroStep step, string error)
            {
                if (error != null) throw new RunStopped("Line " + step.Line + ": " + error);
            }

            // The window in front if it matches, otherwise the most recent one Alt+Tab would list whose title or program name contains the text.
            WindowInfo FindWindow(string text)
            {
                text = (text ?? "").Trim();
                if (text.Length == 0) return null;
                Func<WindowInfo, bool> matches = w => w != null
                    && ((w.Title ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || (w.ProcessName ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
                IntPtr front = Windows.Foreground;
                if (front != IntPtr.Zero && !Windows.IsOwnWindow(front))
                {
                    WindowInfo inFront = Windows.Describe(front);
                    if (matches(inFront)) return inFront;
                }
                return Windows.ListSwitchableWindows().FirstOrDefault(matches);
            }

            // {{FOCUS}} and {{WAITWINDOW}}: bring the window forward and make it the one the macro runs in, so the focus guard follows it.
            void SwitchTo(WindowInfo window)
            {
                if (Windows.Foreground != window.Handle) Activate(window);
                WaitForReady();
                if (partner != null && window.Handle == partner.Handle) return;
                target = window;
                otherWindow = IntPtr.Zero;
                awaitingOtherWindow = false;
            }

            void WaitForWindow(MacroStep step)
            {
                string wanted = Substitute(step.Value).Trim();
                long deadline = Clock.NowMs + step.Ms;
                while (true)
                {
                    WindowInfo window = FindWindow(wanted);
                    if (window != null) { SwitchTo(window); return; }
                    if (Clock.NowMs >= deadline)
                        throw new RunStopped("Line " + step.Line + ": no window matching “" + wanted + "” opened within " + (step.Ms / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " seconds.");
                    Clock.Sleep(WaitWindowPollMs, token);
                }
            }

            const int WaitWindowPollMs = 100;

            int LoopCount(MacroStep step)
            {
                if (step.Var == null) return step.Count;
                string text = Lookup(step.Var).Trim();
                int times;
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out times) || times < 0)
                    throw new RunStopped("{{LOOP:$" + step.Var + "}} needs a whole number of 0 or more, but $" + step.Var + " is \u201C" + text + "\u201D.");
                return Math.Min(times, MaxLoopTimes);
            }

            const int MaxLoopTimes = 100000;

            // The window in front matches when its title or program name contains the text (ignoring case).
            bool WindowMatches(string text)
            {
                string wanted = Substitute(text).Trim();
                WindowInfo front = Windows.Describe(Windows.Foreground);
                if (front == null || wanted.Length == 0) return false;
                return (front.Title ?? "").IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0
                    || (front.ProcessName ?? "").IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;
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
                            IntPtr before = Windows.Foreground;
                            bool copies = IsCopyCombo(step);
                            uint clipboardBefore = copies ? ClipboardSequence() : 0;
                            PressCombo(step.Keys);
                            if (copies) WaitForClipboardChange(clipboardBefore);
                            if (IsAltTabCombo(step))
                            {
                                if (GuardsSpecificWindowWithoutPartner() && otherWindow == IntPtr.Zero) awaitingOtherWindow = true;
                                WaitForSwitchAway(before);
                            }
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
                        TypePaced(step.Text);
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
                        if (text.Length > 0) TypePaced(text);
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
                    case StepKind.Open:
                        {
                            string what, arguments;
                            MacroParser.SplitRun(Substitute(step.Value), out what, out arguments);
                            if (what.Length == 0) throw new RunStopped("Line " + step.Line + ": {{RUN}} has nothing to open. Check that the variable it uses has been set.");
                            Fail(step, owner.Actions.Open(what, arguments));
                            break;
                        }
                    case StepKind.CloseApp:
                        {
                            string program = Substitute(step.Value).Trim();
                            int asked;
                            Fail(step, owner.Actions.CloseProgram(program, out asked));
                            if (asked == 0) Raise(program + " isn't open, so there was nothing to close.");
                            break;
                        }
                    case StepKind.PlaySound:
                        Fail(step, owner.Actions.PlaySound(Substitute(step.Value).Trim().Trim('"')));
                        break;
                    case StepKind.Lock:
                        Fail(step, owner.Actions.Lock());
                        break;
                    case StepKind.Volume:
                        {
                            int amount = step.Count;
                            bool relative = step.Relative;
                            if (step.Value != null)
                            {
                                string level = Substitute(step.Value);
                                if (!MacroParser.TryVolume(level, out amount, out relative))
                                    throw new RunStopped("Line " + step.Line + ": {{VOLUME}} needs 0 to 100, or a change like +10, but got “" + level + "”.");
                            }
                            Fail(step, owner.Actions.SetVolume(amount, relative));
                            break;
                        }
                    case StepKind.FocusWindow:
                        {
                            string wanted = Substitute(step.Value).Trim();
                            WindowInfo window = FindWindow(wanted);
                            if (window == null) throw new RunStopped("Line " + step.Line + ": there is no open window matching “" + wanted + "”.");
                            SwitchTo(window);
                            break;
                        }
                    case StepKind.WaitWindow:
                        WaitForWindow(step);
                        break;
                }
                if (step.SendsInput)
                {
                    Wait(macro.KeyDelayMs);
                    WaitForReady();
                }
            }

            // ---- Waiting for the window to catch up ----

            const int ReadyTimeoutMs = 1000;

            // Before the next step, let the window in front finish what it was sent: Windows has no "done" signal for keys,
            // so this waits until its program answers and has keyboard focus (at most a second, then the run goes on).
            void WaitForReady()
            {
                IntPtr front = Windows.Foreground;
                if (front != IntPtr.Zero && !Windows.IsOwnWindow(front)) Windows.WaitUntilReady(front, ReadyTimeoutMs);
                token.ThrowIfCancellationRequested();
            }

            // Text goes a few characters at a time with the key delay and a ready check in between, so a slow program
            // never drops or repeats characters.
            void TypePaced(string text)
            {
                Input.TypeTextPaced(text, () =>
                {
                    Wait(macro.KeyDelayMs);
                    WaitForReady();
                });
            }

            const int CopyWaitMs = 1000;

            // Ctrl+C, Ctrl+X, Ctrl+Insert and Shift+Delete put something on the clipboard; the next step (a paste, usually)
            // waits until it is really there.
            static bool IsCopyCombo(MacroStep step)
            {
                if (step.Kind != StepKind.Keys || step.IsDynamic || step.Keys.Length != 2) return false;
                ushort first = KeyNames.BaseVk(step.Keys[0]), second = KeyNames.BaseVk(step.Keys[1]);
                bool ctrl = first == 0x11 || first == 0xA2 || first == 0xA3;
                bool shift = first == 0x10 || first == 0xA0 || first == 0xA1;
                return (ctrl && (second == 'C' || second == 'X' || second == 0x2D)) || (shift && second == 0x2E);
            }

            uint ClipboardSequence()
            {
                try { return owner.clipboard == null ? 0 : owner.clipboard.SequenceNumber; }
                catch (Exception) { return 0; }
            }

            // At most a second: with nothing selected, many programs leave the clipboard alone and the run goes on.
            void WaitForClipboardChange(uint before)
            {
                if (owner.clipboard == null) return;
                long deadline = Clock.NowMs + CopyWaitMs;
                while (ClipboardSequence() == before && Clock.NowMs < deadline) Clock.Sleep(PollMs, token);
            }

            // A real Alt+Tab: wait for the switch to happen and for the new window to be ready, instead of a fixed pause.
            void WaitForSwitchAway(IntPtr before)
            {
                long deadline = Clock.NowMs + AltTabActivateTimeoutMs;
                while (Windows.Foreground == before && Clock.NowMs < deadline) Clock.Sleep(PollMs, token);
                WaitForReady();
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
                if (Windows.Activate(other.Handle, AltTabActivateTimeoutMs)) { WaitForReady(); return; }

                PressCombo(step.Keys);
                long deadline = Clock.NowMs + AltTabActivateTimeoutMs;
                while (Windows.Foreground != other.Handle)
                {
                    if (Clock.NowMs >= deadline) throw new RunStopped("Alt+Tab didn't switch to " + NameOf(other) + ", so the macro stopped before typing in the wrong place.");
                    Clock.Sleep(PollMs, token);
                }
                WaitForReady();
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
