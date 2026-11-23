using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MacroKit.Core;

namespace MacroKit.Macros
{
    /// <summary>
    /// Turns a recording into a macro you can read and edit: clicks with where they happened, typed text, key combinations, scrolls
    /// and the pauses between them, one step per line. Mouse movement between clicks is left out (the pointer jumps to each click);
    /// a drag keeps where it started and where it ended. Characters are read with a US keyboard layout.
    /// </summary>
    public static class RecordingToMacro
    {
        /// <summary>Pauses shorter than this are left out (the macro's own key delay covers them).</summary>
        public const int MinPauseMs = 50;
        /// <summary>Gaps shorter than this between typed characters are left out, so typed words stay together.</summary>
        public const int TypingGapMs = 400;
        const int DoubleClickMs = 500;
        const int MaxPauseMs = 3600000;

        /// <summary>
        /// The macro for a recording. <paramref name="speed"/> is its playback speed: at 2× the pauses are half as long. With
        /// <paramref name="followWindows"/>, a recording that knows its windows switches to each one ({{FOCUS}}) and clicks at spots
        /// measured from that window ({{WINDOWXY}}), so the macro still works when the window has moved.
        /// </summary>
        public static MacroDef Convert(Core.Recording recording, double speed = 1, bool followWindows = true)
        {
            if (recording == null) throw new ArgumentNullException("recording");
            string name = string.IsNullOrWhiteSpace(recording.Name) ? "Recording" : recording.Name.Trim();
            return new MacroDef
            {
                Name = name + " (from recording)",
                Script = Script(recording.Events, followWindows ? recording.Windows : null, speed),
                NewLineIsEnter = false,   // one step per line; Enter is written as {{ENTER}}
                ShowOnDeck = false,
            };
        }

        public static string Script(IList<RecEvent> events, double speed = 1)
        {
            return Script(events, null, speed);
        }

        /// <summary>With <paramref name="windows"/> (the recording's), each window's steps start with {{FOCUS}} and use {{WINDOWXY}}.</summary>
        public static string Script(IList<RecEvent> events, IList<RecWindow> windows, double speed)
        {
            var w = new Writer(speed > 0 && !double.IsNaN(speed) && !double.IsInfinity(speed) ? speed : 1);
            int x = 0, y = 0;              // where the pointer is in the recording
            bool havePos = false;
            int downX = 0, downY = 0, downT = 0;
            MouseButton downButton = MouseButton.Left;
            bool buttonDown = false;
            var held = new HashSet<ushort>();   // modifiers held right now (base vk: 0x10 Shift, 0x11 Ctrl, 0x12 Alt, 0x5B Win)
            ushort loneModifier = 0;            // a modifier pressed with nothing after it yet
            bool caps = false;
            int wheel = 0, hwheel = 0, wheelT = 0;
            var opened = new List<ushort>();    // modifiers written as {{KEYDOWN}} because the mouse was used while they were held
            var releaseAfterButton = new List<ushort>();   // opened modifiers let go while a button was down; the click or drag is written when it comes up
            // 1.13.1: Alt or Win held over several keys (Alt held while Tab is pressed three times goes to the third window) is
            // written as {{KEYDOWN}} ... {{KEYUP}}, with the keys between written without it; letting go in between would pick a
            // different window.
            var kept = new List<ushort>();

            // A click or scroll with Ctrl, Shift, Alt or Win held needs that key held in the macro too (Ctrl+click, Ctrl+wheel).
            Action<int> openHeld = t =>
            {
                foreach (ushort mod in new ushort[] { 0x11, 0x10, 0x12, 0x5B })
                {
                    if (!held.Contains(mod) || opened.Contains(mod) || kept.Contains(mod)) continue;
                    w.Step(t, MacroScript.KeyDown(mod), false);
                    opened.Add(mod);
                }
            };
            Action<int> closeOpened = t =>
            {
                foreach (ushort mod in opened.ToArray()) w.Step(t, MacroScript.KeyUp(mod), false);
                opened.Clear();
            };

            for (int i = 0; i < events.Count; i++)
            {
                RecEvent e = events[i];
                if (e.Kind != RecKind.Wheel && wheel != 0) { openHeld(wheelT); w.Scroll(wheelT, wheel, false); wheel = 0; }
                if (e.Kind != RecKind.HWheel && hwheel != 0) { openHeld(wheelT); w.Scroll(wheelT, hwheel, true); hwheel = 0; }
                switch (e.Kind)
                {
                    case RecKind.Move:
                        x = e.A; y = e.B; havePos = true;
                        break;
                    case RecKind.Window:
                        if (windows == null) break;
                        if (e.A >= 0 && e.A < windows.Count) w.Window(e.T, windows[e.A]);
                        else w.Screen();
                        break;
                    case RecKind.ButtonDown:
                    {
                        var button = (MouseButton)e.A;
                        loneModifier = 0;
                        if (havePos) w.MoveTo(e.T, x, y);
                        openHeld(e.T);
                        // The back and forward buttons only click: there is no hold form for them.
                        if (button > MouseButton.Middle) break;
                        buttonDown = true;
                        downButton = button;
                        downX = x; downY = y; downT = e.T;
                        break;
                    }
                    case RecKind.ButtonUp:
                    {
                        var button = (MouseButton)e.A;
                        if (button > MouseButton.Middle) { w.Step(e.T, MacroScript.Click(button), false); break; }
                        if (!buttonDown || button != downButton)
                        {
                            w.Step(e.T, "{{M" + ButtonWord(button) + "UP}}", false);
                            break;
                        }
                        buttonDown = false;
                        bool moved = havePos && (Math.Abs(x - downX) > 3 || Math.Abs(y - downY) > 3);
                        if (moved)
                        {
                            // A drag: press where it started, go to where it ended, let go.
                            w.Step(downT, "{{M" + ButtonWord(button) + "DOWN}}", false);
                            w.MoveTo(e.T, x, y);
                            w.Step(e.T, "{{M" + ButtonWord(button) + "UP}}", false);
                        }
                        else w.Click(downT, button, x, y);
                        foreach (ushort mod in releaseAfterButton)
                        {
                            if (opened.Remove(mod)) w.Step(e.T, MacroScript.KeyUp(mod), false);
                        }
                        releaseAfterButton.Clear();
                        break;
                    }
                    case RecKind.Wheel:
                        if (wheel == 0) wheelT = e.T;
                        wheel += e.A;
                        break;
                    case RecKind.HWheel:
                        if (hwheel == 0) wheelT = e.T;
                        hwheel += e.A;
                        break;
                    case RecKind.KeyDown:
                    {
                        ushort vk = KeyNames.BaseVk((ushort)e.A);
                        ushort mod = ModifierBase(vk);
                        if (mod != 0)
                        {
                            // Let go and pressed again during a drag: it is still held, so it is not let go after the drop.
                            if (releaseAfterButton.Remove(mod)) { held.Add(mod); loneModifier = 0; break; }
                            if (held.Add(mod)) loneModifier = held.Count == 1 ? mod : (ushort)0;
                            break;
                        }
                        loneModifier = 0;
                        closeOpened(e.T);   // a key pressed now is written with its own modifiers, which would let go of the held ones
                        if (vk == 0x14) { caps = !caps; break; }   // Caps Lock changes the letters that follow
                        foreach (ushort hold in new ushort[] { 0x12, 0x5B })
                        {
                            if (!held.Contains(hold) || kept.Contains(hold) || !AnotherKeyBeforeRelease(events, i, hold)) continue;
                            w.Step(e.T, MacroScript.KeyDown(hold), false);
                            kept.Add(hold);
                        }
                        bool shift = held.Contains(0x10);
                        bool command = held.Contains(0x11) || held.Contains(0x12) || held.Contains(0x5B);
                        char ch;
                        if (!command && TryChar(vk, shift, caps, out ch) && ch != '{' && ch != '}') w.Text(e.T, ch);
                        else w.Step(e.T, Combo(held, kept, vk), false);
                        break;
                    }
                    case RecKind.KeyUp:
                    {
                        ushort vk = KeyNames.BaseVk((ushort)e.A);
                        ushort mod = ModifierBase(vk);
                        if (mod == 0) break;
                        held.Remove(mod);
                        if (buttonDown && opened.Contains(mod))
                        {
                            if (!releaseAfterButton.Contains(mod)) releaseAfterButton.Add(mod);
                            loneModifier = 0;
                            break;
                        }
                        if (kept.Remove(mod)) { w.Step(e.T, MacroScript.KeyUp(mod), false); loneModifier = 0; break; }
                        if (opened.Remove(mod)) { w.Step(e.T, MacroScript.KeyUp(mod), false); loneModifier = 0; break; }
                        // Win or Alt pressed and let go on its own (opens Start, or the menu bar).
                        if (loneModifier == mod) w.Step(e.T, "{{" + KeyNames.MacroToken(mod) + "}}", false);
                        loneModifier = 0;
                        break;
                    }
                }
            }
            if (wheel != 0) { openHeld(wheelT); w.Scroll(wheelT, wheel, false); }
            if (hwheel != 0) { openHeld(wheelT); w.Scroll(wheelT, hwheel, true); }
            if (opened.Count > 0 && events.Count > 0) closeOpened(events[events.Count - 1].T);
            if (kept.Count > 0 && events.Count > 0) foreach (ushort mod in kept) w.Step(events[events.Count - 1].T, MacroScript.KeyUp(mod), false);
            return w.Finish();
        }

        // Whether another key (not a modifier) is pressed after event i before the modifier is let go.
        static bool AnotherKeyBeforeRelease(IList<RecEvent> events, int i, ushort mod)
        {
            for (int j = i + 1; j < events.Count; j++)
            {
                RecEvent e = events[j];
                if (e.Kind != RecKind.KeyDown && e.Kind != RecKind.KeyUp) continue;
                ushort m = ModifierBase(KeyNames.BaseVk((ushort)e.A));
                if (e.Kind == RecKind.KeyUp) { if (m == mod) return false; continue; }
                if (m == 0) return true;
            }
            return false;
        }

        static string ButtonWord(MouseButton b) { return b == MouseButton.Right ? "RIGHT" : b == MouseButton.Middle ? "MIDDLE" : "LEFT"; }

        static ushort ModifierBase(ushort vk)
        {
            switch (vk)
            {
                case 0x10: case 0xA0: case 0xA1: return 0x10;
                case 0x11: case 0xA2: case 0xA3: return 0x11;
                case 0x12: case 0xA4: case 0xA5: return 0x12;
                case 0x5B: case 0x5C: return 0x5B;
                default: return 0;
            }
        }

        // The key with the modifiers held for it; the ones kept down with {{KEYDOWN}} are already pressed.
        static string Combo(HashSet<ushort> held, List<ushort> kept, ushort vk)
        {
            var sb = new StringBuilder("{");
            foreach (ushort mod in new ushort[] { 0x11, 0x10, 0x12, 0x5B })
                if (held.Contains(mod) && !kept.Contains(mod)) sb.Append('{').Append(KeyNames.MacroToken(mod)).Append('}');
            sb.Append('{').Append(KeyNames.MacroToken(vk)).Append("}}");
            return sb.ToString();
        }

        const string ShiftedDigits = ")!@#$%^&*(";

        /// <summary>The character a key types on a US layout, when it types one.</summary>
        public static bool TryChar(ushort vk, bool shift, bool caps, out char ch)
        {
            ch = '\0';
            if (vk >= 'A' && vk <= 'Z') { ch = (shift ^ caps) ? (char)vk : char.ToLowerInvariant((char)vk); return true; }
            if (vk >= '0' && vk <= '9') { ch = shift ? ShiftedDigits[vk - '0'] : (char)vk; return true; }
            if (vk >= 0x60 && vk <= 0x69) { ch = (char)('0' + vk - 0x60); return true; }
            switch (vk)
            {
                case 0x20: ch = ' '; return true;
                case 0xBA: ch = shift ? ':' : ';'; return true;
                case 0xBB: ch = shift ? '+' : '='; return true;
                case 0xBC: ch = shift ? '<' : ','; return true;
                case 0xBD: ch = shift ? '_' : '-'; return true;
                case 0xBE: ch = shift ? '>' : '.'; return true;
                case 0xBF: ch = shift ? '?' : '/'; return true;
                case 0xC0: ch = shift ? '~' : '`'; return true;
                case 0xDB: ch = shift ? '{' : '['; return true;
                case 0xDC: ch = shift ? '|' : '\\'; return true;
                case 0xDD: ch = shift ? '}' : ']'; return true;
                case 0xDE: ch = shift ? '"' : '\''; return true;
                case 0x6A: ch = '*'; return true;
                case 0x6B: ch = '+'; return true;
                case 0x6D: ch = '-'; return true;
                case 0x6E: ch = '.'; return true;
                case 0x6F: ch = '/'; return true;
                default: return false;
            }
        }

        /// <summary>Writes the steps one per line, with the pauses between them.</summary>
        sealed class Writer
        {
            readonly List<string> lines = new List<string>();
            readonly StringBuilder text = new StringBuilder();
            readonly double speed;

            public Writer(double speed) { this.speed = speed; }
            int lastT = -1;          // when the last step happened (-1 = nothing yet)
            int lastX = int.MinValue, lastY = int.MinValue;
            int clickIndex = -1, clickT, clickX, clickY;   // the last line, when it is a plain left click (for double clicks)
            RecWindow window;        // the window spots are measured from, or null for the screen
            string focused;          // what the last {{FOCUS}} named

            void Pause(int t, int least)
            {
                if (lastT >= 0)
                {
                    int gap = (int)Math.Round((t - lastT) / speed);
                    if (gap >= least)
                    {
                        FlushText();
                        int ms = (int)Math.Round(gap / 10.0) * 10;
                        // One pause is at most an hour; a longer gap is written as several.
                        while (ms > MaxPauseMs)
                        {
                            lines.Add("{{PAUSE:" + MaxPauseMs.ToString(CultureInfo.InvariantCulture) + "}}");
                            ms -= MaxPauseMs;
                        }
                        if (ms > 0) lines.Add("{{PAUSE:" + ms.ToString(CultureInfo.InvariantCulture) + "}}");
                    }
                }
                lastT = t;
            }

            void FlushText()
            {
                if (text.Length == 0) return;
                lines.Add(text.ToString());
                text.Clear();
            }

            public void Text(int t, char ch)
            {
                Pause(t, text.Length > 0 ? TypingGapMs : MinPauseMs);
                text.Append(ch);
                clickIndex = -1;
            }

            public void Step(int t, string step, bool keepClick)
            {
                Pause(t, MinPauseMs);
                FlushText();
                lines.Add(step);
                if (!keepClick) clickIndex = -1;
            }

            public void MoveTo(int t, int x, int y)
            {
                if (x == lastX && y == lastY) return;
                Step(t, window == null ? MacroScript.MouseTo(x, y) : MacroScript.MouseToInWindow(x - window.Left, y - window.Top), false);
                lastX = x; lastY = y;
            }

            /// <summary>The next clicks are in this window: switch to it, and measure spots from its top-left corner.</summary>
            public void Window(int t, RecWindow w)
            {
                window = w;
                lastX = lastY = int.MinValue;
                string name = FocusName(w);
                if (name.Length == 0) { window = null; return; }   // nothing to find it by: keep screen spots
                if (name == focused) return;
                Step(t, "{{FOCUS:" + name + "}}", false);
                focused = name;
            }

            /// <summary>The next clicks are on the screen itself (the taskbar, the desktop).</summary>
            public void Screen()
            {
                window = null;
                focused = null;
                lastX = lastY = int.MinValue;
            }

            // The title finds the very window; a title with characters that mean something in a macro uses the program instead.
            static string FocusName(RecWindow w)
            {
                string title = (w.Title ?? "").Trim();
                if (title.Length > 0 && title.IndexOfAny(new[] { '{', '}', '$', '\n' }) < 0) return title;
                return (w.Program ?? "").Trim();
            }

            public void Click(int t, MouseButton button, int x, int y)
            {
                // A second left click soon after the first, in the same place, is a double click.
                if (button == MouseButton.Left && clickIndex == lines.Count - 1 && clickIndex >= 0 && text.Length == 0
                    && t - clickT <= DoubleClickMs && Math.Abs(x - clickX) <= 3 && Math.Abs(y - clickY) <= 3)
                {
                    lines[clickIndex] = "{{MLEFTDBLCLICK}}";
                    clickIndex = -1;
                    lastT = t;
                    return;
                }
                Step(t, button == MouseButton.Right ? "{{RBUTTON}}" : button == MouseButton.Middle ? "{{MBUTTON}}" : "{{LBUTTON}}", true);
                if (button == MouseButton.Left) { clickIndex = lines.Count - 1; clickT = t; clickX = x; clickY = y; }
            }

            public void Scroll(int t, int delta, bool horizontal)
            {
                int notches = (int)Math.Round(Math.Abs(delta) / 120.0);
                if (notches == 0) return;
                string word = horizontal ? (delta > 0 ? "MSCROLLRIGHT" : "MSCROLLLEFT") : (delta > 0 ? "MSCROLLUP" : "MSCROLLDOWN");
                Step(t, "{{" + word + (notches > 1 ? ":" + notches.ToString(CultureInfo.InvariantCulture) : "") + "}}", false);
            }

            public string Finish()
            {
                FlushText();
                return string.Join("\n", lines);
            }
        }
    }
}
