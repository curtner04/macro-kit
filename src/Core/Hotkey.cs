using System;
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

        public bool Equals(Hotkey other) { return Modifiers == other.Modifiers && Vk == other.Vk; }
        public override bool Equals(object obj) { return obj is Hotkey && Equals((Hotkey)obj); }
        public override int GetHashCode() { return ((int)Modifiers << 16) ^ Vk; }
        public static bool operator ==(Hotkey a, Hotkey b) { return a.Equals(b); }
        public static bool operator !=(Hotkey a, Hotkey b) { return !a.Equals(b); }
    }
}
