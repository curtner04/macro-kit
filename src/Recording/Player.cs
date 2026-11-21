using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MacroKit.Recording
{
    using MacroKit.Core;

    public sealed class PlaybackProgress
    {
        public int Pass;
        public int TotalPasses;     // 0 = continuous
        public int EventIndex;
        public int EventCount;
        public int ElapsedMs;       // playback time in this pass (already scaled by the speed)
        public int DurationMs;      // length of one pass at the chosen speed
    }

    /// <summary>
    /// Plays a <see cref="Recording"/> through an <see cref="IInputSink"/>. Every event is scheduled against the start of its
    /// pass (start + T / speed) so timing errors never add up. Blocking: call it from a background thread.
    /// </summary>
    public sealed class Player
    {
        const double MinSpeed = 0.1;
        const double MaxSpeed = 100;
        const int MaxTimes = 100000;
        const int ProgressIntervalMs = 100;
        const int ModifierWaitMs = 3000;
        const int ModifierPollMs = 25;
        static readonly ushort[] ModifierKeys = { 0x10, 0x11, 0x12, 0x5B, 0x5C };   // Shift, Ctrl, Alt, left and right Win
        /// <summary>How long playback waits for a recorded window to appear (like {{WAITWINDOW}}, but shorter).</summary>
        public const int FindWindowMs = 5000;
        const int FindWindowPollMs = 100;
        const int ActivateMs = 2000;

        readonly IInputSink input;
        readonly IClock clock;
        readonly IWindowSystem windows;

        public event Action<PlaybackProgress> Progress;

        public Player(IInputSink input, IClock clock) : this(input, clock, null) { }

        /// <summary>With <paramref name="windows"/>, clicks of a recording that knows its windows follow those windows (PlaybackSettings.FollowWindows).</summary>
        public Player(IInputSink input, IClock clock, IWindowSystem windows)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (clock == null) throw new ArgumentNullException("clock");
            this.input = input;
            this.clock = clock;
            this.windows = windows;
        }

        /// <summary>A recorded window that is not open: playback stops instead of clicking where it used to be.</summary>
        sealed class WindowMissingException : Exception
        {
            public WindowMissingException(string message) : base(message) { }
        }

        /// <summary>Runs the recording as the settings say. Never throws: failures come back in <see cref="RunResult.Error"/>.</summary>
        public RunResult Play(Recording rec, PlaybackSettings settings, CancellationToken token)
        {
            return Play(rec, settings, token, false);
        }

        /// <summary>
        /// Like the three-argument Play. With <paramref name="waitForModifierRelease"/> (use it when a hotkey started the playback) it first
        /// waits up to 3 seconds, cancellable, until Shift, Ctrl, Alt and Win are physically released, so the hotkey's modifiers
        /// cannot turn the first recorded clicks and keys into shortcuts.
        /// </summary>
        public RunResult Play(Recording rec, PlaybackSettings settings, CancellationToken token, bool waitForModifierRelease)
        {
            var result = new RunResult();
            var held = new HeldInput();
            try
            {
                if (rec == null || rec.Events == null || rec.Events.Count == 0)
                {
                    result.Error = "This recording is empty, so there is nothing to play.";
                    return result;
                }
                if (settings == null) settings = new PlaybackSettings();
                if (waitForModifierRelease) WaitForModifiersReleased(token);

                double speed = settings.Speed;
                if (double.IsNaN(speed) || double.IsInfinity(speed)) speed = 1.0;
                speed = Math.Min(MaxSpeed, Math.Max(MinSpeed, speed));

                int totalPasses = settings.Repeat == RepeatMode.Continuous ? 0
                    : settings.Repeat == RepeatMode.Times ? Math.Min(MaxTimes, Math.Max(1, settings.RepeatTimes))
                    : 1;
                int delayMs = Math.Max(0, settings.RepeatDelayMs);

                List<RecEvent> events = rec.Events;
                bool follow = settings.FollowWindows && windows != null && rec.KnowsWindows;
                int lastT = Math.Max(rec.DurationMs, events[events.Count - 1].T);
                int passLengthMs = Scale(lastT, speed);

                bool reported = false;
                long lastProgress = 0;
                for (int pass = 1; totalPasses == 0 || pass <= totalPasses; pass++)
                {
                    token.ThrowIfCancellationRequested();
                    if (pass > 1 && delayMs > 0) clock.Sleep(delayMs, token);

                    long passStart = clock.NowMs;
                    int dx = 0, dy = 0;   // how far the window the clicks are in has moved since the recording
                    for (int i = 0; i < events.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        long due = passStart + Scale(events[i].T, speed);
                        long now = clock.NowMs;
                        if (now < due)
                        {
                            clock.Sleep((int)Math.Min(int.MaxValue, due - now), token);
                            now = clock.NowMs;
                        }

                        // Running late: skip a move when the next event is also a move that is already due, so the
                        // cursor catches up. Clicks, wheel and keys always go out.
                        if (events[i].Kind == RecKind.Move && i + 1 < events.Count && events[i + 1].Kind == RecKind.Move
                            && now >= passStart + Scale(events[i + 1].T, speed))
                            continue;

                        if (events[i].Kind == RecKind.Window)
                        {
                            // Waiting for the window does not make the rest of the pass hurry to catch up.
                            if (follow) passStart += FollowWindow(rec, events[i].A, token, ref dx, ref dy);
                            continue;
                        }
                        Apply(events[i], held, dx, dy);

                        if (!reported || now - lastProgress >= ProgressIntervalMs)
                        {
                            reported = true;
                            lastProgress = now;
                            Report(pass, totalPasses, i + 1, events.Count, now - passStart, passLengthMs);
                        }
                    }

                    // Let the quiet tail of the recording play out, then start the next pass from a clean state.
                    long passEnd = passStart + passLengthMs;
                    long left = passEnd - clock.NowMs;
                    if (left > 0) clock.Sleep((int)Math.Min(int.MaxValue, left), token);
                    else if (totalPasses == 0 && delayMs == 0) clock.Sleep(1, token);   // zero-length continuous loop must stay cancellable
                    held.ReleaseAll(input);

                    result.PassesDone = pass;
                    long finished = clock.NowMs;
                    if (pass == totalPasses || finished - lastProgress >= ProgressIntervalMs)
                    {
                        lastProgress = finished;
                        Report(pass, totalPasses, events.Count, events.Count, finished - passStart, passLengthMs);
                    }
                }
                result.Completed = true;
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
            }
            catch (WindowMissingException ex)
            {
                Log.Info("Playback stopped: " + ex.Message);
                result.Error = "Playback stopped: " + ex.Message;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) result.Cancelled = true;
                else
                {
                    Log.Error("Playback failed.", ex);
                    result.Error = "Playback stopped: " + ex.Message;
                }
            }
            finally
            {
                held.ReleaseAll(input);
            }
            // Windows refused to let go of a key or button (usually an administrator window came to the front), so it can stay
            // held for the whole PC. Say which, even after a run that otherwise finished: the person has to press it once.
            string stuck = held.StuckMessage();
            if (stuck != null)
            {
                if (!string.IsNullOrEmpty(result.Error)) result.Error += " " + char.ToUpperInvariant(stuck[0]) + stuck.Substring(1);
                else result.Error = (result.Cancelled ? "Playback stopped, but " : "Playback finished, but ") + stuck;
            }
            return result;
        }

        // The hotkey that started the playback may still be held; its modifiers would turn the first events into shortcuts.
        void WaitForModifiersReleased(CancellationToken token)
        {
            long deadline = clock.NowMs + ModifierWaitMs;
            while (AnyModifierDown() && clock.NowMs < deadline) clock.Sleep(ModifierPollMs, token);
        }

        bool AnyModifierDown()
        {
            foreach (ushort vk in ModifierKeys)
            {
                if (input.IsPhysicallyDown(vk)) return true;
            }
            return false;
        }

        static int Scale(int t, double speed)
        {
            return (int)Math.Round(Math.Max(0, t) / speed);
        }

        /// <summary>
        /// Finds the window the next clicks were made in (waiting up to <see cref="FindWindowMs"/>), brings it to the front and
        /// sets how far it moved since the recording. Returns how long that took, in ms. -1 (the screen itself) clears the shift.
        /// </summary>
        long FollowWindow(Recording rec, int index, CancellationToken token, ref int dx, ref int dy)
        {
            dx = 0;
            dy = 0;
            if (index < 0 || index >= rec.Windows.Count) return 0;
            RecWindow want = rec.Windows[index];
            long start = clock.NowMs;
            WindowInfo found;
            while ((found = FindRecordedWindow(windows, want)) == null)
            {
                if (clock.NowMs - start >= FindWindowMs)
                    throw new WindowMissingException("couldn't find " + want + ". Open it, or switch off Clicks follow their window to click where it used to be.");
                clock.Sleep(FindWindowPollMs, token);
            }
            if (!windows.Activate(found.Handle, ActivateMs)) Log.Warn("Playback could not bring " + want + " to the front; its clicks go on.");
            WindowInfo now = windows.Describe(found.Handle) ?? found;   // restoring a minimised window moves it
            dx = now.Left - want.Left;
            dy = now.Top - want.Top;
            return clock.NowMs - start;
        }

        /// <summary>
        /// The open window that is the recorded one: the same program with the same title; else a title that holds the other
        /// (Notepad's "*" for unsaved changes); else the program's only window. When several windows match equally (two
        /// Explorer windows on the same folder), the one nearest where the recorded window was. When the title matches none of
        /// the program's windows and it has several, only one still exactly where the recorded window was; else null.
        /// </summary>
        public static WindowInfo FindRecordedWindow(IWindowSystem windows, RecWindow want)
        {
            if (windows == null || want == null) return null;
            IList<WindowInfo> open = windows.ListSwitchableWindows() ?? new List<WindowInfo>();
            string program = (want.Program ?? "").Trim(), title = (want.Title ?? "").Trim();
            Func<WindowInfo, bool> sameProgram = w => program.Length == 0 || string.Equals((w.ProcessName ?? "").Trim(), program, StringComparison.OrdinalIgnoreCase);
            Func<WindowInfo, string> titleOf = w => (w.Title ?? "").Trim();
            WindowInfo exact = Nearest(open.Where(w => sameProgram(w) && titleOf(w) == title), want);
            if (exact != null) return exact;
            if (title.Length >= 3)
            {
                WindowInfo close = Nearest(open.Where(w => sameProgram(w) && titleOf(w).Length >= 3
                    && (titleOf(w).IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0 || title.IndexOf(titleOf(w), StringComparison.OrdinalIgnoreCase) >= 0)), want);
                if (close != null) return close;
            }
            if (program.Length == 0) return null;
            List<WindowInfo> ofProgram = open.Where(sameProgram).ToList();
            if (ofProgram.Count == 1) return ofProgram[0];
            List<WindowInfo> inPlace = ofProgram.Where(w => w.Left == want.Left && w.Top == want.Top && w.Width == want.Width && w.Height == want.Height).ToList();
            return inPlace.Count == 1 ? inPlace[0] : null;
        }

        // The candidate whose top-left is nearest the recorded window's (the first of those as near); null when there is none.
        static WindowInfo Nearest(IEnumerable<WindowInfo> candidates, RecWindow want)
        {
            WindowInfo best = null;
            long bestDistance = long.MaxValue;
            foreach (WindowInfo w in candidates)
            {
                long dx = (long)w.Left - want.Left, dy = (long)w.Top - want.Top, distance = dx * dx + dy * dy;
                if (distance < bestDistance) { best = w; bestDistance = distance; }
            }
            return best;
        }

        void Apply(RecEvent e, HeldInput held, int dx, int dy)
        {
            switch (e.Kind)
            {
                case RecKind.Move:
                    input.MouseMoveTo(e.A + dx, e.B + dy);
                    break;
                case RecKind.ButtonDown:
                case RecKind.ButtonUp:
                    if (e.A < (int)MouseButton.Left || e.A > (int)MouseButton.X2) return;   // not a button we know
                    var button = (MouseButton)e.A;
                    bool down = e.Kind == RecKind.ButtonDown;
                    if (down) held.Press(button);
                    input.MouseButton(button, down);
                    if (!down) held.Buttons.Remove(button);
                    break;
                case RecKind.Wheel:
                    input.MouseWheel(e.A, false);
                    break;
                case RecKind.HWheel:
                    input.MouseWheel(e.A, true);
                    break;
                case RecKind.KeyDown:
                    if (e.A <= 0 || e.A > 0xFFFF) return;
                    // A held key repeats in the recording; Windows does not auto-repeat injected keys, so every down is sent.
                    held.Press((ushort)e.A, e.B);
                    SendKey(input, (ushort)e.A, e.B, true);
                    break;
                case RecKind.KeyUp:
                    if (e.A <= 0 || e.A > 0xFFFF) return;
                    SendKey(input, (ushort)e.A, e.B, false);
                    held.Release((ushort)e.A);
                    break;
            }
        }

        // B holds the recorded scan code and extended flag (Recorder). Sent as recorded, numpad Enter stays numpad Enter and numpad
        // Home with Num Lock off stays numpad Home; worked out from the VK on Macro Kit's layout, both would become the main keys.
        // The scan is 8 bits: the hook reports AltGr's extra Ctrl as 0x21D. A held key is let go with the scan it was pressed with.
        static void SendKey(IInputSink sink, ushort vk, int b, bool down)
        {
            var scan = (ushort)(b & 0xFF);
            IRawKeySink raw = sink as IRawKeySink;
            if (raw == null || scan == 0)
            {
                if (down) sink.KeyDown(vk); else sink.KeyUp(vk);
                return;
            }
            bool extended = (b & 0x10000) != 0;
            if (down) raw.KeyDown(vk, scan, extended); else raw.KeyUp(vk, scan, extended);
        }

        void Report(int pass, int totalPasses, int eventIndex, int eventCount, long elapsedMs, int durationMs)
        {
            Action<PlaybackProgress> handler = Progress;
            if (handler == null) return;
            try
            {
                handler(new PlaybackProgress
                {
                    Pass = pass,
                    TotalPasses = totalPasses,
                    EventIndex = eventIndex,
                    EventCount = eventCount,
                    ElapsedMs = (int)Math.Max(0, Math.Min(int.MaxValue, elapsedMs)),
                    DurationMs = durationMs
                });
            }
            catch (Exception ex)
            {
                Log.Warn("A playback progress handler failed.", ex);
            }
        }

        /// <summary>
        /// Keys and buttons this playback pressed and has not released yet. Each appears once (a set that remembers press order),
        /// however many repeated downs were played, and one up releases it completely.
        /// </summary>
        sealed class HeldInput
        {
            public readonly List<ushort> Keys = new List<ushort>();
            public readonly List<MouseButton> Buttons = new List<MouseButton>();

            /// <summary>The recorded scan code and extended flag (RecEvent.B) each held key's latest down was sent with.</summary>
            readonly Dictionary<ushort, int> keyScans = new Dictionary<ushort, int>();

            public void Press(ushort key, int b) { if (!Keys.Contains(key)) Keys.Add(key); keyScans[key] = b; }

            public void Release(ushort key) { Keys.Remove(key); keyScans.Remove(key); }

            public void Press(MouseButton button) { if (!Buttons.Contains(button)) Buttons.Add(button); }

            /// <summary>What could not be released (Windows blocked it), by name, in the order it failed.</summary>
            public readonly List<string> NotReleased = new List<string>();

            /// <summary>Releases everything, most recent first. A release that fails does not stop the others; it is noted in <see cref="NotReleased"/>.</summary>
            public void ReleaseAll(IInputSink sink)
            {
                for (int i = Buttons.Count - 1; i >= 0; i--)
                {
                    try { sink.MouseButton(Buttons[i], false); }
                    catch (Exception ex) { Log.Warn("Could not release a mouse button.", ex); Note(ButtonName(Buttons[i])); }
                }
                Buttons.Clear();
                for (int i = Keys.Count - 1; i >= 0; i--)
                {
                    int b;
                    keyScans.TryGetValue(Keys[i], out b);
                    try { SendKey(sink, Keys[i], b, false); }
                    catch (Exception ex) { Log.Warn("Could not release a key.", ex); Note(KeyNames.Display(Keys[i])); }
                }
                Keys.Clear();
                keyScans.Clear();
            }

            void Note(string name) { if (!NotReleased.Contains(name)) NotReleased.Add(name); }

            static string ButtonName(MouseButton button)
            {
                switch (button)
                {
                    case MouseButton.Right: return "the right mouse button";
                    case MouseButton.Middle: return "the middle mouse button";
                    case MouseButton.X1: return "the back mouse button";
                    case MouseButton.X2: return "the forward mouse button";
                    default: return "the left mouse button";
                }
            }

            /// <summary>Which keys may still be held and what to do (it can start with "the", lower case), or null when every release went through.</summary>
            public string StuckMessage()
            {
                if (NotReleased.Count == 0) return null;
                string names = NotReleased.Count == 1 ? NotReleased[0]
                    : string.Join(", ", NotReleased.GetRange(0, NotReleased.Count - 1)) + " and " + NotReleased[NotReleased.Count - 1];
                return names + (NotReleased.Count == 1 ? " may still be held down; press and release it." : " may still be held down; press and release them.");
            }
        }
    }
}
