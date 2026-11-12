using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MacroKit.Core;

namespace MacroKit.Tests
{
    // The MacroKit.Recording namespace would otherwise hide the Core type of the same name.
    using Recording = MacroKit.Core.Recording;

    public static class KeyNamesTests
    {
        static ushort Vk(string name)
        {
            ushort vk;
            Assert.True(KeyNames.TryGetVk(name, out vk), "name should resolve: " + name);
            return vk;
        }

        [Test]
        public static void LettersAndDigits()
        {
            for (char c = 'A'; c <= 'Z'; c++)
            {
                Assert.Equal((ushort)c, Vk(c.ToString()));
                Assert.Equal((ushort)c, Vk("VK_" + c));
                Assert.Equal((ushort)c, Vk(char.ToLowerInvariant(c).ToString()));
            }
            for (char c = '0'; c <= '9'; c++)
            {
                Assert.Equal((ushort)c, Vk(c.ToString()));
                Assert.Equal((ushort)c, Vk("VK_" + c));
            }
        }

        [Test]
        public static void EveryDocumentedNameResolves()
        {
            string[] names =
            {
                "ENTER", "RETURN", "TAB", "ESCAPE", "ESC", "SPACE", "BACK", "BACKSPACE", "DELETE", "DEL", "INSERT", "INS", "HOME", "END",
                "PAGEUP", "PGUP", "PRIOR", "PAGEDOWN", "PGDN", "NEXT", "UP", "DOWN", "LEFT", "RIGHT", "CLEAR", "BREAK", "CAPITAL", "CAPSLOCK",
                "NUMLOCK", "SCROLL", "SCROLLLOCK", "SNAPSHOT", "PRINTSCREEN", "PRINT", "SELECT", "EXECUTE", "HELP", "APPS", "MENUKEY",
                "SHIFT", "LSHIFT", "RSHIFT", "CTRL", "CONTROL", "LCTRL", "LCONTROL", "RCTRL", "RCONTROL", "ALT", "MENU", "LALT", "LMENU",
                "RALT", "RMENU", "WIN", "WINDOWS", "LWIN", "RWIN",
                "NUMPAD0", "NUMPAD1", "NUMPAD2", "NUMPAD3", "NUMPAD4", "NUMPAD5", "NUMPAD6", "NUMPAD7", "NUMPAD8", "NUMPAD9",
                "MULTIPLY", "ADD", "SUBTRACT", "DECIMAL", "DIVIDE", "SEPARATOR",
                "NUMPAD_PAGEUP", "NUMPAD_PAGEDOWN", "NUMPAD_HOME", "NUMPAD_END", "NUMPAD_UP", "NUMPAD_DOWN", "NUMPAD_LEFT", "NUMPAD_RIGHT",
                "NUMPAD_INSERT", "NUMPAD_DEL",
                "OEM_1", "OEM_2", "OEM_3", "OEM_4", "OEM_5", "OEM_6", "OEM_7", "OEM_8", "OEM_102", "OEM_PLUS", "OEM_MINUS", "OEM_COMMA", "OEM_PERIOD",
                "MEDIA_NEXT_TRACK", "MEDIA_PREV_TRACK", "MEDIA_PLAY_PAUSE", "MEDIA_STOP", "VOLUME_UP", "VOLUME_DOWN", "VOLUME_MUTE",
                "BROWSER_BACK", "BROWSER_FORWARD", "BROWSER_HOME", "BROWSER_REFRESH", "BROWSER_STOP", "BROWSER_SEARCH", "BROWSER_FAVORITES",
            };
            foreach (string name in names)
            {
                Vk(name);
                Vk(name.ToLowerInvariant());
            }
            for (int i = 1; i <= 24; i++) Assert.Equal((ushort)(0x6F + i), Vk("F" + i));
        }

        [Test]
        public static void ValuesMatchWindows()
        {
            Assert.Equal((ushort)0x0D, Vk("ENTER"));
            Assert.Equal((ushort)0x1B, Vk("ESC"));
            Assert.Equal((ushort)0x2E, Vk("DEL"));
            Assert.Equal((ushort)0x21, Vk("PGUP"));
            Assert.Equal((ushort)0x22, Vk("PGDN"));
            Assert.Equal((ushort)0x13, Vk("BREAK"));
            Assert.Equal((ushort)0x70, Vk("F1"));
            Assert.Equal((ushort)0x87, Vk("F24"));
            Assert.Equal((ushort)0x60, Vk("NUMPAD0"));
            Assert.Equal((ushort)0x6F, Vk("DIVIDE"));
            Assert.Equal((ushort)0xBA, Vk("OEM_1"));
            Assert.Equal((ushort)0xE2, Vk("OEM_102"));
            Assert.Equal((ushort)0xB3, Vk("MEDIA_PLAY_PAUSE"));
            Assert.Equal((ushort)0xAF, Vk("VOLUME_UP"));
            Assert.Equal((ushort)0xA6, Vk("BROWSER_BACK"));
        }

        [Test]
        public static void AliasesShareOneKey()
        {
            Assert.Equal(Vk("ENTER"), Vk("RETURN"));
            Assert.Equal(Vk("ESCAPE"), Vk("esc"));
            Assert.Equal(Vk("BACK"), Vk("BACKSPACE"));
            Assert.Equal(Vk("DELETE"), Vk("DEL"));
            Assert.Equal(Vk("INSERT"), Vk("INS"));
            Assert.Equal(Vk("PAGEUP"), Vk("PRIOR"));
            Assert.Equal(Vk("PAGEDOWN"), Vk("NEXT"));
            Assert.Equal(Vk("CAPITAL"), Vk("CAPSLOCK"));
            Assert.Equal(Vk("SCROLL"), Vk("SCROLLLOCK"));
            Assert.Equal(Vk("SNAPSHOT"), Vk("PRINTSCREEN"));
            Assert.Equal(Vk("APPS"), Vk("MENUKEY"));
            Assert.Equal(Vk("CTRL"), Vk("CONTROL"));
            Assert.Equal(Vk("CTRL"), Vk("LCONTROL"));
            Assert.Equal(Vk("RCTRL"), Vk("RCONTROL"));
            Assert.Equal(Vk("ALT"), Vk("MENU"));
            Assert.Equal(Vk("LALT"), Vk("LMENU"));
            Assert.Equal(Vk("RALT"), Vk("RMENU"));
            Assert.Equal(Vk("WIN"), Vk("WINDOWS"));
            Assert.Equal(Vk("WIN"), Vk("LWIN"));
        }

        [Test]
        public static void GenericModifiersSendTheLeftKey()
        {
            Assert.Equal((ushort)0xA0, Vk("SHIFT"));
            Assert.Equal((ushort)0xA2, Vk("CTRL"));
            Assert.Equal((ushort)0xA4, Vk("ALT"));
            Assert.Equal((ushort)0xA1, Vk("RSHIFT"));
            Assert.Equal((ushort)0xA3, Vk("RCTRL"));
            Assert.Equal((ushort)0xA5, Vk("RALT"));
            Assert.Equal((ushort)0x5B, Vk("WIN"));
            Assert.Equal((ushort)0x5C, Vk("RWIN"));
        }

        [Test]
        public static void PauseWordIsNotAKey()
        {
            ushort vk;
            Assert.False(KeyNames.TryGetVk("PAUSE", out vk));
            Assert.False(KeyNames.TryGetVk("", out vk));
            Assert.False(KeyNames.TryGetVk(null, out vk));
            Assert.False(KeyNames.TryGetVk("FOO", out vk));
            Assert.False(KeyNames.TryGetVk("F25", out vk));
            Assert.False(KeyNames.TryGetVk("AB", out vk));
        }

        [Test]
        public static void SpacesAroundNameAreIgnored()
        {
            Assert.Equal(Vk("ENTER"), Vk("  enter "));
        }

        [Test]
        public static void HexCodesAreAccepted()
        {
            Assert.Equal((ushort)0x41, Vk("0x41"));
            Assert.Equal((ushort)0xFA, Vk("VK_0xFA"));
            ushort vk;
            Assert.False(KeyNames.TryGetVk("0x", out vk));
            Assert.False(KeyNames.TryGetVk("0x1FF", out vk));
            Assert.False(KeyNames.TryGetVk("0x00", out vk));
        }

        [Test]
        public static void NumpadNavigationKeysAreNotExtended()
        {
            string[] names = { "PAGEUP", "PAGEDOWN", "HOME", "END", "UP", "DOWN", "LEFT", "RIGHT", "INSERT", "DEL" };
            foreach (string name in names)
            {
                ushort nav = Vk(name == "DEL" ? "DELETE" : name);
                ushort pad = Vk("NUMPAD_" + name);
                Assert.Equal(nav, KeyNames.BaseVk(pad), "same VK as the navigation key: " + name);
                Assert.Equal((ushort)KeyNames.ForceNonExtended, (ushort)(pad & KeyNames.ForceNonExtended), "flag set: " + name);
                Assert.True(KeyNames.IsExtended(nav), "navigation key is extended: " + name);
                Assert.False(KeyNames.IsExtended(pad), "numpad key is not extended: " + name);
            }
        }

        [Test]
        public static void ExtendedKeys()
        {
            foreach (string name in new[] { "UP", "DOWN", "LEFT", "RIGHT", "INSERT", "DELETE", "HOME", "END", "PAGEUP", "PAGEDOWN", "RCTRL", "RALT", "WIN", "RWIN", "APPS",
                "DIVIDE", "NUMLOCK", "SNAPSHOT", "VOLUME_UP", "VOLUME_DOWN", "VOLUME_MUTE", "MEDIA_PLAY_PAUSE", "MEDIA_NEXT_TRACK", "MEDIA_PREV_TRACK", "MEDIA_STOP",
                "BROWSER_BACK", "BROWSER_FORWARD", "BROWSER_HOME", "BROWSER_REFRESH", "BROWSER_STOP", "BROWSER_SEARCH", "BROWSER_FAVORITES" })
                Assert.True(KeyNames.IsExtended(Vk(name)), name + " should be extended");
            foreach (string name in new[] { "A", "Z", "0", "ENTER", "TAB", "SPACE", "CTRL", "ALT", "SHIFT", "RSHIFT", "F5", "NUMPAD5", "MULTIPLY", "ADD", "SUBTRACT", "DECIMAL", "OEM_1", "BACK", "ESC", "BREAK", "CAPITAL" })
                Assert.False(KeyNames.IsExtended(Vk(name)), name + " should not be extended");
        }

        [Test]
        public static void ModifierChecks()
        {
            foreach (string name in new[] { "SHIFT", "RSHIFT", "CTRL", "RCTRL", "ALT", "RALT", "WIN", "RWIN" }) Assert.True(KeyNames.IsModifier(Vk(name)), name);
            foreach (ushort generic in new ushort[] { 0x10, 0x11, 0x12 }) Assert.True(KeyNames.IsModifier(generic), "generic " + generic);
            foreach (string name in new[] { "A", "TAB", "ENTER", "CAPITAL", "F1", "APPS", "NUMPAD_DOWN" }) Assert.False(KeyNames.IsModifier(Vk(name)), name);
        }

        [Test]
        public static void BaseVkMasksTheForceBit()
        {
            Assert.Equal((ushort)0x28, KeyNames.BaseVk((ushort)(0x28 | KeyNames.ForceNonExtended)));
            Assert.Equal((ushort)0x41, KeyNames.BaseVk(0x41));
        }

        [Test]
        public static void MacroTokensRoundTrip()
        {
            foreach (string name in new[] { "A", "9", "ENTER", "TAB", "ESCAPE", "SPACE", "BACK", "DELETE", "INSERT", "HOME", "END", "PAGEUP", "PAGEDOWN", "UP", "DOWN", "LEFT", "RIGHT",
                "CLEAR", "BREAK", "CAPITAL", "NUMLOCK", "SCROLL", "SNAPSHOT", "PRINT", "SELECT", "EXECUTE", "HELP", "APPS", "SHIFT", "RSHIFT", "CTRL", "RCTRL", "ALT", "RALT", "WIN", "RWIN",
                "F1", "F12", "F24", "NUMPAD0", "NUMPAD9", "MULTIPLY", "ADD", "SUBTRACT", "DECIMAL", "DIVIDE", "SEPARATOR", "NUMPAD_PAGEUP", "NUMPAD_DEL", "NUMPAD_DOWN",
                "OEM_1", "OEM_PLUS", "OEM_MINUS", "OEM_COMMA", "OEM_PERIOD", "OEM_102", "MEDIA_PLAY_PAUSE", "VOLUME_MUTE", "BROWSER_FAVORITES" })
            {
                ushort vk = Vk(name);
                string token = KeyNames.MacroToken(vk);
                Assert.Equal(vk, Vk(token), "token " + token + " for " + name);
            }
            Assert.Equal("CTRL", KeyNames.MacroToken(Vk("LCONTROL")));
            Assert.Equal("ALT", KeyNames.MacroToken(Vk("LALT")));
            Assert.Equal("TAB", KeyNames.MacroToken(Vk("TAB")));
            Assert.Equal("DOWN", KeyNames.MacroToken(Vk("DOWN")));
            Assert.Equal("C", KeyNames.MacroToken(0x43));
            Assert.Equal("CTRL", KeyNames.MacroToken(0x11));   // the generic VK
            Assert.Equal("0xFA", KeyNames.MacroToken(0xFA));
            Assert.Equal((ushort)0xFA, Vk(KeyNames.MacroToken(0xFA)));
        }

        [Test]
        public static void DisplayNames()
        {
            Assert.Equal("Ctrl", KeyNames.Display(Vk("CTRL")));
            Assert.Equal("Alt", KeyNames.Display(Vk("ALT")));
            Assert.Equal("Shift", KeyNames.Display(Vk("SHIFT")));
            Assert.Equal("Win", KeyNames.Display(Vk("WIN")));
            Assert.Equal("Right Ctrl", KeyNames.Display(Vk("RCTRL")));
            Assert.Equal("Tab", KeyNames.Display(Vk("TAB")));
            Assert.Equal("Down", KeyNames.Display(Vk("DOWN")));
            Assert.Equal("F5", KeyNames.Display(Vk("F5")));
            Assert.Equal("A", KeyNames.Display(0x41));
            Assert.Equal("Num 5", KeyNames.Display(Vk("NUMPAD5")));
            Assert.Equal("Num Down", KeyNames.Display(Vk("NUMPAD_DOWN")));
            Assert.Equal("Page Up", KeyNames.Display(Vk("PGUP")));
            Assert.Equal("Pause", KeyNames.Display(0x13));
            Assert.Equal("Ctrl", KeyNames.Display(0x11));
            Assert.Equal("Key 0xFA", KeyNames.Display(0xFA));
        }

        [Test]
        public static void DisplayNamesAreUniqueAndParse()
        {
            var seen = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
            for (int vk = 1; vk < 0x100; vk++)
            {
                string text = KeyNames.Display((ushort)vk);
                if (text.StartsWith("Key 0x")) continue;
                // Generic VKs share the display name of their left key on purpose.
                if (vk == 0x10 || vk == 0x11 || vk == 0x12) continue;
                Assert.False(seen.ContainsKey(text), "duplicate display name " + text);
                seen[text] = (ushort)vk;
                ushort parsed;
                Assert.True(KeyNames.TryParseDisplay(text, out parsed), text);
                Assert.Equal((ushort)vk, parsed, text);
            }
            Assert.False(seen.Keys.Any(k => k.Contains("+")), "display names never contain +");
        }

        [Test]
        public static void MouseButtonNames()
        {
            MouseButton b;
            Assert.True(KeyNames.IsMouseButton("LBUTTON", out b)); Assert.Equal(MouseButton.Left, b);
            Assert.True(KeyNames.IsMouseButton("rbutton", out b)); Assert.Equal(MouseButton.Right, b);
            Assert.True(KeyNames.IsMouseButton("MBUTTON", out b)); Assert.Equal(MouseButton.Middle, b);
            Assert.True(KeyNames.IsMouseButton("XBUTTON1", out b)); Assert.Equal(MouseButton.X1, b);
            Assert.True(KeyNames.IsMouseButton("XBUTTON2", out b)); Assert.Equal(MouseButton.X2, b);
            Assert.False(KeyNames.IsMouseButton("BUTTON", out b));
            Assert.False(KeyNames.IsMouseButton("", out b));
        }
    }

    public static class HotkeyTests
    {
        static Hotkey Parse(string text)
        {
            Hotkey hotkey;
            Assert.True(Hotkey.TryParse(text, out hotkey), "should parse: " + text);
            return hotkey;
        }

        [Test]
        public static void ParsesTheDefaults()
        {
            Hotkey h = Parse("Ctrl+Shift+Alt+R");
            Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Alt, h.Modifiers);
            Assert.Equal((ushort)0x52, h.Vk);
            Assert.Equal("Ctrl+Shift+Alt+R", h.ToString());
            Assert.Equal("Ctrl+Alt+1", Parse("Ctrl+Alt+1").ToString());
            Assert.Equal("F13", Parse("F13").ToString());
        }

        [Test]
        public static void ParsingIsCaseAndOrderInsensitive()
        {
            Assert.Equal("Ctrl+Shift+Alt+R", Parse("alt+shift+CTRL+r").ToString());
            Assert.Equal("Ctrl+Alt+Win+Down", Parse("Win + Alt + Control + down").ToString());
            Assert.Equal("Ctrl+Win+Space", Parse("windows+ctrl+space").ToString());
        }

        [Test]
        public static void ModifierValuesMatchRegisterHotKey()
        {
            Assert.Equal(1, (int)HotkeyModifiers.Alt);
            Assert.Equal(2, (int)HotkeyModifiers.Ctrl);
            Assert.Equal(4, (int)HotkeyModifiers.Shift);
            Assert.Equal(8, (int)HotkeyModifiers.Win);
        }

        [Test]
        public static void RoundTripsEveryKey()
        {
            for (int vk = 1; vk < 0x100; vk++)
            {
                if (KeyNames.IsModifier((ushort)vk)) continue;
                var h = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, (ushort)vk);
                string text = h.ToString();
                Hotkey back;
                if (text.Contains("Key 0x")) continue;     // keys without a name cannot be typed
                Assert.True(Hotkey.TryParse(text, out back), "parse " + text);
                Assert.True(h == back, "round trip " + text);
            }
        }

        [Test]
        public static void PauseKey()
        {
            Hotkey h = Parse("Pause");
            Assert.Equal((ushort)0x13, h.Vk);
            Assert.Equal("Pause", h.ToString());
            Assert.True(h.IsValidGlobal);
            Assert.Equal("Ctrl+Pause", Parse("ctrl+pause").ToString());
        }

        [Test]
        public static void RejectsBadText()
        {
            Hotkey h;
            foreach (string text in new[] { null, "", "  ", "Ctrl", "Ctrl+", "+R", "Ctrl+Shift", "Ctrl+A+B", "Ctrl+Foo", "R R", "Ctrl+Alt+Ctrl", "Ctrl++" })
                Assert.False(Hotkey.TryParse(text, out h), "should not parse: " + text);
            Assert.True(Hotkey.TryParse("Ctrl+Ctrl+A", out h), "a repeated modifier is harmless");
            Assert.Equal("Ctrl+A", h.ToString());
        }

        [Test]
        public static void ValidityForGlobalHotkeys()
        {
            Assert.True(Parse("Ctrl+R").IsValidGlobal);
            Assert.True(Parse("Alt+R").IsValidGlobal);
            Assert.True(Parse("Win+R").IsValidGlobal);
            Assert.True(Parse("Ctrl+Shift+Alt+S").IsValidGlobal);
            Assert.True(Parse("F13").IsValidGlobal);
            Assert.True(Parse("F24").IsValidGlobal);
            Assert.True(Parse("Pause").IsValidGlobal);
            Assert.False(Parse("R").IsValidGlobal);
            Assert.False(Parse("F5").IsValidGlobal);
            Assert.False(Parse("F12").IsValidGlobal);
            Assert.False(Parse("Shift+R").IsValidGlobal);
            Assert.False(Parse("Shift+F13").IsValidGlobal);
            Assert.False(default(Hotkey).IsValidGlobal);
        }

        [Test]
        public static void EmptyHotkey()
        {
            Hotkey none = default(Hotkey);
            Assert.True(none.IsEmpty);
            Assert.Equal("", none.ToString());
            Assert.False(Parse("A").IsEmpty);
        }

        [Test]
        public static void EqualityAndForceBitIsDropped()
        {
            Assert.True(Parse("Ctrl+A") == Parse("control+a"));
            Assert.True(Parse("Ctrl+A") != Parse("Ctrl+B"));
            Assert.Equal(Parse("Ctrl+A").GetHashCode(), Parse("ctrl+A").GetHashCode());
            Assert.Equal((ushort)0x28, new Hotkey(HotkeyModifiers.None, (ushort)(0x28 | KeyNames.ForceNonExtended)).Vk);
        }

        [Test]
        public static void DisplayNamesParseToo()
        {
            Assert.Equal("Ctrl+Page Up", Parse("Ctrl+Page Up").ToString());
            Assert.Equal("Alt+Num 5", Parse("Alt+Num 5").ToString());
            Assert.Equal("Ctrl+Alt+Num Plus", Parse("Ctrl+Alt+Num Plus").ToString());
        }
    }

    public static class WindowMatchTests
    {
        static WindowInfo Window(string title, string process) { return new WindowInfo { Handle = new IntPtr(1), Title = title, ProcessName = process }; }

        [Test]
        public static void EmptyMatchMatchesNothing()
        {
            var match = new WindowMatch();
            Assert.True(match.IsEmpty);
            Assert.False(match.Matches(Window("Anything", "a.exe")));
            Assert.False(new WindowMatch { ProcessName = "  " }.Matches(Window("x", "a.exe")));
            Assert.False(new WindowMatch { ProcessName = "a.exe" }.Matches(null));
        }

        [Test]
        public static void ProcessMatchIsCaseInsensitive()
        {
            var match = new WindowMatch { ProcessName = "EXCEL.EXE" };
            Assert.True(match.Matches(Window("Book1", "excel.exe")));
            Assert.True(match.Matches(Window("Book1", "Excel.exe")));
            Assert.False(match.Matches(Window("Book1", "Chrome.exe")));
            Assert.False(match.Matches(Window("Book1", null)));
        }

        [Test]
        public static void ProcessNameWithoutExeMatches()
        {
            Assert.True(new WindowMatch { ProcessName = "chrome" }.Matches(Window("x", "Chrome.exe")));
            Assert.True(new WindowMatch { ProcessName = "Chrome.exe" }.Matches(Window("x", "chrome")));
            Assert.False(new WindowMatch { ProcessName = "chr" }.Matches(Window("x", "Chrome.exe")));
        }

        [Test]
        public static void TitleContainsIsCaseInsensitive()
        {
            var match = new WindowMatch { TitleContains = "orders" };
            Assert.True(match.Matches(Window("Open ORDERS - Chrome", "Chrome.exe")));
            Assert.False(match.Matches(Window("Customers", "Chrome.exe")));
            Assert.False(match.Matches(Window(null, "Chrome.exe")));
        }

        [Test]
        public static void BothFieldsMustMatch()
        {
            var match = new WindowMatch { ProcessName = "Chrome.exe", TitleContains = "Orders" };
            Assert.True(match.Matches(Window("Orders", "Chrome.exe")));
            Assert.False(match.Matches(Window("Orders", "Excel.exe")));
            Assert.False(match.Matches(Window("Customers", "Chrome.exe")));
        }

        [Test]
        public static void Describes()
        {
            Assert.Equal("Chrome.exe, title contains “Orders”", new WindowMatch { ProcessName = "Chrome.exe", TitleContains = "Orders" }.Describe());
            Assert.Equal("Chrome.exe", new WindowMatch { ProcessName = "Chrome.exe" }.Describe());
            Assert.Equal("title contains “Orders”", new WindowMatch { TitleContains = "Orders" }.Describe());
        }

        [Test]
        public static void FromWindowCopiesProcessAndTitle()
        {
            WindowMatch match = WindowMatch.From(Window("Book1 - Excel", "EXCEL.EXE"));
            Assert.Equal("EXCEL.EXE", match.ProcessName);
            Assert.Equal("Book1 - Excel", match.TitleContains);
            Assert.True(match.Matches(Window("Book1 - Excel", "EXCEL.EXE")));
            Assert.True(WindowMatch.From(null).IsEmpty);
        }

        [Test]
        public static void WindowInfoText()
        {
            Assert.Equal("Book1 — EXCEL.EXE", Window("Book1", "EXCEL.EXE").ToString());
        }
    }

    public static class ModelTests
    {
        [Test]
        public static void MacroDefaults()
        {
            var m = new MacroDef();
            Assert.Equal(32, m.Id.Length);
            Assert.Equal("New macro", m.Name);
            Assert.Equal("", m.Script);
            Assert.Equal(RepeatMode.Once, m.Repeat);
            Assert.Equal(5, m.RepeatTimes);
            Assert.Equal(0, m.RepeatDelayMs);
            Assert.Equal(10, m.KeyDelayMs);
            Assert.True(m.NewLineIsEnter);
            Assert.Equal(TargetKind.PreviousWindow, m.Target);
            Assert.True(m.TargetWindow.IsEmpty);
            Assert.True(m.AltTabPartner.IsEmpty);
            Assert.True(m.StopIfFocusLeaves);
            Assert.Equal("", m.Hotkey);
            Assert.True(m.ShowOnDeck);
            Assert.False(m.Id == new MacroDef().Id, "every macro gets its own Id");
        }

        [Test]
        public static void PhraseDefaults()
        {
            var p = new PhraseDef();
            Assert.Equal(32, p.Id.Length);
            Assert.Equal("", p.Trigger);
            Assert.Equal("", p.Replacement);
            Assert.True(p.Enabled);
            Assert.False(p.MatchCase);
            Assert.Equal(ExpandWhen.Immediately, p.When);
            Assert.Equal(InsertMethod.Type, p.Method);
        }

        [Test]
        public static void SettingsDefaults()
        {
            var s = new AppSettings();
            Assert.Equal(1, s.Schema);
            Assert.Equal("Ctrl+Shift+Alt+R", s.RecordHotkey);
            Assert.Equal("Ctrl+Shift+Alt+P", s.PlayHotkey);
            Assert.Equal("Ctrl+Shift+Alt+S", s.StopHotkey);
            Assert.Equal("Ctrl+Shift+Alt+D", s.DeckHotkey);
            Assert.True(s.EscStops);
            Assert.True(s.RecordMouseMoves);
            Assert.False(s.AutoSaveRecordings);
            Assert.Equal(1.0, s.Playback.Speed);
            Assert.Equal(RepeatMode.Once, s.Playback.Repeat);
            Assert.Equal(2, s.Playback.RepeatTimes);
            Assert.Equal(0, s.Playback.RepeatDelayMs);
            Assert.Equal("", s.SelectedRecordingId);
            Assert.True(s.ExpansionEnabled);
            Assert.Equal(0, s.ExcludedPrograms.Count);
            Assert.False(s.DeckVisible);
            Assert.True(double.IsNaN(s.DeckLeft));
            Assert.True(double.IsNaN(s.DeckTop));
            Assert.Equal(3, s.DeckColumns);
            Assert.True(double.IsNaN(s.WindowLeft));
            Assert.True(double.IsNaN(s.WindowTop));
            Assert.Equal(1040.0, s.WindowWidth);
            Assert.Equal(720.0, s.WindowHeight);
            Assert.Equal(0, s.SelectedTab);
            Assert.Equal("System", s.Theme);
            Assert.True(s.CloseToTray);
            Assert.True(s.ShowWindowAfterRun);
            Assert.False(s.StartWithWindows);
        }

        [Test]
        public static void RecordingBasics()
        {
            var r = new Recording();
            Assert.Equal(1, r.Schema);
            Assert.Equal(32, r.Id.Length);
            Assert.Equal(0, r.Events.Count);
            Assert.Equal("Recording 2026-11-12 08:41", Recording.DefaultName(new DateTime(2026, 11, 12, 8, 41, 59)));
            DateTimeOffset parsed;
            Assert.True(DateTimeOffset.TryParse(Recording.NowIso(), out parsed), "NowIso is a valid ISO date");
            Assert.True(Recording.NowIso().Contains("T"));
        }

        [Test]
        public static void RecEventFields()
        {
            var e = new RecEvent(12, RecKind.KeyDown, 0x41, 0x1E | 0x10000);
            Assert.Equal(12, e.T);
            Assert.Equal(RecKind.KeyDown, e.Kind);
            Assert.Equal(0x41, e.A);
            Assert.Equal(0x1001E, e.B);
            Assert.Equal(5, (int)RecKind.KeyDown);
            Assert.Equal(0, (int)RecKind.Move);
            Assert.Equal(6, (int)RecKind.KeyUp);
        }
    }

    public static class ClockTests
    {
        [Test]
        public static void SystemClockCancels()
        {
            var clock = new SystemClock();
            var source = new CancellationTokenSource();
            source.Cancel();
            Assert.Throws<OperationCanceledException>(() => clock.Sleep(10000, source.Token));
        }

        [Test]
        public static void SystemClockSleepsAndMeasures()
        {
            var clock = new SystemClock();
            long before = clock.NowMs;
            clock.Sleep(30, CancellationToken.None);
            Assert.True(clock.NowMs - before >= 25, "slept about 30 ms");
            clock.Sleep(0, CancellationToken.None);
        }

        [Test]
        public static void SystemClockWakesEarlyWhenCancelled()
        {
            var clock = new SystemClock();
            var source = new CancellationTokenSource(50);
            long before = clock.NowMs;
            Assert.Throws<OperationCanceledException>(() => clock.Sleep(10000, source.Token));
            Assert.True(clock.NowMs - before < 5000, "returned long before the 10 s were up");
        }

        [Test]
        public static void FakeClockAdvancesVirtualTime()
        {
            var clock = new FakeClock();
            clock.Sleep(100, CancellationToken.None);
            clock.Sleep(50, CancellationToken.None);
            Assert.Equal(150L, clock.NowMs);
            Assert.SequenceEqual(new[] { 100, 50 }, clock.Sleeps);
        }

        [Test]
        public static void FakeClockRunsScheduledCallbacksInOrder()
        {
            var clock = new FakeClock();
            var seen = new List<string>();
            clock.At(80, () => seen.Add("b@" + clock.NowMs));
            clock.At(30, () => seen.Add("a@" + clock.NowMs));
            clock.After(500, () => seen.Add("late"));
            clock.Sleep(100, CancellationToken.None);
            Assert.SequenceEqual(new[] { "a@30", "b@80" }, seen);
            Assert.Equal(100L, clock.NowMs);
        }

        [Test]
        public static void FakeClockCallbackCanCancelMidSleep()
        {
            var clock = new FakeClock();
            var source = new CancellationTokenSource();
            clock.At(40, source.Cancel);
            Assert.Throws<OperationCanceledException>(() => clock.Sleep(100, source.Token));
            Assert.Equal(40L, clock.NowMs);
        }

        [Test]
        public static void FakeClockHonoursAnAlreadyCancelledToken()
        {
            var clock = new FakeClock();
            var source = new CancellationTokenSource();
            source.Cancel();
            Assert.Throws<OperationCanceledException>(() => clock.Sleep(0, source.Token));
        }
    }

    public static class FakeTests
    {
        [Test]
        public static void InputSinkLogsReadableLines()
        {
            var sink = new FakeInputSink();
            sink.KeyDown(0xA2); sink.KeyUp(0xA2);
            sink.KeyCombo(new ushort[] { 0xA2, 0x43 });
            sink.TypeText("hello\nthere");
            sink.MouseMoveTo(10, 20);
            sink.MouseMoveBy(5, -3);
            sink.MouseButton(MouseButton.Left, true);
            sink.MouseWheel(120, false);
            sink.MouseWheel(-120, true);
            sink.MouseMoveNormalized(100, 200);
            Assert.SequenceEqual(new[]
            {
                "down LCONTROL", "up LCONTROL", "combo LCONTROL+C", "text hello\\nthere", "moveto 10,20", "moveby 5,-3",
                "button Left down", "wheel 120 v", "wheel -120 h", "norm 100,200",
            }, sink.Log);
            Assert.Equal(new ScreenPoint(15, 17).ToString(), sink.CursorPosition.ToString());
            Assert.True(sink.HeldButtons.Contains(MouseButton.Left));
        }

        [Test]
        public static void InputSinkTracksHeldKeys()
        {
            var sink = new FakeInputSink();
            sink.KeyDown(0xA4);
            sink.KeyDown((ushort)(0x28 | KeyNames.ForceNonExtended));
            Assert.True(sink.HeldKeys.Contains(0xA4));
            Assert.True(sink.HeldKeys.Contains(0x28));
            sink.KeyUp(0xA4);
            Assert.False(sink.HeldKeys.Contains(0xA4));
            Assert.Equal("up LALT", sink.Log.Last());
        }

        [Test]
        public static void InputSinkPhysicalKeysAndHooks()
        {
            var sink = new FakeInputSink();
            sink.PhysicallyDown.Add(0xA3);
            Assert.True(sink.IsPhysicallyDown(0xA3));
            Assert.True(sink.IsPhysicallyDown(0x11), "generic Ctrl is down when a side is");
            Assert.False(sink.IsPhysicallyDown(0x10));
            var seen = new List<string>();
            sink.OnCall = seen.Add;
            sink.KeyDown(0x41);
            Assert.SequenceEqual(new[] { "down A" }, seen);
            sink.FailWhen = line => line.StartsWith("down");
            Assert.Throws<InvalidOperationException>(() => sink.KeyDown(0x42));
            Assert.Equal(1, sink.Log.Count);
        }

        [Test]
        public static void WindowSystemTracksForegroundAndZOrder()
        {
            var ws = new FakeWindowSystem();
            WindowInfo excel = ws.AddWindow(1, "Book1", "EXCEL.EXE");
            WindowInfo chrome = ws.AddWindow(2, "Orders", "Chrome.exe");
            WindowInfo own = ws.AddOwnWindow(9);
            Assert.True(ws.IsOwnWindow(own.Handle));
            Assert.False(ws.IsOwnWindow(excel.Handle));
            Assert.Equal(2, ws.ListSwitchableWindows().Count);
            Assert.True(ws.Activate(chrome.Handle, 100));
            Assert.Equal(chrome.Handle, ws.Foreground);
            Assert.Equal(chrome.Handle, ws.LastExternalForeground);
            Assert.Equal(chrome.Handle, ws.ListSwitchableWindows()[0].Handle);
            Assert.True(ws.Activate(own.Handle, 100));
            Assert.Equal(own.Handle, ws.Foreground);
            Assert.Equal(chrome.Handle, ws.LastExternalForeground);
            ws.ActivateFails.Add(excel.Handle);
            Assert.False(ws.Activate(excel.Handle, 100));
            Assert.Equal(own.Handle, ws.Foreground);
            Assert.SequenceEqual(new[] { "activate Orders (ok)", "activate Macro Kit (ok)", "activate Book1 (failed)" }, ws.ActivateLog);
            Assert.Equal(excel.Handle, ws.Find(new WindowMatch { ProcessName = "excel.exe" }).Handle);
            Assert.Null(ws.Find(new WindowMatch()));
            Assert.Null(ws.Find(new WindowMatch { TitleContains = "nothing" }));
        }

        [Test]
        public static void WindowSystemAltTabSwapsTheTopTwo()
        {
            var ws = new FakeWindowSystem();
            WindowInfo a = ws.AddWindow(1, "A", "a.exe");
            WindowInfo b = ws.AddWindow(2, "B", "b.exe");
            ws.SetForeground(a.Handle);
            ws.SimulateAltTab();
            Assert.Equal(b.Handle, ws.Foreground);
            ws.SimulateAltTab();
            Assert.Equal(a.Handle, ws.Foreground);
        }

        [Test]
        public static void ClipboardKeepsHistory()
        {
            var clip = new FakeClipboard { Text = "old" };
            clip.SetText("new");
            Assert.Equal("new", clip.GetText());
            Assert.SequenceEqual(new[] { "new" }, clip.History);
        }
    }

    public static class JsonTests
    {
        [Test]
        public static void EnumsAreWrittenAsNames()
        {
            string text = Json.Serialize(new MacroDef { Repeat = RepeatMode.Times, Target = TargetKind.SpecificWindow });
            Assert.Contains(text, "\"Repeat\": \"Times\"");
            Assert.Contains(text, "\"Target\": \"SpecificWindow\"");
            var back = Json.Deserialize<MacroDef>(text);
            Assert.Equal(RepeatMode.Times, back.Repeat);
            Assert.Equal(TargetKind.SpecificWindow, back.Target);
        }

        [Test]
        public static void OutputIsIndented()
        {
            string text = Json.Serialize(new PlaybackSettings());
            Assert.True(text.StartsWith("{\n  \"Speed\": 1,\n"), text);
            Assert.True(text.EndsWith("}\n"));
        }

        [Test]
        public static void EnumNamesAreCaseInsensitiveAndUnknownNamesKeepTheDefault()
        {
            var p = Json.Deserialize<PhraseDef>("{\"When\":\"afterdelimiter\",\"Method\":\"Teleport\"}");
            Assert.Equal(ExpandWhen.AfterDelimiter, p.When);
            Assert.Equal(InsertMethod.Type, p.Method);
        }

        [Test]
        public static void MissingPropertiesKeepDefaults()
        {
            var m = Json.Deserialize<MacroDef>("{\"Name\":\"Only a name\"}");
            Assert.Equal("Only a name", m.Name);
            Assert.Equal(10, m.KeyDelayMs);
            Assert.True(m.NewLineIsEnter);
            Assert.True(m.ShowOnDeck);
            Assert.Equal(32, m.Id.Length);
            Assert.True(m.TargetWindow.IsEmpty);
        }

        [Test]
        public static void UnknownPropertiesAreIgnored()
        {
            var s = Json.Deserialize<AppSettings>("{\"Schema\":1,\"FutureThing\":{\"a\":[1,2,3]},\"DeckColumns\":4,\"Another\":\"x\"}");
            Assert.Equal(4, s.DeckColumns);
        }

        [Test]
        public static void WrongTypesKeepTheDefault()
        {
            var s = Json.Deserialize<AppSettings>("{\"DeckColumns\":\"many\",\"EscStops\":\"maybe\",\"Theme\":7,\"CloseToTray\":false}");
            Assert.Equal(3, s.DeckColumns);
            Assert.True(s.EscStops);
            Assert.Equal("7", s.Theme);
            Assert.False(s.CloseToTray);
        }

        [Test]
        public static void NullPropertiesKeepTheDefault()
        {
            var s = Json.Deserialize<AppSettings>("{\"Playback\":null,\"Theme\":null}");
            Assert.NotNull(s.Playback);
            Assert.Equal("System", s.Theme);
        }

        [Test]
        public static void PropertyNamesAreCaseInsensitive()
        {
            Assert.Equal("x", Json.Deserialize<MacroDef>("{\"name\":\"x\"}").Name);
        }

        [Test]
        public static void NumbersAcceptIntegersAndDecimals()
        {
            var p = Json.Deserialize<PlaybackSettings>("{\"Speed\":2.5,\"RepeatTimes\":7,\"RepeatDelayMs\":250.0}");
            Assert.Equal(2.5, p.Speed);
            Assert.Equal(7, p.RepeatTimes);
            Assert.Equal(250, p.RepeatDelayMs);
            Assert.Equal(1.0, Json.Deserialize<PlaybackSettings>("{\"Speed\":1}").Speed);
        }

        [Test]
        public static void NaNIsWrittenAsNullAndComesBackAsTheDefault()
        {
            var s = new AppSettings { DeckLeft = double.NaN, DeckTop = 123.5 };
            string text = Json.Serialize(s);
            Assert.Contains(text, "\"DeckLeft\": null");
            var back = Json.Deserialize<AppSettings>(text);
            Assert.True(double.IsNaN(back.DeckLeft));
            Assert.Equal(123.5, back.DeckTop);
        }

        [Test]
        public static void StringsEscapeAndUnicodeSurvive()
        {
            string shrug = "¯\\_(ツ)_/¯";
            var p = new PhraseDef { Trigger = "/shrug", Replacement = shrug + "\n\"quoted\"\ttab\r\n\u0001" };
            string text = Json.Serialize(p);
            Assert.Contains(text, "\\u0001");
            Assert.Contains(text, "\\\\_");
            var back = Json.Deserialize<PhraseDef>(text);
            Assert.Equal(p.Replacement, back.Replacement);
            Assert.Equal("/shrug", back.Trigger);
        }

        [Test]
        public static void ListsOfStringsAndObjectsRoundTrip()
        {
            var s = new AppSettings();
            s.ExcludedPrograms.Add("game.exe");
            s.ExcludedPrograms.Add("other.exe");
            var back = Json.Deserialize<AppSettings>(Json.Serialize(s));
            Assert.SequenceEqual(new[] { "game.exe", "other.exe" }, back.ExcludedPrograms);
            var macro = new MacroDef { AltTabPartner = new WindowMatch { ProcessName = "Chrome.exe", TitleContains = "Orders" } };
            var macroBack = Json.Deserialize<MacroDef>(Json.Serialize(macro));
            Assert.Equal("Chrome.exe", macroBack.AltTabPartner.ProcessName);
            Assert.Equal("Orders", macroBack.AltTabPartner.TitleContains);
            Assert.False(Json.Serialize(macro).Contains("IsEmpty"), "computed properties are not data");
        }

        [Test]
        public static void RecordingEventsAreCompactArrays()
        {
            var r = new Recording { Id = "abc", Name = "Test", Created = "2026-11-12T08:00:00-05:00", DurationMs = 150 };
            r.Events.Add(new RecEvent(0, RecKind.Move, 100, 200));
            r.Events.Add(new RecEvent(10, RecKind.ButtonDown, 0, 0));
            r.Events.Add(new RecEvent(150, RecKind.KeyUp, 65, 30 | 0x10000));
            string text = Json.Serialize(r);
            Assert.Contains(text, "[0,0,100,200]");
            Assert.Contains(text, "[10,1,0,0]");
            Assert.Contains(text, "[150,6,65,65566]");
            var back = Json.Deserialize<Recording>(text);
            Assert.Equal(3, back.Events.Count);
            Assert.Equal(RecKind.ButtonDown, back.Events[1].Kind);
            Assert.Equal(65566, back.Events[2].B);
            Assert.Equal(150, back.DurationMs);
        }

        [Test]
        public static void ManyRecordingEventsRoundTrip()
        {
            var r = new Recording();
            for (int i = 0; i < 50000; i++) r.Events.Add(new RecEvent(i, (RecKind)(i % 7), i, -i));
            var back = Json.Deserialize<Recording>(Json.Serialize(r));
            Assert.Equal(50000, back.Events.Count);
            Assert.Equal(new RecEvent(49999, (RecKind)(49999 % 7), 49999, -49999).B, back.Events[49999].B);
        }

        [Test]
        public static void BadEventsAreSkipped()
        {
            var r = Json.Deserialize<Recording>("{\"Events\":[[0,0,1,2],[1,99,0,0],[2,5],\"junk\",[3,6,4,5]]}");
            Assert.Equal(2, r.Events.Count);
            Assert.Equal(0, r.Events[0].T);
            Assert.Equal(3, r.Events[1].T);
        }

        [Test]
        public static void InvalidJsonThrows()
        {
            Assert.Throws<ArgumentException>(() => Json.Deserialize<AppSettings>("{ not json"));
            Assert.Throws<FormatException>(() => Json.Deserialize<AppSettings>("[1,2,3]"));
            Assert.Throws<ArgumentException>(() => Json.Deserialize<AppSettings>(""));
        }

        [Test]
        public static void HasPropertyLooksAtTopLevel()
        {
            object tree = Json.ParseTree("{\"Events\":[],\"x\":1}");
            Assert.True(Json.HasProperty(tree, "events"));
            Assert.False(Json.HasProperty(tree, "Nope"));
            Assert.False(Json.HasProperty(Json.ParseTree("[1]"), "Events"));
        }
    }

    public static class PathsTests
    {
        [Test]
        public static void OverrideControlsDataRootLogsAndRecordings()
        {
            using (var dir = new TempDir())
            {
                try
                {
                    string data = dir.File("data");
                    Paths.Override(data);
                    Assert.Equal(data, Paths.DataRoot);
                    Assert.Equal(Path.Combine(data, "recordings"), Paths.RecordingsFolder);
                    Assert.Equal(dir.File("logs"), Paths.Logs);
                }
                finally { Paths.Override(null); }
            }
        }

        [Test]
        public static void DefaultsLiveUnderLocalAppData()
        {
            string saved = Environment.GetEnvironmentVariable("MK_DATA_ROOT");
            try
            {
                Environment.SetEnvironmentVariable("MK_DATA_ROOT", null);
                Paths.Override(null);
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                Assert.Equal(Path.Combine(local, "Macro Kit", "data"), Paths.DataRoot);
                Assert.Equal(Path.Combine(local, "Macro Kit", "logs"), Paths.Logs);
            }
            finally { Environment.SetEnvironmentVariable("MK_DATA_ROOT", saved); }
        }

        [Test]
        public static void EnvironmentVariableIsUsedWithoutOverride()
        {
            string saved = Environment.GetEnvironmentVariable("MK_DATA_ROOT");
            using (var dir = new TempDir())
            {
                try
                {
                    Paths.Override(null);
                    Environment.SetEnvironmentVariable("MK_DATA_ROOT", dir.File("env-data"));
                    Assert.Equal(dir.File("env-data"), Paths.DataRoot);
                    Assert.Equal(dir.File("logs"), Paths.Logs);
                    Paths.Override(dir.File("flag-data"));
                    Assert.Equal(dir.File("flag-data"), Paths.DataRoot, "--data wins over the environment");
                }
                finally { Paths.Override(null); Environment.SetEnvironmentVariable("MK_DATA_ROOT", saved); }
            }
        }

        [Test]
        public static void LogWritesAndRotatesWithoutThrowing()
        {
            using (var dir = new TempDir())
            {
                try
                {
                    Paths.Override(dir.File("data"));
                    Log.Info("first line");
                    Log.Warn("a warning", new InvalidOperationException("boom"));
                    Log.Error("an error");
                    string text = File.ReadAllText(Log.FilePath);
                    Assert.Contains(text, "INFO first line");
                    Assert.Contains(text, "WARN a warning");
                    Assert.Contains(text, "boom");
                    Assert.Contains(text, "ERROR an error");
                    File.WriteAllText(Log.FilePath, new string('x', 600 * 1024));
                    Log.Info("after rotation");
                    Assert.True(File.Exists(Log.FilePath + ".old"), "rotated");
                    Assert.True(new FileInfo(Log.FilePath).Length < 1000, "fresh log");
                }
                finally { Paths.Override(null); }
            }
        }

        [Test]
        public static void LogNeverThrows()
        {
            using (var dir = new TempDir())
            {
                try
                {
                    // A file where the log folder should be makes every write fail.
                    string data = dir.File("data");
                    File.WriteAllText(dir.File("logs"), "not a folder");
                    Paths.Override(data);
                    Log.Info("still fine");
                }
                finally { Paths.Override(null); }
            }
        }
    }

    public static class StorageTests
    {
        static Storage Open(TempDir dir)
        {
            Paths.Override(dir.File("data"));   // keeps the log out of the real folder
            return new Storage(dir.File("data"));
        }

        static void Run(Action<Storage, TempDir> body)
        {
            using (var dir = new TempDir())
            {
                try { body(Open(dir), dir); }
                finally { Paths.Override(null); }
            }
        }

        static Recording Sample(string name)
        {
            var r = new Recording { Name = name, Created = Recording.NowIso() };
            r.Events.Add(new RecEvent(0, RecKind.Move, 10, 20));
            r.Events.Add(new RecEvent(40, RecKind.KeyDown, 65, 30));
            r.Events.Add(new RecEvent(90, RecKind.KeyUp, 65, 30));
            r.DurationMs = 90;
            return r;
        }

        [Test]
        public static void FirstStartWritesSettingsWithDefaults()
        {
            Run((storage, dir) =>
            {
                AppSettings s = storage.LoadSettings();
                Assert.Equal("Ctrl+Shift+Alt+R", s.RecordHotkey);
                Assert.True(File.Exists(dir.File("data\\settings.json")), "settings.json is written on first start");
                Assert.Contains(File.ReadAllText(dir.File("data\\settings.json")), "\"Schema\": 1");
            });
        }

        [Test]
        public static void SettingsRoundTrip()
        {
            Run((storage, dir) =>
            {
                AppSettings s = storage.LoadSettings();
                s.Theme = "Dark"; s.DeckColumns = 5; s.DeckLeft = 12.5; s.Playback.Speed = 2.0; s.Playback.Repeat = RepeatMode.Continuous;
                s.ExcludedPrograms.Add("game.exe"); s.SelectedRecordingId = "abc";
                storage.SaveSettings(s);
                AppSettings back = new Storage(dir.File("data")).LoadSettings();
                Assert.Equal("Dark", back.Theme);
                Assert.Equal(5, back.DeckColumns);
                Assert.Equal(12.5, back.DeckLeft);
                Assert.True(double.IsNaN(back.DeckTop));
                Assert.Equal(2.0, back.Playback.Speed);
                Assert.Equal(RepeatMode.Continuous, back.Playback.Repeat);
                Assert.SequenceEqual(new[] { "game.exe" }, back.ExcludedPrograms);
                Assert.Equal("abc", back.SelectedRecordingId);
            });
        }

        [Test]
        public static void FirstStartSeedsTheShrugPhrase()
        {
            Run((storage, dir) =>
            {
                List<PhraseDef> phrases = storage.LoadPhrases();
                Assert.Equal(1, phrases.Count);
                Assert.Equal("/shrug", phrases[0].Trigger);
                Assert.Equal("¯\\_(ツ)_/¯", phrases[0].Replacement);
                Assert.Equal(ExpandWhen.Immediately, phrases[0].When);
                Assert.Equal(InsertMethod.Type, phrases[0].Method);
                Assert.True(File.Exists(dir.File("data\\phrases.json")));
                Assert.Equal(phrases[0].Id, storage.LoadPhrases()[0].Id, "the seed is written once");
            });
        }

        [Test]
        public static void FirstStartSeedsExampleMacro()
        {
            Run((storage, dir) =>
            {
                List<MacroDef> macros = storage.LoadMacros();
                Assert.Equal(1, macros.Count);
                Assert.Equal("Copy, switch, paste, next row", macros[0].Name);
                Assert.Equal("{{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{CTRL}{V}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{DOWN}}{{PAUSE:50}}", macros[0].Script);
                Assert.Equal(RepeatMode.Times, macros[0].Repeat);
                Assert.Equal(5, macros[0].RepeatTimes);
                Assert.Equal(macros[0].Id, storage.LoadMacros()[0].Id);
            });
        }

        [Test]
        public static void PhrasesAndMacrosRoundTrip()
        {
            Run((storage, dir) =>
            {
                storage.SavePhrases(new[]
                {
                    new PhraseDef { Trigger = "/a", Replacement = "line1\nline2", MatchCase = true, When = ExpandWhen.AfterDelimiter, Method = InsertMethod.Paste, Enabled = false },
                    new PhraseDef { Trigger = "/b", Replacement = "B" },
                });
                var phrases = new Storage(dir.File("data")).LoadPhrases();
                Assert.Equal(2, phrases.Count);
                Assert.Equal("line1\nline2", phrases[0].Replacement);
                Assert.True(phrases[0].MatchCase);
                Assert.False(phrases[0].Enabled);
                Assert.Equal(ExpandWhen.AfterDelimiter, phrases[0].When);
                Assert.Equal(InsertMethod.Paste, phrases[0].Method);

                storage.SaveMacros(new[]
                {
                    new MacroDef { Name = "One", Script = "{{ENTER}}", Repeat = RepeatMode.Continuous, Hotkey = "Ctrl+Alt+1", TargetWindow = new WindowMatch { ProcessName = "Chrome.exe" }, Target = TargetKind.SpecificWindow },
                });
                var macros = new Storage(dir.File("data")).LoadMacros();
                Assert.Equal(1, macros.Count);
                Assert.Equal("Ctrl+Alt+1", macros[0].Hotkey);
                Assert.Equal(RepeatMode.Continuous, macros[0].Repeat);
                Assert.Equal("Chrome.exe", macros[0].TargetWindow.ProcessName);
                Assert.Equal(TargetKind.SpecificWindow, macros[0].Target);
            });
        }

        [Test]
        public static void MissingOrDuplicateIdsAreRepaired()
        {
            Run((storage, dir) =>
            {
                Directory.CreateDirectory(dir.File("data"));
                File.WriteAllText(dir.File("data\\phrases.json"), "{\"Schema\":1,\"Phrases\":[{\"Id\":\"x\",\"Trigger\":\"/a\"},{\"Id\":\"x\",\"Trigger\":\"/b\"},{\"Trigger\":\"/c\"}]}");
                var phrases = storage.LoadPhrases();
                Assert.Equal(3, phrases.Count);
                Assert.Equal(3, phrases.Select(p => p.Id).Distinct().Count());
                Assert.True(phrases.All(p => p.Id.Length > 0));
                Assert.Equal("", phrases[2].Replacement);
            });
        }

        [Test]
        public static void ReplacingAFileKeepsTheOldVersionAsPrevious()
        {
            Run((storage, dir) =>
            {
                string file = dir.File("data\\settings.json");
                AppSettings s = new AppSettings { Theme = "Light" };
                storage.SaveSettings(s);          // new file: nothing to keep
                Assert.False(File.Exists(file + ".previous"));
                s.Theme = "Dark";
                storage.SaveSettings(s);          // replace: the Light version is kept
                Assert.True(File.Exists(file + ".previous"));
                Assert.Contains(File.ReadAllText(file + ".previous"), "\"Theme\": \"Light\"");
                Assert.Contains(File.ReadAllText(file), "\"Theme\": \"Dark\"");
                s.Theme = "System";
                storage.SaveSettings(s);
                Assert.Contains(File.ReadAllText(file + ".previous"), "\"Theme\": \"Dark\"");
                Assert.False(File.Exists(file + ".tmp"), "no leftover temp file");
            });
        }

        [Test]
        public static void FilesAreUtf8WithoutBom()
        {
            Run((storage, dir) =>
            {
                storage.LoadPhrases();
                byte[] bytes = File.ReadAllBytes(dir.File("data\\phrases.json"));
                Assert.True(bytes.Length > 3);
                Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "no BOM");
                Assert.Contains(File.ReadAllText(dir.File("data\\phrases.json"), new System.Text.UTF8Encoding(false)), "¯");
            });
        }

        [Test]
        public static void UnreadableFileIsMovedAsideWithAWarning()
        {
            Run((storage, dir) =>
            {
                Directory.CreateDirectory(dir.File("data"));
                string file = dir.File("data\\macros.json");
                File.WriteAllText(file, "{ this is not json");
                List<MacroDef> macros = storage.LoadMacros();
                Assert.Equal(1, macros.Count);
                Assert.Equal("Copy, switch, paste, next row", macros[0].Name);
                string[] aside = Directory.GetFiles(dir.File("data"), "macros.json.unreadable-*");
                Assert.Equal(1, aside.Length);
                Assert.Equal("{ this is not json", File.ReadAllText(aside[0]), "the user's file is kept untouched");
                Assert.Equal(1, storage.Warnings.Count);
                Assert.Contains(storage.Warnings[0], "macros.json");
                Assert.Contains(File.ReadAllText(file), "Copy, switch, paste, next row");
            });
        }

        [Test]
        public static void WrongTopLevelShapeCountsAsUnreadable()
        {
            Run((storage, dir) =>
            {
                Directory.CreateDirectory(dir.File("data"));
                File.WriteAllText(dir.File("data\\settings.json"), "[1,2,3]");
                AppSettings s = storage.LoadSettings();
                Assert.Equal("System", s.Theme);
                Assert.Equal(1, Directory.GetFiles(dir.File("data"), "settings.json.unreadable-*").Length);
                Assert.Equal(1, storage.Warnings.Count);
            });
        }

        [Test]
        public static void WarningsAreASnapshot()
        {
            Run((storage, dir) =>
            {
                IList<string> before = storage.Warnings;
                Assert.Equal(0, before.Count);
                Directory.CreateDirectory(dir.File("data"));
                File.WriteAllText(dir.File("data\\phrases.json"), "garbage");
                storage.LoadPhrases();
                Assert.Equal(0, before.Count);
                Assert.Equal(1, storage.Warnings.Count);
            });
        }

        // ---- a file that exists but cannot be opened is never overwritten ----

        static FileStream Lock(string path) { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); }

        [Test]
        public static void LockedSettingsAreKeptAndNeverOverwritten()
        {
            Run((storage, dir) =>
            {
                storage.SaveSettings(new AppSettings { Theme = "Dark", DeckColumns = 7 });
                string file = dir.File("data\\settings.json");
                string before = File.ReadAllText(file);

                var fresh = new Storage(dir.File("data"));   // a new session; another program holds the file open
                AppSettings loaded;
                using (Lock(file))
                {
                    loaded = fresh.LoadSettings();
                    Assert.Equal("System", loaded.Theme, "defaults are used in memory");
                    Assert.SequenceEqual(new[] { "settings.json" }, fresh.ReadOnlyFiles);
                    Assert.Equal(1, fresh.Warnings.Count);
                    Assert.Contains(fresh.Warnings[0], "settings.json");
                    Assert.Contains(fresh.Warnings[0], "won't be saved over");
                }

                fresh.SaveSettings(loaded);                  // the lock is gone, but this session still must not write
                Assert.Equal(before, File.ReadAllText(file), "the real file is untouched");
                Assert.False(File.Exists(file + ".previous"), "nothing was rotated either");
                Assert.Equal(0, Directory.GetFiles(dir.File("data"), "settings.json.unreadable-*").Length, "and it was not moved aside");
                Assert.Equal(7, new Storage(dir.File("data")).LoadSettings().DeckColumns, "the next session reads it fine");
            });
        }

        [Test]
        public static void LockedMacrosAndPhrasesAreNotSeededOrSavedOver()
        {
            Run((storage, dir) =>
            {
                storage.SavePhrases(new[] { new PhraseDef { Trigger = "/mine", Replacement = "precious" } });
                storage.SaveMacros(new[] { new MacroDef { Name = "Precious", Script = "{{ENTER}}" } });
                string phrasesFile = dir.File("data\\phrases.json"), macrosFile = dir.File("data\\macros.json");
                string phrasesBefore = File.ReadAllText(phrasesFile), macrosBefore = File.ReadAllText(macrosFile);

                var fresh = new Storage(dir.File("data"));
                using (Lock(phrasesFile))
                using (Lock(macrosFile))
                {
                    List<PhraseDef> phrases = fresh.LoadPhrases();
                    List<MacroDef> macros = fresh.LoadMacros();
                    Assert.Equal("/shrug", phrases[0].Trigger, "the starter phrase stands in, in memory only");
                    Assert.Equal("Copy, switch, paste, next row", macros[0].Name);
                }
                Assert.SequenceEqual(new[] { "macros.json", "phrases.json" }, fresh.ReadOnlyFiles);

                fresh.SavePhrases(new[] { new PhraseDef { Trigger = "/new", Replacement = "x" } });
                fresh.SaveMacros(new[] { new MacroDef { Name = "New" } });
                Assert.Equal(phrasesBefore, File.ReadAllText(phrasesFile));
                Assert.Equal(macrosBefore, File.ReadAllText(macrosFile));
                Assert.Equal("precious", new Storage(dir.File("data")).LoadPhrases()[0].Replacement);
            });
        }

        [Test]
        public static void OneLockedFileDoesNotBlockTheOthers()
        {
            Run((storage, dir) =>
            {
                storage.SaveSettings(new AppSettings { Theme = "Dark" });
                storage.SavePhrases(new[] { new PhraseDef { Trigger = "/a", Replacement = "b" } });
                var fresh = new Storage(dir.File("data"));
                using (Lock(dir.File("data\\settings.json")))
                    fresh.LoadSettings();
                fresh.SavePhrases(new[] { new PhraseDef { Trigger = "/c", Replacement = "d" } });
                Assert.Equal("/c", new Storage(dir.File("data")).LoadPhrases()[0].Trigger, "phrases still save");
                Assert.SequenceEqual(new[] { "settings.json" }, fresh.ReadOnlyFiles);
            });
        }

        [Test]
        public static void ABriefLockIsRetriedAndThenReadNormally()
        {
            Run((storage, dir) =>
            {
                storage.SaveSettings(new AppSettings { Theme = "Dark", DeckColumns = 5 });
                var fresh = new Storage(dir.File("data"));
                FileStream held = Lock(dir.File("data\\settings.json"));
                var releaser = new Thread(() => { Thread.Sleep(60); held.Dispose(); });
                releaser.Start();
                AppSettings loaded = fresh.LoadSettings();
                releaser.Join();
                Assert.Equal("Dark", loaded.Theme, "the real settings were read on a retry");
                Assert.Equal(0, fresh.ReadOnlyFiles.Count);
                Assert.Equal(0, fresh.Warnings.Count);
                fresh.SaveSettings(loaded);   // saving works as usual
                Assert.Equal(5, new Storage(dir.File("data")).LoadSettings().DeckColumns);
            });
        }

        [Test]
        public static void ALockedRecordingIsNotOverwrittenAndNotIndexedAway()
        {
            Run((storage, dir) =>
            {
                Recording r = Sample("Precious steps");
                storage.SaveRecording(r);
                string file = dir.File("data\\recordings\\" + r.Id + ".json");
                string before = File.ReadAllText(file);

                var fresh = new Storage(dir.File("data"));
                using (Lock(file))
                {
                    Assert.Null(fresh.LoadRecording(r.Id), "cannot be opened right now");
                    Assert.True(File.Exists(file), "and the file is still there");
                    Assert.SequenceEqual(new[] { r.Id + ".json" }, fresh.ReadOnlyFiles);
                }
                Recording replacement = Sample("Other");
                replacement.Id = r.Id;
                fresh.SaveRecording(replacement);             // same id: refused for this session
                Assert.Equal(before, File.ReadAllText(file));
                Assert.Equal("Precious steps", new Storage(dir.File("data")).LoadRecording(r.Id).Name);
            });
        }

        [Test]
        public static void ReadOnlyFilesStartEmptyAndAreASnapshot()
        {
            Run((storage, dir) =>
            {
                IList<string> before = storage.ReadOnlyFiles;
                Assert.Equal(0, before.Count);
                storage.SaveSettings(new AppSettings());
                var fresh = new Storage(dir.File("data"));
                IList<string> none = fresh.ReadOnlyFiles;
                using (Lock(dir.File("data\\settings.json"))) fresh.LoadSettings();
                Assert.Equal(0, none.Count, "an earlier snapshot does not change");
                Assert.Equal(1, fresh.ReadOnlyFiles.Count);
            });
        }

        [Test]
        public static void AMissingFileIsStillSeededNormally()
        {
            Run((storage, dir) =>
            {
                storage.LoadSettings();
                storage.LoadPhrases();
                storage.LoadMacros();
                Assert.True(File.Exists(dir.File("data\\settings.json")));
                Assert.True(File.Exists(dir.File("data\\phrases.json")));
                Assert.True(File.Exists(dir.File("data\\macros.json")));
                Assert.Equal(0, storage.ReadOnlyFiles.Count);
                Assert.Equal(0, storage.Warnings.Count);
            });
        }

        [Test]
        public static void RecordingSaveLoadAndIndex()
        {
            Run((storage, dir) =>
            {
                Assert.Equal(0, storage.LoadRecordingIndex().Count);
                Recording r = Sample("Login steps");
                storage.SaveRecording(r);
                Assert.True(File.Exists(dir.File("data\\recordings\\" + r.Id + ".json")));
                var index = storage.LoadRecordingIndex();
                Assert.Equal(1, index.Count);
                Assert.Equal("Login steps", index[0].Name);
                Assert.Equal(3, index[0].EventCount);
                Assert.Equal(90, index[0].DurationMs);
                Recording back = storage.LoadRecording(r.Id);
                Assert.Equal(3, back.Events.Count);
                Assert.Equal(RecKind.KeyDown, back.Events[1].Kind);
                Assert.Equal(65, back.Events[1].A);
                Assert.Equal(r.Created, back.Created);
                Assert.Null(storage.LoadRecording("does-not-exist"));
            });
        }

        [Test]
        public static void SavingFillsInMissingFields()
        {
            Run((storage, dir) =>
            {
                var r = new Recording { Id = "", Name = "", Created = "" };
                r.Events.Add(new RecEvent(250, RecKind.Wheel, 120, 0));
                storage.SaveRecording(r);
                Assert.Equal(32, r.Id.Length);
                Assert.True(r.Name.StartsWith("Recording "));
                Assert.True(r.Created.Length > 10);
                Assert.Equal(250, r.DurationMs);
            });
        }

        [Test]
        public static void SavingAgainReplacesTheIndexEntry()
        {
            Run((storage, dir) =>
            {
                Recording a = Sample("A");
                Recording b = Sample("B");
                a.Created = "2026-11-12T08:00:00-05:00";   // distinct times: the index is sorted by creation time, newest first
                b.Created = "2026-11-12T08:00:05-05:00";
                storage.SaveRecording(a);
                storage.SaveRecording(b);
                Assert.Equal("B", storage.LoadRecordingIndex()[0].Name, "newest first");
                a.Name = "A renamed";
                a.Events.Add(new RecEvent(100, RecKind.KeyUp, 66, 48));
                storage.SaveRecording(a);
                var index = storage.LoadRecordingIndex();
                Assert.Equal(2, index.Count);
                Assert.Equal(4, index.First(i => i.Id == a.Id).EventCount);
                Assert.Equal("A renamed", index.First(i => i.Id == a.Id).Name);
            });
        }

        [Test]
        public static void RenameAndDelete()
        {
            Run((storage, dir) =>
            {
                Recording r = Sample("Old name");
                storage.SaveRecording(r);
                storage.RenameRecording(r.Id, "  New name ");
                Assert.Equal("New name", storage.LoadRecording(r.Id).Name);
                Assert.Equal("New name", storage.LoadRecordingIndex()[0].Name);
                Assert.Equal(3, storage.LoadRecording(r.Id).Events.Count, "events survive a rename");
                Assert.Throws<FileNotFoundException>(() => storage.RenameRecording("nope", "x"));

                storage.DeleteRecording(r.Id);
                Assert.False(File.Exists(dir.File("data\\recordings\\" + r.Id + ".json")));
                Assert.Equal(0, storage.LoadRecordingIndex().Count);
                Assert.Null(storage.LoadRecording(r.Id));
                storage.DeleteRecording(r.Id);        // deleting twice is fine
                storage.DeleteRecording("..\\..\\x"); // unsafe ids are ignored
            });
        }

        [Test]
        public static void UnsafeIdsNeverTouchOtherFolders()
        {
            Run((storage, dir) =>
            {
                File.WriteAllText(dir.File("outside.json"), "{}");
                Assert.Null(storage.LoadRecording("..\\outside"));
                Assert.Null(storage.LoadRecording("../outside"));
                Assert.Throws<FileNotFoundException>(() => storage.ExportRecording("..\\outside", dir.File("x.json")));
                var r = new Recording { Id = "..\\..\\evil", Name = "Evil" };
                storage.SaveRecording(r);
                Assert.True(r.Id.Length == 32 && !r.Id.Contains("."), "an unsafe id is replaced");
                Assert.True(File.Exists(dir.File("outside.json")));
            });
        }

        [Test]
        public static void ExportAndImport()
        {
            Run((storage, dir) =>
            {
                Recording r = Sample("Share me");
                storage.SaveRecording(r);
                string file = dir.File("export\\share.json");
                storage.ExportRecording(r.Id, file);
                Assert.True(File.Exists(file));

                Recording imported = storage.ImportRecording(file);
                Assert.False(imported.Id == r.Id, "import makes a new Id");
                Assert.Equal("Share me", imported.Name);
                Assert.Equal(3, imported.Events.Count);
                Assert.Equal(2, storage.LoadRecordingIndex().Count);
                Assert.Equal(3, storage.LoadRecording(imported.Id).Events.Count);
            });
        }

        [Test]
        public static void ImportRejectsOtherFiles()
        {
            Run((storage, dir) =>
            {
                File.WriteAllText(dir.File("a.json"), "not json at all");
                File.WriteAllText(dir.File("b.json"), "{\"Name\":\"x\"}");
                File.WriteAllText(dir.File("c.json"), "[1,2]");
                File.WriteAllText(dir.File("d.json"), "{\"Schema\":9,\"Events\":[]}");
                foreach (string name in new[] { "a.json", "b.json", "c.json" })
                {
                    var e = Assert.Throws<InvalidDataException>(() => storage.ImportRecording(dir.File(name)));
                    Assert.Contains(e.Message, "not a Macro Kit recording", name);
                }
                var newer = Assert.Throws<InvalidDataException>(() => storage.ImportRecording(dir.File("d.json")));
                Assert.Contains(newer.Message, "newer version");
                Assert.Throws<FileNotFoundException>(() => storage.ImportRecording(dir.File("missing.json")));
                Assert.Equal(0, storage.LoadRecordingIndex().Count);
            });
        }

        [Test]
        public static void MissingIndexIsRebuiltFromTheFiles()
        {
            Run((storage, dir) =>
            {
                Recording a = Sample("Older"); a.Created = "2026-11-10T09:00:00-05:00";
                Recording b = Sample("Newer"); b.Created = "2026-11-12T09:00:00-05:00";
                storage.SaveRecording(a);
                storage.SaveRecording(b);
                File.Delete(dir.File("data\\recordings.json"));
                var index = new Storage(dir.File("data")).LoadRecordingIndex();
                Assert.SequenceEqual(new[] { "Newer", "Older" }, index.Select(i => i.Name).ToList());
                Assert.Equal(3, index[0].EventCount);
                Assert.True(File.Exists(dir.File("data\\recordings.json")), "rebuilt index is saved");
            });
        }

        [Test]
        public static void IndexThatDisagreesIsRebuilt()
        {
            Run((storage, dir) =>
            {
                Recording a = Sample("Kept");
                storage.SaveRecording(a);
                // A recording file added behind the index's back, and an index entry whose file is gone.
                Recording b = Sample("Added by hand");
                File.WriteAllText(dir.File("data\\recordings\\" + b.Id + ".json"), Json.Serialize(b));
                File.WriteAllText(dir.File("data\\recordings.json"),
                    "{\"Schema\":1,\"Recordings\":[{\"Id\":\"" + a.Id + "\",\"Name\":\"Kept\"},{\"Id\":\"ghost\",\"Name\":\"Ghost\"}]}");
                var index = new Storage(dir.File("data")).LoadRecordingIndex();
                Assert.Equal(2, index.Count);
                Assert.True(index.Any(i => i.Name == "Kept"));
                Assert.True(index.Any(i => i.Name == "Added by hand"));
                Assert.False(index.Any(i => i.Name == "Ghost"));
                Assert.Equal(3, index.First(i => i.Name == "Kept").EventCount, "rebuilt entries are complete");
            });
        }

        [Test]
        public static void UnreadableIndexIsSetAsideAndRebuilt()
        {
            Run((storage, dir) =>
            {
                Recording a = Sample("Survivor");
                storage.SaveRecording(a);
                File.WriteAllText(dir.File("data\\recordings.json"), "}}}}");
                var fresh = new Storage(dir.File("data"));
                var index = fresh.LoadRecordingIndex();
                Assert.Equal(1, index.Count);
                Assert.Equal("Survivor", index[0].Name);
                Assert.Equal(1, Directory.GetFiles(dir.File("data"), "recordings.json.unreadable-*").Length);
                Assert.Equal(1, fresh.Warnings.Count);
            });
        }

        [Test]
        public static void UnreadableRecordingFileIsSetAside()
        {
            Run((storage, dir) =>
            {
                Recording good = Sample("Good");
                Recording bad = Sample("Bad");
                storage.SaveRecording(good);
                storage.SaveRecording(bad);
                string badFile = dir.File("data\\recordings\\" + bad.Id + ".json");
                File.WriteAllText(badFile, "garbage");
                var fresh = new Storage(dir.File("data"));
                Assert.Null(fresh.LoadRecording(bad.Id));
                Assert.Equal(1, Directory.GetFiles(dir.File("data\\recordings"), bad.Id + ".json.unreadable-*").Length);
                Assert.Equal(1, fresh.Warnings.Count);
                var index = fresh.LoadRecordingIndex();
                Assert.Equal(1, index.Count);
                Assert.Equal("Good", index[0].Name);
            });
        }

        [Test]
        public static void AbandonedTempAndPreviousFilesAreNotRecordings()
        {
            Run((storage, dir) =>
            {
                Recording a = Sample("Only one");
                storage.SaveRecording(a);
                storage.SaveRecording(a);   // creates a .previous
                File.WriteAllText(dir.File("data\\recordings\\stray.json.tmp"), "x");
                Assert.True(File.Exists(dir.File("data\\recordings\\" + a.Id + ".json.previous")));
                Assert.Equal(1, new Storage(dir.File("data")).LoadRecordingIndex().Count);
            });
        }

        [Test]
        public static void ConcurrentSavesDoNotCorruptTheIndex()
        {
            Run((storage, dir) =>
            {
                var threads = new List<Thread>();
                for (int t = 0; t < 4; t++)
                {
                    int id = t;
                    var thread = new Thread(() =>
                    {
                        for (int i = 0; i < 5; i++) storage.SaveRecording(Sample("T" + id + "-" + i));
                    });
                    threads.Add(thread);
                    thread.Start();
                }
                foreach (Thread thread in threads) thread.Join();
                Assert.Equal(20, new Storage(dir.File("data")).LoadRecordingIndex().Count);
                Assert.Equal(0, storage.Warnings.Count);
            });
        }

        [Test]
        public static void ParameterlessStorageUsesTheDataRoot()
        {
            using (var dir = new TempDir())
            {
                try
                {
                    Paths.Override(dir.File("data"));
                    var storage = new Storage();
                    Assert.Equal(dir.File("data"), storage.DataRoot);
                    storage.LoadSettings();
                    Assert.True(File.Exists(dir.File("data\\settings.json")));
                }
                finally { Paths.Override(null); }
            }
        }
    }
}
