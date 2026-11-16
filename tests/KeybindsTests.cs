using System;
using System.Collections.Generic;
using System.Linq;
using MacroKit.Core;
using MacroKit.Keybinds;

namespace MacroKit.Tests
{
    public static class KeybindsTests
    {
        static Hotkey Keys(string text)
        {
            Hotkey combo;
            Assert.True(KeyText.TryParse(text, out combo), "should parse: " + text);
            return combo;
        }

        [Test]
        public static void KeyTextReadsEveryAppsForm()
        {
            Assert.Equal("Ctrl+Shift+C", Keys("Ctrl+Shift+C").ToString());
            Assert.Equal("Ctrl+Shift+C", Keys("ctrl-shift-c").ToString());
            Assert.Equal("Ctrl+Shift+Alt+U", Keys("ctrl-alt-shift-u").ToString());
            Assert.Equal("Ctrl+Shift+C", Keys("Control + Shift + C").ToString());
            Assert.Equal("Ctrl+Shift+P", Keys("CmdOrCtrl+Shift+P").ToString());
            Assert.Equal("Alt+F4", Keys("alt-f4").ToString());
            Assert.Equal("Ctrl+Shift+Tab", Keys("ctrl-shift-tab").ToString());
            Assert.Equal("Print Screen", Keys("PrtSc").ToString());
            Assert.Equal("Print Screen", Keys("Print Screen").ToString());
            Assert.Equal("Ctrl+Print Screen", Keys("ctrl+prtsc").ToString());
            Assert.Equal("Ctrl+Page Down", Keys("ctrl-pagedown").ToString());
            Assert.Equal("Ctrl+-", Keys("ctrl--").ToString());
            Assert.Equal("Ctrl+=", Keys("Ctrl+Plus").ToString());
            Assert.Equal("Ctrl+Alt+1", Keys("Ctrl+Alt+D1").ToString());
            Assert.Equal("Ctrl+Num 1", Keys("ctrl-num1").ToString());
            Assert.Equal(Keys("Ctrl+Shift+C"), Keys("shift+ctrl+c"));
        }

        [Test]
        public static void KeyTextRejectsNonsense()
        {
            Hotkey combo;
            Assert.False(KeyText.TryParse("", out combo));
            Assert.False(KeyText.TryParse("Ctrl", out combo), "a modifier alone");
            Assert.False(KeyText.TryParse("Ctrl+Shift", out combo));
            Assert.False(KeyText.TryParse("ctrl-banana", out combo));
            Assert.False(KeyText.TryParse("Ctrl++C", out combo));
            Assert.True(combo.IsEmpty);
        }

        [Test]
        public static void NativeEncodings()
        {
            Assert.Equal("Print Screen", KeyText.FromNative(44, 0).ToString());
            Assert.Equal("Ctrl+Print Screen", KeyText.FromNative(44, 2).ToString());
            Assert.Equal("Ctrl+Shift+Alt+F8", KeyText.FromNative(119, 7).ToString());
            Assert.True(KeyText.FromNative(0, 2).IsEmpty);
            Assert.Equal("Ctrl+Shift+S", KeyText.FromWinFormsKeys(0x30053).ToString());
        }

        [Test]
        public static void SniplineReadsCaptureAndWindowShortcut()
        {
            string json = "{\"Schema\":1,\"HotkeyKey\":44,\"HotkeyModifiers\":0,\"WindowShortcutModifier\":2,\"WindowShortcutSaves\":false}";
            AppKeybinds app = KeybindReaders.Snipline(@"C:\x\settings.json", json);
            Assert.Equal(null, app.Problem);
            var globals = app.Keybinds.Where(k => k.Scope == KeybindScope.Global).ToList();
            Assert.Equal(2, globals.Count);
            Assert.Equal("Print Screen", globals[0].Keys);
            Assert.True(globals[0].Saved);
            Assert.Equal("Ctrl+Print Screen", globals[1].Keys);
            Assert.True(app.Keybinds.Any(k => k.Scope == KeybindScope.AppActive && k.Keys == "Ctrl+S"));

            AppKeybinds off = KeybindReaders.Snipline("p", "{\"Schema\":1,\"HotkeyKey\":121,\"HotkeyModifiers\":3,\"WindowShortcutModifier\":0}");
            Assert.Equal(1, off.Keybinds.Count(k => k.Scope == KeybindScope.Global));
            Assert.Equal("Ctrl+Alt+F10", off.Keybinds[0].Keys);

            AppKeybinds missing = KeybindReaders.Snipline("p", null);
            Assert.True(missing.Problem != null);
            Assert.Equal("Print Screen", missing.Keybinds[0].Keys);

            AppKeybinds broken = KeybindReaders.Snipline("p", "{not json");
            Assert.True(broken.Problem != null && broken.Problem.Contains("Could not read"));
            Assert.Equal("Print Screen", broken.Keybinds[0].Keys);
        }

        [Test]
        public static void NoteKeeperReadsFunctionKeysAndSharedModifier()
        {
            AppKeybinds app = KeybindReaders.NoteKeeper("p", "{\"Schema\":1,\"RecordKey\":119,\"CaptureKey\":120,\"MarkKey\":118,\"Modifiers\":0}");
            var keys = app.Keybinds.Where(k => k.Scope == KeybindScope.Global).Select(k => k.Keys).ToList();
            Assert.Equal("F8,F9,F7", string.Join(",", keys));

            AppKeybinds withMods = KeybindReaders.NoteKeeper("p", "{\"Schema\":1,\"RecordKey\":119,\"CaptureKey\":120,\"MarkKey\":0,\"Modifiers\":6}");
            keys = withMods.Keybinds.Where(k => k.Scope == KeybindScope.Global).Select(k => k.Keys).ToList();
            Assert.Equal("Ctrl+Shift+F8,Ctrl+Shift+F9", string.Join(",", keys));
        }

        [Test]
        public static void StudioReadsWorkflowHotkeysAndJancyPath()
        {
            string json = "{\"KeybindPath\":\"D:\\\\j\\\\keybinds.txt\",\"Workflows\":[{\"Id\":\"a\",\"Name\":\"Morning\",\"Hotkey\":\"Ctrl+Alt+1\"},{\"Id\":\"b\",\"Name\":\"Other\",\"Hotkey\":null}]}";
            string jancy;
            AppKeybinds app = KeybindReaders.JancyMacroStudio("p", json, out jancy);
            Assert.Equal(@"D:\j\keybinds.txt", jancy);
            var saved = app.Keybinds.Where(k => k.Saved).ToList();
            Assert.Equal(1, saved.Count);
            Assert.Equal("Ctrl+Alt+1", saved[0].Keys);
            Assert.True(saved[0].Action.Contains("Morning"));
            Assert.Equal(KeybindScope.Global, saved[0].Scope);
            Assert.True(app.Keybinds.Any(k => k.Keys == "Ctrl+Alt+F12" && k.Scope == KeybindScope.Global));
        }

        [Test]
        public static void JancyKeybindsAreInAppOnly()
        {
            string text = "id:user-copy-url-list; keybind:ctrl-alt-shift-u; context:tab window\r\n"
                + "# comment\r\n"
                + "id:copy-title-to-the-clipboard; keybind:ctrl-alt-u; context:tab window\r\n"
                + "id:new-related-tab; keybind:ctrl-shift-c; context:tab\r\n"
                + "garbage line\r\n";
            AppKeybinds app = KeybindReaders.Jancy("p", text);
            Assert.Equal(3, app.Keybinds.Count);
            Assert.True(app.Keybinds.All(k => k.Scope == KeybindScope.AppActive && k.Saved));
            Assert.Equal("Copy URL List", app.Keybinds[0].Action);
            Assert.Equal("Ctrl+Shift+Alt+U", app.Keybinds[0].Keys);
            Assert.Equal("Jancy context: tab", app.Keybinds[2].Note);
            Assert.True(app.Problem != null && app.Problem.StartsWith("1 line"));
        }

        static KeybindCatalog Fixture(Dictionary<string, string> files, string sniplineJson)
        {
            var where = new KeybindLocations { Snipline = "snip", NoteKeeper = "nk", JancyDefault = "jancy-default" };
            where.StudioCandidates.Add("studio-a");
            where.StudioCandidates.Add("studio-b");
            if (sniplineJson != null) files["snip"] = sniplineJson;
            var settings = new AppSettings();
            var macros = new List<MacroDef> { new MacroDef { Name = "Paste row", Hotkey = "Ctrl+Alt+1" }, new MacroDef { Name = "No key", Hotkey = "" } };
            return KeybindCatalog.Read(where, settings, macros, path => { string text; return files.TryGetValue(path, out text) ? text : null; });
        }

        [Test]
        public static void CatalogFindsFilesAndClashes()
        {
            var files = new Dictionary<string, string>
            {
                { "nk", "{\"Schema\":1,\"RecordKey\":119,\"CaptureKey\":120,\"MarkKey\":118,\"Modifiers\":0}" },
                { "studio-b", "{\"Workflows\":[{\"Name\":\"Morning\",\"Hotkey\":\"Ctrl+Alt+1\"}]}" },
                { "jancy-default", "id:copy; keybind:ctrl-alt-shift-r; context:tab window\nid:x; keybind:f8; context:tab window\n" },
            };
            KeybindCatalog catalog = Fixture(files, "{\"Schema\":1,\"HotkeyKey\":44,\"HotkeyModifiers\":0,\"WindowShortcutModifier\":0}");
            Assert.Equal("studio-b", catalog.Apps.First(a => a.App == KeybindReaders.StudioName).Source, "the first studio.json that exists wins");
            Assert.Equal("jancy-default", catalog.Apps.First(a => a.App == KeybindReaders.JancyName).Source);
            Assert.True(catalog.All.Any(k => k.App == KeybindReaders.MacroKitName && k.Keys == "Ctrl+Alt+1"));
            Assert.False(catalog.All.Any(k => k.Action.Contains("No key")));

            List<KeybindClash> clashes = catalog.Clashes();
            // Macro Kit's macro and the studio workflow both want Ctrl+Alt+1 everywhere.
            Assert.True(clashes.Any(x => x.BothGlobal && x.A.Combo == Keys("Ctrl+Alt+1")));
            // Macro Kit's Record hotkey (Ctrl+Shift+Alt+R) takes the keys before Jancy's in-app "copy".
            KeybindClash record = clashes.First(x => x.A.Combo == Keys("Ctrl+Shift+Alt+R"));
            Assert.False(record.BothGlobal);
            Assert.Equal(KeybindReaders.MacroKitName, record.A.App);
            Assert.Equal(KeybindReaders.JancyName, record.B.App);
            Keybind jancyCopy = catalog.All.First(k => k.App == KeybindReaders.JancyName && k.Action == "Copy");
            Assert.True(catalog.ClashNote(jancyCopy).Contains("may never reach Jancy"));
            // In-app shortcuts of different apps never clash (Ctrl+S in Snipline's editor and in Jancy Macro Studio).
            Assert.False(clashes.Any(x => x.A.Combo == Keys("Ctrl+S")));
        }

        [Test]
        public static void CatalogSurvivesLockedAndMissingFiles()
        {
            var where = new KeybindLocations { Snipline = "snip", NoteKeeper = "nk", JancyDefault = "j" };
            where.StudioCandidates.Add("s");
            KeybindCatalog catalog = KeybindCatalog.Read(where, new AppSettings(), new List<MacroDef>(), path =>
            {
                if (path == "snip") throw new System.IO.IOException("in use");
                return null;
            });
            AppKeybinds snip = catalog.Apps.First(a => a.App == KeybindReaders.SniplineName);
            Assert.True(snip.Problem.Contains("in use"));
            Assert.True(snip.Keybinds.Count > 0, "defaults still listed");
            Assert.True(catalog.Apps.First(a => a.App == KeybindReaders.JancyName).Problem.Contains("not found"));
        }

        [Test]
        public static void VerdictsSayWhetherKeysAreFree()
        {
            var files = new Dictionary<string, string> { { "jancy-default", "id:copy-x; keybind:ctrl-shift-c; context:tab window\n" } };
            KeybindCatalog catalog = Fixture(files, "{\"Schema\":1,\"HotkeyKey\":44,\"HotkeyModifiers\":0,\"WindowShortcutModifier\":2}");

            Hotkey prtsc = Keys("PrtSc");
            Assert.Equal(ComboVerdict.TakenGlobally, ComboCheck.Judge(catalog.Users(prtsc), true));
            Assert.True(ComboCheck.Explain(prtsc, catalog.Users(prtsc), null).Contains("Snipline"));

            Hotkey jancy = Keys("Ctrl+Shift+C");
            Assert.Equal(ComboVerdict.InAppOnly, ComboCheck.Judge(catalog.Users(jancy), true));
            Assert.True(ComboCheck.Explain(jancy, catalog.Users(jancy), true).Contains("only works inside Jancy"));

            Hotkey free = Keys("Ctrl+Alt+Shift+K");
            Assert.Equal(ComboVerdict.Free, ComboCheck.Judge(catalog.Users(free), true));
            Assert.Equal(ComboVerdict.Free, ComboCheck.Judge(catalog.Users(free), null));
            Assert.Equal(ComboVerdict.TakenByUnknown, ComboCheck.Judge(catalog.Users(free), false));
            Assert.Equal(ComboVerdict.InAppOnly, ComboCheck.Judge(catalog.Users(jancy), null));
        }

        [Test]
        public static void SwitchedOffShortcutsAreNotTaken()
        {
            var settings = new AppSettings { EscStops = false };
            AppKeybinds app = KeybindReaders.MacroKit(settings, new List<MacroDef>());
            Keybind esc = app.Keybinds.First(k => k.Keys == "Esc");
            Assert.False(esc.Enabled);
            Assert.False(esc.Matches(Keys("Esc")));
        }
    }
}
