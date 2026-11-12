using System;
using System.Collections.Generic;
using MacroKit.Core;
using Button = MacroKit.Core.MouseButton;

namespace MacroKit.Win32
{
    /// <summary>
    /// Builds the INPUT structures SendInputSink sends. Pure functions (the scan code lookup is passed in), so the exact
    /// packets can be unit tested without sending anything.
    /// </summary>
    internal static class InputPackets
    {
        /// <summary>Most characters typed per SendInput call, so a long phrase never monopolises the input queue in one go.</summary>
        public const int MaxTextBatch = 64;

        /// <summary>Layout-dependent VK to scan code (MapVirtualKey). Extended keys come back without their 0xE0 prefix; the flag carries that.</summary>
        public static ushort ScanFor(ushort vk)
        {
            return (ushort)Native.MapVirtualKeyW(KeyNames.BaseVk(vk), Native.MAPVK_VK_TO_VSC);
        }

        /// <summary>
        /// One key event. The extended flag comes from KeyNames.IsExtended, which is false for the numpad navigation keys
        /// (they carry KeyNames.ForceNonExtended; the bit is masked off here because it is not a real VK bit).
        /// </summary>
        public static Native.INPUT Key(ushort vk, bool down, ushort scan)
        {
            uint flags = down ? 0u : Native.KEYEVENTF_KEYUP;
            if (KeyNames.IsExtended(vk)) flags |= Native.KEYEVENTF_EXTENDEDKEY;
            var input = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            input.ki.wVk = KeyNames.BaseVk(vk);
            input.ki.wScan = scan;
            input.ki.dwFlags = flags;
            input.ki.dwExtraInfo = Native.ExtraInfo;
            return input;
        }

        /// <summary>All keys down, in the order given.</summary>
        public static Native.INPUT[] ComboDown(IList<ushort> keys, Func<ushort, ushort> scanFor)
        {
            var packets = new Native.INPUT[keys.Count];
            for (int i = 0; i < keys.Count; i++) packets[i] = Key(keys[i], true, scanFor(keys[i]));
            return packets;
        }

        /// <summary>All keys up, in reverse order (so Ctrl is released after C, like a real keyboard).</summary>
        public static Native.INPUT[] ComboUp(IList<ushort> keys, Func<ushort, ushort> scanFor)
        {
            var packets = new Native.INPUT[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                ushort vk = keys[keys.Count - 1 - i];
                packets[i] = Key(vk, false, scanFor(vk));
            }
            return packets;
        }

        /// <summary>
        /// A Unicode character as a key-down/key-up pair (KEYEVENTF_UNICODE with wVk 0 delivers a VK_PACKET message).
        /// Characters outside the BMP are sent as two such pairs, high surrogate first, which is how apps expect them.
        /// </summary>
        static void AddUnicode(List<Native.INPUT> packets, char unit)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var input = new Native.INPUT { type = Native.INPUT_KEYBOARD };
                input.ki.wVk = 0;
                input.ki.wScan = unit;
                input.ki.dwFlags = Native.KEYEVENTF_UNICODE | (pass == 0 ? 0u : Native.KEYEVENTF_KEYUP);
                input.ki.dwExtraInfo = Native.ExtraInfo;
                packets.Add(input);
            }
        }

        /// <summary>
        /// The packets for TypeText, split into batches of at most <see cref="MaxTextBatch"/> characters.
        /// '\n' presses Enter and '\t' presses Tab as real keys (many apps ignore a Unicode newline), '\r' is dropped,
        /// and a surrogate pair is never split across two batches.
        /// </summary>
        public static List<Native.INPUT[]> TextBatches(string text, Func<ushort, ushort> scanFor)
        {
            var batches = new List<Native.INPUT[]>();
            if (string.IsNullOrEmpty(text)) return batches;
            var current = new List<Native.INPUT>();
            int characters = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r') continue;
                bool pair = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
                int units = pair ? 2 : 1;
                if (characters + units > MaxTextBatch)
                {
                    batches.Add(current.ToArray());
                    current.Clear();
                    characters = 0;
                }
                if (c == '\n' || c == '\t')
                {
                    ushort vk = c == '\n' ? Native.VK_RETURN : Native.VK_TAB;
                    current.Add(Key(vk, true, scanFor(vk)));
                    current.Add(Key(vk, false, scanFor(vk)));
                }
                else
                {
                    AddUnicode(current, c);
                    if (pair) AddUnicode(current, text[++i]);
                }
                characters += units;
            }
            if (current.Count > 0) batches.Add(current.ToArray());
            return batches;
        }

        /// <summary>A mouse button press or release. The side buttons use XDOWN/XUP with the button number in mouseData.</summary>
        public static Native.INPUT MouseButtonEvent(Button button, bool down)
        {
            var input = new Native.INPUT { type = Native.INPUT_MOUSE };
            input.mi.dwExtraInfo = Native.ExtraInfo;
            switch (button)
            {
                case Button.Left: input.mi.dwFlags = down ? Native.MOUSEEVENTF_LEFTDOWN : Native.MOUSEEVENTF_LEFTUP; break;
                case Button.Right: input.mi.dwFlags = down ? Native.MOUSEEVENTF_RIGHTDOWN : Native.MOUSEEVENTF_RIGHTUP; break;
                case Button.Middle: input.mi.dwFlags = down ? Native.MOUSEEVENTF_MIDDLEDOWN : Native.MOUSEEVENTF_MIDDLEUP; break;
                case Button.X1: input.mi.dwFlags = down ? Native.MOUSEEVENTF_XDOWN : Native.MOUSEEVENTF_XUP; input.mi.mouseData = Native.XBUTTON1; break;
                case Button.X2: input.mi.dwFlags = down ? Native.MOUSEEVENTF_XDOWN : Native.MOUSEEVENTF_XUP; input.mi.mouseData = Native.XBUTTON2; break;
                default: throw new ArgumentOutOfRangeException("button");
            }
            return input;
        }

        /// <summary>A wheel turn. mouseData is the signed delta stored in a DWORD (120 per notch; positive = up / right).</summary>
        public static Native.INPUT Wheel(int delta, bool horizontal)
        {
            var input = new Native.INPUT { type = Native.INPUT_MOUSE };
            input.mi.dwFlags = horizontal ? Native.MOUSEEVENTF_HWHEEL : Native.MOUSEEVENTF_WHEEL;
            input.mi.mouseData = unchecked((uint)delta);
            input.mi.dwExtraInfo = Native.ExtraInfo;
            return input;
        }

        /// <summary>Move to a 0..65535 position over the primary monitor (Super Macro's MOUSEPOS).</summary>
        public static Native.INPUT MoveNormalized(int x65535, int y65535)
        {
            var input = new Native.INPUT { type = Native.INPUT_MOUSE };
            input.mi.dx = Math.Max(0, Math.Min(65535, x65535));
            input.mi.dy = Math.Max(0, Math.Min(65535, y65535));
            input.mi.dwFlags = Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE;
            input.mi.dwExtraInfo = Native.ExtraInfo;
            return input;
        }
    }
}
