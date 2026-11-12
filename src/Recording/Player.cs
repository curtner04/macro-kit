using System;
using System.Collections.Generic;
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

        readonly IInputSink input;
        readonly IClock clock;

        public event Action<PlaybackProgress> Progress;

        public Player(IInputSink input, IClock clock)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (clock == null) throw new ArgumentNullException("clock");
            this.input = input;
            this.clock = clock;
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
                int lastT = Math.Max(rec.DurationMs, events[events.Count - 1].T);
                int passLengthMs = Scale(lastT, speed);

                bool reported = false;
                long lastProgress = 0;
                for (int pass = 1; totalPasses == 0 || pass <= totalPasses; pass++)
                {
                    token.ThrowIfCancellationRequested();
                    if (pass > 1 && delayMs > 0) clock.Sleep(delayMs, token);

                    long passStart = clock.NowMs;
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

                        Apply(events[i], held);

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

        void Apply(RecEvent e, HeldInput held)
        {
            switch (e.Kind)
            {
                case RecKind.Move:
                    input.MouseMoveTo(e.A, e.B);
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
                    held.Press((ushort)e.A);
                    input.KeyDown((ushort)e.A);
                    break;
                case RecKind.KeyUp:
                    if (e.A <= 0 || e.A > 0xFFFF) return;
                    input.KeyUp((ushort)e.A);
                    held.Keys.Remove((ushort)e.A);
                    break;
            }
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

            public void Press(ushort key) { if (!Keys.Contains(key)) Keys.Add(key); }

            public void Press(MouseButton button) { if (!Buttons.Contains(button)) Buttons.Add(button); }

            /// <summary>Releases everything, most recent first. A release that fails does not stop the others.</summary>
            public void ReleaseAll(IInputSink sink)
            {
                for (int i = Buttons.Count - 1; i >= 0; i--)
                {
                    try { sink.MouseButton(Buttons[i], false); } catch (Exception ex) { Log.Warn("Could not release a mouse button.", ex); }
                }
                Buttons.Clear();
                for (int i = Keys.Count - 1; i >= 0; i--)
                {
                    try { sink.KeyUp(Keys[i]); } catch (Exception ex) { Log.Warn("Could not release a key.", ex); }
                }
                Keys.Clear();
            }
        }
    }
}
