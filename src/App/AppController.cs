using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using MacroKit.Core;
using MacroKit.Keybinds;
using MacroKit.Macros;
using MacroKit.Phrases;
using MacroKit.Recording;
using MacroKit.Win32;
using Rec = MacroKit.Core.Recording;

namespace MacroKit.App
{
    public enum ActivityKind { None, Recording, Playing, Macro, Picking }

    [Flags]
    public enum SaveParts { None = 0, Settings = 1, Phrases = 2, Macros = 4, Keybinds = 8, All = 15 }

    /// <summary>What is running right now. Written by background threads, read by the UI (plain fields; a stale value is harmless).</summary>
    public sealed class ActivityInfo
    {
        public ActivityKind Kind;
        public string Name = "";
        public RunOrigin Origin;
        public string MacroId = "";
        /// <summary>Playing: the recording that plays.</summary>
        public string RecordingId = "";
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
        const int HotkeyRecord = 1, HotkeyPlay = 2, HotkeyStop = 3, HotkeyDeck = 4, HotkeyMacroBase = 100, HotkeyRecordingBase = 30000;
        const ushort VkEscape = 0x1B;

        readonly Dispatcher dispatcher;
        readonly InputHook hook;
        readonly WindowSystem windows;
        readonly IInputSink input;
        readonly IClipboard clipboard = new Win32Clipboard();
        readonly IClock clock = new SystemClock();
        readonly ISystemActions systemActions;
        // Which choice each {{SWITCH}} block takes next, kept while Macro Kit runs so the blocks take turns from run to run.
        readonly Dictionary<string, int> switchTurns = new Dictionary<string, int>();
        readonly ExpansionEngine engine;
        readonly Recorder recorder;
        readonly DispatcherTimer saveTimer;
        readonly DispatcherTimer pickTimer;
        readonly DispatcherTimer scheduleTimer;
        readonly Dictionary<int, string> macroByHotkeyId = new Dictionary<int, string>();
        readonly Dictionary<int, string> recordingByHotkeyId = new Dictionary<int, string>();
        readonly List<int> registeredHotkeys = new List<int>();
        readonly Dictionary<string, string> hotkeyErrors = new Dictionary<string, string>();
        // Keys in hotkeyErrors whose hotkey did not get set up (the rest are warnings about a hotkey that works).
        readonly HashSet<string> hotkeyFailures = new HashSet<string>();

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
        bool quitting;
        DateTime pickStarted;
        IntPtr pickBaseline;
        IntPtr pickSeen;
        int pickStable;
        Action<WindowInfo> pickDone;
        int storageWarningsShown;   // how many of Storage.Warnings the person has been shown

        public readonly Storage Storage;
        public readonly bool InputEnabled;
        public readonly AppSettings Settings;
        public readonly List<PhraseDef> Phrases;
        public readonly List<MacroDef> Macros;
        /// <summary>Apps added by hand on the Global Keybinds tab.</summary>
        public readonly List<KeybindAppDef> KeybindApps;
        public List<RecordingInfo> Recordings { get; private set; }
        /// <summary>The recording that was just made and not saved yet (null when there is none).</summary>
        public Rec Pending { get; internal set; }
        public ActivityInfo Activity { get; private set; }
        public string LastStatus { get; private set; }
        public bool LastStatusIsError { get; private set; }
        /// <summary>How the last activity ended (shown in the run pill for a few seconds); empty when it ended quietly.</summary>
        public string LastOutcome { get; private set; }
        public bool LastOutcomeIsError { get; private set; }
        /// <summary>The run pill's title for <see cref="LastOutcome"/> ("Recorded", "Stopped"); null for the default ("Done").</summary>
        public string LastOutcomeHeading { get; private set; }
        public bool IsUnsaved { get { return unsaved; } }
        public IWindowSystem Windows { get { return windows; } }
        public IInputSink Input { get { return input; } }
        /// <summary>Reads screen colours ({{WAITPIXEL}} and the palette's colour Pick).</summary>
        public IScreen Screen { get { return screen; } }
        /// <summary>The window under a screen point (the palette's colour Pick).</summary>
        public IWindowProbe WindowProbe { get { return windows; } }
        readonly IScreen screen = new ScreenPixels();
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
            KeybindApps = storage.LoadKeybindApps();
            Recordings = storage.LoadRecordingIndex();
            LoadLicense();
            Activity = new ActivityInfo();
            LastStatus = "";
            LastOutcome = "";
            escStops = Settings.EscStops;

            if (inputEnabled)
            {
                hook = new InputHook();
                windows = new WindowSystem(hook);
                input = new SendInputSink();
                systemActions = new SystemActions();
            }
            else
            {
                windows = new WindowSystem();
                input = new NoInputSink();
                systemActions = new NoSystemActions();
            }
            engine = new ExpansionEngine(input, clipboard, clock);
            engine.ExpansionFailed += message => Post(() => { SetStatus(message, true); Flash("Phrase not inserted", message, true); });
            engine.AskForFields = AskPhraseFields;
            engine.InPasswordField = windows.FocusIsPasswordEdit;
            engine.Foreground = () => windows.Foreground;
            recorder = new Recorder(clock) { WindowProbe = windows };
            ApplyEngineSettings();
            engine.SetPhrases(Phrases);

            saveTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); Flush(); };
            pickTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(120) };
            pickTimer.Tick += (s, e) => PickTick();
            scheduleTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(5) };
            scheduleTimer.Tick += (s, e) => ScheduleTick();

            // Once per data folder: the example phrases and macros a new install starts with, added to an older setup too.
            // While phrases.json or macros.json is in use, nothing is added or marked: the next clean start adds them.
            IList<string> readOnlyFiles = storage.ReadOnlyFiles;
            bool examplesBlocked = Examples.TargetsBlocked(readOnlyFiles);
            if (Examples.ShouldAddMissing(Settings, storage.SettingsFromFile, readOnlyFiles))
            {
                Examples.AddMissing(Phrases, Macros);
                Settings.ExamplesAdded = true;
                engine.SetPhrases(Phrases);
                MarkDirty(SaveParts.Settings | SaveParts.Phrases | SaveParts.Macros);
            }
            else if (!Settings.ExamplesAdded && !(storage.SettingsFromFile && examplesBlocked))
            {
                // settings.json was missing, set aside or locked: only the flag is set (and saved when the file can be),
                // so examples the person deleted do not come back.
                Settings.ExamplesAdded = true;
                if (!readOnlyFiles.Contains("settings.json")) MarkDirty(SaveParts.Settings);
            }
            // A brand-new setup asks once which hotkeys to use (the Recorder's "Pick your hotkeys" card); an existing one never does.
            if (!storage.SettingsFromFile && !readOnlyFiles.Contains("settings.json")) Settings.HotkeysChosen = false;
            storageWarningsShown = storage.Warnings.Count;   // the shell shows these at startup
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
                hook.HotkeyPressed += id =>
                {
                    // The Stop hotkey cancels a playback or macro right here on the hook thread, like Esc, so the run stops before
                    // the person's held Ctrl/Shift/Alt can turn its next keys and clicks into shortcuts. OnHotkey then finds it stopping.
                    if (id == HotkeyStop)
                    {
                        int kind = activityKind;
                        if (kind == (int)ActivityKind.Playing || kind == (int)ActivityKind.Macro) CancelWork();
                    }
                    Post(() => OnHotkey(id));
                };
                hook.Start();
            }
            catch (Exception ex)
            {
                Log.Error("The input hook did not start", ex);
                SetStatus("Phrases, recording and hotkeys are unavailable: " + ex.Message, true);
                return;
            }
            ApplyHotkeys();
            // The pill and the recorder bar would say "Press <hotkey>" for a key that does nothing, so say it once here too.
            if (new[] { "Record", "Play", "Stop", "Deck" }.Any(HotkeyFailed))
                SetStatus("Some hotkeys could not be set up (another program may use them). See Settings.", true);
            scheduleTimer.Start();
        }

        public void Dispose()
        {
            if (disposed) return;
            // Edits from the last half second (signing out, a Shutdown from elsewhere) are written before anything stops.
            if (dirty != SaveParts.None) { try { Flush(); } catch (Exception) { } }
            disposed = true;
            saveTimer.Stop();
            pickTimer.Stop();
            scheduleTimer.Stop();
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

        /// <summary>
        /// Macro Kit is leaving: whatever runs is cancelled, and when it ends nothing is shown or brought back, because the windows
        /// are being closed (a window shown or flashed after that would throw or flicker).
        /// </summary>
        public void BeginQuit()
        {
            quitting = true;
            CancelWork();
        }

        void Post(Action action)
        {
            if (disposed || quitting) return;
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

        /// <summary>
        /// A window was moved or resized: saves Settings with the same delay, but skips the engine, hotkey and Edited work, which
        /// would otherwise run (and rebuild the Deck) for every pixel of a drag.
        /// </summary>
        public void MarkPlacementDirty()
        {
            MarkQuietlyDirty(SaveParts.Settings);
        }

        // Saved with the usual delay, but nothing is redone and nobody is told: for values no screen shows while they change.
        void MarkQuietlyDirty(SaveParts parts)
        {
            dirty |= parts;
            saveTimer.Stop();
            saveTimer.Start();
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
                if ((todo & SaveParts.Keybinds) != 0) Storage.SaveKeybindApps(KeybindApps);
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
                .Concat(Macros.Select(m => m.Id + "=" + (m.Enabled ? m.Hotkey : "")))
                .Concat(Settings.RecordingSettings.Where(o => RecordingExists(o.Id)).Select(o => "rec:" + o.Id + "=" + o.Hotkey)));
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
            recordingByHotkeyId.Clear();
            hotkeyErrors.Clear();
            hotkeyFailures.Clear();
            var used = new Dictionary<string, string>();
            Register("Record", HotkeyRecord, Settings.RecordHotkey, "Record", used);
            Register("Play", HotkeyPlay, Settings.PlayHotkey, "Play", used);
            Register("Stop", HotkeyStop, Settings.StopHotkey, "Stop", used);
            Register("Deck", HotkeyDeck, Settings.DeckHotkey, "Deck", used);
            for (int i = 0; i < Macros.Count; i++)
            {
                if (!Macros[i].Enabled || string.IsNullOrWhiteSpace(Macros[i].Hotkey)) continue;
                int id = HotkeyMacroBase + i;
                if (Register(Macros[i].Id, id, Macros[i].Hotkey, "“" + Macros[i].Name + "”", used)) macroByHotkeyId[id] = Macros[i].Id;
            }
            for (int i = 0; i < Settings.RecordingSettings.Count; i++)
            {
                RecordingOptions o = Settings.RecordingSettings[i];
                if (string.IsNullOrWhiteSpace(o.Hotkey) || !RecordingExists(o.Id)) continue;
                int id = HotkeyRecordingBase + i;
                if (Register(RecordingHotkeyKey(o.Id), id, o.Hotkey, "“" + RecordingName(o.Id) + "”", used)) recordingByHotkeyId[id] = o.Id;
            }
            hotkeySignature = HotkeySignature();
            HotkeysChanged();
        }

        bool Register(string key, int id, string text, string name, Dictionary<string, string> used)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            Hotkey hk;
            string problem = Hotkey.Problem(text, used, out hk);
            if (problem != null) return Failed(key, problem);
            // An everyday shortcut (Ctrl+C) still works as a hotkey, so a saved setup keeps working, but it is pointed out.
            string warning = Hotkey.EverydayWarning(hk) ?? WindowsShortcutWarning(hk);
            if (warning != null) hotkeyErrors[key] = warning;
            if (hotkeySuspensions > 0) { used[hk.ToString()] = name; return true; }
            string error;
            if (!hook.RegisterHotkey(id, hk, out error))
            {
                // Another program has it: the Global Keybinds tab may know which one (only for the programs it can read).
                if (error != null && error.IndexOf("already uses", StringComparison.Ordinal) >= 0) error += " Global Keybinds may show which program has it.";
                return Failed(key, error);
            }
            used[hk.ToString()] = name;
            registeredHotkeys.Add(id);
            return true;
        }

        bool Failed(string key, string message)
        {
            hotkeyErrors[key] = message;
            hotkeyFailures.Add(key);
            return false;
        }

        static List<Keybind> windowsShortcuts;

        static string WindowsShortcutWarning(Hotkey hk)
        {
            if (windowsShortcuts == null) windowsShortcuts = KeybindReaders.Windows().Keybinds;
            Keybind own = windowsShortcuts.FirstOrDefault(k => k.Matches(hk));
            return own == null ? null : "Windows uses " + hk + " (" + own.Action + "); while Macro Kit holds it, that won't work.";
        }

        /// <summary>True when the hotkey kept under <paramref name="key"/> in <see cref="HotkeyErrors"/> is not set up (not just a warning).</summary>
        public bool HotkeyFailed(string key) { return hotkeyFailures.Contains(key); }

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
                    if (Activity.Kind == ActivityKind.Recording) StopRecording(ParsedHotkey(Settings.RecordHotkey)); else StartRecording(RunOrigin.Hotkey);
                    break;
                case HotkeyPlay:
                    if (Activity.Kind == ActivityKind.Playing) Stop(); else StartPlayback(RunOrigin.Hotkey);
                    break;
                case HotkeyStop:
                    if (Activity.Kind == ActivityKind.Recording) StopRecording(ParsedHotkey(Settings.StopHotkey)); else Stop();
                    break;
                case HotkeyDeck: ToggleDeck(); break;
                default:
                    string recordingId;
                    if (recordingByHotkeyId.TryGetValue(id, out recordingId))
                    {
                        if (Activity.Kind == ActivityKind.Playing && Activity.RecordingId == recordingId) Stop(); else StartPlayback(RunOrigin.Hotkey, recordingId);
                        break;
                    }
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
            catch (Exception ex) { NoteHookFault(ex); return false; }
        }

        void OnHookMouse(MouseEvent e)
        {
            try
            {
                if (e.Kind == MouseEventKind.Down) engine.Reset();
                recorder.OnMouse(e);
            }
            catch (Exception ex) { NoteHookFault(ex); }
        }

        readonly object hookFaultGate = new object();
        readonly Dictionary<string, DateTime> hookFaultLogged = new Dictionary<string, DateTime>();

        // Hook thread. A failure here must never reach Windows (it would drop the hook), but it must reach the log, or phrases and
        // recording stop working with nothing to go on. Only the kind and message are written (never typed text): each distinct one
        // the first time, then at most once a minute.
        void NoteHookFault(Exception ex)
        {
            try
            {
                string key = ex.GetType().Name + ": " + ex.Message;
                DateTime now = DateTime.UtcNow, last;
                lock (hookFaultGate)
                {
                    if (hookFaultLogged.TryGetValue(key, out last) && now - last < TimeSpan.FromMinutes(1)) return;
                    if (hookFaultLogged.Count > 50) hookFaultLogged.Clear();
                    hookFaultLogged[key] = now;
                }
                Log.Error("Handling a key or mouse event failed (" + key + ")");
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
            LastOutcomeHeading = null;
            if (kind == ActivityKind.Macro || kind == ActivityKind.Playing) engine.Reset();   // phrases ignore the keyboard while it runs
            return Activity;
        }

        void DisposeCancellation()
        {
            CancellationTokenSource old = cts;
            cts = null;
            if (old != null) old.Dispose();
        }

        void End(string outcome, bool outcomeIsError, string heading = null)
        {
            RunOrigin origin = Activity.Origin;
            Activity = new ActivityInfo();
            activityKind = 0;
            DisposeCancellation();
            LastOutcome = outcome ?? "";
            LastOutcomeIsError = outcomeIsError;
            LastOutcomeHeading = heading;
            if (quitting) return;   // the windows are closing
            if (!string.IsNullOrEmpty(outcome)) SetStatus(outcome, outcomeIsError);
            // If bringing the window back fails, the pill and the Deck must still hear that the activity ended.
            try { RestoreMain(origin); }
            catch (Exception ex) { Log.Error("Bringing Macro Kit back after a run failed", ex); }
            ActivityChanged();
        }

        // ---- administrator windows (Windows blocks input between a normal and an elevated program)

        public const string ElevationMessage = "This window runs as administrator, so Windows blocks Macro Kit's input there. Run Macro Kit as administrator to use it with that window.";

        // The same token check that tells whether the target window is elevated, so the two can never disagree.
        static bool IsSelfElevated()
        {
            try { return Elevation.IsCurrentProcessElevated; }
            catch (Exception) { return false; }
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

        // ------------------------------------------------------------------ Macro Kit Pro

        void LoadLicense()
        {
            string key = Storage.LoadLicenseKey();
            if (string.IsNullOrEmpty(key)) return;
            LicenseInfo info;
            string problem;
            if (License.TryRead(key, out info, out problem)) Edition.Set(info);
            else Log.Warn("The saved license key was not accepted: " + problem);
        }

        /// <summary>Checks and saves a key. Returns null once Pro is unlocked, or what is wrong with the key.</summary>
        public string EnterLicense(string text)
        {
            LicenseInfo info;
            string problem;
            if (!License.TryRead(text, out info, out problem)) return problem;
            try { Storage.SaveLicenseKey(info.Key); }
            catch (Exception ex)
            {
                Log.Error("Saving license.key failed", ex);
                return "The key is valid, but it couldn't be saved: " + ex.Message;
            }
            Edition.Set(info);
            ResetSchedules();
            SetStatus("Macro Kit Pro is unlocked. Thank you for supporting Macro Kit!");
            return null;
        }

        public void RemoveLicense()
        {
            try { Storage.SaveLicenseKey(null); }
            catch (Exception ex) { Log.Warn("Removing license.key failed: " + ex.Message); }
            Edition.Set(null);
            ResetSchedules();
            SetStatus("The license key was removed. Macro Kit is the free edition again.");
        }

        // ---- scheduled macros (Pro). Times are worked out in memory, so a run missed while Macro Kit was closed is not made up.

        static readonly TimeSpan ScheduleGrace = TimeSpan.FromMinutes(5);
        readonly Dictionary<string, DateTime> scheduleDue = new Dictionary<string, DateTime>();
        readonly Dictionary<string, string> scheduleSeen = new Dictionary<string, string>();

        /// <summary>Forgets the worked-out run times (the edition changed); the next tick works them out again.</summary>
        public void ResetSchedules()
        {
            scheduleDue.Clear();
            scheduleSeen.Clear();
            ScheduleTick();
        }

        /// <summary>When the macro next runs by itself, or null.</summary>
        public DateTime? NextScheduledRun(MacroDef macro)
        {
            if (macro == null || !macro.Enabled || macro.Schedule == null || macro.Schedule.Kind == ScheduleKind.Off || !Edition.IsPro) return null;
            DateTime due;
            string seen;
            bool current = scheduleSeen.TryGetValue(macro.Id, out seen) && seen == Signature(macro.Schedule);
            if (current) return scheduleDue.TryGetValue(macro.Id, out due) ? due : (DateTime?)null;
            return macro.Schedule.NextRun(DateTime.Now);   // just changed: the next tick picks this same time
        }

        static string Signature(MacroSchedule s)
        {
            return s.Kind + "|" + s.Time + "|" + s.Days + "|" + s.EveryMinutes + "|" + s.At;
        }

        void ScheduleTick()
        {
            if (disposed || !InputEnabled) return;
            DateTime now = DateTime.Now;
            var live = new HashSet<string>();
            foreach (MacroDef macro in Macros.ToArray())
            {
                MacroSchedule schedule = macro.Schedule;
                if (!Edition.IsPro || !macro.Enabled || schedule == null || schedule.Kind == ScheduleKind.Off) continue;
                live.Add(macro.Id);
                string signature = Signature(schedule);
                string seen;
                if (!scheduleSeen.TryGetValue(macro.Id, out seen) || seen != signature)
                {
                    // New or changed: the first run is the next one from now.
                    scheduleSeen[macro.Id] = signature;
                    DateTime? first = schedule.NextRun(now);
                    if (first.HasValue) scheduleDue[macro.Id] = first.Value; else scheduleDue.Remove(macro.Id);
                    continue;
                }
                DateTime due;
                if (!scheduleDue.TryGetValue(macro.Id, out due) || now < due) continue;
                ScheduleAction action = MacroSchedule.Decide(now, due, IsBusy, ScheduleGrace);
                if (action == ScheduleAction.Wait) continue;   // busy: try again on the next tick
                if (action == ScheduleAction.Skip)
                {
                    // Late after sleep, a stalled timer or a long busy spell: running now could type into whatever window is in front.
                    SetStatus("Skipped the scheduled run of \u201C" + macro.Name + "\u201D because it was more than 5 minutes late (Macro Kit was busy, or the PC was asleep).", true);
                    Log.Info("Skipped the scheduled run of a macro: more than the grace time late.");
                    NoteScheduledRun(macro, due, "skipped, it was more than 5 minutes late");
                }
                else
                {
                    Log.Info("Starting a scheduled macro (" + schedule.Describe() + ").");
                    if (!StartMacro(macro, RunOrigin.Schedule)) NoteScheduledRun(macro, now, "could not start");
                }
                DateTime? next = schedule.NextRun(due);
                if (next.HasValue && next.Value <= now) next = schedule.NextRun(now);
                if (next.HasValue) scheduleDue[macro.Id] = next.Value; else scheduleDue.Remove(macro.Id);
            }
            foreach (string id in scheduleSeen.Keys.ToArray())
            {
                if (live.Contains(id)) continue;
                scheduleSeen.Remove(id);
                scheduleDue.Remove(id);
            }
        }

        // What the schedule card shows as "Last run": saved with the macro, without redrawing anything while the person edits.
        void NoteScheduledRun(MacroDef macro, DateTime when, string result)
        {
            if (macro == null || macro.Schedule == null) return;
            macro.Schedule.NoteRun(when, result);
            MarkQuietlyDirty(SaveParts.Macros);
        }

        // ---- the input check (Settings): real keys into Macro Kit's own window, and one temporary phrase

        /// <summary>True when keys can be sent and typing watched in this session.</summary>
        public bool CanCheckInput { get { return InputEnabled && hook != null && hook.IsRunning; } }

        public void TypeForCheck(Action<IInputSink> type)
        {
            type(input);
        }

        string savedOwnProcess;
        bool phraseCheckOn;

        /// <summary>Lets one extra phrase expand inside Macro Kit's own window until EndPhraseCheck.</summary>
        public void BeginPhraseCheck(PhraseDef temporary)
        {
            if (!phraseCheckOn) savedOwnProcess = engine.OwnProcessName;
            phraseCheckOn = true;
            engine.OwnProcessName = null;
            engine.SetPhrases(Phrases.Concat(new[] { temporary }).ToList());
        }

        public void EndPhraseCheck()
        {
            if (!phraseCheckOn) return;
            phraseCheckOn = false;
            engine.OwnProcessName = savedOwnProcess;
            engine.SetPhrases(Phrases);
        }

        // ---- the macro library

        /// <summary>Raised after the library added a macro or a phrase, so its tab can show and select it.</summary>
        public event Action<MacroDef, PhraseDef> LibraryAdded = (macro, phrase) => { };

        /// <summary>Adds a copy of a library item. Returns null, or why it was not added.</summary>
        public string AddFromLibrary(LibraryItem item)
        {
            if (item == null) return "Nothing to add.";
            if (item.IsPhrase)
            {
                if (Phrases.Any(p => string.Equals(p.Trigger, item.Trigger, StringComparison.Ordinal)))
                    return "You already have a phrase for " + item.Trigger + ".";
                PhraseDef phrase = item.ToPhrase();
                Phrases.Add(phrase);
                MarkDirty(SaveParts.Phrases);
                LibraryAdded(null, phrase);
            }
            else
            {
                MacroDef macro = item.ToMacro();
                Macros.Add(macro);
                MarkDirty(SaveParts.Macros);
                LibraryAdded(macro, null);
            }
            return null;
        }

        // ---- macros

        public bool StartMacro(MacroDef macro, RunOrigin origin)
        {
            if (!CanBegin(origin)) return false;
            ParseResult parsed = MacroParser.Parse(macro.Script, macro.NewLineIsEnter);
            if (!parsed.Ok)
            {
                ParseError first = parsed.Errors[0];
                // The line as the Macros tab shows it; the parser counts lines of the stored script.
                string problem = PlainMacro.FirstProblem(macro) ?? "Line " + first.Line + ": " + first.Message;
                return Refuse("“" + macro.Name + "” has an error. " + problem, origin);
            }
            if (parsed.Steps.Count == 0) return Refuse("“" + macro.Name + "” has nothing to run yet.", origin);

            MacroDef snapshot = Json.Deserialize<MacroDef>(Json.Serialize(macro));
            ActivityInfo activity = Begin(ActivityKind.Macro, macro.Name, origin);
            activity.MacroId = macro.Id;
            activity.StepCount = parsed.Steps.Count;
            activity.TotalPasses = macro.Repeat == RepeatMode.Once ? 1 : macro.Repeat == RepeatMode.Times ? Math.Max(1, macro.RepeatTimes) : 0;
            CancellationToken token = cts.Token;
            var runner = new MacroRunner(input, windows, clock, clipboard);
            runner.Actions = systemActions;
            runner.Turns = switchTurns;
            runner.ElevationProbe = IsSelfElevated;
            // {{MACRO:name}} looks among copies taken now, so edits made while the run goes on can't change it halfway.
            List<MacroDef> others = Macros.Select(m => Json.Deserialize<MacroDef>(Json.Serialize(m))).ToList();
            runner.FindMacro = wanted => others.FirstOrDefault(m => string.Equals((m.Name ?? "").Trim(), wanted, StringComparison.OrdinalIgnoreCase));
            runner.Screen = screen;
            // Pictures are read from disk once per run, the first time a step asks for each one.
            var pictures = new Dictionary<string, PixelImage>(StringComparer.OrdinalIgnoreCase);
            runner.FindImage = wanted =>
            {
                PixelImage found;
                if (!pictures.TryGetValue(wanted, out found)) pictures[wanted] = found = Storage.LoadImage(wanted);
                return found;
            };
            runner.AskForInput = AskMacroInput;
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

        // Worker thread (the expansion engine). Shows the {fill:Name} prompt on the UI thread and waits for it, then puts the focus
        // back on the window the phrase was typed in. It posts and waits on an event, like HideForRun, so Exit never waits on it.
        IDictionary<string, string> AskPhraseFields(PhraseDef phrase, IList<string> names)
        {
            if (disposed) return null;
            IntPtr back = windows.Foreground;
            IDictionary<string, string> answers = null;
            var done = new ManualResetEventSlim(false);
            try
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!disposed) answers = Dialogs.Fill(phrase.Trigger, names, w =>
                        {
                            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                            try { windows.Activate(hwnd, 500); } catch (Exception ex) { Log.Warn("Could not bring the fill-in prompt forward: " + ex.Message); }
                        });
                    }
                    catch (Exception ex) { Log.Warn("The fill-in prompt failed: " + ex.Message); }
                    finally { done.Set(); }
                }));
            }
            catch (Exception) { return null; }   // the dispatcher is shutting down
            while (!done.Wait(250)) { if (disposed) return null; }
            if (back != IntPtr.Zero)
            {
                try { windows.Activate(back, 1000); windows.WaitUntilReady(back, 1000); }
                catch (Exception ex) { Log.Warn("Could not go back to the window after the fill-in prompt: " + ex.Message); }
            }
            return answers;
        }

        // Worker thread (a macro run). Shows the {{INPUT}} question on the UI thread and waits for it, then puts the focus back on
        // the window the macro runs in. Stop closes the question. Like AskPhraseFields it posts and waits, so Exit never waits on it.
        string AskMacroInput(string macroName, string name, string question, CancellationToken token)
        {
            if (disposed) return null;
            IntPtr back = windows.Foreground;
            string answer = null;
            System.Windows.Window shownWindow = null;
            var done = new ManualResetEventSlim(false);
            try
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!disposed && !token.IsCancellationRequested) answer = Dialogs.AskForMacro(macroName, name, question, w =>
                        {
                            shownWindow = w;
                            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                            try { windows.Activate(hwnd, 500); } catch (Exception ex) { Log.Warn("Could not bring the macro's question forward: " + ex.Message); }
                        });
                    }
                    catch (Exception ex) { Log.Warn("The macro's question failed: " + ex.Message); }
                    finally { done.Set(); }
                }));
            }
            catch (Exception) { return null; }   // the dispatcher is shutting down
            // Stop closes the question. Tried again every 100 ms until it is gone: a Stop just as the window opens finds it not
            // shown yet (shownWindow is set when it has loaded), and one try would leave it open.
            int closePosted = 0;
            while (!done.Wait(100))
            {
                if (disposed) return null;
                if (token.IsCancellationRequested && Interlocked.CompareExchange(ref closePosted, 1, 0) == 0)
                {
                    try
                    {
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            try { if (shownWindow != null && shownWindow.IsVisible) shownWindow.Close(); }
                            finally { Interlocked.Exchange(ref closePosted, 0); }
                        }));
                    }
                    catch (Exception) { return null; }
                }
            }
            if (answer != null && back != IntPtr.Zero && !token.IsCancellationRequested)
            {
                try { windows.Activate(back, 1000); windows.WaitUntilReady(back, 1000); }
                catch (Exception ex) { Log.Warn("Could not go back to the window after the macro's question: " + ex.Message); }
            }
            return answer;
        }

        // ---- recordings

        public bool StartRecording(RunOrigin origin)
        {
            if (!CanBegin(origin)) return false;
            // Without the watcher a take would end with "Nothing was recorded." and no hint why.
            if (!HookRunning) return Refuse("Macro Kit's keyboard and mouse watcher did not start, so it can't record. Restart Macro Kit.", origin);
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
            recorder.Start(Settings.RecordMouseMoves, KeysHeldNow());
            ActivityChanged();
            return true;
        }

        // The keys held down right now (the record hotkey, usually), as the hook reports them: left/right modifier forms included.
        // Nothing is being sent at this moment, so GetAsyncKeyState is the physical keyboard. 0x01-0x06 are mouse buttons.
        List<ushort> KeysHeldNow()
        {
            var held = new List<ushort>();
            try
            {
                for (ushort vk = 0x08; vk <= 0xFE; vk++) if (input.IsPhysicallyDown(vk)) held.Add(vk);
            }
            catch (Exception ex) { Log.Warn("Could not read which keys are held: " + ex.Message); }
            return held;
        }

        static Hotkey? ParsedHotkey(string text)
        {
            Hotkey hk;
            return Hotkey.TryParse(text, out hk) && !hk.IsEmpty ? hk : (Hotkey?)null;
        }

        /// <summary>
        /// Ends the recording. <paramref name="stoppedBy"/> is the hotkey that stopped it, whose press at the end is then removed;
        /// leave it out for the Stop buttons (recorder bar, Deck, pill, tray), so the person's own last keys are all kept.
        /// </summary>
        public void StopRecording(Hotkey? stoppedBy = null)
        {
            if (Activity.Kind != ActivityKind.Recording) return;
            Rec recording = recorder.Stop(stoppedBy.HasValue ? new[] { stoppedBy.Value } : new Hotkey[0]);
            if (recording.Events.Count == 0)
            {
                End(recorder.InjectedIgnored > 0
                    ? "Nothing was recorded. Macro Kit records only your own keyboard and mouse, not input from remote-control tools, the on-screen keyboard or key remappers."
                    : "Nothing was recorded.", false, "Stopped");
                return;
            }
            Pending = recording;
            Settings.SelectedRecordingId = recording.Id;
            MarkDirty(SaveParts.Settings);
            int eventCount = recording.Events.Count;
            string note = eventCount + (eventCount == 1 ? " event, " : " events, ") + FormatDuration(recording.DurationMs);
            if (Settings.AutoSaveRecordings && SavePending()) End("Recording saved as “" + recording.Name + "” (" + note + ").", false, "Recorded");
            else End("Recorded " + note + ". Press Save to keep it. Recording again from this window asks before replacing it; a hotkey or the Deck saves it first.", false, "Recorded");
            RecordingsChanged();
        }

        /// <summary>The recorder bar's Restart: throws the current take away and starts recording again at once.</summary>
        public void RestartRecording()
        {
            if (Activity.Kind != ActivityKind.Recording) return;
            recorder.Stop(new Hotkey[0]);
            Activity.Started = DateTime.Now;
            recorder.Start(Settings.RecordMouseMoves, KeysHeldNow());
            SetStatus("Recording started again.", false);
            ActivityChanged();
        }

        /// <summary>
        /// Clicks on Macro Kit's tray icon while recording (the right-click that opened its menu, the double-click that opens the
        /// window) are Macro Kit's, not part of the task: takes the last <paramref name="clicks"/> of them back out of the take.
        /// </summary>
        public void ForgetTrayClicks(Core.MouseButton button, int clicks)
        {
            if (Activity.Kind != ActivityKind.Recording) return;
            for (int i = 0; i < clicks; i++) if (!recorder.DropTrailingClick(button)) break;
        }

        /// <summary>The recorder bar's Discard: stops and throws the take away, keeping any earlier unsaved recording.</summary>
        public void DiscardRecording()
        {
            if (Activity.Kind != ActivityKind.Recording) return;
            recorder.Stop(new Hotkey[0]);
            End("Recording discarded.", false, "Stopped");
        }

        /// <summary>True while the main window is hidden because something is running (it comes back when that ends).</summary>
        public bool MainHiddenForActivity { get { return mainHiddenByActivity; } }

        /// <summary>Plays the recording selected on the Recorder tab.</summary>
        public bool StartPlayback(RunOrigin origin) { return StartPlayback(origin, null); }

        /// <summary>Plays a recording (the selected one when <paramref name="recordingId"/> is null) at its own speed and repeat.</summary>
        public bool StartPlayback(RunOrigin origin, string recordingId)
        {
            if (!CanBegin(origin)) return false;
            Rec recording = recordingId == null ? SelectedRecording() : LoadRecording(recordingId);
            if (recording == null) return Refuse(recordingId == null ? "Pick a recording to play first." : "That recording is gone. It may have been deleted.", origin);
            if (recording.Events.Count == 0) return Refuse("This recording is empty, so there is nothing to play.", origin);
            PlaybackSettings settings = PlaybackFor(recording.Id).Copy();
            ActivityInfo activity = Begin(ActivityKind.Playing, recording.Name, origin);
            activity.RecordingId = recording.Id;
            activity.Speed = settings.Speed;
            activity.TotalPasses = settings.Repeat == RepeatMode.Once ? 1 : settings.Repeat == RepeatMode.Times ? Math.Max(1, settings.RepeatTimes) : 0;
            activity.StepCount = recording.Events.Count;
            CancellationToken token = cts.Token;
            WarnIfTargetIsElevated(activity);
            GetOutOfTheWay(true);
            var player = new Player(input, clock, windows);
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
            // 1 ms timer ticks while it plays, so mouse paths and quick clicks keep their recorded timing (the default tick is 15.6 ms).
            StartWorker("playback", () => { using (TimerResolution.Raise()) return player.Play(recording, settings, token, fromHotkey); },
                result => FinishRun(result, "“" + recording.Name + "”", "finished playing"));
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
            ActivityInfo ran = Activity;
            if (ran.Kind == ActivityKind.Macro && ran.Origin == RunOrigin.Schedule)
                NoteScheduledRun(Macros.FirstOrDefault(m => m.Id == ran.MacroId), ran.Started,
                    !string.IsNullOrEmpty(result.Error) ? "stopped by a problem" : result.Cancelled ? "stopped" : "finished");
            if (!string.IsNullOrEmpty(result.Error)) End(result.Error, true);
            else if (result.Cancelled) End("Stopped " + what + ".", false, "Stopped");
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
            if (quitting) return;
            bool wasHidden = mainHiddenByActivity;
            mainHiddenByActivity = false;
            // Whatever goes wrong while the window comes back and the windows update, the person's pick is still handed on.
            try { if (wasHidden) ShowMainWindow(true); }   // the person is working in Macro Kit, so it comes back in front (an explicit open)
            catch (Exception ex) { Log.Error("Bringing Macro Kit back after picking a window failed", ex); }
            try { ActivityChanged(); }
            catch (Exception ex) { Log.Error("Updating the windows after picking a window failed", ex); }
            if (picked != null && done != null) done(picked);
            else SetStatus(cancelled ? "Picking cancelled." : "", false);
        }

        // ------------------------------------------------------------------ recordings storage

        public void RefreshRecordings()
        {
            lastLoaded = null;   // saved, renamed, deleted or imported: read it from disk again next time
            Recordings = Storage.LoadRecordingIndex();
            if (InputEnabled && hook.IsRunning && HotkeySignature() != hotkeySignature) ApplyHotkeys();
            RecordingsChanged();
            ShowNewStorageWarnings();
        }

        /// <summary>A saved recording, or the unsaved one; null when it is gone.</summary>
        public Rec LoadRecording(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Pending != null && Pending.Id == id) return Pending;
            return LoadSaved(id);
        }

        // A recording found damaged (set aside, or put back from an earlier saved copy) or in use after startup is reported too. Posted, so it
        // comes after whatever the current action says ("That recording is gone", for example) instead of being replaced by it.
        void ShowNewStorageWarnings()
        {
            IList<string> all = Storage.Warnings;
            if (all.Count <= storageWarningsShown) return;
            string text = string.Join(" ", all.Skip(storageWarningsShown));
            storageWarningsShown = all.Count;
            Post(() => SetStatus(text, true));
        }

        Rec lastLoaded;

        // The last saved recording read from disk is kept (one only), so playing it again, by hotkey too, skips reading and parsing
        // the file, which takes a moment for a long recording. RefreshRecordings forgets it whenever a recording file changes.
        Rec LoadSaved(string id)
        {
            Rec cached = lastLoaded;
            if (cached != null && cached.Id == id) return cached;
            Rec saved = Storage.LoadRecording(id);
            ShowNewStorageWarnings();
            lastLoaded = saved;
            return saved;
        }

        bool RecordingExists(string id)
        {
            return !string.IsNullOrEmpty(id) && ((Pending != null && Pending.Id == id) || (Recordings != null && Recordings.Any(r => r.Id == id)));
        }

        public string RecordingName(string id)
        {
            if (Pending != null && Pending.Id == id) return Pending.Name;
            RecordingInfo info = Recordings == null ? null : Recordings.FirstOrDefault(r => r.Id == id);
            return info == null ? "" : info.Name;
        }

        // ---- each recording's own hotkey, Deck button and playback

        /// <summary>The key a recording's hotkey problem is kept under in <see cref="HotkeyErrors"/>.</summary>
        public static string RecordingHotkeyKey(string id) { return "rec:" + id; }

        public RecordingOptions OptionsFor(string id)
        {
            return string.IsNullOrEmpty(id) ? null : Settings.RecordingSettings.FirstOrDefault(o => o.Id == id);
        }

        /// <summary>The recording's own options, made from the current defaults the first time one of them is changed.</summary>
        public RecordingOptions EnsureOptions(string id)
        {
            RecordingOptions o = OptionsFor(id);
            if (o != null) return o;
            o = new RecordingOptions { Id = id, Playback = Settings.Playback.Copy() };
            Settings.RecordingSettings.Add(o);
            return o;
        }

        /// <summary>How a recording plays: its own speed and repeat, or the defaults until it has its own.</summary>
        public PlaybackSettings PlaybackFor(string id)
        {
            RecordingOptions o = OptionsFor(id);
            return o != null && o.Playback != null ? o.Playback : Settings.Playback;
        }

        /// <summary>Changes how a recording plays; the change also becomes the default for recordings that have no settings yet.</summary>
        public void EditPlayback(string id, Action<PlaybackSettings> change)
        {
            change(Settings.Playback);
            if (!string.IsNullOrEmpty(id)) change(EnsureOptions(id).Playback);
            MarkDirty(SaveParts.Settings);
        }

        public void SetRecordingHotkey(string id, string hotkey)
        {
            if (string.IsNullOrEmpty(id)) return;
            EnsureOptions(id).Hotkey = hotkey ?? "";
            MarkDirty(SaveParts.Settings);
        }

        public void SetRecordingOnDeck(string id, bool show)
        {
            if (string.IsNullOrEmpty(id)) return;
            EnsureOptions(id).ShowOnDeck = show;
            MarkDirty(SaveParts.Settings);
        }

        /// <summary>The recordings that have a Deck button, in the order they were added to it.</summary>
        public List<RecordingOptions> DeckRecordings()
        {
            return Settings.RecordingSettings.Where(o => o.ShowOnDeck && RecordingExists(o.Id)).ToList();
        }

        /// <summary>Turns a recording into a new macro (clicks, typing, keys and pauses) and returns it; null when there is nothing in it.</summary>
        public MacroDef ConvertRecordingToMacro(string id)
        {
            Rec recording = LoadRecording(id);
            if (recording == null) { SetStatus("Pick a recording to convert first.", true); return null; }
            PlaybackSettings playback = PlaybackFor(recording.Id);
            MacroDef macro = RecordingToMacro.Convert(recording, playback.Speed, playback.FollowWindows);
            if (macro.Script.Length == 0) { SetStatus("“" + recording.Name + "” has no clicks or keys, so there is nothing to turn into a macro.", true); return null; }
            string name = macro.Name;
            for (int n = 2; Macros.Any(m => string.Equals(m.Name, macro.Name, StringComparison.OrdinalIgnoreCase)); n++) macro.Name = name + " " + n;
            Macros.Add(macro);
            MarkDirty(SaveParts.Macros);
            LibraryAdded(macro, null);
            SetStatus("Made the macro “" + macro.Name + "” from the recording. Edit it on the Macros tab.", false);
            return macro;
        }

        /// <summary>The recording to play: the unsaved one when it is selected, otherwise the selected saved one.</summary>
        public Rec SelectedRecording()
        {
            string id = Settings.SelectedRecordingId;
            if (Pending != null && (id == Pending.Id || string.IsNullOrEmpty(id))) return Pending;
            if (!string.IsNullOrEmpty(id)) { Rec saved = LoadSaved(id); if (saved != null) return saved; }
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

        public bool DeleteRecording(string id)
        {
            try
            {
                if (Pending != null && Pending.Id == id) Pending = null;
                else Storage.DeleteRecording(id);
                if (Settings.SelectedRecordingId == id) Settings.SelectedRecordingId = "";
                Settings.RecordingSettings.RemoveAll(o => o.Id == id);
                MarkDirty(SaveParts.Settings);
                RefreshRecordings();
                return true;
            }
            catch (Exception ex) { SetStatus("Could not delete the recording: " + ex.Message, true); return false; }
        }

        /// <summary>A deleted recording kept in memory, with its own hotkey, Deck button and playback, so Undo can put it back.</summary>
        public sealed class RemovedRecording
        {
            public Rec Recording;
            public RecordingOptions Options;
            public bool WasPending;
        }

        /// <summary>Deletes a recording and returns what Undo needs to put it back; null when it is gone or could not be deleted.</summary>
        public RemovedRecording RemoveRecording(string id)
        {
            Rec recording = LoadRecording(id);
            if (recording == null) { SetStatus("That recording is gone.", true); return null; }
            var removed = new RemovedRecording { Recording = recording, Options = OptionsFor(id), WasPending = Pending != null && Pending.Id == id };
            return DeleteRecording(id) ? removed : null;
        }

        /// <summary>Puts a deleted recording back under its own id, with its hotkey, Deck button and playback, and selects it.</summary>
        public bool RestoreRecording(RemovedRecording removed)
        {
            if (removed == null || removed.Recording == null) return false;
            try
            {
                // An unsaved take comes back unsaved, unless a newer take has taken its place meanwhile: then it is saved.
                if (removed.WasPending && Pending == null) Pending = removed.Recording;
                else Storage.SaveRecording(removed.Recording);
                if (removed.Options != null && OptionsFor(removed.Recording.Id) == null) Settings.RecordingSettings.Add(removed.Options);
                Settings.SelectedRecordingId = removed.Recording.Id;
                RefreshRecordings();               // listed again first, so its hotkey counts when the settings change below
                MarkDirty(SaveParts.Settings);
                return true;
            }
            catch (Exception ex) { SetStatus("Could not put the recording back: " + ex.Message, true); return false; }
        }

        /// <summary>
        /// Saves an edited copy of a recording (trimmed, pauses shortened, or put back by Undo) under its own id. An unsaved take
        /// stays unsaved. Not while recording or playing. Returns false, with the reason in the status line, when it could not.
        /// </summary>
        public bool ReplaceRecording(Rec edited, string done)
        {
            if (edited == null) return false;
            if (IsBusy) { SetStatus("Stop the recording or playback first.", true); return false; }
            if (!RecordingExists(edited.Id)) { SetStatus("That recording is gone.", true); return false; }
            try
            {
                if (Pending != null && Pending.Id == edited.Id) Pending = edited;
                else Storage.SaveRecording(edited);
                RefreshRecordings();
                if (!string.IsNullOrEmpty(done)) SetStatus(done, false);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Saving an edited recording failed", ex);
                SetStatus("Could not save the recording: " + ex.Message, true);
                return false;
            }
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
            public void TypeTextPaced(string text, Action betweenBatches) { throw Off(); }
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
