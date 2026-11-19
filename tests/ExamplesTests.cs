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
        public static void ThereAreTenExamplePhrasesAndTenExampleMacros()
        {
            List<PhraseDef> phrases = Examples.Phrases();
            List<MacroDef> macros = Examples.Macros();
            Assert.Equal(10, phrases.Count);
            Assert.Equal(10, macros.Count);
            Assert.Equal(10, phrases.Select(p => p.Trigger).Distinct().Count());
            Assert.Equal(10, macros.Select(m => m.Name).Distinct().Count());
            Assert.True(phrases.All(p => p.Enabled && p.Group == Examples.PhraseGroup));
            foreach (MacroDef m in macros)
            {
                ParseResult r = MacroParser.Parse(m.Script, m.NewLineIsEnter);
                Assert.True(r.Ok, m.Name + ": " + string.Join("; ", r.Errors.Select(e => e.Message)));
            }
            Assert.Equal(1, macros.Count(m => m.ShowOnDeck));
        }

        [Test]
        public static void NewInstallsStartWithTheExamples()
        {
            Assert.Equal(10, Storage.SeedPhrases().Count);
            Assert.Equal("Copy, switch, paste, next row", Storage.SeedMacros()[0].Name);
        }

        [Test]
        public static void ExamplesAreAddedOnceWithoutDuplicates()
        {
            var phrases = new List<PhraseDef> { new PhraseDef { Trigger = ";sig", Replacement = "Mine" } };
            var macros = new List<MacroDef> { new MacroDef { Name = "Duplicate the line", Script = "{{F2}}", ShowOnDeck = true } };
            Assert.Equal(18, Examples.AddMissing(phrases, macros));
            Assert.Equal(10, phrases.Count);
            Assert.Equal(10, macros.Count);
            Assert.Equal("Mine", phrases.First(p => p.Trigger == ";sig").Replacement);
            Assert.True(macros.Skip(1).All(m => !m.ShowOnDeck), "examples added to a setup never land on its Deck");
            Assert.Equal(0, Examples.AddMissing(phrases, macros));
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
