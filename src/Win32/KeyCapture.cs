using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using MacroKit.Core;

namespace MacroKit.Win32
{
    /// <summary>
    /// While one window is in front, takes the keys typed on the physical keyboard before Windows and other programs see them,
    /// so the key recorder also gets Alt+Tab, Win, PrtSc, Alt+F4 and keys another app has claimed as a hotkey. A low-level hook
    /// installed on the calling (UI) thread, which must pump messages; the newest low-level hook runs first, so it is installed
    /// only while the recorder is open. Keys typed while another window is in front, injected keys, and the release of a key that
    /// went down elsewhere pass through untouched. Windows still handles Ctrl+Alt+Delete and Win+L itself.
    /// </summary>
    public sealed class KeyCapture : IDisposable
    {
        readonly IntPtr window;
        readonly Action<ushort, bool, uint> onKey;
        readonly Native.HookProc proc;
        readonly HashSet<uint> taken = new HashSet<uint>();
        IntPtr hook;

        /// <param name="window">The window that must be in front for keys to be taken.</param>
        /// <param name="onKey">(virtual key, down, time in ms on the GetTickCount clock). Keep it short: it runs inside the hook.</param>
        public KeyCapture(IntPtr window, Action<ushort, bool, uint> onKey)
        {
            if (window == IntPtr.Zero) throw new ArgumentException("The window has no handle yet.", "window");
            this.window = window;
            this.onKey = onKey;
            proc = Callback;
            hook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandleW(null), 0);
            if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public void Dispose()
        {
            if (hook == IntPtr.Zero) return;
            Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        IntPtr Callback(int code, UIntPtr wParam, IntPtr lParam)
        {
            if (code == Native.HC_ACTION && hook != IntPtr.Zero)
            {
                try
                {
                    var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
                    int message = (int)wParam.ToUInt32();
                    bool down = message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN;
                    bool injected = (k.flags & (Native.LLKHF_INJECTED | Native.LLKHF_LOWER_IL_INJECTED)) != 0;
                    bool take = !injected && (down ? Native.GetForegroundWindow() == window : taken.Contains(k.vkCode));
                    if (take)
                    {
                        if (down) taken.Add(k.vkCode); else taken.Remove(k.vkCode);
                        onKey((ushort)k.vkCode, down, k.time);
                        return (IntPtr)1;
                    }
                }
                catch (Exception ex) { Log.Error("The key recorder's hook failed.", ex); }
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }
    }
}
