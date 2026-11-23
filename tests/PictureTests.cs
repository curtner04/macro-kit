using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MacroKit.Tests
{
    using MacroKit.Core;
    using MacroKit.Macros;

    /// <summary>1.13.0, Pro: saved pictures, {{WAITIMAGE}} (wait until one shows in the window) and {{CLICKIMAGE}} (then click it).</summary>
    public static class PictureTests
    {
        static void WithPro(bool pro, Action body)
        {
            LicenseInfo before = Edition.License;
            Edition.Set(pro ? new LicenseInfo { Serial = 1, Issued = new DateTime(2026, 11, 17), Name = "Test" } : null);
            try { body(); }
            finally { Edition.Set(before); }
        }

        // An 8 by 6 picture: a red frame around a blue and yellow checker, so no two places in it look alike.
        static PixelImage Button()
        {
            const int w = 8, h = 6;
            var pixels = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = x == 0 || y == 0 || x == w - 1 || y == h - 1 ? 0xD03030 : (x + y) % 2 == 0 ? 0x2050E0 : 0xF0D020;
            return new PixelImage(w, h, pixels);
        }

        // A screen that is light grey, with the picture drawn at (left, top) while shown() is true.
        static Func<int, int, int?> ScreenWith(PixelImage picture, Func<int> left, Func<int> top, Func<bool> shown)
        {
            return (x, y) =>
            {
                int px = x - left(), py = y - top();
                if (shown() && px >= 0 && py >= 0 && px < picture.Width && py < picture.Height) return picture[px, py];
                return 0xF0F0F0;
            };
        }

        static WindowInfo Excel(FakeWindowSystem windows)
        {
            WindowInfo excel = windows.AddWindow(1, "Book1 - Excel", "EXCEL.EXE");
            excel.Left = 300; excel.Top = 150; excel.Width = 200; excel.Height = 120;
            windows.SetForeground(excel.Handle);
            return excel;
        }

        static RunResult Run(MacroRunner runner, string script)
        {
            var macro = new MacroDef { Name = "Save", Script = script, KeyDelayMs = 0 };
            ParseResult parsed = MacroParser.Parse(macro.Script, false);
            Assert.True(parsed.Ok, parsed.Ok ? "" : parsed.Errors[0].Message);
            return runner.Run(macro, parsed, RunOrigin.MainWindow, CancellationToken.None);
        }

        static MacroRunner Runner(FakeInputSink input, FakeWindowSystem windows, FakeClock clock, FakeScreen screen, PixelImage picture)
        {
            var runner = new MacroRunner(input, windows, clock, new FakeClipboard());
            runner.ElevationProbe = () => false;
            runner.Screen = screen;
            runner.FindImage = name => string.Equals(name, "Save button", StringComparison.OrdinalIgnoreCase) ? picture : null;
            return runner;
        }

        // ---------- finding a picture ----------

        [Test]
        public static void FindsAPictureWhereItIs()
        {
            PixelImage needle = Button();
            var screen = new FakeScreen { PixelAt = ScreenWith(needle, () => 37, () => 21, () => true) };
            PixelImage shot = screen.Capture(0, 0, 120, 80);
            int x, y;
            Assert.True(shot.Find(needle, out x, out y));
            Assert.Equal(37, x);
            Assert.Equal(21, y);

            // In a corner too.
            shot = new FakeScreen { PixelAt = ScreenWith(needle, () => 112, () => 74, () => true) }.Capture(0, 0, 120, 80);
            Assert.True(shot.Find(needle, out x, out y));
            Assert.Equal(112, x);
            Assert.Equal(74, y);
        }

        [Test]
        public static void ToleratesShadesAndOneOddPixel()
        {
            PixelImage needle = Button();
            // Every pixel a little off, and one pixel quite different (a caret): still found.
            Func<int, int, int?> look = ScreenWith(needle, () => 10, () => 10, () => true);
            var screen = new FakeScreen
            {
                PixelAt = (x, y) =>
                {
                    if (x == 13 && y == 12) return 0x000000;
                    int c = look(x, y).Value;
                    int r = Math.Min(255, ((c >> 16) & 0xFF) + 20), g = Math.Max(0, ((c >> 8) & 0xFF) - 20);
                    return (r << 16) | (g << 8) | (c & 0xFF);
                }
            };
            int fx, fy;
            Assert.True(screen.Capture(0, 0, 60, 40).Find(needle, out fx, out fy));
            Assert.Equal(10, fx);
        }

        [Test]
        public static void DoesNotFindSomethingElse()
        {
            PixelImage needle = Button();
            // The same frame with the checker the other way round.
            Func<int, int, int?> look = ScreenWith(needle, () => 10, () => 10, () => true);
            var screen = new FakeScreen
            {
                PixelAt = (x, y) =>
                {
                    int c = look(x, y).Value;
                    return c == 0x2050E0 ? 0xF0D020 : c == 0xF0D020 ? 0x2050E0 : c;
                }
            };
            int fx, fy;
            Assert.False(screen.Capture(0, 0, 60, 40).Find(needle, out fx, out fy));
            Assert.False(new FakeScreen().Capture(0, 0, 60, 40).Find(needle, out fx, out fy), "a plain white screen");
            Assert.False(new FakeScreen().Capture(0, 0, 5, 5).Find(needle, out fx, out fy), "a screen smaller than the picture");
        }

        [Test]
        public static void CropKeepsTheRightPart()
        {
            PixelImage needle = Button();
            PixelImage part = needle.Crop(-2, 1, 4, 10);
            Assert.Equal(2, part.Width);
            Assert.Equal(5, part.Height);
            Assert.Equal(needle[1, 2], part[1, 1]);
            Assert.Null(needle.Crop(20, 20, 4, 4));
        }

        // ---------- saving pictures ----------

        [Test]
        public static void PicturesAreSavedAsPngAndReadBack()
        {
            string root = Path.Combine(Path.GetTempPath(), "mk-pictures-" + Guid.NewGuid().ToString("N"));
            try
            {
                var storage = new Storage(root);
                Assert.Equal(0, storage.ListImages().Count);
                Assert.Null(storage.LoadImage("Save button"));
                storage.SaveImage("Save button", Button());
                storage.SaveImage("apply", Button().Crop(0, 0, 4, 4));
                Assert.True(File.Exists(Path.Combine(root, "images", "Save button.png")));
                Assert.SequenceEqual(new[] { "apply", "Save button" }, storage.ListImages());
                PixelImage back = storage.LoadImage("save BUTTON");
                Assert.NotNull(back, "names ignore case");
                Assert.Equal(8, back.Width);
                Assert.SequenceEqual(Button().Pixels, back.Pixels);

                storage.SaveImage("apply", Button());
                Assert.Equal(8, storage.LoadImage("apply").Width, "saving again replaces it");
                storage.DeleteImage("apply");
                Assert.SequenceEqual(new[] { "Save button" }, storage.ListImages());

                File.WriteAllText(Path.Combine(root, "images", "broken.png"), "not a picture");
                Assert.Null(storage.LoadImage("broken"), "a file that isn't a picture");
                bool threw = false;
                try { storage.SaveImage("..\\escape", Button()); } catch (ArgumentException) { threw = true; }
                Assert.True(threw, "a name can't leave the folder");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public static void PictureNamesAreChecked()
        {
            foreach (string good in new[] { "Save button", "a", "OK-2", "step_1", "1st" }) Assert.True(PixelImage.IsValidName(good), good);
            foreach (string bad in new[] { "", " lead", "trail ", "a:b", "a/b", "..", "-dash", "a}b", "$name", new string('x', 41) }) Assert.False(PixelImage.IsValidName(bad), bad);
        }

        // ---------- parsing and writing ----------

        [Test]
        public static void PictureStepsNeedPro()
        {
            WithPro(false, () =>
            {
                foreach (string script in new[] { "{{WAITIMAGE:Save button}}", "{{CLICKIMAGE:Save button}}" })
                    Assert.Contains(MacroParser.Parse(script, false).Errors.Single().Message, "picture steps come with Macro Kit Pro", script);
            });
        }

        [Test]
        public static void PictureStepsParse()
        {
            WithPro(true, () =>
            {
                MacroStep step = MacroParser.Parse("{{WAITIMAGE:Save button}}", false).Steps.Single();
                Assert.Equal(StepKind.WaitImage, step.Kind);
                Assert.Equal("Save button", step.Value);
                Assert.Equal(MacroParser.DefaultWaitImageMs, step.Ms);
                Assert.False(step.SendsInput);
                Assert.Equal("Wait for the picture “Save button” in the window (up to 10 seconds)", step.Describe());

                step = MacroParser.Parse("{{ clickimage : Save button : 2500 }}", false).Steps.Single();
                Assert.Equal(StepKind.ClickImage, step.Kind);
                Assert.Equal("Save button", step.Value);
                Assert.Equal(2500, step.Ms);
                Assert.True(step.SendsInput);
                Assert.Equal("Click the picture “Save button” in the window (waits up to 2.5 seconds)", step.Describe());

                foreach (string bad in new[] { "{{WAITIMAGE}}", "{{WAITIMAGE: }}", "{{CLICKIMAGE:a:1000:2}}" })
                    Assert.Contains(MacroParser.Parse(bad, false).Errors.Single().Message, "needs the name of a saved picture", bad);
                Assert.Contains(MacroParser.Parse("{{WAITIMAGE:a/b}}", false).Errors.Single().Message, "letters, numbers, spaces");
                Assert.Contains(MacroParser.Parse("{{WAITIMAGE:a:50}}", false).Errors.Single().Message, "from 100 to 3,600,000");
                Assert.True(MacroParser.Parse("{{VARSET:t:3000}}{{WAITIMAGE:a:$t}}", false).Ok, "a time from a variable");
            });
        }

        [Test]
        public static void PictureStepsAreMacroKitOnly()
        {
            Assert.SequenceEqual(new[] { "CLICKIMAGE", "WAITIMAGE" }, MacroParser.MacroKitOnlyCommands("{{clickimage:a}}{{ENTER}}{{WAITIMAGE:b}}"));
        }

        [Test]
        public static void PictureStepsAreWrittenAndReadInPlainLines()
        {
            WithPro(true, () =>
            {
                Assert.Equal("{{WAITIMAGE:Save button}}", MacroScript.WaitImage("Save button", MacroParser.DefaultWaitImageMs));
                Assert.Equal("{{CLICKIMAGE:Save button:5000}}", MacroScript.ClickImage(" Save button ", 5000));
                Assert.Equal("wait for picture Save button", PlainMacro.ToPlain("{{WAITIMAGE:Save button}}", false));
                Assert.Equal("click picture Save button 5s", PlainMacro.ToPlain("{{CLICKIMAGE:Save button:5000}}", false));
                Assert.Equal("{{CLICKIMAGE:Save button:5000}}", PlainMacro.ToScript("click picture Save button 5s").Script);
                Assert.Equal("{{WAITIMAGE:Save button}}", PlainMacro.ToScript("Wait for the picture \"Save button\"").Script);
                Assert.Equal("{{CLICKIMAGE:ok}}", PlainMacro.ToScript("click image ok").Script);
                Assert.Equal("{{WAITIMAGE:Step 5s}}", PlainMacro.ToPlain("{{WAITIMAGE:Step 5s}}", false), "a name that reads like a time stays as code");
                Assert.False(PlainMacro.ToScript("click").Script.Contains("IMAGE"), "a plain click is still a click");
            });
        }

        // ---------- running ----------

        [Test]
        public static void WaitImageWaitsUntilThePictureShows()
        {
            WithPro(true, () =>
            {
                var input = new FakeInputSink();
                var windows = new FakeWindowSystem();
                var clock = new FakeClock();
                Excel(windows);
                PixelImage picture = Button();
                var screen = new FakeScreen { PixelAt = ScreenWith(picture, () => 350, () => 200, () => clock.NowMs >= 1000) };
                RunResult result = Run(Runner(input, windows, clock, screen, picture), "{{WAITIMAGE:Save button}}{{MOUSEXY:$IMAGE_X,$IMAGE_Y}}");
                Assert.True(result.Completed, result.Error);
                Assert.True(clock.NowMs >= 1000 && clock.NowMs < 1300, "it waited for the picture: " + clock.NowMs);
                Assert.True(screen.Captures.All(c => c == "300,150 200x120"), "it read the window in front: " + string.Join(" ", screen.Captures.Distinct()));
                Assert.Contains(string.Join("|", input.Log), "moveto 354,203", "$IMAGE_X and $IMAGE_Y hold the middle");
                Assert.False(input.Log.Any(l => l.StartsWith("button", StringComparison.Ordinal)), "waiting doesn't click");
            });
        }

        [Test]
        public static void ClickImageClicksTheMiddleOfThePicture()
        {
            WithPro(true, () =>
            {
                var input = new FakeInputSink();
                var windows = new FakeWindowSystem();
                var clock = new FakeClock();
                WindowInfo excel = Excel(windows);
                clock.At(400, () => { excel.Left = 600; excel.Top = 100; });
                PixelImage picture = Button();
                var screen = new FakeScreen { PixelAt = ScreenWith(picture, () => excel.Left + 20, () => excel.Top + 30, () => true) };
                RunResult result = Run(Runner(input, windows, clock, screen, picture), "{{CLICKIMAGE:save button}}");
                Assert.True(result.Completed, result.Error);
                Assert.SequenceEqual(new[] { "moveto 324,183", "button Left down", "button Left up" }, input.Log);
            });
        }

        [Test]
        public static void PictureStepsStopWithAClearReason()
        {
            WithPro(true, () =>
            {
                var windows = new FakeWindowSystem();
                var clock = new FakeClock();
                Excel(windows);
                PixelImage picture = Button();
                var input = new FakeInputSink();
                RunResult result = Run(Runner(input, windows, clock, new FakeScreen(), picture), "{{CLICKIMAGE:Save button:2000}}{{ENTER}}");
                Assert.False(result.Completed);
                Assert.Contains(result.Error, "the picture “Save button” didn't show in Book1 - Excel within 2 seconds");
                Assert.True(clock.NowMs >= 2000 && clock.NowMs < 2400, clock.NowMs.ToString());
                Assert.Equal(0, input.Log.Count, "nothing was clicked or typed");

                result = Run(Runner(new FakeInputSink(), windows, new FakeClock(), new FakeScreen(), picture), "{{WAITIMAGE:Missing}}");
                Assert.Contains(result.Error, "there is no saved picture called “Missing”");

                MacroRunner noScreen = Runner(new FakeInputSink(), windows, new FakeClock(), null, picture);
                Assert.Contains(Run(noScreen, "{{WAITIMAGE:Save button}}").Error, "can't read the screen");
            });
        }
    }
}
