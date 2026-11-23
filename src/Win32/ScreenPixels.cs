using System;
using System.Runtime.InteropServices;
using MacroKit.Core;

namespace MacroKit.Win32
{
    /// <summary>
    /// 1.12.0: reads one screen pixel for {{WAITPIXEL}} and the palette's colour Pick. 1.13.0: copies a rectangle of the screen for
    /// {{WAITIMAGE}}, {{CLICKIMAGE}} and the palette's picture Capture. Coordinates are physical pixels (the app is PerMonitorV2 DPI
    /// aware), the same ones window rectangles and the mouse use.
    /// </summary>
    public sealed class ScreenPixels : IScreen
    {
        const uint CLR_INVALID = 0xFFFFFFFF;
        const int SRCCOPY = 0x00CC0020;
        const uint DIB_RGB_COLORS = 0;

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr hdc, int x, int y);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src, int sx, int sy, int rop);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines, [Out] int[] bits, ref BITMAPINFOHEADER info, uint usage);

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

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

        // The biggest rectangle read at once: an 8K screen. A larger window is read only this far.
        const int MaxSide = 7680;

        public PixelImage Capture(int left, int top, int width, int height)
        {
            width = Math.Min(width, MaxSide);
            height = Math.Min(height, MaxSide);
            if (width <= 0 || height <= 0) return null;
            IntPtr screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return null;
            IntPtr memory = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                memory = CreateCompatibleDC(screen);
                bitmap = CreateCompatibleBitmap(screen, width, height);
                if (memory == IntPtr.Zero || bitmap == IntPtr.Zero) return null;
                old = SelectObject(memory, bitmap);
                if (!BitBlt(memory, 0, 0, width, height, screen, left, top, SRCCOPY)) return null;
                SelectObject(memory, old);
                old = IntPtr.Zero;
                var info = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)), biWidth = width, biHeight = -height,   // negative: top row first
                    biPlanes = 1, biBitCount = 32, biCompression = 0
                };
                var pixels = new int[width * height];   // 0x00RRGGBB as BGRA bytes in memory
                if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, DIB_RGB_COLORS) == 0) return null;
                for (int i = 0; i < pixels.Length; i++) pixels[i] &= 0xFFFFFF;
                return new PixelImage(width, height, pixels);
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(memory, old);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
