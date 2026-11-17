using System;
using System.Collections.Generic;
using System.Linq;
using MacroKit.Core;

namespace MacroKit.Macros
{
    public enum StepKind
    {
        Keys, KeyDown, KeyUp, KeyRepeat, Text, Pause, MouseMoveTo, MouseMoveBy, MouseMoveNormalized,
        MouseDown, MouseUp, MouseClick, MouseDoubleClick, MouseWheel, SaveMousePos, LoadMousePos,
        SetVar, OutputVar, UnsetVar, UnsetAllVars, SetClipboard, VarFromClipboard,
        // Flow steps. Jump links each block's start and end (see MacroParser.ParseFlow). Loops, switches and random picks are free;
        // conditions (If, IfWindow, Else, EndIf) come with Macro Kit Pro.
        LoopStart, LoopEnd, If, IfWindow, Else, EndIf,
        SwitchStart, RandomStart, Or, EndSwitch, EndRandom,
        // Stream Deck-style actions. Value holds the raw argument, with $names still to be filled in at run time.
        Open, CloseApp, PlaySound, Lock, Volume, RunMacro, FocusWindow, WaitWindow
    }

    /// <summary>One parsed command (or run of literal text) of a Super Macro script.</summary>
    public sealed class MacroStep
    {
        public StepKind Kind;
        public ushort[] Keys;          // Keys: every key of the shortcut in press order. KeyDown / KeyUp / KeyRepeat: one key.
        public string Text;            // Text: the literal text ('\n' = Enter key)
        public int Ms;                 // Pause: the wait. KeyRepeat: the delay between presses (0 = use the key delay).
        public int X;                  // Mouse moves: x (or dx).
        public int Y;
        public int Count = 1;          // KeyRepeat: presses. MouseWheel: notches.
        public MouseButton Button;
        public bool Horizontal;        // MouseWheel
        public int Direction = 1;      // MouseWheel: +1 = up / right, -1 = down / left
        public string Var;             // Variable name (without the $)
        public string Value;           // SetVar / SetClipboard: the value, with $names still to be filled in at run time
        public string Command;         // Only for a command whose numbers contain $names: its upper-case name; Args hold the raw arguments.
        public string[] Args;
        public int Jump = -1;          // Flow steps: LoopStart -> its LoopEnd, LoopEnd -> its LoopStart, If / IfWindow -> its Else or EndIf, Else -> its EndIf,
                                       // SwitchStart / RandomStart / Or -> the block's end
        public int[] Branches;         // SwitchStart / RandomStart: the index of the step each choice follows (the start itself, then every Or)
        public bool Relative;          // Volume: Count is a change (+10 / -10) rather than a level
        public string Left;            // If: the two sides (with $names still to be filled in) and the comparison
        public string Op;
        public string Right;
        public int Index;              // Source span in the script
        public int Length;
        public int Line;               // 1-based
        public int Column;             // 1-based

        /// <summary>True when a number in the command contains a $name, so the runner has to fill it in before the step can run.</summary>
        public bool IsDynamic { get { return Args != null; } }

        /// <summary>True for steps that send keyboard or mouse input (the key delay follows them and the focus guard checks before them).</summary>
        public bool SendsInput
        {
            get
            {
                switch (Kind)
                {
                    case StepKind.Pause:
                    case StepKind.SaveMousePos:
                    case StepKind.SetVar:
                    case StepKind.UnsetVar:
                    case StepKind.UnsetAllVars:
                    case StepKind.SetClipboard:
                    case StepKind.VarFromClipboard:
                    case StepKind.LoopStart:
                    case StepKind.LoopEnd:
                    case StepKind.If:
                    case StepKind.IfWindow:
                    case StepKind.Else:
                    case StepKind.EndIf:
                    case StepKind.SwitchStart:
                    case StepKind.RandomStart:
                    case StepKind.Or:
                    case StepKind.EndSwitch:
                    case StepKind.EndRandom:
                    case StepKind.Open:
                    case StepKind.CloseApp:
                    case StepKind.PlaySound:
                    case StepKind.Lock:
                    case StepKind.Volume:
                    case StepKind.RunMacro:
                    case StepKind.FocusWindow:
                    case StepKind.WaitWindow:
                        return false;
                    default:
                        return true;
                }
            }
        }

        /// <summary>A plain-English line for the steps list: "Press Ctrl+C", "Wait 50 ms", "Press Down 5 times, 100 ms apart".</summary>
        public string Describe()
        {
            if (IsDynamic) return "Run {{" + Command + (Args.Length > 0 ? ":" + string.Join(":", Args) : "") + "}}";
            switch (Kind)
            {
                case StepKind.Keys: return "Press " + KeyList();
                case StepKind.KeyDown: return "Hold " + KeyList();
                case StepKind.KeyUp: return "Release " + KeyList();
                case StepKind.KeyRepeat:
                    if (Count == 1) return "Press " + KeyList();
                    return "Press " + KeyList() + " " + Count + " times" + (Ms > 0 ? ", " + Ms + " ms apart" : "");
                case StepKind.Text:
                    if (Text == "\n") return "Press Enter";
                    return "Type “" + Shorten(Text) + "”";
                case StepKind.Pause: return "Wait " + Ms + " ms";
                case StepKind.MouseMoveTo: return "Move mouse to " + X + ", " + Y;
                case StepKind.MouseMoveBy: return "Move mouse by " + X + ", " + Y;
                case StepKind.MouseMoveNormalized: return "Move mouse to " + X + ", " + Y + " (0 to 65535 scale)";
                case StepKind.MouseDown: return "Hold " + ButtonName() + " mouse button";
                case StepKind.MouseUp: return "Release " + ButtonName() + " mouse button";
                case StepKind.MouseClick: return "Click " + ButtonName() + " mouse button";
                case StepKind.MouseDoubleClick: return "Double-click " + ButtonName() + " mouse button";
                case StepKind.MouseWheel:
                    string direction = Horizontal ? (Direction > 0 ? "right" : "left") : (Direction > 0 ? "up" : "down");
                    return "Scroll " + direction + " " + Count + (Count == 1 ? " notch" : " notches");
                case StepKind.SaveMousePos: return "Remember the mouse position";
                case StepKind.LoadMousePos: return "Move mouse back to the remembered position";
                case StepKind.SetVar: return "Set $" + Var + " to “" + Shorten(Value) + "”";
                case StepKind.OutputVar: return "Type the value of $" + Var;
                case StepKind.UnsetVar: return "Forget $" + Var;
                case StepKind.UnsetAllVars: return "Forget all variables";
                case StepKind.SetClipboard: return "Copy “" + Shorten(Value) + "” to the clipboard";
                case StepKind.VarFromClipboard: return "Store the clipboard text in $" + Var;
                case StepKind.LoopStart: return Var != null ? "Repeat the next steps $" + Var + " times" : "Repeat the next steps " + Count + (Count == 1 ? " time" : " times");
                case StepKind.LoopEnd: return "End of repeat";
                case StepKind.If: return "If " + Shorten(Left) + " " + OpWords() + " " + Shorten(Right);
                case StepKind.IfWindow: return "If the active window matches \u201C" + Shorten(Value) + "\u201D";
                case StepKind.Else: return "Otherwise";
                case StepKind.EndIf: return "End of if";
                case StepKind.SwitchStart: return "Take turns: each time, do the next of " + ChoiceCount() + " choices";
                case StepKind.RandomStart: return "Pick one of " + ChoiceCount() + " choices at random";
                case StepKind.Or: return "Next choice";
                case StepKind.EndSwitch:
                case StepKind.EndRandom: return "End of choices";
                case StepKind.Open: return "Open “" + Shorten(Value) + "”";
                case StepKind.CloseApp: return "Close " + Shorten(Value);
                case StepKind.PlaySound: return "Play the sound “" + Shorten(FileName(Value)) + "”";
                case StepKind.Lock: return "Lock the PC";
                case StepKind.Volume:
                    if (Value != null) return "Set the volume from “" + Shorten(Value) + "”";
                    if (!Relative) return "Set the volume to " + Count + "%";
                    return (Count >= 0 ? "Turn the volume up " : "Turn the volume down ") + Math.Abs(Count) + "%";
                case StepKind.RunMacro: return "Run the macro “" + Shorten(Value) + "”";
                case StepKind.FocusWindow: return "Switch to the window “" + Shorten(Value) + "”";
                case StepKind.WaitWindow: return "Wait for the window “" + Shorten(Value) + "” (up to " + Seconds(Ms) + ")";
            }
            return Kind.ToString();
        }

        int ChoiceCount() { return Branches == null ? 1 : Branches.Length; }

        static string FileName(string path)
        {
            path = path ?? "";
            int slash = Math.Max(path.LastIndexOf('\\'),path.LastIndexOf('/'));
            return slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;
        }

        static string Seconds(int ms)
        {
            if (ms % 1000 == 0) return (ms / 1000) + (ms == 1000 ? " second" : " seconds");
            return (ms / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " seconds";
        }

        string OpWords()
        {
            switch (Op)
            {
                case "=": return "is";
                case "!=": return "is not";
                case "<": return "is less than";
                case ">": return "is more than";
                case "<=": return "is at most";
                case ">=": return "is at least";
                case "~": return "contains";
                default: return Op;
            }
        }

        string KeyList()
        {
            return string.Join("+", (Keys ?? new ushort[0]).Select(KeyNames.Display));
        }

        string ButtonName()
        {
            switch (Button)
            {
                case MouseButton.Left: return "left";
                case MouseButton.Right: return "right";
                case MouseButton.Middle: return "middle";
                case MouseButton.X1: return "back (4th)";
                default: return "forward (5th)";
            }
        }

        static string Shorten(string text)
        {
            text = (text ?? "").Replace("\r", "").Replace("\n", "↵").Replace("\t", "→");
            return text.Length <= 40 ? text : text.Substring(0, 40) + "…";
        }
    }

    public sealed class ParseError
    {
        public int Index;
        public int Length;
        public int Line;      // 1-based
        public int Column;    // 1-based
        public string Message;
    }

    public sealed class ParseResult
    {
        public List<MacroStep> Steps = new List<MacroStep>();
        public List<ParseError> Errors = new List<ParseError>();

        public bool Ok { get { return Errors.Count == 0; } }

        /// <summary>
        /// Rough time one pass takes: every wait plus the key delay after each step that sends input.
        /// Steps whose numbers come from variables count only their key delay. A {{LOOP:n}} multiplies what it holds
        /// (a $variable count counts once); both branches of an {{IF}} count.
        /// </summary>
        public long EstimatedMs(MacroDef m)
        {
            int keyDelay = m == null ? 0 : Math.Max(0, m.KeyDelayMs);
            var totals = new Stack<long>();
            var times = new Stack<long>();
            long total = 0;
            foreach (MacroStep step in Steps)
            {
                if (step.Kind == StepKind.LoopStart)
                {
                    totals.Push(total);
                    times.Push(step.Var != null ? 1 : Math.Max(1, step.Count));
                    total = 0;
                }
                else if (step.Kind == StepKind.LoopEnd && totals.Count > 0)
                {
                    long inner = total;
                    long repeat = times.Pop();
                    total = totals.Pop() + (inner > long.MaxValue / Math.Max(1, repeat) ? long.MaxValue / 2 : inner * repeat);
                }
                else if (step.Kind == StepKind.Pause) total += step.Ms;
                else if (step.Kind == StepKind.KeyRepeat) total += (long)(step.Count - 1) * (step.Ms > 0 ? step.Ms : keyDelay) + keyDelay;
                else if (step.SendsInput) total += keyDelay;
            }
            while (totals.Count > 0) total += totals.Pop();
            return total;
        }
    }
}
