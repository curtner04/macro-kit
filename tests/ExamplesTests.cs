using System;
using System.Collections.Generic;
using System.Linq;

namespace MacroKit.Tests
{
    // Inside the namespace so "Recording" is not taken by the MacroKit.Recording namespace.
    using MacroKit.Core;
    using MacroKit.Keybinds;
    using MacroKit.Macros;
    using MacroKit.Recording;
    using Rec = MacroKit.Core.Recording;

    /// <summary>1.7.0: the example phrases and macros, keys searched in any order, and the recorder bar's Restart.</summary>
    public static class ExamplesTests
    {
        [Test]
        public static void ThereAreTenExamplePhrasesAndThirteenExampleMacros()
        {
            List<PhraseDef> phrases = Examples.Phrases();
            List<MacroDef> macros = Examples.Macros();
            Assert.Equal(10, phrases.Count);
            Assert.Equal(13, macros.Count);
            Assert.Equal(3, macros.Count(m => Examples.IsProSample(m.Name)));
            Assert.Equal(10, phrases.Select(p => p.Trigger).Distinct().Count());
            Assert.Equal(13, macros.Select(m => m.Name).Distinct().Count());
            Assert.True(phrases.All(p => p.Enabled && p.Group == Examples.PhraseGroup));
            LicenseInfo before = Edition.License;
            Edition.Set(new LicenseInfo { Serial = 1, Issued = new DateTime(2026, 11, 17), Name = "Test" });
            try
            {
                foreach (MacroDef m in macros)
                {
                    ParseResult r = MacroParser.Parse(m.Script, m.NewLineIsEnter);
                    Assert.True(r.Ok, m.Name + ": " + string.Join("; ", r.Errors.Select(e => e.Message)));
                }
            }
            finally { Edition.Set(before); }
            // Without Pro, only the two condition samples need fixing, and the scheduled one never runs by itself.
            Edition.Set(null);
            try
            {
                foreach (MacroDef m in macros)
                    Assert.Equal(!Examples.IsProSample(m.Name) || m.Schedule.Kind != ScheduleKind.Off, MacroParser.Parse(m.Script, m.NewLineIsEnter).Ok);
            }
            finally { Edition.Set(before); }
            MacroDef reminder = macros.Single(m => m.Schedule.Kind != ScheduleKind.Off);
            Assert.False(reminder.Enabled, "the scheduled sample is switched off");
            Assert.Equal(1, macros.Count(m => m.ShowOnDeck));
        }

        [Test]
        public static void EveryExampleMacroSaysWhatItDoes()
        {
            foreach (MacroDef m in Examples.Macros())
                Assert.True(Examples.Describe(m.Name).Length > 10, m.Name + " has no description");
            string paste = Examples.Describe("Paste down 10 rows");
            Assert.True(paste.Contains("Copies") && !paste.Contains("what you copied"), "paste note: " + paste);
            Assert.Equal("", Examples.Describe("A macro of my own"));
            Assert.Equal("", Examples.Describe(null));
        }

        [Test]
        public static void NewInstallsStartWithTheExamples()
        {
            Assert.Equal(10, Storage.SeedPhrases().Count);
            Assert.Equal("[SAMPLE] Copy, switch, paste, next row", Storage.SeedMacros()[0].Name);
        }

        [Test]
        public static void ExamplesAreAddedOnceWithoutDuplicates()
        {
            var phrases = new List<PhraseDef> { new PhraseDef { Trigger = ";sig", Replacement = "Mine" } };
            var macros = new List<MacroDef> { new MacroDef { Name = "Duplicate the line", Script = "{{F2}}", ShowOnDeck = true } };
            Assert.Equal(21, Examples.AddMissing(phrases, macros));
            Assert.Equal(10, phrases.Count);
            Assert.Equal(13, macros.Count);
            Assert.Equal("Mine", phrases.First(p => p.Trigger == ";sig").Replacement);
            Assert.True(macros.Skip(1).All(m => !m.ShowOnDeck), "examples added to a setup never land on its Deck");
            Assert.Equal(0, Examples.AddMissing(phrases, macros));
        }

        [Test]
        public static void SamplesAreMarkedAndOlderOnesRenamed()
        {
            Assert.True(Examples.Macros().All(m => m.Name.StartsWith("[SAMPLE] ")), "every example macro starts with [SAMPLE]");
            Assert.True(Examples.Phrases().All(Examples.IsSample));
            Assert.False(Examples.IsSample(new PhraseDef { Trigger = ";sig", Replacement = "Mine" }));
            Assert.Equal(Examples.Describe("Paste down 10 rows"), Examples.Describe("[SAMPLE] Paste down 10 rows"));

            // An older setup: one sample as shipped, one changed, one of the person's own with a sample's name.
            MacroDef shipped = Examples.Macros()[6];
            var macros = new List<MacroDef>
            {
                new MacroDef { Name = "Duplicate the line", Script = shipped.Script, Hotkey = "Ctrl+Alt+D" },
                new MacroDef { Name = "Snip part of the screen", Script = "{{PRINTSCREEN}}" },
                new MacroDef { Name = "Mine", Script = "{{F2}}" },
            };
            var renamed = Examples.MarkSamples(macros);
            Assert.Equal(1, renamed.Count);
            Assert.Equal("Duplicate the line", renamed[0].Key);
            Assert.Equal("[SAMPLE] Duplicate the line", macros[0].Name);
            Assert.Equal("Ctrl+Alt+D", macros[0].Hotkey);
            Assert.Equal("Snip part of the screen", macros[1].Name);
            Assert.Equal(0, Examples.MarkSamples(macros).Count);
            // The old names count as there, so adding the missing ones adds no second copy.
            int before = macros.Count;
            Examples.AddMissing(new List<PhraseDef>(), macros);
            Assert.Equal(before + 11, macros.Count);
            Assert.False(macros.Any(m => m.Name == "[SAMPLE] Snip part of the screen"));
        }

        [Test]
        public static void ExamplesAreNotAddedBackWhenSettingsAreLost()
        {
            using (var dir = new TempDir())
            {
                try
                {
                    string data = dir.File("data");
                    Paths.Override(data);   // keeps the log out of the real folder
                    // A cleaned-up library: one example deleted, then settings.json is lost (or set aside).
                    List<PhraseDef> mine = Examples.Phrases().Where(p => p.Trigger != ";em").ToList();
                    new Storage(data).SavePhrases(mine);
                    var lost = new Storage(data);
                    AppSettings settings = lost.LoadSettings();
                    Assert.False(lost.SettingsFromFile);
                    Assert.False(Examples.ShouldAddMissing(settings, lost.SettingsFromFile), ";em does not come back");

                    var upgraded = new Storage(data);   // settings.json now exists, without ExamplesAdded: an older version's file
                    AppSettings older = upgraded.LoadSettings();
                    Assert.True(upgraded.SettingsFromFile);
                    Assert.True(Examples.ShouldAddMissing(older, upgraded.SettingsFromFile), "an older setup still gets them once");
                    // ...but not while phrases.json or macros.json is in use: they could not be saved, so a later start adds them.
                    Assert.False(Examples.ShouldAddMissing(older, true, new[] { "phrases.json" }));
                    Assert.False(Examples.ShouldAddMissing(older, true, new[] { "MACROS.json" }));
                    Assert.True(Examples.ShouldAddMissing(older, true, new string[0]));
                    Assert.True(Examples.ShouldAddMissing(older, true, new[] { "keybinds.json" }));
                    older.ExamplesAdded = true;
                    Assert.False(Examples.ShouldAddMissing(older, true));
                }
                finally { Paths.Override(null); }
            }
        }

        [Test]
        public static void OnlyUnchangedExamplesCountAsExamples()
        {
            PhraseDef p = Examples.Phrases()[0];
            MacroDef m = Examples.Macros()[1];
            Assert.True(Examples.IsUnchanged(p));
            Assert.True(Examples.IsUnchanged(m));
            p.Replacement = "Cheers, Sam";
            m.Script += "{{ENTER}}";
            Assert.False(Examples.IsUnchanged(p));
            Assert.False(Examples.IsUnchanged(m));
            MacroDef withHotkey = Examples.Macros()[2];
            withHotkey.Hotkey = "Ctrl+Alt+9";
            Assert.False(Examples.IsUnchanged(withHotkey), "a macro given a hotkey is the person's own now");
            Assert.False(Examples.IsUnchanged(new PhraseDef { Trigger = ";sig", Replacement = p.Replacement }));
        }

        static bool Search(string query, string shortcut)
        {
            HotkeyModifiers mods;
            ushort key;
            Hotkey combo;
            Assert.True(KeyText.TryParse(shortcut, out combo), shortcut);
            return KeyText.TryParseLoose(query, out mods, out key) && KeyText.HasKeys(combo, mods, key);
        }

        [Test]
        public static void KeySearchTakesKeysInAnyOrder()
        {
            Assert.True(Search("ctrl alt s", "Ctrl+Alt+S"));
            Assert.True(Search("s alt ctrl", "Ctrl+Alt+S"));
            Assert.True(Search("ctrl alt s", "Ctrl+Shift+Alt+S"), "uses all three, plus Shift");
            Assert.False(Search("ctrl alt s", "Ctrl+S"));
            Assert.False(Search("ctrl alt s", "Ctrl+Alt+D"));
            Assert.True(Search("alt ctrl shift", "Ctrl+Shift+Alt+R"));
            Assert.True(Search("alt ctrl shift", "Ctrl+Shift+Alt+F5"));
            Assert.False(Search("alt ctrl shift", "Ctrl+Alt+R"));
            Assert.True(Search("Shift+Ctrl", "Ctrl+Shift+C"));
            Assert.True(Search("ctrl page up", "Ctrl+Page Up"));
            Assert.True(Search("win v", "Win+V"));
            Assert.True(Search("f5", "F5"));
        }

        [Test]
        public static void KeySearchLeavesWordsToTheTextSearch()
        {
            HotkeyModifiers mods;
            ushort key;
            Assert.False(KeyText.TryParseLoose("copy", out mods, out key));
            Assert.False(KeyText.TryParseLoose("s", out mods, out key), "one letter is a word search");
            Assert.False(KeyText.TryParseLoose("ctrl a b", out mods, out key), "two keys are not one shortcut");
            Assert.False(KeyText.TryParseLoose("ctrl banana", out mods, out key));
            Assert.False(KeyText.TryParseLoose("", out mods, out key));
            Assert.True(KeyText.TryParseLoose("ctrl", out mods, out key));
            Assert.Equal(HotkeyModifiers.Ctrl, mods);
            Assert.Equal((ushort)0, key);
        }

        [Test]
        public static void RecorderStartsCleanAfterARestart()
        {
            var clock = new FakeClock { NowMs = 5000 };
            var r = new Recorder(clock);
            r.Start(true);
            clock.NowMs += 100;
            r.OnKey(new KeyEvent { Vk = 0x41, Down = true });
            r.OnKey(new KeyEvent { Vk = 0x41, Down = false });
            r.Stop(new Hotkey[0]);
            clock.NowMs += 2000;
            r.Start(true);
            clock.NowMs += 300;
            r.OnKey(new KeyEvent { Vk = 0x42, Down = true });
            r.OnKey(new KeyEvent { Vk = 0x42, Down = false });
            Rec rec = r.Stop(new Hotkey[0]);
            Assert.Equal(2, rec.Events.Count);
            Assert.Equal(300, rec.Events[0].T);
        }
    }
}
