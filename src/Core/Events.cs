namespace MacroKit.Core
{
    /// <summary>
    /// One keyboard event from the low-level hook. Shared by the Phrases engine (text expansion), the Recorder and the Win32 hook.
    /// </summary>
    public struct KeyEvent
    {
        public ushort Vk;
        public ushort Scan;
        public bool Down;
        public bool Injected;
        public bool Extended;
        public long TimeMs;
    }

    public enum MouseEventKind { Move, Down, Up, Wheel, HWheel }

    /// <summary>One mouse event from the low-level hook. Shared by the Recorder and the Win32 hook.</summary>
    public struct MouseEvent
    {
        public MouseEventKind Kind;
        public MouseButton Button;
        public int X;
        public int Y;
        public int Delta;
        public bool Injected;
        public bool OverOwnWindow;
        public long TimeMs;
    }
}
