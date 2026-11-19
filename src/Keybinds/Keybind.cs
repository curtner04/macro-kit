using System;
using System.Collections.Generic;
using System.Globalization;
using MacroKit.Core;

namespace MacroKit.Keybinds
{
    /// <summary>Where a shortcut works.</summary>
    public enum KeybindScope
    {
        /// <summary>Works in every program while the app runs (RegisterHotKey or a keyboard hook).</summary>
        Global,
        /// <summary>Works only while the app's own window is the active window.</summary>
        AppActive,
    }

    /// <summary>One keyboard shortcut of one app.</summary>
    public sealed class Keybind
    {
        public string App = "";
        public string Action = "";
        /// <summary>The keys as the person reads them ("Ctrl+Shift+C"); the app's own text when it could not be read.</summary>
        public string Keys = "";
        /// <summary>Empty when the keys could not be understood (they are then still listed, but never matched).</summary>
        public Hotkey Combo;
        public KeybindScope Scope;
        /// <summary>True for a shortcut the person saved or can change; false for one built into the app.</summary>
        public bool Saved;
        /// <summary>Extra condition, such as "only while the screenshot is open". Empty when there is none.</summary>
        public string Note = "";
        /// <summary>False when the app has it switched off; it is listed but not counted as taken.</summary>
        public bool Enabled = true;
        /// <summary>Only listens for a moment (while a run is going), so it is never reported as getting in another app's way.</summary>
        public bool Momentary;

        public bool Matches(Hotkey combo) { return Enabled && !Combo.IsEmpty && Combo == combo; }

        public override string ToString() { return App + ": " + Action + " = " + Keys + " (" + Scope + ")"; }
    }

    /// <summary>The shortcuts read for one app, and how reading went.</summary>
    public sealed class AppKeybinds
    {
        public string App = "";
        /// <summary>The file the saved shortcuts came from, or "" when they are built in or kept by Macro Kit itself.</summary>
        public string Source = "";
        /// <summary>Null when everything was read; otherwise why some or all of the saved shortcuts are missing.</summary>
        public string Problem;
        /// <summary>Process names (without .exe) that mean the app is running; empty = unknown.</summary>
        public string[] Processes = new string[0];
        /// <summary>Set for an app the person added by hand: the id of its entry in keybinds.json.</summary>
        public string CustomId;
        public readonly List<Keybind> Keybinds = new List<Keybind>();

        public AppKeybinds(string app, string source = "") { App = app; Source = source ?? ""; }

        public Keybind Add(string action, string keys, KeybindScope scope, bool saved, string note = "", bool enabled = true)
        {
            Hotkey combo;
            KeyText.TryParse(keys, out combo);
            var keybind = new Keybind
            {
                App = App, Action = action ?? "", Combo = combo, Keys = combo.IsEmpty ? (keys ?? "").Trim() : combo.ToString(),
                Scope = scope, Saved = saved, Note = note ?? "", Enabled = enabled,
            };
            Keybinds.Add(keybind);
            return keybind;
        }

        public Keybind Add(string action, Hotkey combo, KeybindScope scope, bool saved, string note = "", bool enabled = true)
        {
            var keybind = new Keybind
            {
                App = App, Action = action ?? "", Combo = combo, Keys = combo.ToString(), Scope = scope, Saved = saved, Note = note ?? "", Enabled = enabled,
            };
            Keybinds.Add(keybind);
            return keybind;
        }
    }

    /// <summary>
    /// Reads shortcut text in the forms other apps save it: "Ctrl+Shift+C", "ctrl-alt-shift-u" (Jancy), "Control + Alt + K",
    /// "CmdOrCtrl+Shift+P", "PrtSc". Modifier order and case do not matter. A lone key (PrtSc, F5, Delete) is fine.
    /// </summary>
    public static class KeyText
    {
        static readonly Dictionary<string, HotkeyModifiers> modifiers = new Dictionary<string, HotkeyModifiers>(StringComparer.OrdinalIgnoreCase)
        {
            { "ctrl", HotkeyModifiers.Ctrl }, { "control", HotkeyModifiers.Ctrl }, { "cmdorctrl", HotkeyModifiers.Ctrl }, { "commandorcontrol", HotkeyModifiers.Ctrl },
            { "shift", HotkeyModifiers.Shift },
            { "alt", HotkeyModifiers.Alt }, { "menu", HotkeyModifiers.Alt }, { "option", HotkeyModifiers.Alt }, { "altgr", HotkeyModifiers.Alt },
            { "win", HotkeyModifiers.Win }, { "windows", HotkeyModifiers.Win }, { "super", HotkeyModifiers.Win }, { "meta", HotkeyModifiers.Win }, { "lwin", HotkeyModifiers.Win }, { "rwin", HotkeyModifiers.Win },
        };

        static readonly Dictionary<string, ushort> extraKeys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            { "prtsc", 0x2C }, { "prtscn", 0x2C }, { "prntscrn", 0x2C }, { "printscreen", 0x2C }, { "print screen", 0x2C }, { "snapshot", 0x2C },
            { "esc", 0x1B }, { "escape", 0x1B }, { "return", 0x0D }, { "enter", 0x0D }, { "space", 0x20 }, { "spacebar", 0x20 },
            { "del", 0x2E }, { "delete", 0x2E }, { "ins", 0x2D }, { "insert", 0x2D }, { "backspace", 0x08 }, { "back", 0x08 },
            { "pageup", 0x21 }, { "page up", 0x21 }, { "pgup", 0x21 }, { "prior", 0x21 }, { "pagedown", 0x22 }, { "page down", 0x22 }, { "pgdn", 0x22 }, { "next", 0x22 },
            { "up", 0x26 }, { "down", 0x28 }, { "left", 0x25 }, { "right", 0x27 }, { "arrowup", 0x26 }, { "arrowdown", 0x28 }, { "arrowleft", 0x25 }, { "arrowright", 0x27 },
            { "pause", 0x13 }, { "break", 0x13 }, { "capslock", 0x14 }, { "numlock", 0x90 }, { "scrolllock", 0x91 },
            { "plus", 0xBB }, { "equal", 0xBB }, { "equals", 0xBB }, { "=", 0xBB }, { "oemplus", 0xBB },
            { "minus", 0xBD }, { "-", 0xBD }, { "oemminus", 0xBD }, { "comma", 0xBC }, { ",", 0xBC }, { "oemcomma", 0xBC },
            { "period", 0xBE }, { ".", 0xBE }, { "oemperiod", 0xBE }, { "slash", 0xBF }, { "/", 0xBF }, { "oem2", 0xBF }, { "oemquestion", 0xBF },
            { "semicolon", 0xBA }, { ";", 0xBA }, { "oem1", 0xBA }, { "oemsemicolon", 0xBA }, { "quote", 0xDE }, { "'", 0xDE }, { "oem7", 0xDE }, { "oemquotes", 0xDE },
            { "backquote", 0xC0 }, { "backtick", 0xC0 }, { "grave", 0xC0 }, { "`", 0xC0 }, { "oem3", 0xC0 }, { "oemtilde", 0xC0 },
            { "bracketleft", 0xDB }, { "[", 0xDB }, { "oem4", 0xDB }, { "oemopenbrackets", 0xDB }, { "bracketright", 0xDD }, { "]", 0xDD }, { "oem6", 0xDD }, { "oemclosebrackets", 0xDD },
            { "backslash", 0xDC }, { "\\", 0xDC }, { "oem5", 0xDC }, { "oempipe", 0xDC },
            { "numpadadd", 0x6B }, { "numadd", 0x6B }, { "numpadsubtract", 0x6D }, { "numsub", 0x6D }, { "numpadmultiply", 0x6A }, { "nummult", 0x6A },
            { "numpaddivide", 0x6F }, { "numdiv", 0x6F }, { "numpaddecimal", 0x6E }, { "numdec", 0x6E }, { "apps", 0x5D }, { "contextmenu", 0x5D },
        };

        /// <summary>Reads keys text. False (and an empty combo) when it is empty or names a key Macro Kit does not know.</summary>
        public static bool TryParse(string text, out Hotkey combo)
        {
            combo = default(Hotkey);
            if (string.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text.Trim();
            // A whole-text lone key first, so "Print Screen", "Page Up" and "-" work.
            ushort lone;
            if (TryKey(trimmed, out lone)) { combo = new Hotkey(HotkeyModifiers.None, lone); return true; }

            char separator = trimmed.IndexOf('+') >= 0 ? '+' : '-';
            List<string> parts = Split(trimmed, separator);
            if (parts == null || parts.Count == 0) return false;
            HotkeyModifiers mods = HotkeyModifiers.None;
            ushort vk = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                string part = parts[i];
                HotkeyModifiers mod;
                if (i < parts.Count - 1)
                {
                    if (!modifiers.TryGetValue(part, out mod)) return false;
                    mods |= mod;
                    continue;
                }
                if (modifiers.TryGetValue(part, out mod)) return false;     // a modifier on its own is not a shortcut
                if (!TryKey(part, out vk)) return false;
            }
            combo = new Hotkey(mods, vk);
            return !combo.IsEmpty;
        }

        /// <summary>
        /// Reads search text as keys in any order, with spaces or + between them: "ctrl alt s", "s+ctrl+alt", "alt ctrl shift".
        /// False when a word is not a key or modifier, when it names more than one key, or when it is one plain character (that is a
        /// word search). <paramref name="key"/> is 0 when only modifiers were named.
        /// </summary>
        public static bool TryParseLoose(string text, out HotkeyModifiers mods, out ushort key)
        {
            mods = HotkeyModifiers.None;
            key = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text.Trim();
            Hotkey exact;
            if (TryParse(trimmed, out exact) && (exact.Modifiers != HotkeyModifiers.None || trimmed.Length > 1))
            {
                mods = exact.Modifiers;
                key = exact.Vk;
                return true;
            }
            string[] words = trimmed.Split(new[] { ' ', '+', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                HotkeyModifiers mod;
                if (modifiers.TryGetValue(words[i], out mod)) { mods |= mod; continue; }
                ushort vk;
                // Two-word key names: "page up", "print screen".
                if (i + 1 < words.Length && TryKey(words[i] + " " + words[i + 1], out vk)) i++;
                else if (!TryKey(words[i], out vk)) return false;
                if (key != 0 && key != vk) return false;
                key = vk;
            }
            return mods != HotkeyModifiers.None || (key != 0 && trimmed.Length > 1);
        }

        /// <summary>True when the shortcut uses every modifier named and the key named (if any): "ctrl alt" finds Ctrl+Shift+Alt+S too.</summary>
        public static bool HasKeys(Hotkey combo, HotkeyModifiers mods, ushort key)
        {
            return !combo.IsEmpty && (combo.Modifiers & mods) == mods && (key == 0 || combo.Vk == key);
        }

        /// <summary>Splits on the separator; an empty last part means the key is the separator itself ("Ctrl++", "ctrl--").</summary>
        static List<string> Split(string text, char separator)
        {
            var parts = new List<string>();
            string[] raw = text.Split(separator);
            for (int i = 0; i < raw.Length; i++)
            {
                string part = raw[i].Trim();
                if (part.Length > 0) { parts.Add(part); continue; }
                bool trailing = true;
                for (int j = i + 1; j < raw.Length; j++) if (raw[j].Trim().Length > 0) { trailing = false; break; }
                if (!trailing) return null;
                if (i > 0) parts.Add(separator.ToString());
                break;
            }
            return parts;
        }

        static bool TryKey(string name, out ushort vk)
        {
            vk = 0;
            string key = name.Trim();
            if (key.Length == 0) return false;
            if (extraKeys.TryGetValue(key, out vk)) return true;
            if (key.Length > 3 && key.StartsWith("key", StringComparison.OrdinalIgnoreCase) && key.Length == 4 && char.IsLetter(key[3])) key = key.Substring(3);   // KeyA
            if (key.Length == 2 && (key[0] == 'D' || key[0] == 'd') && char.IsDigit(key[1])) key = key.Substring(1);                                                // WPF D1
            if (key.StartsWith("digit", StringComparison.OrdinalIgnoreCase) && key.Length == 6 && char.IsDigit(key[5])) key = key.Substring(5);                         // Digit1
            if (key.StartsWith("num", StringComparison.OrdinalIgnoreCase) && key.Length == 4 && char.IsDigit(key[3])) key = "NUMPAD" + key[3];                          // num1
            if (key.StartsWith("numpad", StringComparison.OrdinalIgnoreCase) && key.Length == 7 && char.IsDigit(key[6])) key = "NUMPAD" + key[6];
            if (KeyNames.TryGetVk(key, out vk) || KeyNames.TryParseDisplay(key, out vk))
            {
                vk = KeyNames.BaseVk(vk);
                if (KeyNames.IsModifier(vk)) { vk = 0; return false; }
                return true;
            }
            vk = 0;
            return false;
        }

        /// <summary>Text for a Windows virtual-key code plus MOD_* bits (Alt 1, Ctrl 2, Shift 4, Win 8), as RegisterHotKey takes them.</summary>
        public static Hotkey FromNative(int vk, int modifierBits)
        {
            if (vk <= 0 || vk > 0xFF) return default(Hotkey);
            return new Hotkey((HotkeyModifiers)(modifierBits & 0x0F), (ushort)vk);
        }

        /// <summary>A WinForms Keys value (key code in the low 16 bits, Shift 0x10000, Ctrl 0x20000, Alt 0x40000).</summary>
        public static Hotkey FromWinFormsKeys(long keys)
        {
            int vk = (int)(keys & 0xFFFF);
            var mods = HotkeyModifiers.None;
            if ((keys & 0x10000) != 0) mods |= HotkeyModifiers.Shift;
            if ((keys & 0x20000) != 0) mods |= HotkeyModifiers.Ctrl;
            if ((keys & 0x40000) != 0) mods |= HotkeyModifiers.Alt;
            if (vk <= 0 || vk > 0xFF) return default(Hotkey);
            return new Hotkey(mods, (ushort)vk);
        }

        internal static string Title(string id)
        {
            string text = (id ?? "").Replace('-', ' ').Replace('_', ' ').Trim();
            return text.Length == 0 ? "" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());
        }
    }
}
