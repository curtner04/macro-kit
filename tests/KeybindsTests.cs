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
            if (sniplineJson != null) files["snip"] = sniplineJson;
            var settings = new AppSettings();
            var macros = new List<MacroDef> { new MacroDef { Name = "Paste row", Hotkey = "F8" }, new MacroDef { Name = "No key", Hotkey = "" } };
            return KeybindCatalog.Read(where, settings, macros, null, path => { string text; return files.TryGetValue(path, out text) ? text : null; });
        }

        [Test]
        public static void CatalogFindsFilesAndClashes()
        {
            var files = new Dictionary<string, string>
            {
                { "nk", "{\"Schema\":1,\"RecordKey\":119,\"CaptureKey\":120,\"MarkKey\":118,\"Modifiers\":0}" },
                { "jancy-default", "id:copy; keybind:ctrl-alt-shift-r; context:tab window\nid:x; keybind:ctrl-s; context:tab window\n" },
            };
            KeybindCatalog catalog = Fixture(files, "{\"Schema\":1,\"HotkeyKey\":44,\"HotkeyModifiers\":0,\"WindowShortcutModifier\":0}");
            Assert.Equal("jancy-default", catalog.Apps.First(a => a.App == KeybindReaders.JancyName).Source);
            Assert.True(catalog.All.Any(k => k.App == KeybindReaders.MacroKitName && k.Keys == "F8"));
            Assert.False(catalog.All.Any(k => k.Action.Contains("No key")));

            List<KeybindClash> clashes = catalog.Clashes();
            // Macro Kit and Note Keeper both register F8.
            Assert.True(clashes.Any(x => x.BothGlobal && x.A.Combo == Keys("F8")));
            // Macro Kit's Record hotkey (Ctrl+Shift+Alt+R) takes the keys before Jancy's in-app "copy".
            KeybindClash record = clashes.First(x => x.A.Combo == Keys("Ctrl+Shift+Alt+R"));
            Assert.False(record.BothGlobal);
            Assert.Equal(KeybindReaders.MacroKitName, record.A.App);
            Assert.Equal(KeybindReaders.JancyName, record.B.App);
            Keybind jancyCopy = catalog.All.First(k => k.App == KeybindReaders.JancyName && k.Action == "Copy");
            Assert.True(catalog.ClashNote(jancyCopy).Contains("may never reach Jancy"));
            // In-app shortcuts of different apps never clash (Ctrl+S in Snipline's editor and in Jancy).
            Assert.False(clashes.Any(x => x.A.Combo == Keys("Ctrl+S")));
        }

        [Test]
        public static void CatalogSurvivesLockedAndMissingFiles()
        {
            var where = new KeybindLocations { Snipline = "snip", NoteKeeper = "nk", JancyDefault = "j" };
            KeybindCatalog catalog = KeybindCatalog.Read(where, new AppSettings(), new List<MacroDef>(), null, path =>
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
        public static void WindowsOwnShortcutsAreListedOnEveryPc()
        {
            KeybindCatalog catalog = Fixture(new Dictionary<string, string>(), null);
            AppKeybinds windows = catalog.Apps.Single(a => a.App == KeybindReaders.WindowsName);
            Assert.True(windows.Keybinds.Count >= 30, "Windows shortcuts");
            Assert.True(windows.Keybinds.All(k => k.Scope == KeybindScope.Global && !k.Saved), "global and built in");
            Assert.True(windows.Keybinds.All(k => !k.Combo.IsEmpty), "every one is understood: " + string.Join(", ", windows.Keybinds.Where(k => k.Combo.IsEmpty).Select(k => k.Keys)));
            Hotkey winV;
            Assert.True(KeyText.TryParse("Win+V", out winV));
            Assert.Equal(ComboVerdict.TakenGlobally, ComboCheck.Judge(catalog.Users(winV), true));
            Assert.Equal(windows.Keybinds.Count, windows.Keybinds.Select(k => k.Combo).Distinct().Count(), "no key twice");
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

        // ---------------------------------------------------------------- keys typed in by hand are stored as display text

        [Test]
        public static void EveryKeyReadsBackFromItsDisplayText()
        {
            var failures = new List<string>();
            for (int vk = 1; vk <= 0xFE; vk++)
            {
                if (KeyNames.IsModifier((ushort)vk)) continue;
                var combo = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, (ushort)vk);
                string text = combo.ToString();
                Hotkey back;
                if (!KeyText.TryParse(text, out back) || back != combo) failures.Add(vk.ToString("X2") + " " + text);
            }
            // Keys Windows has no name for (unassigned codes) may not read back; every key on a keyboard must.
            foreach (string must in new[] { "Ctrl+Shift+A", "Ctrl+Shift+1", "Ctrl+Shift+F9", "Ctrl+Shift+Num 1", "Ctrl+Shift+Print Screen", "Ctrl+Shift+-", "Ctrl+Shift+=", "Ctrl+Shift+;", "Ctrl+Shift+Page Down", "Ctrl+Shift+Space" })
                Assert.False(failures.Any(f => f.EndsWith(" " + must)), "should read back: " + must);
            Hotkey numpad;
            Assert.True(KeyText.TryParse("Ctrl+Shift+Num 1", out numpad) && numpad.Vk == 0x61);
        }

        // ---------------------------------------------------------------- PowerToys

        static Func<string, string> Files(Dictionary<string, string> files)
        {
            return path => { string text; return files.TryGetValue(path, out text) ? text : null; };
        }

        static string Shortcut(bool win, bool ctrl, bool alt, bool shift, int code)
        {
            return "{\"win\":" + (win ? "true" : "false") + ",\"ctrl\":" + (ctrl ? "true" : "false") + ",\"alt\":" + (alt ? "true" : "false") + ",\"shift\":" + (shift ? "true" : "false") + ",\"code\":" + code + ",\"key\":\"\"}";
        }

        [Test]
        public static void PowerToysReadsOnlySwitchedOnTools()
        {
            var files = new Dictionary<string, string>
            {
                { @"pt\settings.json", "{\"enabled\":{\"AlwaysOnTop\":true,\"FancyZones\":false,\"Peek\":true,\"FindMyMouse\":true,\"AdvancedPaste\":true,\"CmdPal\":true,\"Keyboard Manager\":true,\"Awake\":true}}" },
                { @"pt\AlwaysOnTop\settings.json", "{\"properties\":{\"hotkey\":{\"value\":" + Shortcut(true, true, false, false, 84) + "},\"increase-opacity-hotkey\":{\"value\":" + Shortcut(true, true, false, false, 187) + "}}}" },
                { @"pt\FancyZones\settings.json", "{\"properties\":{\"fancyzones_editor_hotkey\":{\"value\":" + Shortcut(true, false, false, true, 192) + "}}}" },
                { @"pt\Peek\settings.json", "{\"properties\":{\"DefaultActivationShortcut\":" + Shortcut(false, true, false, false, 32) + ",\"ActivationShortcut\":" + Shortcut(false, true, false, false, 32) + "}}" },
                { @"pt\FindMyMouse\settings.json", "{\"properties\":{\"activation_method\":{\"value\":0},\"activation_shortcut\":" + Shortcut(true, false, false, true, 70) + "}}" },
                { @"pt\AdvancedPaste\settings.json", "{\"properties\":{\"paste-as-plain-hotkey\":" + Shortcut(true, true, true, false, 86) + ",\"paste-as-json-hotkey\":" + Shortcut(false, false, false, false, 0)
                    + ",\"additional-actions\":{\"image-to-text\":{\"shortcut\":" + Shortcut(true, false, false, true, 73) + ",\"isShown\":true}}}}" },
                { @"pt\Keyboard Manager\settings.json", "{\"properties\":{\"activeConfiguration\":{\"value\":\"default\"},\"EditorShortcut\":" + Shortcut(true, false, false, true, 81) + "}}" },
                { @"pt\Keyboard Manager\default.json", "{\"remapKeys\":{\"inProcess\":[]},\"remapShortcuts\":{\"global\":[{\"originalKeys\":\"162;160;65\",\"newRemapKeys\":\"17;67\"}],\"appSpecific\":[{\"originalKeys\":\"17;77\",\"newRemapKeys\":\"173\",\"targetApp\":\"chrome\"}]}}" },
                { @"cmdpal.json", "{\"Hotkey\":" + Shortcut(true, false, true, false, 32) + ",\"CommandHotkeys\":[{\"Hotkey\":" + Shortcut(true, true, false, false, 78) + ",\"CommandId\":\"notes\"}]}" },
            };
            AppKeybinds app = OtherAppReaders.PowerToys("pt", "cmdpal.json", Files(files));
            Assert.NotNull(app);
            Assert.Null(app.Problem, app.Problem);
            Func<string, Keybind> find = keys => app.Keybinds.FirstOrDefault(k => k.Keys == keys);

            Assert.Equal("Always On Top: Pin or unpin the active window", find("Ctrl+Win+T").Action);
            Assert.Equal(KeybindScope.Global, find("Ctrl+Win+T").Scope);
            Assert.NotNull(find("Ctrl+Win+="));
            Assert.Null(find("Shift+Win+`"), "FancyZones is switched off");
            Assert.Equal(1, app.Keybinds.Count(k => k.Keys == "Ctrl+Space"), "Peek's default copy is not listed");
            Assert.Equal(KeybindScope.AppActive, find("Ctrl+Space").Scope);
            Assert.Contains(find("Ctrl+Space").Note, "File Explorer");
            Assert.Null(find("Shift+Win+F"), "Find My Mouse is set to double-press Ctrl, not its shortcut");
            Assert.Equal("Advanced Paste: Paste as plain text", find("Ctrl+Alt+Win+V").Action);
            Assert.Equal("Advanced Paste: Image to text", find("Shift+Win+I").Action);
            Assert.Equal("Command Palette: Open Command Palette", find("Alt+Win+Space").Action);
            Assert.Contains(find("Ctrl+Win+N").Action, "notes");
            Assert.Equal("Keyboard Manager: Open the Keyboard Manager editor", find("Shift+Win+Q").Action);
            Assert.Equal(KeybindScope.Global, find("Ctrl+Shift+A").Scope);
            Assert.Equal(KeybindScope.AppActive, find("Ctrl+M").Scope);
            Assert.Contains(find("Ctrl+M").Note, "chrome");
            Assert.True(app.Keybinds.All(k => k.Saved && !k.Combo.IsEmpty));
            Assert.Equal("PowerToys", app.Processes[0]);
        }

        [Test]
        public static void PowerToysMissingOrBroken()
        {
            Assert.Null(OtherAppReaders.PowerToys("pt", null, Files(new Dictionary<string, string>())), "not installed: not listed");
            AppKeybinds broken = OtherAppReaders.PowerToys("pt", null, Files(new Dictionary<string, string> { { @"pt\settings.json", "{not json" } }));
            Assert.NotNull(broken.Problem);
            AppKeybinds locked = OtherAppReaders.PowerToys("pt", null, path => { throw new System.IO.IOException("in use"); });
            Assert.Contains(locked.Problem, "in use");
            var partly = new Dictionary<string, string>
            {
                { @"pt\settings.json", "{\"enabled\":{\"AlwaysOnTop\":true,\"ColorPicker\":true}}" },
                { @"pt\AlwaysOnTop\settings.json", "garbage" },
                { @"pt\ColorPicker\settings.json", "{\"properties\":{\"ActivationShortcut\":" + Shortcut(true, false, false, true, 67) + "}}" },
            };
            AppKeybinds app = OtherAppReaders.PowerToys("pt", null, Files(partly));
            Assert.Equal(1, app.Keybinds.Count, "one broken tool does not hide the others");
            Assert.Equal("Color Picker: Open Color Picker", app.Keybinds[0].Action);
            Assert.Contains(app.Problem, "AlwaysOnTop");
        }

        // ---------------------------------------------------------------- OBS Studio

        [Test]
        public static void ObsReadsProfileAndSceneHotkeys()
        {
            var files = new Dictionary<string, string>
            {
                { @"obs\user.ini", "\uFEFF[General]\r\nHotkeyFocusType=NeverDisableHotkeys\r\n[Basic]\r\nProfile=Streaming\r\nProfileDir=Streaming\r\nSceneCollectionFile=Show.json\r\n" },
                { @"obs\basic\profiles\Streaming\basic.ini", "[General]\nName=Streaming\n[Hotkeys]\nOBSBasic.StartRecording={\"bindings\":[{\"control\":true,\"key\":\"OBS_KEY_F9\"}]}\nOBSBasic.StopRecording={\"bindings\":[]}\nOBSBasic.Screenshot={\"bindings\":[{\"key\":\"OBS_KEY_PRINT\",\"shift\":true},{\"key\":\"OBS_KEY_MOUSE4\"}]}\n" },
                { @"obs\basic\scenes\Show.json", "{\"name\":\"Show\",\"DesktopAudioDevice1\":{\"name\":\"Desktop Audio\",\"hotkeys\":{\"libobs.mute\":[{\"key\":\"OBS_KEY_NUM1\",\"alt\":true}],\"libobs.unmute\":[]}},"
                    + "\"sources\":[{\"name\":\"Gameplay\",\"hotkeys\":{\"OBSBasic.SelectScene\":[{\"key\":\"OBS_KEY_1\",\"control\":true,\"shift\":true}]},\"settings\":{\"items\":[{\"name\":\"Cam\"}]}}]}" },
            };
            AppKeybinds app = OtherAppReaders.Obs("obs", Files(files));
            Assert.NotNull(app);
            Assert.Null(app.Problem, app.Problem);
            Func<string, Keybind> find = keys => app.Keybinds.FirstOrDefault(k => k.Keys == keys);
            Assert.Equal("Start recording", find("Ctrl+F9").Action);
            Assert.Equal(KeybindScope.Global, find("Ctrl+F9").Scope);
            Assert.Equal("Take a screenshot of the output", find("Shift+Print Screen").Action);
            Assert.Equal("Mute “Desktop Audio”", find("Alt+Num 1").Action);
            Assert.Equal("Switch to scene “Gameplay”", find("Ctrl+Shift+1").Action);
            Assert.Equal(4, app.Keybinds.Count, "mouse buttons and empty bindings are skipped");
        }

        [Test]
        public static void ObsFocusSettingAndMissingFiles()
        {
            Assert.Null(OtherAppReaders.Obs("obs", Files(new Dictionary<string, string>())), "not installed: not listed");
            var files = new Dictionary<string, string>
            {
                { @"obs\global.ini", "[General]\nHotkeyFocusType=DisableHotkeysOutOfFocus\n[Basic]\nProfileDir=Untitled\nSceneCollectionFile=Untitled\n[Locations]\nProfiles=D:\\\\OBS Data\n" },
                { @"D:\OBS Data\obs-studio\basic\profiles\Untitled\basic.ini", "[Hotkeys]\nOBSBasic.StartStreaming={\"bindings\":[{\"key\":\"OBS_KEY_F10\",\"command\":true}]}\n" },
            };
            AppKeybinds app = OtherAppReaders.Obs("obs", Files(files));
            Assert.Equal(1, app.Keybinds.Count);
            Assert.Equal("Win+F10", app.Keybinds[0].Keys);
            Assert.Equal(KeybindScope.AppActive, app.Keybinds[0].Scope, "OBS set to ignore hotkeys outside its window");
        }

        // ---------------------------------------------------------------- apps added by hand

        [Test]
        public static void AddedAppsCountLikeTheOthers()
        {
            var added = new List<KeybindAppDef>
            {
                new KeybindAppDef
                {
                    Name = "Chat App", Program = @"C:\Program Files\Chat\ChatApp.exe",
                    Shortcuts =
                    {
                        new KeybindDef { Keys = "Ctrl+Shift+M", Action = "Mute" },
                        new KeybindDef { Keys = "ctrl-k", Action = "Quick switcher", OnlyWhenActive = true },
                        new KeybindDef { Keys = "", Action = "" },
                        new KeybindDef { Keys = "Ctrl+Banana", Action = "Broken" },
                    },
                },
                new KeybindAppDef { Name = "  " },
            };
            var where = new KeybindLocations { Snipline = "snip", NoteKeeper = "nk", JancyDefault = "j" };
            KeybindCatalog catalog = KeybindCatalog.Read(where, new AppSettings(), new List<MacroDef>(), added, Files(new Dictionary<string, string>()));
            AppKeybinds chat = catalog.Apps.Single(a => a.App == "Chat App");
            Assert.Equal(added[0].Id, chat.CustomId);
            Assert.Equal("ChatApp", chat.Processes[0]);
            Assert.Equal(3, chat.Keybinds.Count, "the empty row is skipped");
            Assert.Equal(KeybindScope.Global, chat.Keybinds[0].Scope);
            Assert.Equal(KeybindScope.AppActive, chat.Keybinds[1].Scope);
            Assert.Equal("Ctrl+K", chat.Keybinds[1].Keys);
            Assert.True(chat.Keybinds[2].Combo.IsEmpty);
            Assert.Contains(chat.Keybinds[2].Note, "does not recognise");
            Assert.False(catalog.Apps.Any(a => a.App.Trim().Length == 0), "a nameless app is not listed");
            Assert.Equal(1, catalog.Users(Keys("Ctrl+Shift+M")).Count);
            Assert.Equal(ComboVerdict.TakenGlobally, ComboCheck.Judge(catalog.Users(Keys("Ctrl+Shift+M")), true));
        }

        [Test]
        public static void AddedAppProblems()
        {
            var taken = new[] { "Macro Kit", "Jancy" };
            Assert.Contains(CustomKeybinds.Problems(new KeybindAppDef(), taken)[0], "name");
            Assert.Contains(CustomKeybinds.Problems(new KeybindAppDef { Name = "jancy " }, taken)[0], "already on the list");
            var def = new KeybindAppDef { Name = "Tool", Shortcuts = { new KeybindDef { Keys = "Ctrl+Q" }, new KeybindDef { Action = "No keys" }, new KeybindDef { Keys = "ctrl+nope" } } };
            List<string> problems = CustomKeybinds.Problems(def, taken);
            Assert.Equal(2, problems.Count);
            Assert.Contains(problems[0], "Shortcut 2 has no keys");
            Assert.Contains(problems[1], "Shortcut 3");
            Assert.Equal(0, CustomKeybinds.Problems(new KeybindAppDef { Name = "Tool", Program = "tool.exe", Shortcuts = { new KeybindDef { Keys = "F9", Action = "Go" } } }, taken).Count);
            Assert.Equal("obs64", CustomKeybinds.ProgramName("\"C:\\Program Files\\obs-studio\\bin\\64bit\\obs64.exe\""));
            Assert.Equal("", CustomKeybinds.ProgramName("  "));
        }

        [Test]
        public static void AddedAppsAreSavedAndRestored()
        {
            using (var dir = new TempDir())
            {
                Paths.Override(dir.File("data"));
                try
                {
                    var storage = new Storage(dir.File("data"));
                    Assert.Equal(0, storage.LoadKeybindApps().Count, "no file: empty list");
                    Assert.False(System.IO.File.Exists(dir.File(@"data\keybinds.json")), "nothing is written until something is added");
                    var app = new KeybindAppDef { Name = "Tool", Program = "tool", Shortcuts = { new KeybindDef { Keys = "Ctrl+Shift+M", Action = "Mute", OnlyWhenActive = true } } };
                    storage.SaveKeybindApps(new[] { app });
                    List<KeybindAppDef> back = new Storage(dir.File("data")).LoadKeybindApps();
                    Assert.Equal(1, back.Count);
                    Assert.Equal(app.Id, back[0].Id);
                    Assert.Equal("Tool", back[0].Name);
                    Assert.Equal("Ctrl+Shift+M", back[0].Shortcuts[0].Keys);
                    Assert.True(back[0].Shortcuts[0].OnlyWhenActive);
                    Assert.Contains(System.IO.File.ReadAllText(dir.File(@"data\keybinds.json")), "\"Schema\": 1");

                    System.IO.File.WriteAllText(dir.File(@"data\keybinds.json"), "{\"Schema\":1,\"Apps\":[{\"Name\":\"Old\",\"Shortcuts\":null},null]}");
                    List<KeybindAppDef> repaired = new Storage(dir.File("data")).LoadKeybindApps();
                    Assert.Equal(1, repaired.Count);
                    Assert.True(repaired[0].Id.Length > 0 && repaired[0].Shortcuts != null && repaired[0].Program == "");
                }
                finally { Paths.Override(null); }
            }
        }
    }
}
