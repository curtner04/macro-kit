using System;
using System.Collections.Generic;
using System.Globalization;

namespace MacroKit.Core
{
    public enum RepeatMode { Once, Times, Continuous }
    public enum TargetKind { PreviousWindow, SpecificWindow }
    public enum ExpandWhen { Immediately, AfterDelimiter }
    public enum InsertMethod { Type, Paste }
    public enum RunOrigin { MainWindow, Deck, Hotkey, Tray }

    /// <summary>
    /// Outcome of a macro run or a recording playback. Lives in Core (not in the Macros module) because
    /// both the macro runner and the recording player return it.
    /// </summary>
    public sealed class RunResult
    {
        public bool Completed;
        public bool Cancelled;
        public string Error;
        public int PassesDone;
    }

    public sealed class MacroDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Script { get; set; }
        public RepeatMode Repeat { get; set; }
        public int RepeatTimes { get; set; }
        public int RepeatDelayMs { get; set; }
        public int KeyDelayMs { get; set; }               // pause after every command that sends input
        public bool NewLineIsEnter { get; set; }          // Super Macro default; off = line breaks are ignored
        public TargetKind Target { get; set; }
        public WindowMatch TargetWindow { get; set; }
        public WindowMatch AltTabPartner { get; set; }    // empty = normal Windows Alt+Tab
        public bool StopIfFocusLeaves { get; set; }       // applies when Target = SpecificWindow
        public string Hotkey { get; set; }                // "" = none
        public bool ShowOnDeck { get; set; }

        public MacroDef()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "New macro";
            Script = "";
            Repeat = RepeatMode.Once;
            RepeatTimes = 5;
            RepeatDelayMs = 0;
            KeyDelayMs = 10;
            NewLineIsEnter = true;
            Target = TargetKind.PreviousWindow;
            TargetWindow = new WindowMatch();
            AltTabPartner = new WindowMatch();
            StopIfFocusLeaves = true;
            Hotkey = "";
            ShowOnDeck = true;
        }
    }

    public sealed class PhraseDef
    {
        public string Id { get; set; }
        public string Trigger { get; set; }
        public string Replacement { get; set; }
        public bool Enabled { get; set; }
        public bool MatchCase { get; set; }
        public ExpandWhen When { get; set; }
        public InsertMethod Method { get; set; }

        public PhraseDef()
        {
            Id = Guid.NewGuid().ToString("N");
            Trigger = "";
            Replacement = "";
            Enabled = true;
            MatchCase = false;
            When = ExpandWhen.Immediately;
            Method = InsertMethod.Type;
        }
    }

    public sealed class PlaybackSettings
    {
        public double Speed { get; set; }
        public RepeatMode Repeat { get; set; }
        public int RepeatTimes { get; set; }
        public int RepeatDelayMs { get; set; }

        public PlaybackSettings()
        {
            Speed = 1.0;
            Repeat = RepeatMode.Once;
            RepeatTimes = 2;
            RepeatDelayMs = 0;
        }
    }

    public sealed class AppSettings
    {
        public int Schema { get; set; }
        public string RecordHotkey { get; set; }
        public string PlayHotkey { get; set; }
        public string StopHotkey { get; set; }
        public string DeckHotkey { get; set; }
        public bool EscStops { get; set; }                // a physical Esc stops playback and macro runs (never recording)
        public bool RecordMouseMoves { get; set; }
        public bool AutoSaveRecordings { get; set; }
        public PlaybackSettings Playback { get; set; }
        public string SelectedRecordingId { get; set; }
        public bool ExpansionEnabled { get; set; }
        public List<string> ExcludedPrograms { get; set; }   // "game.exe"
        public bool DeckVisible { get; set; }
        public double DeckLeft { get; set; }
        public double DeckTop { get; set; }
        public int DeckColumns { get; set; }
        public double WindowLeft { get; set; }
        public double WindowTop { get; set; }
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public int SelectedTab { get; set; }
        public string Theme { get; set; }                 // System | Light | Dark
        public bool CloseToTray { get; set; }
        public bool ShowWindowAfterRun { get; set; }
        public bool StartWithWindows { get; set; }        // the Run value is the truth; this mirrors it

        public AppSettings()
        {
            Schema = 1;
            RecordHotkey = "Ctrl+Shift+Alt+R";
            PlayHotkey = "Ctrl+Shift+Alt+P";
            StopHotkey = "Ctrl+Shift+Alt+S";
            DeckHotkey = "Ctrl+Shift+Alt+D";
            EscStops = true;
            RecordMouseMoves = true;
            AutoSaveRecordings = false;
            Playback = new PlaybackSettings();
            SelectedRecordingId = "";
            ExpansionEnabled = true;
            ExcludedPrograms = new List<string>();
            DeckVisible = false;
            DeckLeft = double.NaN;
            DeckTop = double.NaN;
            DeckColumns = 3;
            WindowLeft = double.NaN;
            WindowTop = double.NaN;
            WindowWidth = 1040;
            WindowHeight = 720;
            SelectedTab = 0;
            Theme = "System";
            CloseToTray = true;
            ShowWindowAfterRun = true;
            StartWithWindows = false;
        }
    }

    public enum RecKind { Move = 0, ButtonDown = 1, ButtonUp = 2, Wheel = 3, HWheel = 4, KeyDown = 5, KeyUp = 6 }

    /// <summary>
    /// One recorded input event. Move: A=x, B=y (physical virtual-desktop pixels).
    /// ButtonDown/ButtonUp: A=(int)MouseButton, B=0 (buttons carry no position: the recorder writes a Move to the click point first).
    /// Wheel/HWheel: A=delta. KeyDown/KeyUp: A=vk, B=scan | (extended ? 0x10000 : 0).
    /// </summary>
    public struct RecEvent
    {
        public int T;
        public RecKind Kind;
        public int A;
        public int B;

        public RecEvent(int t, RecKind kind, int a, int b)
        {
            T = t;
            Kind = kind;
            A = a;
            B = b;
        }
    }

    public sealed class Recording
    {
        public int Schema { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Created { get; set; }      // ISO 8601, local time with offset
        public int DurationMs { get; set; }
        public List<RecEvent> Events { get; set; }

        public Recording()
        {
            Schema = 1;
            Id = Guid.NewGuid().ToString("N");
            Name = "";
            Created = "";
            DurationMs = 0;
            Events = new List<RecEvent>();
        }

        /// <summary>Local time with offset, for example 2026-11-12T08:41:00-05:00.</summary>
        public static string NowIso()
        {
            return DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        }

        /// <summary>"Recording 2026-11-12 08:41".</summary>
        public static string DefaultName(DateTime when)
        {
            return "Recording " + when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
    }

    public sealed class RecordingInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Created { get; set; }
        public int DurationMs { get; set; }
        public int EventCount { get; set; }

        public RecordingInfo()
        {
            Id = "";
            Name = "";
            Created = "";
        }
    }
}
