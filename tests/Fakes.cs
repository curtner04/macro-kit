using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MacroKit.Core;

namespace MacroKit.Tests
{
    /// <summary>
    /// Records every input call as readable text in <see cref="Log"/>, for example "down LCONTROL", "up LCONTROL",
    /// "combo LCONTROL+C", "text hello", "moveto 10,20", "moveby 5,-3", "button Left down", "wheel 120 v", "norm 100,200".
    /// Key names come from <see cref="Name"/>: L/R modifier names (LSHIFT, LCONTROL, LALT, LWIN...) and Super Macro tokens otherwise.
    /// Never sends real input.
    /// </summary>
    public sealed class FakeInputSink : IInputSink
    {
        public readonly List<string> Log = new List<string>();
        public readonly HashSet<ushort> HeldKeys = new HashSet<ushort>();             // base VKs
        public readonly HashSet<MouseButton> HeldButtons = new HashSet<MouseButton>();
        public readonly HashSet<ushort> PhysicallyDown = new HashSet<ushort>();       // keys IsPhysicallyDown reports as held (base VKs)
        public ScreenPoint CursorPosition { get; set; }
        /// <summary>Called after each call with its log line; tests use it to react (change the foreground when Alt+Tab is sent...).</summary>
        public Action<string> OnCall;
        /// <summary>When it returns true for a log line, that call throws InvalidOperationException (input blocked) and is not applied.</summary>
        public Func<string, bool> FailWhen;

        public static string Name(ushort vk)
        {
            switch (KeyNames.BaseVk(vk))
            {
                case 0xA0: return "LSHIFT";
                case 0xA1: return "RSHIFT";
                case 0xA2: return "LCONTROL";
                case 0xA3: return "RCONTROL";
                case 0xA4: return "LALT";
                case 0xA5: return "RALT";
                case 0x5B: return "LWIN";
                case 0x5C: return "RWIN";
                case 0x10: return "SHIFT";
                case 0x11: return "CONTROL";
                case 0x12: return "ALT";
            }
            return KeyNames.MacroToken(vk);
        }

        void Record(string line)
        {
            if (FailWhen != null && FailWhen(line)) throw new InvalidOperationException("Input blocked (fake): " + line);
            Log.Add(line);
        }

        void Done(string line) { if (OnCall != null) OnCall(line); }

        public void KeyDown(ushort vk) { string l = "down " + Name(vk); Record(l); HeldKeys.Add(KeyNames.BaseVk(vk)); Done(l); }
        public void KeyUp(ushort vk) { string l = "up " + Name(vk); Record(l); HeldKeys.Remove(KeyNames.BaseVk(vk)); Done(l); }

        public void KeyCombo(IList<ushort> keys)
        {
            string l = "combo " + string.Join("+", keys.Select(Name));
            Record(l);
            Done(l);
        }

        public void TypeText(string text)
        {
            string l = "text " + (text ?? "").Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");
            Record(l);
            Done(l);
        }

        public void MouseMoveTo(int x, int y) { string l = "moveto " + x + "," + y; Record(l); CursorPosition = new ScreenPoint(x, y); Done(l); }
        public void MouseMoveBy(int dx, int dy) { string l = "moveby " + dx + "," + dy; Record(l); CursorPosition = new ScreenPoint(CursorPosition.X + dx, CursorPosition.Y + dy); Done(l); }

        public void MouseButton(MouseButton button, bool down)
        {
            string l = "button " + button + (down ? " down" : " up");
            Record(l);
            if (down) HeldButtons.Add(button); else HeldButtons.Remove(button);
            Done(l);
        }

        public void MouseWheel(int delta, bool horizontal) { string l = "wheel " + delta + (horizontal ? " h" : " v"); Record(l); Done(l); }
        public void MouseMoveNormalized(int x65535, int y65535) { string l = "norm " + x65535 + "," + y65535; Record(l); Done(l); }

        /// <summary>Like GetAsyncKeyState: a generic Ctrl/Shift/Alt is down when either side is.</summary>
        public bool IsPhysicallyDown(ushort vk)
        {
            ushort k = KeyNames.BaseVk(vk);
            if (PhysicallyDown.Contains(k)) return true;
            switch (k)
            {
                case 0x10: return PhysicallyDown.Contains(0xA0) || PhysicallyDown.Contains(0xA1);
                case 0x11: return PhysicallyDown.Contains(0xA2) || PhysicallyDown.Contains(0xA3);
                case 0x12: return PhysicallyDown.Contains(0xA4) || PhysicallyDown.Contains(0xA5);
            }
            return false;
        }

        public void Clear() { Log.Clear(); }
    }

    /// <summary>
    /// Virtual time. Sleep advances NowMs instantly, runs callbacks scheduled with At/After when their time is reached,
    /// and throws OperationCanceledException as soon as the token is cancelled (also by such a callback).
    /// </summary>
    public sealed class FakeClock : IClock
    {
        sealed class Scheduled { public long Time; public int Order; public Action Action; }

        readonly List<Scheduled> scheduled = new List<Scheduled>();
        int order;

        public long NowMs { get; set; }
        /// <summary>Every requested Sleep duration, in order (including 0).</summary>
        public readonly List<int> Sleeps = new List<int>();
        /// <summary>Safety net: a runaway loop in the code under test fails instead of hanging.</summary>
        public int SleepLimit = 2000000;

        /// <summary>Runs the action when virtual time reaches the absolute time (during a Sleep or Advance).</summary>
        public void At(long timeMs, Action action) { scheduled.Add(new Scheduled { Time = timeMs, Order = order++, Action = action }); }
        /// <summary>Runs the action that many milliseconds from now.</summary>
        public void After(long deltaMs, Action action) { At(NowMs + deltaMs, action); }

        public void Sleep(int ms, CancellationToken token)
        {
            if (Sleeps.Count >= SleepLimit) throw new InvalidOperationException("FakeClock sleep limit reached (runaway loop?).");
            Sleeps.Add(ms);
            token.ThrowIfCancellationRequested();
            RunUntil(NowMs + Math.Max(0, ms), token);
            token.ThrowIfCancellationRequested();
        }

        /// <summary>Moves time forward without a token (for tests that drive time themselves).</summary>
        public void Advance(int ms) { RunUntil(NowMs + ms, CancellationToken.None); }

        void RunUntil(long target, CancellationToken token)
        {
            while (true)
            {
                Scheduled next = scheduled.Where(s => s.Time <= target).OrderBy(s => s.Time).ThenBy(s => s.Order).FirstOrDefault();
                if (next == null) break;
                scheduled.Remove(next);
                if (next.Time > NowMs) NowMs = next.Time;
                next.Action();
                token.ThrowIfCancellationRequested();   // cancelled by the callback: the sleep ends right there
            }
            if (target > NowMs) NowMs = target;
        }
    }

    /// <summary>A list of windows with a settable foreground. Never touches real windows.</summary>
    public sealed class FakeWindowSystem : IWindowSystem
    {
        readonly List<WindowInfo> windows = new List<WindowInfo>();            // switchable windows, most recent first
        readonly List<WindowInfo> ownWindows = new List<WindowInfo>();
        readonly HashSet<IntPtr> ownHandles = new HashSet<IntPtr>();

        public IntPtr Foreground { get; set; }
        public IntPtr LastExternalForeground { get; set; }
        /// <summary>Handles Activate refuses (returns false and leaves the foreground alone).</summary>
        public readonly HashSet<IntPtr> ActivateFails = new HashSet<IntPtr>();
        /// <summary>One line per Activate call: "activate Title (ok)" or "activate Title (failed)".</summary>
        public readonly List<string> ActivateLog = new List<string>();
        public readonly List<IntPtr> ActivatedHandles = new List<IntPtr>();
        /// <summary>Handles IsSwitchable reports as false even though AddWindow listed them (a window in the list is switchable by default).</summary>
        public readonly HashSet<IntPtr> NotSwitchable = new HashSet<IntPtr>();
        readonly List<WindowInfo> otherWindows = new List<WindowInfo>();       // shell surfaces: Describe finds them, they are never switchable

        /// <summary>Adds a window Alt+Tab would never list (Shell_TrayWnd, the desktop, the Start menu...). Describe finds it; IsSwitchable is false.</summary>
        public WindowInfo AddShellWindow(int handle, string title, string className, string process = "explorer.exe")
        {
            var info = new WindowInfo { Handle = new IntPtr(handle), Title = title, ProcessName = process, ProcessId = 2000 + handle, ClassName = className };
            otherWindows.Add(info);
            return info;
        }

        public bool IsSwitchable(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero && !NotSwitchable.Contains(hwnd) && windows.Any(w => w.Handle == hwnd);
        }

        /// <summary>Adds a switchable window at the back of the Z-order and returns it.</summary>
        public WindowInfo AddWindow(int handle, string title, string process, bool elevated = false)
        {
            var info = new WindowInfo { Handle = new IntPtr(handle), Title = title, ProcessName = process, ProcessId = 1000 + handle, ClassName = "FakeWindow", IsElevated = elevated };
            windows.Add(info);
            return info;
        }

        /// <summary>Adds a window that belongs to Macro Kit (not listed as switchable, IsOwnWindow is true).</summary>
        public WindowInfo AddOwnWindow(int handle, string title = "Macro Kit")
        {
            var info = new WindowInfo { Handle = new IntPtr(handle), Title = title, ProcessName = "Macro Kit.exe", ProcessId = 4242, ClassName = "FakeOwn" };
            ownWindows.Add(info);
            ownHandles.Add(info.Handle);
            return info;
        }

        public void RemoveWindow(IntPtr handle) { windows.RemoveAll(w => w.Handle == handle); }

        public WindowInfo Describe(IntPtr hwnd)
        {
            return windows.Concat(ownWindows).Concat(otherWindows).FirstOrDefault(w => w.Handle == hwnd);
        }

        public IList<WindowInfo> ListSwitchableWindows() { return new List<WindowInfo>(windows); }

        public WindowInfo Find(WindowMatch match) { return windows.FirstOrDefault(w => match != null && match.Matches(w)); }

        public bool IsOwnWindow(IntPtr hwnd) { return ownHandles.Contains(hwnd); }

        public bool Activate(IntPtr hwnd, int timeoutMs)
        {
            WindowInfo info = Describe(hwnd);
            string title = info == null ? hwnd.ToString() : info.Title;
            ActivatedHandles.Add(hwnd);
            if (info == null || ActivateFails.Contains(hwnd))
            {
                ActivateLog.Add("activate " + title + " (failed)");
                return false;
            }
            ActivateLog.Add("activate " + title + " (ok)");
            SetForeground(hwnd);
            return true;
        }

        /// <summary>Makes a window the foreground like a user click would: moves it to the front of the Z-order and tracks LastExternalForeground.</summary>
        public void SetForeground(IntPtr hwnd)
        {
            Foreground = hwnd;
            WindowInfo info = windows.FirstOrDefault(w => w.Handle == hwnd);
            if (info != null)
            {
                windows.Remove(info);
                windows.Insert(0, info);
                LastExternalForeground = hwnd;
            }
        }

        /// <summary>What a real Alt+Tab does: the foreground becomes the next most recent switchable window (Macro Kit is never picked).</summary>
        public void SimulateAltTab()
        {
            if (windows.Count < 2) return;
            WindowInfo next = Foreground == windows[0].Handle ? windows[1] : windows[0];
            SetForeground(next.Handle);
        }
    }

    public sealed class FakeClipboard : IClipboard
    {
        sealed class Snap { public string Text; public object Other; }

        public string Text;
        /// <summary>Non-text content (a picture, files...). Survives Snapshot and Restore, and is wiped by every write like EmptyClipboard.</summary>
        public object Other;
        /// <summary>Every SetText / SetTextPrivate value in order, plus the text of each Restore.</summary>
        public readonly List<string> History = new List<string>();
        /// <summary>The SetTextPrivate values in order.</summary>
        public readonly List<string> PrivateHistory = new List<string>();
        /// <summary>How many times Restore ran.</summary>
        public int Restores;
        /// <summary>The sequence number; every write bumps it.</summary>
        public uint Sequence = 1;
        /// <summary>When true, SetText / SetTextPrivate throw InvalidOperationException (another program holds the clipboard).</summary>
        public bool FailWrites;
        /// <summary>When true, Snapshot throws InvalidOperationException.</summary>
        public bool FailSnapshot;
        /// <summary>When true, Restore throws InvalidOperationException.</summary>
        public bool FailRestore;

        public string GetText() { return Text; }

        public void SetText(string text)
        {
            if (FailWrites) throw new InvalidOperationException("Clipboard busy (fake).");
            Text = text; Other = null; History.Add(text); Sequence++;
        }

        public void SetTextPrivate(string text)
        {
            if (FailWrites) throw new InvalidOperationException("Clipboard busy (fake).");
            Text = text; Other = null; History.Add(text); PrivateHistory.Add(text); Sequence++;
        }

        public object Snapshot()
        {
            if (FailSnapshot) throw new InvalidOperationException("Clipboard busy (fake).");
            return new Snap { Text = Text, Other = Other };
        }

        public void Restore(object snapshot)
        {
            if (FailRestore) throw new InvalidOperationException("Clipboard busy (fake).");
            var snap = snapshot as Snap;
            Text = snap == null ? "" : snap.Text ?? "";
            Other = snap == null ? null : snap.Other;
            History.Add(Text);
            Restores++;
            Sequence++;
        }

        public uint SequenceNumber { get { return Sequence; } }

        /// <summary>Another program copies something: the content and the sequence number change.</summary>
        public void SimulateOtherAppCopy(string text) { Text = text; Other = null; Sequence++; }
    }
}
