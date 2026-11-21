using System;
using System.Collections.Generic;
using System.Linq;

namespace MacroKit.Tests
{
    using MacroKit.Core;
    using MacroKit.Recording;
    using Rec = MacroKit.Core.Recording;

    /// <summary>1.11.0: trimming a recording's start and end, and shortening its long pauses.</summary>
    public static class RecordingEditTests
    {
        const int L = (int)MouseButton.Left;
        static RecEvent E(int t, RecKind kind, int a = 0, int b = 0) { return new RecEvent(t, kind, a, b); }
        static string Fmt(RecEvent e) { return e.T + " " + e.Kind + " " + e.A + "," + e.B; }

        static Rec Make(int duration, params RecEvent[] events)
        {
            var rec = new Rec { Name = "Form", Created = "2026-11-20T09:00:00-05:00", DurationMs = duration };
            rec.Events.AddRange(events);
            return rec;
        }

        [Test]
        public static void TrimKeepsTheMiddleMovedToZero()
        {
            Rec rec = Make(5000,
                E(500, RecKind.Move, 10, 10), E(500, RecKind.ButtonDown, L), E(550, RecKind.ButtonUp, L),
                E(2000, RecKind.KeyDown, 'A', 30), E(2050, RecKind.KeyUp, 'A', 30),
                E(4000, RecKind.Move, 20, 20), E(4000, RecKind.ButtonDown, L), E(4050, RecKind.ButtonUp, L));
            Rec cut = RecordingEdit.Trim(rec, 1000, 3000);
            Assert.SequenceEqual(new[] { "0 Move 10,10", "1000 KeyDown 65,30", "1050 KeyUp 65,30" }, cut.Events.Select(Fmt));
            Assert.Equal(2000, cut.DurationMs);
            Assert.Equal(rec.Id, cut.Id);
            Assert.Equal("Form", cut.Name);
            Assert.Equal(8, rec.Events.Count, "the original is left alone, for Undo");
            Assert.Equal(5000, rec.DurationMs);
        }

        [Test]
        public static void TrimFromZeroAddsNothingInFront()
        {
            Rec rec = Make(3000, E(100, RecKind.KeyDown, 'A'), E(150, RecKind.KeyUp, 'A'), E(2500, RecKind.KeyDown, 'B'), E(2550, RecKind.KeyUp, 'B'));
            Rec cut = RecordingEdit.Trim(rec, 0, 1000);
            Assert.SequenceEqual(new[] { "100 KeyDown 65,0", "150 KeyUp 65,0" }, cut.Events.Select(Fmt));
            Assert.Equal(1000, cut.DurationMs);
        }

        [Test]
        public static void TrimDropsReleasesOfWhatWasPressedBeforeTheCutAndReleasesWhatIsStillHeld()
        {
            Rec rec = Make(4000,
                E(100, RecKind.KeyDown, 0xA0, 0x2A),            // Shift down before the cut...
                E(1500, RecKind.KeyUp, 0xA0, 0x2A),             // ...its release inside: dropped
                E(1600, RecKind.Move, 5, 5), E(1600, RecKind.ButtonDown, L), E(1700, RecKind.Move, 50, 5),   // a drag still going at the end
                E(1800, RecKind.KeyDown, 0xA2, 0x1D),           // Ctrl held at the end
                E(3000, RecKind.ButtonUp, L), E(3100, RecKind.KeyUp, 0xA2, 0x1D));
            Rec cut = RecordingEdit.Trim(rec, 1000, 2000);
            Assert.SequenceEqual(new[]
            {
                "600 Move 5,5", "600 ButtonDown 0,0", "700 Move 50,5", "800 KeyDown 162,29",
                "1000 ButtonUp 0,0", "1000 KeyUp 162,29",
            }, cut.Events.Select(Fmt));
        }

        [Test]
        public static void TrimKeepsTheWindowTheClicksWereIn()
        {
            Rec rec = Make(3000,
                E(0, RecKind.Window, 0), E(0, RecKind.Move, 150, 80), E(0, RecKind.ButtonDown, L), E(20, RecKind.ButtonUp, L),
                E(2000, RecKind.Move, 160, 90), E(2000, RecKind.ButtonDown, L), E(2020, RecKind.ButtonUp, L));
            rec.Schema = 2;
            rec.Windows.Add(new RecWindow { Program = "EXCEL.EXE", Title = "Book1 - Excel", Left = 100, Top = 50 });
            Rec cut = RecordingEdit.Trim(rec, 1000, 3000);
            Assert.SequenceEqual(new[] { "0 Window 0,0", "0 Move 150,80", "1000 Move 160,90", "1000 ButtonDown 0,0", "1020 ButtonUp 0,0" }, cut.Events.Select(Fmt));
            Assert.Equal(2, cut.Schema);
            Assert.Equal("Book1 - Excel", cut.Windows[0].Title);
            cut.Windows[0].Title = "changed";
            Assert.Equal("Book1 - Excel", rec.Windows[0].Title, "the copy has its own windows");
        }

        [Test]
        public static void TrimClampsItsEnds()
        {
            Rec rec = Make(1000, E(100, RecKind.KeyDown, 'A'), E(200, RecKind.KeyUp, 'A'));
            Rec cut = RecordingEdit.Trim(rec, -50, 99999);
            Assert.SequenceEqual(rec.Events.Select(Fmt), cut.Events.Select(Fmt));
            Assert.Equal(1000, cut.DurationMs);
            Assert.Equal(0, RecordingEdit.Trim(rec, 800, 300).DurationMs, "an end before the start keeps nothing");
            Assert.Equal(1000, RecordingEdit.Length(Make(0, E(1000, RecKind.KeyDown, 'A'))), "the last event when the duration is missing");
        }

        [Test]
        public static void ShortenPausesCutsLongGapsAndMovesWhatFollows()
        {
            Rec rec = Make(12000,
                E(3000, RecKind.KeyDown, 'A'), E(3050, RecKind.KeyUp, 'A'),     // 3 s wait at the start
                E(4000, RecKind.KeyDown, 'B'), E(4050, RecKind.KeyUp, 'B'),     // 0.95 s: kept
                E(9050, RecKind.KeyDown, 'C'), E(9100, RecKind.KeyUp, 'C'));    // 5 s, then 2.9 s to the end
            int n;
            Rec shorter = RecordingEdit.ShortenPauses(rec, 2000, 500, out n);
            Assert.Equal(3, n);
            Assert.SequenceEqual(new[]
            {
                "500 KeyDown 65,0", "550 KeyUp 65,0",
                "1500 KeyDown 66,0", "1550 KeyUp 66,0",
                "2050 KeyDown 67,0", "2100 KeyUp 67,0",
            }, shorter.Events.Select(Fmt));
            Assert.Equal(2600, shorter.DurationMs);
            Assert.Equal(3, RecordingEdit.CountLongPauses(rec, 2000));
            Assert.Equal(0, RecordingEdit.CountLongPauses(shorter, 2000));
            Assert.Equal(3050, rec.Events[1].T, "the original is left alone");
        }

        [Test]
        public static void ShortenPausesKeepsALongPressOrASlowDrag()
        {
            Rec rec = Make(6000,
                E(0, RecKind.Move, 5, 5), E(0, RecKind.ButtonDown, L), E(3000, RecKind.ButtonUp, L),   // a 3-second long press
                E(3100, RecKind.KeyDown, 'A'), E(6000, RecKind.KeyUp, 'A'));                          // a key held 2.9 s
            int n;
            Rec same = RecordingEdit.ShortenPauses(rec, 2000, 500, out n);
            Assert.Equal(0, n);
            Assert.SequenceEqual(rec.Events.Select(Fmt), same.Events.Select(Fmt));
        }

        [Test]
        public static void ShortenPausesNeverLengthensAPause()
        {
            Rec rec = Make(5000, E(0, RecKind.KeyDown, 'A'), E(10, RecKind.KeyUp, 'A'), E(3010, RecKind.KeyDown, 'B'), E(3020, RecKind.KeyUp, 'B'));
            int n;
            Rec shorter = RecordingEdit.ShortenPauses(rec, 2000, 9000, out n);   // "to" above "over" acts as "over"
            Assert.Equal(1, n);
            Assert.Equal(2010, shorter.Events[2].T);
        }

        [Test]
        public static void ATrimmedRecordingStillPlays()
        {
            Rec rec = Make(5000,
                E(500, RecKind.Move, 10, 10), E(500, RecKind.ButtonDown, L), E(2500, RecKind.ButtonUp, L),
                E(3000, RecKind.KeyDown, 'A'), E(4500, RecKind.KeyUp, 'A'));
            Rec cut = RecordingEdit.Trim(rec, 1000, 3500);
            var clock = new FakeClock();
            var sink = new FakeInputSink();
            RunResult result = new Player(sink, clock).Play(cut, new PlaybackSettings(), System.Threading.CancellationToken.None);
            Assert.True(result.Completed, result.Error);
            Assert.Equal(0, sink.HeldKeys.Count, "nothing left held");
            Assert.Equal(0, sink.HeldButtons.Count, "no button left down");
            Assert.True(sink.Log.Contains("moveto 10,10"), string.Join("|", sink.Log));
        }

        // ---------- 1.12.1 ----------

        [Test]
        public static void ShortenPausesIgnoresAKeyThatIsNeverLetGo()
        {
            // Ctrl went down and its release never came (lost, or still held when the recording stopped): later pauses are still cut.
            Rec rec = Make(9000, E(0, RecKind.KeyDown, 0xA2), E(100, RecKind.KeyDown, 'A'), E(110, RecKind.KeyUp, 'A'),
                E(4110, RecKind.KeyDown, 'B'), E(4120, RecKind.KeyUp, 'B'));
            int n;
            Rec shorter = RecordingEdit.ShortenPauses(rec, 2000, 500, out n);
            Assert.Equal(2, n, "the 4-second gap and the long end");
            Assert.Equal(610, shorter.Events[3].T);
            Assert.Equal(2, RecordingEdit.CountLongPauses(rec, 2000), "the hint counts the same");
        }

        [Test]
        public static void UndoPutsBackTheContentButKeepsTheNewName()
        {
            Rec before = Make(3000, E(0, RecKind.KeyDown, 'A'), E(10, RecKind.KeyUp, 'A'), E(2500, RecKind.KeyDown, 'B'), E(2510, RecKind.KeyUp, 'B'));
            before.Windows.Add(new RecWindow { Program = "notepad.exe", Title = "notes.txt - Notepad" });
            Rec trimmed = RecordingEdit.Trim(before, 0, 1000);
            trimmed.Name = "Renamed";
            Rec restored = RecordingEdit.WithContentOf(trimmed, before);
            Assert.Equal("Renamed", restored.Name, "a rename made after the trim stays");
            Assert.Equal(before.Id, restored.Id);
            Assert.Equal(3000, restored.DurationMs);
            Assert.SequenceEqual(before.Events.Select(Fmt), restored.Events.Select(Fmt));
            Assert.Equal(1, restored.Windows.Count);
            Assert.False(ReferenceEquals(before.Events, restored.Events), "a copy, so the snapshot on the undo stack stays as it was");
        }

        [Test]
        public static void LengthsAreRoundedBeforeTheyAreShown()
        {
            Assert.Equal("0.5 s", RecordingEdit.FormatLength(500));
            Assert.Equal("12 s", RecordingEdit.FormatLength(12000));
            Assert.Equal("59.9 s", RecordingEdit.FormatLength(59940));
            Assert.Equal("1:00.0", RecordingEdit.FormatLength(59960), "not 60 s");
            Assert.Equal("1:05.3", RecordingEdit.FormatLength(65300));
            Assert.Equal("2:00.0", RecordingEdit.FormatLength(119960), "not 1:60.0");
        }
    }
}
