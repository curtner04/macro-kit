using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MacroKit.Core;

namespace MacroKit.Win32
{
    /// <summary>
    /// The Windows clipboard, usable from any thread (no STA or WPF Clipboard class needed).
    /// The clipboard is a shared resource that other programs hold briefly, so opening it is retried.
    /// Every OpenClipboard is paired with a CloseClipboard in a finally block, and every memory block that the clipboard did not take is freed.
    /// </summary>
    public sealed class Win32Clipboard : IClipboard
    {
        const int OpenAttempts = 10;
        const int OpenRetryMs = 20;

        // SetClipboardData fails when the clipboard owner is NULL (documented), so writing needs a real window as owner.
        // It lives on its own thread with a message loop, because a window dies with the thread that created it.
        static readonly object ownerGate = new object();
        static IntPtr ownerWindow;

        /// <summary>A block to put on the clipboard. Required items make the whole write fail when Windows refuses them.</summary>
        sealed class Outgoing
        {
            public uint Format;
            public byte[] Data;
            public bool Required;
            public bool Marker;     // one of the privacy formats, not user data
            public IntPtr Handle;   // the block; set to zero once the clipboard owns it
        }

        /// <summary>The current text, or null when the clipboard holds no text. Throws InvalidOperationException when another program keeps the clipboard locked.</summary>
        public string GetText()
        {
            Open(IntPtr.Zero);   // reading needs no owner
            try
            {
                if (!Native.IsClipboardFormatAvailable(Native.CF_UNICODETEXT)) return null;
                IntPtr handle = Native.GetClipboardData(Native.CF_UNICODETEXT);
                if (handle == IntPtr.Zero) return null;
                IntPtr memory = Native.GlobalLock(handle);
                if (memory == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(memory); }
                finally { Native.GlobalUnlock(handle); }
            }
            finally { Native.CloseClipboard(); }
        }

        /// <summary>Replaces the clipboard with this text (null becomes empty). Throws InvalidOperationException when it cannot.</summary>
        public void SetText(string text)
        {
            Write(new List<Outgoing> { TextItem(text) }, false);
        }

        /// <summary>
        /// Like SetText, and in the same open session marks the text "do not monitor", "not for clipboard history" and "not for
        /// cloud sync" (the formats Windows documents for password managers), so it stays out of Win+V.
        /// </summary>
        public void SetTextPrivate(string text)
        {
            Write(new List<Outgoing> { TextItem(text) }, true);
        }

        /// <summary>The clipboard's change counter. Changes whenever anything puts new data on it.</summary>
        public uint SequenceNumber
        {
            get { return Native.GetClipboardSequenceNumber(); }
        }

        /// <summary>
        /// Copies every format stored in global memory (text, rich text, HTML, files, CF_DIB pictures...). Formats backed by GDI handles are
        /// skipped. If the copy would pass 64 MB only the text is kept.
        /// </summary>
        public object Snapshot()
        {
            Open(IntPtr.Zero);
            try
            {
                var found = new List<ClipboardItem>();
                long total = 0;
                bool overflow = false;
                uint format = 0;
                while ((format = Native.EnumClipboardFormats(format)) != 0)
                {
                    string name = FormatName(format);
                    if (!ClipboardFormats.IsCopyable(format, name)) continue;
                    ClipboardItem item = CopyFormat(format, ref total, ref overflow);
                    if (item != null) found.Add(item);
                }
                // Over the cap: keep the text alone (Limit does that, and a copy that fits passes through unchanged).
                return ClipboardSnapshot.Limit(found, overflow ? 0 : ClipboardSnapshot.MaxBytes);
            }
            finally { Native.CloseClipboard(); }
        }

        /// <summary>Puts a snapshot back, replacing whatever is there. The restored data is kept out of history and cloud sync, as it was before.</summary>
        public void Restore(object snapshot)
        {
            var copy = snapshot as ClipboardSnapshot;
            var items = new List<Outgoing>();
            if (copy != null)
            {
                foreach (ClipboardItem item in copy.Items)
                    items.Add(new Outgoing { Format = item.Format, Data = item.Data, Required = false });
            }
            bool hadData = items.Count > 0;   // Write adds the privacy markers to the list, so count before it
            Write(items, hadData);            // an empty clipboard is simply emptied again (no markers on nothing)
            if (hadData && !AnyStored(items))
                throw new InvalidOperationException("Windows would not put the previous clipboard contents back.");
        }

        // ---------------- writing ----------------

        static Outgoing TextItem(string text)
        {
            text = text ?? "";
            return new Outgoing { Format = Native.CF_UNICODETEXT, Data = Encoding.Unicode.GetBytes(text + "\0"), Required = true };
        }

        static bool AnyStored(List<Outgoing> items)
        {
            foreach (Outgoing item in items)
            {
                if (!item.Marker && item.Format != 0 && item.Handle == IntPtr.Zero) return true;   // the clipboard took it
            }
            return false;
        }

        // Allocates every block before the clipboard is taken, so it is held for as short a time as possible.
        static void Write(List<Outgoing> items, bool markPrivate)
        {
            if (markPrivate)
            {
                items.Add(new Outgoing { Format = Register(ClipboardFormats.ExcludeFromMonitor), Data = ClipboardFormats.ExcludeMarkerData(), Marker = true });
                items.Add(new Outgoing { Format = Register(ClipboardFormats.CanIncludeInHistory), Data = ClipboardFormats.ZeroDword(), Marker = true });
                items.Add(new Outgoing { Format = Register(ClipboardFormats.CanUploadToCloud), Data = ClipboardFormats.ZeroDword(), Marker = true });
            }
            try
            {
                foreach (Outgoing item in items)
                {
                    if (item.Format == 0) continue;   // a marker format Windows would not register: skip it
                    item.Handle = AllocateBlock(item.Data);
                }

                Open(EnsureOwner());
                try
                {
                    if (!Native.EmptyClipboard())
                        throw new InvalidOperationException("Windows would not clear the clipboard.", new Win32Exception(Marshal.GetLastWin32Error()));
                    foreach (Outgoing item in items)
                    {
                        if (item.Handle == IntPtr.Zero) continue;
                        if (Native.SetClipboardData(item.Format, item.Handle) != IntPtr.Zero)
                        {
                            item.Handle = IntPtr.Zero;   // the clipboard owns the block now
                            continue;
                        }
                        int code = Marshal.GetLastWin32Error();
                        if (item.Required)
                            throw new InvalidOperationException("Windows would not put the text on the clipboard.", new Win32Exception(code));
                        Log.Warn("The clipboard refused format " + item.Format + " (error " + code + ").");
                    }
                }
                finally { Native.CloseClipboard(); }
            }
            finally
            {
                foreach (Outgoing item in items)
                {
                    if (item.Handle != IntPtr.Zero) { Native.GlobalFree(item.Handle); item.Handle = IntPtr.Zero; }
                }
            }
        }

        static IntPtr AllocateBlock(byte[] data)
        {
            IntPtr handle = Native.GlobalAlloc(Native.GMEM_MOVEABLE, new UIntPtr((uint)Math.Max(1, data.Length)));
            if (handle == IntPtr.Zero) throw new InvalidOperationException("Windows is out of memory for the clipboard.", new Win32Exception(Marshal.GetLastWin32Error()));
            bool ok = false;
            try
            {
                IntPtr memory = Native.GlobalLock(handle);
                if (memory == IntPtr.Zero) throw new InvalidOperationException("Windows would not prepare the clipboard data.", new Win32Exception(Marshal.GetLastWin32Error()));
                try { if (data.Length > 0) Marshal.Copy(data, 0, memory, data.Length); }
                finally { Native.GlobalUnlock(handle); }
                ok = true;
                return handle;
            }
            finally
            {
                if (!ok) Native.GlobalFree(handle);
            }
        }

        // ---------------- reading ----------------

        // Called with the clipboard open. Returns null when the format cannot be read as plain memory.
        static ClipboardItem CopyFormat(uint format, ref long total, ref bool overflow)
        {
            // Past the cap everything but the text is dropped anyway, so stop reading other formats (the text is always read).
            if (overflow && format != Native.CF_UNICODETEXT) return null;
            IntPtr handle = Native.GetClipboardData(format);
            if (handle == IntPtr.Zero) return null;
            long size = (long)Native.GlobalSize(handle).ToUInt64();
            if (size <= 0) return null;
            if (format != Native.CF_UNICODETEXT && total + size > ClipboardSnapshot.MaxBytes)
            {
                overflow = true;
                return null;
            }
            IntPtr memory = Native.GlobalLock(handle);
            if (memory == IntPtr.Zero) return null;
            try
            {
                var data = new byte[size];
                Marshal.Copy(memory, data, 0, (int)size);
                total += size;
                return new ClipboardItem(format, data);
            }
            finally { Native.GlobalUnlock(handle); }
        }

        static string FormatName(uint format)
        {
            if (format < 0xC000) return null;   // predefined and private formats have no registered name
            var name = new StringBuilder(128);
            return Native.GetClipboardFormatNameW(format, name, name.Capacity) > 0 ? name.ToString() : null;
        }

        static uint Register(string name)
        {
            uint format = Native.RegisterClipboardFormatW(name);
            if (format == 0) Log.Warn("Windows would not register the clipboard format " + name + ".");
            return format;
        }

        // ---------------- plumbing ----------------

        static IntPtr EnsureOwner()
        {
            lock (ownerGate)
            {
                if (ownerWindow != IntPtr.Zero) return ownerWindow;
                var ready = new ManualResetEvent(false);
                var thread = new Thread(() =>
                {
                    // A message-only window of the predefined STATIC class: no window procedure of our own is needed.
                    ownerWindow = Native.CreateWindowExW(0, "STATIC", "Macro Kit clipboard", 0, 0, 0, 0, 0, Native.HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    ready.Set();
                    if (ownerWindow == IntPtr.Zero) return;
                    Native.MSG msg;
                    while (Native.GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
                    {
                        Native.TranslateMessage(ref msg);
                        Native.DispatchMessageW(ref msg);
                    }
                }) { IsBackground = true, Name = "Macro Kit clipboard owner" };
                thread.Start();
                ready.WaitOne(3000);
                ready.Close();
                return ownerWindow;
            }
        }

        static void Open(IntPtr owner)
        {
            for (int attempt = 0; attempt < OpenAttempts; attempt++)
            {
                if (Native.OpenClipboard(owner)) return;
                Thread.Sleep(OpenRetryMs);
            }
            throw new InvalidOperationException("Another program is using the clipboard right now.", new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }
}
