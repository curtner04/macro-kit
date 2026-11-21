using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MacroKit.Tests
{
    using MacroKit.Core;
    using MacroKit.Macros;
    using MacroKit.Recording;
    using Rec = MacroKit.Core.Recording;

    /// <summary>The window under each recorded click: a list of rectangles, each with its window, checked in order.</summary>
    public sealed class FakeWindowProbe : IWindowProbe
    {
        sealed class Area { public int X0, Y0, X1, Y1; public WindowInfo Window; public bool Switchable; }
        readonly List<Area> areas = new List<Area>();
        public int Calls;
        public bool Throw;

        public FakeWindowProbe Add(int x0, int y0, int x1, int y1, WindowInfo window, bool switchable = true)
        {
            areas.Insert(0, new Area { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Window = window, Switchable = switchable });
            return this;
        }

        public WindowInfo WindowAt(int x, int y, out bool switchable)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("probe failed");
            Area a = areas.FirstOrDefault(r => x >= r.X0 && x < r.X1 && y >= r.Y0 && y < r.Y1);
            switchable = a != null && a.Switchable;
            return a == null ? null : a.Window;
        }

        public static WindowInfo Window(int handle, string title, string program, int left, int top, int width = 800, int height = 600, int pid = 0)
        {
            return new WindowInfo { Handle = new IntPtr(handle), Title = title, ProcessName = program, ProcessId = pid == 0 ? 1000 + handle : pid, Left = left, Top = top, Width = width, Height = height };
        }
    }

    /// <summary>1.11.0: clicks that follow their window (recording, playback, Convert to macro and {{WINDOWXY}}).</summary>
    public static class FollowWindowTests
    {
        static RecEvent E(int t, RecKind kind, int a = 0, int b = 0) { return new RecEvent(t, kind, a, b); }
        static string Fmt(RecEvent e) { return e.T + " " + e.Kind + " " + e.A + "," + e.B; }

        static MouseEvent Down(int x, int y) { return new MouseEvent { Kind = MouseEventKind.Down, Button = MouseButton.Left, X = x, Y = y }; }
        static MouseEvent Up(int x, int y) { return new MouseEvent { Kind = MouseEventKind.Up, Button = MouseButton.Left, X = x, Y = y }; }

        sealed class RecordRig
        {
            public readonly FakeClock Clock = new FakeClock();
            public readonly FakeWindowProbe Probe = new FakeWindowProbe();
            public readonly Recorder Recorder;
            public RecordRig()
            {
                Clock.NowMs = 1000;
                Recorder = new Recorder(Clock) { WindowProbe = Probe };
                Recorder.Start(false);
            }
            public RecordRig Click(int t, int x, int y)
            {
                Clock.NowMs = 1000 + t; Recorder.OnMouse(Down(x, y));
                Clock.NowMs = 1000 + t + 20; Recorder.OnMouse(Up(x, y));
                return this;
            }
        }

        static readonly WindowInfo Excel = FakeWindowProbe.Window(1, "Book1 - Excel", "EXCEL.EXE", 100, 50);
        static readonly WindowInfo Notepad = FakeWindowProbe.Window(2, "Untitled - Notepad", "notepad.exe", 1000, 200);

        static RecordRig TwoWindows()
        {
            var rig = new RecordRig();
            rig.Probe.Add(0, 0, 3000, 2000, null, false)          // nothing (should not happen, but must not break)
                .Add(100, 50, 900, 650, Excel)
                .Add(1000, 200, 1800, 800, Notepad);
            return rig;
        }

        // ---------- recording ----------

        [Test]
        public static void ARecordingNotesTheWindowOfEachClickOnlyWhenItChanges()
        {
            var rig = TwoWindows();
            rig.Click(0, 150, 80).Click(100, 160, 90).Click(200, 1200, 300).Click(300, 170, 95);
            Rec rec = rig.Recorder.Stop(null);
            Assert.SequenceEqual(new[]
            {
                "0 Window 0,0", "0 Move 150,80", "0 ButtonDown 0,0", "20 ButtonUp 0,0",
                "100 Move 160,90", "100 ButtonDown 0,0", "120 ButtonUp 0,0",
                "200 Window 1,0", "200 Move 1200,300", "200 ButtonDown 0,0", "220 ButtonUp 0,0",
                "300 Window 2,0", "300 Move 170,95", "300 ButtonDown 0,0", "320 ButtonUp 0,0",
            }, rec.Events.Select(Fmt));
            Assert.Equal(2, rec.Schema);
            Assert.True(rec.KnowsWindows);
            Assert.Equal(3, rec.Windows.Count);
            Assert.Equal("Book1 - Excel", rec.Windows[0].Title);
            Assert.Equal("EXCEL.EXE", rec.Windows[0].Program);
            Assert.Equal(100, rec.Windows[0].Left);
            Assert.Equal(50, rec.Windows[0].Top);
            Assert.Equal(800, rec.Windows[0].Width);
            Assert.Equal("Untitled - Notepad", rec.Windows[1].Title);
        }

        [Test]
        public static void AWindowThatMovedDuringTheRecordingIsNotedAgain()
        {
            var rig = TwoWindows();
            rig.Click(0, 150, 80);
            Excel.Left = 400;
            try { rig.Click(100, 450, 80); }
            finally { Excel.Left = 100; }
            Rec rec = rig.Recorder.Stop(null);
            Assert.Equal(2, rec.Windows.Count);
            Assert.Equal(400, rec.Windows[1].Left);
            Assert.Equal(2, rec.Events.Count(e => e.Kind == RecKind.Window));
        }

        [Test]
        public static void MenusOfTheSameProgramKeepTheWindowAndTheTaskbarMarksTheScreen()
        {
            var rig = new RecordRig();
            WindowInfo menu = FakeWindowProbe.Window(50, "", "EXCEL.EXE", 120, 60, 200, 300, Excel.ProcessId);
            WindowInfo taskbar = FakeWindowProbe.Window(60, "", "explorer.exe", 0, 1040, 1920, 40);
            rig.Probe.Add(100, 50, 900, 650, Excel).Add(120, 60, 320, 360, menu, false).Add(0, 1040, 1920, 1080, taskbar, false);
            rig.Click(0, 150, 600).Click(100, 130, 70).Click(200, 500, 1060).Click(300, 600, 1060).Click(400, 160, 600);
            Rec rec = rig.Recorder.Stop(null);
            Assert.SequenceEqual(new[] { "0 Window 0,0", "200 Window -1,0", "400 Window 1,0" },
                rec.Events.Where(e => e.Kind == RecKind.Window).Select(Fmt));
            Assert.Equal(2, rec.Windows.Count);
        }

        [Test]
        public static void ARecordingWithOnlyScreenClicksStaysInTheOldFormat()
        {
            var rig = new RecordRig();
            rig.Probe.Add(0, 1040, 1920, 1080, FakeWindowProbe.Window(60, "", "explorer.exe", 0, 1040), false);
            rig.Click(0, 500, 1060).Click(100, 600, 1060);
            Rec rec = rig.Recorder.Stop(null);
            Assert.Equal(1, rec.Schema);
            Assert.False(rec.KnowsWindows);
            Assert.Equal(0, rec.Events.Count(e => e.Kind == RecKind.Window));
        }

        [Test]
        public static void AFailingProbeOnlyLeavesTheWindowsOut()
        {
            var rig = TwoWindows();
            rig.Probe.Throw = true;
            rig.Click(0, 150, 80);
            Rec rec = rig.Recorder.Stop(null);
            Assert.SequenceEqual(new[] { "0 Move 150,80", "0 ButtonDown 0,0", "20 ButtonUp 0,0" }, rec.Events.Select(Fmt));
            Assert.Equal(1, rec.Schema);
        }

        [Test]
        public static void ClicksOnMacroKitItselfAreNotProbed()
        {
            var rig = TwoWindows();
            rig.Recorder.OnMouse(new MouseEvent { Kind = MouseEventKind.Down, Button = MouseButton.Left, X = 150, Y = 80, OverOwnWindow = true });
            Assert.Equal(0, rig.Probe.Calls);
        }

        // ---------- playback ----------

        static Rec ExcelRec()
        {
            var rec = new Rec { Schema = 2 };
            rec.Windows.Add(new RecWindow { Program = "EXCEL.EXE", Title = "Book1 - Excel", Left = 100, Top = 50, Width = 800, Height = 600 });
            rec.Events.AddRange(new[]
            {
                E(0, RecKind.Window, 0), E(0, RecKind.Move, 150, 80), E(0, RecKind.ButtonDown, (int)MouseButton.Left), E(20, RecKind.ButtonUp, (int)MouseButton.Left),
                E(500, RecKind.Window, -1), E(500, RecKind.Move, 10, 1060), E(500, RecKind.ButtonDown, (int)MouseButton.Left), E(520, RecKind.ButtonUp, (int)MouseButton.Left),
            });
            rec.DurationMs = 520;
            return rec;
        }

        sealed class PlayRig
        {
            public readonly FakeClock Clock = new FakeClock();
            public readonly FakeInputSink Sink = new FakeInputSink();
            public readonly FakeWindowSystem Windows = new FakeWindowSystem();
            public readonly Player Player;
            public PlayRig() { Player = new Player(Sink, Clock, Windows); }
            public RunResult Play(Rec rec, bool follow = true)
            {
                return Player.Play(rec, new PlaybackSettings { FollowWindows = follow }, CancellationToken.None);
            }
            public string[] Moves { get { return Sink.Log.Where(l => l.StartsWith("moveto", StringComparison.Ordinal)).ToArray(); } }
        }

        [Test]
        public static void PlaybackBringsTheWindowForwardAndShiftsItsClicks()
        {
            var rig = new PlayRig();
            WindowInfo excel = rig.Windows.AddWindow(7, "Book1 - Excel", "EXCEL.EXE");
            excel.Left = 300; excel.Top = 150;
            RunResult result = rig.Play(ExcelRec());
            Assert.True(result.Completed, result.Error);
            Assert.SequenceEqual(new[] { "activate Book1 - Excel (ok)" }, rig.Windows.ActivateLog);
            // The Excel click moves with the window; the taskbar click after the screen marker does not.
            Assert.SequenceEqual(new[] { "moveto 350,180", "moveto 10,1060" }, rig.Moves);
        }

        [Test]
        public static void WithFollowOffPlaybackClicksWhereItAlwaysDid()
        {
            var rig = new PlayRig();
            WindowInfo excel = rig.Windows.AddWindow(7, "Book1 - Excel", "EXCEL.EXE");
            excel.Left = 300; excel.Top = 150;
            RunResult result = rig.Play(ExcelRec(), false);
            Assert.True(result.Completed, result.Error);
            Assert.Equal(0, rig.Windows.ActivateLog.Count);
            Assert.SequenceEqual(new[] { "moveto 150,80", "moveto 10,1060" }, rig.Moves);
        }

        [Test]
        public static void AnOldRecordingPlaysExactlyAsBefore()
        {
            var rig = new PlayRig();
            rig.Windows.AddWindow(7, "Book1 - Excel", "EXCEL.EXE").Left = 300;
            var rec = new Rec();
            rec.Events.AddRange(new[] { E(0, RecKind.Move, 150, 80), E(0, RecKind.ButtonDown, (int)MouseButton.Left), E(20, RecKind.ButtonUp, (int)MouseButton.Left) });
            rec.DurationMs = 20;
            Assert.True(rig.Play(rec).Completed);
            Assert.Equal(0, rig.Windows.ActivateLog.Count);
            Assert.SequenceEqual(new[] { "moveto 150,80" }, rig.Moves);
        }

        [Test]
        public static void AMissingWindowStopsPlaybackInsteadOfClickingBlind()
        {
            var rig = new PlayRig();
            rig.Windows.AddWindow(8, "Orders", "chrome.exe");
            RunResult result = rig.Play(ExcelRec());
            Assert.False(result.Completed);
            Assert.False(result.Cancelled);
            Assert.Contains(result.Error, "couldn't find “Book1 - Excel” (EXCEL.EXE)");
            Assert.Contains(result.Error, "Clicks follow their window");
            Assert.Equal(0, rig.Sink.Log.Count(l => l.StartsWith("moveto", StringComparison.Ordinal) || l.StartsWith("button", StringComparison.Ordinal)));
            Assert.True(rig.Clock.NowMs >= Player.FindWindowMs, "waited for it first");
        }

        [Test]
        public static void PlaybackWaitsForAWindowThatOpensLateWithoutHurryingTheRest()
        {
            var rig = new PlayRig();
            rig.Clock.After(2000, () => { WindowInfo w = rig.Windows.AddWindow(7, "Book1 - Excel", "EXCEL.EXE"); w.Left = 100; w.Top = 50; });
            var times = new List<long>();
            rig.Sink.OnCall = l => { if (l.StartsWith("moveto", StringComparison.Ordinal)) times.Add(rig.Clock.NowMs); };
            RunResult result = rig.Play(ExcelRec());
            Assert.True(result.Completed, result.Error);
            Assert.SequenceEqual(new[] { "moveto 150,80", "moveto 10,1060" }, rig.Moves);
            Assert.True(times[0] >= 2000, "the first click waited for the window");
            Assert.True(times[1] - times[0] >= 450, "the second click still comes about 500 ms later, not at once: " + (times[1] - times[0]));
        }

        [Test]
        public static void ARecordedWindowIsFoundByTitleThenByProgram()
        {
            var windows = new FakeWindowSystem();
            var want = new RecWindow { Program = "notepad.exe", Title = "Untitled - Notepad" };
            Assert.Null(Player.FindRecordedWindow(windows, want), "nothing open");
            windows.AddWindow(1, "Untitled - Notepad", "chrome.exe");
            Assert.Null(Player.FindRecordedWindow(windows, want), "the same title in another program is not it");
            WindowInfo starred = windows.AddWindow(2, "*Untitled - Notepad", "Notepad.exe");
            Assert.Equal(starred.Handle, Player.FindRecordedWindow(windows, want).Handle, "a title that holds the recorded one, any case of the program");
            WindowInfo exact = windows.AddWindow(3, "Untitled - Notepad", "notepad.exe");
            Assert.Equal(exact.Handle, Player.FindRecordedWindow(windows, want).Handle, "the exact title wins");

            var other = new FakeWindowSystem();
            WindowInfo only = other.AddWindow(4, "notes.txt - Notepad", "notepad.exe");
            Assert.Equal(only.Handle, Player.FindRecordedWindow(other, want).Handle, "the program's only window");
            other.AddWindow(5, "todo.txt - Notepad", "notepad.exe");
            Assert.Null(Player.FindRecordedWindow(other, want), "two other Notepad windows: no guessing");
        }

        // ---------- Convert to macro and {{WINDOWXY}} ----------

        [Test]
        public static void ConvertSwitchesToEachWindowAndMeasuresClicksFromIt()
        {
            Rec rec = ExcelRec();
            string script = RecordingToMacro.Script(rec.Events, rec.Windows, 1);
            string[] lines = script.Split('\n');
            Assert.Equal("{{FOCUS:Book1 - Excel}}", lines[0]);
            Assert.Equal("{{WINDOWXY:50,30}}", lines[1]);
            Assert.True(lines.Contains("{{MOUSEXY:10,1060}}"), script);
            Assert.False(script.Contains("{{MOUSEXY:150,80}}"), script);

            MacroDef m = RecordingToMacro.Convert(rec);
            Assert.Equal(script, m.Script);
            ParseResult parsed = MacroParser.Parse(m.Script, m.NewLineIsEnter);
            Assert.True(parsed.Ok, string.Join("; ", parsed.Errors.Select(e => e.Message)));
            Assert.Equal(1, parsed.Steps.Count(s => s.Kind == StepKind.FocusWindow));
            Assert.Equal(1, parsed.Steps.Count(s => s.Kind == StepKind.MouseMoveInWindow && s.X == 50 && s.Y == 30));
        }

        [Test]
        public static void ConvertWithFollowOffKeepsScreenSpots()
        {
            MacroDef m = RecordingToMacro.Convert(ExcelRec(), 1, false);
            Assert.False(m.Script.Contains("FOCUS"), m.Script);
            Assert.False(m.Script.Contains("WINDOWXY"), m.Script);
            Assert.True(m.Script.StartsWith("{{MOUSEXY:150,80}}", StringComparison.Ordinal), m.Script);
        }

        [Test]
        public static void ConvertNamesAWindowByItsProgramWhenTheTitleWouldBreakTheMacro()
        {
            Rec rec = ExcelRec();
            rec.Windows[0].Title = "{{weird}} $title";
            string script = RecordingToMacro.Script(rec.Events, rec.Windows, 1);
            Assert.True(script.StartsWith("{{FOCUS:EXCEL.EXE}}\n", StringComparison.Ordinal), script);
            Assert.True(MacroParser.Parse(script, false).Ok, script);
        }

        [Test]
        public static void ConvertSwitchesOnlyWhenTheWindowChanges()
        {
            var rec = new Rec { Schema = 2 };
            rec.Windows.Add(new RecWindow { Program = "EXCEL.EXE", Title = "Book1 - Excel", Left = 100, Top = 50 });
            rec.Windows.Add(new RecWindow { Program = "EXCEL.EXE", Title = "Book1 - Excel", Left = 400, Top = 50 });   // the same window, moved
            rec.Events.AddRange(new[]
            {
                E(0, RecKind.Window, 0), E(0, RecKind.Move, 150, 80), E(0, RecKind.ButtonDown, (int)MouseButton.Left), E(20, RecKind.ButtonUp, (int)MouseButton.Left),
                E(500, RecKind.Window, 1), E(500, RecKind.Move, 450, 80), E(500, RecKind.ButtonDown, (int)MouseButton.Left), E(520, RecKind.ButtonUp, (int)MouseButton.Left),
            });
            string script = RecordingToMacro.Script(rec.Events, rec.Windows, 1);
            Assert.Equal(1, script.Split('\n').Count(l => l.StartsWith("{{FOCUS", StringComparison.Ordinal)), script);
            Assert.Equal(2, script.Split('\n').Count(l => l == "{{WINDOWXY:50,30}}"), script);
        }

        [Test]
        public static void WindowXyParsesDescribesAndReadsAsPlainText()
        {
            ParseResult r = MacroParser.Parse("{{WINDOWXY:40,-5}}", false);
            Assert.True(r.Ok, string.Join("; ", r.Errors.Select(e => e.Message)));
            Assert.Equal(StepKind.MouseMoveInWindow, r.Steps[0].Kind);
            Assert.Equal("Move mouse to 40, -5 in the window", r.Steps[0].Describe());
            Assert.Contains(MacroParser.Parse("{{WINDOWXY:40}}", false).Errors[0].Message, "two numbers");
            Assert.Equal("move mouse to 40, -5 in the window", PlainMacro.ToPlain("{{WINDOWXY:40,-5}}", false));
            Assert.Equal("{{WINDOWXY:40,-5}}", PlainMacro.ToScript("move mouse to 40, -5 in the window").Script);
            Assert.Equal("{{WINDOWXY:7,8}}", PlainMacro.ToScript("Move the mouse to 7 8 in window").Script);
            Assert.Equal("{{MOUSEXY:7,8}}", PlainMacro.ToScript("move mouse to 7, 8").Script);
        }

        [Test]
        public static void WindowXyMovesFromTheWindowInFront()
        {
            var input = new FakeInputSink();
            var windows = new FakeWindowSystem();
            WindowInfo excel = windows.AddWindow(1, "Book1 - Excel", "EXCEL.EXE");
            excel.Left = 300; excel.Top = 150;
            windows.SetForeground(excel.Handle);
            var runner = new MacroRunner(input, windows, new FakeClock(), new FakeClipboard());
            runner.ElevationProbe = () => false;
            var macro = new MacroDef { Script = "{{WINDOWXY:50,30}}", KeyDelayMs = 0 };
            RunResult result = runner.Run(macro, MacroParser.Parse(macro.Script, false), RunOrigin.MainWindow, CancellationToken.None);
            Assert.True(result.Completed, result.Error);
            Assert.SequenceEqual(new[] { "moveto 350,180" }, input.Log.Where(l => l.StartsWith("moveto", StringComparison.Ordinal)));
        }

        [Test]
        public static void WindowXyWithNoWindowInFrontStopsTheRun()
        {
            var input = new FakeInputSink();
            var windows = new FakeWindowSystem();
            var runner = new MacroRunner(input, windows, new FakeClock(), new FakeClipboard());
            runner.ElevationProbe = () => false;
            var macro = new MacroDef { Script = "{{WINDOWXY:50,30}}", KeyDelayMs = 0 };
            RunResult result = runner.Run(macro, MacroParser.Parse(macro.Script, false), RunOrigin.MainWindow, CancellationToken.None);
            Assert.False(result.Completed);
            Assert.NotNull(result.Error);
            Assert.Equal(0, input.Log.Count(l => l.StartsWith("moveto", StringComparison.Ordinal)));
        }

        // ---------- saving ----------

        [Test]
        public static void ARecordingsWindowsSurviveSavingAndOldFilesLoadWithout()
        {
            Rec rec = ExcelRec();
            Rec back = Json.Deserialize<Rec>(Json.Serialize(rec));
            Assert.Equal(2, back.Schema);
            Assert.True(back.KnowsWindows);
            Assert.Equal("Book1 - Excel", back.Windows[0].Title);
            Assert.Equal(100, back.Windows[0].Left);
            Assert.Equal(RecKind.Window, back.Events[0].Kind);
            Assert.False(Json.Serialize(rec).Contains("KnowsWindows"), "the computed flag is not saved");

            Rec old = Json.Deserialize<Rec>("{\"Name\":\"Old\",\"Schema\":1,\"Events\":[]}");
            Assert.NotNull(old.Windows);
            Assert.False(old.KnowsWindows);
            Assert.True(Json.Deserialize<PlaybackSettings>("{\"Speed\":2}").FollowWindows, "on for settings saved before 1.11");
            Assert.False(new PlaybackSettings { FollowWindows = false }.Copy().FollowWindows, "Copy keeps it");
        }

        // ---------- 1.12.1 ----------

        [Test]
        public static void OfSeveralMatchingWindowsTheNearestIsChosen()
        {
            var windows = new FakeWindowSystem();
            var want = new RecWindow { Program = "explorer.exe", Title = "Downloads", Left = 900, Top = 100, Width = 800, Height = 600 };
            WindowInfo far = windows.AddWindow(1, "Downloads", "explorer.exe"); far.Left = 0; far.Top = 0;
            WindowInfo near = windows.AddWindow(2, "Downloads", "explorer.exe"); near.Left = 880; near.Top = 120;
            Assert.Equal(near.Handle, Player.FindRecordedWindow(windows, want).Handle, "two the same: the one nearest where it was");

            var chrome = new FakeWindowSystem();
            var tab = new RecWindow { Program = "chrome.exe", Title = "Inbox - Mail", Left = 50, Top = 40, Width = 1200, Height = 900 };
            WindowInfo a = chrome.AddWindow(3, "News", "chrome.exe"); a.Left = 50; a.Top = 40; a.Width = 1200; a.Height = 900;
            WindowInfo b = chrome.AddWindow(4, "Weather", "chrome.exe"); b.Left = 300; b.Top = 40; b.Width = 1200; b.Height = 900;
            Assert.Equal(a.Handle, Player.FindRecordedWindow(chrome, tab).Handle, "the tab changed: the one still exactly where it was");
            a.Left = 60;
            Assert.Null(Player.FindRecordedWindow(chrome, tab), "and when none is, no guessing");
        }

        [Test]
        public static void ADroppedTrayClickTakesItsScreenMarkWithIt()
        {
            var rig = new RecordRig();
            WindowInfo taskbar = FakeWindowProbe.Window(60, "", "explorer.exe", 0, 1040, 1920, 40);
            rig.Probe.Add(100, 50, 900, 650, Excel).Add(0, 1040, 1920, 1080, taskbar, false);
            rig.Click(0, 150, 600).Click(100, 1800, 1060);
            Assert.True(rig.Recorder.DropTrailingClick(MouseButton.Left), "the tray icon click");
            rig.Click(200, 500, 1060);
            Rec rec = rig.Recorder.Stop(null);
            Assert.SequenceEqual(new[] { "0 Window 0,0", "200 Window -1,0" }, rec.Events.Where(e => e.Kind == RecKind.Window).Select(Fmt),
                "the next taskbar click is marked as the screen again, not left in Excel");
        }
    }
}
