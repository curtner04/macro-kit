using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MacroKit.Core
{
    public enum MouseButton { Left, Right, Middle, X1, X2 }

    public struct ScreenPoint
    {
        public int X;
        public int Y;
        public ScreenPoint(int x, int y) { X = x; Y = y; }
        public override string ToString() { return X + "," + Y; }
    }

    /// <summary>Output of keyboard and mouse input. Real: Win32.SendInputSink. Tests: Fakes.FakeInputSink.</summary>
    public interface IInputSink
    {
        void KeyDown(ushort vk);                 // scan code + extended flag derived by the sink (KeyNames.IsExtended)
        void KeyUp(ushort vk);
        void KeyCombo(IList<ushort> keys);       // all down in order, ~15 ms hold, all up in reverse order
        void TypeText(string text);              // Unicode; '\n' = Enter key, '\t' = Tab key, '\r' dropped
        void MouseMoveTo(int x, int y);          // absolute physical pixels on the virtual desktop
        void MouseMoveBy(int dx, int dy);        // relative, exact pixels (no mouse acceleration)
        void MouseButton(MouseButton button, bool down);
        void MouseWheel(int delta, bool horizontal); // 120 per notch; positive = up / right
        ScreenPoint CursorPosition { get; }
        bool IsPhysicallyDown(ushort vk);        // GetAsyncKeyState
        void MouseMoveNormalized(int x65535, int y65535); // Super Macro's deprecated MOUSEPOS: 0..65535 over the primary monitor
    }

    /// <summary>Time. Real: SystemClock (Stopwatch + WaitHandle). Tests: FakeClock (virtual time).</summary>
    public interface IClock
    {
        long NowMs { get; }
        /// <summary>Waits ms milliseconds; throws OperationCanceledException as soon as the token is cancelled.</summary>
        void Sleep(int ms, CancellationToken token);
    }

    public sealed class SystemClock : IClock
    {
        readonly Stopwatch watch = Stopwatch.StartNew();

        public long NowMs { get { return watch.ElapsedMilliseconds; } }

        public void Sleep(int ms, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (ms > 0) token.WaitHandle.WaitOne(ms);
            token.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Clipboard. Real: Win32.Win32Clipboard (usable from any thread). Tests: FakeClipboard.</summary>
    public interface IClipboard
    {
        string GetText();
        void SetText(string text);

        /// <summary>
        /// Copies everything on the clipboard that can be copied (text, rich text, files, pictures...) into an opaque object
        /// for <see cref="Restore"/>. Falls back to text only when the clipboard is huge. Throws InvalidOperationException when it cannot read.
        /// </summary>
        object Snapshot();

        /// <summary>Puts a <see cref="Snapshot"/> back (an empty snapshot empties the clipboard). The restored data is kept out of clipboard history and cloud sync.</summary>
        void Restore(object snapshot);

        /// <summary>Like SetText, but also asks Windows to keep the text out of the clipboard history (Win+V) and cloud sync.</summary>
        void SetTextPrivate(string text);

        /// <summary>Changes every time anything puts new data on the clipboard (GetClipboardSequenceNumber).</summary>
        uint SequenceNumber { get; }
    }

    public sealed class WindowInfo
    {
        public IntPtr Handle;
        public string Title;
        public string ProcessName;   // "Chrome.exe"
        public int ProcessId;
        public string ClassName;
        public bool IsElevated;      // best effort: true when the process token says elevated or cannot be read

        public override string ToString()
        {
            return (Title ?? "") + " — " + (ProcessName ?? "");
        }
    }

    /// <summary>
    /// How a saved target window is found again. Empty fields are ignored; matching is case-insensitive.
    /// An entirely empty match matches no window (it means "not set").
    /// </summary>
    public sealed class WindowMatch
    {
        public string ProcessName { get; set; }    // "EXCEL.EXE"
        public string TitleContains { get; set; }  // "Book1"

        public bool IsEmpty
        {
            get { return string.IsNullOrWhiteSpace(ProcessName) && string.IsNullOrWhiteSpace(TitleContains); }
        }

        public bool Matches(WindowInfo w)
        {
            if (w == null || IsEmpty) return false;
            if (!string.IsNullOrWhiteSpace(ProcessName) && !SameProcess(ProcessName, w.ProcessName)) return false;
            if (!string.IsNullOrWhiteSpace(TitleContains))
            {
                if (w.Title == null || w.Title.IndexOf(TitleContains.Trim(), StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            return true;
        }

        /// <summary>For messages: Chrome.exe, title contains “Orders”</summary>
        public string Describe()
        {
            bool hasProcess = !string.IsNullOrWhiteSpace(ProcessName);
            bool hasTitle = !string.IsNullOrWhiteSpace(TitleContains);
            string title = "title contains “" + (TitleContains ?? "").Trim() + "”";
            if (hasProcess && hasTitle) return ProcessName.Trim() + ", " + title;
            if (hasProcess) return ProcessName.Trim();
            if (hasTitle) return title;
            return "no window chosen";
        }

        public static WindowMatch From(WindowInfo w)
        {
            if (w == null) return new WindowMatch();
            return new WindowMatch { ProcessName = w.ProcessName, TitleContains = w.Title };
        }

        // "excel" and "EXCEL.EXE" name the same program.
        static bool SameProcess(string wanted, string actual)
        {
            if (actual == null) return false;
            return string.Equals(StripExe(wanted), StripExe(actual), StringComparison.OrdinalIgnoreCase);
        }

        static string StripExe(string name)
        {
            name = name.Trim();
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
        }
    }

    /// <summary>Windows. Real: Win32.WindowSystem. Tests: Fakes.FakeWindowSystem.</summary>
    public interface IWindowSystem
    {
        IntPtr Foreground { get; }
        WindowInfo Describe(IntPtr hwnd);                 // null when the window is gone
        IList<WindowInfo> ListSwitchableWindows();        // what Alt+Tab would show, Z-order (most recent first), excluding this process
        WindowInfo Find(WindowMatch match);               // first match in ListSwitchableWindows order, or null
        bool Activate(IntPtr hwnd, int timeoutMs);        // restore if minimised, force foreground, true once Foreground == hwnd
        bool IsOwnWindow(IntPtr hwnd);                    // belongs to this process
        IntPtr LastExternalForeground { get; }            // most recent switchable foreground window not owned by this process
        bool IsSwitchable(IntPtr hwnd);                   // Alt+Tab would list it and it is not ours (false for the taskbar, desktop, Start menu, task switcher)
    }
}
