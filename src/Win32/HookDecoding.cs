using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MacroKit.Core;
using Button = MacroKit.Core.MouseButton;

namespace MacroKit.Win32
{
    /// <summary>The pure parts of the input hook: turning raw hook structs into Core events, hotkey flags and dead-key composition.</summary>
    internal static class HookDecoding
    {
        /// <summary>
        /// Keyboard hook struct + window message to a KeyEvent. Down comes from the message (not the transition flag).
        /// Lower-integrity injected events count as injected too, so they are never mistaken for typing.
        /// </summary>
        public static KeyEvent DecodeKey(Native.KBDLLHOOKSTRUCT k, int message, long timeMs)
        {
            return new KeyEvent
            {
                Vk = (ushort)k.vkCode,
                Scan = (ushort)k.scanCode,
                Down = message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN,
                Injected = (k.flags & (Native.LLKHF_INJECTED | Native.LLKHF_LOWER_IL_INJECTED)) != 0,
                Extended = (k.flags & Native.LLKHF_EXTENDED) != 0,
                TimeMs = timeMs,
            };
        }

        /// <summary>
        /// Mouse hook struct + window message to a MouseEvent (OverOwnWindow is left false; the hook fills it for clicks).
        /// Returns false for messages that are not mouse input Macro Kit models. The wheel delta and the side button
        /// number live in the high word of mouseData.
        /// </summary>
        public static bool DecodeMouse(Native.MSLLHOOKSTRUCT m, int message, long timeMs, out MouseEvent e)
        {
            e = new MouseEvent
            {
                X = m.pt.X,
                Y = m.pt.Y,
                Injected = (m.flags & (Native.LLMHF_INJECTED | Native.LLMHF_LOWER_IL_INJECTED)) != 0,
                TimeMs = timeMs,
            };
            int high = (int)(m.mouseData >> 16);
            switch (message)
            {
                case Native.WM_MOUSEMOVE: e.Kind = MouseEventKind.Move; return true;
                case Native.WM_LBUTTONDOWN: e.Kind = MouseEventKind.Down; e.Button = Button.Left; return true;
                case Native.WM_LBUTTONUP: e.Kind = MouseEventKind.Up; e.Button = Button.Left; return true;
                case Native.WM_RBUTTONDOWN: e.Kind = MouseEventKind.Down; e.Button = Button.Right; return true;
                case Native.WM_RBUTTONUP: e.Kind = MouseEventKind.Up; e.Button = Button.Right; return true;
                case Native.WM_MBUTTONDOWN: e.Kind = MouseEventKind.Down; e.Button = Button.Middle; return true;
                case Native.WM_MBUTTONUP: e.Kind = MouseEventKind.Up; e.Button = Button.Middle; return true;
                case Native.WM_XBUTTONDOWN:
                case Native.WM_XBUTTONUP:
                    if (high != (int)Native.XBUTTON1 && high != (int)Native.XBUTTON2) return false;
                    e.Kind = message == Native.WM_XBUTTONDOWN ? MouseEventKind.Down : MouseEventKind.Up;
                    e.Button = high == (int)Native.XBUTTON1 ? Button.X1 : Button.X2;
                    return true;
                case Native.WM_MOUSEWHEEL: e.Kind = MouseEventKind.Wheel; e.Delta = (short)high; return true;
                case Native.WM_MOUSEHWHEEL: e.Kind = MouseEventKind.HWheel; e.Delta = (short)high; return true;
            }
            return false;
        }

        /// <summary>True when the point is inside the rectangle (left and top edges count, right and bottom do not, like Windows).</summary>
        public static bool PointInRect(Native.RECT rect, int x, int y)
        {
            return x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
        }

        /// <summary>True when the point is inside any of the rectangles.</summary>
        public static bool PointInAny(IList<Native.RECT> rects, int x, int y)
        {
            if (rects == null) return false;
            for (int i = 0; i < rects.Count; i++)
            {
                if (PointInRect(rects[i], x, y)) return true;
            }
            return false;
        }

        /// <summary>HotkeyModifiers already use the MOD_* values; this adds MOD_NOREPEAT so holding the keys fires once.</summary>
        public static uint ToNativeModifiers(HotkeyModifiers modifiers)
        {
            uint flags = Native.MOD_NOREPEAT;
            if ((modifiers & HotkeyModifiers.Alt) != 0) flags |= Native.MOD_ALT;
            if ((modifiers & HotkeyModifiers.Ctrl) != 0) flags |= Native.MOD_CONTROL;
            if ((modifiers & HotkeyModifiers.Shift) != 0) flags |= Native.MOD_SHIFT;
            if ((modifiers & HotkeyModifiers.Win) != 0) flags |= Native.MOD_WIN;
            return flags;
        }

        // Spacing accent (what ToUnicodeEx returns for a dead key) to the combining mark that composes with a letter.
        static readonly Dictionary<char, char> combiningMarks = new Dictionary<char, char>
        {
            { '´', '́' }, { '`', '̀' }, { '^', '̂' }, { '¨', '̈' }, { '~', '̃' },
            { 'ˇ', '̌' }, { '¸', '̧' }, { '˘', '̆' }, { '˚', '̊' }, { '˝', '̋' },
            { '˙', '̇' }, { '¯', '̄' }, { '˛', '̨' },
        };

        /// <summary>
        /// What typing "next" right after the dead key "accent" produces. ToUnicodeEx is called with flag 4 (it must not
        /// disturb the keyboard state the real app is using), which means Windows does not remember the dead key between
        /// our calls, so the composition is redone here: dead accent + e = é, accent + space = the accent alone, and an
        /// accent that cannot combine with the next character comes out in front of it, as Windows does.
        /// </summary>
        public static string ComposeDead(char accent, string next)
        {
            if (string.IsNullOrEmpty(next) || next == " ") return accent.ToString();
            char mark;
            if (char.GetUnicodeCategory(accent) == UnicodeCategory.NonSpacingMark) mark = accent;
            else if (!combiningMarks.TryGetValue(accent, out mark)) return accent + next;
            string composed = string.Concat(next[0].ToString(), mark.ToString()).Normalize(NormalizationForm.FormC);
            return composed.Length == 1 ? composed + next.Substring(1) : accent + next;
        }
    }
}
