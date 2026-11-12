using System;
using System.Collections.Generic;

namespace MacroKit.Recording
{
    // These usings are inside the namespace on purpose: MacroKit.Recording is itself a namespace named like the
    // MacroKit.Core.Recording type, and a using at file level would lose the lookup to the namespace.
    using MacroKit.Core;

    /// <summary>
    /// Turns hook events into a <see cref="Recording"/>. OnKey/OnMouse are called from the hook thread, Start/Stop from the
    /// UI thread; everything is guarded by one lock and does no I/O. Times are milliseconds since Start on the injected clock.
    /// </summary>
    public sealed class Recorder
    {
        /// <summary>Key auto-repeat down events are kept (like TinyTask) but at most this often per key.</summary>
        const int AutoRepeatThrottleMs = 30;

        /// <summary>Mouse moves closer together than this are merged into one event.</summary>
        const int MoveCoalesceMs = 8;

        readonly IClock clock;
        readonly object gate = new object();

        bool recording;
        bool recordMoves;
        long startMs;
        List<RecEvent> events = new List<RecEvent>();

        // The last position the recording itself puts the cursor at.
        bool haveWritten;
        int writtenX, writtenY;

        readonly Dictionary<ushort, int> keysDown = new Dictionary<ushort, int>();   // key -> time of its last recorded down
        readonly HashSet<MouseButton> buttonsDown = new HashSet<MouseButton>();      // recorded downs waiting for their up
        readonly HashSet<MouseButton> buttonsDropped = new HashSet<MouseButton>();   // downs dropped over our own window

        public Recorder(IClock clock)
        {
            if (clock == null) throw new ArgumentNullException("clock");
            this.clock = clock;
        }

        public bool IsRecording
        {
            get { lock (gate) return recording; }
        }

        public void Start(bool recordMouseMoves)
        {
            lock (gate)
            {
                recording = true;
                recordMoves = recordMouseMoves;
                startMs = clock.NowMs;
                events = new List<RecEvent>();
                haveWritten = false;
                keysDown.Clear();
                buttonsDown.Clear();
                buttonsDropped.Clear();
            }
        }

        public void OnKey(KeyEvent e)
        {
            if (e.Injected) return;
            lock (gate)
            {
                if (!recording) return;
                int t = Elapsed();
                int scan = e.Scan | (e.Extended ? 0x10000 : 0);
                if (e.Down)
                {
                    int lastDown;
                    if (keysDown.TryGetValue(e.Vk, out lastDown))
                    {
                        if (t - lastDown < AutoRepeatThrottleMs) return;   // auto-repeat burst
                    }
                    keysDown[e.Vk] = t;
                    events.Add(new RecEvent(t, RecKind.KeyDown, e.Vk, scan));
                }
                else
                {
                    // An up whose down was not recorded is a key that was already held when recording began
                    // (usually the record hotkey itself). Replaying it would press nothing and release a key the user holds.
                    if (!keysDown.Remove(e.Vk)) return;
                    events.Add(new RecEvent(t, RecKind.KeyUp, e.Vk, scan));
                }
            }
        }

        public void OnMouse(MouseEvent e)
        {
            if (e.Injected) return;
            lock (gate)
            {
                if (!recording) return;
                int t = Elapsed();
                switch (e.Kind)
                {
                    case MouseEventKind.Move:
                        if (recordMoves) AddMove(t, e.X, e.Y);
                        break;

                    case MouseEventKind.Down:
                        if (e.OverOwnWindow)
                        {
                            // The click that pressed Record/Stop in our own window. Drop it together with its up.
                            buttonsDropped.Add(e.Button);
                            break;
                        }
                        buttonsDropped.Remove(e.Button);
                        MoveBeforeAction(t, e.X, e.Y);
                        buttonsDown.Add(e.Button);
                        events.Add(new RecEvent(t, RecKind.ButtonDown, (int)e.Button, 0));
                        break;

                    case MouseEventKind.Up:
                        if (buttonsDropped.Remove(e.Button)) break;
                        if (!buttonsDown.Remove(e.Button)) break;   // held before recording began
                        MoveBeforeAction(t, e.X, e.Y);
                        events.Add(new RecEvent(t, RecKind.ButtonUp, (int)e.Button, 0));
                        break;

                    case MouseEventKind.Wheel:
                    case MouseEventKind.HWheel:
                        if (e.OverOwnWindow) break;
                        MoveBeforeAction(t, e.X, e.Y);
                        events.Add(new RecEvent(t, e.Kind == MouseEventKind.Wheel ? RecKind.Wheel : RecKind.HWheel, e.Delta, 0));
                        break;
                }
            }
        }

        /// <summary>
        /// Ends the recording and returns it (an empty one when nothing was recording). Key events at the end that belong to
        /// the given hotkeys (the keys used to stop recording) are removed, generic and left/right modifier forms alike.
        /// </summary>
        public Recording Stop(IEnumerable<Hotkey> hotkeysToStrip)
        {
            List<RecEvent> result;
            lock (gate)
            {
                recording = false;
                result = events;
                events = new List<RecEvent>();
                keysDown.Clear();
                buttonsDown.Clear();
                buttonsDropped.Clear();
            }

            StripTrailingHotkeys(result, hotkeysToStrip);
            DropTrailingMoves(result);

            var rec = new Recording();
            rec.Name = Recording.DefaultName(DateTime.Now);
            rec.Created = Recording.NowIso();
            rec.Events = result;
            rec.DurationMs = result.Count == 0 ? 0 : Math.Max(0, result[result.Count - 1].T);
            return rec;
        }

        int Elapsed()
        {
            long t = clock.NowMs - startMs;
            if (t < 0) return 0;
            return t > int.MaxValue ? int.MaxValue : (int)t;
        }

        /// <summary>
        /// Writes a move. A move that comes less than <see cref="MoveCoalesceMs"/> after the previous event, when that is a move
        /// too, only updates that move's position, so a fast mouse does not store hundreds of events per second.
        /// The newest position is always kept, and a click, wheel turn or key still gets its exact position from MoveBeforeAction.
        /// </summary>
        void AddMove(int t, int x, int y)
        {
            if (haveWritten && writtenX == x && writtenY == y) return;
            haveWritten = true;
            writtenX = x;
            writtenY = y;
            int last = events.Count - 1;
            if (last >= 0 && events[last].Kind == RecKind.Move && t - events[last].T < MoveCoalesceMs)
                events[last] = new RecEvent(events[last].T, RecKind.Move, x, y);
            else
                events.Add(new RecEvent(t, RecKind.Move, x, y));
        }

        /// <summary>Buttons and wheel carry no position, so the cursor is moved to the click point first when needed.</summary>
        void MoveBeforeAction(int t, int x, int y)
        {
            if (haveWritten && writtenX == x && writtenY == y) return;
            haveWritten = true;
            writtenX = x;
            writtenY = y;
            events.Add(new RecEvent(t, RecKind.Move, x, y));
        }

        /// <summary>
        /// Removes the moves after the last button, wheel or key event (the cursor wandering toward Macro Kit's own Stop button,
        /// pill or tray) so the replay does not end with a dead tail. Nothing but moves in the list leaves it empty.
        /// </summary>
        static void DropTrailingMoves(List<RecEvent> list)
        {
            int keep = list.Count;
            while (keep > 0 && list[keep - 1].Kind == RecKind.Move) keep--;
            if (keep < list.Count) list.RemoveRange(keep, list.Count - keep);
        }

        static void StripTrailingHotkeys(List<RecEvent> list, IEnumerable<Hotkey> hotkeys)
        {
            if (hotkeys == null || list.Count == 0) return;
            var strip = new HashSet<ushort>();
            foreach (Hotkey hk in hotkeys)
            {
                if (hk.IsEmpty) continue;
                if ((hk.Modifiers & HotkeyModifiers.Ctrl) != 0) { strip.Add(0x11); strip.Add(0xA2); strip.Add(0xA3); }
                if ((hk.Modifiers & HotkeyModifiers.Shift) != 0) { strip.Add(0x10); strip.Add(0xA0); strip.Add(0xA1); }
                if ((hk.Modifiers & HotkeyModifiers.Alt) != 0) { strip.Add(0x12); strip.Add(0xA4); strip.Add(0xA5); }
                if ((hk.Modifiers & HotkeyModifiers.Win) != 0) { strip.Add(0x5B); strip.Add(0x5C); }
                if (hk.Vk != 0) strip.Add(KeyNames.BaseVk(hk.Vk));
            }
            if (strip.Count == 0) return;

            // Walk back from the end: hotkey key events go, moves are stepped over, anything else ends the trailing run.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                RecEvent ev = list[i];
                if (ev.Kind == RecKind.Move) continue;
                if ((ev.Kind == RecKind.KeyDown || ev.Kind == RecKind.KeyUp) && strip.Contains(KeyNames.BaseVk((ushort)ev.A)))
                {
                    list.RemoveAt(i);
                    continue;
                }
                break;
            }
        }
    }
}
