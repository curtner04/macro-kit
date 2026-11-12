using System;
using MacroKit.Core;

namespace MacroKit.Win32
{
    /// <summary>
    /// Works out which characters a physical key-down produces (the "typed" text handed to the Phrases engine).
    /// Used from the hook thread only, so it keeps one reusable key-state array and allocates only when a character is produced.
    /// </summary>
    internal sealed class KeyTranslator
    {
        readonly byte[] keyState = new byte[256];
        readonly char[] buffer = new char[8];
        bool deadKeyPending;
        char deadAccent;

        /// <summary>Forgets a half-typed dead-key sequence (the foreground window changed, so it no longer applies).</summary>
        public void Reset() { deadKeyPending = false; }

        /// <summary>
        /// Characters produced by this key-down, or null when it produces none (modifiers, shortcuts, navigation keys, a dead key
        /// waiting for its partner). Only call for real (non-injected) key-downs.
        /// </summary>
        public string Translate(ushort vk, ushort scan)
        {
            // Pressing only a modifier or a lock key neither types nor cancels a pending dead key (Shift between ´ and e is normal).
            if (KeyNames.IsModifier(vk) || vk == Native.VK_CAPITAL || vk == 0x90 || vk == 0x91) return null;

            // GetAsyncKeyState, not GetKeyState: inside a low-level hook the thread's own key state has not seen the in-flight
            // keystrokes yet, while the async state already includes the modifiers pressed before this key.
            bool shift = IsDown(Native.VK_SHIFT);
            bool ctrl = IsDown(Native.VK_CONTROL);
            bool alt = IsDown(Native.VK_MENU);
            bool win = IsDown(Native.VK_LWIN) || IsDown(Native.VK_RWIN);

            // Ctrl or Alt alone, or Win, make a shortcut rather than text. Ctrl+Alt together is AltGr on many layouts, so it is
            // passed to the layout, which decides (layouts without AltGr give nothing or a control character).
            if (win || ctrl != alt)
            {
                deadKeyPending = false;
                return null;
            }

            Array.Clear(keyState, 0, keyState.Length);
            if (shift) keyState[Native.VK_SHIFT] = 0x80;
            if (ctrl && alt)
            {
                keyState[Native.VK_CONTROL] = 0x80;
                keyState[Native.VK_MENU] = 0x80;
            }
            // The Caps Lock toggle is bit 0 of GetKeyState; GetAsyncKeyState does not report toggles.
            if ((Native.GetKeyState(Native.VK_CAPITAL) & 1) != 0) keyState[Native.VK_CAPITAL] = 0x01;

            // Layout of the focused window's thread, not ours (a hook thread has no meaningful layout).
            uint pid;
            uint thread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            IntPtr layout = Native.GetKeyboardLayout(thread);

            // Flag 4 (Windows 10 1607+): do not change the keyboard state. Without it, translating a dead key here would consume
            // it and the real application would then type a plain letter instead of the accented one.
            int count = Native.ToUnicodeEx(vk, (uint)(scan & 0x7FFF), keyState, buffer, buffer.Length, Native.ToUnicodeNoStateChange, layout);

            if (count < 0)
            {
                // A dead key: remember its accent; nothing is typed until the next character.
                deadKeyPending = true;
                deadAccent = buffer[0];
                return null;
            }
            if (count == 0)
            {
                deadKeyPending = false;
                return null;
            }
            string typed = new string(buffer, 0, Math.Min(count, buffer.Length));
            if (deadKeyPending)
            {
                deadKeyPending = false;
                typed = HookDecoding.ComposeDead(deadAccent, typed);
            }
            return typed;
        }

        static bool IsDown(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }
    }
}
