using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AppShared;
using MacroKit.Core;

namespace MacroKit.App
{
    /// <summary>
    /// Ties the windows to the controller: the main window, the Deck, the run pill and the tray icon, plus the app-level actions
    /// (restart, export / import, changelog, exit).
    /// </summary>
    public sealed class AppShell
    {
        readonly AppController c;
        readonly bool wantTray;
        TrayIcon tray;
        DeckWindow deck;
        RunPill pill;
        RecorderBar recBar;
        readonly HashSet<Window> closedWindows = new HashSet<Window>();
        bool exitInProgress;

        public MainWindow Main { get; private set; }
        public bool Exiting { get; private set; }
        public bool TrayAvailable { get { return tray != null; } }

        public AppShell(AppController controller, bool useTray)
        {
            c = controller;
            wantTray = useTray;
            Main = new MainWindow(c, this);
            pill = new RunPill(c);
            recBar = new RecorderBar(c);
            deck = new DeckWindow(c);
            c.IsMainWindowVisible = () => Main.IsVisible;
            c.IsMainWindowMinimized = () => Main.WindowState == WindowState.Minimized;
            c.HideMainWindow = () => Main.Hide();
            c.ShowMainWindow = activate => Main.Reveal(activate);
            c.RestoreMainWindow = (minimized, behind) => Main.RestoreQuietly(minimized, behind);
            c.Flash += (title, message, error) => pill.Flash(title, message, error ? "Bad" : "Warn");
            HotkeyBox.CaptureStarted = c.SuspendHotkeys;
            HotkeyBox.CaptureEnded = c.ResumeHotkeys;
            KeyRecorderWindow.UseKeyboardHook = c.InputEnabled;
            KeyRecorderWindow.LoadDelays = () => c.Settings.RecordKeyDelays;
            KeyRecorderWindow.SaveDelays = kind => { c.Settings.RecordKeyDelays = kind; c.MarkDirty(SaveParts.Settings); };
            foreach (Window w in new Window[] { Main, deck, pill, recBar })
            {
                Window window = w;
                window.SourceInitialized += (s, e) => RefreshOwnWindows();
                window.Closed += (s, e) => { closedWindows.Add(window); RefreshOwnWindows(); };
            }
            c.Confirm = message => Ui.Confirm(Main, message);
            c.ActivityChanged += () => { recBar.Present(MainPixelsIfHiddenForRecording()); pill.Present(); deck.Update(); };
            c.ActivityProgressed += () => pill.Refresh();
            c.DeckVisibilityChanged += ApplyDeck;
            c.RecordingsChanged += () => deck.Update();
            c.Edited += parts => { if ((parts & (SaveParts.Macros | SaveParts.Settings)) != 0) deck.Update(); };
        }

        /// <summary>Tells the input hook which windows are Macro Kit's own, so clicks and wheel turns over them are never recorded.</summary>
        void RefreshOwnWindows()
        {
            var handles = new List<IntPtr>();
            foreach (Window w in new Window[] { Main, deck, pill, recBar })
            {
                if (w == null || closedWindows.Contains(w)) continue;
                IntPtr handle = new WindowInteropHelper(w).Handle;
                if (handle != IntPtr.Zero) handles.Add(handle);
            }
            handles.AddRange(extraOwnWindows);
            c.SetOwnWindows(handles.ToArray());
        }

        readonly List<IntPtr> extraOwnWindows = new List<IntPtr>();

        /// <summary>
        /// Counts a window that is not a WPF window (the tray icon's menu) as Macro Kit's own while it is open, so clicks on it are
        /// left out of a recording like clicks on the recorder bar. Call <see cref="RemoveOwnWindow"/> when it closes.
        /// </summary>
        public void AddOwnWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero || extraOwnWindows.Contains(handle)) return;
            extraOwnWindows.Add(handle);
            RefreshOwnWindows();
        }

        public void RemoveOwnWindow(IntPtr handle)
        {
            if (extraOwnWindows.Remove(handle)) RefreshOwnWindows();
        }

        public DeckWindow Deck { get { return deck; } }
        public RunPill Pill { get { return pill; } }
        public RecorderBar RecorderBar { get { return recBar; } }

        /// <summary>Where the main window was, when a recording just hid it (the recorder bar takes its place); empty otherwise.</summary>
        Rect MainPixelsIfHiddenForRecording()
        {
            Rect r;
            if (c.Activity.Kind != ActivityKind.Recording || !c.MainHiddenForActivity || Main.WindowState != WindowState.Normal || !NativeUi.TryGetRect(Main, out r)) return Rect.Empty;
            return r;
        }

        /// <summary>Starts the tray icon and shows the windows that should be visible.</summary>
        public void Start(bool startHidden)
        {
            if (wantTray)
            {
                try { tray = new TrayIcon(c, this); }
                catch (Exception ex) { Log.Warn("The tray icon could not be created", ex); }
            }
            c.TrayAvailable = tray != null;
            ApplyDeck();
            if (!(startHidden && tray != null)) Main.Reveal(true);
            IList<string> warnings = c.Storage.Warnings;
            if (warnings.Count > 0) c.SetStatus(StartupStatus(warnings, c.LastStatus, c.LastStatusIsError), true);
        }

        /// <summary>
        /// The status line at startup when there are storage warnings. Each warning is a whole sentence: a damaged file set aside
        /// (and restored or replaced) or a file that was in use. An error already showing (hotkeys that could not be set up) is kept after them.
        /// </summary>
        public static string StartupStatus(IList<string> warnings, string current, bool currentIsError)
        {
            string text = string.Join(" ", warnings);
            if (currentIsError && !string.IsNullOrEmpty(current) && !text.Contains(current)) text += " " + current;
            return text;
        }

        void ApplyDeck()
        {
            if (c.Settings.DeckVisible)
            {
                deck.Place();
                if (!deck.IsVisible) deck.Show();
            }
            else deck.Hide();
        }

        /// <summary>
        /// Opens (or brings forward) the main window: tray double-click, tray menu, a second launch. A playback is stopped first:
        /// the window takes the focus, so the rest of the recording would go into Macro Kit (a macro's focus guard stops it the same way).
        /// </summary>
        public void OpenMain()
        {
            if (c.Activity.Kind == ActivityKind.Playing) c.Stop();
            Main.Reveal(true);
        }

        public void ShowHowTo()
        {
            HowToWindow.Show(Main, "start");
        }

        public void ShowChangelog()
        {
            new ChangelogWindow(Main).ShowDialog();
        }

        public void OpenDataFolder()
        {
            try
            {
                Directory.CreateDirectory(c.Storage.DataRoot);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + c.Storage.DataRoot + "\"") { UseShellExecute = false });
            }
            catch (Exception ex) { c.SetStatus("Could not open the data folder: " + ex.Message, true); }
        }

        // ------------------------------------------------------------------ leaving

        /// <summary>Saves everything, including a recording that was not saved yet, so leaving never loses work. False when something could not be saved.</summary>
        bool SaveAll()
        {
            if (c.Pending != null) c.SavePending();
            bool flushed = c.Flush();
            return flushed && c.Pending == null;
        }

        /// <summary>Windows is signing out or shutting down: everything is saved, an unsaved recording too, and nothing is asked.</summary>
        public void SaveForSessionEnd()
        {
            if (!SaveAll()) Log.Warn("Not everything could be saved at sign-out.");
            IList<string> blocked = c.Storage.UnsavedBlockedFiles;
            if (blocked.Count > 0) Log.Warn("Changes to files that were in use at startup were not saved at sign-out: " + string.Join(", ", blocked));
        }

        string RefuseWhileBusy(string what)
        {
            string busy = c.BusyReason();
            if (busy == null) return null;
            string message = "Can't " + what + " yet. " + busy;
            c.SetStatus(message, true);
            return message;
        }

        public void Restart()
        {
            if (!ReadyToRestart()) return;
            string blocked = BlockedChangesQuestion(c.Storage.UnsavedBlockedFiles, "Restart");
            if (blocked != null && c.InputEnabled)
            {
                if (!Ui.Confirm(Main, blocked)) return;
                if (!ReadyToRestart()) return;   // a run or recording may have started while the question was open
            }
            var before = new Dictionary<string, string>();
            try
            {
                // The new copy is started with the options the shared restart handoff does not carry: it inherits these
                // variables (Program reads them and clears the start-hidden one).
                PassToChild(before, "MK_DATA_ROOT", Paths.IsOverridden ? Paths.DataRoot : null);
                PassToChild(before, "MK_NO_INPUT", c.InputEnabled ? null : "1");
                PassToChild(before, "MK_START_HIDDEN", Main.IsVisible ? null : "1");
                using (Process child = AppRestart.Prepare()) { Quit(); }
            }
            catch (Exception ex)
            {
                Log.Error("Restart failed", ex);
                c.SetStatus("Restart did not work: " + ex.Message, true);
                Ui.Info(Main, "Macro Kit could not restart.\n\n" + ex.Message);
            }
            finally
            {
                foreach (var pair in before) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        // Nothing is running and every change is saved; otherwise says why and returns false.
        bool ReadyToRestart()
        {
            string refusal = RefuseWhileBusy("restart");
            if (refusal != null) { Ui.Info(Main, refusal); return false; }
            if (!SaveAll())
            {
                Ui.Info(Main, "Macro Kit can't restart until your latest changes are saved. Check the message at the bottom of the window.");
                return false;
            }
            return true;
        }

        // Sets an environment variable for the replacement process (null leaves it as it is) and remembers the old value.
        static void PassToChild(Dictionary<string, string> before, string name, string value)
        {
            if (value == null) return;
            before[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void ExportImport()
        {
            string refusal = RefuseWhileBusy("open Export / import");
            if (refusal != null) { Ui.Info(Main, refusal); return; }
            if (!Ui.Confirm(Main, "Export / import configuration opens in its own window, and Macro Kit closes while it runs (phrases and hotkeys stop until you start it again).\n\nYour settings, phrases, macros and recordings are saved first. Continue?")) return;
            try
            {
                string folder = AppDomain.CurrentDomain.BaseDirectory;
                string helper = ConfigurationTransfer.Find(folder, true);
                if (helper == null) throw new IOException("Configuration Transfer is missing. Restore the helper from Macro Kit's package.");
                if (!SaveAll()) throw new IOException("Your latest changes could not be saved, so the export would be out of date.");
                ConfigurationTransfer.Start(helper, folder, Paths.DataRoot, true);
                Quit();
            }
            catch (Exception ex)
            {
                Log.Error("Export / import failed", ex);
                c.SetStatus("Export / import could not start: " + ex.Message, true);
                Ui.Info(Main, ex.Message);
            }
        }

        /// <summary>
        /// Exit from the tray, the ⋯ menu or the window when the tray is off: stop, save, leave. When something could not be saved
        /// the person decides; No keeps Macro Kit open. Never shows a question in a session without input (tests).
        /// </summary>
        public void Exit()
        {
            if (Exiting || exitInProgress) return;
            exitInProgress = true;
            try
            {
                c.Stop();
                if (!SaveAll() && c.InputEnabled && !Ui.Confirm(Main, "Your latest changes could not be saved. Exit anyway?")) return;
                string blocked = BlockedChangesQuestion(c.Storage.UnsavedBlockedFiles);
                if (blocked != null && c.InputEnabled && !Ui.Confirm(Main, blocked)) return;
                Quit();
            }
            finally { exitInProgress = false; }
        }

        /// <summary>
        /// The question Exit and Restart ask when changes went to files that were in use at startup (so they were never saved over);
        /// null when there were none. Restart asks too: the new copy reads those files from disk, so the changes would be lost.
        /// </summary>
        public static string BlockedChangesQuestion(IList<string> files, string action = "Exit")
        {
            if (files == null || files.Count == 0) return null;
            string names = files.Count == 1 ? files[0] : string.Join(", ", files.Take(files.Count - 1)) + " and " + files[files.Count - 1];
            return "Changes to " + names + " made this session can't be saved because " + (files.Count == 1 ? "the file was" : "the files were")
                + " in use when Macro Kit started. " + action + " anyway?";
        }

        // Always ends the application, even if closing one of the windows throws. The main window is closed by Shutdown itself:
        // calling Close on it here would fail whenever Quit runs from inside its own Closing event.
        void Quit()
        {
            if (Exiting) return;
            Exiting = true;
            c.BeginQuit();   // anything still running is cancelled, and its end no longer touches the windows being closed
            try
            {
                if (tray != null) { tray.Dispose(); tray = null; }
                deck.Close();
                pill.Close();
                recBar.Close();
            }
            catch (Exception ex) { Log.Warn("Closing a window while leaving failed: " + ex.Message); }
            finally { Application.Current.Shutdown(); }
        }
    }
}
