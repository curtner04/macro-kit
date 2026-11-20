using System;
using System.Runtime.InteropServices;
using MacroKit.Core;

namespace MacroKit.Win32
{
    /// <summary>
    /// 1.12.0: reads one screen pixel for {{WAITPIXEL}} and the palette's colour Pick. Coordinates are physical pixels (the app is
    /// PerMonitorV2 DPI aware), the same ones window rectangles and the mouse use.
    /// </summary>
    public sealed class ScreenPixels : IScreen
    {
        const uint CLR_INVALID = 0xFFFFFFFF;

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr hdc, int x, int y);

        public bool TryGetPixel(int x, int y, out int rgb)
        {
            rgb = 0;
            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;
            try
            {
                uint bgr = GetPixel(dc, x, y);   // 0x00BBGGRR
                if (bgr == CLR_INVALID) return false;
                rgb = (int)(((bgr & 0xFF) << 16) | (bgr & 0xFF00) | ((bgr >> 16) & 0xFF));
                return true;
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
        }
    }
}
