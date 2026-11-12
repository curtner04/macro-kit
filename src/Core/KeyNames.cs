using System;
using System.Collections.Generic;
using System.Globalization;

namespace MacroKit.Core
{
    /// <summary>
    /// Key names shared by the Super Macro parser, hotkeys and the UI. Virtual-key codes are the Windows VK_* values.
    /// Keys that must be sent without the extended flag (the numpad navigation keys) carry <see cref="ForceNonExtended"/>.
    /// </summary>
    public static class KeyNames
    {
        /// <summary>OR-ed into a VK so the sink sends it without KEYEVENTF_EXTENDEDKEY (numpad Home, End, arrows...). Real VKs are below 0x100.</summary>
        public const ushort ForceNonExtended = 0x8000;

        const ushort VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;

        static readonly Dictionary<string, ushort> byName = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, ushort> byDisplay = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<ushort, string> displayByVk = new Dictionary<ushort, string>();
        static readonly Dictionary<ushort, string> tokenByVk = new Dictionary<ushort, string>();
        static readonly Dictionary<string, MouseButton> mouseButtons = new Dictionary<string, MouseButton>(StringComparer.OrdinalIgnoreCase)
        {
            { "LBUTTON", MouseButton.Left }, { "RBUTTON", MouseButton.Right }, { "MBUTTON", MouseButton.Middle },
            { "XBUTTON1", MouseButton.X1 }, { "XBUTTON2", MouseButton.X2 },
        };

        // Registers one key: canonical Super Macro token, display text, and any aliases (all case-insensitive).
        static void Def(int vk, string token, string display, params string[] aliases)
        {
            ushort key = (ushort)vk;
            byName[token] = key;
            tokenByVk[key] = token;
            displayByVk[key] = display;
            byDisplay[display] = key;
            foreach (string alias in aliases) byName[alias] = key;
        }

        static KeyNames()
        {
            for (char c = 'A'; c <= 'Z'; c++) Def(c, c.ToString(), c.ToString());
            for (char c = '0'; c <= '9'; c++) Def(c, c.ToString(), c.ToString());

            Def(0x0D, "ENTER", "Enter", "RETURN");
            Def(0x09, "TAB", "Tab");
            Def(0x1B, "ESCAPE", "Esc", "ESC");
            Def(0x20, "SPACE", "Space");
            Def(0x08, "BACK", "Backspace", "BACKSPACE");
            Def(0x2E, "DELETE", "Delete", "DEL");
            Def(0x2D, "INSERT", "Insert", "INS");
            Def(0x24, "HOME", "Home");
            Def(0x23, "END", "End");
            Def(0x21, "PAGEUP", "Page Up", "PGUP", "PRIOR");
            Def(0x22, "PAGEDOWN", "Page Down", "PGDN", "NEXT");
            Def(0x26, "UP", "Up");
            Def(0x28, "DOWN", "Down");
            Def(0x25, "LEFT", "Left");
            Def(0x27, "RIGHT", "Right");
            Def(0x0C, "CLEAR", "Clear");
            Def(0x13, "BREAK", "Pause");               // the Pause/Break key; the word PAUSE alone is the macro pause command
            Def(0x14, "CAPITAL", "Caps Lock", "CAPSLOCK");
            Def(0x90, "NUMLOCK", "Num Lock");
            Def(0x91, "SCROLL", "Scroll Lock", "SCROLLLOCK");
            Def(0x2C, "SNAPSHOT", "Print Screen", "PRINTSCREEN");
            Def(0x2A, "PRINT", "Print");
            Def(0x29, "SELECT", "Select");
            Def(0x2B, "EXECUTE", "Execute");
            Def(0x2F, "HELP", "Help");
            Def(0x5D, "APPS", "Apps", "MENUKEY");

            // Generic CTRL / SHIFT / ALT send the left key so shortcuts and Alt+Tab behave like a real keyboard.
            Def(0xA0, "SHIFT", "Shift", "LSHIFT");
            Def(0xA1, "RSHIFT", "Right Shift");
            Def(0xA2, "CTRL", "Ctrl", "CONTROL", "LCTRL", "LCONTROL");
            Def(0xA3, "RCTRL", "Right Ctrl", "RCONTROL");
            Def(0xA4, "ALT", "Alt", "MENU", "LALT", "LMENU");
            Def(0xA5, "RALT", "Right Alt", "RMENU");
            Def(0x5B, "WIN", "Win", "WINDOWS", "LWIN");
            Def(0x5C, "RWIN", "Right Win");
            // The generic VKs (what hooks and GetAsyncKeyState sometimes report) only need names for display and tokens.
            displayByVk[VkShift] = "Shift"; tokenByVk[VkShift] = "SHIFT";
            displayByVk[VkControl] = "Ctrl"; tokenByVk[VkControl] = "CTRL";
            displayByVk[VkMenu] = "Alt"; tokenByVk[VkMenu] = "ALT";

            for (int i = 1; i <= 24; i++) Def(0x6F + i, "F" + i, "F" + i);

            for (int i = 0; i <= 9; i++) Def(0x60 + i, "NUMPAD" + i, "Num " + i);
            Def(0x6A, "MULTIPLY", "Num *");
            Def(0x6B, "ADD", "Num Plus");                // no "+" in display names: hotkey text uses "+" as the separator
            Def(0x6C, "SEPARATOR", "Num Separator");
            Def(0x6D, "SUBTRACT", "Num -");
            Def(0x6E, "DECIMAL", "Num .");
            Def(0x6F, "DIVIDE", "Num /");

            // Numpad navigation keys share the VKs of the navigation block but are sent without the extended flag.
            Def(0x21 | ForceNonExtended, "NUMPAD_PAGEUP", "Num Page Up");
            Def(0x22 | ForceNonExtended, "NUMPAD_PAGEDOWN", "Num Page Down");
            Def(0x24 | ForceNonExtended, "NUMPAD_HOME", "Num Home");
            Def(0x23 | ForceNonExtended, "NUMPAD_END", "Num End");
            Def(0x26 | ForceNonExtended, "NUMPAD_UP", "Num Up");
            Def(0x28 | ForceNonExtended, "NUMPAD_DOWN", "Num Down");
            Def(0x25 | ForceNonExtended, "NUMPAD_LEFT", "Num Left");
            Def(0x27 | ForceNonExtended, "NUMPAD_RIGHT", "Num Right");
            Def(0x2D | ForceNonExtended, "NUMPAD_INSERT", "Num Insert");
            Def(0x2E | ForceNonExtended, "NUMPAD_DEL", "Num Delete");

            // OEM keys (US layout glyphs for display).
            Def(0xBA, "OEM_1", ";");
            Def(0xBB, "OEM_PLUS", "=");
            Def(0xBC, "OEM_COMMA", ",");
            Def(0xBD, "OEM_MINUS", "-");
            Def(0xBE, "OEM_PERIOD", ".");
            Def(0xBF, "OEM_2", "/");
            Def(0xC0, "OEM_3", "`");
            Def(0xDB, "OEM_4", "[");
            Def(0xDC, "OEM_5", "\\");
            Def(0xDD, "OEM_6", "]");
            Def(0xDE, "OEM_7", "'");
            Def(0xDF, "OEM_8", "OEM 8");
            Def(0xE2, "OEM_102", "OEM 102");

            Def(0xB0, "MEDIA_NEXT_TRACK", "Next Track");
            Def(0xB1, "MEDIA_PREV_TRACK", "Previous Track");
            Def(0xB2, "MEDIA_STOP", "Media Stop");
            Def(0xB3, "MEDIA_PLAY_PAUSE", "Play/Pause");
            Def(0xAF, "VOLUME_UP", "Volume Up");
            Def(0xAE, "VOLUME_DOWN", "Volume Down");
            Def(0xAD, "VOLUME_MUTE", "Volume Mute");
            Def(0xA6, "BROWSER_BACK", "Browser Back");
            Def(0xA7, "BROWSER_FORWARD", "Browser Forward");
            Def(0xAC, "BROWSER_HOME", "Browser Home");
            Def(0xA8, "BROWSER_REFRESH", "Browser Refresh");
            Def(0xA9, "BROWSER_STOP", "Browser Stop");
            Def(0xAA, "BROWSER_SEARCH", "Browser Search");
            Def(0xAB, "BROWSER_FAVORITES", "Browser Favorites");
        }

        /// <summary>The VK without <see cref="ForceNonExtended"/>.</summary>
        public static ushort BaseVk(ushort vk)
        {
            return (ushort)(vk & ~ForceNonExtended);
        }

        /// <summary>
        /// Looks up a Super Macro key name or alias, case-insensitive. Also accepts an optional VK_ prefix
        /// (VK_A, VK_RETURN) and a raw hex code such as 0x41. The bare word PAUSE is not a key (it is the macro pause command).
        /// </summary>
        public static bool TryGetVk(string name, out ushort vk)
        {
            vk = 0;
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            if (byName.TryGetValue(name, out vk)) return true;
            if (name.StartsWith("VK_", StringComparison.OrdinalIgnoreCase) && byName.TryGetValue(name.Substring(3), out vk)) return true;
            string hex = name.StartsWith("VK_", StringComparison.OrdinalIgnoreCase) ? name.Substring(3) : name;
            int code;
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(hex.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)
                && code > 0 && code < 0x100)
            {
                vk = (ushort)code;
                return true;
            }
            vk = 0;
            return false;
        }

        /// <summary>Looks up the text <see cref="Display"/> produces ("Page Up", "Num 5", "Right Ctrl"), so hotkey text round-trips.</summary>
        public static bool TryParseDisplay(string text, out ushort vk)
        {
            vk = 0;
            return !string.IsNullOrWhiteSpace(text) && byDisplay.TryGetValue(text.Trim(), out vk);
        }

        /// <summary>Friendly name: "Ctrl", "Alt", "Tab", "Down", "F5", "A", "Num 5". Unknown keys show their code ("Key 0xFA").</summary>
        public static string Display(ushort vk)
        {
            string text;
            if (displayByVk.TryGetValue(vk, out text)) return text;
            if (displayByVk.TryGetValue(BaseVk(vk), out text)) return text;
            return "Key 0x" + BaseVk(vk).ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>Canonical Super Macro name: "CTRL", "ALT", "TAB", "DOWN", "C". Unknown keys come back as "0x" + hex, which TryGetVk reads.</summary>
        public static string MacroToken(ushort vk)
        {
            string token;
            if (tokenByVk.TryGetValue(vk, out token)) return token;
            if (tokenByVk.TryGetValue(BaseVk(vk), out token)) return token;
            return "0x" + BaseVk(vk).ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>Shift, Ctrl, Alt or Win, in generic and left/right forms.</summary>
        public static bool IsModifier(ushort vk)
        {
            ushort k = BaseVk(vk);
            return (k >= VkShift && k <= VkMenu) || (k >= 0xA0 && k <= 0xA5) || k == 0x5B || k == 0x5C;
        }

        /// <summary>
        /// True for keys sent with KEYEVENTF_EXTENDEDKEY: arrows, Ins/Del/Home/End/PgUp/PgDn, right Ctrl/Alt, Win, Apps,
        /// numpad Divide, NumLock, PrintScreen and the browser/volume/media keys. False for anything with <see cref="ForceNonExtended"/>.
        /// </summary>
        public static bool IsExtended(ushort vk)
        {
            if ((vk & ForceNonExtended) != 0) return false;
            switch (vk)
            {
                case 0x21: case 0x22: case 0x23: case 0x24:   // PgUp PgDn End Home
                case 0x25: case 0x26: case 0x27: case 0x28:   // arrows
                case 0x2C: case 0x2D: case 0x2E:              // PrintScreen Insert Delete
                case 0x5B: case 0x5C: case 0x5D:              // LWin RWin Apps
                case 0x6F: case 0x90:                         // Divide NumLock
                case 0xA3: case 0xA5:                         // RCtrl RAlt
                    return true;
            }
            return vk >= 0xA6 && vk <= 0xB7;                  // browser, volume, media and launch keys
        }

        public static bool IsMouseButton(string name, out MouseButton b)
        {
            b = MouseButton.Left;
            return !string.IsNullOrWhiteSpace(name) && mouseButtons.TryGetValue(name.Trim(), out b);
        }
    }
}
