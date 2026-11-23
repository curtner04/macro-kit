using System;
using System.Collections.Generic;
using System.Linq;
using MacroKit.Core;
using MacroKit.Macros;

namespace MacroKit.Tests
{
    /// <summary>The plain macro language: plain lines to Super Macro text and back.</summary>
    public static class PlainMacroTests
    {
        static string S(string plain)
        {
            PlainResult r = PlainMacro.ToScript(plain);
            Assert.True(r.Ok, "should understand: " + plain + " -> " + string.Join("; ", r.Lines.Where(l => l.Error != null).Select(l => l.Error)));
            return r.Script;
        }

        static string Bad(string plain)
        {
            PlainResult r = PlainMacro.ToScript(plain);
            Assert.False(r.Ok, "should not understand: " + plain);
            return r.Lines.First(l => l.Error != null).Error;
        }

        static string[] Describe(string script, bool newLineIsEnter = true)
        {
            ParseResult r = MacroParser.Parse(script, newLineIsEnter);
            Assert.True(r.Ok, "script should parse: " + script + " -> " + string.Join("; ", r.Errors.Select(e => e.Message)));
            // Text with a line break presses Enter, the same as the separate Enter line plain text uses.
            var lines = new List<string>();
            foreach (MacroStep s in r.Steps)
            {
                if (s.Kind != StepKind.Text || s.Text == "\n") { lines.Add(s.Kind == StepKind.Text ? "Press Enter" : s.Describe()); continue; }
                string[] parts = s.Text.Split('\n');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0) lines.Add("Press Enter");
                    if (parts[i].Length > 0) lines.Add("Type " + parts[i]);
                }
            }
            return lines.ToArray();
        }

        [Test]
        public static void LeftClickIsSuggestedAndUnderstood()
        {
            Assert.True(PlainMacro.Suggestions.Any(s => s[0] == "left click"), "left click is in the suggestions");
            Assert.Equal(S("click"), S("left click"));
            // Enter takes the highlighted suggestion only for a line that isn't a step yet ("typ"), not for one that is ("click").
            PlainKind kind; string error;
            Assert.True(PlainMacro.Translate("typ", out kind, out error) == null || error != null, "typ is not a step");
            Assert.True(PlainMacro.Translate("click", out kind, out error) != null && error == null, "click is a step");
        }

        [Test]
        public static void KeysAndShortcuts()
        {
            Assert.Equal("{{CTRL}{C}}", S("ctrl+c"));
            Assert.Equal("{{CTRL}{C}}", S("Ctrl + C"));
            Assert.Equal("{{CTRL}{C}}", S("copy"));
            Assert.Equal("{{CTRL}{A}}", S("select all"));
            Assert.Equal("{{CTRL}{SHIFT}{ESCAPE}}", S("ctrl+shift+esc"));
            Assert.Equal("{{ENTER}}", S("enter"));
            Assert.Equal("{{ENTER}}", S("press Enter"));
            Assert.Equal("{{PAGEUP}}", S("page up"));
            Assert.Equal("{{F5}}", S("f5"));
            Assert.Equal("{{END}}", S("end"));
            Assert.Equal("{{VOLUME_MUTE}}", S("mute"));
            Assert.Equal("{{MEDIA_PLAY_PAUSE}}", S("play/pause"));
        }

        [Test]
        public static void RepeatedKeys()
        {
            Assert.Equal("{{KEYPRESS:SPACE:10:100}}", S("space 10 times every 100ms"));
            Assert.Equal("{{KEYPRESS:TAB:3}}", S("tab x3"));
            Assert.Equal("{{KEYPRESS:DOWN:5}}", S("press down 5 times"));
            Assert.Equal("{{LOOP:2}}{{CTRL}{V}}{{ENDLOOP}}", S("ctrl+v 2 times"));
        }

        [Test]
        public static void WaitsAndText()
        {
            Assert.Equal("{{PAUSE:50}}", S("wait 50ms"));
            Assert.Equal("{{PAUSE:50}}", S("wait 50"));
            Assert.Equal("{{PAUSE:2000}}", S("wait 2s"));
            Assert.Equal("{{PAUSE:1500}}", S("pause 1.5 seconds"));
            Assert.Equal("{{PAUSE:60000}}", S("wait 1 minute"));
            Assert.Equal("hello world", S("type hello world"));
            Assert.Equal("  spaced ", S("type \"  spaced \""));
            Assert.Equal("{{OUTPUT:name}}", S("type $name"));
            Assert.Equal("$name", S("type \"$name\""));
            Assert.Equal("{{OUTPUT:SMLCB}}{x", S("type {{x"));
        }

        [Test]
        public static void MouseLines()
        {
            Assert.Equal("{{LBUTTON}}", S("click"));
            Assert.Equal("{{RBUTTON}}", S("right click"));
            Assert.Equal("{{MLEFTDBLCLICK}}", S("double click"));
            Assert.Equal("{{MRIGHTDBLCLICK}}", S("right double-click"));
            Assert.Equal("{{MLEFTDOWN}}", S("hold left mouse"));
            Assert.Equal("{{MLEFTUP}}", S("release left mouse button"));
            Assert.Equal("{{MOUSEXY:640,400}}", S("move mouse to 640, 400"));
            Assert.Equal("{{MOUSEMOVE:10,-5}}", S("move mouse by 10,-5"));
            Assert.Equal("{{MSCROLLDOWN:3}}", S("scroll down 3"));
            Assert.Equal("{{MSCROLLUP}}", S("scroll up"));
            // Without the word mouse, left is the arrow key.
            Assert.Equal("{{KEYDOWN:LEFT}}", S("hold left"));
            Assert.Equal("{{KEYDOWN:CTRL}}{{KEYDOWN:SHIFT}}", S("hold ctrl+shift"));
            Assert.Equal("{{KEYUP:SHIFT}}{{KEYUP:CTRL}}", S("release ctrl+shift"));
        }

        [Test]
        public static void ActionsAndBlocks()
        {
            Assert.Equal("{{RUN:notepad}}", S("open notepad"));
            Assert.Equal("{{RUN:https://www.example.com}}", S("open www.example.com"));
            Assert.Equal("{{CLOSE:chrome}}", S("close chrome"));
            Assert.Equal("{{LOCK}}", S("lock pc"));
            Assert.Equal("{{VOLUME:40}}", S("volume 40%"));
            Assert.Equal("{{VOLUME:+10}}", S("volume +10"));
            Assert.Equal("{{VOLUME:-5}}", S("volume down 5"));
            Assert.Equal("{{FOCUS:Discord}}", S("switch to Discord"));
            Assert.Equal("{{WAITWINDOW:Notepad:5000}}", S("wait for Notepad 5s"));
            Assert.Equal("{{WAITWINDOW:Notepad}}", S("wait for Notepad"));
            Assert.Equal("{{MACRO:Daily login}}", S("run macro \"Daily login\""));
            Assert.Equal("{{LOOP:3}}{{ENTER}}{{ENDLOOP}}", S("repeat 3 times\nenter\nend repeat"));
            Assert.Equal("{{SWITCH}}a{{OR}}b{{ENDSWITCH}}", S("take turns\ntype a\nor\ntype b\nend turns"));
            Assert.Equal("{{VARSET:name:Sam}}", S("set $name = Sam"));
            Assert.Equal("{{VARSETFROMCLIPBOARD:row}}", S("set $row = clipboard"));
            Assert.Equal("{{SETCLIPBOARD:hi}}", S("set clipboard to hi"));
            Assert.Equal("{{//}} a note\n{{TAB}}", S("# a note\ntab"));
            Assert.Equal("{{CTRL}{C}}", S("{{CTRL}{C}}"));
        }

        [Test]
        public static void ProblemsAreReportedAndBlockTheRun()
        {
            Assert.Contains(Bad("hello there"), "type");
            Assert.Contains(Bad("wait soon"), "how long");
            Assert.Contains(Bad("repeat 3 times\nenter"), "end repeat");
            Assert.Contains(Bad("end repeat"), "repeat");
            PlainResult r = PlainMacro.ToScript("ctrl+c\nhelo\nctrl+v");
            Assert.Equal(1, r.Lines.Count(l => l.Error != null));
            Assert.Equal(1, r.Lines.First(l => l.Error != null).Number);
            // The unknown line is kept in the script so it survives a reload, and the parser refuses to run it.
            Assert.False(MacroParser.Parse(r.Script, true).Ok);
            Assert.Equal("Ctrl+C\r\nhelo\r\nCtrl+V", PlainMacro.ToPlain(r.Script, true));
        }

        [Test]
        public static void LinesKnowTheirSteps()
        {
            PlainResult r = PlainMacro.ToScript("ctrl+c\n\nwait 50ms\ntype \"hi\"");
            Assert.Equal(3, r.Steps.Count());
            Assert.Equal("Press Ctrl+C", r.Lines[0].Meaning);
            Assert.Equal(PlainKind.Empty, r.Lines[1].Kind);
            Assert.Equal(PlainKind.Wait, r.Lines[2].Kind);
            Assert.Equal("50 ms", PlainMacro.TileLabel(r.Lines[2]));
            Assert.Equal("Ctrl + C", PlainMacro.TileLabel(r.Lines[0]));
            Assert.Equal("hi", PlainMacro.TileLabel(r.Lines[3]));
        }

        [Test]
        public static void ScriptToPlain()
        {
            Assert.Equal("Ctrl+C\r\nwait 50ms\r\nAlt+Tab", PlainMacro.ToPlain("{{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}}", true));
            Assert.Equal("type \"Hi\"\r\nEnter\r\ntype \"there\"", PlainMacro.ToPlain("Hi\nthere", true));
            Assert.Equal("type \"Hithere\"", PlainMacro.ToPlain("Hi\nthere", false));
            Assert.Equal("# note\r\nTab", PlainMacro.ToPlain("{{//}} note\n{{TAB}}", true));
            Assert.Equal("open $x", PlainMacro.ToPlain("{{RUN:$SMDOLLARx}}", true));
            // A $variable in an action's argument keeps its code, so it still fills in at run time.
            Assert.Equal("{{RUN:$path}}", PlainMacro.ToPlain("{{RUN:$path}}", true));
            Assert.Equal("{{MOUSEPOS:1,2}}", PlainMacro.ToPlain("{{MOUSEPOS:1,2}}", true));
        }

        static readonly string[] RoundTripScripts =
        {
            "{{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{CTRL}{V}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{DOWN}}{{PAUSE:50}}",
            "Hello {{SHIFT}{TAB}} world\n{{KEYPRESS:DOWN:5:100}}{{KEYDOWN:SHIFT}}x{{KEYUP:SHIFT}}",
            "{{LBUTTON}{RBUTTON}}{{MLEFTDBLCLICK}}{{MSCROLLDOWN:3}}{{MSCROLLLEFT}}{{MOUSEXY:10,20}}{{MOUSEMOVE:-1,2}}{{MSAVEPOS}}{{MLOADPOS}}",
            "{{VARSET:a:1}}{{OUTPUT:a}}{{VARUNSET:a}}{{VARUNSETALL}}{{SETCLIPBOARD:x y}}{{VARSETFROMCLIPBOARD:b}}{{VARSET:c: spaced }}",
            "{{LOOP:3}}{{SWITCH}}a{{OR}}b{{ENDSWITCH}}{{ENDLOOP}}{{RANDOM}}x{{OR}}y{{ENDRANDOM}}",
            "{{RUN:notepad}}{{RUN:\"C:\\Program Files\\App\\app.exe\" --new}}{{RUN:https://example.com}}{{CLOSE:chrome}}{{LOCK}}{{VOLUME:40}}{{VOLUME:-10}}",
            "{{MACRO:Other one}}{{FOCUS:Notepad}}{{WAITWINDOW:Notepad}}{{WAITWINDOW:Calc:5000}}{{PLAYSOUND:C:\\a b.wav}}",
            "{{//}} a note\n{{ENTER}}{{TAB}}{{F5}}{{VOLUME_UP}}{{MEDIA_NEXT_TRACK}}{{BREAK}}{{NUMPAD5}}{{OEM_MINUS}}",
            "{{KEYPRESS:$key:$n}}{{PAUSE:$wait}}{{LOOP:$times}}{{ENDLOOP}}",
            "  {{CTRL}{V}}  \"quoted\"  "
        };

        [Test]
        public static void ScriptsRoundTripThroughPlain()
        {
            var scripts = RoundTripScripts.ToList();
            foreach (LibraryPack pack in MacroLibrary.Packs) scripts.AddRange(pack.Items.Where(i => !i.IsPhrase).Select(i => i.Script));
            foreach (string script in scripts)
            {
                foreach (bool enter in new[] { true, false }.Where(e => MacroParser.Parse(script, e).Ok))
                {
                    string plain = PlainMacro.ToPlain(script, enter);
                    PlainResult back = PlainMacro.ToScript(plain);
                    Assert.True(back.Ok, "plain should read back: " + script + " -> " + plain + " -> " + string.Join("; ", back.Lines.Where(l => l.Error != null).Select(l => l.Error)));
                    Assert.SequenceEqual(Describe(script, enter), Describe(back.Script, true), "same steps for " + script + " (newLineIsEnter " + enter + ")");
                    // Plain made from plain stays the same.
                    Assert.Equal(plain, PlainMacro.ToPlain(back.Script, true), "stable plain for " + script);
                }
            }
        }

        [Test]
        public static void PlainForKeepsTypedTextWhileItMatches()
        {
            var m = new MacroDef { Plain = "copy\nwait 1s" };
            m.Script = PlainMacro.ToScript(m.Plain).Script;
            Assert.Equal("copy\nwait 1s", PlainMacro.PlainFor(m));
            m.Script = "{{TAB}}";
            Assert.Equal("Tab", PlainMacro.PlainFor(m));
        }

        [Test]
        public static void FirstProblemNamesThePlainLine()
        {
            // The stored script is one line, so the parser says "Line 1" and quotes its own {{?:...}} token; this says the line typed.
            var m = new MacroDef { Plain = "ctrl+c\r\nfrobnicate wildly" };
            m.Script = PlainMacro.ToScript(m.Plain).Script;
            Assert.False(MacroParser.Parse(m.Script, m.NewLineIsEnter).Ok);
            string problem = PlainMacro.FirstProblem(m);
            Assert.True(problem != null && problem.StartsWith("Line 2: ", StringComparison.Ordinal) && !problem.Contains("?:"), problem);
            m.Plain = "ctrl+c";
            m.Script = PlainMacro.ToScript(m.Plain).Script;
            Assert.Null(PlainMacro.FirstProblem(m));
        }

        [Test]
        public static void EverySuggestionIsUnderstoodOrAStarter()
        {
            foreach (string[] s in PlainMacro.Suggestions)
            {
                string text = s[0];
                if (text.EndsWith(" ", StringComparison.Ordinal) || text.EndsWith("\"\"", StringComparison.Ordinal) || text.EndsWith("\\", StringComparison.Ordinal)
                    || text.EndsWith("://", StringComparison.Ordinal) || text.StartsWith("end ", StringComparison.Ordinal) || text == "or" || text == "otherwise"
                    || text.StartsWith("if ", StringComparison.Ordinal) || text.StartsWith("repeat", StringComparison.Ordinal) || text == "take turns" || text == "pick at random")
                    continue;
                PlainKind kind;
                string error;
                Assert.NotNull(PlainMacro.Translate(text, out kind, out error), "suggestion " + text + ": " + error);
            }
        }

        // ---- Sweep A1 ----

        [Test]
        public static void ANoteInCodeDoesNotSwallowTheLinesBelowIt()
        {
            ParseResult a = MacroParser.Parse(S("{{//}} x\nctrl+c"), true);
            Assert.True(a.Ok);
            Assert.Equal(1, a.Steps.Count(s => s.Kind == StepKind.Keys), "the Ctrl+C below a pasted note still runs");
            ParseResult b = MacroParser.Parse(S("{{CTRL}{C}} {{// copy}}\ntab"), true);
            Assert.True(b.Ok);
            Assert.Equal(2, b.Steps.Count(s => s.Kind == StepKind.Keys), "both steps run");
            Assert.Equal(0, b.Steps.Count(s => s.Kind == StepKind.Text && s.Text.Contains("\n")), "no Enter is typed");
        }

        [Test]
        public static void WaitingForAWindowWithAClockTimeInItsTitleReadsBack()
        {
            Assert.Equal("{{WAITWINDOW:Clock 12:00:10000}}", S("wait for Clock 12:00"));
            string window;
            int ms;
            Assert.True(MacroParser.TryWaitWindow("Clock 12:00:10000", out window, out ms));
            Assert.Equal("Clock 12:00", window);
            Assert.Equal(10000, ms);
            Assert.Equal("{{WAITWINDOW:Notepad}}", MacroScript.WaitWindow("Notepad", MacroParser.DefaultWaitWindowMs));
        }

        [Test]
        public static void HoldDownReadsAsTheWordsAndStillAsTheDownKey()
        {
            Assert.Equal("{{KEYDOWN:SHIFT}}", S("hold down shift"));
            Assert.Equal("{{MLEFTDOWN}}", S("hold down left mouse"));
            Assert.Equal("{{KEYDOWN:DOWN}}", S("hold down"));
        }

        [Test]
        public static void ABraceAtTheEndOfTextCannotJoinTheNextLine()
        {
            ParseResult r = MacroParser.Parse(S("type \"a{\"\ntype \"{b}}\""), false);
            Assert.True(r.Ok, string.Join("; ", r.Errors.Select(e => e.Message)));
            Assert.Equal(1, r.Steps.Count(s => s.Kind == StepKind.OutputVar));
            Assert.Equal("a|{b}}", string.Join("|", r.Steps.Where(s => s.Kind == StepKind.Text).Select(s => s.Text)));
            ParseResult two = MacroParser.Parse(S("type \"{\"\ntype \"{\""), false);
            Assert.True(two.Ok);
            Assert.Equal("{{", string.Concat(two.Steps.Select(s => s.Kind == StepKind.Text ? s.Text : "{")));
        }

        [Test]
        public static void TextEndingInABraceStaysOneStep()
        {
            Assert.Equal("a{{{ENTER}}", S("type \"a{\"\nenter"));
            Assert.Equal("a{", MacroScript.Text("a{"));
            PlainLine line = PlainMacro.ToScript("type \"a{\"").Lines[0];
            Assert.False(line.Meaning.Contains("$SMLCB"), line.Meaning);
            Assert.Equal("Type “{”", MacroParser.Parse("{{OUTPUT:SMLCB}}", true).Steps[0].Describe());
        }

        [Test]
        public static void SavedLinesEndingInABraceKeepTheirText()
        {
            // Saved by 1.7.1: the script it made then is still what the lines make now, so the lines are shown as written.
            var m = new MacroDef { Plain = "type \"if (x) {\"\n\nenter", Script = "if (x) {{{ENTER}}" };
            Assert.Equal(m.Plain, PlainMacro.PlainFor(m));
        }

        [Test]
        public static void AWindowTitleEndingInSpacedDigitsKeepsItsTimeLimit()
        {
            string token = MacroScript.WaitWindow("Score: 100", 10000);
            Assert.Equal("{{WAITWINDOW:Score: 100:10000}}", token);
            MacroStep step = MacroParser.Parse(token, true).Steps.Single();
            Assert.Equal("Score: 100", step.Value);
            Assert.Equal(10000, step.Ms);
            MacroStep back = MacroParser.Parse(S(PlainMacro.ToPlain(token, true)), true).Steps.Single();
            Assert.Equal("Score: 100", back.Value);
            Assert.Equal(10000, back.Ms);
        }

        [Test]
        public static void ANoteWrittenAsCodeShowsWithoutItsBraces()
        {
            Assert.Equal("note", PlainMacro.TileLabel(PlainMacro.ToScript("{{// note}}").Lines[0]));
            Assert.Equal("note", PlainMacro.TileLabel(PlainMacro.ToScript("{{//}} note").Lines[0]));
            Assert.Equal("note", PlainMacro.TileLabel(PlainMacro.ToScript("# note").Lines[0]));
        }

        [Test]
        public static void EnterTakesTheHighlightedSuggestion()
        {
            // 1.13.2: "ctrl" works as a step (press Ctrl alone), but Enter should still take the highlighted ctrl+c.
            Assert.True(PlainMacro.EnterTakesSuggestion("ctrl"), "ctrl");
            Assert.True(PlainMacro.EnterTakesSuggestion("ctrl+shift"), "ctrl+shift");
            Assert.True(PlainMacro.EnterTakesSuggestion("wai"), "wai");
            // A line that is itself a suggested step keeps Enter as a new line.
            Assert.True(!PlainMacro.EnterTakesSuggestion("click"), "click");
            Assert.True(!PlainMacro.EnterTakesSuggestion(" Tab "), "tab");
            Assert.True(!PlainMacro.EnterTakesSuggestion("end"), "end");
        }
    }
}
