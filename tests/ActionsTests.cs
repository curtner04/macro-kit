using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MacroKit.Core;
using MacroKit.Macros;

namespace MacroKit.Tests
{
    /// <summary>Free repeat blocks, take-turns and random choices, and the Stream Deck-style actions ({{RUN}}, {{MACRO}}, {{VOLUME}}...).</summary>
    public static class ActionsTests
    {
        static void WithPro(bool pro, Action body)
        {
            LicenseInfo before = Edition.License;
            Edition.Set(pro ? new LicenseInfo { Serial = 1, Issued = new DateTime(2026, 11, 17), Name = "Test" } : null);
            try { body(); }
            finally { Edition.Set(before); }
        }

        sealed class Rig
        {
            public readonly FakeInputSink Input = new FakeInputSink();
            public readonly FakeWindowSystem Windows = new FakeWindowSystem();
            public readonly FakeClock Clock = new FakeClock();
            public readonly FakeSystemActions Actions = new FakeSystemActions();
            public readonly List<MacroDef> Macros = new List<MacroDef>();
            public readonly Dictionary<string, int> Turns = new Dictionary<string, int>();
            public readonly WindowInfo Excel, Notepad;
            public Func<int, int> Pick;
            public RunResult Result;

            public Rig()
            {
                Windows.AddOwnWindow(900);
                Excel = Windows.AddWindow(1, "Book1 - Excel", "EXCEL.EXE");
                Notepad = Windows.AddWindow(3, "Untitled - Notepad", "notepad.exe");
                Windows.SetForeground(Excel.Handle);
            }

            public MacroDef Add(string name, string script)
            {
                var m = new MacroDef { Name = name, Script = script, KeyDelayMs = 0 };
                Macros.Add(m);
                return m;
            }

            /// <summary>Runs the script (free edition) and returns the typed text joined with |, or "ERROR message".</summary>
            public string Run(string script, MacroDef macro = null)
            {
                macro = macro ?? new MacroDef { Name = "Test", Script = script, KeyDelayMs = 0 };
                ParseResult parsed = MacroParser.Parse(macro.Script, true);
                Assert.True(parsed.Ok, parsed.Ok ? "" : parsed.Errors[0].Message);
                var runner = new MacroRunner(Input, Windows, Clock, new FakeClipboard());
                runner.ElevationProbe = () => false;
                runner.Actions = Actions;
                runner.Turns = Turns;
                runner.FindMacro = n => Macros.FirstOrDefault(m => string.Equals(m.Name, n, StringComparison.OrdinalIgnoreCase));
                if (Pick != null) runner.PickRandom = Pick;
                Input.Clear();
                Result = runner.Run(macro, parsed, RunOrigin.MainWindow, CancellationToken.None);
                return Result.Error != null ? "ERROR " + Result.Error : string.Join("|", Input.Log.Where(l => l.StartsWith("text ", StringComparison.Ordinal)).Select(l => l.Substring(5)));
            }
        }

        static string Run(string script) { string log = null; WithPro(false, () => log = new Rig().Run(script)); return log; }

        static string ErrorOf(string script)
        {
            string message = null;
            WithPro(false, () =>
            {
                ParseResult parsed = MacroParser.Parse(script, true);
                Assert.False(parsed.Ok, "should fail: " + script);
                message = parsed.Errors[0].Message;
            });
            return message;
        }

        // ---- Repeat blocks are free ----

        [Test]
        public static void RepeatBlocksWorkWithoutPro()
        {
            Assert.Equal("a|a|a|b|b", Run("{{LOOP:3}}a{{ENDLOOP}}{{LOOP:2}}b{{ENDLOOP}}"));
            Assert.Equal("x|y|x|y", Run("{{LOOP:2}}x{{PAUSE:10}}y{{ENDLOOP}}"));
        }

        [Test]
        public static void ConditionsStillNeedPro()
        {
            Assert.Contains(ErrorOf("{{IF:$a=1}}x{{ENDIF}}"), "Macro Kit Pro");
            Assert.Contains(ErrorOf("{{IFWINDOW:Excel}}x{{ENDIF}}"), "Macro Kit Pro");
            Assert.Contains(ErrorOf("{{IF:$a=1}}x{{ENDIF}}"), "Repeat blocks");
        }

        // ---- Take turns and random ----

        [Test]
        public static void SwitchBlocksLinkTheirChoices()
        {
            WithPro(false, () =>
            {
                ParseResult parsed = MacroParser.Parse("{{SWITCH}}a{{OR}}b{{OR}}c{{ENDSWITCH}}", true);
                Assert.True(parsed.Ok, parsed.Ok ? "" : parsed.Errors[0].Message);
                List<MacroStep> s = parsed.Steps;
                Assert.Equal(StepKind.SwitchStart, s[0].Kind);
                Assert.Equal("0,2,4", string.Join(",", s[0].Branches));
                Assert.Equal(6, s[0].Jump);
                Assert.Equal(6, s[2].Jump);
                Assert.Equal(6, s[4].Jump);
                Assert.Equal(StepKind.EndSwitch, s[6].Kind);
                Assert.Contains(s[0].Describe(), "3 choices");
            });
        }

        [Test]
        public static void SwitchTakesTurnsFromRunToRun()
        {
            WithPro(false, () =>
            {
                var rig = new Rig();
                MacroDef macro = rig.Add("Toggle", "{{SWITCH}}on{{OR}}off{{ENDSWITCH}}!");
                Assert.Equal("on|!", rig.Run(null, macro));
                Assert.Equal("off|!", rig.Run(null, macro));
                Assert.Equal("on|!", rig.Run(null, macro));
            });
        }

        [Test]
        public static void SwitchInsideALoopTakesTheNextTurnEachTime()
        {
            Assert.Equal("1|2|3|1", Run("{{LOOP:4}}{{SWITCH}}1{{OR}}2{{OR}}3{{ENDSWITCH}}{{ENDLOOP}}"));
        }

        [Test]
        public static void TwoSwitchBlocksKeepTheirOwnTurns()
        {
            Assert.Equal("a|x|b|y", Run("{{LOOP:2}}{{SWITCH}}a{{OR}}b{{ENDSWITCH}}{{SWITCH}}x{{OR}}y{{ENDSWITCH}}{{ENDLOOP}}"));
        }

        [Test]
        public static void RandomRunsTheChoiceItPicks()
        {
            WithPro(false, () =>
            {
                var rig = new Rig();
                int next = 0;
                var picks = new[] { 2, 0, 1 };
                rig.Pick = n => { Assert.Equal(3, n); return picks[next++]; };
                Assert.Equal("c|a|b", rig.Run("{{LOOP:3}}{{RANDOM}}a{{OR}}b{{OR}}c{{ENDRANDOM}}{{ENDLOOP}}"));
            });
        }

        [Test]
        public static void ChoiceBlocksCanHoldOtherBlocks()
        {
            Assert.Equal("a|a|b", Run("{{SWITCH}}{{LOOP:2}}a{{ENDLOOP}}{{OR}}z{{ENDSWITCH}}b"));
            Assert.Equal("only", Run("{{SWITCH}}only{{ENDSWITCH}}"));
        }

        [Test]
        public static void ChoiceMistakesAreExplained()
        {
            Assert.Contains(ErrorOf("{{SWITCH}}a{{OR}}b"), "no {{ENDSWITCH}}");
            Assert.Contains(ErrorOf("{{RANDOM}}a"), "no {{ENDRANDOM}}");
            Assert.Contains(ErrorOf("a{{OR}}b"), "between the choices");
            Assert.Contains(ErrorOf("{{ENDSWITCH}}"), "no {{SWITCH}}");
            Assert.Contains(ErrorOf("{{SWITCH}}a{{ENDRANDOM}}"), "Close the {{SWITCH}} with {{ENDSWITCH}}");
            Assert.Contains(ErrorOf("{{LOOP:2}}a{{OR}}b{{ENDLOOP}}"), "Close the {{LOOP}} with {{ENDLOOP}}");
            Assert.Contains(ErrorOf("{{SWITCH:2}}a{{ENDSWITCH}}"), "doesn't take a value");
            Assert.Contains(ErrorOf("{{SWITCH}}{{LOOP:2}}a{{ENDSWITCH}}"), "Close the {{LOOP}}");
        }

        // ---- Run another macro ----

        [Test]
        public static void MacroRunsAnotherMacroInPlace()
        {
            WithPro(false, () =>
            {
                var rig = new Rig();
                rig.Add("Copy row", "copy");
                rig.Add("Paste row", "paste");
                Assert.Equal("copy|copy|copy|paste|paste", rig.Run("{{LOOP:3}}{{MACRO:Copy row}}{{ENDLOOP}}{{LOOP:2}}{{MACRO:paste row}}{{ENDLOOP}}"));
            });
        }

        [Test]
        public static void MacroSharesVariables()
        {
            WithPro(false, () =>
            {
                var rig = new Rig();
                rig.Add("Greet", "Hi {{$who}}");
                Assert.Equal("Hi |Sam", rig.Run("{{VARSET:who:Sam}}{{MACRO:Greet}}"));
            });
        }

        [Test]
        public static void MacroProblemsStopTheRun()
        {
            WithPro(false, () =>
            {
                var rig = new Rig();
                Assert.Contains(rig.Run("{{MACRO:Missing}}"), "no macro called \u201CMissing\u201D");
                MacroDef self = rig.Add("Self", "a{{MACRO:Self}}");
                Assert.Contains(rig.Run(null, self), "starts itself");
                rig.Add("Ping", "{{MACRO:Pong}}");
                rig.Add("Pong", "{{MACRO:Ping}}");
                Assert.Contains(rig.Run("{{MACRO:Ping}}"), "starts itself");
                rig.Add("Broken", "{{PAUSE}}");
                Assert.Contains(rig.Run("{{MACRO:Broken}}"), "\u201CBroken\u201D has a problem on line 1");
            });
        }

        [Test]
        public static void MacroProgressShowsTheOuterStep()
        {
            WithPro(false, () =>
            {
                var rig = new Rig();
                rig.Add("Inner", "a{{PAUSE:5}}b");
                MacroDef outer = new MacroDef { Name = "Outer", Script = "x{{MACRO:Inner}}", KeyDelayMs = 0 };
                ParseResult parsed = MacroParser.Parse(outer.Script, true);
                var runner = new MacroRunner(rig.Input, rig.Windows, rig.Clock, new FakeClipboard());
                runner.ElevationProbe = () => false;
                runner.FindMacro = n => rig.Macros.FirstOrDefault(m => m.Name == n);
                var seen = new List<string>();
                runner.Progress += p => seen.Add(p.StepIndex + "/" + p.StepCount + " " + p.Message);
                Assert.True(runner.Run(outer, parsed, RunOrigin.MainWindow, CancellationToken.None).Completed);
                Assert.True(seen.Contains("2/2 Inner: Wait 5 ms"), string.Join(" ; ", seen));
            });
        }

        // ---- System actions ----

        [Test]
        public static void ActionsParseTheirWholeArgument()
        {
            WithPro(false, () =>
            {
                ParseResult parsed = MacroParser.Parse("{{RUN:https://example.com/a?b=c}}{{RUN:C:\\Notes\\todo.txt}}{{PLAYSOUND:C:\\Sounds\\ding.wav}}", true);
                Assert.True(parsed.Ok, parsed.Ok ? "" : parsed.Errors[0].Message);
                Assert.Equal("https://example.com/a?b=c", parsed.Steps[0].Value);
                Assert.Equal("C:\\Notes\\todo.txt", parsed.Steps[1].Value);
                Assert.Equal(StepKind.PlaySound, parsed.Steps[2].Kind);
                Assert.False(parsed.Steps[0].SendsInput);
            });
        }

        [Test]
        public static void RunSplitsQuotedProgramsFromTheirArguments()
        {
            string target, arguments;
            MacroParser.SplitRun("\"C:\\Program Files\\App\\app.exe\" --new window", out target, out arguments);
            Assert.Equal("C:\\Program Files\\App\\app.exe", target);
            Assert.Equal("--new window", arguments);
            MacroParser.SplitRun(" notepad ", out target, out arguments);
            Assert.Equal("notepad", target);
            Assert.Equal("", arguments);
            MacroParser.SplitRun("\"unclosed", out target, out arguments);
            Assert.Equal("unclosed", target);
        }

        [Test]
        public static void ActionsReachTheSystem()
        {
            WithPro(false, () =>
            {
                var rig = new Rig();
                Assert.Equal("", rig.Run("{{VARSET:site:example.com}}{{RUN:https://$site}}{{RUN:\"C:\\A B\\x.exe\" -q}}{{CLOSE:notepad.exe}}{{PLAYSOUND:\"C:\\s.wav\"}}{{VOLUME:40}}{{VOLUME:+10}}{{VOLUME:-5}}{{LOCK}}"));
                Assert.Equal("open https://example.com |;open C:\\A B\\x.exe |-q;close notepad.exe;sound C:\\s.wav;volume 40;volume +10;volume -5;lock", string.Join(";", rig.Actions.Log));
            });
        }

        [Test]
        public static void AFailedActionStopsTheRun()
        {
            WithPro(false, () =>
            {
                var rig = new Rig();
                rig.Actions.FailWith = "Windows couldn't find nothing.exe.";
                Assert.Equal("ERROR Line 1: Windows couldn't find nothing.exe.", rig.Run("{{RUN:nothing.exe}}after"));
            });
        }

        [Test]
        public static void ClosingAProgramThatIsNotOpenCarriesOn()
        {
            WithPro(false, () =>
            {
                var rig = new Rig();
                rig.Actions.WindowsToClose = 0;
                Assert.Equal("after", rig.Run("{{CLOSE:paint}}after"));
            });
        }

        [Test]
        public static void VolumeValuesAreChecked()
        {
            int amount;
            bool relative;
            Assert.True(MacroParser.TryVolume("50", out amount, out relative) && amount == 50 && !relative);
            Assert.True(MacroParser.TryVolume("+10", out amount, out relative) && amount == 10 && relative);
            Assert.True(MacroParser.TryVolume("-10%", out amount, out relative) && amount == -10 && relative);
            Assert.False(MacroParser.TryVolume("101", out amount, out relative));
            Assert.False(MacroParser.TryVolume("loud", out amount, out relative));
            Assert.Contains(ErrorOf("{{VOLUME:200}}"), "0 to 100");
            Assert.Contains(Run("{{VARSET:v:loud}}{{VOLUME:$v}}"), "needs 0 to 100");
        }

        [Test]
        public static void ActionMistakesAreExplained()
        {
            Assert.Contains(ErrorOf("{{RUN}}"), "what to open");
            Assert.Contains(ErrorOf("{{CLOSE}}"), "program name");
            Assert.Contains(ErrorOf("{{PLAYSOUND}}"), "sound file");
            Assert.Contains(ErrorOf("{{LOCK:now}}"), "doesn't take a value");
            Assert.Contains(ErrorOf("{{MACRO}}"), "another macro");
            Assert.Contains(ErrorOf("{{FOCUS}}"), "window's title");
            Assert.Contains(ErrorOf("{{WAITWINDOW:Notepad:5}}"), "100 to 3,600,000");
        }

        // ---- Windows ----

        [Test]
        public static void FocusSwitchesWindowsAndTheGuardFollows()
        {
            WithPro(false, () =>
            {
                var rig = new Rig();
                var macro = new MacroDef
                {
                    Name = "Focus", Script = "a{{FOCUS:notepad}}b", KeyDelayMs = 0,
                    Target = TargetKind.SpecificWindow, TargetWindow = new WindowMatch { ProcessName = "EXCEL.EXE" }, StopIfFocusLeaves = true
                };
                Assert.Equal("a|b", rig.Run(null, macro));
                Assert.Equal(rig.Notepad.Handle, rig.Windows.Foreground);
                Assert.Contains(rig.Run("{{FOCUS:Word}}"), "no open window matching \u201CWord\u201D");
            });
        }

        [Test]
        public static void WaitWindowWaitsForTheWindowToOpen()
        {
            WithPro(false, () =>
            {
                var rig = new Rig();
                rig.Actions.OnOpen = t => rig.Clock.After(1500, () => rig.Windows.AddWindow(7, "Calculator", "CalculatorApp.exe"));
                Assert.Equal("5", rig.Run("{{RUN:calc}}{{WAITWINDOW:Calculator}}5"));
                Assert.Equal((IntPtr)7, rig.Windows.Foreground);
                Assert.True(rig.Clock.NowMs >= 1500, "waited for it");
            });
        }

        [Test]
        public static void WaitWindowGivesUp()
        {
            WithPro(false, () =>
            {
                var rig = new Rig();
                Assert.Contains(rig.Run("{{WAITWINDOW:Calculator:2000}}x"), "within 2 seconds");
                Assert.Equal("", string.Join("|", rig.Input.Log.Where(l => l.StartsWith("text ", StringComparison.Ordinal))));
            });
        }

        // ---- Palette tokens ----

        [Test]
        public static void BlockTokensParseBack()
        {
            WithPro(false, () =>
            {
                string script = MacroScript.Loop(3) + "a" + MacroScript.EndLoop
                    + MacroScript.Switch + "x" + MacroScript.Or + "y" + MacroScript.EndSwitch
                    + MacroScript.Random + "p" + MacroScript.Or + "q" + MacroScript.EndRandom
                    + MacroScript.Run("C:\\Program Files\\App\\app.exe") + MacroScript.Run("notepad") + MacroScript.Website("example.com")
                    + MacroScript.Close("notepad.exe") + MacroScript.PlaySound("C:\\a b\\ding.wav") + MacroScript.Volume(30) + MacroScript.VolumeBy(-5)
                    + MacroScript.Lock + MacroScript.Macro("Copy row") + MacroScript.Focus("Excel") + MacroScript.WaitWindow("Excel", 5000);
                ParseResult parsed = MacroParser.Parse(script, true);
                Assert.True(parsed.Ok, parsed.Ok ? "" : parsed.Errors[0].Message);
                string target, arguments;
                MacroParser.SplitRun(parsed.Steps.First(s => s.Kind == StepKind.Open).Value, out target, out arguments);
                Assert.Equal("C:\\Program Files\\App\\app.exe", target);
                Assert.Equal("https://example.com", parsed.Steps.Where(s => s.Kind == StepKind.Open).Last().Value);
                Assert.Equal(3, parsed.Steps.Count(s => s.Kind == StepKind.Open));
                Assert.Equal("C:\\a b\\ding.wav", parsed.Steps.First(s => s.Kind == StepKind.PlaySound).Value);
                Assert.Equal(5000, parsed.Steps.First(s => s.Kind == StepKind.WaitWindow).Ms);
            });
            Assert.Equal("{{RUN:https://example.com}}", MacroScript.Website("https://example.com"));
            Assert.Equal("{{RUN:http://intranet/page}}", MacroScript.Website("http://intranet/page"));
        }
    }
}
