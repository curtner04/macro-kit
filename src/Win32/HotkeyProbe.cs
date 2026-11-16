using System;
using System.Runtime.InteropServices;
using MacroKit.Core;

namespace MacroKit.Win32
{
    /// <summary>
    /// Asks Windows whether another program has registered a key combination as a hotkey: Macro Kit registers it for an
    /// instant and lets it go straight away. Programs that watch keys with a keyboard hook instead (Snipline's PrtSc, for
    /// one) are invisible to this check, which is why the Global Keybinds tab also reads each app's own settings.
    /// </summary>
    public static class HotkeyProbe
    {
        const int ProbeId = 0xBF00;   // inside the application range 0x0000-0xBFFF, away from Macro Kit's own ids

        /// <summary>True when Windows let the keys be claimed (nobody had them), false when another program has them, null when Windows gave another error.</summary>
        public static bool? IsFree(Hotkey combo)
        {
            if (combo.IsEmpty) return null;
            if (Native.RegisterHotKey(IntPtr.Zero, ProbeId, HookDecoding.ToNativeModifiers(combo.Modifiers), combo.Vk))
            {
                Native.UnregisterHotKey(IntPtr.Zero, ProbeId);
                return true;
            }
            return Marshal.GetLastWin32Error() == Native.ERROR_HOTKEY_ALREADY_REGISTERED ? false : (bool?)null;
        }
    }
}
