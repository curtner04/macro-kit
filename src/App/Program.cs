using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using AppShared;
using MacroKit.Core;

namespace MacroKit.App
{
    /// <summary>Command-line options. Unknown options are ignored.</summary>
    public sealed class Options
    {
        public bool NoInput;
        public bool StartInTray;
        public string DataFolder;
        public string SelfTestFolder;
        public bool FromRestart;

        public static Options Parse(string[] args)
        {
            var o = new Options();
            o.FromRestart = args.Length == 3 && args[0] == AppRestart.Argument;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string next = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null;
                if (a == "--no-input") o.NoInput = true;
                else if (a == "--tray") o.StartInTray = true;
                else if (a == "--data" && next != null) { o.DataFolder = next; i++; }
                else if (a == "--self-test-ui" && next != null) { o.SelfTestFolder = next; i++; }
            }
            // A test run on the hidden desktop must never send or watch real input, whatever else was passed.
            if (Environment.GetEnvironmentVariable("CLAUDE_HIDDEN_DESKTOP") == "1") o.NoInput = true;
            if (o.SelfTestFolder != null) o.NoInput = true;
            // Set by Restart so the new copy keeps the options of the old one (the restart handoff only carries its own arguments).
            if (Environment.GetEnvironmentVariable("MK_NO_INPUT") == "1") o.NoInput = true;
            if (Environment.GetEnvironmentVariable("MK_START_HIDDEN") == "1") o.StartInTray = true;
            return o;
        }
    }

    public static class Program
    {
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int state);
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int handle);
        [DllImport("kernel32.dll")] static extern bool SetStdHandle(int handle, IntPtr value);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--version") { WriteOut(Release.Version); return 0; }
                if (args.Length > 0 && args[0] == "--changelog") { WriteOut(ReleaseNotes.Read()); return 0; }

                Options options = Options.Parse(args);
                // One-shot: only the restart that set it should start hidden, not later launches that inherit this environment.
                Environment.SetEnvironmentVariable("MK_START_HIDDEN", null);
                if (options.DataFolder != null) Paths.Override(Path.GetFullPath(options.DataFolder));
                else if (options.NoInput && !Paths.IsOverridden) Paths.Override(FreshTestFolder());
                if (options.SelfTestFolder != null) return SelfTest.Run(Path.GetFullPath(options.SelfTestFolder));

                if (options.FromRestart)
                {
                    // The old instance is closing: wait for it to finish before taking the instance mutex.
                    try { AppRestart.WaitForParent(args[1], args[2]); }
                    catch (ArgumentException) { /* the old process is already gone */ }
                    catch (InvalidOperationException ex) { Log.Warn("Restart handoff: " + ex.Message); }
                }

                // Sessions without input (tests) use their own names, so they can never signal or block the real Macro Kit.
                string suffix = options.NoInput ? ".NoInput" : "";
                string mutexName = @"Local\MacroKit.App" + suffix;
                string showName = @"Local\MacroKit.App.Show" + suffix;
                using (var mutex = new Mutex(false, mutexName))
                {
                    if (!SingleInstance.TryAcquire(mutex, options.FromRestart ? 10000 : 0)) { TellAlreadyRunning(showName); return 0; }
                    try { return RunApp(options, showName); }
                    finally { try { mutex.ReleaseMutex(); } catch (Exception) { } }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not start", ex);
                if (Environment.GetEnvironmentVariable("CLAUDE_HIDDEN_DESKTOP") != "1")
                    MessageBox.Show(ex.Message, "Macro Kit could not start", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        static int RunApp(Options options, string showName)
        {
            if (!options.NoInput && SingleInstance.TransferWindowOpen())
                throw new IOException("Finish and close Export / import configuration before opening Macro Kit.");

            Log.Info("Macro Kit " + Release.Version + " starting" + (options.NoInput ? " (input off)" : ""));
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            bool showingError = false;
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log.Error("Unhandled UI error", e.Exception);
                e.Handled = true;
                if (showingError || options.NoInput) return;
                showingError = true;
                try { MessageBox.Show("Something unexpected went wrong:\n\n" + e.Exception.Message + "\n\nDetails were saved to " + Log.FilePath, "Macro Kit", MessageBoxButton.OK, MessageBoxImage.Warning); }
                finally { showingError = false; }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("Unhandled error", e.ExceptionObject as Exception);

            var controller = new AppController(app.Dispatcher, new Storage(), !options.NoInput);
            Theme.Initialize(app, controller.Settings.Theme);
            var shell = new AppShell(controller, !options.NoInput);
            controller.Start();
            app.Dispatcher.BeginInvoke(new Action(() => shell.Start(options.StartInTray)));

            EventWaitHandle showRequest = null;
            RegisteredWaitHandle showWait = null;
            try
            {
                // A second launch sets this event so the running window comes forward.
                var security = new EventWaitHandleSecurity();
                using (WindowsIdentity me = WindowsIdentity.GetCurrent())
                    security.AddAccessRule(new EventWaitHandleAccessRule(me.User, EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize, AccessControlType.Allow));
                bool created;
                showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, showName, out created, security);
                showWait = ThreadPool.RegisterWaitForSingleObject(showRequest, (state, timedOut) => app.Dispatcher.BeginInvoke(new Action(shell.OpenMain)), null, Timeout.Infinite, false);
            }
            catch (Exception ex) { Log.Warn("Single-instance signal unavailable: " + ex.Message); }
            try { return app.Run(); }
            finally
            {
                if (showWait != null) showWait.Unregister(null);
                if (showRequest != null) showRequest.Dispose();
                controller.Dispose();
            }
        }

        /// <summary>
        /// A session without input (--no-input, the hidden desktop, tests) that was given no data folder gets a fresh temporary one
        /// (and so its own log), never the real %LOCALAPPDATA%\Macro Kit data.
        /// </summary>
        public static string FreshTestFolder()
        {
            return Path.Combine(Path.GetTempPath(), "MacroKit-noinput", "run-" + Guid.NewGuid().ToString("N"), "data");
        }

        static void TellAlreadyRunning(string showName)
        {
            try
            {
                using (var request = EventWaitHandle.OpenExisting(showName)) { request.Set(); return; }
            }
            catch (Exception) { /* an older copy without the signal: fall back to its window */ }
            foreach (var p in Process.GetProcessesByName("Macro Kit"))
                using (p)
                    if (p.Id != Process.GetCurrentProcess().Id && p.MainWindowHandle != IntPtr.Zero) { ShowWindow(p.MainWindowHandle, 9); SetForegroundWindow(p.MainWindowHandle); return; }
        }

        /// <summary>
        /// This is a GUI-subsystem exe, so --version and --changelog attach to the parent terminal's console
        /// (unless the output is already redirected to a file or pipe) to print where the user can see it.
        /// </summary>
        public static void WriteOut(string text)
        {
            IntPtr handle = GetStdHandle(-11);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                if (AttachConsole(-1))
                {
                    // GENERIC_WRITE, FILE_SHARE_WRITE, OPEN_EXISTING
                    IntPtr console = CreateFile("CONOUT$", 0x40000000, 2, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (console != new IntPtr(-1)) SetStdHandle(-11, console);
                }
            }
            Console.Out.WriteLine(text);
            Console.Out.Flush();
        }
    }
}
