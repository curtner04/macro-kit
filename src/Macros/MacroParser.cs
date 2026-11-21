using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MacroKit.Core;

namespace MacroKit.Macros
{
    /// <summary>Parses BarRaider Super Macro text ({{CTRL}{C}}{{PAUSE:50}} and friends) into steps.</summary>
    public static class MacroParser
    {
        const int MaxPauseMs = 3600000;
        const int MaxRepeat = 100000;
        const int MaxNotches = 1000;

        static readonly Regex GroupSeparator = new Regex(@"\}\s*\{", RegexOptions.Compiled);
        static readonly Regex VariableName = new Regex(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled);

        static readonly HashSet<string> Commands = new HashSet<string>
        {
            "PAUSE", "KEYPRESS", "KEYDOWN", "KEYUP", "MOUSEXY", "MOUSEMOVE", "MOUSEPOS", "WINDOWXY",
            "MLEFTDOWN", "MLEFTUP", "MLEFTDBLCLICK", "MRIGHTDOWN", "MRIGHTUP", "MRIGHTDBLCLICK", "MMIDDLEDOWN", "MMIDDLEUP",
            "MSCROLLUP", "MSCROLLDOWN", "MSCROLLLEFT", "MSCROLLRIGHT", "MSAVEPOS", "MLOADPOS",
            "VARSET", "OUTPUT", "VARUNSET", "VARUNSETALL", "SETCLIPBOARD", "VARSETFROMCLIPBOARD", "WAITPIXEL", "INPUT"
        };

        /// <summary>Commands only Macro Kit runs: a Stream Deck Super Macro action doesn't know them.</summary>
        public static readonly string[] MacroKitOnly = { "WINDOWXY", "WAITPIXEL" };

        // Super Macro commands that need files, prompts or program flow. Macro Kit leaves them out on purpose.
        static readonly HashSet<string> Unsupported = new HashSet<string>
        {
            "OUTPUTTOFILE", "APPENDTOFILE", "VARSETFROMFILE", "VARSETFROMENV", "SETICONFROMFILE", "SETKEYTITLE",
            "FUNCTION", "ENDFUNCTION", "CALL", "FOR", "ENDFOR", "WHILE", "ENDWHILE"
        };

        // Blocks: loops, take-turns switches and random picks (free) and conditions (Macro Kit Pro). They parse into flow steps whose
        // Jump links each block's start and end.
        static readonly HashSet<string> Flow = new HashSet<string>
        {
            "LOOP", "ENDLOOP", "IF", "IFWINDOW", "ELSE", "ENDIF", "SWITCH", "RANDOM", "OR", "ENDSWITCH", "ENDRANDOM"
        };

        // Only these blocks need Macro Kit Pro.
        static readonly HashSet<string> ProFlow = new HashSet<string> { "IF", "IFWINDOW", "ELSE", "ENDIF" };

        // Stream Deck-style actions. Their whole argument is kept as written (paths and web addresses contain colons).
        static readonly HashSet<string> Actions = new HashSet<string> { "RUN", "CLOSE", "PLAYSOUND", "LOCK", "VOLUME", "MACRO", "FOCUS", "WAITWINDOW" };

        /// <summary>How long {{WAITWINDOW}} waits when the script doesn't say.</summary>
        public const int DefaultWaitWindowMs = 10000;

        /// <summary>How long {{WAITPIXEL}} waits when the script doesn't say.</summary>
        public const int DefaultWaitPixelMs = 10000;

        /// <summary>How far each of red, green and blue may be off for {{WAITPIXEL}} to count a colour as the same (0 to 255).</summary>
        public const int PixelTolerance = 10;

        /// <summary>Reads "#1E90FF" or "1E90FF" as 0x1E90FF.</summary>
        public static bool TryColor(string text, out int rgb)
        {
            rgb = 0;
            text = (text ?? "").Trim();
            if (text.StartsWith("#", StringComparison.Ordinal)) text = text.Substring(1);
            return text.Length == 6 && text.All(Uri.IsHexDigit) && int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb);
        }

        /// <summary>0x1E90FF as "#1E90FF".</summary>
        public static string FormatColor(int rgb) { return "#" + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture); }

        /// <summary>True when each of red, green and blue is within <see cref="PixelTolerance"/>.</summary>
        public static bool ColorsMatch(int a, int b)
        {
            for (int shift = 0; shift <= 16; shift += 8)
            {
                if (Math.Abs(((a >> shift) & 0xFF) - ((b >> shift) & 0xFF)) > PixelTolerance) return false;
            }
            return true;
        }

        /// <summary>The Macro Kit-only commands a script uses ("WINDOWXY", "WAITPIXEL"), in the order they first appear.</summary>
        public static IList<string> MacroKitOnlyCommands(string script)
        {
            var found = new List<string>();
            foreach (Match m in Regex.Matches(script ?? "", @"\{\{\s*([A-Za-z]+)\s*[:}]"))
            {
                string name = m.Groups[1].Value.ToUpperInvariant();
                if (MacroKitOnly.Contains(name) && !found.Contains(name)) found.Add(name);
            }
            return found;
        }

        /// <summary>
        /// Splits {{RUN}}'s argument into what to open and its arguments. A target in double quotes may be followed by arguments
        /// ("C:\Program Files\App\app.exe" --new); without quotes the whole text is the target (notepad, C:\Notes\todo.txt, https://...).
        /// </summary>
        public static void SplitRun(string value, out string target, out string arguments)
        {
            value = (value ?? "").Trim();
            arguments = "";
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                int close = value.IndexOf('"', 1);
                if (close > 0)
                {
                    target = value.Substring(1, close - 1).Trim();
                    arguments = value.Substring(close + 1).Trim();
                    return;
                }
                value = value.Substring(1);
            }
            target = value;
        }

        /// <summary>Reads {{VOLUME}}'s value: "50" sets 50%, "+10" and "-10" change it. False when it isn't one of those.</summary>
        public static bool TryVolume(string value, out int amount, out bool relative)
        {
            value = (value ?? "").Trim().TrimEnd('%').Trim();
            relative = value.StartsWith("+", StringComparison.Ordinal) || value.StartsWith("-", StringComparison.Ordinal);
            return int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount)
                && (relative ? amount >= -100 && amount <= 100 : amount >= 0 && amount <= 100);
        }

        /// <summary>Reads {{WAITWINDOW}}'s value: "Notepad" or "Notepad:5000" (the last part, when it is a number, is the time limit in ms).</summary>
        public static bool TryWaitWindow(string value, out string window, out int timeoutMs)
        {
            window = (value ?? "").Trim();
            timeoutMs = DefaultWaitWindowMs;
            int colon = window.LastIndexOf(':');
            if (colon >= 0)
            {
                string tail = window.Substring(colon + 1).Trim();
                if (tail.Length > 0 && tail.All(char.IsDigit))
                {
                    int ms;
                    if (!int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out ms) || ms < 100 || ms > MaxPauseMs) return false;
                    timeoutMs = ms;
                    window = window.Substring(0, colon).Trim();
                }
            }
            return window.Length > 0;
        }

        /// <summary>The comparisons {{IF}} understands, two-character ones first so "&lt;=" wins over "&lt;".</summary>
        public static readonly string[] Comparisons = { "!=", ">=", "<=", "=", ">", "<", "~" };

        /// <summary>"$count>=3" gives "$count", ">=", "3". The first comparison sign splits; the left side can't be empty.</summary>
        public static bool SplitComparison(string condition, out string left, out string op, out string right)
        {
            left = op = right = null;
            condition = condition ?? "";
            for (int i = 0; i < condition.Length; i++)
            {
                foreach (string candidate in Comparisons)
                {
                    if (string.CompareOrdinal(condition, i, candidate, 0, candidate.Length) != 0) continue;
                    left = condition.Substring(0, i).Trim();
                    op = candidate;
                    right = condition.Substring(i + candidate.Length).Trim();
                    return left.Length > 0;
                }
            }
            return false;
        }

        // Numbers compare as numbers when both sides are numbers; otherwise text compares, ignoring case.
        public static bool Compare(string left, string op, string right)
        {
            left = (left ?? "").Trim();
            right = (right ?? "").Trim();
            if (op == "~") return left.IndexOf(right, StringComparison.OrdinalIgnoreCase) >= 0;
            double a, b;
            int order = double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out a) && double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out b)
                ? a.CompareTo(b)
                : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            switch (op)
            {
                case "=": return order == 0;
                case "!=": return order != 0;
                case "<": return order < 0;
                case ">": return order > 0;
                case "<=": return order <= 0;
                case ">=": return order >= 0;
                default: return false;
            }
        }

        public static ParseResult Parse(string script, bool newLineIsEnter)
        {
            return new Parser(script ?? "", newLineIsEnter).Run();
        }

        /// <summary>
        /// Builds the step for one command. Returns null on success or the error message.
        /// With a resolver, $names in numbers are filled in right away (the runner uses this for steps the parser had to defer);
        /// without one, a number containing $ marks the step as dynamic (Command and Args set) and skips its range check.
        /// </summary>
        internal static string Build(string name, string[] args, Func<string, string> resolve, out MacroStep step)
        {
            var builder = new Builder { Resolve = resolve };
            step = new MacroStep();
            string error = builder.Fill(name, args, step);
            if (error != null) return error;
            if (builder.Dynamic)
            {
                step.Command = name;
                step.Args = args;
            }
            return null;
        }

        sealed class Builder
        {
            public Func<string, string> Resolve;
            public bool Dynamic;

            // Reads an integer in [min, max]. A number with a $name is deferred (returns min) when there is no resolver.
            bool Number(string raw, int min, int max, out int value)
            {
                value = min;
                if (raw.IndexOf('$') >= 0)
                {
                    if (Resolve == null) { Dynamic = true; return true; }
                    raw = Resolve(raw);
                }
                return int.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
            }

            // Reads a key name. A name with a $variable is deferred without a resolver (vk stays 0 and the step becomes dynamic);
            // with one it is filled in first. Returns null on success or the error message.
            string Key(string raw, string tag, out ushort vk)
            {
                vk = 0;
                if (raw.IndexOf('$') >= 0)
                {
                    if (Resolve == null) { Dynamic = true; return null; }
                    string resolved = (Resolve(raw) ?? "").Trim();
                    if (resolved.Length == 0) return "The key name in " + tag + " is empty. Check that the variable it uses has been set.";
                    raw = resolved;
                }
                if (!KeyNames.TryGetVk(raw, out vk)) return "Unknown key “" + raw + "” in " + tag + ".";
                return null;
            }

            // Reads a colour (#RRGGBB). One with a $variable is deferred (black) without a resolver, like Number.
            bool Color(string raw, out int rgb)
            {
                rgb = 0;
                if (raw.IndexOf('$') >= 0)
                {
                    if (Resolve == null) { Dynamic = true; return true; }
                    raw = Resolve(raw);
                }
                return TryColor(raw, out rgb);
            }

            // Reads "x,y" (also given as two arguments). Returns false when it is not two whole numbers in range.
            bool Pair(string[] args, int min, int max, out int x, out int y)
            {
                x = y = Math.Max(min, Math.Min(max, 0));
                string joined = string.Join(",", args);
                if (joined.IndexOf('$') >= 0)
                {
                    if (Resolve == null) { Dynamic = true; return true; }
                    joined = Resolve(joined);
                }
                string[] parts = joined.Split(',');
                return parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out x) && x >= min && x <= max
                    && int.TryParse(parts[1].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out y) && y >= min && y <= max;
            }

            public string Fill(string name, string[] args, MacroStep step)
            {
                string tag = "{{" + name + "}}";
                int n, m, x, y;
                ushort vk;
                switch (name)
                {
                    case "PAUSE":
                        if (args.Length == 0) return tag + " needs a time in milliseconds, like {{PAUSE:100}}.";
                        if (args.Length > 1 || !Number(args[0], 0, MaxPauseMs, out n)) return "The wait in " + tag + " must be a whole number of milliseconds from 0 to 3,600,000.";
                        step.Kind = StepKind.Pause; step.Ms = n;
                        return null;

                    case "KEYPRESS":
                        if (args.Length < 2 || args.Length > 3) return tag + " needs a key and a count, like {{KEYPRESS:DOWN:5}} or {{KEYPRESS:DOWN:5:100}} to wait 100 ms between presses.";
                        string keyProblem = Key(args[0], tag, out vk);
                        if (keyProblem != null) return keyProblem;
                        if (!Number(args[1], 1, MaxRepeat, out n)) return "The count in " + tag + " must be a whole number from 1 to 100,000.";
                        m = 0;
                        if (args.Length == 3 && !Number(args[2], 0, MaxPauseMs, out m)) return "The delay in " + tag + " must be a whole number of milliseconds from 0 to 3,600,000.";
                        step.Kind = StepKind.KeyRepeat; step.Keys = vk == 0 ? new ushort[0] : new[] { vk }; step.Count = n; step.Ms = m;
                        return null;

                    case "KEYDOWN":
                    case "KEYUP":
                        if (args.Length != 1) return tag + " needs one key, like {{" + name + ":SHIFT}}.";
                        keyProblem = Key(args[0], tag, out vk);
                        if (keyProblem != null) return keyProblem;
                        step.Kind = name == "KEYDOWN" ? StepKind.KeyDown : StepKind.KeyUp; step.Keys = vk == 0 ? new ushort[0] : new[] { vk };
                        return null;

                    case "MOUSEXY":
                        if (!Pair(args, -100000, 100000, out x, out y)) return tag + " needs two numbers, like {{MOUSEXY:100,200}}.";
                        step.Kind = StepKind.MouseMoveTo; step.X = x; step.Y = y;
                        return null;
                    case "WINDOWXY":
                        if (!Pair(args, -100000, 100000, out x, out y)) return tag + " needs two numbers, like {{WINDOWXY:100,200}}: the spot measured from the top-left corner of the window in front.";
                        step.Kind = StepKind.MouseMoveInWindow; step.X = x; step.Y = y;
                        return null;
                    case "MOUSEMOVE":
                        if (!Pair(args, -100000, 100000, out x, out y)) return tag + " needs two numbers, like {{MOUSEMOVE:10,-5}}.";
                        step.Kind = StepKind.MouseMoveBy; step.X = x; step.Y = y;
                        return null;
                    case "MOUSEPOS":
                        if (!Pair(args, 0, 65535, out x, out y)) return tag + " needs two numbers from 0 to 65535, like {{MOUSEPOS:32768,32768}}.";
                        step.Kind = StepKind.MouseMoveNormalized; step.X = x; step.Y = y;
                        return null;

                    case "MLEFTDOWN": return Button(tag, args, step, StepKind.MouseDown, MouseButton.Left);
                    case "MLEFTUP": return Button(tag, args, step, StepKind.MouseUp, MouseButton.Left);
                    case "MLEFTDBLCLICK": return Button(tag, args, step, StepKind.MouseDoubleClick, MouseButton.Left);
                    case "MRIGHTDOWN": return Button(tag, args, step, StepKind.MouseDown, MouseButton.Right);
                    case "MRIGHTUP": return Button(tag, args, step, StepKind.MouseUp, MouseButton.Right);
                    case "MRIGHTDBLCLICK": return Button(tag, args, step, StepKind.MouseDoubleClick, MouseButton.Right);
                    case "MMIDDLEDOWN": return Button(tag, args, step, StepKind.MouseDown, MouseButton.Middle);
                    case "MMIDDLEUP": return Button(tag, args, step, StepKind.MouseUp, MouseButton.Middle);

                    case "MSCROLLUP":
                    case "MSCROLLDOWN":
                    case "MSCROLLLEFT":
                    case "MSCROLLRIGHT":
                        n = 1;
                        if (args.Length > 1 || (args.Length == 1 && !Number(args[0], 1, MaxNotches, out n))) return "The amount in " + tag + " must be a whole number of notches from 1 to 1000.";
                        step.Kind = StepKind.MouseWheel; step.Count = n;
                        step.Horizontal = name == "MSCROLLLEFT" || name == "MSCROLLRIGHT";
                        step.Direction = name == "MSCROLLUP" || name == "MSCROLLRIGHT" ? 1 : -1;
                        return null;

                    case "MSAVEPOS": return Plain(tag, args, step, StepKind.SaveMousePos);
                    case "MLOADPOS": return Plain(tag, args, step, StepKind.LoadMousePos);
                    case "VARUNSETALL": return Plain(tag, args, step, StepKind.UnsetAllVars);

                    case "VARSET":
                        if (args.Length == 0) return tag + " needs a name and a value, like {{VARSET:row:1}}.";
                        if (!ValidName(ref args[0])) return NameProblem(tag);
                        step.Kind = StepKind.SetVar; step.Var = args[0]; step.Value = string.Join(":", args.Skip(1));
                        return null;
                    case "OUTPUT":
                    case "VARUNSET":
                    case "VARSETFROMCLIPBOARD":
                        if (args.Length != 1) return tag + " needs one variable name, like {{" + name + ":row}}.";
                        if (!ValidName(ref args[0])) return NameProblem(tag);
                        step.Var = args[0];
                        step.Kind = name == "OUTPUT" ? StepKind.OutputVar : name == "VARUNSET" ? StepKind.UnsetVar : StepKind.VarFromClipboard;
                        return null;
                    case "SETCLIPBOARD":
                        if (args.Length == 0) return tag + " needs the text to put on the clipboard, like {{SETCLIPBOARD:hello}}.";
                        step.Kind = StepKind.SetClipboard; step.Value = string.Join(":", args);
                        return null;

                    case "INPUT":
                        // {{INPUT:name}} or {{INPUT:name:The question to ask}}
                        if (args.Length == 0 || args[0].Length == 0) return tag + " needs a variable name, like {{INPUT:order}}, and optionally a question, like {{INPUT:order:Which order number?}}.";
                        if (!ValidName(ref args[0])) return NameProblem(tag);
                        step.Kind = StepKind.AskInput; step.Var = args[0]; step.Value = string.Join(":", args.Skip(1)).Trim();
                        return null;

                    case "WAITPIXEL":
                        // {{WAITPIXEL:x,y:#RRGGBB}} or {{WAITPIXEL:x,y:#RRGGBB:5000}}, x and y measured from the window in front
                        int rgb;
                        if (args.Length < 2 || args.Length > 3 || !Pair(new[] { args[0] }, -100000, 100000, out x, out y) || !Color(args[1], out rgb))
                            return tag + " needs a spot in the window and a colour, like {{WAITPIXEL:120,40:#1E90FF}}, and optionally a time limit in ms, like {{WAITPIXEL:120,40:#1E90FF:5000}}.";
                        n = DefaultWaitPixelMs;
                        if (args.Length == 3 && !Number(args[2], 100, MaxPauseMs, out n)) return "The time limit in " + tag + " must be a whole number of milliseconds from 100 to 3,600,000.";
                        step.Kind = StepKind.WaitPixel; step.X = x; step.Y = y; step.Color = rgb; step.Ms = n;
                        return null;
                }
                return "Unknown key or command “" + name + "”.";
            }

            static string Button(string tag, string[] args, MacroStep step, StepKind kind, MouseButton button)
            {
                if (args.Length != 0) return tag + " doesn't take a value.";
                step.Kind = kind; step.Button = button;
                return null;
            }

            static string Plain(string tag, string[] args, MacroStep step, StepKind kind)
            {
                if (args.Length != 0) return tag + " doesn't take a value.";
                step.Kind = kind;
                return null;
            }

            // Accepts "name" or "$name" and rewrites it to "name".
            static bool ValidName(ref string name)
            {
                if (name.StartsWith("$", StringComparison.Ordinal)) name = name.Substring(1);
                return VariableName.IsMatch(name);
            }

            static string NameProblem(string tag)
            {
                return "A variable name in " + tag + " can only use letters, numbers and underscores.";
            }
        }

        // Walks the script once, turning literal text and {{commands}} into steps and errors.
        sealed class Parser
        {
            readonly string script;
            readonly bool newLineIsEnter;
            readonly ParseResult result = new ParseResult();
            readonly List<int> lineStarts = new List<int> { 0 };
            readonly StringBuilder literal = new StringBuilder();
            readonly List<int> open = new List<int>();   // indexes of the LOOP, IF and ELSE steps still waiting for their end
            int literalStart = -1;

            public Parser(string script, bool newLineIsEnter)
            {
                this.script = script;
                this.newLineIsEnter = newLineIsEnter;
                for (int i = 0; i < script.Length; i++)
                {
                    if (script[i] == '\n' || (script[i] == '\r' && !(i + 1 < script.Length && script[i + 1] == '\n'))) lineStarts.Add(i + 1);
                }
            }

            public ParseResult Run()
            {
                int i = 0;
                int n = script.Length;
                while (i < n)
                {
                    char c = script[i];
                    if (c == '{' && i + 1 < n && script[i + 1] == '{')
                    {
                        if (i + 2 < n && script[i + 2] == '{')
                        {
                            // Three or more braces in a row: the extra ones in front are plain text.
                            AddLiteral("{", i);
                            i++;
                            continue;
                        }
                        FlushLiteral(i);
                        i = ParseCommand(i);
                        continue;
                    }
                    if (c == '\r' || c == '\n')
                    {
                        int breakLength = c == '\r' && i + 1 < n && script[i + 1] == '\n' ? 2 : 1;
                        if (newLineIsEnter) AddLiteral("\n", i);
                        i += breakLength;
                        continue;
                    }
                    AddLiteral(c.ToString(), i);
                    i++;
                }
                FlushLiteral(n);
                foreach (int start in open)
                {
                    StepKind kind = BlockKind(start);
                    int first = BlockStart(start);
                    MacroStep step = result.Steps[first];
                    AddError(step.Index, step.Length, "This {{" + StartWord(kind) + "}} has no {{" + EndWord(kind) + "}} after it.");
                }
                return result;
            }

            void AddLiteral(string text, int index)
            {
                if (literal.Length == 0) literalStart = index;
                literal.Append(text);
            }

            void FlushLiteral(int endIndex)
            {
                if (literal.Length == 0) return;
                AddStep(new MacroStep { Kind = StepKind.Text, Text = literal.ToString() }, literalStart, endIndex - literalStart);
                literal.Clear();
            }

            // Parses the command starting at "{{" and returns the index just after it.
            int ParseCommand(int start)
            {
                int lineEnd = start;
                while (lineEnd < script.Length && script[lineEnd] != '\r' && script[lineEnd] != '\n') lineEnd++;

                int p = start + 2;
                while (p < lineEnd && (script[p] == ' ' || script[p] == '\t')) p++;
                if (p + 1 < lineEnd && script[p] == '/' && script[p + 1] == '/')
                {
                    // A comment hides the rest of its line, including the line break.
                    int after = lineEnd;
                    if (after < script.Length) after += script[after] == '\r' && after + 1 < script.Length && script[after + 1] == '\n' ? 2 : 1;
                    return after;
                }

                int close = script.IndexOf("}}", start + 2, lineEnd - (start + 2), StringComparison.Ordinal);
                if (close < 0)
                {
                    AddError(start, lineEnd - start, "This command is missing its closing }}.");
                    return lineEnd;
                }
                ParseBody(script.Substring(start + 2, close - (start + 2)), start, close + 2 - start);
                return close + 2;
            }

            void ParseBody(string content, int index, int length)
            {
                string body = content.Trim();
                if (body.Length == 0)
                {
                    AddError(index, length, "This command is empty. Put a key or a command name between the braces.");
                    return;
                }
                if (GroupSeparator.IsMatch(body))
                {
                    ParseShortcut(GroupSeparator.Split(body).Select(k => k.Trim()).ToArray(), index, length);
                    return;
                }
                if (body[0] == '$')
                {
                    BuildCommand("OUTPUT", new[] { body.Substring(1).Trim() }, index, length);
                    return;
                }

                int colon = body.IndexOf(':');
                string rawName = (colon < 0 ? body : body.Substring(0, colon)).Trim();
                string name = rawName.ToUpperInvariant();
                string[] args = colon < 0 ? new string[0] : body.Substring(colon + 1).Split(':').Select(a => a.Trim()).ToArray();
                if (colon >= 0 && (name == "VARSET" || name == "SETCLIPBOARD" || name == "INPUT"))
                {
                    // The text of a value is exact: spaces at its ends and around inner colons stay. Only a variable name is trimmed.
                    string[] exact = content.Substring(content.IndexOf(':') + 1).Split(':');
                    if (name != "SETCLIPBOARD") exact[0] = exact[0].Trim();
                    args = exact;
                }

                if (Actions.Contains(name))
                {
                    ParseAction(name, colon < 0 ? null : content.Substring(content.IndexOf(':') + 1), index, length);
                    return;
                }
                if (Flow.Contains(name))
                {
                    ParseFlow(name, colon < 0 ? null : content.Substring(content.IndexOf(':') + 1), index, length);
                    return;
                }
                if (Commands.Contains(name))
                {
                    BuildCommand(name, args, index, length);
                    return;
                }
                if (Unsupported.Contains(name))
                {
                    AddError(index, length, "{{" + name + "}} is a Super Macro command Macro Kit doesn't support.");
                    return;
                }
                ushort vk;
                MouseButton button;
                bool isKey = KeyNames.TryGetVk(rawName, out vk);
                bool isMouse = !isKey && KeyNames.IsMouseButton(rawName, out button);
                if (colon >= 0 && (isKey || isMouse))
                {
                    AddError(index, length, "“" + rawName + "” is a key and doesn't take a value. To press it several times use {{KEYPRESS:" + rawName + ":3}}.");
                    return;
                }
                if (colon < 0 && (isKey || isMouse))
                {
                    ParseShortcut(new[] { rawName }, index, length);
                    return;
                }
                AddError(index, length, "Unknown key or command “" + rawName + "”.");
            }

            // {{CTRL}{C}}: every name is a key (press in order, release in reverse), or all of them are mouse buttons (clicks).
            void ParseShortcut(string[] names, int index, int length)
            {
                var keys = new List<ushort>();
                var buttons = new List<MouseButton>();
                foreach (string name in names)
                {
                    ushort vk;
                    MouseButton button;
                    if (KeyNames.TryGetVk(name, out vk)) keys.Add(vk);
                    else if (KeyNames.IsMouseButton(name, out button)) buttons.Add(button);
                    else if (name.Equals("PAUSE", StringComparison.OrdinalIgnoreCase))
                    {
                        AddError(index, length, "“PAUSE” is the wait command. The Pause/Break key is called BREAK.");
                        return;
                    }
                    else
                    {
                        AddError(index, length, "Unknown key or command “" + name + "”.");
                        return;
                    }
                }
                if (keys.Count > 0 && buttons.Count > 0)
                {
                    AddError(index, length, "A shortcut can't mix keys and mouse buttons. Use {{KEYDOWN:...}} and {{LBUTTON}} one after the other instead.");
                    return;
                }
                if (buttons.Count > 0)
                {
                    foreach (MouseButton button in buttons) AddStep(new MacroStep { Kind = StepKind.MouseClick, Button = button }, index, length);
                    return;
                }
                AddStep(new MacroStep { Kind = StepKind.Keys, Keys = keys.ToArray() }, index, length);
            }

            void BuildCommand(string name, string[] args, int index, int length)
            {
                MacroStep step;
                string error = Build(name, args, null, out step);
                if (error != null) AddError(index, length, error);
                else AddStep(step, index, length);
            }

            // The kind of block an open step belongs to: an Else stands for its If, an Or for the Switch or Random it is part of.
            StepKind BlockKind(int open)
            {
                MacroStep step = result.Steps[open];
                if (step.Kind == StepKind.Else || step.Kind == StepKind.IfWindow) return StepKind.If;
                if (step.Kind == StepKind.Or) return result.Steps[step.Jump].Kind;
                return step.Kind;
            }

            // While a Switch or Random block is still open, each of its Or steps points back at the block's first step.
            int BlockStart(int open)
            {
                MacroStep step = result.Steps[open];
                return step.Kind == StepKind.Or ? step.Jump : open;
            }

            static string StartWord(StepKind block)
            {
                switch (block)
                {
                    case StepKind.LoopStart: return "LOOP";
                    case StepKind.SwitchStart: return "SWITCH";
                    case StepKind.RandomStart: return "RANDOM";
                    default: return "IF";
                }
            }

            static string EndWord(StepKind block)
            {
                switch (block)
                {
                    case StepKind.LoopStart: return "ENDLOOP";
                    case StepKind.SwitchStart: return "ENDSWITCH";
                    case StepKind.RandomStart: return "ENDRANDOM";
                    default: return "ENDIF";
                }
            }

            // {{LOOP:n}} ... {{ENDLOOP}}, {{IF:a=b}} or {{IFWINDOW:text}} ... {{ELSE}} ... {{ENDIF}}, and
            // {{SWITCH}} or {{RANDOM}} ... {{OR}} ... {{ENDSWITCH}} / {{ENDRANDOM}}. Blocks nest.
            void ParseFlow(string name, string arg, int index, int length)
            {
                if (ProFlow.Contains(name) && !Edition.IsPro)
                {
                    AddError(index, length, "{{" + name + "}}: conditions come with Macro Kit Pro. Enter your license key in Settings. Repeat blocks ({{LOOP}}) are free.");
                    return;
                }
                string value = (arg ?? "").Trim();
                int top = open.Count == 0 ? -1 : open[open.Count - 1];
                StepKind block = top < 0 ? StepKind.Text : BlockKind(top);
                StepKind topKind = top < 0 ? StepKind.Text : result.Steps[top].Kind;
                string closeFirst = top < 0 ? null : "Close the {{" + StartWord(block) + "}} with {{" + EndWord(block) + "}} before this {{" + name + "}}.";
                var step = new MacroStep();
                switch (name)
                {
                    case "LOOP":
                        if (value.Length == 0) { AddError(index, length, "Say how many times, for example {{LOOP:5}}."); return; }
                        if (value[0] == '$')
                        {
                            string variable = value.Substring(1).Trim();
                            if (!VariableName.IsMatch(variable)) { AddError(index, length, "\u201C" + value + "\u201D isn't a variable name. Use letters, digits and _."); return; }
                            step.Var = variable;
                            step.Count = 0;
                        }
                        else
                        {
                            int count;
                            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1 || count > MaxRepeat)
                            {
                                AddError(index, length, "{{LOOP}} repeats 1 to " + MaxRepeat.ToString("N0", CultureInfo.InvariantCulture) + " times, or as many times as a $variable says.");
                                return;
                            }
                            step.Count = count;
                        }
                        step.Kind = StepKind.LoopStart;
                        open.Add(AddFlowStep(step, index, length));
                        return;
                    case "ENDLOOP":
                        if (topKind != StepKind.LoopStart)
                        {
                            AddError(index, length, top < 0 ? "{{ENDLOOP}} has no {{LOOP}} before it." : closeFirst);
                            return;
                        }
                        step.Kind = StepKind.LoopEnd;
                        step.Jump = top;
                        result.Steps[top].Jump = AddFlowStep(step, index, length);
                        open.RemoveAt(open.Count - 1);
                        return;
                    case "IF":
                        string left, op, right;
                        if (!SplitComparison(value, out left, out op, out right))
                        {
                            AddError(index, length, "{{IF}} needs a comparison, for example {{IF:$name=Sam}}. Use =, !=, <, >, <=, >= or ~ (contains).");
                            return;
                        }
                        step.Kind = StepKind.If;
                        step.Left = left;
                        step.Op = op;
                        step.Right = right;
                        open.Add(AddFlowStep(step, index, length));
                        return;
                    case "IFWINDOW":
                        if (value.Length == 0) { AddError(index, length, "Say which window, for example {{IFWINDOW:Notepad}} (part of its title or its program name)."); return; }
                        step.Kind = StepKind.IfWindow;
                        step.Value = value;
                        open.Add(AddFlowStep(step, index, length));
                        return;
                    case "ELSE":
                        if (topKind != StepKind.If && topKind != StepKind.IfWindow)
                        {
                            AddError(index, length, topKind == StepKind.Else ? "This {{IF}} already has an {{ELSE}}." : top < 0 ? "{{ELSE}} has no {{IF}} before it." : closeFirst);
                            return;
                        }
                        step.Kind = StepKind.Else;
                        int elseIndex = AddFlowStep(step, index, length);
                        result.Steps[top].Jump = elseIndex;
                        open[open.Count - 1] = elseIndex;
                        return;
                    case "ENDIF":
                        if (block != StepKind.If)
                        {
                            AddError(index, length, top < 0 ? "{{ENDIF}} has no {{IF}} before it." : closeFirst);
                            return;
                        }
                        step.Kind = StepKind.EndIf;
                        result.Steps[top].Jump = AddFlowStep(step, index, length);
                        open.RemoveAt(open.Count - 1);
                        return;
                    case "SWITCH":
                    case "RANDOM":
                        if (value.Length > 0) { AddError(index, length, "{{" + name + "}} doesn't take a value. Put {{OR}} between the choices."); return; }
                        step.Kind = name == "SWITCH" ? StepKind.SwitchStart : StepKind.RandomStart;
                        int startIndex = AddFlowStep(step, index, length);
                        step.Branches = new[] { startIndex };
                        open.Add(startIndex);
                        return;
                    case "OR":
                        if (block != StepKind.SwitchStart && block != StepKind.RandomStart)
                        {
                            AddError(index, length, top < 0 ? "{{OR}} goes between the choices of a {{SWITCH}} or {{RANDOM}} block." : closeFirst);
                            return;
                        }
                        if (value.Length > 0) { AddError(index, length, "{{OR}} doesn't take a value."); return; }
                        int blockStart = BlockStart(top);
                        step.Kind = StepKind.Or;
                        step.Jump = blockStart;   // until the block ends; then every Or jumps to the end
                        int orIndex = AddFlowStep(step, index, length);
                        MacroStep starter = result.Steps[blockStart];
                        starter.Branches = starter.Branches.Concat(new[] { orIndex }).ToArray();
                        open[open.Count - 1] = orIndex;
                        return;
                    case "ENDSWITCH":
                    case "ENDRANDOM":
                        StepKind wanted = name == "ENDSWITCH" ? StepKind.SwitchStart : StepKind.RandomStart;
                        if (block != wanted)
                        {
                            AddError(index, length, top < 0 ? "{{" + name + "}} has no {{" + StartWord(wanted) + "}} before it." : closeFirst);
                            return;
                        }
                        int first = BlockStart(top);
                        step.Kind = name == "ENDSWITCH" ? StepKind.EndSwitch : StepKind.EndRandom;
                        step.Jump = first;
                        int endIndex = AddFlowStep(step, index, length);
                        foreach (int branch in result.Steps[first].Branches) result.Steps[branch].Jump = endIndex;
                        open.RemoveAt(open.Count - 1);
                        return;
                }
            }

            // {{RUN:...}}, {{CLOSE:...}}, {{PLAYSOUND:...}}, {{LOCK}}, {{VOLUME:...}}, {{MACRO:...}}, {{FOCUS:...}}, {{WAITWINDOW:...}}.
            void ParseAction(string name, string arg, int index, int length)
            {
                string tag = "{{" + name + "}}";
                string value = (arg ?? "").Trim();
                var step = new MacroStep { Value = value };
                switch (name)
                {
                    case "RUN":
                        if (value.Length == 0) { AddError(index, length, tag + " needs what to open, like {{RUN:notepad}}, {{RUN:C:\\Notes\\todo.txt}} or {{RUN:https://example.com}}."); return; }
                        step.Kind = StepKind.Open;
                        break;
                    case "CLOSE":
                        if (value.Length == 0) { AddError(index, length, tag + " needs a program name, like {{CLOSE:notepad.exe}}."); return; }
                        step.Kind = StepKind.CloseApp;
                        break;
                    case "PLAYSOUND":
                        if (value.Length == 0) { AddError(index, length, tag + " needs a sound file, like {{PLAYSOUND:C:\\Sounds\\ding.wav}}."); return; }
                        step.Kind = StepKind.PlaySound;
                        break;
                    case "LOCK":
                        if (value.Length > 0) { AddError(index, length, tag + " doesn't take a value."); return; }
                        step.Kind = StepKind.Lock;
                        step.Value = null;
                        break;
                    case "VOLUME":
                        step.Kind = StepKind.Volume;
                        if (value.IndexOf('$') >= 0) break;   // filled in at run time
                        int amount;
                        bool relative;
                        if (!TryVolume(value, out amount, out relative))
                        {
                            AddError(index, length, tag + " needs a level from 0 to 100, like {{VOLUME:50}}, or a change, like {{VOLUME:+10}} or {{VOLUME:-10}}.");
                            return;
                        }
                        step.Count = amount;
                        step.Relative = relative;
                        step.Value = null;
                        break;
                    case "MACRO":
                        if (value.Length == 0) { AddError(index, length, tag + " needs the name of another macro, like {{MACRO:Copy row}}."); return; }
                        step.Kind = StepKind.RunMacro;
                        break;
                    case "FOCUS":
                        if (value.Length == 0) { AddError(index, length, tag + " needs part of a window's title or its program name, like {{FOCUS:Notepad}}."); return; }
                        step.Kind = StepKind.FocusWindow;
                        break;
                    case "WAITWINDOW":
                        string window;
                        int timeout;
                        if (!TryWaitWindow(value, out window, out timeout))
                        {
                            AddError(index, length, tag + " needs part of a window's title or its program name, like {{WAITWINDOW:Notepad}}, and optionally a time limit from 100 to 3,600,000 ms, like {{WAITWINDOW:Notepad:5000}}.");
                            return;
                        }
                        step.Kind = StepKind.WaitWindow;
                        step.Value = window;
                        step.Ms = timeout;
                        break;
                }
                AddStep(step, index, length);
            }

            int AddFlowStep(MacroStep step, int index, int length)
            {
                AddStep(step, index, length);
                return result.Steps.Count - 1;
            }

            void AddStep(MacroStep step, int index, int length)
            {
                step.Index = index;
                step.Length = length;
                Locate(index, out step.Line, out step.Column);
                result.Steps.Add(step);
            }

            void AddError(int index, int length, string message)
            {
                var error = new ParseError { Index = index, Length = length, Message = message };
                Locate(index, out error.Line, out error.Column);
                result.Errors.Add(error);
            }

            void Locate(int index, out int line, out int column)
            {
                int low = 0, high = lineStarts.Count - 1;
                while (low < high)
                {
                    int mid = (low + high + 1) / 2;
                    if (lineStarts[mid] <= index) low = mid; else high = mid - 1;
                }
                line = low + 1;
                column = index - lineStarts[low] + 1;
            }
        }
    }
}
