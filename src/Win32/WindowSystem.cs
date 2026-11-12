using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using MacroKit.Core;

namespace MacroKit.Win32
{
    /// <summary>What the Alt+Tab filter needs to know about a window. Plain data, so the filter can be tested on fake windows.</summary>
    internal sealed class WindowFacts
    {
        public IntPtr Handle;
        public bool Visible;
        public string Title;
        public bool Cloaked;
        public bool HasOwner;
        public long ExStyle;
        public string ClassName;
        public bool OwnProcess;
    }

    /// <summary>The "would Alt+Tab list this window" rule, as a pure predicate.</summary>
    internal static class WindowFilter
    {
        // Shell surfaces that are visible, titled in some cases, and never in Alt+Tab (including the Windows 11 task switcher itself).
        static readonly HashSet<string> shellClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW",
            "XamlExplorerHostIslandWindow", "MultitaskingViewFrame", "ForegroundStaging",
        };

        public static bool IsSwitchable(WindowFacts f)
        {
            if (f == null || f.OwnProcess || !f.Visible || string.IsNullOrEmpty(f.Title)) return false;
            // Suspended Store apps and windows on other virtual desktops stay "visible" but are cloaked by the compositor.
            if (f.Cloaked) return false;
            if (f.ClassName != null && shellClasses.Contains(f.ClassName)) return false;
            bool appWindow = (f.ExStyle & Native.WS_EX_APPWINDOW) != 0;
            if (appWindow) return true;
            if ((f.ExStyle & Native.WS_EX_TOOLWINDOW) != 0) return false;
            if ((f.ExStyle & Native.WS_EX_NOACTIVATE) != 0) return false;   // floating palettes such as our Deck
            return !f.HasOwner;   // owned dialogs ride along with their owner's entry
        }
    }

    /// <summary>
    /// The real window layer: what is on screen, who owns it, and bringing a window to the front.
    /// Give it the InputHook to follow focus changes as they happen; without one it still works, it just notices a focus
    /// change the next time it is asked.
    /// </summary>
    public sealed class WindowSystem : IWindowSystem
    {
        const string UwpFrameClass = "ApplicationFrameWindow";
        const string UwpCoreClass = "Windows.UI.Core.CoreWindow";

        readonly object gate = new object();
        readonly uint ownProcessId = Native.GetCurrentProcessId();
        IntPtr lastExternal;
        IntPtr nameCacheWindow;
        uint nameCacheProcess;
        string nameCacheValue = "";

        public WindowSystem() { }

        /// <summary>Follows hook.ForegroundChanged so LastExternalForeground is exact even when focus flips quickly.</summary>
        public WindowSystem(InputHook hook)
        {
            if (hook != null) hook.ForegroundChanged += NotifyForeground;
        }

        /// <summary>Feed focus changes here (the hook thread calls it). Cheap; remembers the window when Alt+Tab would list it and it is not ours.</summary>
        public void NotifyForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            if (!WindowFilter.IsSwitchable(ReadFacts(hwnd))) return;
            lock (gate) lastExternal = hwnd;
        }

        public IntPtr Foreground { get { return Native.GetForegroundWindow(); } }

        public IntPtr LastExternalForeground
        {
            get
            {
                NotifyForeground(Native.GetForegroundWindow());   // covers a missed event, and the no-hook case
                IntPtr remembered;
                lock (gate) remembered = lastExternal;
                if (remembered != IntPtr.Zero && Native.IsWindow(remembered)) return remembered;
                IList<WindowInfo> windows = ListSwitchableWindows();   // it closed: the topmost remaining window is the best guess
                return windows.Count > 0 ? windows[0].Handle : IntPtr.Zero;
            }
        }

        /// <summary>The program name of the focused window ("notepad.exe", or "" when unknown). Cached per window, so it is cheap enough for a keyboard hook.</summary>
        public string ForegroundProcessName
        {
            get
            {
                IntPtr hwnd = Native.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return "";
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                lock (gate)
                {
                    if (hwnd == nameCacheWindow && pid == nameCacheProcess) return nameCacheValue;
                }
                string name = ProcessNameFor(hwnd, ClassOf(hwnd)) ?? "";
                lock (gate)
                {
                    nameCacheWindow = hwnd;
                    nameCacheProcess = pid;
                    nameCacheValue = name;
                }
                return name;
            }
        }

        public WindowInfo Describe(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return null;
            return BuildInfo(hwnd, ClassOf(hwnd), new Dictionary<uint, bool>());
        }

        public IList<WindowInfo> ListSwitchableWindows()
        {
            var handles = new List<IntPtr>();
            Native.EnumWindowsProc collect = (hwnd, lParam) => { handles.Add(hwnd); return true; };   // EnumWindows walks top-level windows in Z-order, topmost first
            Native.EnumWindows(collect, IntPtr.Zero);
            GC.KeepAlive(collect);

            var result = new List<WindowInfo>();
            var elevationByProcess = new Dictionary<uint, bool>();
            foreach (IntPtr hwnd in handles)
            {
                WindowFacts facts = ReadFacts(hwnd);
                if (WindowFilter.IsSwitchable(facts)) result.Add(BuildInfo(hwnd, facts.ClassName, elevationByProcess));
            }
            return result;
        }

        public WindowInfo Find(WindowMatch match)
        {
            if (match == null || match.IsEmpty) return null;
            foreach (WindowInfo window in ListSwitchableWindows())
            {
                if (match.Matches(window)) return window;
            }
            return null;
        }

        /// <summary>True when Alt+Tab would list the window and it does not belong to this process.</summary>
        public bool IsSwitchable(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return false;
            return WindowFilter.IsSwitchable(ReadFacts(hwnd));
        }

        public bool IsOwnWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            return pid == ownProcessId;
        }

        /// <summary>
        /// Brings a window to the front: restores it if minimised, asks Windows politely, and if the focus lock refuses,
        /// borrows the foreground thread's input state (AttachThreadInput). It never taps Alt (that would show Excel's key tips).
        /// Returns true once the window really is the foreground window, or false after timeoutMs.
        /// </summary>
        public bool Activate(IntPtr hwnd, int timeoutMs)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return false;
            var watch = Stopwatch.StartNew();
            Func<int> remaining = () => (int)Math.Max(0, timeoutMs - watch.ElapsedMilliseconds);

            if (Native.IsIconic(hwnd))
            {
                // The Async variant posts the request: a plain ShowWindow would block this thread for as long as a busy or hung window takes to answer.
                Native.ShowWindowAsync(hwnd, Native.SW_RESTORE);
                PollUntil(() => !Native.IsIconic(hwnd), Math.Min(500, remaining()));
            }
            if (Native.GetForegroundWindow() == hwnd) return true;

            Native.SetForegroundWindow(hwnd);
            if (PollUntil(() => Native.GetForegroundWindow() == hwnd, Math.Min(100, remaining()))) return true;

            // Windows only lets the process that last received input take the foreground. Attached to the current foreground thread we share its input state, so the call is allowed.
            AttachAndForce(hwnd);
            return PollUntil(() => Native.GetForegroundWindow() == hwnd, remaining());
        }

        static void AttachAndForce(IntPtr hwnd)
        {
            uint foregroundProcess;
            uint foregroundThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out foregroundProcess);
            uint thisThread = Native.GetCurrentThreadId();
            bool attached = foregroundThread != 0 && foregroundThread != thisThread && Native.AttachThreadInput(thisThread, foregroundThread, true);
            try
            {
                Native.BringWindowToTop(hwnd);
                Native.SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attached) Native.AttachThreadInput(thisThread, foregroundThread, false);
            }
        }

        // Checks the condition, then every 10 ms until it holds or ms have passed. One check always happens, even for 0 ms.
        static bool PollUntil(Func<bool> condition, int ms)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                if (condition()) return true;
                if (watch.ElapsedMilliseconds >= ms) return false;
                Thread.Sleep(10);
            }
        }

        // ---------------- reading windows ----------------

        WindowFacts ReadFacts(IntPtr hwnd)
        {
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            return new WindowFacts
            {
                Handle = hwnd,
                Visible = Native.IsWindowVisible(hwnd),
                Title = TitleOf(hwnd),
                Cloaked = IsCloaked(hwnd),
                HasOwner = Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero,
                ExStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64(),
                ClassName = ClassOf(hwnd),
                OwnProcess = pid == ownProcessId,
            };
        }

        WindowInfo BuildInfo(IntPtr hwnd, string className, Dictionary<uint, bool> elevationByProcess)
        {
            uint frameProcess;
            Native.GetWindowThreadProcessId(hwnd, out frameProcess);
            uint pid = RealProcessId(hwnd, className, frameProcess);
            bool elevated;
            if (!elevationByProcess.TryGetValue(pid, out elevated))
            {
                elevated = Elevation.IsProcessElevated(pid);
                elevationByProcess[pid] = elevated;
            }
            return new WindowInfo
            {
                Handle = hwnd,
                Title = TitleOf(hwnd),
                ProcessName = ImageNameOf(pid),
                ProcessId = (int)pid,
                ClassName = className,
                IsElevated = elevated,
            };
        }

        string ProcessNameFor(IntPtr hwnd, string className)
        {
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            return ImageNameOf(RealProcessId(hwnd, className, pid));
        }

        // Store apps live inside a frame window owned by ApplicationFrameHost.exe; the app's own process owns the CoreWindow child.
        static uint RealProcessId(IntPtr hwnd, string className, uint frameProcess)
        {
            if (!string.Equals(className, UwpFrameClass, StringComparison.Ordinal)) return frameProcess;
            IntPtr core = Native.FindWindowExW(hwnd, IntPtr.Zero, UwpCoreClass, null);
            if (core == IntPtr.Zero) return frameProcess;
            uint corePid;
            Native.GetWindowThreadProcessId(core, out corePid);
            return corePid != 0 ? corePid : frameProcess;
        }

        static string ImageNameOf(uint pid)
        {
            IntPtr process = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return "";
            try
            {
                for (int capacity = 1024; capacity <= 32768; capacity *= 4)
                {
                    var path = new StringBuilder(capacity);
                    int size = capacity;
                    if (Native.QueryFullProcessImageNameW(process, 0, path, ref size)) return Path.GetFileName(path.ToString());
                }
                return "";
            }
            finally { Native.CloseHandle(process); }
        }

        // GetWindowText reads the caption of another process's window directly; sending WM_GETTEXT instead would hang on a frozen app.
        static string TitleOf(IntPtr hwnd)
        {
            int length = Native.GetWindowTextLengthW(hwnd);
            if (length <= 0) return "";
            var text = new StringBuilder(length + 1);
            Native.GetWindowTextW(hwnd, text, text.Capacity);
            return text.ToString();
        }

        static string ClassOf(IntPtr hwnd)
        {
            var name = new StringBuilder(256);
            return Native.GetClassNameW(hwnd, name, name.Capacity) > 0 ? name.ToString() : "";
        }

        static bool IsCloaked(IntPtr hwnd)
        {
            int cloaked;
            try { return Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }
    }
}
