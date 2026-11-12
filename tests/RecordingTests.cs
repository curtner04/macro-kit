using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MacroKit.Tests
{
    // Inside the namespace so "Recording" is not taken by the MacroKit.Recording namespace.
    using MacroKit.Core;
    using MacroKit.Recording;
    using Rec = MacroKit.Core.Recording;

    /// <summary>Recorder and Player tests. Everything runs on a fake clock and a fake input sink: no real input, hooks or windows.</summary>
    public static class RecordingTests
    {
        const ushort VkA = 0x41, VkB = 0x42, VkC = 0x43, VkR = 0x52, VkS = 0x53;
        const ushort LShift = 0xA0, LControl = 0xA2, LMenu = 0xA4, RMenu = 0xA5, RControl = 0xA3, Control = 0x11;

        // ---------- helpers ----------

        static KeyEvent Key(ushort vk, bool down, bool injected = false, bool extended = false, ushort scan = 0)
        {
            return new KeyEvent { Vk = vk, Scan = scan, Down = down, Injected = injected, Extended = extended };
        }

        static MouseEvent Move(int x, int y, bool injected = false)
        {
            return new MouseEvent { Kind = MouseEventKind.Move, X = x, Y = y, Injected = injected };
        }

        static MouseEvent Button(bool down, int x, int y, MouseButton button = MouseButton.Left, bool ownWindow = false, bool injected = false)
        {
            return new MouseEvent { Kind = down ? MouseEventKind.Down : MouseEventKind.Up, Button = button, X = x, Y = y, OverOwnWindow = ownWindow, Injected = injected };
        }

        static MouseEvent Wheel(int delta, int x, int y, bool horizontal = false)
        {
            return new MouseEvent { Kind = horizontal ? MouseEventKind.HWheel : MouseEventKind.Wheel, Delta = delta, X = x, Y = y };
        }

        static string Fmt(RecEvent e) { return e.T + " " + e.Kind + " " + e.A + "," + e.B; }

        static List<string> Show(Rec rec) { return rec.Events.Select(Fmt).ToList(); }

        /// <summary>A recorder started at virtual time 1000 so event times are relative to Start.</summary>
        sealed class Session
        {
            public readonly FakeClock Clock = new FakeClock();
            public readonly Recorder Recorder;
            public Session(bool recordMoves = true)
            {
                Clock.NowMs = 1000;
                Recorder = new Recorder(Clock);
                Recorder.Start(recordMoves);
            }
            public Session At(int t) { Clock.NowMs = 1000 + t; return this; }
            public Session K(int t, ushort vk, bool down) { At(t); Recorder.OnKey(Key(vk, down)); return this; }
            public Session M(int t, MouseEvent e) { At(t); Recorder.OnMouse(e); return this; }
        }

        static Rec MakeRec(params RecEvent[] events)
        {
            var rec = new Rec();
            rec.Events = new List<RecEvent>(events);
            rec.DurationMs = events.Length == 0 ? 0 : events[events.Length - 1].T;
            return rec;
        }

        static RecEvent Ev(int t, RecKind kind, int a = 0, int b = 0) { return new RecEvent(t, kind, a, b); }

        sealed class PlayRun
        {
            public readonly FakeClock Clock = new FakeClock();
            public readonly FakeInputSink Sink = new FakeInputSink();
            public readonly List<long> Times = new List<long>();
            public readonly List<PlaybackProgress> Reports = new List<PlaybackProgress>();
            public readonly Player Player;
            public PlayRun()
            {
                Sink.OnCall = line => Times.Add(Clock.NowMs);
                Player = new Player(Sink, Clock);
                Player.Progress += p => Reports.Add(p);
            }
            public RunResult Play(Rec rec, PlaybackSettings settings, CancellationToken token = default(CancellationToken))
            {
                return Player.Play(rec, settings, token);
            }
        }

        static PlaybackSettings Settings(double speed = 1.0, RepeatMode repeat = RepeatMode.Once, int times = 2, int delay = 0)
        {
            return new PlaybackSettings { Speed = speed, Repeat = repeat, RepeatTimes = times, RepeatDelayMs = delay };
        }

        // ---------- Recorder: basics ----------

        [Test]
        public static void RecorderRecordsKeysAndMouseWithTimes()
        {
            var s = new Session();
            Assert.True(s.Recorder.IsRecording, "recording after Start");
            s.At(10); s.Recorder.OnKey(Key(VkA, true, scan: 0x1E));
            s.At(20); s.Recorder.OnKey(Key(VkA, false, scan: 0x1E));
            s.M(30, Move(5, 6));
            s.M(40, Button(true, 5, 6));
            s.M(50, Button(false, 5, 6));
            Rec rec = s.Recorder.Stop(null);
            Assert.SequenceEqual(new[] { "10 KeyDown 65,30", "20 KeyUp 65,30", "30 Move 5,6", "40 ButtonDown 0,0", "50 ButtonUp 0,0" }, Show(rec));
            Assert.Equal(50, rec.DurationMs);
            Assert.False(s.Recorder.IsRecording, "stopped");
        }

        [Test]
        public static void RecorderKeepsExtendedFlagAndScanInB()
        {
            var s = new Session();
            s.At(5); s.Recorder.OnKey(Key(0x27, true, extended: true, scan: 0x4D));
            s.At(9); s.Recorder.OnKey(Key(0x27, false, extended: true, scan: 0x4D));
            Rec rec = s.Recorder.Stop(null);
            Assert.Equal(0x1004D, rec.Events[0].B);
            Assert.Equal(0x1004D, rec.Events[1].B);
            Assert.Equal(0x27, rec.Events[0].A);
        }

        [Test]
        public static void RecorderTimesAreRelativeToStart()
        {
            var clock = new FakeClock { NowMs = 987654 };
            var r = new Recorder(clock);
            r.Start(true);
            clock.NowMs += 250;
            r.OnKey(Key(VkA, true));
            Rec rec = r.Stop(null);
            Assert.Equal(250, rec.Events[0].T);
            Assert.Equal(250, rec.DurationMs);
        }

        [Test]
        public static void RecorderIgnoresInjectedEvents()
        {
            var s = new Session();
            s.At(5); s.Recorder.OnKey(Key(VkA, true, injected: true));
            s.At(6); s.Recorder.OnKey(Key(VkA, false, injected: true));
            s.M(7, Move(1, 1, injected: true));
            s.M(8, Button(true, 1, 1, injected: true));
            s.M(9, Button(false, 1, 1, injected: true));
            s.M(10, new MouseEvent { Kind = MouseEventKind.Wheel, Delta = 120, Injected = true });
            Rec rec = s.Recorder.Stop(null);
            Assert.Equal(0, rec.Events.Count);
            Assert.Equal(0, rec.DurationMs);
        }

        [Test]
        public static void RecorderIgnoresEventsWhenNotRecording()
        {
            var clock = new FakeClock();
            var r = new Recorder(clock);
            Assert.False(r.IsRecording);
            r.OnKey(Key(VkA, true));
            r.OnMouse(Move(1, 2));
            r.Start(true);
            Assert.True(r.IsRecording);
            Rec rec = r.Stop(null);
            Assert.Equal(0, rec.Events.Count);
            r.OnKey(Key(VkA, true));
            Rec again = r.Stop(null);
            Assert.NotNull(again, "Stop without Start still returns a recording");
            Assert.Equal(0, again.Events.Count);
        }

        [Test]
        public static void RecorderStartDiscardsPreviousRecording()
        {
            var s = new Session();
            s.K(10, VkA, true);
            s.Recorder.Start(true);
            s.K(30, VkB, true);
            Rec rec = s.Recorder.Stop(null);
            Assert.Equal(1, rec.Events.Count);
            Assert.Equal(VkB, rec.Events[0].A);
            Assert.Equal(20, rec.Events[0].T, "time restarts at the new Start");
        }

        [Test]
        public static void RecorderDefaultNameCreatedAndId()
        {
            var s = new Session();
            Rec rec = s.Recorder.Stop(null);
            Assert.True(Regex.IsMatch(rec.Name, @"^Recording \d{4}-\d{2}-\d{2} \d{2}:\d{2}$"), "default name: " + rec.Name);
            DateTimeOffset parsed;
            Assert.True(DateTimeOffset.TryParse(rec.Created, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed), "Created is ISO: " + rec.Created);
            Assert.Contains(rec.Created, "T");
            Assert.True(!string.IsNullOrEmpty(rec.Id));
            Assert.Equal(1, rec.Schema);
            Assert.Equal(0, rec.DurationMs);
        }

        // ---------- Recorder: keys ----------

        [Test]
        public static void RecorderDropsOrphanKeyUpsAtStart()
        {
            // Keys held when recording began (the record hotkey): their ups arrive without a recorded down.
            var s = new Session();
            s.K(5, LControl, false).K(6, LShift, false).K(7, LMenu, false).K(8, VkR, false);
            s.K(20, VkA, true).K(30, VkA, false);
            Rec rec = s.Recorder.Stop(null);
            Assert.SequenceEqual(new[] { "20 KeyDown 65,0", "30 KeyUp 65,0" }, Show(rec));
        }

        [Test]
        public static void RecorderKeepsUpWhenDownWasRecorded()
        {
            var s = new Session();
            s.K(1, LControl, true).K(2, VkC, true).K(3, VkC, false).K(4, LControl, false);
            Rec rec = s.Recorder.Stop(null);
            Assert.Equal(4, rec.Events.Count);
            Assert.Equal(RecKind.KeyUp, rec.Events[3].Kind);
        }

        [Test]
        public static void RecorderThrottlesAutoRepeatDowns()
        {
            var s = new Session();
            s.K(0, VkA, true).K(10, VkA, true).K(20, VkA, true).K(30, VkA, true).K(45, VkA, true).K(70, VkA, true).K(80, VkA, false);
            Rec rec = s.Recorder.Stop(null);
            // Kept: 0, 30 (30 ms after 0), 70 (40 ms after 30); 10, 20 and 45 are inside the 30 ms window.
            Assert.SequenceEqual(new[] { "0 KeyDown 65,0", "30 KeyDown 65,0", "70 KeyDown 65,0", "80 KeyUp 65,0" }, Show(rec));
        }

        [Test]
        public static void RecorderThrottleIsPerKey()
        {
            var s = new Session();
            s.K(0, VkA, true).K(5, VkB, true).K(10, VkA, true).K(12, VkB, true);
            Rec rec = s.Recorder.Stop(null);
            Assert.Equal(2, rec.Events.Count);
        }

        [Test]
        public static void RecorderRecordsQuickRetypeOfSameKey()
        {
            // Down, up, down again within 30 ms is a real second press, not auto-repeat.
            var s = new Session();
            s.K(0, VkA, true).K(5, VkA, false).K(10, VkA, true).K(15, VkA, false);
            Assert.Equal(4, s.Recorder.Stop(null).Events.Count);
        }

        // ---------- Recorder: hotkey stripping ----------

        [Test]
        public static void RecorderStripsStopHotkeyAtEnd()
        {
            var s = new Session();
            s.K(10, VkA, true).K(20, VkA, false);
            s.K(100, LControl, true).K(101, LShift, true).K(102, LMenu, true).K(103, VkR, true);
            s.K(150, VkR, false).K(151, LMenu, false).K(152, LShift, false).K(153, LControl, false);
            Hotkey hk;
            Assert.True(Hotkey.TryParse("Ctrl+Shift+Alt+R", out hk));
            Rec rec = s.Recorder.Stop(new[] { hk });
            Assert.SequenceEqual(new[] { "10 KeyDown 65,0", "20 KeyUp 65,0" }, Show(rec));
            Assert.Equal(20, rec.DurationMs);
        }

        [Test]
        public static void RecorderStripsGenericAndRightModifierForms()
        {
            var s = new Session();
            s.K(10, VkB, true).K(20, VkB, false);
            s.K(100, Control, true).K(101, RMenu, true).K(102, 0x10, true).K(103, VkR, true);
            s.K(110, VkR, false).K(111, RControl, false).K(112, 0x12, false).K(113, 0x10, false).K(114, Control, false).K(115, RMenu, false);
            Hotkey hk;
            Assert.True(Hotkey.TryParse("Ctrl+Shift+Alt+R", out hk));
            Rec rec = s.Recorder.Stop(new[] { hk });
            Assert.Equal(2, rec.Events.Count);
            Assert.Equal(VkB, rec.Events[0].A);
        }

        [Test]
        public static void RecorderStripsWinHotkeyKeys()
        {
            var s = new Session();
            s.K(10, VkA, true).K(20, VkA, false).K(50, 0x5B, true).K(51, 0x31, true).K(60, 0x31, false).K(61, 0x5B, false);
            Hotkey hk = new Hotkey(HotkeyModifiers.Win, 0x31);
            Rec rec = s.Recorder.Stop(new[] { hk });
            Assert.Equal(2, rec.Events.Count);
        }

        [Test]
        public static void RecorderKeepsEarlierShortcutsWithSameModifiers()
        {
            // Ctrl+C in the middle stays; only the run of hotkey keys at the very end is removed.
            var s = new Session();
            s.K(10, LControl, true).K(11, VkC, true).K(12, VkC, false).K(13, LControl, false);
            s.K(30, VkB, true).K(31, VkB, false);
            s.K(100, LControl, true).K(101, LShift, true).K(102, LMenu, true).K(103, VkR, true).K(110, VkR, false);
            Hotkey hk;
            Hotkey.TryParse("Ctrl+Shift+Alt+R", out hk);
            Rec rec = s.Recorder.Stop(new[] { hk });
            Assert.Equal(6, rec.Events.Count);
            Assert.Equal(31, rec.DurationMs);
        }

        [Test]
        public static void RecorderStripStopsAtFirstOtherEvent()
        {
            var s = new Session();
            s.K(10, VkR, true).K(20, VkR, false);   // an R press the user really recorded...
            s.K(30, VkB, true).K(40, VkB, false);   // ...followed by other keys
            s.K(50, VkR, true).K(60, VkR, false);   // trailing R belongs to the hotkey
            Hotkey hk = new Hotkey(HotkeyModifiers.Ctrl, VkR);
            Rec rec = s.Recorder.Stop(new[] { hk });
            Assert.Equal(4, rec.Events.Count);
            Assert.Equal(40, rec.DurationMs);
        }

        [Test]
        public static void RecorderStripsSeveralHotkeys()
        {
            var s = new Session();
            s.K(10, VkA, true).K(20, VkA, false);
            s.K(50, LControl, true).K(51, VkS, true).K(52, VkS, false).K(53, LControl, false);
            Rec rec = s.Recorder.Stop(new[] { new Hotkey(HotkeyModifiers.Ctrl, VkR), new Hotkey(HotkeyModifiers.Ctrl, VkS) });
            Assert.Equal(2, rec.Events.Count);
        }

        [Test]
        public static void RecorderStripHandlesNullAndEmptyHotkeys()
        {
            var s = new Session();
            s.K(10, LControl, true).K(20, LControl, false);
            Rec a = s.Recorder.Stop(null);
            Assert.Equal(2, a.Events.Count);
            s.Recorder.Start(true);
            s.K(10, LControl, true).K(20, LControl, false);
            Rec b = s.Recorder.Stop(new Hotkey[0]);
            Assert.Equal(2, b.Events.Count);
            s.Recorder.Start(true);
            s.K(10, LControl, true).K(20, LControl, false);
            Rec c = s.Recorder.Stop(new[] { new Hotkey() });
            Assert.Equal(2, c.Events.Count, "an empty hotkey strips nothing");
        }

        [Test]
        public static void RecorderStripStepsOverTrailingMoves()
        {
            var s = new Session();
            s.K(10, VkA, true).K(20, VkA, false);
            s.K(50, LControl, true).K(51, VkR, true);
            s.M(60, Move(9, 9));
            s.K(70, VkR, false).K(71, LControl, false);
            Rec rec = s.Recorder.Stop(new[] { new Hotkey(HotkeyModifiers.Ctrl, VkR) });
            // The move between the hotkey's keys is trailing once the hotkey is gone, so it goes too.
            Assert.SequenceEqual(new[] { "10 KeyDown 65,0", "20 KeyUp 65,0" }, Show(rec));
            Assert.Equal(20, rec.DurationMs);
        }

        [Test]
        public static void RecorderStopDropsMovesAfterTheLastRealEvent()
        {
            var s = new Session();
            s.M(10, Move(1, 1));
            s.M(20, Button(true, 5, 5));
            s.M(30, Button(false, 5, 5));
            s.M(100, Move(50, 50)).M(120, Move(300, 20)).M(150, Move(900, 900));   // heading for the Stop button
            Rec rec = s.Recorder.Stop(null);
            Assert.SequenceEqual(new[] { "10 Move 1,1", "20 Move 5,5", "20 ButtonDown 0,0", "30 ButtonUp 0,0" }, Show(rec));
            Assert.Equal(30, rec.DurationMs, "the quiet tail is gone from the length too");
        }

        [Test]
        public static void RecorderStopKeepsMovesThatComeBeforeALaterEvent()
        {
            var s = new Session();
            s.M(10, Move(1, 1)).M(40, Move(2, 2));
            s.K(60, VkA, true).K(70, VkA, false);
            Rec rec = s.Recorder.Stop(null);
            Assert.SequenceEqual(new[] { "10 Move 1,1", "40 Move 2,2", "60 KeyDown 65,0", "70 KeyUp 65,0" }, Show(rec));
            Assert.Equal(70, rec.DurationMs);
        }

        [Test]
        public static void RecorderStopWithOnlyMovesGivesAnEmptyRecording()
        {
            var s = new Session();
            s.M(10, Move(1, 1)).M(30, Move(9, 9));
            Rec rec = s.Recorder.Stop(null);
            Assert.Equal(0, rec.Events.Count);
            Assert.Equal(0, rec.DurationMs);
        }

        [Test]
        public static void RecorderStopDropsMovesBehindAStrippedHotkeyToo()
        {
            var s = new Session();
            s.K(10, VkA, true).K(20, VkA, false);
            s.M(30, Move(4, 4));
            s.K(50, LControl, true).K(51, VkR, true).K(60, VkR, false).K(61, LControl, false);
            s.M(80, Move(7, 7));
            Rec rec = s.Recorder.Stop(new[] { new Hotkey(HotkeyModifiers.Ctrl, VkR) });
            Assert.SequenceEqual(new[] { "10 KeyDown 65,0", "20 KeyUp 65,0" }, Show(rec));
            Assert.Equal(20, rec.DurationMs);
        }

        // ---------- Recorder: mouse ----------

        [Test]
        public static void RecorderMergesMovesWithinOneMillisecond()
        {
            var s = new Session();
            s.M(10, Move(1, 1)).M(10, Move(2, 2)).M(10, Move(3, 3));
            s.M(30, Move(4, 4));
            s.M(30, Move(5, 5));
            s.K(50, VkA, true);
            Rec rec = s.Recorder.Stop(null);
            Assert.SequenceEqual(new[] { "10 Move 3,3", "30 Move 5,5", "50 KeyDown 65,0" }, Show(rec));
        }

        [Test]
        public static void RecorderCoalescesMovesCloserThanEightMilliseconds()
        {
            var s = new Session();
            s.M(10, Move(1, 1)).M(12, Move(2, 2)).M(17, Move(3, 3));   // all within 8 ms of the move at 10: one event, newest position
            s.M(18, Move(4, 4));                                        // 8 ms after it: a new event
            s.M(26, Move(5, 5));                                        // 8 ms after that: another
            s.K(40, VkA, true);
            Rec rec = s.Recorder.Stop(null);
            Assert.SequenceEqual(new[] { "10 Move 3,3", "18 Move 4,4", "26 Move 5,5", "40 KeyDown 65,0" }, Show(rec));
        }

        [Test]
        public static void RecorderCoalescingKeepsTheExactPositionBeforeAClick()
        {
            var s = new Session();
            s.M(10, Move(1, 1)).M(12, Move(2, 2)).M(14, Move(3, 3));   // merged into one move at 10
            s.M(15, Button(true, 3, 3));                                // the click is at the merged position: no extra move
            s.M(16, Move(8, 8));                                        // a move right after a click is a new event (the click sits between)
            s.M(17, Button(false, 9, 9));                               // up at a position the moves did not reach: a move to it first
            Rec rec = s.Recorder.Stop(null);
            Assert.SequenceEqual(new[]
            {
                "10 Move 3,3", "15 ButtonDown 0,0", "16 Move 8,8", "17 Move 9,9", "17 ButtonUp 0,0"
            }, Show(rec));
        }

        [Test]
        public static void RecorderHighRateMovesStayFewEvents()
        {
            var s = new Session();
            for (int t = 0; t < 2000; t++) s.M(t, Move(t, t));          // a 1000 Hz mouse for two seconds
            s.K(2100, VkA, true);
            Rec rec = s.Recorder.Stop(null);
            Assert.True(rec.Events.Count <= 2000 / 8 + 3, "about one move per 8 ms, got " + rec.Events.Count);
            Assert.Equal(1999, rec.Events[rec.Events.Count - 2].A, "the last position is kept");
        }

        [Test]
        public static void RecorderKeepsMovesEightMillisecondsApart()
        {
            var s = new Session();
            s.M(10, Move(1, 1)).M(18, Move(2, 2)).M(26, Move(3, 3));
            s.K(40, VkA, true);
            Assert.Equal(4, s.Recorder.Stop(null).Events.Count);
        }

        [Test]
        public static void RecorderSkipsMoveToSamePosition()
        {
            var s = new Session();
            s.M(10, Move(1, 1)).M(20, Move(1, 1)).M(30, Move(2, 2));
            s.K(40, VkA, true);
            Assert.Equal(3, s.Recorder.Stop(null).Events.Count);
        }

        [Test]
        public static void RecorderWritesMoveBeforeClickWhenPositionChanged()
        {
            var s = new Session();
            s.M(10, Move(10, 10));
            s.M(20, Button(true, 30, 40));
            s.M(30, Button(false, 30, 40));
            Rec rec = s.Recorder.Stop(null);
            Assert.SequenceEqual(new[] { "10 Move 10,10", "20 Move 30,40", "20 ButtonDown 0,0", "30 ButtonUp 0,0" }, Show(rec));
        }

        [Test]
        public static void RecorderWithoutMoveRecordingStillPositionsClicks()
        {
            var s = new Session(recordMoves: false);
            s.M(5, Move(1, 1)).M(6, Move(2, 2)).M(7, Move(300, 400));
            s.M(10, Button(true, 300, 400));
            s.M(20, Button(false, 300, 400));
            s.M(30, Move(5, 5)).M(40, Move(6, 6));
            s.M(50, Button(true, 6, 6, MouseButton.Right));
            s.M(60, Button(false, 6, 6, MouseButton.Right));
            Rec rec = s.Recorder.Stop(null);
            Assert.SequenceEqual(new[]
            {
                "10 Move 300,400", "10 ButtonDown 0,0", "20 ButtonUp 0,0",
                "50 Move 6,6", "50 ButtonDown 1,0", "60 ButtonUp 1,0"
            }, Show(rec));
        }

        [Test]
        public static void RecorderWithoutMoveRecordingWritesNoPlainMoves()
        {
            var s = new Session(recordMoves: false);
            for (int i = 0; i < 20; i++) s.M(i * 5, Move(i, i));
            Assert.Equal(0, s.Recorder.Stop(null).Events.Count);
        }

        [Test]
        public static void RecorderDragMovesBetweenDownAndUp()
        {
            var s = new Session(recordMoves: false);
            s.M(10, Button(true, 10, 10));
            s.M(30, Move(50, 50));
            s.M(40, Button(false, 60, 70));
            Rec rec = s.Recorder.Stop(null);
            Assert.SequenceEqual(new[] { "10 Move 10,10", "10 ButtonDown 0,0", "40 Move 60,70", "40 ButtonUp 0,0" }, Show(rec));
        }

        [Test]
        public static void RecorderRecordsAllButtons()
        {
            var s = new Session();
            s.M(1, Button(true, 0, 0, MouseButton.Middle)).M(2, Button(false, 0, 0, MouseButton.Middle));
            s.M(3, Button(true, 0, 0, MouseButton.X1)).M(4, Button(false, 0, 0, MouseButton.X1));
            Rec rec = s.Recorder.Stop(null);
            Assert.Equal((int)MouseButton.Middle, rec.Events.First(e => e.Kind == RecKind.ButtonDown).A);
            Assert.Equal((int)MouseButton.X1, rec.Events.Last(e => e.Kind == RecKind.ButtonUp).A);
        }

        [Test]
        public static void RecorderRecordsWheelAtItsPosition()
        {
            var s = new Session(recordMoves: false);
            s.M(10, Wheel(120, 7, 8));
            s.M(20, Wheel(-120, 7, 8, horizontal: true));
            Rec rec = s.Recorder.Stop(null);
            Assert.SequenceEqual(new[] { "10 Move 7,8", "10 Wheel 120,0", "20 HWheel -120,0" }, Show(rec));
        }

        [Test]
        public static void RecorderDropsClickOverOwnWindowWithItsUp()
        {
            var s = new Session();
            s.M(10, Button(true, 100, 100, ownWindow: true));     // the click on the Stop/Record button
            s.M(20, Button(false, 100, 100, ownWindow: true));
            s.M(30, Button(true, 500, 500));                      // a real click elsewhere
            s.M(40, Button(false, 500, 500));
            Rec rec = s.Recorder.Stop(null);
            Assert.SequenceEqual(new[] { "30 Move 500,500", "30 ButtonDown 0,0", "40 ButtonUp 0,0" }, Show(rec));
        }

        [Test]
        public static void RecorderDropsUpEvenWhenReleasedElsewhere()
        {
            var s = new Session();
            s.M(10, Button(true, 100, 100, ownWindow: true));
            s.M(20, Button(false, 900, 900, ownWindow: false));   // dragged out of our window before releasing
            Assert.Equal(0, s.Recorder.Stop(null).Events.Count);
        }

        [Test]
        public static void RecorderDropsOwnWindowClickPerButton()
        {
            var s = new Session();
            s.M(10, Button(true, 5, 5, MouseButton.Left, ownWindow: true));
            s.M(15, Button(true, 800, 800, MouseButton.Right));
            s.M(20, Button(false, 5, 5, MouseButton.Left, ownWindow: true));
            s.M(25, Button(false, 800, 800, MouseButton.Right));
            Rec rec = s.Recorder.Stop(null);
            Assert.Equal(1, rec.Events.Count(e => e.Kind == RecKind.ButtonDown));
            Assert.Equal((int)MouseButton.Right, rec.Events.First(e => e.Kind == RecKind.ButtonDown).A);
            Assert.Equal(1, rec.Events.Count(e => e.Kind == RecKind.ButtonUp));
        }

        [Test]
        public static void RecorderDropsOrphanButtonUp()
        {
            var s = new Session();
            s.M(10, Button(false, 5, 5));   // button was already held when recording began
            Assert.Equal(0, s.Recorder.Stop(null).Events.Count);
        }

        [Test]
        public static void RecorderWheelOverOwnWindowIsDropped()
        {
            var s = new Session();
            var w = Wheel(120, 3, 3);
            w.OverOwnWindow = true;
            s.M(10, w);
            Assert.Equal(0, s.Recorder.Stop(null).Events.Count);
        }

        // ---------- Recorder: threads ----------

        [Test]
        public static void RecorderIsSafeAcrossThreads()
        {
            var recorder = new Recorder(new SystemClock());
            recorder.Start(true);
            Exception error = null;
            Action keys = () =>
            {
                try { for (int i = 0; i < 1000; i++) { recorder.OnKey(Key(VkA, true)); recorder.OnKey(Key(VkA, false)); } }
                catch (Exception e) { error = e; }
            };
            Action mouse = () =>
            {
                try { for (int i = 0; i < 2000; i++) { recorder.OnMouse(Move(i, i)); recorder.OnMouse(Button(true, i, i)); recorder.OnMouse(Button(false, i, i)); } }
                catch (Exception e) { error = e; }
            };
            Task a = Task.Factory.StartNew(keys), b = Task.Factory.StartNew(mouse);
            Task.WaitAll(a, b);
            Rec mid = recorder.Stop(null);
            Assert.Null(error);
            Assert.Equal(2000, mid.Events.Count(e => e.Kind == RecKind.KeyDown || e.Kind == RecKind.KeyUp));
            Assert.Equal(2000, mid.Events.Count(e => e.Kind == RecKind.ButtonDown));
            Assert.Equal(2000, mid.Events.Count(e => e.Kind == RecKind.ButtonUp));
        }

        [Test]
        public static void RecorderStopWhileEventsArriveDoesNotThrow()
        {
            var recorder = new Recorder(new SystemClock());
            recorder.Start(true);
            Exception error = null;
            bool stop = false;
            Task feeder = Task.Factory.StartNew(() =>
            {
                try { int i = 0; while (!stop) { recorder.OnKey(Key(VkA, true)); recorder.OnKey(Key(VkA, false)); recorder.OnMouse(Move(i++ % 500, 1)); } }
                catch (Exception e) { error = e; }
            });
            Thread.Sleep(20);
            Rec rec = recorder.Stop(null);
            stop = true;
            feeder.Wait();
            Assert.Null(error);
            Assert.NotNull(rec);
            Assert.False(recorder.IsRecording);
        }

        // ---------- Player: order and timing ----------

        static Rec SampleRec()
        {
            return MakeRec(
                Ev(0, RecKind.Move, 10, 10),
                Ev(100, RecKind.ButtonDown, (int)MouseButton.Left),
                Ev(150, RecKind.ButtonUp, (int)MouseButton.Left),
                Ev(200, RecKind.KeyDown, VkA, 0x1E),
                Ev(260, RecKind.KeyUp, VkA, 0x1E),
                Ev(300, RecKind.Wheel, 120),
                Ev(350, RecKind.HWheel, -120));
        }

        static readonly string[] SampleLog =
        {
            "moveto 10,10", "button Left down", "button Left up", "down A", "up A", "wheel 120 v", "wheel -120 h"
        };

        [Test]
        public static void PlayerPlaysEventsInOrderAtNormalSpeed()
        {
            var run = new PlayRun();
            RunResult result = run.Play(SampleRec(), Settings());
            Assert.SequenceEqual(SampleLog, run.Sink.Log);
            Assert.SequenceEqual(new long[] { 0, 100, 150, 200, 260, 300, 350 }, run.Times);
            Assert.True(result.Completed);
            Assert.False(result.Cancelled);
            Assert.Null(result.Error);
            Assert.Equal(1, result.PassesDone);
        }

        [Test]
        public static void PlayerStartsFromCurrentClockTime()
        {
            var run = new PlayRun();
            run.Clock.NowMs = 5000;
            run.Play(SampleRec(), Settings());
            Assert.SequenceEqual(new long[] { 5000, 5100, 5150, 5200, 5260, 5300, 5350 }, run.Times);
        }

        [Test]
        public static void PlayerDoubleSpeedHalvesTheTimes()
        {
            var run = new PlayRun();
            run.Play(SampleRec(), Settings(speed: 2));
            Assert.SequenceEqual(new long[] { 0, 50, 75, 100, 130, 150, 175 }, run.Times);
            Assert.SequenceEqual(SampleLog, run.Sink.Log);
        }

        [Test]
        public static void PlayerHalfSpeedDoublesTheTimes()
        {
            var run = new PlayRun();
            run.Play(SampleRec(), Settings(speed: 0.5));
            Assert.SequenceEqual(new long[] { 0, 200, 300, 400, 520, 600, 700 }, run.Times);
        }

        [Test]
        public static void PlayerClampsSpeedToRange()
        {
            var fast = new PlayRun();
            fast.Play(MakeRec(Ev(0, RecKind.Move, 1, 1), Ev(1000, RecKind.Move, 2, 2)), Settings(speed: 1000));
            Assert.SequenceEqual(new long[] { 0, 10 }, fast.Times);   // 100x at most

            var slow = new PlayRun();
            slow.Play(MakeRec(Ev(0, RecKind.Move, 1, 1), Ev(1000, RecKind.Move, 2, 2)), Settings(speed: 0));
            Assert.SequenceEqual(new long[] { 0, 10000 }, slow.Times);   // 0.1x at least

            var nan = new PlayRun();
            nan.Play(MakeRec(Ev(0, RecKind.Move, 1, 1), Ev(1000, RecKind.Move, 2, 2)), Settings(speed: double.NaN));
            Assert.SequenceEqual(new long[] { 0, 1000 }, nan.Times);

            var negative = new PlayRun();
            negative.Play(MakeRec(Ev(0, RecKind.Move, 1, 1), Ev(1000, RecKind.Move, 2, 2)), Settings(speed: -5));
            Assert.SequenceEqual(new long[] { 0, 10000 }, negative.Times);
        }

        [Test]
        public static void PlayerAbsoluteScheduleDoesNotDriftWhenSinkIsSlow()
        {
            // Each call costs 3 ms of virtual time, yet event times stay on the recorded schedule.
            var run = new PlayRun();
            run.Sink.OnCall = line => { run.Times.Add(run.Clock.NowMs); run.Clock.Advance(3); };
            var events = new List<RecEvent>();
            for (int i = 0; i < 20; i++) events.Add(Ev(i * 50, RecKind.Wheel, 120));
            run.Play(MakeRec(events.ToArray()), Settings());
            for (int i = 0; i < 20; i++) Assert.Equal(i * 50L, run.Times[i], "event " + i);
        }

        [Test]
        public static void PlayerWaitsOutTheQuietTailOfTheRecording()
        {
            var run = new PlayRun();
            Rec rec = MakeRec(Ev(0, RecKind.Move, 1, 1), Ev(100, RecKind.Move, 2, 2));
            rec.DurationMs = 500;
            run.Play(rec, Settings());
            Assert.Equal(500L, run.Clock.NowMs);
        }

        [Test]
        public static void PlayerSendsMovesThroughMouseMoveTo()
        {
            var run = new PlayRun();
            run.Play(MakeRec(Ev(0, RecKind.Move, -50, 2000), Ev(10, RecKind.Move, 7, 8)), Settings());
            Assert.SequenceEqual(new[] { "moveto -50,2000", "moveto 7,8" }, run.Sink.Log);
            Assert.Equal(7, run.Sink.CursorPosition.X);
        }

        [Test]
        public static void PlayerSkipsUnknownEventValues()
        {
            var run = new PlayRun();
            Rec rec = MakeRec(Ev(0, RecKind.ButtonDown, 99), Ev(1, RecKind.KeyDown, 0), Ev(2, RecKind.KeyUp, 70000), Ev(3, RecKind.Wheel, 120));
            RunResult result = run.Play(rec, Settings());
            Assert.SequenceEqual(new[] { "wheel 120 v" }, run.Sink.Log);
            Assert.True(result.Completed);
        }

        // ---------- Player: running late ----------

        static Rec LateRec()
        {
            return MakeRec(
                Ev(0, RecKind.Move, 0, 0), Ev(10, RecKind.Move, 10, 0), Ev(20, RecKind.Move, 20, 0), Ev(30, RecKind.Move, 30, 0),
                Ev(40, RecKind.ButtonDown, (int)MouseButton.Left), Ev(45, RecKind.ButtonUp, (int)MouseButton.Left),
                Ev(50, RecKind.Move, 50, 0), Ev(60, RecKind.Move, 60, 0),
                Ev(70, RecKind.KeyDown, VkA), Ev(80, RecKind.KeyUp, VkA));
        }

        [Test]
        public static void PlayerCoalescesMovesWhenBehindButKeepsFinalPositionAndClicks()
        {
            var run = new PlayRun();
            run.Sink.OnCall = line => run.Clock.Advance(50);   // every call is slow: the player falls behind
            RunResult result = run.Play(LateRec(), Settings());
            Assert.True(result.Completed);
            Assert.SequenceEqual(new[] { "moveto 0,0", "moveto 30,0", "button Left down", "button Left up", "moveto 60,0", "down A", "up A" }, run.Sink.Log);
            Assert.Equal(60, run.Sink.CursorPosition.X, "final cursor position kept");
        }

        [Test]
        public static void PlayerNeverDropsClicksWheelOrKeysWhenFarBehind()
        {
            var run = new PlayRun();
            run.Sink.OnCall = line => run.Clock.Advance(1000);
            var events = new List<RecEvent>();
            for (int i = 0; i < 10; i++)
            {
                events.Add(Ev(i * 10, RecKind.ButtonDown, 0));
                events.Add(Ev(i * 10 + 1, RecKind.ButtonUp, 0));
                events.Add(Ev(i * 10 + 2, RecKind.Wheel, 120));
                events.Add(Ev(i * 10 + 3, RecKind.KeyDown, VkB));
                events.Add(Ev(i * 10 + 4, RecKind.KeyUp, VkB));
            }
            run.Play(MakeRec(events.ToArray()), Settings());
            Assert.Equal(50, run.Sink.Log.Count);
        }

        [Test]
        public static void PlayerOnTimeDoesNotSkipAnyMove()
        {
            var run = new PlayRun();
            var events = new List<RecEvent>();
            for (int i = 0; i < 50; i++) events.Add(Ev(i * 5, RecKind.Move, i, i));
            run.Play(MakeRec(events.ToArray()), Settings());
            Assert.Equal(50, run.Sink.Log.Count);
        }

        // ---------- Player: repeat modes ----------

        static Rec TapRec() { return MakeRec(Ev(0, RecKind.KeyDown, VkA), Ev(100, RecKind.KeyUp, VkA)); }

        [Test]
        public static void PlayerOnceIgnoresRepeatSettings()
        {
            var run = new PlayRun();
            RunResult result = run.Play(TapRec(), Settings(repeat: RepeatMode.Once, times: 9, delay: 500));
            Assert.Equal(2, run.Sink.Log.Count);
            Assert.Equal(1, result.PassesDone);
            Assert.Equal(100L, run.Clock.NowMs);
        }

        [Test]
        public static void PlayerTimesRepeatsWithDelayBetweenPassesOnly()
        {
            var run = new PlayRun();
            RunResult result = run.Play(TapRec(), Settings(repeat: RepeatMode.Times, times: 3, delay: 500));
            Assert.SequenceEqual(new long[] { 0, 100, 600, 700, 1200, 1300 }, run.Times);
            Assert.Equal(3, result.PassesDone);
            Assert.True(result.Completed);
            Assert.Equal(1300L, run.Clock.NowMs, "no delay after the last pass");
        }

        [Test]
        public static void PlayerTimesWithoutDelayRunsBackToBack()
        {
            var run = new PlayRun();
            run.Play(TapRec(), Settings(repeat: RepeatMode.Times, times: 3, delay: 0));
            Assert.SequenceEqual(new long[] { 0, 100, 100, 200, 200, 300 }, run.Times);
        }

        [Test]
        public static void PlayerTimesClampsCount()
        {
            var one = new PlayRun();
            Assert.Equal(1, one.Play(TapRec(), Settings(repeat: RepeatMode.Times, times: 0)).PassesDone);
            var negative = new PlayRun();
            Assert.Equal(1, negative.Play(TapRec(), Settings(repeat: RepeatMode.Times, times: -4)).PassesDone);
        }

        [Test]
        public static void PlayerContinuousRunsUntilCancelled()
        {
            var run = new PlayRun();
            var cts = new CancellationTokenSource();
            run.Clock.At(1050, cts.Cancel);
            RunResult result = run.Play(TapRec(), Settings(repeat: RepeatMode.Continuous, delay: 0), cts.Token);
            Assert.True(result.Cancelled);
            Assert.False(result.Completed);
            Assert.Equal(10, result.PassesDone);
            Assert.True(run.Sink.HeldKeys.Count == 0, "key released");
            Assert.Equal(11, run.Sink.Log.Count(l => l == "down A"));
            Assert.Equal(11, run.Sink.Log.Count(l => l == "up A"), "cancelled pass is closed by the release");
        }

        [Test]
        public static void PlayerContinuousWithDelayCancelsDuringDelay()
        {
            var run = new PlayRun();
            var cts = new CancellationTokenSource();
            run.Clock.At(300, cts.Cancel);   // pass 1 ends at 100, the 500 ms delay is under way
            RunResult result = run.Play(TapRec(), Settings(repeat: RepeatMode.Continuous, delay: 500), cts.Token);
            Assert.True(result.Cancelled);
            Assert.Equal(1, result.PassesDone);
            Assert.Equal(300L, run.Clock.NowMs, "cancellation is immediate");
        }

        [Test]
        public static void PlayerContinuousZeroLengthRecordingStaysCancellable()
        {
            var run = new PlayRun();
            var cts = new CancellationTokenSource();
            run.Clock.At(20, cts.Cancel);
            RunResult result = run.Play(MakeRec(Ev(0, RecKind.Wheel, 120)), Settings(repeat: RepeatMode.Continuous), cts.Token);
            Assert.True(result.Cancelled);
            Assert.True(result.PassesDone >= 5, "it looped");
        }

        // ---------- Player: cancellation and cleanup ----------

        [Test]
        public static void PlayerCancelMidWaitReleasesHeldKeysAndButtons()
        {
            var run = new PlayRun();
            var cts = new CancellationTokenSource();
            Rec rec = MakeRec(
                Ev(0, RecKind.KeyDown, LControl), Ev(0, RecKind.KeyDown, VkC),
                Ev(0, RecKind.ButtonDown, (int)MouseButton.Left), Ev(0, RecKind.ButtonDown, (int)MouseButton.Right),
                Ev(5000, RecKind.ButtonUp, (int)MouseButton.Left), Ev(5000, RecKind.KeyUp, VkC));
            run.Clock.At(500, cts.Cancel);
            RunResult result = run.Play(rec, Settings(), cts.Token);
            Assert.True(result.Cancelled);
            Assert.Equal(0, result.PassesDone);
            Assert.Equal(0, run.Sink.HeldKeys.Count);
            Assert.Equal(0, run.Sink.HeldButtons.Count);
            Assert.Equal(500L, run.Clock.NowMs, "stops at once, not at the next event");
            // Buttons first (newest first), then keys (newest first).
            Assert.SequenceEqual(new[] { "button Right up", "button Left up", "up C", "up LCONTROL" }, run.Sink.Log.Skip(4));
        }

        [Test]
        public static void PlayerAlreadyCancelledDoesNothing()
        {
            var run = new PlayRun();
            var cts = new CancellationTokenSource();
            cts.Cancel();
            RunResult result = run.Play(SampleRec(), Settings(), cts.Token);
            Assert.True(result.Cancelled);
            Assert.Equal(0, run.Sink.Log.Count);
        }

        [Test]
        public static void PlayerReleasesKeysLeftDownAtTheEndOfARecording()
        {
            var run = new PlayRun();
            Rec rec = MakeRec(Ev(0, RecKind.KeyDown, 0x10), Ev(10, RecKind.ButtonDown, (int)MouseButton.Middle));
            RunResult result = run.Play(rec, Settings());
            Assert.True(result.Completed);
            Assert.Equal(0, run.Sink.HeldKeys.Count);
            Assert.Equal(0, run.Sink.HeldButtons.Count);
        }

        [Test]
        public static void PlayerReleasesHeldInputBetweenPasses()
        {
            var run = new PlayRun();
            Rec rec = MakeRec(Ev(0, RecKind.KeyDown, VkA), Ev(10, RecKind.Wheel, 120));
            run.Play(rec, Settings(repeat: RepeatMode.Times, times: 2));
            Assert.SequenceEqual(new[] { "down A", "wheel 120 v", "up A", "down A", "wheel 120 v", "up A" }, run.Sink.Log);
        }

        [Test]
        public static void PlayerDoesNotReleaseWhatItNeverPressed()
        {
            var run = new PlayRun();
            run.Play(MakeRec(Ev(0, RecKind.KeyUp, VkA), Ev(5, RecKind.Wheel, 120)), Settings());
            Assert.Equal(0, run.Sink.Log.Count(l => l == "button Left up"));
            Assert.Equal(1, run.Sink.Log.Count(l => l == "up A"), "only the recorded up");
        }

        [Test]
        public static void PlayerReportsBlockedInputAsErrorAndStillReleases()
        {
            var run = new PlayRun();
            run.Sink.FailWhen = line => line == "moveto 5,5";
            Rec rec = MakeRec(Ev(0, RecKind.KeyDown, VkA), Ev(10, RecKind.Move, 5, 5), Ev(20, RecKind.KeyUp, VkA));
            RunResult result = run.Play(rec, Settings());
            Assert.False(result.Completed);
            Assert.False(result.Cancelled);
            Assert.NotNull(result.Error);
            Assert.Contains(result.Error, "Input blocked");
            Assert.Equal(0, run.Sink.HeldKeys.Count);
            Assert.SequenceEqual(new[] { "down A", "up A" }, run.Sink.Log);
        }

        [Test]
        public static void PlayerKeepsReleasingWhenOneReleaseFails()
        {
            var run = new PlayRun();
            run.Sink.FailWhen = line => line == "button Left up";
            Rec rec = MakeRec(Ev(0, RecKind.KeyDown, VkA), Ev(1, RecKind.ButtonDown, 0), Ev(2, RecKind.Wheel, 120));
            RunResult result = run.Play(rec, Settings());
            Assert.True(result.Completed);   // the pass itself finished
            Assert.Equal(0, run.Sink.HeldKeys.Count, "the key was still released");
        }

        [Test]
        public static void PlayerEmptyAndNullRecordingsGiveAnErrorNotAnException()
        {
            var run = new PlayRun();
            RunResult empty = run.Play(new Rec(), Settings());
            Assert.False(empty.Completed);
            Assert.NotNull(empty.Error);
            RunResult none = run.Play(null, Settings());
            Assert.NotNull(none.Error);
            var noEvents = new Rec();
            noEvents.Events = null;
            Assert.NotNull(run.Play(noEvents, Settings()).Error);
            Assert.Equal(0, run.Sink.Log.Count);
        }

        [Test]
        public static void PlayerNullSettingsUseDefaults()
        {
            var run = new PlayRun();
            RunResult result = run.Play(TapRec(), null);
            Assert.True(result.Completed);
            Assert.Equal(1, result.PassesDone);
        }

        // ---------- Player: progress ----------

        [Test]
        public static void PlayerProgressIsThrottledAndEndsComplete()
        {
            var run = new PlayRun();
            var events = new List<RecEvent>();
            for (int i = 0; i < 300; i++) events.Add(Ev(i * 10, RecKind.Move, i, 0));
            run.Play(MakeRec(events.ToArray()), Settings());
            Assert.True(run.Reports.Count >= 20 && run.Reports.Count <= 40, "about 10 per second over 3 s, got " + run.Reports.Count);
            PlaybackProgress first = run.Reports[0], last = run.Reports[run.Reports.Count - 1];
            Assert.Equal(1, first.Pass);
            Assert.Equal(1, first.TotalPasses);
            Assert.Equal(300, first.EventCount);
            Assert.Equal(last.EventCount, last.EventIndex);
            Assert.Equal(2990, last.DurationMs);
        }

        [Test]
        public static void PlayerProgressIsThrottledAcrossManyFastPasses()
        {
            var run = new PlayRun();
            run.Play(TapRec(), Settings(repeat: RepeatMode.Times, times: 100));   // 10 s of playback
            Assert.True(run.Reports.Count <= 115, "got " + run.Reports.Count);
            Assert.Equal(100, run.Reports[run.Reports.Count - 1].Pass);
        }

        [Test]
        public static void PlayerProgressSaysZeroTotalPassesWhenContinuous()
        {
            var run = new PlayRun();
            var cts = new CancellationTokenSource();
            run.Clock.At(250, cts.Cancel);
            run.Play(TapRec(), Settings(repeat: RepeatMode.Continuous), cts.Token);
            Assert.True(run.Reports.Count > 0);
            Assert.True(run.Reports.All(r => r.TotalPasses == 0));
        }

        [Test]
        public static void PlayerProgressDurationFollowsSpeed()
        {
            var run = new PlayRun();
            run.Play(TapRec(), Settings(speed: 2));
            Assert.Equal(50, run.Reports[0].DurationMs);
        }

        [Test]
        public static void PlayerSurvivesAFailingProgressHandler()
        {
            var run = new PlayRun();
            run.Player.Progress += p => { throw new InvalidOperationException("handler bug"); };
            RunResult result = run.Play(TapRec(), Settings());
            Assert.True(result.Completed);
            Assert.Null(result.Error);
        }

        [Test]
        public static void PlayerWorksWithoutProgressSubscribers()
        {
            var clock = new FakeClock();
            var sink = new FakeInputSink();
            RunResult result = new Player(sink, clock).Play(TapRec(), Settings(), CancellationToken.None);
            Assert.True(result.Completed);
        }

        // ---------- Record then play, and storage ----------

        [Test]
        public static void RecordedSessionPlaysBackAsRecorded()
        {
            var s = new Session(recordMoves: true);
            s.M(0, Move(100, 100));
            s.K(20, VkA, true).K(60, VkA, false);
            s.M(80, Button(true, 100, 100));
            s.M(120, Button(false, 100, 100));
            Rec rec = s.Recorder.Stop(null);
            var run = new PlayRun();
            run.Play(rec, Settings());
            Assert.SequenceEqual(new[] { "moveto 100,100", "down A", "up A", "button Left down", "button Left up" }, run.Sink.Log);
            Assert.SequenceEqual(new long[] { 0, 20, 60, 80, 120 }, run.Times);
        }

        [Test]
        public static void StorageRoundTripKeepsRecordedEventsExactly()
        {
            var s = new Session();
            s.M(1, Move(-1920, 40));
            s.K(10, LControl, true).K(12, VkC, true).K(14, VkC, false).K(16, LControl, false);
            s.At(20); s.Recorder.OnKey(Key(0x27, true, extended: true, scan: 0x4D));
            s.At(25); s.Recorder.OnKey(Key(0x27, false, extended: true, scan: 0x4D));
            s.M(30, Button(true, 3000, 2000, MouseButton.X2)).M(35, Button(false, 3000, 2000, MouseButton.X2));
            s.M(40, Wheel(-240, 3000, 2000)).M(45, Wheel(120, 3000, 2000, horizontal: true));
            Rec rec = s.Recorder.Stop(null);
            rec.Name = "Round trip é";
            int count = rec.Events.Count;
            Assert.True(count >= 12, "enough events to be meaningful: " + count);

            using (var dir = new TempDir())
            {
                var storage = new Storage(dir.Path);
                storage.SaveRecording(rec);
                Rec back = new Storage(dir.Path).LoadRecording(rec.Id);   // a fresh Storage reads from disk
                Assert.NotNull(back);
                Assert.SequenceEqual(Show(rec), Show(back));
                Assert.Equal(rec.DurationMs, back.DurationMs);
                Assert.Equal(rec.Name, back.Name);
                Assert.Equal(1, storage.LoadRecordingIndex().Count);
                Assert.Equal(count, storage.LoadRecordingIndex()[0].EventCount);

                // And the loaded recording plays back exactly like the original.
                var a = new PlayRun();
                var b = new PlayRun();
                a.Play(rec, Settings());
                b.Play(back, Settings());
                Assert.SequenceEqual(a.Sink.Log, b.Sink.Log);
                Assert.SequenceEqual(a.Times, b.Times);
            }
        }

        // ---------- Player: waiting for the hotkey's modifiers ----------

        static Rec OneClick()
        {
            return MakeRec(Ev(0, RecKind.ButtonDown, (int)MouseButton.Left), Ev(10, RecKind.ButtonUp, (int)MouseButton.Left));
        }

        [Test]
        public static void PlayerWaitsUntilTheHotkeyModifiersAreReleased()
        {
            var run = new PlayRun();
            run.Sink.PhysicallyDown.Add(LControl);
            run.Sink.PhysicallyDown.Add(LShift);
            run.Clock.At(500, () => run.Sink.PhysicallyDown.Remove(LControl));
            run.Clock.At(700, () => run.Sink.PhysicallyDown.Remove(LShift));
            RunResult result = run.Player.Play(OneClick(), Settings(), CancellationToken.None, true);
            Assert.True(result.Completed);
            Assert.True(run.Times[0] >= 700, "nothing is sent while a modifier is still down, first call at " + run.Times[0]);
            Assert.True(run.Times[0] < 760, "and it starts right after the release, first call at " + run.Times[0]);
            Assert.SequenceEqual(new[] { "button Left down", "button Left up" }, run.Sink.Log);
        }

        [Test]
        public static void PlayerChecksEveryModifierKey()
        {
            foreach (ushort vk in new ushort[] { 0x5B, 0x5C, 0xA4, 0xA5, 0xA3, 0xA1 })
            {
                var run = new PlayRun();
                run.Sink.PhysicallyDown.Add(vk);
                run.Clock.At(200, () => run.Sink.PhysicallyDown.Remove(vk));
                run.Player.Play(OneClick(), Settings(), CancellationToken.None, true);
                Assert.True(run.Times[0] >= 200, "waited for key 0x" + vk.ToString("X"));
            }
        }

        [Test]
        public static void PlayerStopsWaitingAfterThreeSeconds()
        {
            var run = new PlayRun();
            run.Sink.PhysicallyDown.Add(LControl);   // never released
            RunResult result = run.Player.Play(OneClick(), Settings(), CancellationToken.None, true);
            Assert.True(result.Completed, "it plays anyway");
            Assert.True(run.Times[0] >= 3000 && run.Times[0] <= 3050, "gave up at about 3 s, first call at " + run.Times[0]);
        }

        [Test]
        public static void PlayerModifierWaitCanBeCancelled()
        {
            var run = new PlayRun();
            run.Sink.PhysicallyDown.Add(LControl);
            var cts = new CancellationTokenSource();
            run.Clock.At(300, cts.Cancel);
            RunResult result = run.Player.Play(OneClick(), Settings(), cts.Token, true);
            Assert.True(result.Cancelled);
            Assert.False(result.Completed);
            Assert.Equal(0, run.Sink.Log.Count);
            Assert.True(run.Clock.NowMs < 400, "the wait ended right at the cancel");
        }

        [Test]
        public static void PlayerDoesNotWaitUnlessAsked()
        {
            var run = new PlayRun();
            run.Sink.PhysicallyDown.Add(LControl);
            run.Player.Play(OneClick(), Settings(), CancellationToken.None);
            Assert.Equal(0L, run.Times[0]);
            var other = new PlayRun();
            other.Sink.PhysicallyDown.Add(LControl);
            other.Player.Play(OneClick(), Settings(), CancellationToken.None, false);
            Assert.Equal(0L, other.Times[0]);
        }

        [Test]
        public static void PlayerWithNothingHeldStartsAtOnceEvenWhenAsked()
        {
            var run = new PlayRun();
            run.Player.Play(OneClick(), Settings(), CancellationToken.None, true);
            Assert.Equal(0L, run.Times[0]);
            Assert.False(run.Clock.Sleeps.Contains(25), "no modifier polling");
        }

        // ---------- Player: held keys are a set ----------

        [Test]
        public static void PlayerReleasesAHeldKeyOnceNoMatterHowManyRepeatsWereRecorded()
        {
            var run = new PlayRun();
            // A held key records one down per auto-repeat and a single up; Ctrl is never released in the recording.
            Rec rec = MakeRec(
                Ev(0, RecKind.KeyDown, LControl),
                Ev(10, RecKind.KeyDown, VkA), Ev(50, RecKind.KeyDown, VkA), Ev(90, RecKind.KeyDown, VkA), Ev(130, RecKind.KeyDown, VkA),
                Ev(170, RecKind.KeyUp, VkA),
                Ev(180, RecKind.KeyDown, LControl), Ev(190, RecKind.KeyDown, LControl));
            run.Play(rec, Settings());
            Assert.Equal(4, run.Sink.Log.Count(l => l == "down " + FakeInputSink.Name(VkA)), "every recorded down is played: Windows does not auto-repeat injected keys");
            Assert.Equal(1, run.Sink.Log.Count(l => l == "up " + FakeInputSink.Name(VkA)), "the key goes up exactly once, at its own up event");
            Assert.Equal(1, run.Sink.Log.Count(l => l == "up LCONTROL"), "Ctrl is let go once, not once per repeat");
            Assert.Equal(0, run.Sink.HeldKeys.Count, "nothing stays down");
        }

        [Test]
        public static void PlayerDoesNotReleaseAKeyItAlreadyReleased()
        {
            var run = new PlayRun();
            Rec rec = MakeRec(Ev(0, RecKind.KeyDown, VkA), Ev(10, RecKind.KeyDown, VkA), Ev(20, RecKind.KeyUp, VkA));
            run.Play(rec, Settings(repeat: RepeatMode.Times, times: 3));
            Assert.Equal(3, run.Sink.Log.Count(l => l == "up " + FakeInputSink.Name(VkA)), "one up per pass, none extra at the pass ends or the finish");
        }

        [Test]
        public static void PlayerCancelReleasesEachHeldKeyAndButtonOnce()
        {
            var run = new PlayRun();
            Rec rec = MakeRec(
                Ev(0, RecKind.KeyDown, LShift), Ev(10, RecKind.KeyDown, LShift), Ev(20, RecKind.KeyDown, LShift),
                Ev(30, RecKind.ButtonDown, (int)MouseButton.Left), Ev(40, RecKind.ButtonDown, (int)MouseButton.Left),
                Ev(5000, RecKind.KeyUp, LShift));
            var cts = new CancellationTokenSource();
            run.Clock.At(1000, cts.Cancel);
            RunResult result = run.Play(rec, Settings(), cts.Token);
            Assert.True(result.Cancelled);
            Assert.Equal(1, run.Sink.Log.Count(l => l == "up LSHIFT"));
            Assert.Equal(1, run.Sink.Log.Count(l => l == "button Left up"));
            Assert.Equal(0, run.Sink.HeldKeys.Count + run.Sink.HeldButtons.Count);
        }

        [Test]
        public static void PlayerMouseButtonDownRepeatsAreReleasedOnce()
        {
            var run = new PlayRun();
            Rec rec = MakeRec(Ev(0, RecKind.ButtonDown, (int)MouseButton.Right), Ev(10, RecKind.ButtonDown, (int)MouseButton.Right));
            run.Play(rec, Settings());
            Assert.Equal(1, run.Sink.Log.Count(l => l == "button Right up"));
        }
    }
}
