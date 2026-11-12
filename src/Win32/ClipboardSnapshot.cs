using System;
using System.Collections.Generic;

namespace MacroKit.Win32
{
    /// <summary>One clipboard format and its bytes.</summary>
    internal sealed class ClipboardItem
    {
        public uint Format;
        public byte[] Data;

        public ClipboardItem(uint format, byte[] data)
        {
            Format = format;
            Data = data ?? new byte[0];
        }
    }

    /// <summary>
    /// A copy of the clipboard's global-memory formats, so a temporary paste can put the user's clipboard back exactly
    /// (pictures, files, rich text...). Plain data; the Win32 calls live in <see cref="Win32Clipboard"/>.
    /// </summary>
    internal sealed class ClipboardSnapshot
    {
        /// <summary>Everything larger than this is not copied; the snapshot falls back to the text alone.</summary>
        public const long MaxBytes = 64L * 1024 * 1024;

        public readonly List<ClipboardItem> Items = new List<ClipboardItem>();

        public long TotalBytes
        {
            get
            {
                long total = 0;
                foreach (ClipboardItem item in Items) total += item.Data.Length;
                return total;
            }
        }

        /// <summary>
        /// The snapshot to keep: all items when they fit in <paramref name="maxBytes"/>, otherwise only the Unicode text
        /// (kept even when it alone is larger, because losing the text is worse than holding some memory for a moment).
        /// </summary>
        public static ClipboardSnapshot Limit(IList<ClipboardItem> items, long maxBytes)
        {
            var snapshot = new ClipboardSnapshot();
            long total = 0;
            foreach (ClipboardItem item in items) total += item.Data.Length;
            if (total <= maxBytes)
            {
                snapshot.Items.AddRange(items);
                return snapshot;
            }
            foreach (ClipboardItem item in items)
            {
                if (item.Format == Native.CF_UNICODETEXT) snapshot.Items.Add(item);
            }
            return snapshot;
        }
    }

    /// <summary>Which clipboard formats can be copied as plain memory, and the markers that keep data out of history and cloud sync.</summary>
    internal static class ClipboardFormats
    {
        public const string ExcludeFromMonitor = "ExcludeClipboardContentFromMonitorProcessing";
        public const string CanIncludeInHistory = "CanIncludeInClipboardHistory";
        public const string CanUploadToCloud = "CanUploadToCloudClipboard";

        /// <summary>The formats Macro Kit sets itself; they are never copied into a snapshot (they are added again when restoring).</summary>
        public static bool IsPrivacyMarker(string registeredName)
        {
            return string.Equals(registeredName, ExcludeFromMonitor, StringComparison.Ordinal)
                || string.Equals(registeredName, CanIncludeInHistory, StringComparison.Ordinal)
                || string.Equals(registeredName, CanUploadToCloud, StringComparison.Ordinal);
        }

        /// <summary>
        /// False for formats whose clipboard handle is a GDI object or other non-memory handle (bitmap, palette, metafiles,
        /// owner-display, the GDI-object range), for the privacy markers, and for COM-backed formats whose bytes are only
        /// pointers into the source program. Pictures still come back through CF_DIB / CF_DIBV5, which are plain memory.
        /// </summary>
        public static bool IsCopyable(uint format, string registeredName)
        {
            switch (format)
            {
                case Native.CF_BITMAP:
                case Native.CF_METAFILEPICT:
                case Native.CF_PALETTE:
                case Native.CF_ENHMETAFILE:
                case Native.CF_OWNERDISPLAY:
                case Native.CF_DSPBITMAP:
                case Native.CF_DSPMETAFILEPICT:
                case Native.CF_DSPENHMETAFILE:
                    return false;
            }
            if (format >= Native.CF_GDIOBJFIRST && format <= Native.CF_GDIOBJLAST) return false;
            if (registeredName != null)
            {
                if (IsPrivacyMarker(registeredName)) return false;
                if (string.Equals(registeredName, "DataObject", StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(registeredName, "Ole Private Data", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        /// <summary>The 4-byte zero value used for the "CanIncludeInClipboardHistory" and "CanUploadToCloudClipboard" markers.</summary>
        public static byte[] ZeroDword()
        {
            return new byte[4];
        }

        /// <summary>The data for "ExcludeClipboardContentFromMonitorProcessing" (any value works; a single DWORD of 1).</summary>
        public static byte[] ExcludeMarkerData()
        {
            return new byte[] { 1, 0, 0, 0 };
        }
    }
}
