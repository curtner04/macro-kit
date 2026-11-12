using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using MacroKit.Core;
using Button = MacroKit.Core.MouseButton;

namespace MacroKit.Win32
{
    /// <summary>
    /// The real keyboard and mouse output (SendInput / SetCursorPos). Every event carries dwExtraInfo "MKIT" and is marked
    /// injected by Windows, so Macro Kit's own hook ignores it. Safe to call from any thread.
    /// </summary>
    public sealed class SendInputSink : IInputSink
    {
        const int ComboHoldMs = 15;
        static readonly Func<ushort, ushort> scanFor = InputPackets.ScanFor;

        public void KeyDown(ushort vk) { Send(new[] { InputPackets.Key(vk, true, InputPackets.ScanFor(vk)) }); }

        public void KeyUp(ushort vk) { Send(new[] { InputPackets.Key(vk, false, InputPackets.ScanFor(vk)) }); }

        public void KeyCombo(IList<ushort> keys)
        {
            if (keys == null || keys.Count == 0) return;
            try
            {
                Send(InputPackets.ComboDown(keys, scanFor));
                Thread.Sleep(ComboHoldMs);
            }
            finally
            {
                // Release even when the downs failed half way, so a modifier is never left stuck.
                Send(InputPackets.ComboUp(keys, scanFor));
            }
        }

        public void TypeText(string text)
        {
            foreach (Native.INPUT[] batch in InputPackets.TextBatches(text, scanFor)) Send(batch);
        }

        /// <summary>
        /// SetCursorPos takes physical pixels because the app manifest declares PerMonitorV2 DPI awareness; without that
        /// Windows would scale the coordinates and the cursor would land in the wrong place on a scaled monitor.
        /// </summary>
        public void MouseMoveTo(int x, int y)
        {
            if (!Native.SetCursorPos(x, y)) throw new InvalidOperationException("Windows would not move the mouse pointer.", new Win32Exception(Marshal.GetLastWin32Error()));
        }

        /// <summary>GetCursorPos + SetCursorPos moves by exact pixels; a relative MOUSEEVENTF_MOVE would be bent by pointer acceleration.</summary>
        public void MouseMoveBy(int dx, int dy)
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) throw new InvalidOperationException("Windows would not tell where the mouse pointer is.", new Win32Exception(Marshal.GetLastWin32Error()));
            MouseMoveTo(p.X + dx, p.Y + dy);
        }

        public void MouseButton(Button button, bool down) { Send(new[] { InputPackets.MouseButtonEvent(button, down) }); }

        public void MouseWheel(int delta, bool horizontal) { Send(new[] { InputPackets.Wheel(delta, horizontal) }); }

        public ScreenPoint CursorPosition
        {
            get
            {
                Native.POINT p;
                return Native.GetCursorPos(out p) ? new ScreenPoint(p.X, p.Y) : new ScreenPoint(0, 0);
            }
        }

        /// <summary>
        /// GetAsyncKeyState. It reports keys held by injected input too, so it is only "physical" when nothing is being sent;
        /// the runner uses it for the user's own Esc and for waiting until hotkey modifiers are released.
        /// </summary>
        public bool IsPhysicallyDown(ushort vk)
        {
            return (Native.GetAsyncKeyState(KeyNames.BaseVk(vk)) & 0x8000) != 0;
        }

        public void MouseMoveNormalized(int x65535, int y65535) { Send(new[] { InputPackets.MoveNormalized(x65535, y65535) }); }

        static void Send(Native.INPUT[] packets)
        {
            if (packets.Length == 0) return;
            uint sent = Native.SendInput((uint)packets.Length, packets, Marshal.SizeOf(typeof(Native.INPUT)));
            if (sent < packets.Length)
            {
                // SendInput returns fewer events than asked when UIPI blocks it (the focused app runs as administrator) or another thread holds input.
                throw new InvalidOperationException("Windows blocked the input (the active window may be running as administrator).",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }
        }
    }
}
