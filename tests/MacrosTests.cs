using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MacroKit.Core;
using MacroKit.Macros;

namespace MacroKit.Tests
{
    /// <summary>Parser tests: Super Macro text in, steps and errors out.</summary>
    public static class MacroParserTests
    {
        const string ExampleScript = "{{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{CTRL}{V}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{DOWN}}{{PAUSE:50}}";

        static ParseResult P(string script, bool newLineIsEnter = true) { return MacroParser.Parse(script, newLineIsEnter); }

        static string Errors(ParseResult r) { return string.Join("; ", r.Errors.Select(e => e.Message)); }

        static MacroStep One(string script, bool newLineIsEnter = true)
        {
            ParseResult r = P(script, newLineIsEnter);
            Assert.True(r.Ok, "should parse: " + script + " -> " + Errors(r));
            Assert.Equal(1, r.Steps.Count, "step count for " + script);
            return r.Steps[0];
        }

        static ParseError Error(string script)
        {
            ParseResult r = P(script);
            Assert.False(r.Ok, "should fail: " + script);
            return r.Errors[0];
        }

        static ushort Vk(string name)
        {
            ushort vk;
            Assert.True(KeyNames.TryGetVk(name, out vk), name);
            return vk;
        }

        [Test]
        public static void ExampleScriptIsTenSteps()
        {
            ParseResult r = P(ExampleScript);
            Assert.True(r.Ok, Errors(r));
            Assert.Equal(10, r.Steps.Count);
            StepKind[] kinds = { StepKind.Keys, StepKind.Pause, StepKind.Keys, StepKind.Pause, StepKind.Keys, StepKind.Pause, StepKind.Keys, StepKind.Pause, StepKind.Keys, StepKind.Pause };
            Assert.SequenceEqual(kinds, r.Steps.Select(s => s.Kind));
            Assert.SequenceEqual(new ushort[] { Vk("CTRL"), Vk("C") }, r.Steps[0].Keys);
            Assert.SequenceEqual(new ushort[] { Vk("ALT"), Vk("TAB") }, r.Steps[2].Keys);
            Assert.SequenceEqual(new ushort[] { Vk("CTRL"), Vk("V") }, r.Steps[4].Keys);
            Assert.SequenceEqual(new ushort[] { Vk("ALT"), Vk("TAB") }, r.Steps[6].Keys);
            Assert.SequenceEqual(new ushort[] { Vk("DOWN") }, r.Steps[8].Keys);
            Assert.SequenceEqual(new[] { 50, 100, 50, 100, 50 }, new[] { 1, 3, 5, 7, 9 }.Select(i => r.Steps[i].Ms));
        }

        [Test]
        public static void ExampleScriptDescriptions()
        {
            string[] lines = P(ExampleScript).Steps.Select(s => s.Describe()).ToArray();
            Assert.SequenceEqual(new[]
            {
                "Press Ctrl+C", "Wait 50 ms", "Press Alt+Tab", "Wait 100 ms", "Press Ctrl+V", "Wait 50 ms", "Press Alt+Tab", "Wait 100 ms", "Press Down", "Wait 50 ms"
            }, lines);
        }

        [Test]
        public static void EmptyAndNullScriptsHaveNoSteps()
        {
            Assert.Equal(0, P("").Steps.Count);
            Assert.True(P("").Ok);
            Assert.Equal(0, P(null).Steps.Count);
            Assert.Equal(1, P("   ").Steps.Count, "spaces are literal text");
        }

        [Test]
        public static void SingleKeyAndCombos()
        {
            Assert.SequenceEqual(new[] { Vk("ENTER") }, One("{{ENTER}}").Keys);
            Assert.SequenceEqual(new[] { Vk("CTRL"), Vk("SHIFT"), Vk("ESC") }, One("{{CTRL}{SHIFT}{ESC}}").Keys);
            Assert.SequenceEqual(new[] { Vk("CTRL"), Vk("SHIFT"), Vk("ESC") }, One("{{CTRL}{SHIFT}{ESCAPE}}").Keys);
        }

        [Test]
        public static void KeyNamesAreCaseInsensitive()
        {
            Assert.SequenceEqual(new[] { Vk("CTRL"), Vk("C") }, One("{{ctrl}{c}}").Keys);
            Assert.SequenceEqual(new[] { Vk("ENTER") }, One("{{Enter}}").Keys);
            Assert.SequenceEqual(new[] { Vk("PAGEDOWN") }, One("{{pgdn}}").Keys);
        }

        [Test]
        public static void WhitespaceInsideBracesIsIgnored()
        {
            Assert.SequenceEqual(new[] { Vk("ENTER") }, One("{{ ENTER }}").Keys);
            Assert.SequenceEqual(new[] { Vk("CTRL"), Vk("C") }, One("{{ CTRL } { C }}").Keys);
            Assert.SequenceEqual(new[] { Vk("CTRL"), Vk("C") }, One("{{\tCTRL}\t{C }}").Keys);
            Assert.Equal(50, One("{{ PAUSE : 50 }}").Ms);
            Assert.Equal(StepKind.Pause, One("{{pause:50}}").Kind);
        }

        [Test]
        public static void HexAndPrefixedKeyNames()
        {
            Assert.SequenceEqual(new ushort[] { 0x41 }, One("{{0x41}}").Keys);
            Assert.SequenceEqual(new ushort[] { 0x41 }, One("{{VK_A}}").Keys);
            Assert.SequenceEqual(new ushort[] { 0x0D }, One("{{VK_RETURN}}").Keys);
        }

        [Test]
        public static void NumpadNavigationKeepsTheNonExtendedFlag()
        {
            ushort vk = One("{{NUMPAD_HOME}}").Keys[0];
            Assert.Equal((ushort)(0x24 | KeyNames.ForceNonExtended), vk);
        }

        [Test]
        public static void BreakIsTheKeyAndPauseIsTheCommand()
        {
            Assert.SequenceEqual(new ushort[] { 0x13 }, One("{{BREAK}}").Keys);
            ParseError e = Error("{{PAUSE}}");
            Assert.Contains(e.Message, "milliseconds");
            Assert.Contains(Error("{{CTRL}{PAUSE}}").Message, "BREAK");
        }

        [Test]
        public static void MouseButtonCombosClick()
        {
            MacroStep click = One("{{LBUTTON}}");
            Assert.Equal(StepKind.MouseClick, click.Kind);
            Assert.Equal(MouseButton.Left, click.Button);
            Assert.Equal(MouseButton.Right, One("{{RBUTTON}}").Button);
            Assert.Equal(MouseButton.Middle, One("{{mbutton}}").Button);
            Assert.Equal(MouseButton.X1, One("{{XBUTTON1}}").Button);
            Assert.Equal(MouseButton.X2, One("{{XBUTTON2}}").Button);
            Assert.Equal("Click left mouse button", click.Describe());
        }

        [Test]
        public static void SeveralMouseButtonsClickInOrder()
        {
            ParseResult r = P("{{LBUTTON}{RBUTTON}}");
            Assert.True(r.Ok);
            Assert.SequenceEqual(new[] { MouseButton.Left, MouseButton.Right }, r.Steps.Select(s => s.Button));
        }

        [Test]
        public static void KeysAndMouseButtonsCannotMix()
        {
            Assert.Contains(Error("{{CTRL}{LBUTTON}}").Message, "mix");
        }

        [Test]
        public static void PlainTextIsOneTextStep()
        {
            MacroStep step = One("hello world");
            Assert.Equal(StepKind.Text, step.Kind);
            Assert.Equal("hello world", step.Text);
            Assert.Equal("Type “hello world”", step.Describe());
        }

        [Test]
        public static void LiteralSingleBracesStayText()
        {
            Assert.Equal("a{b}c", One("a{b}c").Text);
            Assert.Equal("{", One("{").Text);
            Assert.Equal("}}", One("}}").Text);
            Assert.Equal("x } y", One("x } y").Text);
            Assert.Equal("{ENTER}", One("{ENTER}").Text);
        }

        [Test]
        public static void ExtraBracesBeforeACommandAreText()
        {
            ParseResult r = P("{{{ENTER}}");
            Assert.True(r.Ok, Errors(r));
            Assert.SequenceEqual(new[] { StepKind.Text, StepKind.Keys }, r.Steps.Select(s => s.Kind));
            Assert.Equal("{", r.Steps[0].Text);
        }

        [Test]
        public static void TextAndCommandsAlternate()
        {
            ParseResult r = P("hello{{ENTER}}world");
            Assert.SequenceEqual(new[] { StepKind.Text, StepKind.Keys, StepKind.Text }, r.Steps.Select(s => s.Kind));
            Assert.Equal("hello", r.Steps[0].Text);
            Assert.Equal("world", r.Steps[2].Text);
        }

        [Test]
        public static void NewLinesBecomeEnterWhenOn()
        {
            Assert.Equal("a\nb", One("a\nb").Text);
            Assert.Equal("a\nb", One("a\r\nb").Text);
            Assert.Equal("a\n\nb", One("a\n\nb").Text);
            Assert.Equal("Press Enter", One("\n").Describe());
        }

        [Test]
        public static void NewLinesAreSkippedWhenOff()
        {
            Assert.Equal("ab", One("a\r\nb", false).Text);
            Assert.Equal("ab", One("a\nb", false).Text);
            Assert.Equal(0, P("\r\n\r\n", false).Steps.Count);
            ParseResult r = P("{{CTRL}{C}}\n{{PAUSE:50}}\n", false);
            Assert.SequenceEqual(new[] { StepKind.Keys, StepKind.Pause }, r.Steps.Select(s => s.Kind));
        }

        [Test]
        public static void CommentHidesTheRestOfTheLineAndItsBreak()
        {
            ParseResult r = P("a{{//}} note\nb");
            Assert.True(r.Ok, Errors(r));
            Assert.SequenceEqual(new[] { "a", "b" }, r.Steps.Select(s => s.Text));
        }

        [Test]
        public static void CommentHidesCommandsOnItsLine()
        {
            ParseResult r = P("{{ENTER}}{{//}}{{TAB}}\n{{ESC}}");
            Assert.SequenceEqual(new[] { Vk("ENTER"), Vk("ESC") }, r.Steps.Select(s => s.Keys[0]));
        }

        [Test]
        public static void CommentWorksWithWindowsLineBreaksAndWholeLines()
        {
            ParseResult r = P("{{//}} first line\r\n{{ENTER}}\r\n{{// second}}\r\n{{TAB}}", false);
            Assert.True(r.Ok, Errors(r));
            Assert.SequenceEqual(new[] { Vk("ENTER"), Vk("TAB") }, r.Steps.Select(s => s.Keys[0]));
        }

        [Test]
        public static void CommentAtTheEndOfTheScript()
        {
            Assert.Equal(0, P("{{//}} nothing else").Steps.Count);
            Assert.True(P("{{//}}").Ok);
        }

        [Test]
        public static void KeyPressWithCountAndDelay()
        {
            MacroStep step = One("{{KEYPRESS:DOWN:5:100}}");
            Assert.Equal(StepKind.KeyRepeat, step.Kind);
            Assert.SequenceEqual(new[] { Vk("DOWN") }, step.Keys);
            Assert.Equal(5, step.Count);
            Assert.Equal(100, step.Ms);
            Assert.Equal("Press Down 5 times, 100 ms apart", step.Describe());
            MacroStep noDelay = One("{{keypress:a:3}}");
            Assert.Equal(0, noDelay.Ms);
            Assert.Equal("Press A 3 times", noDelay.Describe());
            Assert.Equal("Press A", One("{{KEYPRESS:A:1}}").Describe());
        }

        [Test]
        public static void KeyPressErrors()
        {
            Assert.Contains(Error("{{KEYPRESS:DOWN}}").Message, "count");
            Assert.Contains(Error("{{KEYPRESS}}").Message, "count");
            Assert.Contains(Error("{{KEYPRESS:DOWN:0}}").Message, "count");
            Assert.Contains(Error("{{KEYPRESS:DOWN:x}}").Message, "count");
            Assert.Contains(Error("{{KEYPRESS:NOPE:3}}").Message, "NOPE");
            Assert.Contains(Error("{{KEYPRESS:DOWN:3:-5}}").Message, "delay");
            Assert.Contains(Error("{{KEYPRESS:DOWN:3:1:2}}").Message, "needs");
        }

        [Test]
        public static void KeyDownAndKeyUpAcceptModifiers()
        {
            MacroStep down = One("{{KEYDOWN:SHIFT}}");
            Assert.Equal(StepKind.KeyDown, down.Kind);
            Assert.SequenceEqual(new[] { Vk("SHIFT") }, down.Keys);
            Assert.Equal("Hold Shift", down.Describe());
            MacroStep up = One("{{keyup:ctrl}}");
            Assert.Equal(StepKind.KeyUp, up.Kind);
            Assert.Equal("Release Ctrl", up.Describe());
            Assert.Contains(Error("{{KEYDOWN}}").Message, "one key");
            Assert.Contains(Error("{{KEYUP:WAT}}").Message, "WAT");
        }

        [Test]
        public static void PauseRange()
        {
            Assert.Equal(0, One("{{PAUSE:0}}").Ms);
            Assert.Equal(3600000, One("{{PAUSE:3600000}}").Ms);
            Assert.Contains(Error("{{PAUSE:3600001}}").Message, "3,600,000");
            Assert.Contains(Error("{{PAUSE:-1}}").Message, "whole number");
            Assert.Contains(Error("{{PAUSE:abc}}").Message, "whole number");
            Assert.Contains(Error("{{PAUSE:1.5}}").Message, "whole number");
            Assert.Contains(Error("{{PAUSE:1:2}}").Message, "whole number");
        }

        [Test]
        public static void MouseMovesAndPositions()
        {
            MacroStep to = One("{{MOUSEXY:100,200}}");
            Assert.Equal(StepKind.MouseMoveTo, to.Kind);
            Assert.Equal(100, to.X);
            Assert.Equal(200, to.Y);
            Assert.Equal("Move mouse to 100, 200", to.Describe());
            MacroStep negative = One("{{MOUSEXY:-1920,-30}}");
            Assert.Equal(-1920, negative.X);
            Assert.Equal(-30, negative.Y);
            MacroStep by = One("{{MOUSEMOVE: 10 , -5 }}");
            Assert.Equal(StepKind.MouseMoveBy, by.Kind);
            Assert.Equal(10, by.X);
            Assert.Equal(-5, by.Y);
            Assert.Equal("Move mouse by 10, -5", by.Describe());
            MacroStep pos = One("{{MOUSEPOS:32768,65535}}");
            Assert.Equal(StepKind.MouseMoveNormalized, pos.Kind);
            Assert.Equal(32768, pos.X);
            Assert.Equal(65535, pos.Y);
            Assert.Equal(100, One("{{MOUSEXY:100:200}}").X);
        }

        [Test]
        public static void MouseMoveErrors()
        {
            Assert.Contains(Error("{{MOUSEXY:100}}").Message, "two numbers");
            Assert.Contains(Error("{{MOUSEXY}}").Message, "two numbers");
            Assert.Contains(Error("{{MOUSEXY:a,b}}").Message, "two numbers");
            Assert.Contains(Error("{{MOUSEXY:1,2,3}}").Message, "two numbers");
            Assert.Contains(Error("{{MOUSEPOS:65536,0}}").Message, "65535");
            Assert.Contains(Error("{{MOUSEPOS:-1,0}}").Message, "65535");
        }

        [Test]
        public static void EveryMouseButtonCommand()
        {
            var cases = new[]
            {
                new { Text = "MLEFTDOWN", Kind = StepKind.MouseDown, Button = MouseButton.Left },
                new { Text = "MLEFTUP", Kind = StepKind.MouseUp, Button = MouseButton.Left },
                new { Text = "MLEFTDBLCLICK", Kind = StepKind.MouseDoubleClick, Button = MouseButton.Left },
                new { Text = "MRIGHTDOWN", Kind = StepKind.MouseDown, Button = MouseButton.Right },
                new { Text = "MRIGHTUP", Kind = StepKind.MouseUp, Button = MouseButton.Right },
                new { Text = "MRIGHTDBLCLICK", Kind = StepKind.MouseDoubleClick, Button = MouseButton.Right },
                new { Text = "MMIDDLEDOWN", Kind = StepKind.MouseDown, Button = MouseButton.Middle },
                new { Text = "MMIDDLEUP", Kind = StepKind.MouseUp, Button = MouseButton.Middle },
            };
            foreach (var c in cases)
            {
                MacroStep step = One("{{" + c.Text + "}}");
                Assert.Equal(c.Kind, step.Kind, c.Text);
                Assert.Equal(c.Button, step.Button, c.Text);
            }
            Assert.Equal("Hold right mouse button", One("{{MRIGHTDOWN}}").Describe());
            Assert.Equal("Double-click left mouse button", One("{{MLEFTDBLCLICK}}").Describe());
            Assert.Contains(Error("{{MLEFTDOWN:1}}").Message, "doesn't take a value");
        }

        [Test]
        public static void ScrollCommands()
        {
            MacroStep up = One("{{MSCROLLUP}}");
            Assert.Equal(StepKind.MouseWheel, up.Kind);
            Assert.Equal(1, up.Count);
            Assert.Equal(1, up.Direction);
            Assert.False(up.Horizontal);
            Assert.Equal("Scroll up 1 notch", up.Describe());
            MacroStep down = One("{{MSCROLLDOWN:3}}");
            Assert.Equal(3, down.Count);
            Assert.Equal(-1, down.Direction);
            Assert.Equal("Scroll down 3 notches", down.Describe());
            MacroStep left = One("{{MSCROLLLEFT:2}}");
            Assert.True(left.Horizontal);
            Assert.Equal(-1, left.Direction);
            MacroStep right = One("{{MSCROLLRIGHT}}");
            Assert.True(right.Horizontal);
            Assert.Equal(1, right.Direction);
            Assert.Contains(Error("{{MSCROLLUP:0}}").Message, "notches");
            Assert.Contains(Error("{{MSCROLLUP:1001}}").Message, "notches");
            Assert.Contains(Error("{{MSCROLLUP:x}}").Message, "notches");
        }

        [Test]
        public static void SaveAndLoadMousePosition()
        {
            Assert.Equal(StepKind.SaveMousePos, One("{{MSAVEPOS}}").Kind);
            Assert.Equal(StepKind.LoadMousePos, One("{{MLOADPOS}}").Kind);
            Assert.False(One("{{MSAVEPOS}}").SendsInput);
            Assert.True(One("{{MLOADPOS}}").SendsInput);
        }

        [Test]
        public static void VariableCommands()
        {
            MacroStep set = One("{{VARSET:row:12}}");
            Assert.Equal(StepKind.SetVar, set.Kind);
            Assert.Equal("row", set.Var);
            Assert.Equal("12", set.Value);
            Assert.Equal("a:b:c", One("{{VARSET:x:a:b:c}}").Value);
            Assert.Equal("", One("{{VARSET:x}}").Value);
            Assert.Equal(" hello world ", One("{{VARSET:x: hello world }}").Value);   // a value keeps its exact text; only the name is trimmed
            MacroStep output = One("{{OUTPUT:row}}");
            Assert.Equal(StepKind.OutputVar, output.Kind);
            Assert.Equal("row", output.Var);
            Assert.Equal("row", One("{{$row}}").Var);
            Assert.Equal(StepKind.OutputVar, One("{{$row}}").Kind);
            Assert.Equal("row", One("{{VARSET:$row:1}}").Var);
            Assert.Equal(StepKind.UnsetVar, One("{{VARUNSET:row}}").Kind);
            Assert.Equal(StepKind.UnsetAllVars, One("{{VARUNSETALL}}").Kind);
            MacroStep clip = One("{{SETCLIPBOARD:hello:there}}");
            Assert.Equal(StepKind.SetClipboard, clip.Kind);
            Assert.Equal("hello:there", clip.Value);
            Assert.Equal(StepKind.VarFromClipboard, One("{{VARSETFROMCLIPBOARD:copy}}").Kind);
            Assert.Equal("copy", One("{{VARSETFROMCLIPBOARD:copy}}").Var);
        }

        [Test]
        public static void VariableCommandErrors()
        {
            Assert.Contains(Error("{{VARSET}}").Message, "name and a value");
            Assert.Contains(Error("{{VARSET:bad name:1}}").Message, "letters, numbers");
            Assert.Contains(Error("{{OUTPUT}}").Message, "variable name");
            Assert.Contains(Error("{{OUTPUT:a:b}}").Message, "variable name");
            Assert.Contains(Error("{{VARUNSET:a-b}}").Message, "letters, numbers");
            Assert.Contains(Error("{{SETCLIPBOARD}}").Message, "clipboard");
            Assert.Contains(Error("{{VARUNSETALL:x}}").Message, "doesn't take a value");
        }

        [Test]
        public static void NumbersMayUseVariables()
        {
            MacroStep step = One("{{PAUSE:$delay}}");
            Assert.True(step.IsDynamic);
            Assert.Equal(StepKind.Pause, step.Kind);
            Assert.Equal("PAUSE", step.Command);
            Assert.Equal("Run {{PAUSE:$delay}}", step.Describe());
            Assert.True(One("{{MOUSEXY:$MOUSE_X,$MOUSE_Y}}").IsDynamic);
            Assert.False(One("{{PAUSE:5}}").IsDynamic);
        }

        [Test]
        public static void KeyNamesMayUseVariables()
        {
            MacroStep press = One("{{KEYPRESS:$k:3}}");
            Assert.True(press.IsDynamic);
            Assert.Equal(StepKind.KeyRepeat, press.Kind);
            Assert.Equal("KEYPRESS", press.Command);
            Assert.Equal("Run {{KEYPRESS:$k:3}}", press.Describe());
            Assert.True(One("{{KEYPRESS:$k:$n:100}}").IsDynamic);
            MacroStep down = One("{{KEYDOWN:$k}}");
            Assert.True(down.IsDynamic);
            Assert.Equal(StepKind.KeyDown, down.Kind);
            MacroStep up = One("{{KEYUP:$k}}");
            Assert.True(up.IsDynamic);
            Assert.Equal(StepKind.KeyUp, up.Kind);
            Assert.False(One("{{KEYPRESS:DOWN:3}}").IsDynamic, "a plain key name is still checked and built at parse time");
        }

        [Test]
        public static void PlainKeyNamesAreStillCheckedWhileParsing()
        {
            Assert.Contains(Error("{{KEYPRESS:NOPE:3}}").Message, "Unknown key “NOPE”");
            Assert.Contains(Error("{{KEYDOWN:NOPE}}").Message, "Unknown key “NOPE”");
            Assert.Contains(Error("{{KEYUP:NOPE}}").Message, "Unknown key “NOPE”");
            Assert.Contains(Error("{{KEYPRESS:$k}}").Message, "needs a key and a count");
        }

        [Test]
        public static void VarsetAndSetclipboardKeepTheExactValueText()
        {
            Assert.Equal("  two  spaces  ", One("{{VARSET:x:  two  spaces  }}").Value);
            Assert.Equal(" a : b ", One("{{VARSET:x: a : b }}").Value, "spaces around inner colons stay");
            Assert.Equal("x", One("{{VARSET: x :1}}").Var, "the name is trimmed");
            Assert.Equal("1", One("{{VARSET: x :1}}").Value);
            Assert.Equal("  hi there ", One("{{SETCLIPBOARD:  hi there }}").Value);
            Assert.Equal(" a: b :c ", One("{{SETCLIPBOARD: a: b :c }}").Value);
            Assert.Equal("", One("{{VARSET:x:}}").Value);
            Assert.Equal(" ", One("{{VARSET:x: }}").Value);
            Assert.Equal("5", One("{{ varset : n :5}}").Value, "the command name is trimmed and case-insensitive");
        }

        [Test]
        public static void NumbersAndKeysAreStillTrimmed()
        {
            Assert.Equal(50, One("{{PAUSE: 50 }}").Ms);
            Assert.Equal(3, One("{{KEYPRESS: DOWN : 3 }}").Count);
            Assert.SequenceEqual(new ushort[] { Vk("DOWN") }, One("{{KEYPRESS: DOWN : 3 }}").Keys);
        }

        [Test]
        public static void UnsupportedCommandsSayTheyAreNotSupported()
        {
            Assert.Equal("{{INPUT}} is a Super Macro command Macro Kit doesn't support.", Error("{{INPUT}}").Message);
            foreach (string name in new[] { "OUTPUTTOFILE", "APPENDTOFILE", "VARSETFROMFILE", "VARSETFROMENV", "SETICONFROMFILE", "SETKEYTITLE", "RUN", "PLAYSOUND" })
            {
                ParseError e = Error("{{" + name + ":whatever}}");
                Assert.Equal("{{" + name + "}} is a Super Macro command Macro Kit doesn't support.", e.Message);
            }
            Assert.Contains(Error("{{input:Enter a value}}").Message, "{{INPUT}}");
        }

        [Test]
        public static void UnknownNamesGiveAnError()
        {
            Assert.Equal("Unknown key or command “FOO”.", Error("{{FOO}}").Message);
            Assert.Equal("Unknown key or command “foo”.", Error("{{foo:1}}").Message);
            Assert.Equal("Unknown key or command “NOPE”.", Error("{{CTRL}{NOPE}}").Message);
            Assert.Contains(Error("{{A:1}}").Message, "KEYPRESS");
        }

        [Test]
        public static void ErrorsCarryLineAndColumn()
        {
            ParseError first = Error("{{FOO}}");
            Assert.Equal(1, first.Line);
            Assert.Equal(1, first.Column);
            Assert.Equal(0, first.Index);
            Assert.Equal(7, first.Length);

            ParseError third = Error("a\r\nb\n{{FOO}}");
            Assert.Equal(3, third.Line);
            Assert.Equal(1, third.Column);

            ParseError indented = Error("{{ENTER}}\n  hi {{NOPE}}");
            Assert.Equal(2, indented.Line);
            Assert.Equal(6, indented.Column);
            Assert.Equal(indented.Index, "{{ENTER}}\n  hi ".Length);
        }

        [Test]
        public static void ErrorsDoNotStopTheParse()
        {
            ParseResult r = P("{{ENTER}}{{FOO}}{{TAB}}{{PAUSE:x}}");
            Assert.Equal(2, r.Errors.Count);
            Assert.Equal(2, r.Steps.Count);
            Assert.False(r.Ok);
        }

        [Test]
        public static void UnclosedCommandStopsAtTheEndOfItsLine()
        {
            ParseResult r = P("x{{ENTER\n{{TAB}}");
            Assert.Equal(1, r.Errors.Count);
            Assert.Contains(r.Errors[0].Message, "closing");
            Assert.Equal(1, r.Errors[0].Line);
            Assert.Equal(2, r.Errors[0].Column);
            Assert.Equal(Vk("TAB"), r.Steps.Last().Keys[0]);
        }

        [Test]
        public static void EmptyCommandIsAnError()
        {
            Assert.Contains(Error("{{}}").Message, "empty");
            Assert.Contains(Error("{{  }}").Message, "empty");
        }

        [Test]
        public static void StepsRememberTheirSourceSpan()
        {
            ParseResult r = P("ab\n{{ENTER}} {{PAUSE:5}}");
            MacroStep text = r.Steps[0];
            Assert.Equal(0, text.Index);
            Assert.Equal(3, text.Length);
            Assert.Equal(1, text.Line);
            Assert.Equal(1, text.Column);
            MacroStep enter = r.Steps[1];
            Assert.Equal(3, enter.Index);
            Assert.Equal(9, enter.Length);
            Assert.Equal(2, enter.Line);
            Assert.Equal(1, enter.Column);
            MacroStep pause = r.Steps[3];
            Assert.Equal(2, pause.Line);
            Assert.Equal(11, pause.Column);
        }

        [Test]
        public static void EstimatedTimePerPass()
        {
            var macro = new MacroDef { KeyDelayMs = 10 };
            Assert.Equal(400L, P(ExampleScript).EstimatedMs(macro));
            macro.KeyDelayMs = 0;
            Assert.Equal(350L, P(ExampleScript).EstimatedMs(macro));
            macro.KeyDelayMs = 10;
            Assert.Equal(410L, P("{{KEYPRESS:DOWN:5:100}}").EstimatedMs(macro));
            Assert.Equal(50L, P("{{KEYPRESS:DOWN:5}}").EstimatedMs(macro));
            Assert.Equal(20L, P("hi{{VARSET:a:1}}{{ENTER}}").EstimatedMs(macro));
            Assert.Equal(0L, P("{{VARSET:a:1}}").EstimatedMs(macro));
        }

        [Test]
        public static void DescribeCoversTheOtherSteps()
        {
            Assert.Equal("Remember the mouse position", One("{{MSAVEPOS}}").Describe());
            Assert.Equal("Set $a to “1”", One("{{VARSET:a:1}}").Describe());
            Assert.Equal("Type the value of $a", One("{{OUTPUT:a}}").Describe());
            Assert.Equal("Forget $a", One("{{VARUNSET:a}}").Describe());
            Assert.Equal("Forget all variables", One("{{VARUNSETALL}}").Describe());
            Assert.Equal("Copy “hi” to the clipboard", One("{{SETCLIPBOARD:hi}}").Describe());
            Assert.Equal("Store the clipboard text in $c", One("{{VARSETFROMCLIPBOARD:c}}").Describe());
            Assert.Equal("Type “a↵b”", One("a\nb").Describe());
            Assert.Equal("Type “" + new string('x', 40) + "…”", One(new string('x', 60)).Describe());
        }
    }

    /// <summary>Palette token tests: what the UI inserts must parse back to the same step.</summary>
    public static class MacroScriptTests
    {
        static MacroStep RoundTrip(string token)
        {
            ParseResult r = MacroParser.Parse(token, true);
            Assert.True(r.Ok, token + " -> " + string.Join("; ", r.Errors.Select(e => e.Message)));
            Assert.Equal(1, r.Steps.Count, token);
            return r.Steps[0];
        }

        [Test]
        public static void ShortcutTokens()
        {
            Assert.Equal("{{CTRL}{C}}", MacroScript.Shortcut(0xA2, 0x43));
            Assert.Equal("{{DOWN}}", MacroScript.Shortcut(0x28));
            Assert.Equal("{{ALT}{TAB}}", MacroScript.Shortcut(new List<ushort> { 0xA4, 0x09 }));
            Assert.SequenceEqual(new ushort[] { 0xA2, 0x43 }, RoundTrip(MacroScript.Shortcut(0xA2, 0x43)).Keys);
            Assert.SequenceEqual(new ushort[] { 0xA3, 0x58 }, RoundTrip(MacroScript.Shortcut(0xA3, 0x58)).Keys);
            Assert.SequenceEqual(new ushort[] { 0xA2, 0xA0, 0x1B }, RoundTrip(MacroScript.Shortcut(0xA2, 0xA0, 0x1B)).Keys);
            Assert.Throws<ArgumentException>(() => MacroScript.Shortcut(new ushort[0]));
        }

        [Test]
        public static void ShortcutTokensKeepSpecialKeys()
        {
            ushort numpadHome = (ushort)(0x24 | KeyNames.ForceNonExtended);
            Assert.SequenceEqual(new[] { numpadHome }, RoundTrip(MacroScript.Shortcut(numpadHome)).Keys);
            Assert.SequenceEqual(new ushort[] { 0xFA }, RoundTrip(MacroScript.Shortcut(0xFA)).Keys);
            Assert.SequenceEqual(new ushort[] { 0x13 }, RoundTrip(MacroScript.Shortcut(0x13)).Keys);
            for (ushort f = 0x70; f <= 0x87; f++) Assert.SequenceEqual(new[] { f }, RoundTrip(MacroScript.Shortcut(f)).Keys);
        }

        [Test]
        public static void EveryNamedKeyRoundTrips()
        {
            for (int vk = 1; vk < 0x100; vk++)
            {
                string token = MacroScript.Shortcut((ushort)vk);
                ParseResult r = MacroParser.Parse(token, true);
                Assert.True(r.Ok, "vk " + vk + " -> " + token);
                Assert.Equal(1, r.Steps.Count);
            }
        }

        [Test]
        public static void PauseAndKeyPressTokens()
        {
            Assert.Equal("{{PAUSE:100}}", MacroScript.Pause(100));
            Assert.Equal(100, RoundTrip(MacroScript.Pause(100)).Ms);
            Assert.Equal("{{KEYPRESS:DOWN:5:100}}", MacroScript.KeyPress(0x28, 5, 100));
            Assert.Equal("{{KEYPRESS:DOWN:5}}", MacroScript.KeyPress(0x28, 5));
            MacroStep step = RoundTrip(MacroScript.KeyPress(0x28, 5, 100));
            Assert.Equal(5, step.Count);
            Assert.Equal(100, step.Ms);
            Assert.Equal(StepKind.KeyDown, RoundTrip(MacroScript.KeyDown(0xA0)).Kind);
            Assert.Equal(StepKind.KeyUp, RoundTrip(MacroScript.KeyUp(0xA0)).Kind);
            Assert.Equal("{{KEYDOWN:SHIFT}}", MacroScript.KeyDown(0xA0));
        }

        [Test]
        public static void TextTokens()
        {
            Assert.Equal("hello", MacroScript.Text("hello"));
            Assert.Equal("a{b}", MacroScript.Text("a{b}"));
            Assert.Equal("hello", RoundTrip(MacroScript.Text("hello")).Text);
            Assert.Equal("", MacroScript.Text(null));
        }

        [Test]
        public static void TextWithDoubleBracesParsesBackToTheSameCharacters()
        {
            string token = MacroScript.Text("a{{b}} {{{c");
            ParseResult r = MacroParser.Parse(token, true);
            Assert.True(r.Ok, string.Join("; ", r.Errors.Select(e => e.Message)));
            // Run the steps the way the runner would: text is typed, OUTPUT:SMLCB types a brace.
            string typed = string.Concat(r.Steps.Select(s => s.Kind == StepKind.Text ? s.Text : "{"));
            Assert.Equal("a{{b}} {{{c", typed);
        }

        [Test]
        public static void CommentToken()
        {
            Assert.Equal("{{//}} fill the form", MacroScript.Comment("fill the form"));
            Assert.Equal("{{//}}", MacroScript.Comment(""));
            Assert.Equal("{{//}} two lines", MacroScript.Comment("two\r\nlines".Replace("\r\n", "\n")));
            Assert.Equal(0, MacroParser.Parse(MacroScript.Comment("note"), true).Steps.Count);
            Assert.True(MacroParser.Parse(MacroScript.Comment("note"), true).Ok);
        }

        [Test]
        public static void MouseTokens()
        {
            Assert.Equal("{{MOUSEXY:100,200}}", MacroScript.MouseTo(100, 200));
            MacroStep to = RoundTrip(MacroScript.MouseTo(-5, 7));
            Assert.Equal(-5, to.X);
            Assert.Equal(7, to.Y);
            Assert.Equal(StepKind.MouseMoveBy, RoundTrip(MacroScript.MouseBy(3, -4)).Kind);
            Assert.Equal(StepKind.MouseMoveNormalized, RoundTrip(MacroScript.MouseNormalized(10, 20)).Kind);
        }

        [Test]
        public static void ClickAndButtonTokens()
        {
            foreach (MouseButton b in Enum.GetValues(typeof(MouseButton)).Cast<MouseButton>())
            {
                MacroStep click = RoundTrip(MacroScript.Click(b));
                Assert.Equal(StepKind.MouseClick, click.Kind);
                Assert.Equal(b, click.Button);
            }
            foreach (MouseButton b in new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle })
            {
                Assert.Equal(StepKind.MouseDown, RoundTrip(MacroScript.MouseDown(b)).Kind);
                Assert.Equal(b, RoundTrip(MacroScript.MouseUp(b)).Button);
            }
            Assert.Equal(StepKind.MouseDoubleClick, RoundTrip(MacroScript.DoubleClick(MouseButton.Right)).Kind);
            Assert.Throws<ArgumentException>(() => MacroScript.MouseDown(MouseButton.X1));
            Assert.Throws<ArgumentException>(() => MacroScript.DoubleClick(MouseButton.Middle));
        }

        [Test]
        public static void ScrollTokens()
        {
            Assert.Equal("{{MSCROLLUP}}", MacroScript.ScrollUp());
            Assert.Equal("{{MSCROLLDOWN:3}}", MacroScript.ScrollDown(3));
            MacroStep left = RoundTrip(MacroScript.ScrollLeft(2));
            Assert.True(left.Horizontal);
            Assert.Equal(-1, left.Direction);
            Assert.Equal(2, left.Count);
            Assert.Equal(1, RoundTrip(MacroScript.ScrollRight()).Direction);
        }

        [Test]
        public static void TokensJoinIntoAWorkingScript()
        {
            string script = MacroScript.Shortcut(0xA2, 0x43) + MacroScript.Pause(50) + MacroScript.Shortcut(0xA4, 0x09) + MacroScript.Pause(100) + MacroScript.Shortcut(0xA2, 0x56);
            ParseResult r = MacroParser.Parse(script, true);
            Assert.True(r.Ok);
            Assert.Equal(5, r.Steps.Count);
        }
    }

    /// <summary>Runner tests, all against fakes: no real input, windows or time.</summary>
    public static class MacroRunnerTests
    {
        sealed class Rig
        {
            public readonly FakeInputSink Input = new FakeInputSink();
            public readonly FakeWindowSystem Windows = new FakeWindowSystem();
            public readonly FakeClock Clock = new FakeClock();
            public readonly FakeClipboard Clipboard = new FakeClipboard();
            public readonly MacroRunner Runner;
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
            public readonly List<RunProgress> Progress = new List<RunProgress>();
            public readonly WindowInfo Own, Excel, Chrome, Notepad;

            public Rig()
            {
                Own = Windows.AddOwnWindow(900);
                Excel = Windows.AddWindow(1, "Book1 - Excel", "EXCEL.EXE");
                Chrome = Windows.AddWindow(2, "Orders", "Chrome.exe");
                Notepad = Windows.AddWindow(3, "Untitled - Notepad", "notepad.exe");
                Windows.SetForeground(Excel.Handle);
                Runner = new MacroRunner(Input, Windows, Clock, Clipboard);
                Runner.ElevationProbe = () => false;
                Runner.Progress += p => Progress.Add(p);
            }

            public string Log { get { return string.Join("|", Input.Log); } }

            public RunResult Run(string script, Action<MacroDef> setup = null, RunOrigin origin = RunOrigin.MainWindow)
            {
                var macro = new MacroDef { Script = script, KeyDelayMs = 0 };
                if (setup != null) setup(macro);
                ParseResult parsed = MacroParser.Parse(script, macro.NewLineIsEnter);
                return Runner.Run(macro, parsed, origin, Cts.Token);
            }

            public static WindowMatch Match(string process, string title = null) { return new WindowMatch { ProcessName = process, TitleContains = title }; }
        }

        const string ExampleScript = "{{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{CTRL}{V}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{DOWN}}{{PAUSE:50}}";

        // ---- Refusing to run ----

        [Test]
        public static void RefusesScriptsWithErrors()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ENTER}}\n{{FOO}}");
            Assert.False(result.Completed);
            Assert.Contains(result.Error, "line 2");
            Assert.Contains(result.Error, "FOO");
            Assert.Equal(0, rig.Input.Log.Count);
            Assert.Equal(0, rig.Windows.ActivateLog.Count);
        }

        [Test]
        public static void RefusesEmptyScripts()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{//}} only a comment");
            Assert.False(result.Completed);
            Assert.Contains(result.Error, "nothing to run");
        }

        [Test]
        public static void NullArgumentsGiveAnErrorNotAnException()
        {
            var rig = new Rig();
            RunResult result = rig.Runner.Run(null, null, RunOrigin.MainWindow, CancellationToken.None);
            Assert.NotNull(result.Error);
            Assert.False(result.Completed);
        }

        // ---- Target window ----

        [Test]
        public static void SpecificWindowNotFound()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ENTER}}", m => { m.Target = TargetKind.SpecificWindow; m.TargetWindow = Rig.Match("Nope.exe", "Orders"); });
            Assert.False(result.Completed);
            Assert.Equal("Can't find the window to run in (Nope.exe, title contains “Orders”). Open it and try again.", result.Error);
            Assert.Equal(0, rig.Input.Log.Count);
            Assert.Equal(0, rig.Windows.ActivateLog.Count);
        }

        [Test]
        public static void SpecificWindowWithNothingChosen()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ENTER}}", m => { m.Target = TargetKind.SpecificWindow; });
            Assert.Contains(result.Error, "Choose the window");
        }

        [Test]
        public static void SpecificWindowIsActivatedWhenNotInFront()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ENTER}}", m => { m.Target = TargetKind.SpecificWindow; m.TargetWindow = Rig.Match("chrome"); });
            Assert.True(result.Completed, result.Error);
            Assert.SequenceEqual(new[] { "activate Orders (ok)" }, rig.Windows.ActivateLog);
            Assert.Equal("combo ENTER", rig.Log);
        }

        [Test]
        public static void SpecificWindowAlreadyInFrontIsLeftAlone()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ENTER}}", m => { m.Target = TargetKind.SpecificWindow; m.TargetWindow = Rig.Match("EXCEL.EXE"); });
            Assert.True(result.Completed, result.Error);
            Assert.Equal(0, rig.Windows.ActivateLog.Count);
        }

        [Test]
        public static void MainWindowOriginUsesTheLastExternalWindow()
        {
            var rig = new Rig();
            rig.Windows.Foreground = rig.Own.Handle;     // the user is looking at Macro Kit
            RunResult result = rig.Run("{{ENTER}}", null, RunOrigin.MainWindow);
            Assert.True(result.Completed, result.Error);
            Assert.SequenceEqual(new[] { "activate Book1 - Excel (ok)" }, rig.Windows.ActivateLog);
        }

        [Test]
        public static void MainWindowOriginIgnoresWhateverIsInFront()
        {
            var rig = new Rig();
            rig.Windows.Foreground = rig.Chrome.Handle;   // not the last external window as far as the tracker knows
            rig.Windows.LastExternalForeground = rig.Excel.Handle;
            RunResult result = rig.Run("{{ENTER}}", null, RunOrigin.Tray);
            Assert.True(result.Completed, result.Error);
            Assert.SequenceEqual(new[] { rig.Excel.Handle }, rig.Windows.ActivatedHandles);
        }

        [Test]
        public static void DeckAndHotkeyOriginsUseTheForegroundWindow()
        {
            foreach (RunOrigin origin in new[] { RunOrigin.Deck, RunOrigin.Hotkey })
            {
                var rig = new Rig();
                rig.Windows.SetForeground(rig.Chrome.Handle);
                rig.Windows.LastExternalForeground = rig.Excel.Handle;
                RunResult result = rig.Run("{{ENTER}}", null, origin);
                Assert.True(result.Completed, result.Error);
                Assert.Equal(0, rig.Windows.ActivateLog.Count, origin.ToString());
                Assert.Equal("combo ENTER", rig.Log);
            }
        }

        [Test]
        public static void DeckOriginFallsBackWhenMacroKitIsInFront()
        {
            var rig = new Rig();
            rig.Windows.Foreground = rig.Own.Handle;
            rig.Windows.LastExternalForeground = rig.Chrome.Handle;
            RunResult result = rig.Run("{{ENTER}}", null, RunOrigin.Deck);
            Assert.True(result.Completed, result.Error);
            Assert.SequenceEqual(new[] { "activate Orders (ok)" }, rig.Windows.ActivateLog);
        }

        [Test]
        public static void NoUsableWindowAsksTheUserToClickOne()
        {
            var rig = new Rig();
            rig.Windows.Foreground = rig.Own.Handle;
            rig.Windows.LastExternalForeground = IntPtr.Zero;
            RunResult result = rig.Run("{{ENTER}}");
            Assert.Equal("Click the window you want the macro to run in first.", result.Error);
            Assert.Equal(0, rig.Input.Log.Count);
        }

        [Test]
        public static void ActivationFailureIsReported()
        {
            var rig = new Rig();
            rig.Windows.Foreground = rig.Own.Handle;
            rig.Windows.ActivateFails.Add(rig.Excel.Handle);
            RunResult result = rig.Run("{{ENTER}}");
            Assert.Equal("Windows didn't let Macro Kit switch to Book1 - Excel.", result.Error);
            Assert.Equal(0, rig.Input.Log.Count);
        }

        [Test]
        public static void AdministratorTargetGetsAWarning()
        {
            var rig = new Rig();
            rig.Windows.AddWindow(4, "Admin tool", "admin.exe", true);
            rig.Run("{{ENTER}}", m => { m.Target = TargetKind.SpecificWindow; m.TargetWindow = Rig.Match("admin.exe"); });
            Assert.True(rig.Progress.Any(p => p.Message.Contains("administrator")), "warning expected");

            var other = new Rig();
            other.Windows.AddWindow(4, "Admin tool", "admin.exe", true);
            other.Runner.ElevationProbe = () => true;
            RunResult result = other.Run("{{ENTER}}", m => { m.Target = TargetKind.SpecificWindow; m.TargetWindow = Rig.Match("admin.exe"); });
            Assert.True(result.Completed, result.Error);
            Assert.False(other.Progress.Any(p => p.Message.Contains("administrator")), "no warning when Macro Kit is elevated too");
        }

        // ---- Alt+Tab partner ----

        [Test]
        public static void PartnerNotFound()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ENTER}}", m => m.AltTabPartner = Rig.Match("Missing.exe"));
            Assert.Equal("Can't find the Alt+Tab window (Missing.exe). Open it and try again.", result.Error);
            Assert.Equal(0, rig.Input.Log.Count);
        }

        [Test]
        public static void PartnerMustDifferFromTarget()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ENTER}}", m => m.AltTabPartner = Rig.Match("excel"));
            Assert.Contains(result.Error, "different window");
        }

        [Test]
        public static void PartnerIsPrimedBeforeTheTarget()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ENTER}}", m => m.AltTabPartner = Rig.Match("Chrome.exe"));
            Assert.True(result.Completed, result.Error);
            Assert.SequenceEqual(new[] { "activate Orders (ok)", "activate Book1 - Excel (ok)" }, rig.Windows.ActivateLog);
            Assert.Equal(rig.Excel.Handle, rig.Windows.Foreground);
        }

        [Test]
        public static void PartnerFailureNamesThePartner()
        {
            var rig = new Rig();
            rig.Windows.ActivateFails.Add(rig.Chrome.Handle);
            RunResult result = rig.Run("{{ENTER}}", m => m.AltTabPartner = Rig.Match("Chrome.exe"));
            Assert.Equal("Windows didn't let Macro Kit switch to Orders.", result.Error);
        }

        [Test]
        public static void SmartAltTabTogglesBetweenTargetAndPartner()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{CTRL}{C}}{{ALT}{TAB}}{{CTRL}{V}}{{ALT}{TAB}}{{DOWN}}", m => m.AltTabPartner = Rig.Match("Chrome.exe"));
            Assert.True(result.Completed, result.Error);
            Assert.Equal("combo LCONTROL+C|combo LCONTROL+V|combo DOWN", rig.Log);
            Assert.SequenceEqual(new[]
            {
                "activate Orders (ok)", "activate Book1 - Excel (ok)",   // priming
                "activate Orders (ok)",                                   // first Alt+Tab: target -> partner
                "activate Book1 - Excel (ok)",                            // second Alt+Tab: partner -> target
            }, rig.Windows.ActivateLog);
            Assert.Equal(rig.Excel.Handle, rig.Windows.Foreground);
        }

        [Test]
        public static void SmartAltTabRecognizesEveryAltSpelling()
        {
            foreach (string alt in new[] { "ALT", "LALT", "RALT", "MENU", "alt" })
            {
                var rig = new Rig();
                RunResult result = rig.Run("{{" + alt + "}{TAB}}", m => m.AltTabPartner = Rig.Match("Chrome.exe"));
                Assert.True(result.Completed, result.Error);
                Assert.Equal(0, rig.Input.Log.Count, alt);
                Assert.Equal(rig.Chrome.Handle, rig.Windows.Foreground, alt);
            }
        }

        [Test]
        public static void AltTabWithOtherKeysIsSentForReal()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ALT}{SHIFT}{TAB}}", m => m.AltTabPartner = Rig.Match("Chrome.exe"));
            Assert.True(result.Completed, result.Error);
            Assert.Equal("combo LALT+LSHIFT+TAB", rig.Log);
        }

        [Test]
        public static void AltTabWithoutPartnerSendsTheRealKeys()
        {
            var rig = new Rig();
            rig.Input.OnCall = line => { if (line == "combo LALT+TAB") rig.Windows.SimulateAltTab(); };
            RunResult result = rig.Run("{{ALT}{TAB}}");
            Assert.True(result.Completed, result.Error);
            Assert.Equal("combo LALT+TAB", rig.Log);
            Assert.Equal(rig.Chrome.Handle, rig.Windows.Foreground);
        }

        [Test]
        public static void SmartAltTabFallsBackToRealKeysWhenActivateFails()
        {
            var rig = new Rig();
            rig.Clock.After(5, () => rig.Windows.ActivateFails.Add(rig.Chrome.Handle));   // Windows starts refusing after priming
            rig.Input.OnCall = line => { if (line == "combo LALT+TAB") rig.Windows.SimulateAltTab(); };
            RunResult result = rig.Run("{{PAUSE:10}}{{ALT}{TAB}}{{ENTER}}", m => m.AltTabPartner = Rig.Match("Chrome.exe"));
            Assert.True(result.Completed, result.Error);
            Assert.Equal("combo LALT+TAB|combo ENTER", rig.Log);
            Assert.True(rig.Windows.ActivateLog.Contains("activate Orders (failed)"));
        }

        [Test]
        public static void FailedFallbackStopsBeforeTypingInTheWrongWindow()
        {
            var rig = new Rig();
            rig.Clock.After(5, () => rig.Windows.ActivateFails.Add(rig.Chrome.Handle));
            RunResult result = rig.Run("{{PAUSE:10}}{{ALT}{TAB}}{{ENTER}}", m => m.AltTabPartner = Rig.Match("Chrome.exe"));
            Assert.False(result.Completed);
            Assert.Contains(result.Error, "Alt+Tab didn't switch to Orders");
            Assert.Equal("combo LALT+TAB", rig.Log);
        }

        [Test]
        public static void SmartAltTabReportsTheSwitch()
        {
            var rig = new Rig();
            rig.Run("{{ALT}{TAB}}", m => m.AltTabPartner = Rig.Match("Chrome.exe"));
            Assert.True(rig.Progress.Any(p => p.Message == "Switching to Orders"));
        }

        // ---- Focus guard ----

        static void FocusSwapsAfterFirstCopy(Rig rig, int settleAfterMs)
        {
            rig.Input.OnCall = line =>
            {
                if (line != "combo LCONTROL+C") return;
                rig.Windows.SetForeground(rig.Notepad.Handle);
                if (settleAfterMs > 0) rig.Clock.After(settleAfterMs, () => rig.Windows.SetForeground(rig.Excel.Handle));
            };
        }

        static void RunInExcel(MacroDef m) { m.Target = TargetKind.SpecificWindow; m.TargetWindow = Rig.Match("EXCEL.EXE"); }

        [Test]
        public static void FocusGuardStopsWhenAnotherWindowTakesFocus()
        {
            var rig = new Rig();
            FocusSwapsAfterFirstCopy(rig, 0);
            RunResult result = rig.Run("{{CTRL}{C}}{{CTRL}{V}}", RunInExcel);
            Assert.False(result.Completed);
            Assert.False(result.Cancelled);
            Assert.Equal("Stopped because Untitled - Notepad took focus.", result.Error);
            Assert.Equal("combo LCONTROL+C", rig.Log);
            Assert.True(rig.Clock.NowMs >= 750, "waited the settle time first");
        }

        [Test]
        public static void FocusGuardWaitsForFocusToSettle()
        {
            var rig = new Rig();
            FocusSwapsAfterFirstCopy(rig, 300);
            RunResult result = rig.Run("{{CTRL}{C}}{{CTRL}{V}}", RunInExcel);
            Assert.True(result.Completed, result.Error);
            Assert.Equal("combo LCONTROL+C|combo LCONTROL+V", rig.Log);
        }

        [Test]
        public static void FocusGuardAllowsSettlingJustInTime()
        {
            var rig = new Rig();
            FocusSwapsAfterFirstCopy(rig, 700);
            RunResult result = rig.Run("{{CTRL}{C}}{{CTRL}{V}}", RunInExcel);
            Assert.True(result.Completed, result.Error);
        }

        [Test]
        public static void FocusGuardAcceptsThePartnerToo()
        {
            var rig = new Rig();
            rig.Input.OnCall = line => { if (line == "combo LCONTROL+C") rig.Windows.SetForeground(rig.Chrome.Handle); };
            RunResult result = rig.Run("{{CTRL}{C}}{{CTRL}{V}}", m => { RunInExcel(m); m.AltTabPartner = Rig.Match("Chrome.exe"); });
            Assert.True(result.Completed, result.Error);
        }

        [Test]
        public static void FocusGuardCanBeTurnedOff()
        {
            var rig = new Rig();
            FocusSwapsAfterFirstCopy(rig, 0);
            RunResult result = rig.Run("{{CTRL}{C}}{{CTRL}{V}}", m => { RunInExcel(m); m.StopIfFocusLeaves = false; });
            Assert.True(result.Completed, result.Error);
            Assert.Equal("combo LCONTROL+C|combo LCONTROL+V", rig.Log);
        }

        [Test]
        public static void MacroKitTakingFocusAlwaysStops()
        {
            var rig = new Rig();
            rig.Input.OnCall = line => { if (line == "combo LCONTROL+C") rig.Windows.Foreground = rig.Own.Handle; };
            RunResult result = rig.Run("{{CTRL}{C}}{{CTRL}{V}}", m => m.StopIfFocusLeaves = false);
            Assert.Equal("Stopped because Macro Kit took focus.", result.Error);
            Assert.Equal("combo LCONTROL+C", rig.Log);
        }

        [Test]
        public static void PreviousWindowModeTolleratesOtherWindows()
        {
            var rig = new Rig();
            FocusSwapsAfterFirstCopy(rig, 0);
            RunResult result = rig.Run("{{CTRL}{C}}{{CTRL}{V}}");
            Assert.True(result.Completed, result.Error);
        }

        [Test]
        public static void FocusGuardDoesNotBlockPausesOrVariables()
        {
            var rig = new Rig();
            rig.Windows.Foreground = rig.Own.Handle;
            // Priming would normally fix the focus; make Macro Kit steal it again right after.
            rig.Clock.After(1, () => rig.Windows.Foreground = rig.Own.Handle);
            RunResult result = rig.Run("{{PAUSE:5}}{{VARSET:a:1}}{{SETCLIPBOARD:x}}");
            Assert.True(result.Completed, result.Error);
        }

        // ---- Held keys ----

        [Test]
        public static void HeldKeysAndButtonsAreReleasedOnCancel()
        {
            var rig = new Rig();
            rig.Clock.After(500, () => rig.Cts.Cancel());
            RunResult result = rig.Run("{{KEYDOWN:SHIFT}}{{MLEFTDOWN}}{{PAUSE:1000}}");
            Assert.True(result.Cancelled);
            Assert.False(result.Completed);
            Assert.Equal(0, rig.Input.HeldKeys.Count);
            Assert.Equal(0, rig.Input.HeldButtons.Count);
            Assert.Equal("down LSHIFT|button Left down|up LSHIFT|button Left up", rig.Log);
        }

        [Test]
        public static void HeldKeysAreReleasedWhenTheRunFails()
        {
            var rig = new Rig();
            FocusSwapsAfterFirstCopy(rig, 0);
            RunResult result = rig.Run("{{KEYDOWN:CTRL}}{{CTRL}{C}}{{ENTER}}", RunInExcel);
            Assert.NotNull(result.Error);
            Assert.Equal(0, rig.Input.HeldKeys.Count);
        }

        [Test]
        public static void ReleasedKeysAreNotReleasedAgain()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{KEYDOWN:A}}{{KEYUP:A}}{{MRIGHTDOWN}}{{MRIGHTUP}}");
            Assert.True(result.Completed, result.Error);
            Assert.Equal("down A|up A|button Right down|button Right up", rig.Log);
        }

        [Test]
        public static void AKeyHeldTwiceIsReleasedOnce()
        {
            var rig = new Rig();
            rig.Clock.After(10, () => rig.Cts.Cancel());
            rig.Run("{{KEYDOWN:A}}{{KEYDOWN:A}}{{PAUSE:100}}");
            Assert.Equal(1, rig.Input.Log.Count(l => l == "up A"));
        }

        [Test]
        public static void ReleaseFailuresDoNotEscape()
        {
            var rig = new Rig();
            rig.Clock.After(10, () => rig.Cts.Cancel());
            rig.Input.FailWhen = line => line.StartsWith("up ");
            RunResult result = rig.Run("{{KEYDOWN:A}}{{PAUSE:100}}");
            Assert.True(result.Cancelled);
        }

        // ---- Passes ----

        [Test]
        public static void OnceRunsOnePassAndIgnoresTheCount()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ENTER}}", m => { m.Repeat = RepeatMode.Once; m.RepeatTimes = 9; m.RepeatDelayMs = 500; });
            Assert.True(result.Completed);
            Assert.Equal(1, result.PassesDone);
            Assert.Equal("combo ENTER", rig.Log);
            Assert.Equal(0, rig.Clock.Sleeps.Count);
        }

        [Test]
        public static void TimesRunsThatManyPasses()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ENTER}}", m => { m.Repeat = RepeatMode.Times; m.RepeatTimes = 3; });
            Assert.True(result.Completed);
            Assert.Equal(3, result.PassesDone);
            Assert.Equal("combo ENTER|combo ENTER|combo ENTER", rig.Log);
        }

        [Test]
        public static void TimesCountsAtLeastOnePass()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ENTER}}", m => { m.Repeat = RepeatMode.Times; m.RepeatTimes = 0; });
            Assert.Equal(1, result.PassesDone);
        }

        [Test]
        public static void RepeatDelayIsOnlyBetweenPasses()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ENTER}}", m => { m.Repeat = RepeatMode.Times; m.RepeatTimes = 3; m.RepeatDelayMs = 500; });
            Assert.True(result.Completed);
            Assert.SequenceEqual(new[] { 500, 500 }, rig.Clock.Sleeps);
            Assert.Equal(1000L, rig.Clock.NowMs);

            var single = new Rig();
            single.Run("{{ENTER}}", m => { m.Repeat = RepeatMode.Times; m.RepeatTimes = 1; m.RepeatDelayMs = 500; });
            Assert.Equal(0, single.Clock.Sleeps.Count);
        }

        [Test]
        public static void ContinuousStopsImmediatelyWhenCancelledMidPause()
        {
            var rig = new Rig();
            rig.Clock.At(2500, () => rig.Cts.Cancel());
            RunResult result = rig.Run("{{ENTER}}{{PAUSE:1000}}", m => m.Repeat = RepeatMode.Continuous);
            Assert.True(result.Cancelled);
            Assert.False(result.Completed);
            Assert.Null(result.Error);
            Assert.Equal(2, result.PassesDone);
            Assert.Equal(2500L, rig.Clock.NowMs);
            Assert.Equal(3, rig.Input.Log.Count);
        }

        [Test]
        public static void CancellingDuringTheRepeatDelayIsImmediate()
        {
            var rig = new Rig();
            rig.Clock.At(300, () => rig.Cts.Cancel());
            RunResult result = rig.Run("{{ENTER}}", m => { m.Repeat = RepeatMode.Times; m.RepeatTimes = 5; m.RepeatDelayMs = 1000; });
            Assert.True(result.Cancelled);
            Assert.Equal(1, result.PassesDone);
            Assert.Equal(300L, rig.Clock.NowMs);
        }

        [Test]
        public static void CancelledBeforeTheStartSendsNothing()
        {
            var rig = new Rig();
            rig.Cts.Cancel();
            RunResult result = rig.Run("{{ENTER}}");
            Assert.True(result.Cancelled);
            Assert.Equal(0, rig.Input.Log.Count);
        }

        [Test]
        public static void ExampleScriptRunsFivePasses()
        {
            var rig = new Rig();
            RunResult result = rig.Run(ExampleScript, m => { m.Repeat = RepeatMode.Times; m.RepeatTimes = 5; m.AltTabPartner = Rig.Match("Chrome.exe"); });
            Assert.True(result.Completed, result.Error);
            Assert.Equal(5, result.PassesDone);
            Assert.Equal(15, rig.Input.Log.Count);
            Assert.Equal("combo LCONTROL+C|combo LCONTROL+V|combo DOWN", string.Join("|", rig.Input.Log.Take(3)));
            Assert.Equal(5 * 350L, rig.Clock.NowMs);
        }

        // ---- Timing ----

        [Test]
        public static void KeyDelayFollowsEveryInputStep()
        {
            var rig = new Rig();
            rig.Run("{{ENTER}}{{VARSET:a:1}}{{TAB}}{{PAUSE:50}}hi", m => m.KeyDelayMs = 10);
            Assert.SequenceEqual(new[] { 10, 10, 50, 10 }, rig.Clock.Sleeps);
        }

        [Test]
        public static void KeyPressRepeatsWithItsOwnDelay()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{KEYPRESS:DOWN:3:100}}");
            Assert.True(result.Completed, result.Error);
            Assert.Equal("combo DOWN|combo DOWN|combo DOWN", rig.Log);
            Assert.SequenceEqual(new[] { 100, 100 }, rig.Clock.Sleeps);
        }

        [Test]
        public static void KeyPressWithoutDelayUsesTheKeyDelay()
        {
            var rig = new Rig();
            rig.Run("{{KEYPRESS:DOWN:3}}", m => m.KeyDelayMs = 20);
            Assert.SequenceEqual(new[] { 20, 20, 20 }, rig.Clock.Sleeps);
        }

        [Test]
        public static void KeyPressCanBeCancelledBetweenPresses()
        {
            var rig = new Rig();
            rig.Clock.At(150, () => rig.Cts.Cancel());
            RunResult result = rig.Run("{{KEYPRESS:DOWN:100:100}}");
            Assert.True(result.Cancelled);
            Assert.Equal(2, rig.Input.Log.Count);
        }

        [Test]
        public static void HotkeyOriginWaitsForModifiersToBeReleased()
        {
            var rig = new Rig();
            rig.Input.PhysicallyDown.Add(0xA2);
            rig.Input.PhysicallyDown.Add(0xA4);
            rig.Clock.At(400, () => rig.Input.PhysicallyDown.Clear());
            long firstInput = -1;
            rig.Input.OnCall = line => { if (firstInput < 0) firstInput = rig.Clock.NowMs; };
            RunResult result = rig.Run("{{ENTER}}", null, RunOrigin.Hotkey);
            Assert.True(result.Completed, result.Error);
            Assert.True(firstInput >= 400 && firstInput < 500, "first input at " + firstInput);
        }

        [Test]
        public static void HotkeyOriginGivesUpWaitingAfterThreeSeconds()
        {
            var rig = new Rig();
            rig.Input.PhysicallyDown.Add(0x5B);
            long firstInput = -1;
            rig.Input.OnCall = line => { if (firstInput < 0) firstInput = rig.Clock.NowMs; };
            RunResult result = rig.Run("{{ENTER}}", null, RunOrigin.Hotkey);
            Assert.True(result.Completed, result.Error);
            Assert.True(firstInput >= 3000 && firstInput < 3100, "first input at " + firstInput);
        }

        [Test]
        public static void HotkeyOriginCanBeCancelledWhileWaiting()
        {
            var rig = new Rig();
            rig.Input.PhysicallyDown.Add(0xA0);
            rig.Clock.At(200, () => rig.Cts.Cancel());
            RunResult result = rig.Run("{{ENTER}}", null, RunOrigin.Hotkey);
            Assert.True(result.Cancelled);
            Assert.Equal(0, rig.Input.Log.Count);
        }

        [Test]
        public static void OtherOriginsDoNotWaitForModifiers()
        {
            foreach (RunOrigin origin in new[] { RunOrigin.MainWindow, RunOrigin.Deck, RunOrigin.Tray })
            {
                var rig = new Rig();
                rig.Input.PhysicallyDown.Add(0xA2);
                RunResult result = rig.Run("{{ENTER}}", null, origin);
                Assert.True(result.Completed, result.Error);
                Assert.Equal(0, rig.Clock.Sleeps.Count, origin.ToString());
            }
        }

        // ---- What gets sent ----

        [Test]
        public static void TextIsTypedWithLineBreaksAsEnter()
        {
            var rig = new Rig();
            rig.Run("hello\nthere{{TAB}}x");
            Assert.Equal("text hello\\nthere|combo TAB|text x", rig.Log);
        }

        [Test]
        public static void LineBreaksAreIgnoredWhenOff()
        {
            var rig = new Rig();
            rig.Run("a\nb", m => m.NewLineIsEnter = false);
            Assert.Equal("text ab", rig.Log);
        }

        [Test]
        public static void ModifierComboLogsPressOrder()
        {
            var rig = new Rig();
            rig.Run("{{CTRL}{SHIFT}{ESC}}{{WIN}{R}}{{RCTRL}{RALT}{DEL}}");
            Assert.Equal("combo LCONTROL+LSHIFT+ESCAPE|combo LWIN+R|combo RCONTROL+RALT+DELETE", rig.Log);
        }

        [Test]
        public static void KeyDownAndKeyUp()
        {
            var rig = new Rig();
            rig.Run("{{KEYDOWN:SHIFT}}{{DOWN}}{{KEYUP:SHIFT}}");
            Assert.Equal("down LSHIFT|combo DOWN|up LSHIFT", rig.Log);
        }

        [Test]
        public static void EveryMouseCommandReachesTheSink()
        {
            var rig = new Rig();
            RunResult result = rig.Run(
                "{{MOUSEXY:100,200}}{{MOUSEMOVE:5,-3}}{{MOUSEPOS:100,200}}{{MLEFTDOWN}}{{MLEFTUP}}{{MRIGHTDOWN}}{{MRIGHTUP}}{{MMIDDLEDOWN}}{{MMIDDLEUP}}{{LBUTTON}}{{RBUTTON}}{{MBUTTON}}{{XBUTTON1}}{{XBUTTON2}}");
            Assert.True(result.Completed, result.Error);
            Assert.Equal(
                "moveto 100,200|moveby 5,-3|norm 100,200|button Left down|button Left up|button Right down|button Right up|button Middle down|button Middle up"
                + "|button Left down|button Left up|button Right down|button Right up|button Middle down|button Middle up|button X1 down|button X1 up|button X2 down|button X2 up",
                rig.Log);
        }

        [Test]
        public static void DoubleClicks()
        {
            var rig = new Rig();
            rig.Run("{{MLEFTDBLCLICK}}{{MRIGHTDBLCLICK}}");
            Assert.Equal(
                "button Left down|button Left up|button Left down|button Left up|button Right down|button Right up|button Right down|button Right up",
                rig.Log);
        }

        [Test]
        public static void ScrollingSendsOneNotchAtATime()
        {
            var rig = new Rig();
            rig.Run("{{MSCROLLUP}}{{MSCROLLDOWN:2}}{{MSCROLLLEFT}}{{MSCROLLRIGHT:2}}");
            Assert.Equal("wheel 120 v|wheel -120 v|wheel -120 v|wheel -120 h|wheel 120 h|wheel 120 h", rig.Log);
        }

        [Test]
        public static void SavedMousePositionIsRestored()
        {
            var rig = new Rig();
            rig.Input.CursorPosition = new ScreenPoint(50, 60);
            rig.Run("{{MSAVEPOS}}{{MOUSEXY:1,1}}{{MLOADPOS}}");
            Assert.Equal("moveto 1,1|moveto 50,60", rig.Log);
        }

        [Test]
        public static void LoadingBeforeSavingUsesTheStartingPosition()
        {
            var rig = new Rig();
            rig.Input.CursorPosition = new ScreenPoint(70, 80);
            rig.Run("{{MOUSEXY:1,1}}{{MLOADPOS}}");
            Assert.Equal("moveto 1,1|moveto 70,80", rig.Log);
        }

        [Test]
        public static void MousePositionVariablesWorkInNumbers()
        {
            var rig = new Rig();
            rig.Input.CursorPosition = new ScreenPoint(50, 60);
            rig.Run("{{MSAVEPOS}}{{MOUSEXY:5,5}}{{MOUSEXY:$MOUSE_X,$MOUSE_Y}}{{MOUSEMOVE:$MOUSE_X,0}}");
            Assert.Equal("moveto 5,5|moveto 50,60|moveby 50,0", rig.Log);
        }

        [Test]
        public static void BlockedInputEndsTheRunWithAMessage()
        {
            var rig = new Rig();
            rig.Input.FailWhen = line => line.StartsWith("combo");
            RunResult result = rig.Run("{{ENTER}}{{TAB}}");
            Assert.False(result.Completed);
            Assert.False(result.Cancelled);
            Assert.Contains(result.Error, "Couldn't finish the macro");
        }

        // ---- Variables and clipboard ----

        [Test]
        public static void VariablesAreSetAndTyped()
        {
            var rig = new Rig();
            rig.Run("{{VARSET:name:Sam}}{{OUTPUT:name}}{{$name}}");
            Assert.Equal("text Sam|text Sam", rig.Log);
        }

        [Test]
        public static void VariablesCanReferToOtherVariables()
        {
            var rig = new Rig();
            rig.Run("{{VARSET:a:x}}{{VARSET:b:$a$a!}}{{OUTPUT:b}}");
            Assert.Equal("text xx!", rig.Log);
        }

        [Test]
        public static void UnknownVariablesAreEmpty()
        {
            var rig = new Rig();
            rig.Run("{{VARSET:a:[$nothing]}}{{OUTPUT:a}}{{OUTPUT:missing}}");
            Assert.Equal("text []", rig.Log);
        }

        [Test]
        public static void PredefinedVariables()
        {
            var rig = new Rig();
            rig.Run("{{VARSET:v:a$SMCOLONb$SMDOLLARc$SMLCBd$SMRCBe$SMSPACEf}}{{OUTPUT:v}}{{VARSET:e:x$SMENTERy}}{{OUTPUT:e}}");
            Assert.Equal("text a:b$c{d}e f|text x\\ny", rig.Log);
        }

        [Test]
        public static void VariableNamesIgnoreCase()
        {
            var rig = new Rig();
            rig.Run("{{VARSET:Row:7}}{{VARSET:x:$ROW}}{{OUTPUT:x}}");
            Assert.Equal("text 7", rig.Log);
        }

        [Test]
        public static void VariablesLiveAcrossPasses()
        {
            var rig = new Rig();
            rig.Run("{{OUTPUT:n}}{{VARSET:n:X}}", m => { m.Repeat = RepeatMode.Times; m.RepeatTimes = 2; });
            Assert.Equal("text X", rig.Log);
        }

        [Test]
        public static void VariablesDoNotSurviveAcrossRuns()
        {
            var rig = new Rig();
            rig.Run("{{VARSET:n:X}}");
            rig.Run("{{OUTPUT:n}}");
            Assert.Equal(0, rig.Input.Log.Count);
        }

        [Test]
        public static void UnsetRemovesVariables()
        {
            var rig = new Rig();
            rig.Run("{{VARSET:a:1}}{{VARSET:b:2}}{{VARUNSET:a}}{{OUTPUT:a}}{{OUTPUT:b}}{{VARUNSETALL}}{{OUTPUT:b}}{{VARSET:s:$SMSPACE}}{{OUTPUT:s}}");
            Assert.Equal("text 2|text  ", rig.Log, "UNSETALL keeps the built-in $SM variables");
        }

        [Test]
        public static void ClipboardCommands()
        {
            var rig = new Rig();
            rig.Run("{{SETCLIPBOARD:hello$SMSPACEworld}}");
            Assert.Equal("hello world", rig.Clipboard.Text);

            rig.Clipboard.Text = "abc";
            rig.Run("{{VARSETFROMCLIPBOARD:c}}{{OUTPUT:c}}");
            Assert.Equal("text abc", rig.Log);

            var empty = new Rig();
            empty.Run("{{VARSETFROMCLIPBOARD:c}}{{OUTPUT:c}}");
            Assert.Equal(0, empty.Input.Log.Count);
        }

        [Test]
        public static void NumbersFromVariablesAreFilledInAtRunTime()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{VARSET:d:75}}{{PAUSE:$d}}");
            Assert.True(result.Completed, result.Error);
            Assert.SequenceEqual(new[] { 75 }, rig.Clock.Sleeps);

            var count = new Rig();
            count.Run("{{VARSET:n:2}}{{KEYPRESS:A:$n}}");
            Assert.Equal("combo A|combo A", count.Log);
        }

        [Test]
        public static void BadNumbersFromVariablesStopTheRun()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{VARSET:d:abc}}{{PAUSE:$d}}");
            Assert.False(result.Completed);
            Assert.Contains(result.Error, "Line 1");
            Assert.Contains(result.Error, "whole number");

            var unset = new Rig();
            RunResult empty = unset.Run("{{PAUSE:$nothing}}");
            Assert.Contains(empty.Error, "whole number");
        }

        // ---- Progress ----

        [Test]
        public static void ProgressReportsPassesAndSteps()
        {
            var rig = new Rig();
            rig.Run("{{ENTER}}{{TAB}}", m => { m.Repeat = RepeatMode.Times; m.RepeatTimes = 2; });
            List<RunProgress> steps = rig.Progress.Where(p => p.StepIndex > 0).ToList();
            Assert.Equal(4, steps.Count);
            Assert.SequenceEqual(new[] { 1, 1, 2, 2 }, steps.Select(p => p.Pass));
            Assert.SequenceEqual(new[] { 1, 2, 1, 2 }, steps.Select(p => p.StepIndex));
            Assert.True(steps.All(p => p.StepCount == 2 && p.TotalPasses == 2));
            Assert.SequenceEqual(new[] { "Press Enter", "Press Tab", "Press Enter", "Press Tab" }, steps.Select(p => p.Message));
        }

        [Test]
        public static void ContinuousProgressHasNoTotal()
        {
            var rig = new Rig();
            rig.Clock.At(10, () => rig.Cts.Cancel());
            rig.Run("{{ENTER}}{{PAUSE:100}}", m => m.Repeat = RepeatMode.Continuous);
            Assert.True(rig.Progress.All(p => p.TotalPasses == 0));
        }

        [Test]
        public static void AFailingProgressListenerDoesNotStopTheRun()
        {
            var rig = new Rig();
            rig.Runner.Progress += p => { throw new InvalidOperationException("listener bug"); };
            RunResult result = rig.Run("{{ENTER}}");
            Assert.True(result.Completed, result.Error);
            Assert.Equal("combo ENTER", rig.Log);
        }

        [Test]
        public static void RunnerCanBeUsedAgain()
        {
            var rig = new Rig();
            Assert.True(rig.Run("{{ENTER}}").Completed);
            Assert.True(rig.Run("{{TAB}}").Completed);
            Assert.Equal("combo ENTER|combo TAB", rig.Log);
        }

        // ---- The target is never the taskbar, the desktop or the Start menu ----

        [Test]
        public static void DeckAndHotkeyOriginsSkipAForegroundThatIsNotSwitchable()
        {
            foreach (RunOrigin origin in new[] { RunOrigin.Deck, RunOrigin.Hotkey })
            {
                foreach (string shellClass in new[] { "Shell_TrayWnd", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow" })
                {
                    var rig = new Rig();
                    WindowInfo shell = rig.Windows.AddShellWindow(700, "Taskbar", shellClass);
                    rig.Windows.Foreground = shell.Handle;            // the user just clicked the taskbar or the desktop
                    rig.Windows.LastExternalForeground = rig.Chrome.Handle;
                    RunResult result = rig.Run("{{CTRL}{V}}", null, origin);
                    Assert.True(result.Completed, result.Error);
                    Assert.SequenceEqual(new[] { "activate Orders (ok)" }, rig.Windows.ActivateLog, origin + " " + shellClass);
                    Assert.False(rig.Windows.ActivatedHandles.Contains(shell.Handle), "the shell window was never the target");
                    Assert.Equal("combo LCONTROL+V", rig.Log);
                }
            }
        }

        [Test]
        public static void AWindowAltTabWouldNotListIsNotTheTargetEither()
        {
            var rig = new Rig();
            rig.Windows.NotSwitchable.Add(rig.Notepad.Handle);      // switchable-looking but the filter says no
            rig.Windows.Foreground = rig.Notepad.Handle;
            rig.Windows.LastExternalForeground = rig.Excel.Handle;
            RunResult result = rig.Run("{{ENTER}}", null, RunOrigin.Deck);
            Assert.True(result.Completed, result.Error);
            Assert.SequenceEqual(new[] { rig.Excel.Handle }, rig.Windows.ActivatedHandles);
        }

        [Test]
        public static void ANormalForegroundStillWinsOverTheLastExternalWindow()
        {
            var rig = new Rig();
            rig.Windows.SetForeground(rig.Chrome.Handle);
            rig.Windows.LastExternalForeground = rig.Excel.Handle;
            RunResult result = rig.Run("{{ENTER}}", null, RunOrigin.Hotkey);
            Assert.True(result.Completed, result.Error);
            Assert.Equal(0, rig.Windows.ActivateLog.Count, "Orders was already in front and stays the target");
        }

        [Test]
        public static void NoForegroundWindowFallsBackToTheLastExternalOne()
        {
            var rig = new Rig();
            rig.Windows.Foreground = IntPtr.Zero;
            rig.Windows.LastExternalForeground = rig.Chrome.Handle;
            RunResult result = rig.Run("{{ENTER}}", null, RunOrigin.Deck);
            Assert.True(result.Completed, result.Error);
            Assert.SequenceEqual(new[] { "activate Orders (ok)" }, rig.Windows.ActivateLog);
        }

        [Test]
        public static void ShellForegroundWithNothingElseToRunInAsksForAClick()
        {
            var rig = new Rig();
            WindowInfo tray = rig.Windows.AddShellWindow(701, "", "Shell_TrayWnd");
            rig.Windows.Foreground = tray.Handle;
            rig.Windows.LastExternalForeground = IntPtr.Zero;
            RunResult result = rig.Run("{{CTRL}{V}}", null, RunOrigin.Deck);
            Assert.Equal("Click the window you want the macro to run in first.", result.Error);
            Assert.Equal(0, rig.Input.Log.Count, "nothing was pasted into the desktop");
        }

        // ---- A real Alt+Tab with a specific window and no partner ----

        const string CopyHopPasteScript = "{{CTRL}{C}}{{ALT}{TAB}}{{CTRL}{V}}{{ALT}{TAB}}{{DOWN}}";

        static void AltTabHops(Rig rig)
        {
            rig.Input.OnCall = line => { if (line == "combo LALT+TAB") rig.Windows.SimulateAltTab(); };
        }

        [Test]
        public static void SpecificWindowWithoutPartnerAllowsARealAltTabThereAndBack()
        {
            var rig = new Rig();
            AltTabHops(rig);
            RunResult result = rig.Run(CopyHopPasteScript, RunInExcel);
            Assert.True(result.Completed, result.Error);
            Assert.Equal("combo LCONTROL+C|combo LALT+TAB|combo LCONTROL+V|combo LALT+TAB|combo DOWN", rig.Log);
            Assert.Equal(rig.Excel.Handle, rig.Windows.Foreground);
        }

        [Test]
        public static void ThatAltTabLoopSurvivesManyPasses()
        {
            var rig = new Rig();
            AltTabHops(rig);
            RunResult result = rig.Run(CopyHopPasteScript, m => { RunInExcel(m); m.Repeat = RepeatMode.Times; m.RepeatTimes = 5; });
            Assert.True(result.Completed, result.Error);
            Assert.Equal(5, result.PassesDone);
            Assert.Equal(5, rig.Input.Log.Count(l => l == "combo LCONTROL+V"));
        }

        [Test]
        public static void ARealAltTabToAThirdWindowStopsTheRun()
        {
            var rig = new Rig();
            int tabs = 0;
            rig.Input.OnCall = line =>
            {
                if (line != "combo LALT+TAB") return;
                tabs++;
                if (tabs == 1) rig.Windows.SetForeground(rig.Chrome.Handle);       // the "other" window
                else if (tabs == 2) rig.Windows.SetForeground(rig.Excel.Handle);  // back
                else rig.Windows.SetForeground(rig.Notepad.Handle);               // a window nobody planned for
            };
            RunResult result = rig.Run("{{ALT}{TAB}}{{CTRL}{V}}{{ALT}{TAB}}{{CTRL}{V}}{{ALT}{TAB}}{{CTRL}{V}}", RunInExcel);
            Assert.False(result.Completed);
            Assert.Equal("Stopped because Untitled - Notepad took focus.", result.Error);
            Assert.Equal(2, rig.Input.Log.Count(l => l == "combo LCONTROL+V"), "the paste into the third window never happened");
        }

        [Test]
        public static void ARealAltTabIntoMacroKitStillStops()
        {
            var rig = new Rig();
            rig.Input.OnCall = line => { if (line == "combo LALT+TAB") rig.Windows.Foreground = rig.Own.Handle; };
            RunResult result = rig.Run("{{ALT}{TAB}}{{CTRL}{V}}", RunInExcel);
            Assert.Equal("Stopped because Macro Kit took focus.", result.Error);
            Assert.Equal("combo LALT+TAB", rig.Log);
        }

        [Test]
        public static void AltTabWaitsForFocusToSettleBeforeLearningTheOtherWindow()
        {
            var rig = new Rig();
            rig.Input.OnCall = line =>
            {
                if (line != "combo LALT+TAB") return;
                // Focus is nowhere for a moment, then on the taskbar's switcher surface, then on the real window.
                rig.Windows.Foreground = IntPtr.Zero;
                rig.Clock.After(100, () => rig.Windows.Foreground = rig.Windows.AddShellWindow(710, "Task Switching", "XamlExplorerHostIslandWindow").Handle);
                rig.Clock.After(300, () => rig.Windows.SetForeground(rig.Chrome.Handle));
            };
            RunResult result = rig.Run("{{ALT}{TAB}}{{CTRL}{V}}", RunInExcel);
            Assert.True(result.Completed, result.Error);
            Assert.Equal("combo LALT+TAB|combo LCONTROL+V", rig.Log);
        }

        [Test]
        public static void AltTabThatNeverSettlesStopsAfterTheGracePeriod()
        {
            var rig = new Rig();
            rig.Input.OnCall = line => { if (line == "combo LALT+TAB") rig.Windows.Foreground = IntPtr.Zero; };
            RunResult result = rig.Run("{{ALT}{TAB}}{{CTRL}{V}}", RunInExcel);
            Assert.False(result.Completed);
            Assert.Contains(result.Error, "took focus");
            Assert.True(rig.Clock.NowMs >= 750, "the full grace period passed first");
            Assert.Equal("combo LALT+TAB", rig.Log);
        }

        [Test]
        public static void AltTabThatStaysPutLearnsNothingAndIsHarmless()
        {
            var rig = new Rig();   // Alt+Tab does nothing: Excel stays in front
            RunResult result = rig.Run("{{ALT}{TAB}}{{CTRL}{V}}{{ALT}{TAB}}{{CTRL}{V}}", RunInExcel);
            Assert.True(result.Completed, result.Error);
        }

        [Test]
        public static void AWindowThatTakesFocusWithoutAnAltTabStillStops()
        {
            var rig = new Rig();
            rig.Input.OnCall = line => { if (line == "combo LCONTROL+C") rig.Windows.SetForeground(rig.Chrome.Handle); };
            RunResult result = rig.Run("{{CTRL}{C}}{{CTRL}{V}}", RunInExcel);
            Assert.Equal("Stopped because Orders took focus.", result.Error);
        }

        [Test]
        public static void AltTabWithoutPartnerFollowsOneOtherWindowOnly()
        {
            // The first hop lands on Orders and that is remembered; a later hop landing on Notepad is a stranger.
            var rig = new Rig();
            int tabs = 0;
            rig.Input.OnCall = line =>
            {
                if (line != "combo LALT+TAB") return;
                tabs++;
                rig.Windows.SetForeground(tabs == 1 ? rig.Chrome.Handle : tabs == 2 ? rig.Excel.Handle : rig.Notepad.Handle);
            };
            RunResult result = rig.Run("{{ALT}{TAB}}{{ENTER}}{{ALT}{TAB}}{{ENTER}}{{ALT}{TAB}}{{ENTER}}", RunInExcel);
            Assert.False(result.Completed);
            Assert.Contains(result.Error, "Untitled - Notepad took focus");
            Assert.Equal(2, rig.Input.Log.Count(l => l == "combo ENTER"));
        }

        // ---- Key names from variables ----

        [Test]
        public static void KeypressTakesItsKeyFromAVariable()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{VARSET:k:DOWN}}{{KEYPRESS:$k:2}}", null);
            Assert.True(result.Completed, result.Error);
            Assert.Equal("combo DOWN|combo DOWN", rig.Log);
        }

        [Test]
        public static void KeypressWithVariableKeyAndVariableCount()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{VARSET:k:TAB}}{{VARSET:n:3}}{{KEYPRESS:$k:$n:5}}", null);
            Assert.True(result.Completed, result.Error);
            Assert.Equal("combo TAB|combo TAB|combo TAB", rig.Log);
        }

        [Test]
        public static void KeydownAndKeyupTakeTheirKeyFromAVariable()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{VARSET:k:SHIFT}}{{KEYDOWN:$k}}{{KEYPRESS:A:1}}{{KEYUP:$k}}", null);
            Assert.True(result.Completed, result.Error);
            Assert.Equal("down LSHIFT|combo A|up LSHIFT", rig.Log);
        }

        [Test]
        public static void ChangingTheVariableChangesTheKeyOnTheNextUse()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{VARSET:k:UP}}{{KEYPRESS:$k:1}}{{VARSET:k:DOWN}}{{KEYPRESS:$k:1}}", null);
            Assert.True(result.Completed, result.Error);
            Assert.Equal("combo UP|combo DOWN", rig.Log);
        }

        [Test]
        public static void AnUnknownKeyNameAtRunTimeStopsTheRunWithAMessage()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ENTER}}{{VARSET:k:NOTAKEY}}{{KEYPRESS:$k:2}}{{TAB}}", null);
            Assert.False(result.Completed);
            Assert.Contains(result.Error, "Line 1");
            Assert.Contains(result.Error, "Unknown key “NOTAKEY”");
            Assert.Equal("combo ENTER", rig.Log);
        }

        [Test]
        public static void AVariableKeyThatWasNeverSetStopsTheRun()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{KEYDOWN:$nothing}}{{TAB}}", null);
            Assert.False(result.Completed);
            Assert.Contains(result.Error, "key name");
            Assert.Contains(result.Error, "empty");
            Assert.Equal(0, rig.Input.Log.Count);
        }

        [Test]
        public static void AWrongKeyInKeyupLeavesNothingHeld()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{KEYDOWN:SHIFT}}{{VARSET:k:BOGUS}}{{KEYUP:$k}}", null);
            Assert.False(result.Completed);
            Assert.Equal(0, rig.Input.HeldKeys.Count, "the held Shift is released when the run stops");
        }

        // ---- The shell hides Macro Kit before the first step ----

        [Test]
        public static void TargetReadyRunsAfterPrimingAndBeforeAnyInput()
        {
            var rig = new Rig();
            rig.Windows.Foreground = rig.Own.Handle;      // Macro Kit is in front, so the target has to be activated
            var seen = new List<string>();
            rig.Runner.TargetReady = token =>
            {
                seen.Add("inputs=" + rig.Input.Log.Count + " activations=" + rig.Windows.ActivateLog.Count + " fg=" + rig.Windows.Foreground);
            };
            RunResult result = rig.Run("{{ENTER}}{{TAB}}", null);
            Assert.True(result.Completed, result.Error);
            Assert.SequenceEqual(new[] { "inputs=0 activations=1 fg=" + rig.Excel.Handle }, seen);
            Assert.Equal("combo ENTER|combo TAB", rig.Log);
        }

        [Test]
        public static void TargetReadyRunsOnceNotOncePerPass()
        {
            var rig = new Rig();
            int calls = 0;
            rig.Runner.TargetReady = token => calls++;
            RunResult result = rig.Run("{{ENTER}}", m => { m.Repeat = RepeatMode.Times; m.RepeatTimes = 4; });
            Assert.True(result.Completed, result.Error);
            Assert.Equal(1, calls);
        }

        [Test]
        public static void TargetReadyRunsAfterThePartnerAndTargetAreActivated()
        {
            var rig = new Rig();
            int activations = -1;
            rig.Runner.TargetReady = token => activations = rig.Windows.ActivateLog.Count;
            RunResult result = rig.Run("{{ENTER}}", m => m.AltTabPartner = Rig.Match("Chrome.exe"));
            Assert.True(result.Completed, result.Error);
            Assert.Equal(2, activations, "partner and target");
        }

        [Test]
        public static void TargetReadyIsNotCalledWhenTheRunCannotStart()
        {
            var rig = new Rig();
            int calls = 0;
            rig.Runner.TargetReady = token => calls++;
            rig.Run("{{NOPE}}");
            rig.Run("{{ENTER}}", m => { m.Target = TargetKind.SpecificWindow; m.TargetWindow = Rig.Match("Missing.exe"); });
            rig.Windows.Foreground = rig.Own.Handle;
            rig.Windows.ActivateFails.Add(rig.Excel.Handle);
            rig.Run("{{ENTER}}");
            Assert.Equal(0, calls);
        }

        [Test]
        public static void TargetReadyGetsTheRunsCancellationTokenAndCancelsTheRun()
        {
            var rig = new Rig();
            rig.Runner.TargetReady = token =>
            {
                rig.Cts.Cancel();
                token.ThrowIfCancellationRequested();   // what a waiting shell does when the user presses Esc
            };
            RunResult result = rig.Run("{{ENTER}}");
            Assert.True(result.Cancelled);
            Assert.Equal(0, rig.Input.Log.Count);
        }

        [Test]
        public static void AFailingTargetReadyHandlerDoesNotStopTheRun()
        {
            var rig = new Rig();
            rig.Runner.TargetReady = token => { throw new InvalidOperationException("handler bug"); };
            RunResult result = rig.Run("{{ENTER}}");
            Assert.True(result.Completed, result.Error);
            Assert.Equal("combo ENTER", rig.Log);
        }

        [Test]
        public static void IfHidingMovedTheFocusTheTargetIsBroughtBack()
        {
            var rig = new Rig();
            rig.Runner.TargetReady = token => rig.Windows.Foreground = rig.Notepad.Handle;   // Windows picked another window when Macro Kit hid
            RunResult result = rig.Run("{{ENTER}}", RunInExcel);
            Assert.True(result.Completed, result.Error);
            Assert.SequenceEqual(new[] { "activate Book1 - Excel (ok)" }, rig.Windows.ActivateLog);
            Assert.Equal(rig.Excel.Handle, rig.Windows.Foreground);
        }

        [Test]
        public static void WithoutAHandlerNothingExtraIsActivated()
        {
            var rig = new Rig();
            RunResult result = rig.Run("{{ENTER}}");
            Assert.True(result.Completed, result.Error);
            Assert.Equal(0, rig.Windows.ActivateLog.Count);
        }
    }
}
