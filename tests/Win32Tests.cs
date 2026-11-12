using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MacroKit.Core;
using MacroKit.Win32;

namespace MacroKit.Tests
{
    /// <summary>
    /// Win32 layer tests. Safe by design: they only look at struct layouts and run the pure helpers; nothing here installs a hook,
    /// sends input, registers a hotkey, touches the clipboard or the registry, or changes focus.
    /// </summary>
    public static class Win32Tests
    {
        // A fake scan code lookup so the tests do not depend on the keyboard layout of the machine running them.
        static ushort FakeScan(ushort vk) { return (ushort)(KeyNames.BaseVk(vk) + 1); }

        static string Describe(Native.INPUT p)
        {
            if (p.type == Native.INPUT_KEYBOARD)
                return "key vk=" + p.ki.wVk + " scan=" + p.ki.wScan + " flags=0x" + p.ki.dwFlags.ToString("X");
            return "mouse flags=0x" + p.mi.dwFlags.ToString("X") + " data=" + p.mi.mouseData + " dx=" + p.mi.dx + " dy=" + p.mi.dy;
        }

        // ---------------- struct layout (x64) ----------------

        [Test]
        public static void StructSizesMatchWindowsX64()
        {
            Assert.Equal(8, IntPtr.Size);
            Assert.Equal(40, Marshal.SizeOf(typeof(Native.INPUT)));
            Assert.Equal(24, Marshal.SizeOf(typeof(Native.KBDLLHOOKSTRUCT)));
            Assert.Equal(32, Marshal.SizeOf(typeof(Native.MSLLHOOKSTRUCT)));
            Assert.Equal(32, Marshal.SizeOf(typeof(Native.MOUSEINPUT)));
            Assert.Equal(24, Marshal.SizeOf(typeof(Native.KEYBDINPUT)));
            Assert.Equal(8, Marshal.SizeOf(typeof(Native.POINT)));
        }

        [Test]
        public static void StructFieldOffsetsMatchWindowsX64()
        {
            Func<Type, string, int> offset = (t, f) => (int)Marshal.OffsetOf(t, f);
            Assert.Equal(0, offset(typeof(Native.INPUT), "type"));
            Assert.Equal(8, offset(typeof(Native.INPUT), "mi"));
            Assert.Equal(8, offset(typeof(Native.INPUT), "ki"));
            Assert.Equal(0, offset(typeof(Native.KEYBDINPUT), "wVk"));
            Assert.Equal(2, offset(typeof(Native.KEYBDINPUT), "wScan"));
            Assert.Equal(4, offset(typeof(Native.KEYBDINPUT), "dwFlags"));
            Assert.Equal(8, offset(typeof(Native.KEYBDINPUT), "time"));
            Assert.Equal(16, offset(typeof(Native.KEYBDINPUT), "dwExtraInfo"));
            Assert.Equal(0, offset(typeof(Native.MOUSEINPUT), "dx"));
            Assert.Equal(4, offset(typeof(Native.MOUSEINPUT), "dy"));
            Assert.Equal(8, offset(typeof(Native.MOUSEINPUT), "mouseData"));
            Assert.Equal(12, offset(typeof(Native.MOUSEINPUT), "dwFlags"));
            Assert.Equal(16, offset(typeof(Native.MOUSEINPUT), "time"));
            Assert.Equal(24, offset(typeof(Native.MOUSEINPUT), "dwExtraInfo"));
            Assert.Equal(0, offset(typeof(Native.KBDLLHOOKSTRUCT), "vkCode"));
            Assert.Equal(8, offset(typeof(Native.KBDLLHOOKSTRUCT), "flags"));
            Assert.Equal(16, offset(typeof(Native.KBDLLHOOKSTRUCT), "dwExtraInfo"));
            Assert.Equal(8, offset(typeof(Native.MSLLHOOKSTRUCT), "mouseData"));
            Assert.Equal(12, offset(typeof(Native.MSLLHOOKSTRUCT), "flags"));
            Assert.Equal(24, offset(typeof(Native.MSLLHOOKSTRUCT), "dwExtraInfo"));
        }

        [Test]
        public static void FlagConstantsMatchWindowsHeaders()
        {
            Assert.Equal(1u, Native.KEYEVENTF_EXTENDEDKEY);
            Assert.Equal(2u, Native.KEYEVENTF_KEYUP);
            Assert.Equal(4u, Native.KEYEVENTF_UNICODE);
            Assert.Equal(0x0800u, Native.MOUSEEVENTF_WHEEL);
            Assert.Equal(0x1000u, Native.MOUSEEVENTF_HWHEEL);
            Assert.Equal(0x8000u, Native.MOUSEEVENTF_ABSOLUTE);
            Assert.Equal(0x10u, Native.LLKHF_INJECTED);
            Assert.Equal(1u, Native.LLMHF_INJECTED);
            Assert.Equal(0x4D4B4954u, (uint)Native.ExtraInfo);
        }

        // ---------------- key packets ----------------

        [Test]
        public static void KeyPacketCarriesScanCodeAndMarker()
        {
            Native.INPUT down = InputPackets.Key(0x43, true, 0x2E);
            Assert.Equal(Native.INPUT_KEYBOARD, down.type);
            Assert.Equal((ushort)0x43, down.ki.wVk);
            Assert.Equal((ushort)0x2E, down.ki.wScan);
            Assert.Equal(0u, down.ki.dwFlags);
            Assert.Equal(Native.ExtraInfo, down.ki.dwExtraInfo);
            Native.INPUT up = InputPackets.Key(0x43, false, 0x2E);
            Assert.Equal(Native.KEYEVENTF_KEYUP, up.ki.dwFlags);
        }

        [Test]
        public static void ExtendedKeysGetTheExtendedFlag()
        {
            // arrows, navigation block, right Ctrl/Alt, both Win keys, Apps
            ushort[] extended = { 0x25, 0x26, 0x27, 0x28, 0x21, 0x22, 0x23, 0x24, 0x2D, 0x2E, 0xA3, 0xA5, 0x5B, 0x5C, 0x5D, 0x6F };
            foreach (ushort vk in extended)
            {
                Native.INPUT down = InputPackets.Key(vk, true, 1);
                Assert.Equal(Native.KEYEVENTF_EXTENDEDKEY, down.ki.dwFlags, "down flags of " + KeyNames.MacroToken(vk));
                Native.INPUT up = InputPackets.Key(vk, false, 1);
                Assert.Equal(Native.KEYEVENTF_EXTENDEDKEY | Native.KEYEVENTF_KEYUP, up.ki.dwFlags, "up flags of " + KeyNames.MacroToken(vk));
            }
            ushort[] plain = { 0x41, 0x0D, 0x09, 0x1B, 0x20, 0xA0, 0xA2, 0xA4, 0x70, 0x60 };
            foreach (ushort vk in plain)
                Assert.Equal(0u, InputPackets.Key(vk, true, 1).ki.dwFlags, "flags of " + KeyNames.MacroToken(vk));
        }

        [Test]
        public static void NumpadNavigationKeysAreNotExtended()
        {
            ushort home;
            Assert.True(KeyNames.TryGetVk("NUMPAD_HOME", out home));
            Native.INPUT down = InputPackets.Key(home, true, 0x47);
            Assert.Equal((ushort)0x24, down.ki.wVk, "the force bit must not reach Windows");
            Assert.Equal(0u, down.ki.dwFlags);
            ushort delKey;
            Assert.True(KeyNames.TryGetVk("NUMPAD_DEL", out delKey));
            Assert.Equal((ushort)0x2E, InputPackets.Key(delKey, false, 0x53).ki.wVk);
            // the real navigation key with the same VK is extended
            Assert.Equal(Native.KEYEVENTF_EXTENDEDKEY, InputPackets.Key(0x24, true, 0x47).ki.dwFlags);
        }

        [Test]
        public static void ComboPressesInOrderAndReleasesInReverse()
        {
            var keys = new List<ushort> { 0xA2, 0xA0, 0x1B };   // Ctrl+Shift+Esc
            Native.INPUT[] downs = InputPackets.ComboDown(keys, FakeScan);
            Native.INPUT[] ups = InputPackets.ComboUp(keys, FakeScan);
            Assert.SequenceEqual(new ushort[] { 0xA2, 0xA0, 0x1B }, downs.Select(p => p.ki.wVk));
            Assert.SequenceEqual(new ushort[] { 0x1B, 0xA0, 0xA2 }, ups.Select(p => p.ki.wVk));
            Assert.True(downs.All(p => (p.ki.dwFlags & Native.KEYEVENTF_KEYUP) == 0), "downs are all key-downs");
            Assert.True(ups.All(p => (p.ki.dwFlags & Native.KEYEVENTF_KEYUP) != 0), "ups are all key-ups");
            Assert.SequenceEqual(new ushort[] { 0xA3, 0xA1, 0x1C }, downs.Select(p => p.ki.wScan));
        }

        [Test]
        public static void ComboWithRightAndWinKeysUsesTheExtendedFlagOnlyWhereNeeded()
        {
            var keys = new List<ushort> { 0x5B, 0xA3, 0x45 };   // LWin, RCtrl, E
            Native.INPUT[] downs = InputPackets.ComboDown(keys, FakeScan);
            Assert.SequenceEqual(new[] { 1u, 1u, 0u }, downs.Select(p => p.ki.dwFlags & Native.KEYEVENTF_EXTENDEDKEY));
        }

        // ---------------- typing text ----------------

        [Test]
        public static void PlainTextIsUnicodePairs()
        {
            List<Native.INPUT[]> batches = InputPackets.TextBatches("Hi", FakeScan);
            Assert.Equal(1, batches.Count);
            Native.INPUT[] p = batches[0];
            Assert.Equal(4, p.Length);
            Assert.SequenceEqual(new ushort[] { 'H', 'H', 'i', 'i' }, p.Select(x => x.ki.wScan));
            Assert.True(p.All(x => x.ki.wVk == 0), "unicode packets have no virtual key");
            Assert.SequenceEqual(new[] { Native.KEYEVENTF_UNICODE, Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP,
                Native.KEYEVENTF_UNICODE, Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP }, p.Select(x => x.ki.dwFlags));
            Assert.True(p.All(x => x.ki.dwExtraInfo == Native.ExtraInfo), "every packet is stamped MKIT");
        }

        [Test]
        public static void ShrugTypesEveryCharacterIncludingTheBackslash()
        {
            string shrug = "\u00AF\\_(\u30C4)_/\u00AF";
            List<Native.INPUT[]> batches = InputPackets.TextBatches(shrug, FakeScan);
            Assert.Equal(1, batches.Count);
            Assert.SequenceEqual(shrug.ToCharArray().Select(c => (ushort)c), batches[0].Where((x, i) => i % 2 == 0).Select(x => x.ki.wScan));
        }

        [Test]
        public static void NewlineAndTabArePressedAsRealKeysAndCarriageReturnIsDropped()
        {
            Native.INPUT[] p = InputPackets.TextBatches("a\r\nb\tc", FakeScan).Single();
            // a(2) Enter(2) b(2) Tab(2) c(2)
            Assert.Equal(10, p.Length);
            Assert.Equal((ushort)Native.VK_RETURN, p[2].ki.wVk);
            Assert.Equal((ushort)(Native.VK_RETURN + 1), p[2].ki.wScan);
            Assert.Equal(0u, p[2].ki.dwFlags);
            Assert.Equal(Native.KEYEVENTF_KEYUP, p[3].ki.dwFlags);
            Assert.Equal((ushort)Native.VK_TAB, p[6].ki.wVk);
            Assert.Equal((ushort)'c', p[8].ki.wScan);
            Assert.Equal(0, InputPackets.TextBatches("\r\r", FakeScan).Count);
            Assert.Equal(0, InputPackets.TextBatches("", FakeScan).Count);
            Assert.Equal(0, InputPackets.TextBatches(null, FakeScan).Count);
        }

        [Test]
        public static void SurrogatePairIsSentAsTwoUnitsHighFirst()
        {
            string emoji = "\U0001F600";   // D83D DE00
            Native.INPUT[] p = InputPackets.TextBatches(emoji, FakeScan).Single();
            Assert.Equal(4, p.Length);
            Assert.SequenceEqual(new ushort[] { 0xD83D, 0xD83D, 0xDE00, 0xDE00 }, p.Select(x => x.ki.wScan));
        }

        [Test]
        public static void LongTextIsSplitIntoBatchesOfAtMost64Characters()
        {
            string text = new string('x', 64 * 2 + 5);
            List<Native.INPUT[]> batches = InputPackets.TextBatches(text, FakeScan);
            Assert.Equal(3, batches.Count);
            Assert.Equal(128, batches[0].Length);
            Assert.Equal(128, batches[1].Length);
            Assert.Equal(10, batches[2].Length);
            Assert.Equal(text.Length * 2, batches.Sum(b => b.Length));
        }

        [Test]
        public static void BatchesNeverSplitASurrogatePair()
        {
            string text = new string('x', 63) + "\U0001F600" + "y";   // the pair would straddle character 64
            List<Native.INPUT[]> batches = InputPackets.TextBatches(text, FakeScan);
            Assert.Equal(2, batches.Count);
            Assert.Equal(126, batches[0].Length);
            Assert.SequenceEqual(new ushort[] { 0xD83D, 0xD83D, 0xDE00, 0xDE00, 'y', 'y' }, batches[1].Select(x => x.ki.wScan));
        }

        [Test]
        public static void EnterCountsTowardTheBatchLimit()
        {
            string text = new string('\n', 70);
            List<Native.INPUT[]> batches = InputPackets.TextBatches(text, FakeScan);
            Assert.Equal(2, batches.Count);
            Assert.Equal(128, batches[0].Length);
            Assert.Equal(12, batches[1].Length);
        }

        // ---------------- mouse packets ----------------

        [Test]
        public static void MouseButtonPackets()
        {
            Assert.Equal(Native.MOUSEEVENTF_LEFTDOWN, InputPackets.MouseButtonEvent(MouseButton.Left, true).mi.dwFlags);
            Assert.Equal(Native.MOUSEEVENTF_LEFTUP, InputPackets.MouseButtonEvent(MouseButton.Left, false).mi.dwFlags);
            Assert.Equal(Native.MOUSEEVENTF_RIGHTDOWN, InputPackets.MouseButtonEvent(MouseButton.Right, true).mi.dwFlags);
            Assert.Equal(Native.MOUSEEVENTF_RIGHTUP, InputPackets.MouseButtonEvent(MouseButton.Right, false).mi.dwFlags);
            Assert.Equal(Native.MOUSEEVENTF_MIDDLEDOWN, InputPackets.MouseButtonEvent(MouseButton.Middle, true).mi.dwFlags);
            Assert.Equal(Native.MOUSEEVENTF_MIDDLEUP, InputPackets.MouseButtonEvent(MouseButton.Middle, false).mi.dwFlags);
            Native.INPUT x1 = InputPackets.MouseButtonEvent(MouseButton.X1, true);
            Assert.Equal(Native.MOUSEEVENTF_XDOWN, x1.mi.dwFlags);
            Assert.Equal(1u, x1.mi.mouseData);
            Native.INPUT x2 = InputPackets.MouseButtonEvent(MouseButton.X2, false);
            Assert.Equal(Native.MOUSEEVENTF_XUP, x2.mi.dwFlags);
            Assert.Equal(2u, x2.mi.mouseData);
            Assert.Equal(Native.INPUT_MOUSE, x1.type);
            Assert.Equal(Native.ExtraInfo, x1.mi.dwExtraInfo);
            Assert.Equal(0u, InputPackets.MouseButtonEvent(MouseButton.Left, true).mi.mouseData);
        }

        [Test]
        public static void WheelPackets()
        {
            Native.INPUT up = InputPackets.Wheel(120, false);
            Assert.Equal(Native.MOUSEEVENTF_WHEEL, up.mi.dwFlags);
            Assert.Equal(120u, up.mi.mouseData);
            Native.INPUT down = InputPackets.Wheel(-120, false);
            Assert.Equal(unchecked((uint)-120), down.mi.mouseData);
            Assert.Equal(-120, unchecked((int)down.mi.mouseData));
            Native.INPUT right = InputPackets.Wheel(240, true);
            Assert.Equal(Native.MOUSEEVENTF_HWHEEL, right.mi.dwFlags);
            Assert.Equal(240u, right.mi.mouseData);
            Assert.Equal(Native.ExtraInfo, right.mi.dwExtraInfo);
        }

        [Test]
        public static void NormalizedMoveIsAbsoluteAndClamped()
        {
            Native.INPUT p = InputPackets.MoveNormalized(32768, 100);
            Assert.Equal(Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE, p.mi.dwFlags);
            Assert.Equal(32768, p.mi.dx);
            Assert.Equal(100, p.mi.dy);
            Native.INPUT clamped = InputPackets.MoveNormalized(-5, 70000);
            Assert.Equal(0, clamped.mi.dx);
            Assert.Equal(65535, clamped.mi.dy);
        }

        // ---------------- hook decoding ----------------

        [Test]
        public static void KeyboardHookStructBecomesAKeyEvent()
        {
            var raw = new Native.KBDLLHOOKSTRUCT { vkCode = 0x28, scanCode = 0x50, flags = Native.LLKHF_EXTENDED };
            KeyEvent down = HookDecoding.DecodeKey(raw, Native.WM_KEYDOWN, 1234);
            Assert.Equal((ushort)0x28, down.Vk);
            Assert.Equal((ushort)0x50, down.Scan);
            Assert.True(down.Down);
            Assert.True(down.Extended);
            Assert.False(down.Injected);
            Assert.Equal(1234L, down.TimeMs);
            Assert.False(HookDecoding.DecodeKey(raw, Native.WM_KEYUP, 0).Down);
            Assert.True(HookDecoding.DecodeKey(raw, Native.WM_SYSKEYDOWN, 0).Down, "Alt combinations arrive as SYSKEYDOWN");
            Assert.False(HookDecoding.DecodeKey(raw, Native.WM_SYSKEYUP, 0).Down);
        }

        [Test]
        public static void InjectedKeysAreRecognisedIncludingLowerIntegrity()
        {
            var injected = new Native.KBDLLHOOKSTRUCT { vkCode = 0x41, flags = Native.LLKHF_INJECTED };
            var lower = new Native.KBDLLHOOKSTRUCT { vkCode = 0x41, flags = Native.LLKHF_LOWER_IL_INJECTED };
            var physical = new Native.KBDLLHOOKSTRUCT { vkCode = 0x41, flags = 0 };
            Assert.True(HookDecoding.DecodeKey(injected, Native.WM_KEYDOWN, 0).Injected);
            Assert.True(HookDecoding.DecodeKey(lower, Native.WM_KEYDOWN, 0).Injected);
            Assert.False(HookDecoding.DecodeKey(physical, Native.WM_KEYDOWN, 0).Injected);
        }

        static MouseEvent Mouse(int message, uint data = 0, uint flags = 0, int x = 10, int y = 20)
        {
            var raw = new Native.MSLLHOOKSTRUCT { pt = new Native.POINT { X = x, Y = y }, mouseData = data, flags = flags };
            MouseEvent e;
            Assert.True(HookDecoding.DecodeMouse(raw, message, 7, out e), "message 0x" + message.ToString("X") + " decodes");
            return e;
        }

        [Test]
        public static void MouseHookMessagesBecomeMouseEvents()
        {
            MouseEvent move = Mouse(Native.WM_MOUSEMOVE, x: -300, y: 1500);
            Assert.Equal(MouseEventKind.Move, move.Kind);
            Assert.Equal(-300, move.X);
            Assert.Equal(1500, move.Y);
            Assert.Equal(7L, move.TimeMs);

            Assert.Equal(MouseEventKind.Down, Mouse(Native.WM_LBUTTONDOWN).Kind);
            Assert.Equal(MouseButton.Left, Mouse(Native.WM_LBUTTONDOWN).Button);
            Assert.Equal(MouseEventKind.Up, Mouse(Native.WM_LBUTTONUP).Kind);
            Assert.Equal(MouseButton.Right, Mouse(Native.WM_RBUTTONDOWN).Button);
            Assert.Equal(MouseButton.Right, Mouse(Native.WM_RBUTTONUP).Button);
            Assert.Equal(MouseButton.Middle, Mouse(Native.WM_MBUTTONDOWN).Button);
            Assert.Equal(MouseEventKind.Up, Mouse(Native.WM_MBUTTONUP).Kind);
        }

        [Test]
        public static void SideButtonAndWheelDataComeFromTheHighWord()
        {
            Assert.Equal(MouseButton.X1, Mouse(Native.WM_XBUTTONDOWN, 1u << 16).Button);
            Assert.Equal(MouseButton.X2, Mouse(Native.WM_XBUTTONUP, 2u << 16).Button);
            Assert.Equal(MouseEventKind.Up, Mouse(Native.WM_XBUTTONUP, 2u << 16).Kind);
            MouseEvent ignored;
            Assert.False(HookDecoding.DecodeMouse(new Native.MSLLHOOKSTRUCT { mouseData = 3u << 16 }, Native.WM_XBUTTONDOWN, 0, out ignored), "unknown side button");

            MouseEvent up = Mouse(Native.WM_MOUSEWHEEL, 120u << 16);
            Assert.Equal(MouseEventKind.Wheel, up.Kind);
            Assert.Equal(120, up.Delta);
            Assert.Equal(-120, Mouse(Native.WM_MOUSEWHEEL, unchecked((uint)(ushort)-120) << 16).Delta);
            MouseEvent right = Mouse(Native.WM_MOUSEHWHEEL, 240u << 16);
            Assert.Equal(MouseEventKind.HWheel, right.Kind);
            Assert.Equal(240, right.Delta);
            Assert.False(HookDecoding.DecodeMouse(new Native.MSLLHOOKSTRUCT(), 0x0999, 0, out ignored), "unrelated message");
        }

        [Test]
        public static void InjectedMouseEventsAreRecognised()
        {
            Assert.True(Mouse(Native.WM_MOUSEMOVE, 0, Native.LLMHF_INJECTED).Injected);
            Assert.True(Mouse(Native.WM_MOUSEMOVE, 0, Native.LLMHF_LOWER_IL_INJECTED).Injected);
            Assert.False(Mouse(Native.WM_MOUSEMOVE, 0, 0).Injected);
        }

        // ---------------- dead keys ----------------

        [Test]
        public static void DeadKeysComposeWithTheNextCharacter()
        {
            Assert.Equal("\u00E9", HookDecoding.ComposeDead('\u00B4', "e"));
            Assert.Equal("\u00C9", HookDecoding.ComposeDead('\u00B4', "E"));
            Assert.Equal("\u00E0", HookDecoding.ComposeDead('`', "a"));
            Assert.Equal("\u00F1", HookDecoding.ComposeDead('~', "n"));
            Assert.Equal("\u00FC", HookDecoding.ComposeDead('\u00A8', "u"));
            Assert.Equal("\u00EA", HookDecoding.ComposeDead('^', "e"));
        }

        [Test]
        public static void DeadKeyThatCannotComposeKeepsTheAccent()
        {
            Assert.Equal("\u00B4x", HookDecoding.ComposeDead('\u00B4', "x"));
            Assert.Equal("^", HookDecoding.ComposeDead('^', " "), "accent plus space is the accent alone");
            Assert.Equal("\u00B4", HookDecoding.ComposeDead('\u00B4', null));
            Assert.Equal("\u00B4\r", HookDecoding.ComposeDead('\u00B4', "\r"));
        }

        // ---------------- hotkeys ----------------

        [Test]
        public static void HotkeyModifiersConvertToModFlagsWithNoRepeat()
        {
            Assert.Equal(Native.MOD_NOREPEAT, HookDecoding.ToNativeModifiers(HotkeyModifiers.None));
            Assert.Equal(Native.MOD_CONTROL | Native.MOD_NOREPEAT, HookDecoding.ToNativeModifiers(HotkeyModifiers.Ctrl));
            Assert.Equal(Native.MOD_ALT | Native.MOD_NOREPEAT, HookDecoding.ToNativeModifiers(HotkeyModifiers.Alt));
            Assert.Equal(Native.MOD_SHIFT | Native.MOD_NOREPEAT, HookDecoding.ToNativeModifiers(HotkeyModifiers.Shift));
            Assert.Equal(Native.MOD_WIN | Native.MOD_NOREPEAT, HookDecoding.ToNativeModifiers(HotkeyModifiers.Win));
            Hotkey record;
            Assert.True(Hotkey.TryParse("Ctrl+Shift+Alt+R", out record));
            Assert.Equal(0x4000u | 0x1u | 0x2u | 0x4u, HookDecoding.ToNativeModifiers(record.Modifiers));
            Assert.Equal(0x4000u | 0xFu, HookDecoding.ToNativeModifiers(HotkeyModifiers.Alt | HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Win));
            Assert.Equal(1u, Native.MOD_ALT);
            Assert.Equal(2u, Native.MOD_CONTROL);
            Assert.Equal(4u, Native.MOD_SHIFT);
            Assert.Equal(8u, Native.MOD_WIN);
        }

        // ---------------- switchable window filter ----------------

        static WindowFacts Normal(string title = "Book1 - Excel")
        {
            return new WindowFacts { Handle = new IntPtr(100), Visible = true, Title = title, ClassName = "XLMAIN", ExStyle = 0 };
        }

        [Test]
        public static void OrdinaryTopLevelWindowIsSwitchable()
        {
            Assert.True(WindowFilter.IsSwitchable(Normal()));
        }

        [Test]
        public static void HiddenUntitledCloakedAndOwnWindowsAreNotSwitchable()
        {
            WindowFacts hidden = Normal(); hidden.Visible = false;
            WindowFacts untitled = Normal("");
            WindowFacts nullTitle = Normal(null);
            WindowFacts cloaked = Normal(); cloaked.Cloaked = true;
            WindowFacts own = Normal(); own.OwnProcess = true;
            Assert.False(WindowFilter.IsSwitchable(hidden), "hidden");
            Assert.False(WindowFilter.IsSwitchable(untitled), "no title");
            Assert.False(WindowFilter.IsSwitchable(nullTitle), "null title");
            Assert.False(WindowFilter.IsSwitchable(cloaked), "cloaked (other desktop or suspended app)");
            Assert.False(WindowFilter.IsSwitchable(own), "this process");
            Assert.False(WindowFilter.IsSwitchable(null));
        }

        [Test]
        public static void ToolWindowsNeedTheAppWindowStyle()
        {
            WindowFacts tool = Normal(); tool.ExStyle = Native.WS_EX_TOOLWINDOW;
            WindowFacts toolApp = Normal(); toolApp.ExStyle = Native.WS_EX_TOOLWINDOW | Native.WS_EX_APPWINDOW;
            WindowFacts noActivate = Normal(); noActivate.ExStyle = Native.WS_EX_NOACTIVATE;
            Assert.False(WindowFilter.IsSwitchable(tool));
            Assert.True(WindowFilter.IsSwitchable(toolApp));
            Assert.False(WindowFilter.IsSwitchable(noActivate), "a no-activate palette like the Deck");
        }

        [Test]
        public static void OwnedWindowsNeedTheAppWindowStyle()
        {
            WindowFacts dialog = Normal("Save As"); dialog.HasOwner = true;
            WindowFacts ownedApp = Normal("Save As"); ownedApp.HasOwner = true; ownedApp.ExStyle = Native.WS_EX_APPWINDOW;
            Assert.False(WindowFilter.IsSwitchable(dialog));
            Assert.True(WindowFilter.IsSwitchable(ownedApp));
        }

        [Test]
        public static void ShellWindowsAreNotSwitchable()
        {
            string[] shell = { "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "XamlExplorerHostIslandWindow", "MultitaskingViewFrame", "ForegroundStaging" };
            foreach (string className in shell)
            {
                WindowFacts f = Normal("Program Manager");
                f.ClassName = className;
                Assert.False(WindowFilter.IsSwitchable(f), className);
            }
            WindowFacts lower = Normal(); lower.ClassName = "progman";
            Assert.False(WindowFilter.IsSwitchable(lower), "class names compare without case");
        }

        [Test]
        public static void IsSwitchableIsFalseForNoWindow()
        {
            Assert.False(new WindowSystem().IsSwitchable(IntPtr.Zero));
        }

        // ---------------- an Enter or Tab that ends an after-delimiter trigger ----------------

        [Test]
        public static void ALoneEnterDelimiterIsSentAsTheReturnKey()
        {
            Native.INPUT[] p = InputPackets.TextBatches("\n", FakeScan).Single();
            Assert.Equal(2, p.Length);
            Assert.Equal(Native.INPUT_KEYBOARD, p[0].type);
            Assert.Equal((ushort)Native.VK_RETURN, p[0].ki.wVk);
            Assert.Equal(0u, p[0].ki.dwFlags & Native.KEYEVENTF_UNICODE, "a real key, not a Unicode character");
            Assert.Equal((ushort)Native.VK_RETURN, p[1].ki.wVk);
            Assert.Equal(Native.KEYEVENTF_KEYUP, p[1].ki.dwFlags);
        }

        [Test]
        public static void ALoneTabDelimiterIsSentAsTheTabKey()
        {
            Native.INPUT[] p = InputPackets.TextBatches("\t", FakeScan).Single();
            Assert.Equal(2, p.Length);
            Assert.Equal((ushort)Native.VK_TAB, p[0].ki.wVk);
            Assert.Equal(0u, p[0].ki.dwFlags & Native.KEYEVENTF_UNICODE, "a real key, not a Unicode character");
            Assert.Equal(Native.KEYEVENTF_KEYUP, p[1].ki.dwFlags);
        }

        // ---------------- own windows under the mouse (no WindowFromPoint in the hook) ----------------

        static Native.RECT Rect(int left, int top, int right, int bottom)
        {
            return new Native.RECT { Left = left, Top = top, Right = right, Bottom = bottom };
        }

        [Test]
        public static void PointInRectIncludesLeftTopAndExcludesRightBottom()
        {
            Native.RECT r = Rect(100, 50, 300, 150);
            Assert.True(HookDecoding.PointInRect(r, 100, 50));
            Assert.True(HookDecoding.PointInRect(r, 299, 149));
            Assert.False(HookDecoding.PointInRect(r, 300, 100));
            Assert.False(HookDecoding.PointInRect(r, 200, 150));
            Assert.False(HookDecoding.PointInRect(r, 99, 100));
            Assert.False(HookDecoding.PointInRect(r, 200, 49));
        }

        [Test]
        public static void PointInAnyChecksEveryRectangle()
        {
            var rects = new List<Native.RECT> { Rect(0, 0, 10, 10), Rect(500, 500, 600, 560), Rect(-1920, 0, -1000, 400) };
            Assert.True(HookDecoding.PointInAny(rects, 5, 5));
            Assert.True(HookDecoding.PointInAny(rects, 550, 520));
            Assert.True(HookDecoding.PointInAny(rects, -1500, 200), "a monitor to the left has negative coordinates");
            Assert.False(HookDecoding.PointInAny(rects, 300, 300));
            Assert.False(HookDecoding.PointInAny(new List<Native.RECT>(), 5, 5));
            Assert.False(HookDecoding.PointInAny(null, 5, 5));
        }

        [Test]
        public static void SetOwnWindowsAcceptsNullAndDuplicatesWithoutStartingTheHook()
        {
            var hook = new InputHook();
            hook.SetOwnWindows(null);
            hook.SetOwnWindows(new IntPtr[] { IntPtr.Zero, new IntPtr(5), new IntPtr(5) });
            Assert.False(hook.IsRunning, "setting the list never installs a hook");
        }

        // ---------------- clipboard snapshot rules (pure parts; the real clipboard is never touched) ----------------

        [Test]
        public static void GdiHandleFormatsAreNotCopied()
        {
            uint[] gdi = { Native.CF_BITMAP, Native.CF_METAFILEPICT, Native.CF_PALETTE, Native.CF_ENHMETAFILE, Native.CF_OWNERDISPLAY,
                Native.CF_DSPBITMAP, Native.CF_DSPMETAFILEPICT, Native.CF_DSPENHMETAFILE, 0x0300, 0x03FF };
            foreach (uint format in gdi) Assert.False(ClipboardFormats.IsCopyable(format, null), "format " + format);
        }

        [Test]
        public static void MemoryFormatsAreCopiedAndPicturesSurviveAsDib()
        {
            Assert.True(ClipboardFormats.IsCopyable(Native.CF_UNICODETEXT, null));
            Assert.True(ClipboardFormats.IsCopyable(Native.CF_TEXT, null));
            Assert.True(ClipboardFormats.IsCopyable(Native.CF_DIB, null), "a picture comes back through CF_DIB");
            Assert.True(ClipboardFormats.IsCopyable(15, null), "CF_HDROP (files)");
            Assert.True(ClipboardFormats.IsCopyable(0xC123, "HTML Format"));
            Assert.True(ClipboardFormats.IsCopyable(0xC124, "Rich Text Format"));
            Assert.True(ClipboardFormats.IsCopyable(0x0201, null), "a private format");
        }

        [Test]
        public static void PrivacyMarkersAndComBackedFormatsAreNotCopied()
        {
            Assert.False(ClipboardFormats.IsCopyable(0xC200, "ExcludeClipboardContentFromMonitorProcessing"));
            Assert.False(ClipboardFormats.IsCopyable(0xC201, "CanIncludeInClipboardHistory"));
            Assert.False(ClipboardFormats.IsCopyable(0xC202, "CanUploadToCloudClipboard"));
            Assert.False(ClipboardFormats.IsCopyable(0xC203, "DataObject"));
            Assert.False(ClipboardFormats.IsCopyable(0xC204, "Ole Private Data"));
        }

        [Test]
        public static void PrivacyMarkerDataIsSmallAndZeroForHistoryAndCloud()
        {
            Assert.SequenceEqual(new byte[] { 0, 0, 0, 0 }, ClipboardFormats.ZeroDword());
            Assert.Equal(4, ClipboardFormats.ExcludeMarkerData().Length);
        }

        [Test]
        public static void SnapshotKeepsEverythingThatFits()
        {
            var items = new List<ClipboardItem>
            {
                new ClipboardItem(Native.CF_UNICODETEXT, new byte[100]),
                new ClipboardItem(Native.CF_DIB, new byte[5000]),
            };
            ClipboardSnapshot s = ClipboardSnapshot.Limit(items, ClipboardSnapshot.MaxBytes);
            Assert.Equal(2, s.Items.Count);
            Assert.Equal(5100L, s.TotalBytes);
            Assert.Equal(64L * 1024 * 1024, ClipboardSnapshot.MaxBytes);
        }

        [Test]
        public static void SnapshotOverTheCapFallsBackToTheTextAlone()
        {
            var items = new List<ClipboardItem>
            {
                new ClipboardItem(Native.CF_DIB, new byte[900]),
                new ClipboardItem(Native.CF_UNICODETEXT, new byte[100]),
                new ClipboardItem(Native.CF_TEXT, new byte[50]),
            };
            ClipboardSnapshot s = ClipboardSnapshot.Limit(items, 1000);
            Assert.Equal(1, s.Items.Count);
            Assert.Equal(Native.CF_UNICODETEXT, s.Items[0].Format);
            Assert.Equal(1000L - 900L, s.TotalBytes);
        }

        [Test]
        public static void SnapshotKeepsTheTextEvenWhenTheTextAloneIsOverTheCap()
        {
            var items = new List<ClipboardItem> { new ClipboardItem(Native.CF_UNICODETEXT, new byte[2000]) };
            ClipboardSnapshot s = ClipboardSnapshot.Limit(items, 1000);
            Assert.Equal(1, s.Items.Count);
        }

        [Test]
        public static void SnapshotWithNothingToCopyIsEmpty()
        {
            Assert.Equal(0, ClipboardSnapshot.Limit(new List<ClipboardItem>(), 1000).Items.Count);
            Assert.Equal(0, new ClipboardItem(1, null).Data.Length);
        }

        // ---------------- start with Windows ----------------

        [Test]
        public static void StartupCommandQuotesThePathAndAddsTray()
        {
            Assert.Equal("\"C:\\Users\\Sam\\AppData\\Local\\Programs\\Macro Kit\\Macro Kit.exe\" --tray",
                StartupEntry.BuildCommand(@"C:\Users\Sam\AppData\Local\Programs\Macro Kit\Macro Kit.exe"));
            Assert.Equal("\"C:\\a.exe\" --tray", StartupEntry.BuildCommand(@"C:\a.exe"));
        }

        [Test]
        public static void StartupCommandRejectsEmptyAndQuotedPaths()
        {
            Assert.Throws<ArgumentException>(() => StartupEntry.BuildCommand(null));
            Assert.Throws<ArgumentException>(() => StartupEntry.BuildCommand("  "));
            Assert.Throws<ArgumentException>(() => StartupEntry.BuildCommand("C:\\a\"b.exe"));
        }

        [Test]
        public static void StartupApprovedFlagReadsTheUsersChoice()
        {
            Assert.False(StartupEntry.IsDisabledByUser(null));
            Assert.False(StartupEntry.IsDisabledByUser(new byte[0]));
            Assert.False(StartupEntry.IsDisabledByUser(new byte[] { 0x02, 0, 0, 0 }), "02 = enabled");
            Assert.False(StartupEntry.IsDisabledByUser(new byte[] { 0x06, 0, 0, 0 }), "06 = enabled");
            Assert.True(StartupEntry.IsDisabledByUser(new byte[] { 0x03, 0, 0, 0 }), "03 = turned off");
            Assert.True(StartupEntry.IsDisabledByUser(new byte[] { 0x07, 0, 0, 0 }), "07 = turned off");
            Assert.Equal("Macro Kit", StartupEntry.ValueName);
        }
    }
}
