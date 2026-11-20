using System;
using System.Collections.Generic;
using System.Linq;

namespace MacroKit.Tests
{
    using MacroKit.Core;
    using MacroKit.Macros;
    using Rec = MacroKit.Core.Recording;

    /// <summary>1.7.0: Convert to macro turns a recording into steps you can read and edit.</summary>
    public static class RecordingToMacroTests
    {
        static RecEvent E(int t, RecKind kind, int a, int b = 0) { return new RecEvent(t, kind, a, b); }

        static List<RecEvent> Sample()
        {
            return new List<RecEvent>
            {
                E(0, RecKind.Move, 100, 200), E(0, RecKind.ButtonDown, (int)MouseButton.Left), E(80, RecKind.ButtonUp, (int)MouseButton.Left),
                E(1000, RecKind.KeyDown, 0xA0), E(1010, RecKind.KeyDown, 'H'), E(1015, RecKind.KeyUp, 'H'), E(1020, RecKind.KeyUp, 0xA0),
                E(1100, RecKind.KeyDown, 'I'), E(1110, RecKind.KeyUp, 'I'),
                E(1300, RecKind.KeyDown, 0x0D), E(1310, RecKind.KeyUp, 0x0D),
                E(1500, RecKind.KeyDown, 0xA2), E(1510, RecKind.KeyDown, 'S'), E(1520, RecKind.KeyUp, 'S'), E(1530, RecKind.KeyUp, 0xA2),
                E(2000, RecKind.Wheel, -240), E(2010, RecKind.Wheel, -120),
                E(3000, RecKind.Move, 300, 300), E(3000, RecKind.ButtonDown, (int)MouseButton.Left), E(3200, RecKind.Move, 400, 300), E(3300, RecKind.ButtonUp, (int)MouseButton.Left),
                E(4000, RecKind.Move, 50, 50), E(4000, RecKind.ButtonDown, (int)MouseButton.Left), E(4050, RecKind.ButtonUp, (int)MouseButton.Left),
                E(4150, RecKind.ButtonDown, (int)MouseButton.Left), E(4200, RecKind.ButtonUp, (int)MouseButton.Left),
            };
        }

        [Test]
        public static void ARecordingBecomesClicksTextKeysScrollsAndPauses()
        {
            string script = RecordingToMacro.Script(Sample());
            string expected = string.Join("\n", new[]
            {
                "{{MOUSEXY:100,200}}", "{{LBUTTON}}",
                "{{PAUSE:1010}}", "Hi",
                "{{PAUSE:200}}", "{{ENTER}}",
                "{{PAUSE:210}}", "{{CTRL}{S}}",
                "{{PAUSE:490}}", "{{MSCROLLDOWN:3}}",
                "{{PAUSE:1000}}", "{{MOUSEXY:300,300}}", "{{MLEFTDOWN}}", "{{PAUSE:300}}", "{{MOUSEXY:400,300}}", "{{MLEFTUP}}",
                "{{PAUSE:700}}", "{{MOUSEXY:50,50}}", "{{MLEFTDBLCLICK}}",
            });
            Assert.Equal(expected, script);
        }

        [Test]
        public static void TheConvertedMacroParsesAndTypesTheSameText()
        {
            var rec = new Rec { Name = "Save the form" };
            rec.Events.AddRange(Sample());
            MacroDef m = RecordingToMacro.Convert(rec);
            Assert.Equal("Save the form (from recording)", m.Name);
            Assert.False(m.NewLineIsEnter);
            Assert.False(m.ShowOnDeck);
            ParseResult r = MacroParser.Parse(m.Script, m.NewLineIsEnter);
            Assert.True(r.Ok, string.Join("; ", r.Errors.Select(e => e.Message)));
            // The line breaks between steps are not typed: the only Enter is the one that was pressed.
            Assert.Equal(1, r.Steps.Count(s => s.Kind == StepKind.Text && s.Text == "Hi"));
        }

        [Test]
        public static void BracesCapsLockAndALoneWinKeyStillParse()
        {
            var events = new List<RecEvent>
            {
                E(0, RecKind.KeyDown, 0x14), E(5, RecKind.KeyUp, 0x14),
                E(10, RecKind.KeyDown, 'A'), E(20, RecKind.KeyDown, 'B'),
                E(30, RecKind.KeyDown, 0xA1), E(40, RecKind.KeyDown, 0xDB), E(50, RecKind.KeyUp, 0xA1),   // Shift+[ is {
                E(100, RecKind.KeyDown, 0x5B), E(150, RecKind.KeyUp, 0x5B),                               // Win on its own
                E(200, RecKind.ButtonDown, (int)MouseButton.X1), E(210, RecKind.ButtonUp, (int)MouseButton.X1),
            };
            string script = RecordingToMacro.Script(events);
            Assert.True(script.StartsWith("AB\n", StringComparison.Ordinal), script);
            Assert.True(script.Contains("{{" + KeyNames.MacroToken(0x5B) + "}}"), script);
            ParseResult r = MacroParser.Parse(script, false);
            Assert.True(r.Ok, script + " → " + string.Join("; ", r.Errors.Select(e => e.Message)));
        }

        [Test]
        public static void AnEmptyRecordingGivesAnEmptyScript()
        {
            Assert.Equal("", RecordingToMacro.Script(new List<RecEvent>()));
            Assert.Equal("Recording (from recording)", RecordingToMacro.Convert(new Rec { Name = " " }).Name);
        }

        // ---- Sweep A1: more of the conversion ----

        static string Script(params RecEvent[] events)
        {
            string script = RecordingToMacro.Script(events);
            ParseResult r = MacroParser.Parse(script, false);
            Assert.True(r.Ok, script + " -> " + string.Join("; ", r.Errors.Select(e => e.Message)));
            return script;
        }

        static RecEvent Down(int t, MouseButton b) { return E(t, RecKind.ButtonDown, (int)b); }
        static RecEvent Up(int t, MouseButton b) { return E(t, RecKind.ButtonUp, (int)b); }

        [Test]
        public static void ADragIsDownMoveUp()
        {
            string s = Script(E(0, RecKind.Move, 10, 10), Down(0, MouseButton.Left), E(100, RecKind.Move, 200, 150), Up(200, MouseButton.Left));
            Assert.Equal("{{MOUSEXY:10,10}}\n{{MLEFTDOWN}}\n{{PAUSE:200}}\n{{MOUSEXY:200,150}}\n{{MLEFTUP}}", s);
        }

        [Test]
        public static void ClicksCloseTogetherAreADoubleClickAndSlowOnesAreTwo()
        {
            string fast = Script(E(0, RecKind.Move, 5, 5), Down(0, MouseButton.Left), Up(30, MouseButton.Left), Down(200, MouseButton.Left), Up(230, MouseButton.Left));
            Assert.Equal("{{MOUSEXY:5,5}}\n{{MLEFTDBLCLICK}}", fast);
            string slow = Script(E(0, RecKind.Move, 5, 5), Down(0, MouseButton.Left), Up(30, MouseButton.Left), Down(1500, MouseButton.Left), Up(1530, MouseButton.Left));
            Assert.Equal("{{MOUSEXY:5,5}}\n{{LBUTTON}}\n{{PAUSE:1500}}\n{{LBUTTON}}", slow);
        }

        [Test]
        public static void SpeedTwoHalvesThePauses()
        {
            var events = new[] { E(0, RecKind.Move, 5, 5), Down(0, MouseButton.Left), Up(10, MouseButton.Left), Down(2000, MouseButton.Right), Up(2010, MouseButton.Right) };
            Assert.Contains(RecordingToMacro.Script(events, 1), "{{PAUSE:2000}}");
            Assert.Contains(RecordingToMacro.Script(events, 2), "{{PAUSE:1000}}");
        }

        [Test]
        public static void HeldCtrlOverThreeKeysAndAnOrphanKeyUp()
        {
            string s = Script(E(0, RecKind.KeyUp, 'Z'), E(10, RecKind.KeyDown, 0xA2), E(20, RecKind.KeyDown, 'A'), E(30, RecKind.KeyDown, 'C'), E(40, RecKind.KeyDown, 'V'), E(50, RecKind.KeyUp, 0xA2));
            Assert.Equal("{{CTRL}{A}}\n{{CTRL}{C}}\n{{CTRL}{V}}", s);
        }

        [Test]
        public static void ModifiersHeldDuringClicksAndScrollsAreKept()
        {
            string click = Script(E(0, RecKind.Move, 50, 60), E(5, RecKind.KeyDown, 0xA2), Down(20, MouseButton.Left), Up(40, MouseButton.Left), E(60, RecKind.KeyUp, 0xA2));
            Assert.Equal("{{MOUSEXY:50,60}}\n{{KEYDOWN:CTRL}}\n{{LBUTTON}}\n{{KEYUP:CTRL}}", click);
            string wheel = Script(E(0, RecKind.KeyDown, 0xA0), E(10, RecKind.Wheel, 120), E(20, RecKind.KeyUp, 0xA0));
            Assert.Equal("{{KEYDOWN:SHIFT}}\n{{MSCROLLUP}}\n{{KEYUP:SHIFT}}", wheel);
            string still = Script(E(0, RecKind.KeyDown, 0xA2), E(10, RecKind.Wheel, -120));
            Assert.Equal("{{KEYDOWN:CTRL}}\n{{MSCROLLDOWN}}\n{{KEYUP:CTRL}}", still, "a modifier still held at the end is let go");
            string keyThenClick = Script(E(0, RecKind.KeyDown, 0xA2), Down(10, MouseButton.Left), Up(20, MouseButton.Left), E(30, RecKind.KeyDown, 'C'), E(40, RecKind.KeyUp, 'C'), E(50, RecKind.KeyUp, 0xA2));
            Assert.Equal("{{KEYDOWN:CTRL}}\n{{LBUTTON}}\n{{KEYUP:CTRL}}\n{{CTRL}{C}}", keyThenClick);
        }

        [Test]
        public static void ModifierLetGoBeforeTheButtonStaysHeldThroughTheClick()
        {
            string drag = Script(E(0, RecKind.KeyDown, 0xA2), E(5, RecKind.Move, 100, 100), Down(10, MouseButton.Left), E(20, RecKind.Move, 200, 200), E(30, RecKind.KeyUp, 0xA2), Up(40, MouseButton.Left));
            Assert.True(drag.IndexOf("{{KEYDOWN:CTRL}}", StringComparison.Ordinal) < drag.IndexOf("{{MLEFTDOWN}}", StringComparison.Ordinal), drag);
            Assert.True(drag.IndexOf("{{KEYUP:CTRL}}", StringComparison.Ordinal) > drag.IndexOf("{{MLEFTUP}}", StringComparison.Ordinal), drag);
            string click = Script(E(0, RecKind.KeyDown, 0xA2), E(5, RecKind.Move, 100, 100), Down(10, MouseButton.Left), E(30, RecKind.KeyUp, 0xA2), Up(40, MouseButton.Left));
            Assert.Equal("{{MOUSEXY:100,100}}\n{{KEYDOWN:CTRL}}\n{{LBUTTON}}\n{{KEYUP:CTRL}}", click);
        }

        [Test]
        public static void ModifierPressedAgainDuringADragStaysHeld()
        {
            string s = Script(E(0, RecKind.Move, 0, 0), E(5, RecKind.KeyDown, 0x5B), Down(10, MouseButton.Left), E(20, RecKind.Move, 100, 100),
                E(30, RecKind.KeyUp, 0x5B), E(40, RecKind.KeyDown, 0x5B), Up(50, MouseButton.Left), E(60, RecKind.KeyUp, 0x5B));
            Assert.False(s.Contains("{{WIN}}"), s);
            int up = s.IndexOf("{{KEYUP:WIN}}", StringComparison.Ordinal);
            Assert.True(up > s.IndexOf("{{MLEFTUP}}", StringComparison.Ordinal), s);
            Assert.Equal(-1, s.IndexOf("{{KEYUP:WIN}}", up + 1, StringComparison.Ordinal), s);
        }

        [Test]
        public static void BackAndForwardButtonsAreKept()
        {
            string s = Script(E(0, RecKind.Move, 5, 5), Down(0, MouseButton.X1), Up(10, MouseButton.X1), Down(500, MouseButton.X2), Up(510, MouseButton.X2));
            Assert.Contains(s, "{{XBUTTON1}}");
            Assert.Contains(s, "{{XBUTTON2}}");
        }

        [Test]
        public static void AGapLongerThanAnHourIsSeveralPauses()
        {
            string s = Script(E(0, RecKind.KeyDown, 0x0D), E(9000000, RecKind.KeyDown, 0x0D));
            Assert.Equal("{{ENTER}}\n{{PAUSE:3600000}}\n{{PAUSE:3600000}}\n{{PAUSE:1800000}}\n{{ENTER}}", s);
        }
    }
}
