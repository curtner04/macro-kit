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
        readonly HashSet<ushort> heldAtStart = new HashSet<ushort>();                // keys already down at Start, ignored until released
        int injectedIgnored;

        public Recorder(IClock clock)
        {
            if (clock == null) throw new ArgumentNullException("clock");
            this.clock = clock;
        }

        public bool IsRecording
        {
            get { lock (gate) return recording; }
        }

        /// <summary>
        /// Key presses, clicks and wheel turns from other programs (remote-control tools, the on-screen keyboard, key remappers)
        /// that were left out of the recording since the last Start. They are never recorded, so a take made only of them is empty.
        /// </summary>
        public int InjectedIgnored
        {
            get { lock (gate) return injectedIgnored; }
        }

        public void Start(bool recordMouseMoves)
        {
            Start(recordMouseMoves, null);
        }

        /// <summary>
        /// Starts recording. <paramref name="heldKeys"/> are the keys held down at this moment (usually the record hotkey): their
        /// auto-repeat and their release are ignored, so holding the hotkey a little long does not type its last key into the take.
        /// </summary>
        public void Start(bool recordMouseMoves, IEnumerable<ushort> heldKeys)
        {
            lock (gate)
            {
                recording = true;
                recordMoves = recordMouseMoves;
                startMs = clock.NowMs;
                events = new List<RecEvent>();
                haveWritten = false;
                injectedIgnored = 0;
                keysDown.Clear();
                buttonsDown.Clear();
                buttonsDropped.Clear();
                heldAtStart.Clear();
                if (heldKeys != null) foreach (ushort vk in heldKeys) heldAtStart.Add(vk);
            }
        }

        public void OnKey(KeyEvent e)
        {
            if (e.Injected)
            {
                if (e.Down) CountInjected();
                return;
            }
            lock (gate)
            {
                if (!recording) return;
                if (heldAtStart.Count > 0 && heldAtStart.Contains(e.Vk))
                {
                    // Still the press that was going on when recording began; its first release ends it.
                    if (!e.Down) heldAtStart.Remove(e.Vk);
                    return;
                }
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
            if (e.Injected)
            {
                if (e.Kind != MouseEventKind.Move && e.Kind != MouseEventKind.Up) CountInjected();
                return;
            }
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
        /// Ends the recording and returns it (an empty one when nothing was recording). Pass the hotkey that stopped it, if one
        /// did: its last press at the end of the take is removed (generic and left/right modifier forms alike). Pass nothing when
        /// a button stopped it, so the person's own last keys stay. Several hotkeys are removed one after the other.
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
                heldAtStart.Clear();
            }

            if (hotkeysToStrip != null)
            {
                foreach (Hotkey hk in hotkeysToStrip) StripTrailingHotkey(result, hk);
            }
            DropTrailingMoves(result);

            var rec = new Recording();
            rec.Name = Recording.DefaultName(DateTime.Now);
            rec.Created = Recording.NowIso();
            rec.Events = result;
            rec.DurationMs = result.Count == 0 ? 0 : Math.Max(0, result[result.Count - 1].T);
            return rec;
        }

        /// <summary>
        /// Takes back the last click of <paramref name="button"/> while recording, when nothing but moves came after it, together
        /// with the moves around it. For clicks on Macro Kit's tray icon, which is not one of its own windows. A press whose release
        /// has not come yet is removed too, and its release is then ignored. Returns false (and changes nothing) otherwise.
        /// </summary>
        public bool DropTrailingClick(MouseButton button)
        {
            lock (gate)
            {
                if (!recording) return false;
                int i = events.Count - 1;
                while (i >= 0 && events[i].Kind == RecKind.Move) i--;
                if (i < 0 || events[i].A != (int)button) return false;
                int down;
                if (events[i].Kind == RecKind.ButtonUp)
                {
                    down = i - 1;
                    while (down >= 0 && events[down].Kind == RecKind.Move) down--;
                    if (down < 0 || events[down].Kind != RecKind.ButtonDown || events[down].A != (int)button) return false;
                }
                else if (events[i].Kind == RecKind.ButtonDown && buttonsDown.Contains(button))
                {
                    down = i;
                    buttonsDown.Remove(button);
                    buttonsDropped.Add(button);
                }
                else return false;
                // The moves that took the pointer to the click go as well.
                while (down > 0 && events[down - 1].Kind == RecKind.Move) down--;
                events.RemoveRange(down, events.Count - down);
                haveWritten = false;   // the next action writes its own position again
                return true;
            }
        }

        void CountInjected()
        {
            lock (gate)
            {
                if (recording) injectedIgnored++;
            }
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

        /// <summary>Modifier family of a key (0x10 Shift, 0x11 Ctrl, 0x12 Alt, 0x5B Win), or 0 when it is not a modifier.</summary>
        static ushort Family(ushort vk)
        {
            switch (KeyNames.BaseVk(vk))
            {
                case 0x10: case 0xA0: case 0xA1: return 0x10;
                case 0x11: case 0xA2: case 0xA3: return 0x11;
                case 0x12: case 0xA4: case 0xA5: return 0x12;
                case 0x5B: case 0x5C: return 0x5B;
            }
            return 0;
        }

        static bool IsKey(RecEvent ev) { return ev.Kind == RecKind.KeyDown || ev.Kind == RecKind.KeyUp; }

        /// <summary>
        /// Removes the one press of the hotkey that stopped the recording. That is the last press of its main key, when nothing but
        /// moves and the hotkey's own keys come after it (anything else means the press is the person's own and nothing is removed),
        /// with the auto-repeats right before it, every hotkey key after it, and the hotkey modifiers pressed for it. A modifier that
        /// was released in between, or anything else, ends that walk back, so an earlier Ctrl+S stays even when Ctrl+Shift+Alt+S stopped.
        /// </summary>
        static void StripTrailingHotkey(List<RecEvent> list, Hotkey hk)
        {
            if (hk.IsEmpty || list.Count == 0) return;
            ushort main = KeyNames.BaseVk(hk.Vk);
            var families = new HashSet<ushort>();
            if ((hk.Modifiers & HotkeyModifiers.Ctrl) != 0) families.Add(0x11);
            if ((hk.Modifiers & HotkeyModifiers.Shift) != 0) families.Add(0x10);
            if ((hk.Modifiers & HotkeyModifiers.Alt) != 0) families.Add(0x12);
            if ((hk.Modifiers & HotkeyModifiers.Win) != 0) families.Add(0x5B);
            Func<RecEvent, bool> isMain = ev => IsKey(ev) && KeyNames.BaseVk((ushort)ev.A) == main;
            Func<RecEvent, bool> isHotkeyKey = ev => isMain(ev) || (IsKey(ev) && families.Contains(Family((ushort)ev.A)));

            int start = -1;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                RecEvent ev = list[i];
                if (ev.Kind == RecKind.Move) continue;
                if (!isHotkeyKey(ev)) return;
                if (ev.Kind == RecKind.KeyDown && isMain(ev)) { start = i; break; }
            }
            if (start < 0) return;

            // A hotkey held a moment repeats its main key; those downs belong to the same press.
            for (int i = start - 1; i >= 0; i--)
            {
                if (list[i].Kind == RecKind.Move) continue;
                if (list[i].Kind == RecKind.KeyDown && isMain(list[i])) { start = i; continue; }
                break;
            }

            for (int i = list.Count - 1; i >= start; i--)
            {
                if (isHotkeyKey(list[i])) list.RemoveAt(i);
            }

            var released = new HashSet<ushort>();
            for (int i = start - 1; i >= 0; i--)
            {
                RecEvent ev = list[i];
                if (ev.Kind == RecKind.Move) continue;
                ushort family = IsKey(ev) ? Family((ushort)ev.A) : (ushort)0;
                if (family == 0 || !families.Contains(family)) break;
                if (ev.Kind == RecKind.KeyUp) released.Add(family);
                else if (!released.Contains(family)) list.RemoveAt(i);
            }
        }
    }
}
