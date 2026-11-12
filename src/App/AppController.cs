using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using MacroKit.Core;
using MacroKit.Macros;
using MacroKit.Phrases;
using MacroKit.Recording;
using MacroKit.Win32;
using Rec = MacroKit.Core.Recording;

namespace MacroKit.App
{
    public enum ActivityKind { None, Recording, Playing, Macro, Picking }

    [Flags]
    public enum SaveParts { None = 0, Settings = 1, Phrases = 2, Macros = 4, All = 7 }

    /// <summary>What is running right now. Written by background threads, read by the UI (plain fields; a stale value is harmless).</summary>
    public sealed class ActivityInfo
    {
        public ActivityKind Kind;
        public string Name = "";
        public RunOrigin Origin;
        public string MacroId = "";
        public int Pass, TotalPasses, StepIndex, StepCount, ElapsedMs, DurationMs;
        public double Speed = 1;
        public string Message = "";
        public DateTime Started = DateTime.Now;
        /// <summary>Picking a window: the window that would be used if the person presses "Use this".</summary>
        public WindowInfo Candidate;
        /// <summary>A plain-language warning shown with the activity (for example that the target window runs as administrator).</summary>
        public string Warning = "";
    }

    /// <summary>
    /// Owns the data, the input hook, the engines and the one running activity (recording, playback, macro run or window pick).
    /// Everything public is called on the UI thread; background threads and the hook thread marshal back with the dispatcher.
    /// </summary>
    public sealed class AppController : IDisposable
    {
        const int HotkeyRecord = 1, HotkeyPlay = 2, HotkeyStop = 3, HotkeyDeck = 4, HotkeyMacroBase = 100;
        const ushort VkEscape = 0x1B;

        readonly Dispatcher dispatcher;
        readonly InputHook hook;
        readonly WindowSystem windows;
        readonly IInputSink input;
        readonly IClipboard clipboard = new Win32Clipboard();
        readonly IClock clock = new SystemClock();
        readonly ExpansionEngine engine;
        readonly Recorder recorder;
        readonly DispatcherTimer saveTimer;
        readonly DispatcherTimer pickTimer;
        readonly Dictionary<int, string> macroByHotkeyId = new Dictionary<int, string>();
        readonly List<int> registeredHotkeys = new List<int>();
        readonly Dictionary<string, string> hotkeyErrors = new Dictionary<string, string>();

        SaveParts dirty = SaveParts.None;
        bool unsaved;
        string hotkeySignature = "";
        volatile int activityKind;
        volatile bool escStops = true;
        bool progressPosted;
        // Created in Begin, before anything else happens, so an Esc pressed during start-up always has something to cancel.
        volatile CancellationTokenSource cts;
        Thread worker;
        bool mainHiddenByActivity;
        bool mainWasMinimized;
        int hotkeySuspensions;
        bool disposed;
        static bool? selfElevated;
        DateTime pickStarted;
        IntPtr pickBaseline;
        IntPtr pickSeen;
        int pickStable;
        Action<WindowInfo> pickDone;

        public readonly Storage Storage;
        public readonly bool InputEnabled;
        public readonly AppSettings Settings;
        public readonly List<PhraseDef> Phrases;
        public readonly List<MacroDef> Macros;
        public List<RecordingInfo> Recordings { get; private set; }
        /// <summary>The recording that was just made and not saved yet (null when there is none).</summary>
        public Rec Pending { get; internal set; }
        public ActivityInfo Activity { get; private set; }
        public string LastStatus { get; private set; }
        public bool LastStatusIsError { get; private set; }
        /// <summary>How the last activity ended (shown in the run pill for a few seconds); empty when it ended quietly.</summary>
        public string LastOutcome { get; private set; }
        public bool LastOutcomeIsError { get; private set; }
        public bool IsUnsaved { get { return unsaved; } }
        public IWindowSystem Windows { get { return windows; } }
        public IInputSink Input { get { return input; } }
        public bool HookRunning { get { return hook != null && hook.IsRunning; } }

        // Raised on the UI thread.
        public event Action<string, bool> StatusChanged = (text, error) => { };
        public event Action ActivityChanged = () => { };
        public event Action ActivityProgressed = () => { };
        public event Action<SaveParts> Edited = parts => { };
        public event Action RecordingsChanged = () => { };
        public event Action SaveStateChanged = () => { };
        public event Action HotkeysChanged = () => { };
        public event Action DeckVisibilityChanged = () => { };
        /// <summary>A short message for the run pill (title, message, is it an error); shown for a few seconds when nothing is running.</summary>
        public event Action<string, string, bool> Flash = (title, message, error) => { };

        // Set by the window shell so the controller can get the main window out of the way and bring it back.
        public Func<bool> IsMainWindowVisible = () => false;
        public Func<bool> IsMainWindowMinimized = () => false;
        public Action HideMainWindow = () => { };
        /// <summary>Opens the main window for the person (they asked for it); the argument is whether it may take focus.</summary>
        public Action<bool> ShowMainWindow = activate => { };
        /// <summary>
        /// Brings the main window back after a run without activating it. Arguments: it was minimized before the run (then it returns
        /// minimized), and it goes directly behind the window in front (true) or to the very bottom (false).
        /// </summary>
        public Action<bool, bool> RestoreMainWindow = (minimized, behindForeground) => { };
        public Func<string, bool> Confirm = message => true;
        /// <summary>False when there is no tray icon, so a hidden main window could not be reopened.</summary>
        public bool TrayAvailable = true;

        public AppController(Dispatcher dispatcher, Storage storage, bool inputEnabled)
        {
            this.dispatcher = dispatcher;
            Storage = storage;
            InputEnabled = inputEnabled;
            Settings = storage.LoadSettings();
            Phrases = storage.LoadPhrases();
            Macros = storage.LoadMacros();
            Recordings = storage.LoadRecordingIndex();
            Activity = new ActivityInfo();
            LastStatus = "";
            LastOutcome = "";
            escStops = Settings.EscStops;

            if (inputEnabled)
            {
                hook = new InputHook();
                windows = new WindowSystem(hook);
                input = new SendInputSink();
            }
            else
            {
                windows = new WindowSystem();
                input = new NoInputSink();
            }
            engine = new ExpansionEngine(input, clipboard, clock);
            engine.ExpansionFailed += message => Post(() => { SetStatus(message, true); Flash("Phrase not inserted", message, true); });
            recorder = new Recorder(clock);
            ApplyEngineSettings();
            engine.SetPhrases(Phrases);

            saveTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); Flush(); };
            pickTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(120) };
            pickTimer.Tick += (s, e) => PickTick();
        }

        // ------------------------------------------------------------------ start and stop

        /// <summary>Installs the hook and registers the hotkeys. Without input (tests, hidden desktop) this does nothing at all.</summary>
        public void Start()
        {
            if (!InputEnabled) return;
            try
            {
                hook.KeyHandler = OnHookKey;
                hook.MouseHandler = OnHookMouse;
                hook.ForegroundChanged += hwnd => engine.Reset();
                hook.HotkeyPressed += id => Post(() => OnHotkey(id));
                hook.Start();
            }
            catch (Exception ex)
            {
                Log.Error("The input hook did not start", ex);
                SetStatus("Phrases, recording and hotkeys are unavailable: " + ex.Message, true);
                return;
            }
            ApplyHotkeys();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            saveTimer.Stop();
            pickTimer.Stop();
            CancelWork();
            Thread t = worker;
            if (t != null && t.IsAlive) t.Join(3000);
            if (hook != null) { foreach (int id in registeredHotkeys.ToArray()) hook.UnregisterHotkey(id); hook.Dispose(); }
            engine.Dispose();
        }

        /// <summary>
        /// Tells the input hook which windows are Macro Kit's own (main window, Deck, run pill), so clicks and wheel turns over them
        /// are never recorded. Call on the UI thread whenever one of them is created or closed.
        /// </summary>
        public void SetOwnWindows(IntPtr[] hwnds)
        {
            if (hook != null) hook.SetOwnWindows(hwnds);
        }

        void Post(Action action)
        {
            if (disposed) return;
            try { dispatcher.BeginInvoke(action); }
            catch (Exception) { /* the dispatcher is shutting down */ }
        }

        // ------------------------------------------------------------------ status

        public void SetStatus(string text, bool error = false)
        {
            LastStatus = text ?? "";
            LastStatusIsError = error;
            StatusChanged(LastStatus, error);
        }

        /// <summary>
        /// A plain warning while a data file exists but could not be read (for example another program had it open): Macro Kit then
        /// works with defaults for it and does not save over the real file. Null when everything was read.
        /// </summary>
        public string ReadOnlyNotice()
        {
            IList<string> files = Storage.ReadOnlyFiles;
            if (files.Count == 0) return null;
            return "Macro Kit could not read " + string.Join(", ", files) + " (another program may have it open), so it is using defaults for now and will not save over the real file. Close the other program, then restart Macro Kit.";
        }

        // ------------------------------------------------------------------ saving

        /// <summary>Call after changing Settings, Phrases or Macros in place; saves about half a second later.</summary>
        public void MarkDirty(SaveParts parts)
        {
            dirty |= parts;
            if ((parts & SaveParts.Settings) != 0) { ApplyEngineSettings(); escStops = Settings.EscStops; }
            if ((parts & SaveParts.Phrases) != 0) engine.SetPhrases(Phrases);
            if (InputEnabled && hook.IsRunning && HotkeySignature() != hotkeySignature) ApplyHotkeys();
            saveTimer.Stop();
            saveTimer.Start();
            Edited(parts);
        }

        /// <summary>Writes pending changes now. Returns false (and shows "Unsaved") when a file could not be written.</summary>
        public bool Flush()
        {
            saveTimer.Stop();
            SaveParts todo = dirty;
            bool ok = true;
            try
            {
                if ((todo & SaveParts.Settings) != 0) Storage.SaveSettings(Settings);
                if ((todo & SaveParts.Phrases) != 0) Storage.SavePhrases(Phrases);
                if ((todo & SaveParts.Macros) != 0) Storage.SaveMacros(Macros);
                dirty = SaveParts.None;
            }
            catch (Exception ex)
            {
                ok = false;
                Log.Error("Saving failed", ex);
                SetStatus("Could not save your changes: " + ex.Message, true);
                saveTimer.Interval = TimeSpan.FromSeconds(4);   // try again soon
                saveTimer.Start();
            }
            if (ok) saveTimer.Interval = TimeSpan.FromMilliseconds(500);
            if (unsaved == ok) { unsaved = !ok; SaveStateChanged(); }
            return ok;
        }

        void ApplyEngineSettings()
        {
            engine.Enabled = Settings.ExpansionEnabled;
            engine.SetExcluded(Settings.ExcludedPrograms);
        }

        // ------------------------------------------------------------------ hotkeys

        public IDictionary<string, string> HotkeyErrors { get { return hotkeyErrors; } }

        string HotkeySignature()
        {
            return string.Join("|", new[] { Settings.RecordHotkey, Settings.PlayHotkey, Settings.StopHotkey, Settings.DeckHotkey }
                .Concat(Macros.Select(m => m.Id + "=" + m.Hotkey)));
        }

        /// <summary>Registers every hotkey again (after any hotkey changed). Failures are kept in <see cref="HotkeyErrors"/>.</summary>
        public void ApplyHotkeys()
        {
            if (!InputEnabled || !hook.IsRunning) return;
            foreach (int id in registeredHotkeys) hook.UnregisterHotkey(id);
            registeredHotkeys.Clear();
            // While a hotkey box is capturing keys nothing is registered (Windows would swallow the keys before the box saw them);
            // the combinations are still checked so a duplicate is reported right away.
            macroByHotkeyId.Clear();
            hotkeyErrors.Clear();
            var used = new Dictionary<string, string>();
            Register("Record", HotkeyRecord, Settings.RecordHotkey, "Record", used);
            Register("Play", HotkeyPlay, Settings.PlayHotkey, "Play", used);
            Register("Stop", HotkeyStop, Settings.StopHotkey, "Stop", used);
            Register("Deck", HotkeyDeck, Settings.DeckHotkey, "Deck", used);
            for (int i = 0; i < Macros.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(Macros[i].Hotkey)) continue;
                int id = HotkeyMacroBase + i;
                if (Register(Macros[i].Id, id, Macros[i].Hotkey, "“" + Macros[i].Name + "”", used)) macroByHotkeyId[id] = Macros[i].Id;
            }
            hotkeySignature = HotkeySignature();
            HotkeysChanged();
        }

        bool Register(string key, int id, string text, string name, Dictionary<string, string> used)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            Hotkey hk;
            if (!Hotkey.TryParse(text, out hk)) { hotkeyErrors[key] = "“" + text + "” is not a hotkey Macro Kit understands."; return false; }
            if (!hk.IsValidGlobal) { hotkeyErrors[key] = hk + " needs Ctrl, Alt or Win (or be an F13–F24 key)."; return false; }
            string own;
            if (used.TryGetValue(hk.ToString(), out own)) { hotkeyErrors[key] = "Macro Kit already uses " + hk + " for " + own + "."; return false; }
            if (hotkeySuspensions > 0) { used[hk.ToString()] = name; return true; }
            string error;
            if (!hook.RegisterHotkey(id, hk, out error)) { hotkeyErrors[key] = error; return false; }
            used[hk.ToString()] = name;
            registeredHotkeys.Add(id);
            return true;
        }

        /// <summary>
        /// A hotkey box starts capturing: every global hotkey is released, so pressing a combination that is already assigned reaches
        /// the box instead of running its action. Calls nest; each one needs a <see cref="ResumeHotkeys"/>.
        /// </summary>
        public void SuspendHotkeys()
        {
            if (++hotkeySuspensions != 1) return;
            if (!InputEnabled || !hook.IsRunning) return;
            foreach (int id in registeredHotkeys) hook.UnregisterHotkey(id);
            registeredHotkeys.Clear();
        }

        /// <summary>The matching end of <see cref="SuspendHotkeys"/>: when the last box lets go, all hotkeys are registered again.</summary>
        public void ResumeHotkeys()
        {
            if (hotkeySuspensions == 0) return;
            if (--hotkeySuspensions == 0) ApplyHotkeys();
        }

        public bool HotkeysSuspended { get { return hotkeySuspensions > 0; } }

        void OnHotkey(int id)
        {
            switch (id)
            {
                case HotkeyRecord:
                    if (Activity.Kind == ActivityKind.Recording) StopRecording(); else StartRecording(RunOrigin.Hotkey);
                    break;
                case HotkeyPlay:
                    if (Activity.Kind == ActivityKind.Playing) Stop(); else StartPlayback(RunOrigin.Hotkey);
                    break;
                case HotkeyStop: Stop(); break;
                case HotkeyDeck: ToggleDeck(); break;
                default:
                    string macroId;
                    MacroDef macro;
                    if (macroByHotkeyId.TryGetValue(id, out macroId) && (macro = Macros.FirstOrDefault(m => m.Id == macroId)) != null)
                    {
                        if (Activity.Kind == ActivityKind.Macro && Activity.MacroId == macro.Id) Stop(); else StartMacro(macro, RunOrigin.Hotkey);
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ hook callbacks (hook thread: fast, never throw)

        bool OnHookKey(KeyEvent e, string typed)
        {
            try
            {
                if (e.Down && !e.Injected && escStops && (e.Vk & 0xFF) == VkEscape)
                {
                    int kind = activityKind;
                    if (kind == (int)ActivityKind.Playing || kind == (int)ActivityKind.Macro) CancelWork();
                }
                // Phrases pause while recording, so a recording replays exactly what was typed.
                if (recorder.IsRecording) { recorder.OnKey(e); return false; }
                // ...and while a macro or a playback runs, so a phrase can never paste over the macro's clipboard or interleave with its keys.
                int running = activityKind;
                if (running == (int)ActivityKind.Playing || running == (int)ActivityKind.Macro) return false;
                return engine.OnKey(e, typed, windows.ForegroundProcessName);
            }
            catch (Exception) { return false; }
        }

        void OnHookMouse(MouseEvent e)
        {
            try
            {
                if (e.Kind == MouseEventKind.Down) engine.Reset();
                recorder.OnMouse(e);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ activities

        public bool IsBusy { get { return Activity.Kind != ActivityKind.None; } }

        /// <summary>Why the app cannot restart or leave right now, or null when it can.</summary>
        public string BusyReason()
        {
            switch (Activity.Kind)
            {
                case ActivityKind.Recording: return "Stop the recording first.";
                case ActivityKind.Playing: return "Stop the playback first.";
                case ActivityKind.Macro: return "Stop the macro first.";
                case ActivityKind.Picking: return "Finish picking a window first.";
                default: return null;
            }
        }

        /// <summary>
        /// Something could not start. The message goes to the status line, and when the person did not start it from the main window
        /// (hotkey, Deck, tray) it also flashes on the run pill for a few seconds, because Macro Kit may be hidden in the tray.
        /// </summary>
        bool Refuse(string message, RunOrigin origin = RunOrigin.MainWindow)
        {
            SetStatus(message, true);
            if (origin != RunOrigin.MainWindow) Flash("Can't start", message, true);
            return false;
        }

        bool CanBegin(RunOrigin origin)
        {
            if (!InputEnabled) return Refuse("Input is turned off in this session, so nothing can be recorded or played.", origin);
            string busy = BusyReason();
            if (busy != null) return Refuse(busy, origin);
            return true;
        }

        ActivityInfo Begin(ActivityKind kind, string name, RunOrigin origin)
        {
            // The cancellation source exists before anything slow happens (hiding the window, bringing the target forward),
            // so an Esc pressed in that moment still stops the activity.
            DisposeCancellation();
            cts = new CancellationTokenSource();
            Activity = new ActivityInfo { Kind = kind, Name = name, Origin = origin };
            activityKind = (int)kind;
            LastOutcome = "";
            LastOutcomeIsError = false;
            if (kind == ActivityKind.Macro || kind == ActivityKind.Playing) engine.Reset();   // phrases ignore the keyboard while it runs
            return Activity;
        }

        void DisposeCancellation()
        {
            CancellationTokenSource old = cts;
            cts = null;
            if (old != null) old.Dispose();
        }

        void End(string outcome, bool outcomeIsError)
        {
            RunOrigin origin = Activity.Origin;
            Activity = new ActivityInfo();
            activityKind = 0;
            DisposeCancellation();
            LastOutcome = outcome ?? "";
            LastOutcomeIsError = outcomeIsError;
            if (!string.IsNullOrEmpty(outcome)) SetStatus(outcome, outcomeIsError);
            RestoreMain(origin);
            ActivityChanged();
        }

        // ---- administrator windows (Windows blocks input between a normal and an elevated program)

        public const string ElevationMessage = "This window runs as administrator, so Windows blocks Macro Kit's input there. Run Macro Kit as administrator to use it with that window.";

        static bool IsSelfElevated()
        {
            if (selfElevated.HasValue) return selfElevated.Value;
            bool elevated = false;
            try
            {
                using (System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                    elevated = new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { }
            selfElevated = elevated;
            return elevated;
        }

        /// <summary>The warning to show when the window input is aimed at runs as administrator and Macro Kit does not; otherwise null.</summary>
        public static string ElevationWarning(WindowInfo target, bool macroKitElevated)
        {
            return target != null && target.IsElevated && !macroKitElevated ? ElevationMessage : null;
        }

        // The window a recording or playback works in: the one in front when Alt+Tab would list it, otherwise the last one used.
        void WarnIfTargetIsElevated(ActivityInfo activity)
        {
            try
            {
                IntPtr fg = windows.Foreground;
                IntPtr hwnd = fg != IntPtr.Zero && !windows.IsOwnWindow(fg) && windows.IsSwitchable(fg) ? fg : windows.LastExternalForeground;
                string warning = ElevationWarning(Describe(hwnd), IsSelfElevated());
                if (warning == null) return;
                activity.Warning = warning;
                SetStatus(warning, true);
            }
            catch (Exception ex) { Log.Warn("Could not check whether the window runs as administrator: " + ex.Message); }
        }

        /// <summary>Cancels whatever runs in the background (thread-safe).</summary>
        void CancelWork()
        {
            CancellationTokenSource c = cts;
            if (c == null) return;
            try { c.Cancel(); } catch (ObjectDisposedException) { }
        }

        /// <summary>Stop button, pill, Deck, tray, Stop hotkey: stops recording, playback, a macro run or window picking.</summary>
        public void Stop()
        {
            switch (Activity.Kind)
            {
                case ActivityKind.Recording: StopRecording(); break;
                case ActivityKind.Picking: EndPick(null, true); break;
                case ActivityKind.None: break;
                default: CancelWork(); break;
            }
        }

        // The "get out of the way" sequence: the target window is brought forward while Macro Kit still has the right to
        // do that, then the main window disappears (from the screen, Alt+Tab and the taskbar).
        void GetOutOfTheWay(bool activateLastExternal)
        {
            if (!IsMainWindowVisible()) return;
            mainWasMinimized = IsMainWindowMinimized();
            if (activateLastExternal && windows.IsOwnWindow(windows.Foreground))
            {
                IntPtr target = windows.LastExternalForeground;
                if (target != IntPtr.Zero && !windows.Activate(target, 600)) Log.Warn("Could not bring the previous window forward before hiding Macro Kit.");
            }
            HideMainWindow();
            mainHiddenByActivity = true;
        }

        // After a run the main window comes back only if it was on screen before the run, and it never covers the window the person
        // is working in: minimized stays minimized; otherwise it sits directly behind the window in front (a run started from the main
        // window) or at the very bottom (Deck, hotkey, tray), so it is not first in Alt+Tab either.
        void RestoreMain(RunOrigin origin)
        {
            if (!mainHiddenByActivity) return;
            mainHiddenByActivity = false;
            if (IsMainWindowVisible()) return;   // the person opened it again in the meantime
            if (Settings.ShowWindowAfterRun || !TrayAvailable) RestoreMainWindow(mainWasMinimized, origin == RunOrigin.MainWindow);
        }

        // ---- macros

        public bool StartMacro(MacroDef macro, RunOrigin origin)
        {
            if (!CanBegin(origin)) return false;
            ParseResult parsed = MacroParser.Parse(macro.Script, macro.NewLineIsEnter);
            if (!parsed.Ok)
            {
                ParseError first = parsed.Errors[0];
                return Refuse("“" + macro.Name + "” has an error. Line " + first.Line + ": " + first.Message, origin);
            }
            if (parsed.Steps.Count == 0) return Refuse("“" + macro.Name + "” has nothing to run yet.", origin);

            MacroDef snapshot = Json.Deserialize<MacroDef>(Json.Serialize(macro));
            ActivityInfo activity = Begin(ActivityKind.Macro, macro.Name, origin);
            activity.MacroId = macro.Id;
            activity.StepCount = parsed.Steps.Count;
            activity.TotalPasses = macro.Repeat == RepeatMode.Once ? 1 : macro.Repeat == RepeatMode.Times ? Math.Max(1, macro.RepeatTimes) : 0;
            CancellationToken token = cts.Token;
            var runner = new MacroRunner(input, windows, clock, clipboard);
            runner.Progress += p => OnMacroProgress(activity, p);
            // The runner calls this once the window it runs in is in front and before its first step.
            runner.TargetReady = ready => HideForRun(activity, ready);
            StartWorker("macro", () => runner.Run(snapshot, parsed, origin, token), result => FinishRun(result, "“" + macro.Name + "”", "finished"));
            ActivityChanged();
            return true;
        }

        void OnMacroProgress(ActivityInfo activity, RunProgress p)
        {
            activity.Pass = p.Pass;
            activity.TotalPasses = p.TotalPasses;
            activity.StepIndex = p.StepIndex;
            activity.StepCount = p.StepCount;
            if (!string.IsNullOrEmpty(p.Message)) activity.Message = p.Message;
            PostProgress();
        }

        // Worker thread. Macro Kit steps aside on the UI thread and this waits (at most a second, and not past a Stop) until that is
        // done, so the first key the macro sends can never meet a visible Macro Kit window. It posts and waits on an event rather than
        // calling Dispatcher.Invoke, so Stop, Exit and Dispose can never end up waiting for each other.
        void HideForRun(ActivityInfo activity, CancellationToken token)
        {
            if (disposed) return;
            var hidden = new ManualResetEventSlim(false);
            try
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    try { if (!disposed && ReferenceEquals(Activity, activity) && !token.IsCancellationRequested) GetOutOfTheWay(false); }
                    catch (Exception ex) { Log.Warn("Hiding Macro Kit for a macro failed: " + ex.Message); }
                    finally { hidden.Set(); }
                }));
            }
            catch (Exception) { return; }   // the dispatcher is shutting down
            hidden.Wait(1000, token);
        }

        // ---- recordings

        public bool StartRecording(RunOrigin origin)
        {
            if (!CanBegin(origin)) return false;
            if (Pending != null)
            {
                // An earlier recording that is still unsaved is never replaced without the person knowing.
                if (origin == RunOrigin.MainWindow && !Settings.AutoSaveRecordings)
                {
                    if (!Confirm("The recording you have not saved yet will be replaced. Record anyway?")) return false;
                }
                else
                {
                    SavePending();
                    if (Pending != null)
                    {
                        if (origin != RunOrigin.MainWindow)
                            return Refuse("The last recording could not be saved yet, so Macro Kit did not start a new one. Open Macro Kit and save it first.", origin);
                        if (!Confirm("The last recording could not be saved and will be replaced. Record anyway?")) return false;
                    }
                }
            }
            ActivityInfo activity = Begin(ActivityKind.Recording, "Recording", origin);
            WarnIfTargetIsElevated(activity);
            GetOutOfTheWay(true);
            recorder.Start(Settings.RecordMouseMoves);
            ActivityChanged();
            return true;
        }

        public void StopRecording()
        {
            if (Activity.Kind != ActivityKind.Recording) return;
            var strip = new List<Hotkey>();
            foreach (string text in new[] { Settings.RecordHotkey, Settings.StopHotkey })
            {
                Hotkey hk;
                if (Hotkey.TryParse(text, out hk)) strip.Add(hk);
            }
            Rec recording = recorder.Stop(strip);
            if (recording.Events.Count == 0) { End("Nothing was recorded.", false); return; }
            Pending = recording;
            Settings.SelectedRecordingId = recording.Id;
            MarkDirty(SaveParts.Settings);
            string note = recording.Events.Count + " events, " + FormatDuration(recording.DurationMs) + ".";
            if (Settings.AutoSaveRecordings && SavePending()) End("Recording saved as “" + recording.Name + "” (" + note + ")", false);
            else End("Recorded " + note + " Save it to keep it.", false);
            RecordingsChanged();
        }

        public bool StartPlayback(RunOrigin origin)
        {
            if (!CanBegin(origin)) return false;
            Rec recording = SelectedRecording();
            if (recording == null) return Refuse("Pick a recording to play first.", origin);
            if (recording.Events.Count == 0) return Refuse("This recording is empty, so there is nothing to play.", origin);
            PlaybackSettings settings = new PlaybackSettings
            {
                Speed = Settings.Playback.Speed, Repeat = Settings.Playback.Repeat,
                RepeatTimes = Settings.Playback.RepeatTimes, RepeatDelayMs = Settings.Playback.RepeatDelayMs
            };
            ActivityInfo activity = Begin(ActivityKind.Playing, recording.Name, origin);
            activity.Speed = settings.Speed;
            activity.TotalPasses = settings.Repeat == RepeatMode.Once ? 1 : settings.Repeat == RepeatMode.Times ? Math.Max(1, settings.RepeatTimes) : 0;
            activity.StepCount = recording.Events.Count;
            CancellationToken token = cts.Token;
            WarnIfTargetIsElevated(activity);
            GetOutOfTheWay(true);
            var player = new Player(input, clock);
            player.Progress += p =>
            {
                activity.Pass = p.Pass;
                activity.TotalPasses = p.TotalPasses;
                activity.StepIndex = p.EventIndex;
                activity.StepCount = p.EventCount;
                activity.ElapsedMs = p.ElapsedMs;
                activity.DurationMs = p.DurationMs;
                PostProgress();
            };
            // A hotkey that started this may still be held down; the player waits for its modifiers to be released.
            bool fromHotkey = origin == RunOrigin.Hotkey;
            StartWorker("playback", () => player.Play(recording, settings, token, fromHotkey), result => FinishRun(result, "“" + recording.Name + "”", "finished playing"));
            ActivityChanged();
            return true;
        }

        void StartWorker(string name, Func<RunResult> work, Action<RunResult> finish)
        {
            worker = new Thread(() =>
            {
                RunResult result;
                try { result = work(); }
                catch (Exception ex)
                {
                    Log.Error("A " + name + " run failed unexpectedly", ex);
                    result = new RunResult { Error = "Something went wrong: " + ex.Message };
                }
                Post(() => finish(result));
            }) { IsBackground = true, Name = "Macro Kit " + name };
            worker.Start();
        }

        void FinishRun(RunResult result, string what, string finishedWord)
        {
            if (!string.IsNullOrEmpty(result.Error)) End(result.Error, true);
            else if (result.Cancelled) End("Stopped " + what + ".", false);
            else End(Capitalize(what) + " " + finishedWord + (result.PassesDone > 1 ? " (" + result.PassesDone + " passes)." : "."), false);
        }

        void PostProgress()
        {
            if (progressPosted) return;
            progressPosted = true;
            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { progressPosted = false; ActivityProgressed(); }));
            }
            catch (Exception) { progressPosted = false; }
        }

        // ---- window picking

        /// <summary>
        /// "Pick" buttons: Macro Kit steps aside, the person switches to the window they want (or presses "Use this" for the one in
        /// front) and <paramref name="done"/> gets it. Nothing is clicked or sent; the foreground window is only watched.
        /// </summary>
        public bool BeginPick(Action<WindowInfo> done)
        {
            string busy = BusyReason();
            if (busy != null) return Refuse(busy);
            ActivityInfo activity = Begin(ActivityKind.Picking, "Pick a window", RunOrigin.MainWindow);
            pickDone = done;
            pickStarted = DateTime.UtcNow;
            pickBaseline = IntPtr.Zero;
            pickSeen = IntPtr.Zero;
            pickStable = 0;
            GetOutOfTheWay(true);
            activity.Candidate = Describe(windows.LastExternalForeground);
            pickTimer.Start();
            ActivityChanged();
            return true;
        }

        WindowInfo Describe(IntPtr hwnd) { return hwnd == IntPtr.Zero ? null : windows.Describe(hwnd); }

        void PickTick()
        {
            if (Activity.Kind != ActivityKind.Picking) { pickTimer.Stop(); return; }
            double seconds = (DateTime.UtcNow - pickStarted).TotalSeconds;
            if (seconds > 90) { EndPick(null, true); return; }
            IntPtr fg = windows.Foreground;
            // Macro Kit's own windows, the taskbar, the desktop, the Start menu and the task switcher can never be picked.
            bool own = fg == IntPtr.Zero || windows.IsOwnWindow(fg) || !windows.IsSwitchable(fg);
            if (!own)
            {
                WindowInfo current = Describe(fg);
                if (current != null && current.Title.Length > 0 && (Activity.Candidate == null || Activity.Candidate.Handle != fg || Activity.Candidate.Title != current.Title))
                {
                    Activity.Candidate = current;
                    ActivityProgressed();
                }
            }
            if (seconds < 0.5)
            {
                // Windows is still handing focus to the next window after Macro Kit hid; that one is the starting point.
                if (!own) pickBaseline = fg;
                return;
            }
            if (own || fg == pickBaseline) { pickStable = 0; pickSeen = IntPtr.Zero; return; }
            pickStable = fg == pickSeen ? pickStable + 1 : 1;
            pickSeen = fg;
            if (pickStable < 3) return;   // wait until focus has settled (Alt+Tab passes through other windows)
            WindowInfo info = windows.ListSwitchableWindows().FirstOrDefault(w => w.Handle == fg);
            if (info != null) EndPick(info, false);
        }

        /// <summary>The pill's "Use this" button: takes the window that is in front.</summary>
        public void AcceptPickCandidate()
        {
            if (Activity.Kind != ActivityKind.Picking) return;
            IntPtr fg = windows.Foreground;
            WindowInfo info = (fg != IntPtr.Zero && !windows.IsOwnWindow(fg) && windows.IsSwitchable(fg)) ? Describe(fg) : null;
            info = info ?? Activity.Candidate ?? Describe(windows.LastExternalForeground);
            if (info == null) { SetStatus("There is no window to use yet.", true); return; }
            EndPick(info, false);
        }

        void EndPick(WindowInfo picked, bool cancelled)
        {
            pickTimer.Stop();
            Action<WindowInfo> done = pickDone;
            pickDone = null;
            Activity = new ActivityInfo();
            activityKind = 0;
            DisposeCancellation();
            bool wasHidden = mainHiddenByActivity;
            mainHiddenByActivity = false;
            if (wasHidden) ShowMainWindow(true);   // the person is working in Macro Kit, so it comes back in front (an explicit open)
            ActivityChanged();
            if (picked != null && done != null) done(picked);
            else SetStatus(cancelled ? "Picking cancelled." : "", false);
        }

        // ------------------------------------------------------------------ recordings storage

        public void RefreshRecordings()
        {
            Recordings = Storage.LoadRecordingIndex();
            RecordingsChanged();
        }

        /// <summary>The recording to play: the unsaved one when it is selected, otherwise the selected saved one.</summary>
        public Rec SelectedRecording()
        {
            string id = Settings.SelectedRecordingId;
            if (Pending != null && (id == Pending.Id || string.IsNullOrEmpty(id))) return Pending;
            if (!string.IsNullOrEmpty(id)) { Rec saved = Storage.LoadRecording(id); if (saved != null) return saved; }
            return Pending;
        }

        public string SelectedRecordingName()
        {
            string id = Settings.SelectedRecordingId;
            if (Pending != null && id == Pending.Id) return Pending.Name;
            RecordingInfo info = Recordings.FirstOrDefault(r => r.Id == id);
            return info == null ? (Pending != null ? Pending.Name : "") : info.Name;
        }

        public void SelectRecording(string id)
        {
            if (Settings.SelectedRecordingId == id) return;
            Settings.SelectedRecordingId = id ?? "";
            MarkDirty(SaveParts.Settings);
        }

        public bool SavePending()
        {
            if (Pending == null) return true;
            try
            {
                Storage.SaveRecording(Pending);
                Settings.SelectedRecordingId = Pending.Id;
                Pending = null;
                MarkDirty(SaveParts.Settings);
                RefreshRecordings();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Saving a recording failed", ex);
                SetStatus("Could not save the recording: " + ex.Message, true);
                return false;
            }
        }

        public void RenameRecording(string id, string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return;
            try
            {
                if (Pending != null && Pending.Id == id) Pending.Name = name;
                else Storage.RenameRecording(id, name);
                RefreshRecordings();
            }
            catch (Exception ex) { SetStatus("Could not rename the recording: " + ex.Message, true); }
        }

        public void DeleteRecording(string id)
        {
            try
            {
                if (Pending != null && Pending.Id == id) Pending = null;
                else Storage.DeleteRecording(id);
                if (Settings.SelectedRecordingId == id) { Settings.SelectedRecordingId = ""; MarkDirty(SaveParts.Settings); }
                RefreshRecordings();
            }
            catch (Exception ex) { SetStatus("Could not delete the recording: " + ex.Message, true); }
        }

        public void ExportRecording(string id, string file)
        {
            try
            {
                if (Pending != null && Pending.Id == id && !SavePending()) return;   // exporting keeps the recording, so it is saved first
                Storage.ExportRecording(id, file);
                SetStatus("Exported to " + file, false);
            }
            catch (Exception ex) { SetStatus("Could not export the recording: " + ex.Message, true); }
        }

        public void ImportRecording(string file)
        {
            try
            {
                Rec imported = Storage.ImportRecording(file);
                Settings.SelectedRecordingId = imported.Id;
                MarkDirty(SaveParts.Settings);
                RefreshRecordings();
                SetStatus("Imported “" + imported.Name + "”.", false);
            }
            catch (Exception ex) { SetStatus("Could not import that file: " + ex.Message, true); }
        }

        // ------------------------------------------------------------------ deck

        public void ToggleDeck()
        {
            Settings.DeckVisible = !Settings.DeckVisible;
            MarkDirty(SaveParts.Settings);
            DeckVisibilityChanged();
        }

        public void SetPhrasesEnabled(bool enabled)
        {
            Settings.ExpansionEnabled = enabled;
            MarkDirty(SaveParts.Settings);
        }

        // ------------------------------------------------------------------ helpers

        public static string FormatDuration(long ms)
        {
            if (ms < 1000) return ms + " ms";
            double seconds = ms / 1000.0;
            if (seconds < 60) return seconds.ToString(seconds < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture) + " s";
            long total = (long)Math.Round(seconds);
            if (total < 3600) return (total / 60) + ":" + (total % 60).ToString("00");
            return (total / 3600) + ":" + ((total / 60) % 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        static string Capitalize(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        /// <summary>Output sink used when input is turned off: any attempt to send input fails loudly instead of reaching the screen.</summary>
        sealed class NoInputSink : IInputSink
        {
            static InvalidOperationException Off() { return new InvalidOperationException("Input is turned off in this session."); }
            public void KeyDown(ushort vk) { throw Off(); }
            public void KeyUp(ushort vk) { throw Off(); }
            public void KeyCombo(IList<ushort> keys) { throw Off(); }
            public void TypeText(string text) { throw Off(); }
            public void MouseMoveTo(int x, int y) { throw Off(); }
            public void MouseMoveBy(int dx, int dy) { throw Off(); }
            public void MouseButton(Core.MouseButton button, bool down) { throw Off(); }
            public void MouseWheel(int delta, bool horizontal) { throw Off(); }
            public ScreenPoint CursorPosition { get { return new ScreenPoint(0, 0); } }
            public bool IsPhysicallyDown(ushort vk) { return false; }
            public void MouseMoveNormalized(int x65535, int y65535) { throw Off(); }
        }
    }
}
