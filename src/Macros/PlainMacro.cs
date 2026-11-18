using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MacroKit.Core;

namespace MacroKit.Macros
{
    /// <summary>What a line of a plain macro does, for the colour of its tile.</summary>
    public enum PlainKind { Empty, Keys, Text, Wait, Mouse, App, Block, Variable, Note, Code, Problem }

    /// <summary>One line of a plain macro, translated.</summary>
    public sealed class PlainLine
    {
        public int Number;          // 0-based line in the plain text
        public string Source = "";  // the line as typed, trimmed
        public string Token = "";   // its Super Macro text
        public string Error;        // null when the line is understood
        public PlainKind Kind;
        public List<MacroStep> Steps = new List<MacroStep>();

        /// <summary>What the line does in plain English ("Press Ctrl+C", "Wait 50 ms").</summary>
        public string Meaning { get { return Steps.Count == 0 ? "" : string.Join(", then ", Steps.Select(s => s.Describe())); } }
    }

    public sealed class PlainResult
    {
        public string Script = "";
        public List<PlainLine> Lines = new List<PlainLine>();
        public bool Ok { get { return Lines.All(l => l.Error == null); } }
        public IEnumerable<PlainLine> Steps { get { return Lines.Where(l => l.Kind != PlainKind.Empty); } }
    }

    /// <summary>
    /// The plain macro language: one step per line, typed the way you'd say it ("ctrl+c", "wait 50ms", "type "hello"",
    /// "repeat 3 times" ... "end repeat"). It translates both ways to Super Macro text, which stays what a macro stores and runs,
    /// so Stream Deck compatibility never changes. A line Macro Kit doesn't understand is kept as {{?:line}}, which the parser
    /// reports as a problem, so a macro with a typo never runs half of itself.
    /// </summary>
    public static class PlainMacro
    {
        const string UnknownPrefix = "{{?:";

        // ------------------------------------------------------------------ plain -> Super Macro

        public static PlainResult ToScript(string plain)
        {
            var result = new PlainResult();
            var script = new StringBuilder();
            var starts = new List<int>();
            string[] lines = (plain ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = new PlainLine { Number = i, Source = lines[i].Trim() };
                result.Lines.Add(line);
                starts.Add(script.Length);
                if (line.Source.Length == 0) { line.Kind = PlainKind.Empty; continue; }
                string error;
                PlainKind kind;
                string token = Translate(line.Source, out kind, out error);
                if (token == null)
                {
                    line.Error = error;
                    line.Kind = PlainKind.Problem;
                    token = UnknownPrefix + Escape(line.Source) + "}}";
                }
                else line.Kind = kind;
                line.Token = token;
                script.Append(token);
            }
            result.Script = script.ToString();

            // Parse once to check blocks, ranges and Pro commands, and to give each line its steps.
            ParseResult parsed = MacroParser.Parse(result.Script, true);
            foreach (MacroStep step in parsed.Steps)
            {
                PlainLine owner = LineAt(result, starts, step.Index);
                if (owner != null) owner.Steps.Add(step);
            }
            foreach (ParseError e in parsed.Errors)
            {
                PlainLine owner = LineAt(result, starts, e.Index);
                if (owner == null || owner.Error != null) continue;
                owner.Error = Reword(e.Message);
                if (owner.Kind != PlainKind.Code) owner.Kind = PlainKind.Problem;
            }
            return result;
        }

        static PlainLine LineAt(PlainResult result, List<int> starts, int index)
        {
            for (int i = result.Lines.Count - 1; i >= 0; i--)
            {
                if (result.Lines[i].Kind == PlainKind.Empty) continue;
                if (starts[i] <= index) return result.Lines[i];
            }
            return null;
        }

        /// <summary>Parser messages name Super Macro commands; in the plain editor the plain words read better.</summary>
        static string Reword(string message)
        {
            var words = new[,]
            {
                { "{{ENDLOOP}}", "end repeat" }, { "{{LOOP}}", "repeat" }, { "{{ENDSWITCH}}", "end turns" }, { "{{SWITCH}}", "take turns" },
                { "{{ENDRANDOM}}", "end random" }, { "{{RANDOM}}", "pick at random" }, { "{{ENDIF}}", "end if" }, { "{{IFWINDOW}}", "if window" },
                { "{{IF}}", "if" }, { "{{ELSE}}", "otherwise" }, { "{{OR}}", "or" }
            };
            for (int i = 0; i < words.GetLength(0); i++) message = message.Replace(words[i, 0], "“" + words[i, 1] + "”");
            return message;
        }

        static readonly Regex Duration = new Regex(@"^(?<n>\d+(?:\.\d+)?)\s*(?<u>ms|msec|msecs|milliseconds?|s|sec|secs|seconds?|m|min|mins|minutes?)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex TrailingDuration = new Regex(@"^(?<rest>.+?)\s+(?:up to\s+|for\s+)?(?<d>\d+(?:\.\d+)?\s*(?:ms|msec|msecs|milliseconds?|s|sec|secs|seconds?|m|min|mins|minutes?))$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex Count = new Regex(@"^(?<rest>.+?)\s*(?:(?:x|×)\s*(?<n>\d+)|(?<n>\d+)\s*(?:times?|x|×))$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex Every = new Regex(@"^(?<rest>.+?)[\s,]+(?:every\s+(?<d>[\d.]+\s*[a-z]*)|(?<d>[\d.]+\s*[a-z]*)\s+apart)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex Point = new Regex(@"^(?<x>-?\d+)\s*[, ]\s*(?<y>-?\d+)$", RegexOptions.Compiled);
        static readonly Regex VariableName = new Regex(@"^\$?[A-Za-z0-9_]+$", RegexOptions.Compiled);

        /// <summary>One plain line to Super Macro text, or null with the reason.</summary>
        public static string Translate(string line, out PlainKind kind, out string error)
        {
            error = null;
            kind = PlainKind.Keys;
            string s = (line ?? "").Trim();
            string lower = s.ToLowerInvariant();

            if (s.StartsWith("#", StringComparison.Ordinal) || s.StartsWith("//", StringComparison.Ordinal))
            {
                kind = PlainKind.Note;
                return MacroScript.Comment(s.TrimStart('#', '/').Trim()) + "\n";
            }
            if (s.StartsWith("{{", StringComparison.Ordinal)) { kind = PlainKind.Code; return s; }

            // Keys first: "ctrl+c", "enter", "space 10 times every 100ms".
            string keys = TryKeyLine(s);
            if (keys != null) return keys;

            string rest;
            if (Word(s, out rest, "type", "write"))
            {
                kind = PlainKind.Text;
                if (rest.Length == 0) { error = "Say what to type, like: type \"hello\"."; return null; }
                if (!IsQuoted(rest) && rest.StartsWith("$", StringComparison.Ordinal) && VariableName.IsMatch(rest))
                {
                    kind = PlainKind.Variable;
                    return MacroScript.OutputVar(rest.Substring(1));
                }
                string text = Unquote(rest);
                if (text.Length == 0) { error = "There is nothing between the quotes to type."; return null; }
                return MacroScript.Text(text);
            }
            if (Word(s, out rest, "wait for window", "wait for the window", "wait for"))
            {
                kind = PlainKind.App;
                int ms = MacroParser.DefaultWaitWindowMs;
                Match m = TrailingDuration.Match(rest);
                if (m.Success && TryDuration(m.Groups["d"].Value, out ms)) rest = m.Groups["rest"].Value.Trim();
                rest = Unquote(rest);
                if (rest.Length == 0) { error = "Say which window to wait for, like: wait for Notepad 10s."; return null; }
                if (ms < 100) { error = "Wait at least 100 ms for a window."; return null; }
                return MacroScript.WaitWindow(rest, ms);
            }
            if (Word(s, out rest, "wait", "pause", "delay", "sleep"))
            {
                kind = PlainKind.Wait;
                int ms;
                if (!TryDuration(rest, out ms)) { error = "Say how long, like: wait 50ms, wait 2s or wait 1 minute."; return null; }
                return MacroScript.Pause(ms);
            }
            if (Word(s, out rest, "hold", "hold down", "press and hold", "release", "let go of", "let go"))
            {
                bool down = lower.StartsWith("hold", StringComparison.Ordinal) || lower.StartsWith("press", StringComparison.Ordinal);
                MouseButton button;
                if (TryMouseButton(rest, out button))
                {
                    kind = PlainKind.Mouse;
                    if (button != MouseButton.Left && button != MouseButton.Right && button != MouseButton.Middle) { error = "Only the left, right and middle mouse buttons can be held."; return null; }
                    return down ? MacroScript.MouseDown(button) : MacroScript.MouseUp(button);
                }
                List<ushort> vks = ParseKeys(rest);
                if (vks == null) { error = "“" + rest + "” isn't a key Macro Kit knows."; return null; }
                IEnumerable<ushort> order = down ? vks : Enumerable.Reverse(vks);
                return string.Concat(order.Select(vk => down ? MacroScript.KeyDown(vk) : MacroScript.KeyUp(vk)));
            }
            string click = TryClick(lower);
            if (click != null) { kind = PlainKind.Mouse; return click; }
            if (Word(s, out rest, "move mouse to", "move the mouse to", "mouse to", "move to"))
            {
                kind = PlainKind.Mouse;
                Match m = Point.Match(rest);
                if (!m.Success) { error = "Give the spot as two numbers, like: move mouse to 640, 400."; return null; }
                return MacroScript.MouseTo(Int(m.Groups["x"].Value), Int(m.Groups["y"].Value));
            }
            if (Word(s, out rest, "move mouse by", "move the mouse by", "mouse by", "move by"))
            {
                kind = PlainKind.Mouse;
                Match m = Point.Match(rest);
                if (!m.Success) { error = "Give the distance as two numbers, like: move mouse by 10, -5."; return null; }
                return MacroScript.MouseBy(Int(m.Groups["x"].Value), Int(m.Groups["y"].Value));
            }
            if (lower == "remember mouse position" || lower == "save mouse position" || lower == "remember the mouse position") { kind = PlainKind.Mouse; return "{{MSAVEPOS}}"; }
            if (lower == "restore mouse position" || lower == "move mouse back" || lower == "restore the mouse position") { kind = PlainKind.Mouse; return "{{MLOADPOS}}"; }
            if (Word(s, out rest, "scroll"))
            {
                kind = PlainKind.Mouse;
                string[] parts = rest.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int notches = 1;
                if (parts.Length == 0 || parts.Length > 3 || (parts.Length >= 2 && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out notches))
                    || (parts.Length == 3 && !parts[2].StartsWith("notch", StringComparison.Ordinal)) || notches < 1 || notches > 1000)
                {
                    error = "Say which way and how far, like: scroll down 3.";
                    return null;
                }
                switch (parts[0])
                {
                    case "up": return MacroScript.ScrollUp(notches);
                    case "down": return MacroScript.ScrollDown(notches);
                    case "left": return MacroScript.ScrollLeft(notches);
                    case "right": return MacroScript.ScrollRight(notches);
                }
                error = "Scroll up, down, left or right.";
                return null;
            }
            if (Word(s, out rest, "open", "launch", "start", "run program"))
            {
                kind = PlainKind.App;
                if (rest.Length == 0) { error = "Say what to open, like: open notepad or open https://example.com."; return null; }
                if (rest.StartsWith("\"", StringComparison.Ordinal)) return "{{RUN:" + MacroScript.Arg(rest) + "}}";
                if (rest.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return MacroScript.Website(rest);
                return MacroScript.Run(rest);
            }
            if (Word(s, out rest, "run macro", "run the macro", "macro"))
            {
                kind = PlainKind.Block;
                rest = Unquote(rest);
                if (rest.Length == 0) { error = "Say which macro, like: run macro \"Daily login\"."; return null; }
                return MacroScript.Macro(rest);
            }
            if (Word(s, out rest, "close"))
            {
                kind = PlainKind.App;
                rest = Unquote(rest);
                if (rest.Length == 0) { error = "Say which program to close, like: close notepad."; return null; }
                return MacroScript.Close(rest);
            }
            if (lower == "lock" || lower == "lock pc" || lower == "lock the pc" || lower == "lock computer" || lower == "lock the computer") { kind = PlainKind.App; return MacroScript.Lock; }
            if (Word(s, out rest, "play sound", "play the sound"))
            {
                kind = PlainKind.App;
                rest = Unquote(rest);
                if (rest.Length == 0) { error = "Say which sound file to play."; return null; }
                return MacroScript.PlaySound(rest);
            }
            if (Word(s, out rest, "set volume to", "set the volume to", "volume to", "set volume", "volume"))
            {
                kind = PlainKind.App;
                string v = rest.Trim().TrimEnd('%').Trim().ToLowerInvariant();
                int n;
                if (v.StartsWith("up ", StringComparison.Ordinal) || v.StartsWith("down ", StringComparison.Ordinal))
                {
                    bool up = v.StartsWith("up", StringComparison.Ordinal);
                    if (int.TryParse(v.Substring(up ? 3 : 5).Trim().TrimEnd('%').Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= 0 && n <= 100)
                        return MacroScript.VolumeBy(up ? n : -n);
                }
                else if ((v.StartsWith("+", StringComparison.Ordinal) || v.StartsWith("-", StringComparison.Ordinal))
                    && int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n) && n >= -100 && n <= 100)
                    return MacroScript.VolumeBy(n);
                else if (int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n <= 100)
                    return MacroScript.Volume(n);
                error = "Give a level from 0 to 100, like: volume 40%, or a change, like: volume +10.";
                return null;
            }
            if (Word(s, out rest, "switch to", "focus", "go to window", "activate"))
            {
                kind = PlainKind.App;
                rest = Unquote(rest);
                if (rest.Length == 0) { error = "Say which window, like: switch to Notepad."; return null; }
                return MacroScript.Focus(rest);
            }

            // ---- blocks
            if (Word(s, out rest, "repeat", "loop"))
            {
                kind = PlainKind.Block;
                string count = Regex.Replace(rest, @"\s*(times?|x)$", "", RegexOptions.IgnoreCase).Trim();
                if (count.StartsWith("$", StringComparison.Ordinal) && VariableName.IsMatch(count)) return "{{LOOP:" + count + "}}";
                int n;
                if (!int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out n) || n < 1) { error = "Say how many times, like: repeat 3 times (and end the steps with: end repeat)."; return null; }
                return MacroScript.Loop(n);
            }
            switch (lower)
            {
                case "end repeat": case "end loop": case "endloop": kind = PlainKind.Block; return MacroScript.EndLoop;
                case "take turns": kind = PlainKind.Block; return MacroScript.Switch;
                case "end turns": case "end take turns": kind = PlainKind.Block; return MacroScript.EndSwitch;
                case "pick at random": case "pick one at random": case "random": kind = PlainKind.Block; return MacroScript.Random;
                case "end random": case "end pick": kind = PlainKind.Block; return MacroScript.EndRandom;
                case "or": case "next choice": kind = PlainKind.Block; return MacroScript.Or;
                case "otherwise": case "else": kind = PlainKind.Block; return MacroScript.Else;
                case "end if": case "endif": kind = PlainKind.Block; return MacroScript.EndIf;
                case "forget all": case "forget all variables": kind = PlainKind.Variable; return "{{VARUNSETALL}}";
            }
            if (Word(s, out rest, "if window", "if the window is", "if the window"))
            {
                kind = PlainKind.Block;
                rest = Unquote(rest);
                if (rest.Length == 0) { error = "Say which window, like: if window Notepad."; return null; }
                return MacroScript.IfWindow(rest);
            }
            if (Word(s, out rest, "if"))
            {
                kind = PlainKind.Block;
                string condition = Regex.Replace(rest, @"\s+contains\s+", " ~ ", RegexOptions.IgnoreCase);
                string left, op, right;
                if (!MacroParser.SplitComparison(condition, out left, out op, out right)) { error = "Compare two things, like: if $count = 3 (then end the steps with: end if)."; return null; }
                return MacroScript.If(left, op, right);
            }

            // ---- variables and clipboard
            if (Word(s, out rest, "set clipboard to", "set the clipboard to", "set clipboard =", "copy to clipboard"))
            {
                kind = PlainKind.Variable;
                return MacroScript.SetClipboard(Unquote(rest));
            }
            if (Word(s, out rest, "set", "let"))
            {
                kind = PlainKind.Variable;
                Match m = Regex.Match(rest, @"^\$?(?<name>[A-Za-z0-9_]+)\s*(?:=|\bto\b)\s*(?<value>.*)$");
                if (!m.Success) { error = "Give a name and a value, like: set $name = Sam."; return null; }
                string name = m.Groups["name"].Value;
                string value = m.Groups["value"].Value.Trim();
                string bare = value.ToLowerInvariant();
                if (bare == "clipboard" || bare == "the clipboard") return MacroScript.VarFromClipboard(name);
                return MacroScript.SetVar(name, Unquote(value));
            }
            if (Word(s, out rest, "forget"))
            {
                kind = PlainKind.Variable;
                if (!VariableName.IsMatch(rest)) { error = "Say which variable, like: forget $name."; return null; }
                return "{{VARUNSET:" + rest.TrimStart('$') + "}}";
            }

            kind = PlainKind.Problem;
            error = "Macro Kit doesn't know “" + Shorten(s) + "”. To type text, start the line with type, like: type \"" + Shorten(s) + "\".";
            return null;
        }

        /// <summary>"ctrl+c", "enter", "copy", "space 10 times every 100ms". Null when the line isn't keys.</summary>
        static string TryKeyLine(string s)
        {
            string body = s;
            string rest;
            if (Word(body, out rest, "press", "tap", "hit")) body = rest;
            if (body.Length == 0) return null;

            int delay = 0;
            Match every = Every.Match(body);
            if (every.Success && TryDuration(every.Groups["d"].Value.Trim(), out delay)) body = every.Groups["rest"].Value.Trim();
            else delay = 0;
            int count = 1;
            Match counted = Count.Match(body);
            if (counted.Success)
            {
                List<ushort> inner = ParseKeys(counted.Groups["rest"].Value);
                if (inner != null && int.TryParse(counted.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out count) && count >= 1 && count <= 100000)
                    return KeysToken(inner, count, delay);
            }
            if (delay > 0) return null;
            List<ushort> vks = ParseKeys(body);
            return vks == null ? null : KeysToken(vks, 1, 0);
        }

        static string KeysToken(List<ushort> vks, int count, int delay)
        {
            if (count <= 1) return MacroScript.Shortcut(vks);
            if (vks.Count == 1) return MacroScript.KeyPress(vks[0], count, delay);
            return MacroScript.Loop(count) + MacroScript.Shortcut(vks) + (delay > 0 ? MacroScript.Pause(delay) : "") + MacroScript.EndLoop;
        }

        static readonly Dictionary<string, string[]> NamedShortcuts = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "copy", new[] { "CTRL", "C" } }, { "cut", new[] { "CTRL", "X" } }, { "paste", new[] { "CTRL", "V" } },
            { "select all", new[] { "CTRL", "A" } }, { "undo", new[] { "CTRL", "Z" } }, { "redo", new[] { "CTRL", "Y" } },
            { "save", new[] { "CTRL", "S" } }
        };

        static readonly Dictionary<string, string> KeyAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "control", "CTRL" }, { "ctl", "CTRL" }, { "windows", "WIN" }, { "windows key", "WIN" }, { "win key", "WIN" },
            { "escape", "ESCAPE" }, { "return", "ENTER" }, { "spacebar", "SPACE" }, { "space bar", "SPACE" },
            { "pgup", "PAGEUP" }, { "pgdn", "PAGEDOWN" }, { "page up", "PAGEUP" }, { "page down", "PAGEDOWN" },
            { "up arrow", "UP" }, { "down arrow", "DOWN" }, { "left arrow", "LEFT" }, { "right arrow", "RIGHT" },
            { "arrow up", "UP" }, { "arrow down", "DOWN" }, { "arrow left", "LEFT" }, { "arrow right", "RIGHT" },
            { "prtsc", "SNAPSHOT" }, { "prtscn", "SNAPSHOT" }, { "prnt scrn", "SNAPSHOT" }, { "printscreen", "SNAPSHOT" },
            { "caps", "CAPITAL" }, { "capslock", "CAPITAL" }, { "ins", "INSERT" },
            { "play pause", "MEDIA_PLAY_PAUSE" }, { "play/pause", "MEDIA_PLAY_PAUSE" }, { "next track", "MEDIA_NEXT_TRACK" },
            { "previous track", "MEDIA_PREV_TRACK" }, { "prev track", "MEDIA_PREV_TRACK" }, { "stop media", "MEDIA_STOP" },
            { "mute", "VOLUME_MUTE" }, { "volume mute", "VOLUME_MUTE" }, { "volume up", "VOLUME_UP" }, { "volume down", "VOLUME_DOWN" }
        };

        /// <summary>"ctrl+shift+esc" → its keys, or null when any part isn't a key.</summary>
        public static List<ushort> ParseKeys(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            string[] named;
            if (NamedShortcuts.TryGetValue(Regex.Replace(text, @"\s+", " "), out named))
            {
                var list = new List<ushort>();
                foreach (string n in named) { ushort v; KeyNames.TryGetVk(n, out v); list.Add(v); }
                return list;
            }
            var vks = new List<ushort>();
            string[] parts = text == "+" ? new[] { "+" } : text.Split('+');
            foreach (string raw in parts)
            {
                ushort vk;
                if (!TryKey(raw, out vk)) return null;
                vks.Add(vk);
            }
            return vks;
        }

        public static bool TryKey(string raw, out ushort vk)
        {
            vk = 0;
            string part = Regex.Replace((raw ?? "").Trim(), @"\s+", " ");
            if (part.Length == 0) return false;
            MouseButton b;
            if (KeyNames.IsMouseButton(part, out b)) return false;
            string alias;
            if (KeyAliases.TryGetValue(part, out alias)) return KeyNames.TryGetVk(alias, out vk);
            if (KeyNames.TryParseDisplay(part, out vk)) return true;
            if (part.Equals("pause", StringComparison.OrdinalIgnoreCase)) return KeyNames.TryGetVk("BREAK", out vk);
            if (part.IndexOf(' ') < 0 && KeyNames.TryGetVk(part, out vk)) return true;
            return KeyNames.TryGetVk(part.Replace(' ', '_'), out vk);
        }

        static bool TryMouseButton(string text, out MouseButton button)
        {
            string t = Regex.Replace((text ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
            button = MouseButton.Left;
            // "hold left" is the Left arrow key; the mouse needs the word mouse or button.
            if (!Regex.IsMatch(t, @"\b(mouse|button)\b")) return false;
            t = Regex.Replace(t, @"\b(mouse|button|the)\b", " ").Trim();
            t = Regex.Replace(t, @"\s+", " ");
            switch (t)
            {
                case "left": case "": button = MouseButton.Left; return true;
                case "right": button = MouseButton.Right; return true;
                case "middle": button = MouseButton.Middle; return true;
            }
            button = MouseButton.Left;
            return false;
        }

        static string TryClick(string lower)
        {
            string t = Regex.Replace(lower, @"\b(mouse|button|the)\b", " ");
            t = Regex.Replace(t, @"\s+", " ").Trim().Replace("double-click", "double click").Replace("doubleclick", "double click");
            if (!t.EndsWith("click", StringComparison.Ordinal) && !t.StartsWith("click", StringComparison.Ordinal)) return null;
            bool dbl = t.Contains("double click");
            string side = t.Replace("double click", " ").Replace("click", " ").Trim();
            side = Regex.Replace(side, @"\s+", " ");
            MouseButton b;
            switch (side)
            {
                case "": case "left": b = MouseButton.Left; break;
                case "right": b = MouseButton.Right; break;
                case "middle": b = MouseButton.Middle; break;
                case "back": case "side": b = MouseButton.X1; break;
                case "forward": b = MouseButton.X2; break;
                default: return null;
            }
            if (!dbl) return MacroScript.Click(b);
            return b == MouseButton.Left || b == MouseButton.Right ? MacroScript.DoubleClick(b) : null;
        }

        /// <summary>"50", "50ms", "2s", "1.5 seconds", "1 minute" → milliseconds (0 to 1 hour).</summary>
        public static bool TryDuration(string text, out int ms)
        {
            ms = 0;
            Match m = Duration.Match((text ?? "").Trim());
            if (!m.Success) return false;
            double n;
            if (!double.TryParse(m.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return false;
            string u = m.Groups["u"].Value.ToLowerInvariant();
            double factor = u.Length == 0 || u.StartsWith("ms", StringComparison.Ordinal) || u.StartsWith("milli", StringComparison.Ordinal) ? 1
                : u.StartsWith("m", StringComparison.Ordinal) ? 60000 : 1000;
            double value = Math.Round(n * factor);
            if (value < 0 || value > 3600000) return false;
            ms = (int)value;
            return true;
        }

        /// <summary>True when s starts with one of the words (whole words, any case); rest is what follows.</summary>
        static bool Word(string s, out string rest, params string[] words)
        {
            foreach (string w in words)
            {
                if (s.Length < w.Length || string.Compare(s, 0, w, 0, w.Length, StringComparison.OrdinalIgnoreCase) != 0) continue;
                if (s.Length > w.Length && !char.IsWhiteSpace(s[w.Length]) && char.IsLetterOrDigit(w[w.Length - 1])) continue;
                rest = s.Substring(w.Length).Trim();
                return true;
            }
            rest = null;
            return false;
        }

        static bool IsQuoted(string s)
        {
            return s.Length >= 2 && ((s[0] == '"' && s[s.Length - 1] == '"') || (s[0] == '“' && s[s.Length - 1] == '”'));
        }

        static string Unquote(string s)
        {
            s = (s ?? "").Trim();
            return IsQuoted(s) ? s.Substring(1, s.Length - 2) : s;
        }

        static int Int(string s) { return int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); }

        static string Shorten(string s) { return s.Length <= 30 ? s : s.Substring(0, 30) + "…"; }

        static string Escape(string s) { return s.Replace("$", "$SMDOLLAR").Replace("}", "$SMRCB"); }
        static string Unescape(string s) { return s.Replace("$SMRCB", "}").Replace("$SMDOLLAR", "$"); }

        // ------------------------------------------------------------------ Super Macro -> plain

        static readonly Regex CommentText = new Regex(@"\{\{[ \t]*//(?<note>[^\r\n]*)", RegexOptions.Compiled);

        /// <summary>Super Macro text as plain lines. Anything without a plain wording stays as its {{code}} on a line of its own.</summary>
        public static string ToPlain(string script, bool newLineIsEnter)
        {
            script = script ?? "";
            ParseResult parsed = MacroParser.Parse(script, newLineIsEnter);
            var spans = new List<Tuple<int, int, MacroStep>>();
            spans.AddRange(parsed.Steps.Select(s => Tuple.Create(s.Index, s.Length, s)));
            spans.AddRange(parsed.Errors.Select(e => Tuple.Create(e.Index, e.Length, (MacroStep)null)));
            // A stable sort: two clicks of one {{LBUTTON}{RBUTTON}} share a span and keep their order; errors come after a step at the same place.
            spans = spans.OrderBy(t => t.Item1).ThenBy(t => t.Item3 == null ? 1 : 0).ToList();

            var lines = new List<string>();
            int pos = 0;
            int lastIndex = -1;
            foreach (var span in spans)
            {
                if (span.Item1 < pos && span.Item1 != lastIndex) continue;
                if (span.Item1 > pos) Gap(script.Substring(pos, span.Item1 - pos), lines);
                string source = script.Substring(span.Item1, Math.Min(span.Item2, script.Length - span.Item1));
                if (span.Item3 == null)
                {
                    if (span.Item1 != lastIndex)
                        lines.Add(source.StartsWith(UnknownPrefix, StringComparison.Ordinal) && source.EndsWith("}}", StringComparison.Ordinal)
                            ? Unescape(source.Substring(UnknownPrefix.Length, source.Length - UnknownPrefix.Length - 2)) : source.Trim());
                }
                else lines.AddRange(StepLines(span.Item3, source));
                lastIndex = span.Item1;
                pos = Math.Max(pos, span.Item1 + span.Item2);
            }
            if (pos < script.Length) Gap(script.Substring(pos), lines);
            return string.Join("\r\n", lines);
        }

        static void Gap(string text, List<string> lines)
        {
            foreach (Match m in CommentText.Matches(text))
            {
                string note = m.Groups["note"].Value;
                if (note.StartsWith("}}", StringComparison.Ordinal)) note = note.Substring(2);
                note = note.Trim();
                lines.Add(note.Length == 0 ? "#" : "# " + note);
            }
        }

        static IEnumerable<string> StepLines(MacroStep step, string source)
        {
            if (step.IsDynamic) { yield return source.Trim(); yield break; }
            string one = StepLine(step);
            if (one != null) { yield return one; yield break; }
            if (step.Kind == StepKind.Text)
            {
                string[] parts = (step.Text ?? "").Split('\n');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0) yield return "Enter";
                    if (parts[i].Length > 0) yield return "type \"" + parts[i] + "\"";
                }
                yield break;
            }
            yield return source.Trim();
        }

        /// <summary>The plain line for one step, or null when it needs its source (or, for text, several lines).</summary>
        static string StepLine(MacroStep s)
        {
            switch (s.Kind)
            {
                case StepKind.Keys: return KeyText(s.Keys);
                case StepKind.KeyDown: return KeyText(s.Keys) == null ? null : "hold " + KeyText(s.Keys);
                case StepKind.KeyUp: return KeyText(s.Keys) == null ? null : "release " + KeyText(s.Keys);
                case StepKind.KeyRepeat:
                    if (KeyText(s.Keys) == null) return null;
                    if (s.Count == 1) return KeyText(s.Keys);
                    return KeyText(s.Keys) + " " + s.Count + " times" + (s.Ms > 0 ? " every " + Ms(s.Ms) : "");
                case StepKind.Pause: return "wait " + Ms(s.Ms);
                case StepKind.MouseMoveTo: return "move mouse to " + s.X + ", " + s.Y;
                case StepKind.MouseMoveBy: return "move mouse by " + s.X + ", " + s.Y;
                case StepKind.MouseDown: return "hold " + ButtonWord(s.Button) + " mouse";
                case StepKind.MouseUp: return "release " + ButtonWord(s.Button) + " mouse";
                case StepKind.MouseClick: return s.Button == MouseButton.Left ? "click" : ButtonWord(s.Button) + " click";
                case StepKind.MouseDoubleClick: return s.Button == MouseButton.Left ? "double click" : ButtonWord(s.Button) + " double click";
                case StepKind.MouseWheel:
                    string way = s.Horizontal ? (s.Direction > 0 ? "right" : "left") : (s.Direction > 0 ? "up" : "down");
                    return "scroll " + way + (s.Count == 1 ? "" : " " + s.Count);
                case StepKind.SaveMousePos: return "remember mouse position";
                case StepKind.LoadMousePos: return "restore mouse position";
                case StepKind.SetVar: return "set $" + s.Var + " = " + QuoteIfNeeded(s.Value, true);
                case StepKind.OutputVar: return "type $" + s.Var;
                case StepKind.UnsetVar: return "forget $" + s.Var;
                case StepKind.UnsetAllVars: return "forget all variables";
                case StepKind.SetClipboard: return "set clipboard to " + QuoteIfNeeded(s.Value, false);
                case StepKind.VarFromClipboard: return "set $" + s.Var + " = clipboard";
                case StepKind.LoopStart: return s.Var != null ? "repeat $" + s.Var + " times" : "repeat " + s.Count + " times";
                case StepKind.LoopEnd: return "end repeat";
                case StepKind.If: return Regex.IsMatch(s.Left + " " + s.Right, @"\bcontains\b", RegexOptions.IgnoreCase) ? null : "if " + s.Left + " " + s.Op + " " + s.Right;
                case StepKind.IfWindow: return "if window " + s.Value;
                case StepKind.Else: return "otherwise";
                case StepKind.EndIf: return "end if";
                case StepKind.SwitchStart: return "take turns";
                case StepKind.RandomStart: return "pick at random";
                case StepKind.Or: return "or";
                case StepKind.EndSwitch: return "end turns";
                case StepKind.EndRandom: return "end random";
                case StepKind.Lock: return "lock pc";
                case StepKind.Volume:
                    if (s.Value != null) return null;
                    return s.Relative ? "volume " + (s.Count >= 0 ? "+" : "") + s.Count + "%" : "volume " + s.Count + "%";
            }
            // Actions keep their argument as written; one that uses a $variable keeps its {{code}} (plain text would escape the $).
            string literal = Literal(s.Value);
            if (literal == null) return null;
            switch (s.Kind)
            {
                case StepKind.Open: return "open " + literal;
                case StepKind.CloseApp: return "close " + literal;
                case StepKind.PlaySound: return "play sound " + literal;
                case StepKind.RunMacro: return "run macro " + literal;
                case StepKind.FocusWindow: return "switch to " + literal;
                case StepKind.WaitWindow:
                    // A window name ending in ":digits" or in a time would read back differently.
                    if (Regex.IsMatch(literal, @":\d+$") || TrailingDuration.IsMatch(literal)) return null;
                    return "wait for " + literal + (s.Ms == MacroParser.DefaultWaitWindowMs ? "" : " " + Ms(s.Ms));
            }
            return null;
        }

        static string Literal(string value)
        {
            if (value == null) return null;
            string without = value.Replace("$SMDOLLAR", "").Replace("$SMRCB", "");
            if (without.IndexOf('$') >= 0) return null;
            string text = Unescape(value);
            return text.Length == 0 || text.Trim().Length != text.Length || IsQuoted(text) ? null : text;
        }

        static string QuoteIfNeeded(string value, bool variable)
        {
            value = value ?? "";
            string lower = value.Trim().ToLowerInvariant();
            bool quote = value.Length == 0 || value.Trim().Length != value.Length || IsQuoted(value) || (variable && (lower == "clipboard" || lower == "the clipboard"));
            return quote ? "\"" + value + "\"" : value;
        }

        /// <summary>The plain line for keys pressed together ("Ctrl+C"); a key with no plain name keeps its Super Macro code.</summary>
        public static string KeysLine(IList<ushort> keys)
        {
            if (keys == null || keys.Count == 0) return "";
            return KeyText(keys.ToArray()) ?? MacroScript.Shortcut(keys);
        }

        static string KeyText(ushort[] keys)
        {
            if (keys == null || keys.Length == 0) return null;
            var names = keys.Select(KeyNames.Display).ToArray();
            if (names.Any(n => n.StartsWith("Key 0x", StringComparison.Ordinal))) return null;
            // A display name must read back as the same key ("Pause" is the Break key; "-" or "=" would be fine too).
            for (int i = 0; i < keys.Length; i++)
            {
                ushort back;
                if (!TryKey(names[i], out back) || back != keys[i]) return null;
            }
            return string.Join("+", names);
        }

        static string ButtonWord(MouseButton b)
        {
            switch (b)
            {
                case MouseButton.Left: return "left";
                case MouseButton.Right: return "right";
                case MouseButton.Middle: return "middle";
                case MouseButton.X1: return "back";
                default: return "forward";
            }
        }

        /// <summary>50 → "50ms", 2000 → "2s", 1500 → "1.5s", 120000 → "2 min".</summary>
        public static string Ms(int ms)
        {
            if (ms >= 60000 && ms % 60000 == 0) return (ms / 60000) + " min";
            if (ms >= 1000 && ms % 100 == 0) return (ms / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "s";
            return ms.ToString(CultureInfo.InvariantCulture) + "ms";
        }

        // ------------------------------------------------------------------ editor helpers

        /// <summary>The plain text to show for a macro: its saved plain text while that still matches the script, else the script translated.</summary>
        public static string PlainFor(MacroDef m)
        {
            if (m == null) return "";
            if (!string.IsNullOrEmpty(m.Plain) && ToScript(m.Plain).Script == (m.Script ?? "")) return m.Plain;
            return ToPlain(m.Script, m.NewLineIsEnter);
        }

        /// <summary>A short tile label for a line: "Ctrl + C", "hello", "50 ms", "Repeat 3 times".</summary>
        public static string TileLabel(PlainLine line)
        {
            if (line.Error != null) return line.Source;
            if (line.Kind == PlainKind.Note) return line.Source.TrimStart('#', '/').Trim();
            if (line.Steps.Count == 1)
            {
                MacroStep s = line.Steps[0];
                switch (s.Kind)
                {
                    case StepKind.Keys: return string.Join(" + ", s.Keys.Select(KeyNames.Display));
                    case StepKind.KeyRepeat: return string.Join(" + ", s.Keys.Select(KeyNames.Display)) + (s.Count > 1 ? "  ×" + s.Count : "");
                    case StepKind.Text: return s.Text == "\n" ? "Enter" : s.Text.Replace("\n", "↵");
                    case StepKind.Pause:
                        string t = Ms(s.Ms);
                        return t.EndsWith("ms", StringComparison.Ordinal) ? t.Substring(0, t.Length - 2) + " ms"
                            : t.EndsWith("s", StringComparison.Ordinal) ? t.Substring(0, t.Length - 1) + " s" : t;
                }
            }
            string meaning = line.Meaning;
            return meaning.Length == 0 ? line.Source : meaning;
        }

        /// <summary>Lines the editor suggests as you type: the text and what it does.</summary>
        public static readonly string[][] Suggestions =
        {
            new[] { "ctrl+c", "Press keys together" }, new[] { "ctrl+v", "Paste" }, new[] { "ctrl+x", "Cut" }, new[] { "ctrl+a", "Select all" },
            new[] { "ctrl+z", "Undo" }, new[] { "ctrl+s", "Save" }, new[] { "alt+tab", "Switch window" }, new[] { "alt+f4", "Close window" },
            new[] { "enter", "Press Enter" }, new[] { "tab", "Press Tab" }, new[] { "esc", "Press Esc" }, new[] { "space", "Press Space" },
            new[] { "backspace", "Press Backspace" }, new[] { "delete", "Press Delete" }, new[] { "end", "Press End" }, new[] { "home", "Press Home" },
            new[] { "up", "Up arrow" }, new[] { "down", "Down arrow" }, new[] { "left", "Left arrow" }, new[] { "right", "Right arrow" },
            new[] { "page up", "Press Page Up" }, new[] { "page down", "Press Page Down" }, new[] { "f5", "A function key" },
            new[] { "win+d", "Show the desktop" }, new[] { "ctrl+shift+esc", "Task Manager" },
            new[] { "copy", "Ctrl+C" }, new[] { "paste", "Ctrl+V" }, new[] { "cut", "Ctrl+X" }, new[] { "select all", "Ctrl+A" },
            new[] { "type \"\"", "Type text" }, new[] { "wait 50ms", "Pause" }, new[] { "wait 1s", "Pause a second" },
            new[] { "wait for Notepad 10s", "Wait for a window to open" },
            new[] { "space 10 times every 100ms", "Press a key several times" },
            new[] { "hold shift", "Hold a key down" }, new[] { "release shift", "Let go of a held key" },
            new[] { "click", "Left click" }, new[] { "right click", "Right click" }, new[] { "double click", "Double-click" },
            new[] { "middle click", "Middle click" }, new[] { "hold left mouse", "Hold the left button" }, new[] { "release left mouse", "Let go of the left button" },
            new[] { "move mouse to 640, 400", "Move the pointer" }, new[] { "move mouse by 10, 0", "Nudge the pointer" },
            new[] { "scroll down 3", "Mouse wheel" }, new[] { "scroll up 3", "Mouse wheel" },
            new[] { "remember mouse position", "Save where the pointer is" }, new[] { "restore mouse position", "Put the pointer back" },
            new[] { "open notepad", "Open a program, file or web page" }, new[] { "open https://", "Open a web page" },
            new[] { "close notepad", "Close a program" }, new[] { "lock pc", "Lock Windows" },
            new[] { "switch to Notepad", "Bring a window to the front" }, new[] { "run macro \"\"", "Run another macro" },
            new[] { "play sound C:\\", "Play a sound file" }, new[] { "volume 40%", "Set the volume" }, new[] { "volume +10", "Turn the volume up" },
            new[] { "mute", "Mute or unmute" }, new[] { "play/pause", "Media key" }, new[] { "next track", "Media key" }, new[] { "previous track", "Media key" },
            new[] { "repeat 3 times", "Repeat the lines below…" }, new[] { "end repeat", "…up to here" },
            new[] { "take turns", "Each run does the next choice" }, new[] { "pick at random", "Each run does one choice at random" },
            new[] { "or", "Next choice" }, new[] { "end turns", "End of take turns" }, new[] { "end random", "End of pick at random" },
            new[] { "if $count = 3", "Pro: only if it matches" }, new[] { "if window Notepad", "Pro: only in this window" },
            new[] { "otherwise", "Pro: else" }, new[] { "end if", "Pro: end of if" },
            new[] { "set $name = ", "Remember a value" }, new[] { "type $name", "Type a remembered value" },
            new[] { "set $name = clipboard", "Remember the clipboard" }, new[] { "set clipboard to ", "Put text on the clipboard" },
            new[] { "forget $name", "Forget a value" }, new[] { "# ", "A note to yourself" }
        };
    }
}
