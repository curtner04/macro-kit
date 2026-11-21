using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MacroKit.Tests
{
    using MacroKit.Core;
    using MacroKit.Macros;

    /// <summary>A screen whose colours a test sets: one colour everywhere, or a function of the point and the time.</summary>
    public sealed class FakeScreen : IScreen
    {
        public Func<int, int, int?> PixelAt = (x, y) => 0xFFFFFF;
        public readonly List<string> Reads = new List<string>();

        public bool TryGetPixel(int x, int y, out int rgb)
        {
            Reads.Add(x + "," + y);
            int? value = PixelAt(x, y);
            rgb = value ?? 0;
            return value.HasValue;
        }
    }

    /// <summary>1.12.0: {{WAITPIXEL}} (wait until a spot in the window turns a colour) and {{INPUT}} (ask for a value while running).</summary>
    public static class WaitAndAskTests
    {
        const int Blue = 0x1E90FF;

        static MacroRunner Runner(FakeInputSink input, FakeWindowSystem windows, FakeClock clock)
        {
            var runner = new MacroRunner(input, windows, clock, new FakeClipboard());
            runner.ElevationProbe = () => false;
            return runner;
        }

        static WindowInfo Excel(FakeWindowSystem windows)
        {
            WindowInfo excel = windows.AddWindow(1, "Book1 - Excel", "EXCEL.EXE");
            excel.Left = 300; excel.Top = 150;
            windows.SetForeground(excel.Handle);
            return excel;
        }

        static RunResult Run(MacroRunner runner, string script, string name = "Orders")
        {
            var macro = new MacroDef { Name = name, Script = script, KeyDelayMs = 0 };
            ParseResult parsed = MacroParser.Parse(macro.Script, false);
            Assert.True(parsed.Ok, parsed.Ok ? "" : parsed.Errors[0].Message);
            return runner.Run(macro, parsed, RunOrigin.MainWindow, CancellationToken.None);
        }

        // ---------- {{WAITPIXEL}}: parsing and writing ----------

        [Test]
        public static void WaitPixelParses()
        {
            MacroStep step = MacroParser.Parse("{{WAITPIXEL:120,40:#1E90FF}}", false).Steps.Single();
            Assert.Equal(StepKind.WaitPixel, step.Kind);
            Assert.Equal(120, step.X);
            Assert.Equal(40, step.Y);
            Assert.Equal(Blue, step.Color);
            Assert.Equal(MacroParser.DefaultWaitPixelMs, step.Ms);
            Assert.False(step.SendsInput);
            Assert.Equal("Wait until 120, 40 in the window is #1E90FF (up to 10 seconds)", step.Describe());

            step = MacroParser.Parse("{{ waitpixel : -5, 7 : 1e90ff : 2500 }}", false).Steps.Single();
            Assert.Equal(-5, step.X);
            Assert.Equal(7, step.Y);
            Assert.Equal(Blue, step.Color);
            Assert.Equal(2500, step.Ms);
        }

        [Test]
        public static void WaitPixelProblemsAreExplained()
        {
            foreach (string bad in new[] { "{{WAITPIXEL}}", "{{WAITPIXEL:120,40}}", "{{WAITPIXEL:120:#1E90FF}}", "{{WAITPIXEL:120,40:blue}}", "{{WAITPIXEL:120,40:#1E90F}}", "{{WAITPIXEL:1,2:#1E90FF:5:6}}" })
                Assert.Contains(MacroParser.Parse(bad, false).Errors.Single().Message, "like {{WAITPIXEL:120,40:#1E90FF}}", bad);
            Assert.Contains(MacroParser.Parse("{{WAITPIXEL:1,2:#1E90FF:50}}", false).Errors.Single().Message, "from 100 to 3,600,000");
        }

        [Test]
        public static void WaitPixelIsWrittenAndReadInPlainLines()
        {
            Assert.Equal("{{WAITPIXEL:120,40:#1E90FF}}", MacroScript.WaitPixel(120, 40, Blue, MacroParser.DefaultWaitPixelMs));
            Assert.Equal("{{WAITPIXEL:120,40:#1E90FF:5000}}", MacroScript.WaitPixel(120, 40, Blue, 5000));
            Assert.Equal("wait for colour #1E90FF at 120, 40 in the window", PlainMacro.ToPlain("{{WAITPIXEL:120,40:#1E90FF}}", false));
            Assert.Equal("wait for colour #1E90FF at 120, 40 in the window 5s", PlainMacro.ToPlain("{{WAITPIXEL:120,40:#1E90FF:5000}}", false));
            Assert.Equal("{{WAITPIXEL:120,40:#1E90FF:5000}}", PlainMacro.ToScript("wait for colour #1E90FF at 120, 40 in the window 5s").Script);
            Assert.Equal("{{WAITPIXEL:120,40:#1E90FF}}", PlainMacro.ToScript("Wait for color 1e90ff at 120 40").Script);
            Assert.Equal("{{WAITWINDOW:Notepad}}", PlainMacro.ToScript("wait for Notepad").Script, "a window wait still reads as one");
        }

        [Test]
        public static void ColoursMatchWithinAFewShades()
        {
            Assert.True(MacroParser.ColorsMatch(Blue, Blue));
            Assert.True(MacroParser.ColorsMatch(Blue, 0x2896F5), "10 off in red, 6 in green, 10 in blue");
            Assert.False(MacroParser.ColorsMatch(Blue, 0x2990FF), "11 off in red");
            Assert.False(MacroParser.ColorsMatch(Blue, 0x1E90F4), "11 off in blue");
            Assert.Equal("#00FF00", MacroParser.FormatColor(0x00FF00));
            int rgb;
            Assert.False(MacroParser.TryColor("#GG0000", out rgb));
            Assert.False(MacroParser.TryColor("+1E90F", out rgb), "a sign isn't a hex digit");
        }

        [Test]
        public static void MacroKitOnlyCommandsAreFound()
        {
            Assert.SequenceEqual(new[] { "WAITPIXEL", "WINDOWXY" },
                MacroParser.MacroKitOnlyCommands("{{FOCUS:Excel}}{{waitpixel:1,2:#000000}}{{INPUT:row}}{{WINDOWXY:5,5}}{{WAITPIXEL:3,4:#FFFFFF}}"));
            Assert.Equal(0, MacroParser.MacroKitOnlyCommands("{{CTRL}{C}}{{INPUT:row}}{{WAITWINDOW:Notepad}}").Count);
        }

        // ---------- {{WAITPIXEL}}: running ----------

        [Test]
        public static void WaitPixelWaitsForTheColourThenCarriesOn()
        {
            var input = new FakeInputSink();
            var windows = new FakeWindowSystem();
            var clock = new FakeClock();
            Excel(windows);
            var screen = new FakeScreen { PixelAt = (x, y) => x == 420 && y == 190 && clock.NowMs >= 1500 ? Blue : 0xFFFFFF };
            MacroRunner runner = Runner(input, windows, clock);
            runner.Screen = screen;
            RunResult result = Run(runner, "{{WAITPIXEL:120,40:#1E90FF}}{{ENTER}}");
            Assert.True(result.Completed, result.Error);
            Assert.True(clock.NowMs >= 1500 && clock.NowMs < 1800, "it waited for the colour: " + clock.NowMs);
            Assert.True(screen.Reads.All(r => r == "420,190"), "measured from the window: " + string.Join(" ", screen.Reads.Distinct()));
            Assert.True(input.Log.Any(l => l.Contains("ENTER")), string.Join("|", input.Log));
        }

        [Test]
        public static void WaitPixelFollowsAWindowThatMovesWhileItWaits()
        {
            var input = new FakeInputSink();
            var windows = new FakeWindowSystem();
            var clock = new FakeClock();
            WindowInfo excel = Excel(windows);
            clock.At(500, () => { excel.Left = 700; excel.Top = 100; });
            var screen = new FakeScreen { PixelAt = (x, y) => x == 820 && y == 140 ? Blue : 0xFFFFFF };
            MacroRunner runner = Runner(input, windows, clock);
            runner.Screen = screen;
            RunResult result = Run(runner, "{{WAITPIXEL:120,40:#1E90FF}}");
            Assert.True(result.Completed, result.Error);
            Assert.True(clock.NowMs >= 500 && clock.NowMs < 800, clock.NowMs.ToString());
        }

        [Test]
        public static void WaitPixelStopsWithWhatItSawWhenTheColourNeverComes()
        {
            var input = new FakeInputSink();
            var windows = new FakeWindowSystem();
            var clock = new FakeClock();
            Excel(windows);
            MacroRunner runner = Runner(input, windows, clock);
            runner.Screen = new FakeScreen { PixelAt = (x, y) => 0xFAFAFA };
            RunResult result = Run(runner, "{{WAITPIXEL:120,40:#1E90FF:2000}}{{ENTER}}");
            Assert.False(result.Completed);
            Assert.Contains(result.Error, "120, 40 in Book1 - Excel didn't turn #1E90FF within 2 seconds (it was #FAFAFA)");
            Assert.True(clock.NowMs >= 2000 && clock.NowMs < 2300, clock.NowMs.ToString());
            Assert.False(input.Log.Any(l => l.Contains("ENTER")), "nothing after it ran");
        }

        [Test]
        public static void WaitPixelWithoutAScreenStops()
        {
            var windows = new FakeWindowSystem();
            Excel(windows);
            RunResult result = Run(Runner(new FakeInputSink(), windows, new FakeClock()), "{{WAITPIXEL:1,2:#1E90FF}}");
            Assert.False(result.Completed);
            Assert.Contains(result.Error, "can't read the screen");
        }

        // ---------- {{INPUT}} ----------

        [Test]
        public static void InputParses()
        {
            MacroStep step = MacroParser.Parse("{{INPUT:row}}", false).Steps.Single();
            Assert.Equal(StepKind.AskInput, step.Kind);
            Assert.Equal("row", step.Var);
            Assert.Equal("", step.Value);
            Assert.False(step.SendsInput);
            Assert.Equal("Ask for $row", step.Describe());

            step = MacroParser.Parse("{{INPUT: $when :Time: now or later?}}", false).Steps.Single();
            Assert.Equal("when", step.Var);
            Assert.Equal("Time: now or later?", step.Value, "a colon inside the question stays");
            Assert.Equal("Ask for $when: “Time: now or later?”", step.Describe());

            Assert.Contains(MacroParser.Parse("{{INPUT}}", false).Errors.Single().Message, "needs a variable name");
            Assert.Contains(MacroParser.Parse("{{INPUT:two words}}", false).Errors.Single().Message, "letters, numbers and underscores");
        }

        [Test]
        public static void InputIsWrittenAndReadInPlainLines()
        {
            Assert.Equal("{{INPUT:row}}", MacroScript.Input("row"));
            Assert.Equal("{{INPUT:row:Which row?}}", MacroScript.Input("row", "  Which row? "));
            Assert.Equal("{{INPUT:x:a $SMRCB b}}", MacroScript.Input("x", "a } b"));
            Assert.Equal("ask for $row", PlainMacro.ToPlain("{{INPUT:row}}", false));
            Assert.Equal("ask for $row \"Which row?\"", PlainMacro.ToPlain("{{INPUT:row:Which row?}}", false));
            Assert.Equal("{{INPUT:row:Which row?}}", PlainMacro.ToScript("ask for $row \"Which row?\"").Script);
            Assert.Equal("{{INPUT:row}}", PlainMacro.ToScript("Ask $row").Script);
            Assert.NotNull(PlainMacro.ToScript("ask for row").Lines.First().Error, "a $ is needed");
        }

        [Test]
        public static void InputAsksAndKeepsTheAnswer()
        {
            var input = new FakeInputSink();
            var windows = new FakeWindowSystem();
            Excel(windows);
            MacroRunner runner = Runner(input, windows, new FakeClock());
            var asked = new List<string>();
            runner.AskForInput = (macro, name, question, token) => { asked.Add(macro + "|" + name + "|" + question); return "42"; };
            RunResult result = Run(runner, "{{VARSET:n:3}}{{INPUT:row:Row for item $n?}}{{OUTPUT:row}}{{INPUT:note}}");
            Assert.True(result.Completed, result.Error);
            Assert.SequenceEqual(new[] { "Orders|row|Row for item 3?", "Orders|note|" }, asked);
            Assert.True(input.Log.Contains("text 42"), string.Join("|", input.Log));
        }

        [Test]
        public static void CancellingTheQuestionStopsTheMacroQuietly()
        {
            var input = new FakeInputSink();
            var windows = new FakeWindowSystem();
            Excel(windows);
            MacroRunner runner = Runner(input, windows, new FakeClock());
            runner.AskForInput = (macro, name, question, token) => null;
            RunResult result = Run(runner, "{{INPUT:row}}{{OUTPUT:row}}{{ENTER}}");
            Assert.False(result.Completed);
            Assert.True(result.Cancelled, "the same as pressing Stop");
            Assert.False(input.Log.Any(l => l.StartsWith("text", StringComparison.Ordinal) || l.Contains("ENTER")), string.Join("|", input.Log));
        }

        [Test]
        public static void HeldKeysAreLetGoBeforeAsking()
        {
            var input = new FakeInputSink();
            var windows = new FakeWindowSystem();
            Excel(windows);
            MacroRunner runner = Runner(input, windows, new FakeClock());
            int heldWhenAsked = -1;
            runner.AskForInput = (macro, name, question, token) => { heldWhenAsked = input.HeldKeys.Count; return "x"; };
            RunResult result = Run(runner, "{{KEYDOWN:SHIFT}}{{INPUT:a}}{{OUTPUT:a}}");
            Assert.True(result.Completed, result.Error);
            Assert.Equal(0, heldWhenAsked, "Shift was let go first");
        }

        [Test]
        public static void InputWithNoOneToAskStops()
        {
            var windows = new FakeWindowSystem();
            Excel(windows);
            RunResult result = Run(Runner(new FakeInputSink(), windows, new FakeClock()), "{{INPUT:row}}");
            Assert.False(result.Completed);
            Assert.Contains(result.Error, "can't ask a question here");
        }

        // ---------- 1.12.1 ----------

        [Test]
        public static void HeldKeysAndButtonsArePressedAgainAfterTheAnswer()
        {
            var input = new FakeInputSink();
            var windows = new FakeWindowSystem();
            Excel(windows);
            MacroRunner runner = Runner(input, windows, new FakeClock());
            int heldWhenAsked = -1;
            runner.AskForInput = (macro, name, question, token) =>
            {
                heldWhenAsked = input.HeldButtons.Count + input.HeldKeys.Count;
                input.MouseMoveTo(5, 5);   // the person moves the pointer to answer
                return "x";
            };
            RunResult result = Run(runner, "{{MOUSEXY:400,300}}{{KEYDOWN:SHIFT}}{{MLEFTDOWN}}{{INPUT:a}}{{MOUSEXY:450,300}}{{MLEFTUP}}{{KEYUP:SHIFT}}");
            Assert.True(result.Completed, result.Error);
            Assert.Equal(0, heldWhenAsked, "let go first");
            int asked = input.Log.IndexOf("moveto 5,5"), next = input.Log.IndexOf("moveto 450,300");
            Assert.True(asked >= 0 && next > asked, string.Join("|", input.Log));
            List<string> between = input.Log.Skip(asked + 1).Take(next - asked - 1).ToList();
            Assert.True(between.Any(l => l.StartsWith("down ", StringComparison.Ordinal)), "Shift is down again: " + string.Join("|", between));
            Assert.SequenceEqual(new[] { "moveto 400,300", "button Left down" }, between.Where(l => !l.StartsWith("down ", StringComparison.Ordinal)),
                "the button goes down again where the pointer was, so the drag carries on");
            Assert.Equal(0, input.HeldKeys.Count + input.HeldButtons.Count, "nothing left held at the end");
        }

        [Test]
        public static void WaitPixelColourCanBeAVariable()
        {
            var input = new FakeInputSink();
            var windows = new FakeWindowSystem();
            var clock = new FakeClock();
            Excel(windows);
            var screen = new FakeScreen { PixelAt = (x, y) => clock.NowMs >= 500 ? Blue : 0xFFFFFF };
            MacroRunner runner = Runner(input, windows, clock);
            runner.Screen = screen;
            RunResult result = Run(runner, "{{VARSET:c:#1E90FF}}{{WAITPIXEL:120,40:$c:2000}}{{ENTER}}");
            Assert.True(result.Completed, result.Error);
            Assert.True(clock.NowMs >= 500, "it waited for the colour in $c: " + clock.NowMs);
        }
    }
}
