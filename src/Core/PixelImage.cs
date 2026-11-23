using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacroKit.Core
{
    /// <summary>
    /// 1.13.0: a picture as 0xRRGGBB values, row by row. Saved pictures ({{WAITIMAGE}}, {{CLICKIMAGE}}) live as PNG files in
    /// &lt;data&gt;\images, and the screen is read into one of these to look for them.
    /// </summary>
    public sealed class PixelImage
    {
        public readonly int Width, Height;
        public readonly int[] Pixels;

        /// <summary>The smallest and largest side a saved picture may have, in pixels.</summary>
        public const int MinSide = 4, MaxSide = 256;

        public PixelImage(int width, int height, int[] pixels)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("width", "A picture needs a positive size.");
            if (pixels == null || pixels.Length != width * height) throw new ArgumentException("The pixels don't fit the size.", "pixels");
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int this[int x, int y] { get { return Pixels[y * Width + x]; } }

        /// <summary>The part from (left, top), width by height, clipped to this picture. Null when nothing is left.</summary>
        public PixelImage Crop(int left, int top, int width, int height)
        {
            int x0 = Math.Max(0, left), y0 = Math.Max(0, top);
            int x1 = Math.Min(Width, left + width), y1 = Math.Min(Height, top + height);
            if (x1 <= x0 || y1 <= y0) return null;
            var pixels = new int[(x1 - x0) * (y1 - y0)];
            for (int y = y0; y < y1; y++) Array.Copy(Pixels, y * Width + x0, pixels, (y - y0) * (x1 - x0), x1 - x0);
            return new PixelImage(x1 - x0, y1 - y0, pixels);
        }

        // ---- finding one picture inside another ------------------------------------------------------------------------------

        /// <summary>How far each of red, green and blue may be off for a pixel to count as the same.</summary>
        public const int Tolerance = 24;

        /// <summary>The share of a picture's pixels that may differ (a blinking caret, a hover glow) and it still counts as found: 1 in 50, and at least one.</summary>
        public const int AllowedMismatchDivisor = 50;

        static bool Near(int a, int b)
        {
            int d = ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF);
            if (d > Tolerance || d < -Tolerance) return false;
            d = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF);
            if (d > Tolerance || d < -Tolerance) return false;
            d = (a & 0xFF) - (b & 0xFF);
            return d <= Tolerance && d >= -Tolerance;
        }

        /// <summary>
        /// Looks for <paramref name="needle"/> in this picture, top row first, and gives the top-left corner of the first place it
        /// appears. A few spread-out pixels are checked before the whole picture, so most places are ruled out at once.
        /// </summary>
        public bool Find(PixelImage needle, out int foundX, out int foundY)
        {
            foundX = foundY = -1;
            if (needle == null || needle.Width > Width || needle.Height > Height) return false;
            int allowed = Math.Max(1, needle.Pixels.Length / AllowedMismatchDivisor);
            int[] samples = SamplePoints(needle);
            int anchor = samples[0];
            int ax = anchor % needle.Width, ay = anchor / needle.Width, anchorColour = needle.Pixels[anchor];
            int sampleAllowed = Math.Max(1, samples.Length / 8);
            for (int y = 0; y <= Height - needle.Height; y++)
            {
                for (int x = 0; x <= Width - needle.Width; x++)
                {
                    if (!Near(Pixels[(y + ay) * Width + x + ax], anchorColour)) continue;
                    int off = 0;
                    for (int i = 1; i < samples.Length && off <= sampleAllowed; i++)
                    {
                        int s = samples[i];
                        if (!Near(Pixels[(y + s / needle.Width) * Width + x + s % needle.Width], needle.Pixels[s])) off++;
                    }
                    if (off > sampleAllowed) continue;
                    if (Matches(needle, x, y, allowed)) { foundX = x; foundY = y; return true; }
                }
            }
            return false;
        }

        bool Matches(PixelImage needle, int left, int top, int allowed)
        {
            int off = 0;
            for (int y = 0; y < needle.Height; y++)
            {
                int row = (top + y) * Width + left, nrow = y * needle.Width;
                for (int x = 0; x < needle.Width; x++)
                {
                    if (!Near(Pixels[row + x], needle.Pixels[nrow + x]) && ++off > allowed) return false;
                }
            }
            return true;
        }

        // Up to 25 pixels on a 5 by 5 grid, the first one taken from the middle and preferring pixels that differ from their
        // neighbours' colour, so a plain background doesn't let every place through.
        static int[] SamplePoints(PixelImage p)
        {
            var points = new List<int>();
            int centre = (p.Height / 2) * p.Width + p.Width / 2;
            points.Add(centre);
            for (int gy = 0; gy < 5; gy++)
                for (int gx = 0; gx < 5; gx++)
                {
                    int x = (p.Width - 1) * gx / 4, y = (p.Height - 1) * gy / 4;
                    int index = y * p.Width + x;
                    if (!points.Contains(index)) points.Add(index);
                }
            // Put a pixel unlike the centre second, so the first two checks rule out a plain background.
            for (int i = 1; i < points.Count; i++)
            {
                if (!Near(p.Pixels[points[i]], p.Pixels[centre]))
                {
                    int swap = points[1]; points[1] = points[i]; points[i] = swap;
                    break;
                }
            }
            return points.ToArray();
        }

        // ---- PNG files ----------------------------------------------------------------------------------------------------------

        /// <summary>A WPF picture of this one (for showing it in the app).</summary>
        public BitmapSource ToBitmap()
        {
            var bgra = new int[Pixels.Length];
            for (int i = 0; i < Pixels.Length; i++) bgra[i] = unchecked((int)0xFF000000) | (Pixels[i] & 0xFFFFFF);
            BitmapSource source = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, bgra, Width * 4);
            source.Freeze();
            return source;
        }

        public byte[] ToPng()
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(ToBitmap()));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        /// <summary>Reads a PNG (or any picture WPF reads). Throws InvalidDataException when it isn't one or is too big.</summary>
        public static PixelImage FromPng(byte[] bytes)
        {
            try
            {
                using (var stream = new MemoryStream(bytes))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    BitmapSource frame = decoder.Frames[0];
                    if (frame.PixelWidth > MaxSide * 4 || frame.PixelHeight > MaxSide * 4) throw new InvalidDataException("The picture is too big.");
                    var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                    var pixels = new int[frame.PixelWidth * frame.PixelHeight];
                    converted.CopyPixels(pixels, frame.PixelWidth * 4, 0);
                    for (int i = 0; i < pixels.Length; i++) pixels[i] &= 0xFFFFFF;
                    return new PixelImage(frame.PixelWidth, frame.PixelHeight, pixels);
                }
            }
            catch (InvalidDataException) { throw; }
            catch (Exception e) { throw new InvalidDataException("It isn't a picture Macro Kit can read.", e); }
        }

        // ---- names --------------------------------------------------------------------------------------------------------------

        static readonly Regex NamePattern = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 _\-]{0,39}$", RegexOptions.Compiled);

        /// <summary>A picture name: up to 40 letters, numbers, spaces, dashes and underscores, starting with a letter or number.</summary>
        public static bool IsValidName(string name)
        {
            return name != null && NamePattern.IsMatch(name) && name == name.Trim();
        }

        public const string NameRule = "A picture's name can use up to 40 letters, numbers, spaces, dashes and underscores.";
    }
}
