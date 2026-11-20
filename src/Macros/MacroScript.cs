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

        // ---- Blocks (Stream Deck Multi Action style) ----

        /// <summary>Repeat the steps up to {{ENDLOOP}} this many times.</summary>
        public static string Loop(int times) { return "{{LOOP:" + times.ToString(CultureInfo.InvariantCulture) + "}}"; }
        public const string EndLoop = "{{ENDLOOP}}";

        /// <summary>Take turns: each time the macro reaches it, the next choice runs (like Stream Deck's Multi Action Switch).</summary>
        public const string Switch = "{{SWITCH}}";
        public const string EndSwitch = "{{ENDSWITCH}}";

        /// <summary>One of the choices, picked at random (like Stream Deck's Random Action).</summary>
        public const string Random = "{{RANDOM}}";
        public const string EndRandom = "{{ENDRANDOM}}";

        /// <summary>Goes between the choices of a switch or random block.</summary>
        public const string Or = "{{OR}}";

        /// <summary>Macro Kit Pro: {{IF:left op right}}.</summary>
        public static string If(string left, string op, string right) { return "{{IF:" + Arg(left, false) + op + Arg(right, false) + "}}"; }
        public static string IfWindow(string text) { return "{{IFWINDOW:" + Arg(text, false) + "}}"; }
        public const string Else = "{{ELSE}}";
        public const string EndIf = "{{ENDIF}}";

        // ---- Actions ----

        /// <summary>Open a program, file or folder. A path with spaces is quoted so arguments could follow.</summary>
        public static string Run(string target, string arguments = null)
        {
            target = (target ?? "").Trim().Trim('"');
            bool quote = target.IndexOf(' ') >= 0 || !string.IsNullOrWhiteSpace(arguments);
            string text = quote ? "\"" + target + "\"" : target;
            if (!string.IsNullOrWhiteSpace(arguments)) text += " " + arguments.Trim();
            return "{{RUN:" + Arg(text) + "}}";
        }

        /// <summary>Open a web page in the default browser. "example.com" becomes https://example.com.</summary>
        public static string Website(string url)
        {
            url = (url ?? "").Trim();
            if (url.IndexOf("://", StringComparison.Ordinal) < 0 && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
            return "{{RUN:" + Arg(url) + "}}";
        }

        public static string Close(string program) { return "{{CLOSE:" + Arg((program ?? "").Trim()) + "}}"; }
        public static string PlaySound(string path) { return "{{PLAYSOUND:" + Arg((path ?? "").Trim().Trim('"')) + "}}"; }
        public static string Volume(int percent) { return "{{VOLUME:" + percent.ToString(CultureInfo.InvariantCulture) + "}}"; }
        public static string VolumeBy(int change) { return "{{VOLUME:" + (change >= 0 ? "+" : "") + change.ToString(CultureInfo.InvariantCulture) + "}}"; }
        public const string Lock = "{{LOCK}}";
        public static string Macro(string name) { return "{{MACRO:" + Arg((name ?? "").Trim()) + "}}"; }
        public static string Focus(string window) { return "{{FOCUS:" + Arg((window ?? "").Trim()) + "}}"; }

        public static string WaitWindow(string window, int timeoutMs)
        {
            string name = Arg((window ?? "").Trim());
            // A title that ends in ":digits" would be read back as the time limit, so the limit is written whenever the title looks like that.
            bool writeLimit = timeoutMs != MacroParser.DefaultWaitWindowMs || System.Text.RegularExpressions.Regex.IsMatch(name, @":\s*\d+\s*$");
            return "{{WAITWINDOW:" + name + (writeLimit ? ":" + timeoutMs.ToString(CultureInfo.InvariantCulture) : "") + "}}";
        }

        // ---- Variables and clipboard ----

        public static string SetVar(string name, string value) { return "{{VARSET:" + name + ":" + Arg(value, false) + "}}"; }
        public static string OutputVar(string name) { return "{{OUTPUT:" + name + "}}"; }
        public static string SetClipboard(string value) { return "{{SETCLIPBOARD:" + Arg(value, false) + "}}"; }
        public static string VarFromClipboard(string name) { return "{{VARSETFROMCLIPBOARD:" + name + "}}"; }

        /// <summary>
        /// A value written inside a command. A } would end the command, so it is written as $SMRCB, which the run fills back in.
        /// For a literal value (a path, a web address, a name) a $ is written as $SMDOLLAR so it isn't read as a variable;
        /// otherwise $names stay so the run fills them in. Line breaks become spaces.
        /// </summary>
        public static string Arg(string value, bool literal = true)
        {
            string text = (value ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            if (literal) text = text.Replace("$", "$SMDOLLAR");
            return text.Replace("}", "$SMRCB");
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
