using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using MacroKit.Core;
using MacroKit.Macros;

namespace MacroKit.Tests
{
    /// <summary>Macro Kit Pro: Ed25519, license keys, loops and conditions, schedules and the library.</summary>
    public static class ProTests
    {
        static byte[] Hex(string hex) { return License.FromHex(hex); }

        static string ToHex(byte[] bytes) { return string.Concat(bytes.Select(b => b.ToString("x2"))); }

        /// <summary>Runs the body with Pro switched on (or off), then puts the edition back.</summary>
        static void WithPro(bool pro, Action body)
        {
            LicenseInfo before = Edition.License;
            Edition.Set(pro ? new LicenseInfo { Serial = 1, Issued = new DateTime(2026, 11, 17), Name = "Test" } : null);
            try { body(); }
            finally { Edition.Set(before); }
        }

        // ---- Ed25519 ----

        [Test]
        public static void Ed25519MatchesRfc8032TestVector1()
        {
            byte[] secret = Hex("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
            byte[] expectedPublic = Hex("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
            byte[] expectedSignature = Hex("e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");
            Assert.Equal(ToHex(expectedPublic), ToHex(Ed25519.PublicKey(secret)));
            byte[] signature = Ed25519.Sign(secret, new byte[0]);
            Assert.Equal(ToHex(expectedSignature), ToHex(signature));
            Assert.True(Ed25519.Verify(expectedPublic, new byte[0], signature));
            Assert.False(Ed25519.Verify(expectedPublic, new byte[] { 1 }, signature), "a different message");
            signature[5] ^= 1;
            Assert.False(Ed25519.Verify(expectedPublic, new byte[0], signature), "a changed signature");
        }

        [Test]
        public static void Ed25519VerifyNeverThrows()
        {
            Assert.False(Ed25519.Verify(null, new byte[0], new byte[64]));
            Assert.False(Ed25519.Verify(new byte[32], null, new byte[64]));
            Assert.False(Ed25519.Verify(new byte[31], new byte[0], new byte[64]));
            Assert.False(Ed25519.Verify(Enumerable.Repeat((byte)0xFF, 32).ToArray(), new byte[0], Enumerable.Repeat((byte)0xFF, 64).ToArray()));
        }

        // ---- License keys ----

        static readonly byte[] TestSecret = Enumerable.Repeat((byte)7, 32).ToArray();

        static string TestKey(int serial, string name, DateTime issued)
        {
            byte[] payload = License.Payload(serial, issued, name);
            return License.Format(payload, Ed25519.Sign(TestSecret, License.SignedMessage(payload)));
        }

        static bool Read(string key, out LicenseInfo info, out string problem)
        {
            return License.TryRead(key, new[] { Ed25519.PublicKey(TestSecret) }, out info, out problem);
        }

        [Test]
        public static void LicenseKeyRoundTrips()
        {
            string key = TestKey(42, "Ada Lovelace", new DateTime(2026, 11, 17));
            Assert.True(key.StartsWith(License.Prefix, StringComparison.Ordinal), key);
            LicenseInfo info;
            string problem;
            Assert.True(Read(key, out info, out problem), problem);
            Assert.Equal(42, info.Serial);
            Assert.Equal("Ada Lovelace", info.Name);
            Assert.Equal(new DateTime(2026, 11, 17), info.Issued);
            Assert.Equal(key, info.Key);
            Assert.Contains(info.Describe(), "Ada Lovelace");
        }

        [Test]
        public static void LicenseKeyAcceptsPastedVariants()
        {
            string key = TestKey(7, "Sam", new DateTime(2026, 12, 1));
            LicenseInfo info;
            string problem;
            Assert.True(Read("  " + key.ToLowerInvariant() + "\r\n", out info, out problem), "lower case: " + problem);
            Assert.True(Read(key.Replace("-", " "), out info, out problem), "spaces for dashes: " + problem);
            Assert.True(Read(key.Substring(0, 20) + "\n" + key.Substring(20), out info, out problem), "a line break: " + problem);
            Assert.Equal(7, info.Serial);
        }

        [Test]
        public static void LicenseKeyRejectsTamperingAndOtherSigners()
        {
            string key = TestKey(3, "Kim", new DateTime(2026, 11, 20));
            LicenseInfo info;
            string problem;
            int at = key.Length - 30;
            char swapped = key[at] == 'A' ? 'B' : 'A';
            Assert.False(Read(key.Substring(0, at) + swapped + key.Substring(at + 1), out info, out problem), "one character changed");
            Assert.Null(info);
            Assert.False(Read(key.Substring(0, key.Length - 1) + (key[key.Length - 1] == '0' ? '1' : '0'), out info, out problem), "the last character changed");
            Assert.False(Read(key.Substring(0, key.Length - 9), out info, out problem), "cut short");
            Assert.False(License.TryRead(key, out info, out problem), "the real public key must not accept a test-signed key");
            Assert.Contains(problem, "doesn't check out");
        }

        [Test]
        public static void LicenseKeyExplainsWhatIsWrong()
        {
            LicenseInfo info;
            string problem;
            Assert.False(Read("", out info, out problem));
            Assert.Contains(problem, "Paste");
            Assert.False(Read("ABCD-1234", out info, out problem));
            Assert.Contains(problem, License.Prefix);
            Assert.False(Read("MK1-U!!", out info, out problem));
            Assert.Contains(problem, "typo");
        }

        [Test]
        public static void LicenseNameIsCutToFitWithoutBreakingCharacters()
        {
            string name = new string('é', 40);   // 80 bytes in UTF-8
            byte[] payload = License.Payload(1, new DateTime(2026, 11, 17), name);
            Assert.True(payload[10] <= License.MaxNameBytes);
            string back = Encoding.UTF8.GetString(payload, 11, payload[10]);
            Assert.Equal(new string('é', 30), back);
        }

        // ---- Flow steps: parsing ----

        [Test]
        public static void LoopsAndConditionsNeedPro()
        {
            WithPro(false, () =>
            {
                ParseResult parsed = MacroParser.Parse("{{LOOP:3}}a{{ENDLOOP}}", true);
                Assert.False(parsed.Ok);
                Assert.Contains(parsed.Errors[0].Message, "Macro Kit Pro");
                Assert.False(MacroParser.Parse("{{IF:$a=1}}x{{ENDIF}}", true).Ok);
                Assert.True(MacroParser.Parse("{{CTRL}{C}}hello", true).Ok, "ordinary scripts stay free");
            });
        }

        [Test]
        public static void FlowStepsLinkTheirBlocks()
        {
            WithPro(true, () =>
            {
                ParseResult parsed = MacroParser.Parse("{{LOOP:2}}{{IF:$a=1}}x{{ELSE}}y{{ENDIF}}{{ENDLOOP}}", true);
                Assert.True(parsed.Ok, parsed.Errors.Count == 0 ? "" : parsed.Errors[0].Message);
                List<MacroStep> s = parsed.Steps;
                Assert.Equal(StepKind.LoopStart, s[0].Kind);
                Assert.Equal(StepKind.If, s[1].Kind);
                Assert.Equal(StepKind.Else, s[3].Kind);
                Assert.Equal(StepKind.EndIf, s[5].Kind);
                Assert.Equal(StepKind.LoopEnd, s[6].Kind);
                Assert.Equal(6, s[0].Jump);
                Assert.Equal(0, s[6].Jump);
                Assert.Equal(3, s[1].Jump);
                Assert.Equal(5, s[3].Jump);
                Assert.Equal("$a", s[1].Left);
                Assert.Equal("=", s[1].Op);
                Assert.Equal("1", s[1].Right);
                Assert.False(s[0].SendsInput);
            });
        }

        [Test]
        public static void FlowMistakesAreExplained()
        {
            WithPro(true, () =>
            {
                Assert.Contains(MacroParser.Parse("{{LOOP:3}}a", true).Errors[0].Message, "no {{ENDLOOP}}");
                Assert.Contains(MacroParser.Parse("a{{ENDLOOP}}", true).Errors[0].Message, "no {{LOOP}}");
                Assert.Contains(MacroParser.Parse("{{IF:$a=1}}a", true).Errors[0].Message, "no {{ENDIF}}");
                Assert.Contains(MacroParser.Parse("{{ENDIF}}", true).Errors[0].Message, "no {{IF}}");
                Assert.Contains(MacroParser.Parse("{{IF:$a=1}}a{{ELSE}}b{{ELSE}}c{{ENDIF}}", true).Errors[0].Message, "already has an {{ELSE}}");
                Assert.Contains(MacroParser.Parse("{{IF:hello}}a{{ENDIF}}", true).Errors[0].Message, "comparison");
                Assert.Contains(MacroParser.Parse("{{LOOP:0}}a{{ENDLOOP}}", true).Errors[0].Message, "1 to");
                Assert.Contains(MacroParser.Parse("{{LOOP}}a{{ENDLOOP}}", true).Errors[0].Message, "how many");
                Assert.Contains(MacroParser.Parse("{{LOOP:2}}{{IF:$a=1}}{{ENDLOOP}}{{ENDIF}}", true).Errors[0].Message, "Close the {{IF}}");
                Assert.Contains(MacroParser.Parse("{{IFWINDOW}}a{{ENDIF}}", true).Errors[0].Message, "which window");
            });
        }

        [Test]
        public static void ComparisonsSplitOnTheFirstSign()
        {
            string l, o, r;
            Assert.True(MacroParser.SplitComparison("$count>=3", out l, out o, out r));
            Assert.Equal("$count|>=|3", l + "|" + o + "|" + r);
            Assert.True(MacroParser.SplitComparison(" $a != b c ", out l, out o, out r));
            Assert.Equal("$a|!=|b c", l + "|" + o + "|" + r);
            Assert.True(MacroParser.SplitComparison("$clip=", out l, out o, out r));
            Assert.Equal("", r);
            Assert.False(MacroParser.SplitComparison("=5", out l, out o, out r), "no left side");
            Assert.False(MacroParser.SplitComparison("plain", out l, out o, out r));
        }

        [Test]
        public static void ComparisonsUseNumbersWhenBothSidesAreNumbers()
        {
            Assert.True(MacroParser.Compare("10", ">", "9"), "10 > 9 as numbers");
            Assert.True(MacroParser.Compare("2.5", "<", "10"));
            Assert.True(MacroParser.Compare("b", ">", "A"), "text ignores case");
            Assert.True(MacroParser.Compare("Sam", "=", "sam"));
            Assert.True(MacroParser.Compare("Hello World", "~", "WORLD"));
            Assert.False(MacroParser.Compare("abc", "~", "x"));
            Assert.True(MacroParser.Compare("5", "<=", "5"));
            Assert.True(MacroParser.Compare("a", "!=", "b"));
        }

        [Test]
        public static void EstimateMultipliesLoops()
        {
            WithPro(true, () =>
            {
                var m = new MacroDef { KeyDelayMs = 0 };
                Assert.Equal(300L, MacroParser.Parse("{{LOOP:3}}{{PAUSE:100}}{{ENDLOOP}}", true).EstimatedMs(m));
                Assert.Equal(650L, MacroParser.Parse("{{PAUSE:50}}{{LOOP:2}}{{LOOP:3}}{{PAUSE:100}}{{ENDLOOP}}{{ENDLOOP}}", true).EstimatedMs(m));
            });
        }

        // ---- Flow steps: running ----

        static string RunScript(string script, Action<FakeWindowSystem, WindowInfo, WindowInfo> setup = null)
        {
            string log = null;
            WithPro(true, () =>
            {
                var input = new FakeInputSink();
                var windows = new FakeWindowSystem();
                windows.AddOwnWindow(900);
                WindowInfo excel = windows.AddWindow(1, "Book1 - Excel", "EXCEL.EXE");
                WindowInfo notepad = windows.AddWindow(3, "Untitled - Notepad", "notepad.exe");
                windows.SetForeground(excel.Handle);
                if (setup != null) setup(windows, excel, notepad);
                var runner = new MacroRunner(input, windows, new FakeClock(), new FakeClipboard());
                runner.ElevationProbe = () => false;
                var macro = new MacroDef { Script = script, KeyDelayMs = 0 };
                ParseResult parsed = MacroParser.Parse(script, true);
                Assert.True(parsed.Ok, parsed.Ok ? "" : parsed.Errors[0].Message);
                RunResult result = runner.Run(macro, parsed, RunOrigin.MainWindow, CancellationToken.None);
                log = result.Error != null ? "ERROR " + result.Error : string.Join("|", input.Log.Where(l => l.StartsWith("text ", StringComparison.Ordinal)));
            });
            return log;
        }

        [Test]
        public static void LoopsRepeatTheirSteps()
        {
            Assert.Equal("text a|text a|text a|text b", RunScript("{{LOOP:3}}a{{ENDLOOP}}b"));
            Assert.Equal(6, RunScript("{{LOOP:2}}{{LOOP:3}}x{{ENDLOOP}}{{ENDLOOP}}").Split('|').Length);
            Assert.Equal("text a|text a|text end", RunScript("{{VARSET:n:2}}{{LOOP:$n}}a{{ENDLOOP}}end"));
            Assert.Equal("text end", RunScript("{{VARSET:n:0}}{{LOOP:$n}}a{{ENDLOOP}}end"));
            Assert.Contains(RunScript("{{VARSET:n:lots}}{{LOOP:$n}}a{{ENDLOOP}}"), "whole number");
        }

        [Test]
        public static void ConditionsPickABranch()
        {
            Assert.Equal("text big", RunScript("{{VARSET:x:5}}{{IF:$x>3}}big{{ELSE}}small{{ENDIF}}"));
            Assert.Equal("text small", RunScript("{{VARSET:x:2}}{{IF:$x>3}}big{{ELSE}}small{{ENDIF}}"));
            Assert.Equal("text end", RunScript("{{VARSET:x:2}}{{IF:$x>3}}big{{ENDIF}}end"));
            Assert.Equal("text 3|text  |text 2|text  |text 1|text  |text Go!", RunScript("{{VARSET:n:3}}{{LOOP:3}}{{$n}} {{IF:$n=3}}{{VARSET:n:2}}{{ELSE}}{{IF:$n=2}}{{VARSET:n:1}}{{ENDIF}}{{ENDIF}}{{ENDLOOP}}Go!"));
        }

        [Test]
        public static void IfWindowLooksAtTheWindowInFront()
        {
            Assert.Equal("text yes", RunScript("{{IFWINDOW:excel}}yes{{ELSE}}no{{ENDIF}}"));
            Assert.Equal("text no", RunScript("{{IFWINDOW:notepad}}yes{{ELSE}}no{{ENDIF}}"));
            Assert.Equal("text yes", RunScript("{{IFWINDOW:Book1}}yes{{ENDIF}}"));
        }

        // ---- Schedules ----

        [Test]
        public static void DailySchedulesFindTheNextTime()
        {
            var s = new MacroSchedule { Kind = ScheduleKind.Daily, Time = "09:00", Days = MacroSchedule.EveryDay };
            var tuesday8 = new DateTime(2026, 11, 17, 8, 0, 0);   // a Tuesday
            Assert.Equal(new DateTime(2026, 11, 17, 9, 0, 0), s.NextRun(tuesday8).Value);
            Assert.Equal(new DateTime(2026, 11, 18, 9, 0, 0), s.NextRun(new DateTime(2026, 11, 17, 9, 0, 0)).Value, "exactly on time moves to tomorrow");
            s.Days = 1 << (int)DayOfWeek.Monday;
            Assert.Equal(new DateTime(2026, 11, 23, 9, 0, 0), s.NextRun(tuesday8).Value);
            s.Days = 0;
            Assert.Null(s.NextRun(tuesday8));
            s.Days = MacroSchedule.EveryDay;
            s.Time = "nine";
            Assert.Null(s.NextRun(tuesday8));
            s.Time = "7:05";
            Assert.Equal(new DateTime(2026, 11, 18, 7, 5, 0), s.NextRun(tuesday8).Value);
        }

        [Test]
        public static void OtherSchedulesFindTheNextTime()
        {
            var now = new DateTime(2026, 11, 17, 8, 0, 0);
            Assert.Equal(now.AddMinutes(30), new MacroSchedule { Kind = ScheduleKind.Every, EveryMinutes = 30 }.NextRun(now).Value);
            Assert.Equal(now.AddMinutes(1), new MacroSchedule { Kind = ScheduleKind.Every, EveryMinutes = 0 }.NextRun(now).Value, "at least a minute");
            Assert.Equal(new DateTime(2026, 11, 20, 8, 30, 0), new MacroSchedule { Kind = ScheduleKind.Once, At = "2026-11-20 08:30" }.NextRun(now).Value);
            Assert.Null(new MacroSchedule { Kind = ScheduleKind.Once, At = "2026-11-01 08:30" }.NextRun(now));
            Assert.Null(new MacroSchedule { Kind = ScheduleKind.Off }.NextRun(now));
        }

        [Test]
        public static void SchedulesDescribeThemselves()
        {
            Assert.Equal("Every day at 09:00", new MacroSchedule { Kind = ScheduleKind.Daily, Time = "9:00" }.Describe());
            Assert.Equal("Weekdays at 17:30", new MacroSchedule { Kind = ScheduleKind.Daily, Time = "17:30", Days = 62 }.Describe());
            Assert.Equal("Every 15 minutes", new MacroSchedule { Kind = ScheduleKind.Every, EveryMinutes = 15 }.Describe());
            Assert.Equal("Once on 24 Dec 2026 at 08:00", new MacroSchedule { Kind = ScheduleKind.Once, At = "2026-12-24 08:00" }.Describe());
            Assert.Equal("Not scheduled", new MacroSchedule().Describe());
        }

        [Test]
        public static void SchedulesSurviveSavingAndOldFilesGetOne()
        {
            var m = new MacroDef { Name = "x" };
            m.Schedule.Kind = ScheduleKind.Every;
            m.Schedule.EveryMinutes = 45;
            MacroDef back = Json.Deserialize<MacroDef>(Json.Serialize(m));
            Assert.Equal(ScheduleKind.Every, back.Schedule.Kind);
            Assert.Equal(45, back.Schedule.EveryMinutes);
            MacroDef old = Json.Deserialize<MacroDef>("{\"Name\":\"old\",\"Script\":\"a\"}");
            Assert.NotNull(old.Schedule);
            Assert.Equal(ScheduleKind.Off, old.Schedule.Kind);
        }

        [Test]
        public static void LicenseKeyFileIsSavedAndRemoved()
        {
            using (var dir = new TempDir())
            {
                var storage = new Storage(dir.Path);
                Assert.Null(storage.LoadLicenseKey());
                storage.SaveLicenseKey("MK1-ABC");
                Assert.Equal("MK1-ABC", storage.LoadLicenseKey());
                storage.SaveLicenseKey(null);
                Assert.Null(storage.LoadLicenseKey());
            }
        }

        // ---- Library ----

        [Test]
        public static void EveryLibraryMacroParses()
        {
            WithPro(true, () =>
            {
                foreach (LibraryPack pack in MacroLibrary.Packs)
                    foreach (LibraryItem item in pack.Items.Where(i => !i.IsPhrase))
                    {
                        ParseResult parsed = MacroParser.Parse(item.Script, true);
                        Assert.True(parsed.Ok, pack.Name + " / " + item.Name + ": " + (parsed.Ok ? "" : parsed.Errors[0].Message));
                    }
            });
            WithPro(false, () =>
            {
                foreach (LibraryItem item in MacroLibrary.Packs.Where(p => !p.Pro).SelectMany(p => p.Items).Where(i => !i.IsPhrase))
                    Assert.True(MacroParser.Parse(item.Script, true).Ok, "free pack works without Pro: " + item.Name);
            });
        }

        [Test]
        public static void LibraryPhrasesHaveUniqueTriggersAndAFreeStarter()
        {
            var triggers = MacroLibrary.Packs.SelectMany(p => p.Items).Where(i => i.IsPhrase).Select(i => i.Trigger).ToList();
            Assert.Equal(triggers.Count, triggers.Distinct().Count());
            Assert.True(MacroLibrary.Packs.Any(p => !p.Pro && p.Items.Count > 0), "a free pack");
            Assert.True(MacroLibrary.Packs.Count(p => p.Pro) >= 3, "Pro packs");
            MacroDef macro = MacroLibrary.Packs[0].Items.First(i => !i.IsPhrase).ToMacro();
            Assert.True(macro.Id.Length > 0);
            Assert.Equal(ScheduleKind.Off, macro.Schedule.Kind);
        }
    }
}
