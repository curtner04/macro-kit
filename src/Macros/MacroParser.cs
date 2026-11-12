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
            "PAUSE", "KEYPRESS", "KEYDOWN", "KEYUP", "MOUSEXY", "MOUSEMOVE", "MOUSEPOS",
            "MLEFTDOWN", "MLEFTUP", "MLEFTDBLCLICK", "MRIGHTDOWN", "MRIGHTUP", "MRIGHTDBLCLICK", "MMIDDLEDOWN", "MMIDDLEUP",
            "MSCROLLUP", "MSCROLLDOWN", "MSCROLLLEFT", "MSCROLLRIGHT", "MSAVEPOS", "MLOADPOS",
            "VARSET", "OUTPUT", "VARUNSET", "VARUNSETALL", "SETCLIPBOARD", "VARSETFROMCLIPBOARD"
        };

        // Super Macro commands that need files, prompts or program flow. Macro Kit leaves them out on purpose.
        static readonly HashSet<string> Unsupported = new HashSet<string>
        {
            "INPUT", "OUTPUTTOFILE", "APPENDTOFILE", "VARSETFROMFILE", "VARSETFROMENV", "SETICONFROMFILE", "SETKEYTITLE", "RUN", "PLAYSOUND",
            "FUNCTION", "ENDFUNCTION", "CALL", "LOOP", "ENDLOOP", "FOR", "ENDFOR", "WHILE", "ENDWHILE", "IF", "ENDIF"
        };

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
                if (colon >= 0 && (name == "VARSET" || name == "SETCLIPBOARD"))
                {
                    // The text of a value is exact: spaces at its ends and around inner colons stay. Only a variable name is trimmed.
                    string[] exact = content.Substring(content.IndexOf(':') + 1).Split(':');
                    if (name == "VARSET") exact[0] = exact[0].Trim();
                    args = exact;
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
