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
    }
}
