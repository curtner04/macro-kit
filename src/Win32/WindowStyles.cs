using System;
using System.Windows.Interop;

namespace MacroKit.Win32
{
    /// <summary>Window styles that keep Macro Kit's Deck and run pill from taking focus or appearing in Alt+Tab.</summary>
    public static class WindowStyles
    {
        /// <summary>
        /// Makes a window a no-activate tool window: clicking it never takes focus from the app the macro is running in, it
        /// has no taskbar button and Alt+Tab skips it. Call it once the window handle exists (WPF: SourceInitialized),
        /// before the window is shown. WS_EX_NOACTIVATE alone is not enough: without WS_EX_TOOLWINDOW the window still gets a
        /// taskbar button, and a leftover WS_EX_APPWINDOW would force it into Alt+Tab.
        /// </summary>
        public static void MakeNoActivate(IntPtr hwnd)
        {
            long style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            style |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
            style &= ~Native.WS_EX_APPWINDOW;
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(style));
            // Tell the window manager the frame changed without moving, resizing, re-stacking or activating anything.
            Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        }

        /// <summary>
        /// An HwndSourceHook that answers WM_MOUSEACTIVATE with MA_NOACTIVATE, so a click on the window is handled (buttons
        /// work) without the window becoming active. Add it with HwndSource.AddHook, or call <see cref="ApplyNoActivate"/>.
        /// </summary>
        public static IntPtr NoActivateHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == (int)Native.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
            }
            return IntPtr.Zero;
        }

        /// <summary>MakeNoActivate plus the WM_MOUSEACTIVATE hook for a WPF window's HwndSource.</summary>
        public static void ApplyNoActivate(HwndSource source)
        {
            if (source == null) throw new ArgumentNullException("source");
            MakeNoActivate(source.Handle);
            source.AddHook(NoActivateHook);
        }

        /// <summary>
        /// Hides a window from Alt+Tab and the taskbar (hide = true) or lets it appear again. Windows reads these styles when the
        /// window is shown, so a visible window is hidden and shown again, without activating it.
        /// </summary>
        public static void HideFromAltTab(IntPtr hwnd, bool hide)
        {
            bool wasVisible = Native.IsWindowVisible(hwnd);
            if (wasVisible) Native.ShowWindow(hwnd, Native.SW_HIDE);
            long style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            if (hide)
            {
                style |= Native.WS_EX_TOOLWINDOW;
                style &= ~Native.WS_EX_APPWINDOW;
            }
            else
            {
                style &= ~Native.WS_EX_TOOLWINDOW;
            }
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(style));
            if (wasVisible) Native.ShowWindow(hwnd, Native.SW_SHOWNA);
        }
    }
}
