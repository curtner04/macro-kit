using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using MacroKit.Core;

namespace MacroKit.Win32
{
    /// <summary>
    /// Everything that needs a Windows message loop, on one dedicated thread: the low-level keyboard and mouse hooks, the
    /// foreground-window WinEvent hook and a message-only window for RegisterHotKey. Events and handlers run on that thread,
    /// so they must return quickly (Windows silently removes a low-level hook whose callback is too slow); subscribers
    /// marshal to the UI themselves.
    /// </summary>
    public sealed class InputHook : IDisposable
    {
        const uint WmRunQueued = Native.WM_APP + 1;      // run the queued work on the hook thread
        const uint WmReinstallHooks = Native.WM_APP + 2; // a callback was slow: Windows may have dropped that hook
        // LowLevelHooksTimeout defaults to 300 ms and Windows 10 caps it at 1000 ms. A callback anywhere near that risks removal,
        // so anything past this is treated as "the hook may be gone" and reinstalled.
        const double SlowCallbackMs = 200;
        const int QueuedWorkTimeoutMs = 3000;

        readonly object gate = new object();
        readonly ConcurrentQueue<QueuedWork> queue = new ConcurrentQueue<QueuedWork>();
        readonly HashSet<int> registeredHotkeys = new HashSet<int>();   // touched on the hook thread only
        readonly KeyTranslator translator = new KeyTranslator();
        readonly Stopwatch clock = Stopwatch.StartNew();
        volatile IntPtr[] ownWindows = new IntPtr[0];   // Macro Kit's own windows, replaced whole by SetOwnWindows

        // The delegates are fields so the garbage collector can never free them while Windows holds their function pointers.
        readonly Native.HookProc keyboardProc;
        readonly Native.HookProc mouseProc;
        readonly Native.WinEventProc foregroundProc;
        readonly Native.WndProc windowProc;

        Thread thread;
        uint threadId;
        int threadManagedId;
        volatile bool running;
        string startError;
        string className;
        IntPtr window, keyboardHook, mouseHook, foregroundHook;

        public InputHook()
        {
            keyboardProc = KeyboardCallback;
            mouseProc = MouseCallback;
            foregroundProc = ForegroundCallback;
            windowProc = WindowCallback;
        }

        /// <summary>Called for every keyboard event on the hook thread: (event, typed text, ...) returns true to swallow the key. TimeMs is on the hook's own monotonic clock.</summary>
        public Func<KeyEvent, string, bool> KeyHandler { get; set; }

        /// <summary>Called for every mouse event on the hook thread. OverOwnWindow is worked out for clicks and wheel turns only (not for every move), from the windows given to SetOwnWindows.</summary>
        public Action<MouseEvent> MouseHandler { get; set; }

        /// <summary>A registered global hotkey was pressed (argument: the id given to RegisterHotkey). Raised on the hook thread.</summary>
        public event Action<int> HotkeyPressed;

        /// <summary>The foreground window changed (argument: its handle). Raised on the hook thread.</summary>
        public event Action<IntPtr> ForegroundChanged;

        public bool IsRunning { get { return running; } }

        /// <summary>Starts the hook thread and installs everything. Throws InvalidOperationException with a plain message when Windows refuses.</summary>
        public void Start()
        {
            lock (gate)
            {
                if (running) return;
                startError = null;
                var started = new ManualResetEvent(false);
                thread = new Thread(() => Run(started)) { IsBackground = true, Name = "Macro Kit input hook" };
                thread.Start();
                if (!started.WaitOne(5000)) throw new InvalidOperationException("The input hook did not start in time.");
                started.Close();
                if (startError != null)
                {
                    thread.Join(2000);
                    throw new InvalidOperationException(startError);
                }
            }
        }

        /// <summary>Unhooks, unregisters every hotkey and ends the thread.</summary>
        public void Stop()
        {
            Thread toJoin;
            lock (gate)
            {
                if (!running) return;
                toJoin = thread;
                Native.PostThreadMessageW(threadId, Native.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            }
            if (Thread.CurrentThread.ManagedThreadId != threadManagedId) toJoin.Join(3000);
        }

        public void Dispose() { Stop(); }

        /// <summary>
        /// Registers a system-wide hotkey (with MOD_NOREPEAT, so holding it fires once). Safe from any thread; the work is
        /// done on the hook thread because RegisterHotKey ties the hotkey to the thread that owns the window.
        /// Registering an id again replaces its hotkey. On failure the error is a message for the Settings page.
        /// </summary>
        public bool RegisterHotkey(int id, Hotkey hotkey, out string error)
        {
            string failure = null;
            bool ok = RunOnHookThread(() => RegisterCore(id, hotkey, out failure));
            error = ok ? null : failure ?? "The hotkey could not be registered because the input hook is not running.";
            return ok;
        }

        public void UnregisterHotkey(int id)
        {
            RunOnHookThread(() =>
            {
                if (registeredHotkeys.Remove(id)) Native.UnregisterHotKey(window, id);
                return true;
            });
        }

        // ---------------- hook thread ----------------

        void Run(ManualResetEvent started)
        {
            try
            {
                threadId = Native.GetCurrentThreadId();
                threadManagedId = Thread.CurrentThread.ManagedThreadId;
                Native.MSG first;
                Native.PeekMessageW(out first, IntPtr.Zero, 0, 0, Native.PM_NOREMOVE);   // gives this thread a message queue before anyone posts to it
                CreateWindow();
                InstallKeyboard();
                InstallMouse();
                foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                    foregroundProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
                if (foregroundHook == IntPtr.Zero) throw new InvalidOperationException("Windows would not report window focus changes.");
                running = true;
            }
            catch (Exception ex)
            {
                startError = ex is InvalidOperationException ? ex.Message : "Macro Kit could not start watching the keyboard and mouse: " + ex.Message;
                Log.Error("Input hook start failed.", ex);
            }
            started.Set();
            if (!running)
            {
                Teardown();
                return;
            }

            Native.MSG msg;
            int result;
            while ((result = Native.GetMessageW(out msg, IntPtr.Zero, 0, 0)) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessageW(ref msg);
            }
            running = false;
            Teardown();
            DrainQueueWithFailure();
        }

        void CreateWindow()
        {
            className = "MacroKit.InputHook." + Guid.NewGuid().ToString("N");
            IntPtr module = Native.GetModuleHandleW(null);
            var wc = new Native.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf(typeof(Native.WNDCLASSEX)),
                lpfnWndProc = windowProc,
                hInstance = module,
                lpszClassName = className,
            };
            if (Native.RegisterClassExW(ref wc) == 0) throw new InvalidOperationException("Windows would not create the hotkey window.");
            // A message-only window (parent HWND_MESSAGE): it receives WM_HOTKEY but never shows up in any window list.
            window = Native.CreateWindowExW(0, className, "Macro Kit input", 0, 0, 0, 0, 0, Native.HWND_MESSAGE, IntPtr.Zero, module, IntPtr.Zero);
            if (window == IntPtr.Zero) throw new InvalidOperationException("Windows would not create the hotkey window.");
        }

        void InstallKeyboard()
        {
            if (keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboardHook);
            // hMod = the exe's own module: low-level hooks run in the installing thread, but SetWindowsHookEx still wants a valid module.
            keyboardHook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, keyboardProc, Native.GetModuleHandleW(null), 0);
            if (keyboardHook == IntPtr.Zero) throw new InvalidOperationException("Windows would not let Macro Kit watch the keyboard (error " + Marshal.GetLastWin32Error() + ").");
        }

        void InstallMouse()
        {
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            mouseHook = Native.SetWindowsHookExW(Native.WH_MOUSE_LL, mouseProc, Native.GetModuleHandleW(null), 0);
            if (mouseHook == IntPtr.Zero) throw new InvalidOperationException("Windows would not let Macro Kit watch the mouse (error " + Marshal.GetLastWin32Error() + ").");
        }

        void Teardown()
        {
            foreach (int id in registeredHotkeys) Native.UnregisterHotKey(window, id);
            registeredHotkeys.Clear();
            if (keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            if (foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(foregroundHook);
            keyboardHook = mouseHook = foregroundHook = IntPtr.Zero;
            if (window != IntPtr.Zero) Native.DestroyWindow(window);
            window = IntPtr.Zero;
            if (className != null) Native.UnregisterClassW(className, Native.GetModuleHandleW(null));
            className = null;
        }

        IntPtr KeyboardCallback(int code, UIntPtr wParam, IntPtr lParam)
        {
            if (code == Native.HC_ACTION)
            {
                long started = Stopwatch.GetTimestamp();
                bool swallow = false;
                try { swallow = HandleKey((int)wParam.ToUInt32(), lParam); }
                catch (Exception ex) { Log.Error("Keyboard hook handler failed.", ex); }
                NoteDuration(started, "keyboard");
                if (swallow) return (IntPtr)1;   // a non-zero return stops the key from reaching the application
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        IntPtr MouseCallback(int code, UIntPtr wParam, IntPtr lParam)
        {
            if (code == Native.HC_ACTION)
            {
                long started = Stopwatch.GetTimestamp();
                try { HandleMouse((int)wParam.ToUInt32(), lParam); }
                catch (Exception ex) { Log.Error("Mouse hook handler failed.", ex); }
                NoteDuration(started, "mouse");
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        bool HandleKey(int message, IntPtr lParam)
        {
            var handler = KeyHandler;
            if (handler == null) return false;
            var raw = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            KeyEvent e = HookDecoding.DecodeKey(raw, message, clock.ElapsedMilliseconds);
            string typed = e.Down && !e.Injected ? translator.Translate(e.Vk, e.Scan) : null;
            return handler(e, typed);
        }

        void HandleMouse(int message, IntPtr lParam)
        {
            var handler = MouseHandler;
            if (handler == null) return;
            var raw = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            MouseEvent e;
            if (!HookDecoding.DecodeMouse(raw, message, clock.ElapsedMilliseconds, out e)) return;
            bool click = e.Kind == MouseEventKind.Down || e.Kind == MouseEventKind.Up || e.Kind == MouseEventKind.Wheel || e.Kind == MouseEventKind.HWheel;
            if (!e.Injected && click) e.OverOwnWindow = IsOverOwnWindow(raw.pt);
            handler(e);
        }

        /// <summary>
        /// Tells the hook which windows are Macro Kit's own (main window, Deck, pill). Call it from the UI thread whenever one is
        /// created or closed; a window that is hidden at the time of a click simply does not count. The list is copied.
        /// </summary>
        public void SetOwnWindows(IntPtr[] hwnds)
        {
            var list = new List<IntPtr>();
            if (hwnds != null)
            {
                foreach (IntPtr hwnd in hwnds)
                {
                    if (hwnd != IntPtr.Zero && !list.Contains(hwnd)) list.Add(hwnd);
                }
            }
            ownWindows = list.ToArray();   // replaced whole: the hook thread reads whichever array it sees, never a half-built one
        }

        // WindowFromPoint would send WM_NCHITTEST to the window under the cursor, which can be a window of this very process whose
        // UI thread is busy: far too risky inside a low-level hook. GetWindowRect and IsWindowVisible send nothing.
        bool IsOverOwnWindow(Native.POINT point)
        {
            IntPtr[] windows = ownWindows;
            for (int i = 0; i < windows.Length; i++)
            {
                if (!Native.IsWindowVisible(windows[i])) continue;
                Native.RECT rect;
                if (!Native.GetWindowRect(windows[i], out rect)) continue;
                if (HookDecoding.PointInRect(rect, point.X, point.Y)) return true;
            }
            return false;
        }

        // Windows removes a low-level hook without telling anyone when its callback exceeds LowLevelHooksTimeout. We cannot
        // observe the removal, but we can see our own slow callbacks and put the hook back from the message loop.
        void NoteDuration(long startTimestamp, string which)
        {
            double ms = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            if (ms < SlowCallbackMs) return;
            Log.Warn("The " + which + " hook handler took " + (int)ms + " ms; reinstalling the hooks in case Windows dropped them.");
            Native.PostMessageW(window, WmReinstallHooks, UIntPtr.Zero, IntPtr.Zero);
        }

        void ForegroundCallback(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
        {
            if (eventType != Native.EVENT_SYSTEM_FOREGROUND || idObject != Native.OBJID_WINDOW || hwnd == IntPtr.Zero) return;
            translator.Reset();   // a half-typed dead key belongs to the window that lost focus
            var handler = ForegroundChanged;
            if (handler == null) return;
            try { handler(hwnd); }
            catch (Exception ex) { Log.Error("Foreground change handler failed.", ex); }
        }

        IntPtr WindowCallback(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case Native.WM_HOTKEY:
                    var pressed = HotkeyPressed;
                    if (pressed != null)
                    {
                        try { pressed((int)wParam.ToUInt32()); }
                        catch (Exception ex) { Log.Error("Hotkey handler failed.", ex); }
                    }
                    return IntPtr.Zero;
                case WmRunQueued:
                    QueuedWork work;
                    while (queue.TryDequeue(out work)) work.Run();
                    return IntPtr.Zero;
                case WmReinstallHooks:
                    try { InstallKeyboard(); InstallMouse(); }
                    catch (Exception ex) { Log.Error("Could not reinstall the input hooks.", ex); }
                    return IntPtr.Zero;
            }
            return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // ---------------- hotkeys and cross-thread work ----------------

        bool RegisterCore(int id, Hotkey hotkey, out string error)
        {
            error = null;
            if (hotkey.IsEmpty) { error = "No hotkey is set."; return false; }
            if (!hotkey.IsValidGlobal) { error = hotkey + " can't be a hotkey on its own. Add Ctrl, Alt or Win."; return false; }
            if (registeredHotkeys.Remove(id)) Native.UnregisterHotKey(window, id);
            if (!Native.RegisterHotKey(window, id, HookDecoding.ToNativeModifiers(hotkey.Modifiers), hotkey.Vk))
            {
                int code = Marshal.GetLastWin32Error();
                error = code == Native.ERROR_HOTKEY_ALREADY_REGISTERED
                    ? "Another app already uses " + hotkey + "."
                    : "Windows would not register " + hotkey + " (error " + code + ").";
                return false;
            }
            registeredHotkeys.Add(id);
            return true;
        }

        sealed class QueuedWork
        {
            readonly Func<bool> work;
            readonly ManualResetEvent done = new ManualResetEvent(false);
            public bool Result;

            public QueuedWork(Func<bool> work) { this.work = work; }

            public void Run()
            {
                try { Result = work(); }
                catch (Exception ex) { Log.Error("Work on the input hook thread failed.", ex); }
                finally { done.Set(); }
            }

            /// <summary>Releases the waiting caller with a failure, without running the work (the thread is going away).</summary>
            public void Abandon() { done.Set(); }

            public bool Wait(int ms) { return done.WaitOne(ms); }
        }

        // Runs work on the hook thread and waits for it. Returns false when the hook is not running or does not answer in time.
        bool RunOnHookThread(Func<bool> work)
        {
            if (running && Thread.CurrentThread.ManagedThreadId == threadManagedId) return work();
            if (!running) return false;
            var item = new QueuedWork(work);
            queue.Enqueue(item);
            if (!Native.PostMessageW(window, WmRunQueued, UIntPtr.Zero, IntPtr.Zero)) return false;
            return item.Wait(QueuedWorkTimeoutMs) && item.Result;
        }

        // Anything still queued when the loop ends must not leave a caller waiting for its full timeout.
        void DrainQueueWithFailure()
        {
            QueuedWork work;
            while (queue.TryDequeue(out work)) work.Abandon();
        }
    }
}
