using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MacroKit.Core;

namespace MacroKit.Macros
{
    /// <summary>
    /// Builds the script text the command palette inserts. Every token parses back to the step it stands for
    /// ({{CTRL}{C}}, {{PAUSE:100}}, {{KEYPRESS:DOWN:5:100}}, {{MOUSEXY:100,200}}...).
    /// </summary>
    public static class MacroScript
    {
        /// <summary>A key combination: {{CTRL}{C}}. One key gives {{DOWN}}.</summary>
        public static string Shortcut(params ushort[] keys)
        {
            return Shortcut((IEnumerable<ushort>)keys);
        }

        public static string Shortcut(IEnumerable<ushort> keys)
        {
            string[] tokens = keys.Select(KeyNames.MacroToken).ToArray();
            if (tokens.Length == 0) throw new ArgumentException("A shortcut needs at least one key.", "keys");
            return "{{" + string.Join("}{", tokens) + "}}";
        }

        public static string Pause(int ms) { return "{{PAUSE:" + ms.ToString(CultureInfo.InvariantCulture) + "}}"; }

        /// <summary>Press a key several times; a delay of 0 leaves out the delay part.</summary>
        public static string KeyPress(ushort key, int count, int delayMs = 0)
        {
            return "{{KEYPRESS:" + KeyNames.MacroToken(key) + ":" + count.ToString(CultureInfo.InvariantCulture)
                + (delayMs > 0 ? ":" + delayMs.ToString(CultureInfo.InvariantCulture) : "") + "}}";
        }

        public static string KeyDown(ushort key) { return "{{KEYDOWN:" + KeyNames.MacroToken(key) + "}}"; }
        public static string KeyUp(ushort key) { return "{{KEYUP:" + KeyNames.MacroToken(key) + "}}"; }

        /// <summary>Plain text to type. A "{{" in it would start a command, so the first brace of each pair is written as {{OUTPUT:SMLCB}}.</summary>
        public static string Text(string text)
        {
            text = text ?? "";
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '{' && i + 1 < text.Length && text[i + 1] == '{') result.Append("{{OUTPUT:SMLCB}}");
                else result.Append(text[i]);
            }
            return result.ToString();
        }

        /// <summary>A note that hides the rest of its line. Put it on a line of its own.</summary>
        public static string Comment(string note)
        {
            string oneLine = (note ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return oneLine.Length == 0 ? "{{//}}" : "{{//}} " + oneLine;
        }

        public static string MouseTo(int x, int y) { return "{{MOUSEXY:" + Pair(x, y) + "}}"; }
        public static string MouseBy(int dx, int dy) { return "{{MOUSEMOVE:" + Pair(dx, dy) + "}}"; }
        public static string MouseNormalized(int x, int y) { return "{{MOUSEPOS:" + Pair(x, y) + "}}"; }

        public static string Click(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Left: return "{{LBUTTON}}";
                case MouseButton.Right: return "{{RBUTTON}}";
                case MouseButton.Middle: return "{{MBUTTON}}";
                case MouseButton.X1: return "{{XBUTTON1}}";
                default: return "{{XBUTTON2}}";
            }
        }

        /// <summary>Hold a left, right or middle button. The side buttons only click.</summary>
        public static string MouseDown(MouseButton button) { return "{{M" + ButtonWord(button) + "DOWN}}"; }
        public static string MouseUp(MouseButton button) { return "{{M" + ButtonWord(button) + "UP}}"; }

        /// <summary>Double-click the left or right button.</summary>
        public static string DoubleClick(MouseButton button)
        {
            if (button != MouseButton.Left && button != MouseButton.Right) throw new ArgumentException("Only the left and right buttons can double-click.", "button");
            return "{{M" + ButtonWord(button) + "DBLCLICK}}";
        }

        public static string ScrollUp(int notches = 1) { return Scroll("UP", notches); }
        public static string ScrollDown(int notches = 1) { return Scroll("DOWN", notches); }
        public static string ScrollLeft(int notches = 1) { return Scroll("LEFT", notches); }
        public static string ScrollRight(int notches = 1) { return Scroll("RIGHT", notches); }

        static string Scroll(string direction, int notches)
        {
            return "{{MSCROLL" + direction + (notches == 1 ? "" : ":" + notches.ToString(CultureInfo.InvariantCulture)) + "}}";
        }

        static string Pair(int a, int b)
        {
            return a.ToString(CultureInfo.InvariantCulture) + "," + b.ToString(CultureInfo.InvariantCulture);
        }

        static string ButtonWord(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Left: return "LEFT";
                case MouseButton.Right: return "RIGHT";
                case MouseButton.Middle: return "MIDDLE";
                default: throw new ArgumentException("Only the left, right and middle buttons can be held or released.", "button");
            }
        }
    }
}
