using System;
using System.Collections.Generic;
using System.Text;

namespace MacroKit.Core
{
    [Flags]
    public enum HotkeyModifiers { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }   // = MOD_* values

    /// <summary>A global hotkey such as Ctrl+Shift+Alt+R. Text form: modifiers in canonical order Ctrl+Shift+Alt+Win, then the key.</summary>
    public struct Hotkey : IEquatable<Hotkey>
    {
        const ushort VkPause = 0x13;

        public HotkeyModifiers Modifiers;
        public ushort Vk;

        public Hotkey(HotkeyModifiers modifiers, ushort vk)
        {
            Modifiers = modifiers;
            Vk = KeyNames.BaseVk(vk);
        }

        public bool IsEmpty { get { return Vk == 0; } }

        /// <summary>Parses "Ctrl+Shift+Alt+R", "Ctrl+Alt+1", "F13" (case-insensitive, any modifier order).</summary>
        public static bool TryParse(string text, out Hotkey hotkey)
        {
            hotkey = default(Hotkey);
            if (string.IsNullOrWhiteSpace(text)) return false;
            HotkeyModifiers modifiers = HotkeyModifiers.None;
            ushort vk = 0;
            foreach (string raw in text.Split('+'))
            {
                string part = raw.Trim();
                if (part.Length == 0) return false;
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": modifiers |= HotkeyModifiers.Ctrl; continue;
                    case "shift": modifiers |= HotkeyModifiers.Shift; continue;
                    case "alt": modifiers |= HotkeyModifiers.Alt; continue;
                    case "win": case "windows": modifiers |= HotkeyModifiers.Win; continue;
                }
                if (vk != 0) return false;       // only one non-modifier key
                ushort found;
                if (part.Equals("pause", StringComparison.OrdinalIgnoreCase)) found = VkPause;
                else if (!KeyNames.TryGetVk(part, out found) && !KeyNames.TryParseDisplay(part, out found)) return false;
                found = KeyNames.BaseVk(found);
                if (KeyNames.IsModifier(found)) return false;
                vk = found;
            }
            if (vk == 0) return false;
            hotkey = new Hotkey(modifiers, vk);
            return true;
        }

        /// <summary>Canonical text, Ctrl+Shift+Alt+Win+Key; empty for an empty hotkey.</summary>
        public override string ToString()
        {
            if (IsEmpty) return "";
            var text = new StringBuilder();
            if ((Modifiers & HotkeyModifiers.Ctrl) != 0) text.Append("Ctrl+");
            if ((Modifiers & HotkeyModifiers.Shift) != 0) text.Append("Shift+");
            if ((Modifiers & HotkeyModifiers.Alt) != 0) text.Append("Alt+");
            if ((Modifiers & HotkeyModifiers.Win) != 0) text.Append("Win+");
            text.Append(KeyNames.Display(Vk));
            return text.ToString();
        }

        /// <summary>
        /// Usable as a system-wide hotkey: needs Ctrl, Alt or Win (Shift alone would block normal typing),
        /// or a lone F13-F24 / Pause key.
        /// </summary>
        public bool IsValidGlobal
        {
            get
            {
                if (IsEmpty) return false;
                if ((Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0) return true;
                if (Modifiers != HotkeyModifiers.None) return false;
                return (Vk >= 0x7C && Vk <= 0x87) || Vk == VkPause;   // F13-F24, Pause
            }
        }

        /// <summary>
        /// Why <paramref name="text"/> cannot be a global hotkey, or null when it can (and nothing for an empty text). Pure: the
        /// caller keeps <paramref name="usedBy"/> (combination text to the name of whoever has it), so duplicates are reported
        /// the same way in the same order the hotkeys are set up.
        /// </summary>
        public static string Problem(string text, IDictionary<string, string> usedBy, out Hotkey hotkey)
        {
            hotkey = default(Hotkey);
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!TryParse(text, out hotkey)) return "“" + text + "” is not a hotkey Macro Kit understands.";
            if (!hotkey.IsValidGlobal)
            {
                string key = KeyNames.Display(hotkey.Vk);
                if (TypesText(hotkey.Vk))
                    return hotkey + " would get in the way of normal typing. Add Ctrl, Alt or Win, for example Ctrl+Alt+" + key + ".";
                if ((hotkey.Vk >= 0x7C && hotkey.Vk <= 0x87) || hotkey.Vk == VkPause)   // Shift+F13: the key works without Shift
                    return hotkey + " needs Ctrl, Alt or Win, or use " + key + " on its own.";
                return hotkey + " needs Ctrl, Alt or Win (F13–F24 and Pause also work on their own).";
            }
            string own;
            if (usedBy != null && usedBy.TryGetValue(hotkey.ToString(), out own))
                return "Macro Kit already uses " + hotkey + " for " + own + ". Press a different combination.";
            return null;
        }

        // Keys that type something: letters, digits (top row and keypad), space and punctuation.
        static bool TypesText(ushort vk)
        {
            return (vk >= 0x41 && vk <= 0x5A) || (vk >= 0x30 && vk <= 0x39) || (vk >= 0x60 && vk <= 0x6F) || vk == 0x20
                || (vk >= 0xBA && vk <= 0xC0) || (vk >= 0xDB && vk <= 0xDF) || vk == 0xE2;
        }

        /// <summary>
        /// What an everyday shortcut does in almost every program ("Copy" for Ctrl+C), or null. A global hotkey on one of these
        /// takes it away from every other program while Macro Kit runs, so it is worth a warning (not a refusal: a saved setup keeps working).
        /// </summary>
        public static string EverydayUse(Hotkey hk)
        {
            if (hk.IsEmpty) return null;
            if (hk.Modifiers == HotkeyModifiers.Ctrl)
            {
                switch (hk.Vk)
                {
                    case 0x41: return "Select all";
                    case 0x43: return "Copy";
                    case 0x46: return "Find";
                    case 0x4E: return "New";
                    case 0x4F: return "Open";
                    case 0x50: return "Print";
                    case 0x53: return "Save";
                    case 0x54: return "New tab";
                    case 0x56: return "Paste";
                    case 0x57: return "Close tab";
                    case 0x58: return "Cut";
                    case 0x59: return "Redo";
                    case 0x5A: return "Undo";
                }
                return null;
            }
            if (hk.Modifiers == (HotkeyModifiers.Ctrl | HotkeyModifiers.Shift))
            {
                if (hk.Vk == 0x54) return "Reopen closed tab";
                if (hk.Vk == 0x1B) return "Task Manager";
                return null;
            }
            if (hk.Modifiers == HotkeyModifiers.Alt)
            {
                if (hk.Vk == 0x73) return "Close window";       // F4
                if (hk.Vk == 0x09) return "Switch windows";     // Tab
            }
            return null;
        }

        /// <summary>The warning for a hotkey on an everyday shortcut (see <see cref="EverydayUse"/>), or null.</summary>
        public static string EverydayWarning(Hotkey hk)
        {
            string use = EverydayUse(hk);
            if (use == null) return null;
            string text = hk + " is " + use + " in almost every program; while Macro Kit holds it, " + use + " won't work there.";
            Hotkey other = hk.Suggestion();
            return other.IsEmpty ? text : text + " Try " + other + ".";
        }

        // The same key with Alt added (or Ctrl, when it already has Alt), which no program uses for an everyday command.
        Hotkey Suggestion()
        {
            if (Vk == 0x09 || Vk == 0x1B) return default(Hotkey);   // Tab and Esc combinations all belong to Windows
            HotkeyModifiers more = (Modifiers & HotkeyModifiers.Alt) == 0 ? HotkeyModifiers.Alt : HotkeyModifiers.Ctrl;
            var other = new Hotkey(Modifiers | more, Vk);
            return EverydayUse(other) == null ? other : default(Hotkey);
        }

        public bool Equals(Hotkey other) { return Modifiers == other.Modifiers && Vk == other.Vk; }
        public override bool Equals(object obj) { return obj is Hotkey && Equals((Hotkey)obj); }
        public override int GetHashCode() { return ((int)Modifiers << 16) ^ Vk; }
        public static bool operator ==(Hotkey a, Hotkey b) { return a.Equals(b); }
        public static bool operator !=(Hotkey a, Hotkey b) { return !a.Equals(b); }
    }
}
