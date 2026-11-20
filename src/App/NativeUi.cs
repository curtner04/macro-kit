using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MacroKit.App
{
    /// <summary>
    /// The few Win32 calls the WPF shell needs for its own windows (placing the Deck and the run pill in physical pixels,
    /// reading window styles for the self test). Nothing here can send input or change focus.
    /// </summary>
    internal static class NativeUi
    {
        public const long WS_EX_TOOLWINDOW = 0x80;
        public const long WS_EX_NOACTIVATE = 0x08000000;
        public const long WS_EX_APPWINDOW = 0x40000;
        const int GWL_EXSTYLE = -20;
        const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", SetLastError = true)] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        /// <summary>The window's outer rectangle in physical screen pixels, or false when it has no handle yet.</summary>
        public static bool TryGetRect(Window window, out Rect pixels)
        {
            pixels = Rect.Empty;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            RECT r;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out r)) return false;
            pixels = new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            return true;
        }

        /// <summary>Moves a window to physical screen pixels without activating it, resizing it or changing its z-order.</summary>
        public static void MoveTo(Window window, int x, int y)
        {
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        const uint SWP_NOMOVE = 0x2;
        const int SW_SHOWMINNOACTIVE = 7;
        static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);

        /// <summary>True when Windows itself has the window maximized.</summary>
        public static bool IsMaximized(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            return hwnd != IntPtr.Zero && IsZoomed(hwnd);
        }

        /// <summary>Shows a window minimized without activating it (SW_SHOWMINNOACTIVE); its restored size and position are kept.</summary>
        public static void ShowMinimizedNoActivate(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SW_SHOWMINNOACTIVE);
        }

        /// <summary>
        /// Puts an already visible window directly behind the window that is in front (or at the very bottom when there is none, or
        /// when that window is always-on-top), without activating, moving or resizing anything. Returns what it did.
        /// </summary>
        public static string PlaceBehind(Window window, bool behindForeground)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return "no window";
            IntPtr after = HWND_BOTTOM;
            string what = "bottom";
            if (behindForeground)
            {
                IntPtr foreground = GetForegroundWindow();
                bool topmost = foreground != IntPtr.Zero && (GetWindowLongPtr(foreground, GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST) != 0;
                if (foreground != IntPtr.Zero && foreground != hwnd && !topmost) { after = foreground; what = "behind the front window"; }
            }
            SetWindowPos(hwnd, after, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            return what;
        }

        const long WS_EX_TOPMOST = 0x8;

        public static long ExStyle(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            return hwnd == IntPtr.Zero ? 0 : GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        }

        /// <summary>
        /// True when the rectangle (physical pixels) shows at least a small corner (80 x 40) on one real monitor. The virtual
        /// desktop's bounding box is not enough: with monitors of different sizes or positions it also holds gaps nobody can see.
        /// </summary>
        public static bool IsOnScreen(double left, double top, double width, double height)
        {
            var monitors = new System.Collections.Generic.List<Rect>();
            foreach (System.Windows.Forms.Screen s in System.Windows.Forms.Screen.AllScreens)
                monitors.Add(new Rect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height));
            return IsOnAnyMonitor(new Rect(left, top, Math.Max(width, 1), Math.Max(height, 1)), monitors);
        }

        /// <summary>The check behind <see cref="IsOnScreen"/>, for a given list of monitors.</summary>
        public static bool IsOnAnyMonitor(Rect area, System.Collections.Generic.IEnumerable<Rect> monitors)
        {
            foreach (Rect monitor in monitors)
            {
                Rect shown = area;
                shown.Intersect(monitor);
                if (!shown.IsEmpty && shown.Width >= 80 && shown.Height >= 40) return true;
            }
            return false;
        }
    }
}
