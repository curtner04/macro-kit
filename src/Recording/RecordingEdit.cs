using System;
using System.Collections.Generic;
using System.Linq;
using MacroKit.Core;
using Rec = MacroKit.Core.Recording;

namespace MacroKit.Recording
{
    /// <summary>
    /// 1.11.0: edits to a recording's event list (cut the start and end, shorten long pauses). Each returns a new recording with
    /// the same id, name and windows, and leaves the one it was given alone, so the caller can keep it for Undo.
    /// </summary>
    public static class RecordingEdit
    {
        /// <summary>The recording's length in ms: its saved duration, or its last event when that is later.</summary>
        public static int Length(Rec rec)
        {
            if (rec == null) return 0;
            int last = rec.Events == null || rec.Events.Count == 0 ? 0 : rec.Events.Max(e => e.T);
            return Math.Max(Math.Max(0, rec.DurationMs), last);
        }

        /// <summary>
        /// Keeps only what happened from <paramref name="startMs"/> to <paramref name="endMs"/>, moved to start at 0. The pointer
        /// starts where it was at the cut (and in the window it was in); a key or button already down at the cut has its release
        /// dropped, and one still down at the end is released there, so nothing is left held.
        /// </summary>
        public static Rec Trim(Rec rec, int startMs, int endMs)
        {
            if (rec == null) throw new ArgumentNullException("rec");
            int length = Length(rec);
            int start = Math.Max(0, Math.Min(startMs, length));
            int end = Math.Max(start, Math.Min(endMs, length));
            var output = new List<RecEvent>();
            var heldBefore = new HashSet<long>();   // keys and buttons pressed before the cut
            var held = new HashSet<long>();          // pressed inside, not released yet
            var keyScan = new Dictionary<long, int>();
            bool havePos = false;
            int x = 0, y = 0;
            RecEvent? window = null;
            bool begun = false;

            foreach (RecEvent e in rec.Events)
            {
                if (e.T < start)
                {
                    switch (e.Kind)
                    {
                        case RecKind.Move: havePos = true; x = e.A; y = e.B; break;
                        case RecKind.Window: window = e; break;
                        case RecKind.KeyDown: case RecKind.ButtonDown: heldBefore.Add(Key(e)); break;
                        case RecKind.KeyUp: case RecKind.ButtonUp: heldBefore.Remove(Key(e)); break;
                    }
                    continue;
                }
                if (e.T > end) break;
                if (!begun)
                {
                    begun = true;
                    if (start > 0) Lead(output, window, havePos, x, y);
                }
                long k = Key(e);
                switch (e.Kind)
                {
                    case RecKind.KeyDown:
                    case RecKind.ButtonDown:
                        heldBefore.Remove(k);
                        held.Add(k);
                        if (e.Kind == RecKind.KeyDown) keyScan[k] = e.B;
                        break;
                    case RecKind.KeyUp:
                    case RecKind.ButtonUp:
                        if (heldBefore.Remove(k) && !held.Contains(k)) continue;   // pressed before the cut: its release goes too
                        held.Remove(k);
                        break;
                }
                output.Add(new RecEvent(e.T - start, e.Kind, e.A, e.B));
            }
            if (!begun && start > 0) Lead(output, window, havePos, x, y);

            // Release what is still held at the end, buttons first (as a person lets go of a drag before the keys).
            int at = end - start;
            foreach (long k in held.OrderBy(k => k >= 0 ? 0 : 1))
            {
                if (k >= 0) output.Add(new RecEvent(at, RecKind.ButtonUp, (int)k, 0));
                else { int vk = (int)(-k - 1); int scan; keyScan.TryGetValue(k, out scan); output.Add(new RecEvent(at, RecKind.KeyUp, vk, scan)); }
            }
            return Copy(rec, output, at);
        }

        // At the cut: back into the window the clicks were in, and the pointer where it was.
        static void Lead(List<RecEvent> output, RecEvent? window, bool havePos, int x, int y)
        {
            if (window.HasValue) output.Add(new RecEvent(0, RecKind.Window, window.Value.A, 0));
            if (havePos) output.Add(new RecEvent(0, RecKind.Move, x, y));
        }

        // Buttons are 0 and up, keys are -1 - vk, so the two never collide.
        static long Key(RecEvent e)
        {
            return e.Kind == RecKind.ButtonDown || e.Kind == RecKind.ButtonUp ? e.A : -1L - e.A;
        }

        /// <summary>
        /// Cuts each pause longer than <paramref name="overMs"/> down to <paramref name="toMs"/>: the time between two events, and
        /// before the first and after the last. A pause while a key or button is held down (a long press, a slow drag) is kept.
        /// <paramref name="shortened"/> says how many were cut.
        /// </summary>
        public static Rec ShortenPauses(Rec rec, int overMs, int toMs, out int shortened)
        {
            if (rec == null) throw new ArgumentNullException("rec");
            toMs = Math.Max(0, Math.Min(toMs, overMs));
            shortened = 0;
            var output = new List<RecEvent>(rec.Events.Count);
            var held = new HashSet<long>();
            int previous = 0, shift = 0;
            foreach (RecEvent e in rec.Events)
            {
                int gap = e.T - previous;
                if (gap > overMs && held.Count == 0) { shift += gap - toMs; shortened++; }
                previous = e.T;
                switch (e.Kind)
                {
                    case RecKind.KeyDown: case RecKind.ButtonDown: held.Add(Key(e)); break;
                    case RecKind.KeyUp: case RecKind.ButtonUp: held.Remove(Key(e)); break;
                }
                output.Add(new RecEvent(e.T - shift, e.Kind, e.A, e.B));
            }
            int length = Length(rec);
            int tail = length - previous;
            if (tail > overMs && held.Count == 0) { shift += tail - toMs; shortened++; }
            return Copy(rec, output, length - shift);
        }

        /// <summary>How many pauses <see cref="ShortenPauses"/> would cut.</summary>
        public static int CountLongPauses(Rec rec, int overMs)
        {
            int shortened;
            if (rec == null) return 0;
            ShortenPauses(rec, overMs, overMs, out shortened);
            return shortened;
        }

        static Rec Copy(Rec rec, List<RecEvent> events, int durationMs)
        {
            return new Rec
            {
                Schema = rec.Schema,
                Id = rec.Id,
                Name = rec.Name,
                Created = rec.Created,
                DurationMs = Math.Max(0, durationMs),
                Events = events,
                Windows = rec.Windows == null ? new List<RecWindow>() : rec.Windows.Select(w => new RecWindow
                {
                    Program = w.Program, Title = w.Title, Left = w.Left, Top = w.Top, Width = w.Width, Height = w.Height,
                }).ToList(),
            };
        }
    }
}
