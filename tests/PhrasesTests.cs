using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using MacroKit.Core;
using MacroKit.Phrases;

namespace MacroKit.Tests
{
    /// <summary>
    /// A fake keyboard plus a fake text field around one ExpansionEngine. Keys the engine lets through are "typed" into
    /// Screen; Backspaces and text the engine sends through the fake sink edit Screen too, so tests can check what the user would see.
    /// Nothing here touches real input, hooks, the clipboard or windows.
    /// </summary>
    sealed class PhraseRig : IDisposable
    {
        public const string Shrug = "¯\\_(ツ)_/¯";

        public readonly FakeInputSink Sink = new FakeInputSink();
        public readonly FakeClipboard Clip = new FakeClipboard();
        public readonly FakeClock Clock = new FakeClock();
        public readonly ExpansionEngine Engine;
        public readonly StringBuilder Screen = new StringBuilder();
        public string Process = "notepad.exe";
        /// <summary>Runs after every sink call (on whichever thread made it); lets a test type while an expansion is in progress.</summary>
        public Action<string> During;

        readonly Dictionary<string, char> charByKeyName = new Dictionary<string, char>();
        const string Punctuation = ".,;:!?-_()[]{}'\"/\\+=*&%$#@^~`|<>";

        public PhraseRig(bool inline, params PhraseDef[] phrases)
        {
            Engine = new ExpansionEngine(Sink, Clip, Clock);
            Engine.RunInline = inline;
            Engine.OwnProcessName = "Macro Kit";
            Engine.SetPhrases(phrases.Length == 0 ? new[] { Make("/shrug", Shrug) } : phrases);
            Sink.OnCall = line =>
            {
                Apply(line);
                if (During != null) During(line);
            };
        }

        public static PhraseDef Make(string trigger, string replacement, ExpandWhen when = ExpandWhen.Immediately,
            InsertMethod method = InsertMethod.Type, bool matchCase = false)
        {
            return new PhraseDef { Trigger = trigger, Replacement = replacement, When = when, Method = method, MatchCase = matchCase };
        }

        public static ushort VkOf(char c)
        {
            if (c >= 'a' && c <= 'z') return (ushort)char.ToUpperInvariant(c);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c;
            if (c == ' ') return 0x20;
            int i = Punctuation.IndexOf(c);
            return (ushort)(0xBA + (i < 0 ? 0 : i));
        }

        void Apply(string line)
        {
            lock (Screen)
            {
                if (line == "down BACK") { if (Screen.Length > 0) Screen.Length--; }
                else if (line.StartsWith("text ")) Screen.Append(line.Substring(5).Replace("\\n", "\n").Replace("\\t", "\t"));
                else if (line.StartsWith("down "))
                {
                    char c;
                    if (charByKeyName.TryGetValue(line.Substring(5), out c)) Screen.Append(c);
                }
            }
        }

        public string Text { get { lock (Screen) return Screen.ToString(); } }

        public int Backspaces { get { return Sink.Log.Count(l => l == "down BACK"); } }

        public bool Down(ushort vk, string typed, bool injected = false, string process = null)
        {
            var e = new KeyEvent { Vk = vk, Down = true, Injected = injected, TimeMs = Clock.NowMs };
            if (!injected && KeyNames.IsModifier(vk)) Sink.PhysicallyDown.Add(KeyNames.BaseVk(vk));   // the engine reads held modifiers from the key state
            if (!string.IsNullOrEmpty(typed) && typed[0] >= 0x20) charByKeyName[FakeInputSink.Name(vk)] = typed[0];
            bool swallowed = Engine.OnKey(e, typed, process ?? Process);
            if (!swallowed && !injected && !KeyNames.IsModifier(vk)) PassThrough(vk, typed);
            return swallowed;
        }

        public bool Up(ushort vk, bool injected = false, string process = null)
        {
            var e = new KeyEvent { Vk = vk, Down = false, Injected = injected, TimeMs = Clock.NowMs };
            if (!injected && KeyNames.IsModifier(vk)) Sink.PhysicallyDown.Remove(KeyNames.BaseVk(vk));
            return Engine.OnKey(e, null, process ?? Process);
        }

        void PassThrough(ushort vk, string typed)
        {
            lock (Screen)
            {
                if (vk == 0x08) { if (Screen.Length > 0) Screen.Length--; }
                else if (vk == 0x0D) Screen.Append('\n');
                else if (vk == 0x09) Screen.Append('\t');
                else if (!string.IsNullOrEmpty(typed) && typed[0] >= 0x20) Screen.Append(typed);
            }
        }

        /// <summary>Presses and releases one key. Returns whether the key-down was swallowed.</summary>
        public bool Press(ushort vk, string typed = null)
        {
            bool swallowed = Down(vk, typed);
            Up(vk);
            return swallowed;
        }

        /// <summary>Types text one character per key. Returns how many key-downs were swallowed.</summary>
        public int Type(string text)
        {
            int swallowedCount = 0;
            foreach (char c in text)
            {
                ushort vk = VkOf(c);
                charByKeyName[FakeInputSink.Name(vk)] = c;
                if (Press(vk, c.ToString())) swallowedCount++;
            }
            return swallowedCount;
        }

        public void PressEnter() { charByKeyName["ENTER"] = '\n'; Press(0x0D, "\r"); }
        public void PressTab() { charByKeyName["TAB"] = '\t'; Press(0x09, "\t"); }

        public void Dispose() { Engine.Dispose(); }
    }

    public static class PhrasesTests
    {
        static PhraseRig Inline(params PhraseDef[] phrases) { return new PhraseRig(true, phrases); }

        // ---- the /shrug example ----

        [Test]
        public static void Shrug_ReplacesTriggerWithExactlyFiveBackspaces()
        {
            using (var rig = Inline())
            {
                int swallowed = rig.Type("/shrug");
                Assert.Equal(1, swallowed, "only the last key is swallowed");
                Assert.Equal(PhraseRig.Shrug, rig.Text);
                Assert.Equal(5, rig.Backspaces);
                var expected = new List<string>();
                for (int i = 0; i < 5; i++) { expected.Add("down BACK"); expected.Add("up BACK"); }
                expected.Add("text " + PhraseRig.Shrug);
                Assert.SequenceEqual(expected, rig.Sink.Log);
            }
        }

        [Test]
        public static void Shrug_SwallowsFinalKeyDownAndItsKeyUp()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                Assert.True(rig.Down(PhraseRig.VkOf('g'), "g"), "key-down swallowed");
                Assert.True(rig.Up(PhraseRig.VkOf('g')), "key-up swallowed");
                Assert.False(rig.Up(PhraseRig.VkOf('g')), "a second key-up is not swallowed");
            }
        }

        [Test]
        public static void Shrug_PrecedingTextIsKept()
        {
            using (var rig = Inline())
            {
                rig.Type("well ");
                rig.Type("/shrug");
                Assert.Equal("well " + PhraseRig.Shrug, rig.Text);
            }
        }

        [Test]
        public static void Shrug_CanBeUsedTwiceInARow()
        {
            using (var rig = Inline())
            {
                rig.Type("/shrug");
                rig.Type("/shrug");
                Assert.Equal(PhraseRig.Shrug + PhraseRig.Shrug, rig.Text);
                Assert.Equal(10, rig.Backspaces);
            }
        }

        [Test]
        public static void Keys_BeforeTheFinalOneAreNotSwallowed()
        {
            using (var rig = Inline())
            {
                foreach (char c in "/shru")
                {
                    Assert.False(rig.Down(PhraseRig.VkOf(c), c.ToString()), "key " + c);
                    Assert.False(rig.Up(PhraseRig.VkOf(c)), "key-up " + c);
                }
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        // ---- case ----

        [Test]
        public static void Match_IsCaseInsensitiveByDefault()
        {
            foreach (string typed in new[] { "/SHRUG", "/Shrug", "/sHrUg" })
            {
                using (var rig = Inline())
                {
                    rig.Type(typed);
                    Assert.Equal(PhraseRig.Shrug, rig.Text, typed);
                }
            }
        }

        [Test]
        public static void MatchCase_RequiresTheExactCase()
        {
            using (var rig = Inline(PhraseRig.Make("/Shrug", "X", matchCase: true)))
            {
                rig.Type("/shrug");
                Assert.Equal("/shrug", rig.Text);
                Assert.Equal(0, rig.Sink.Log.Count);
                rig.Type(" /Shrug");
                Assert.Equal("/shrug X", rig.Text);
            }
        }

        [Test]
        public static void MatchCase_AppliesToDelimitedPhrasesToo()
        {
            using (var rig = Inline(PhraseRig.Make("BRB", "be right back", ExpandWhen.AfterDelimiter, matchCase: true)))
            {
                rig.Type("brb ");
                Assert.Equal(0, rig.Sink.Log.Count);
                rig.Type("BRB ");
                Assert.Contains(rig.Text, "be right back ");
            }
        }

        // ---- inside a word vs. word boundary ----

        [Test]
        public static void Immediate_ExpandsInsideAWord()
        {
            using (var rig = Inline())
            {
                rig.Type("abc/shrug");
                Assert.Equal("abc" + PhraseRig.Shrug, rig.Text);
            }
        }

        [Test]
        public static void Delimited_DoesNotExpandUntilTheDelimiter()
        {
            using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
            {
                rig.Type("brb");
                Assert.Equal("brb", rig.Text);
                Assert.Equal(0, rig.Sink.Log.Count);
                rig.Type(" ");
                Assert.Equal("be right back ", rig.Text);
                Assert.Equal(3, rig.Backspaces);
            }
        }

        [Test]
        public static void Delimited_NeedsAWordBoundaryBeforeTheTrigger()
        {
            using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
            {
                rig.Type("abrb ");
                Assert.Equal("abrb ", rig.Text);
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        [Test]
        public static void Delimited_ExpandsAfterWhitespaceOrPunctuation()
        {
            foreach (string prefix in new[] { "so ", "(", "\"", "x." })
            {
                using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
                {
                    rig.Type(prefix + "brb ");
                    Assert.Equal(prefix + "be right back ", rig.Text, "prefix " + prefix);
                }
            }
        }

        [Test]
        public static void Delimited_ExpandsAtTheStartOfTheBuffer()
        {
            using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
            {
                rig.Type("brb ");
                Assert.Equal("be right back ", rig.Text);
            }
        }

        [Test]
        public static void Delimited_ALongerWordDoesNotExpand()
        {
            using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
            {
                rig.Type("brbx ");
                Assert.Equal("brbx ", rig.Text);
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        // ---- delimiter variants ----

        [Test]
        public static void Delimiter_SpaceIsSwallowedAndResentAfterTheReplacement()
        {
            using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
            {
                rig.Type("brb");
                Assert.True(rig.Down(0x20, " "), "space down swallowed");
                Assert.True(rig.Up(0x20), "space up swallowed");
                Assert.SequenceEqual(new[] { "text be right back", "text  " }, rig.Sink.Log.Where(l => l.StartsWith("text")));
                Assert.Equal("be right back ", rig.Text);
            }
        }

        [Test]
        public static void Delimiter_EnterIsResentAsAnEnterKey()
        {
            using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
            {
                rig.Type("brb");
                rig.PressEnter();
                Assert.Equal("be right back\n", rig.Text);
                Assert.Equal("text \\n", rig.Sink.Log.Last());
                Assert.Equal(3, rig.Backspaces);
            }
        }

        [Test]
        public static void Delimiter_TabIsResentAsATabKey()
        {
            using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
            {
                rig.Type("brb");
                rig.PressTab();
                Assert.Equal("be right back\t", rig.Text);
                Assert.Equal("text \\t", rig.Sink.Log.Last());
            }
        }

        [Test]
        public static void Delimiter_PunctuationIsResent()
        {
            foreach (char punctuation in new[] { '.', ',', '!', '?', ';', ':', ')' })
            {
                using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
                {
                    rig.Type("brb" + punctuation);
                    Assert.Equal("be right back" + punctuation, rig.Text, "delimiter " + punctuation);
                }
            }
        }

        [Test]
        public static void Delimiter_SpaceWithoutTypedCharacterStillCounts()
        {
            using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
            {
                rig.Type("brb");
                rig.Press(0x20, null);
                Assert.Equal("be right back ", rig.Text);
            }
        }

        [Test]
        public static void Delimiter_KeysOtherThanTheDelimiterAreNotSwallowed()
        {
            using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
            {
                Assert.Equal(0, rig.Type("brb"));
            }
        }

        [Test]
        public static void Delimiter_ClearsTheBufferWhenNothingMatches()
        {
            using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
            {
                rig.PressEnter();
                rig.Type("brb ");
                Assert.Equal("\nbe right back ", rig.Text);
            }
        }

        // ---- backspace, clearing keys, reset ----

        [Test]
        public static void Backspace_RemovesTheLastBufferedCharacter()
        {
            using (var rig = Inline())
            {
                rig.Type("/shrux");
                rig.Press(0x08);
                rig.Type("g");
                Assert.Equal(PhraseRig.Shrug, rig.Text);
            }
        }

        [Test]
        public static void Backspace_OnAnEmptyBufferIsHarmless()
        {
            using (var rig = Inline())
            {
                rig.Press(0x08);
                rig.Press(0x08);
                rig.Type("/shrug");
                Assert.Equal(PhraseRig.Shrug, rig.Text);
            }
        }

        [Test]
        public static void Backspace_CanBreakATrigger()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                rig.Press(0x08);
                rig.Type("g");
                Assert.Equal("/shrg", rig.Text);
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        [Test]
        public static void CtrlBackspace_ClearsTheBuffer()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                rig.Down(0xA2, null);
                rig.Press(0x08);
                rig.Up(0xA2);
                rig.Type("g");
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        [Test]
        public static void ClearingKeys_ForgetWhatWasTyped()
        {
            var keys = new Dictionary<string, ushort>
            {
                { "Escape", 0x1B }, { "PageUp", 0x21 }, { "PageDown", 0x22 }, { "End", 0x23 }, { "Home", 0x24 }, { "Left", 0x25 },
                { "Up", 0x26 }, { "Right", 0x27 }, { "Down", 0x28 }, { "Insert", 0x2D }, { "Delete", 0x2E }
            };
            foreach (var key in keys)
            {
                using (var rig = Inline())
                {
                    rig.Type("/shru");
                    rig.Press(key.Value);
                    rig.Type("g");
                    Assert.Equal(0, rig.Sink.Log.Count, key.Key + " should clear");
                }
            }
        }

        [Test]
        public static void EnterAndTab_StartAFreshBuffer()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                rig.PressEnter();
                rig.Type("g");
                rig.Type("/shru");
                rig.PressTab();
                rig.Type("g");
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        [Test]
        public static void Reset_ForgetsWhatWasTyped()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                rig.Engine.Reset();
                rig.Type("g");
                Assert.Equal(0, rig.Sink.Log.Count);
                rig.Type("/shrug");
                Assert.Equal(5, rig.Backspaces);
            }
        }

        [Test]
        public static void KeysWithoutCharacters_LeaveTheBufferAlone()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                rig.Press(0x70, null);     // F1
                rig.Press(0x14, null);     // Caps Lock
                rig.Press(0x91, null);     // Scroll Lock
                rig.Type("g");
                Assert.Equal(5, rig.Backspaces);
            }
        }

        [Test]
        public static void ControlCharactersInTyped_AreNotAddedToTheBuffer()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                rig.Press(0x41, "\u0001");
                rig.Type("g");
                Assert.Equal(5, rig.Backspaces);
            }
        }

        [Test]
        public static void BufferKeepsOnlyTheLast64Characters()
        {
            using (var rig = Inline())
            {
                rig.Type(new string('x', 200));
                rig.Type("/shrug");
                Assert.Equal(new string('x', 200) + PhraseRig.Shrug, rig.Text);
                Assert.Equal(5, rig.Backspaces);
            }
            string longTrigger = new string('a', 64);
            using (var rig = Inline(PhraseRig.Make(longTrigger, "Z")))
            {
                rig.Type("b" + longTrigger);
                Assert.Equal("bZ", rig.Text);
                Assert.Equal(63, rig.Backspaces);
            }
        }

        [Test]
        public static void AfterAnExpansion_TheBufferStartsEmpty()
        {
            using (var rig = Inline(PhraseRig.Make("ab", "X"), PhraseRig.Make("bc", "Y")))
            {
                rig.Type("abc");
                Assert.Equal("Xc", rig.Text);
            }
        }

        // ---- shortcuts and AltGr ----

        [Test]
        public static void CtrlShortcut_ClearsTheBuffer()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                rig.Down(0xA2, null);
                rig.Press(0x56, null);   // Ctrl+V produces no character
                rig.Up(0xA2);
                rig.Type("g");
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        [Test]
        public static void AltShortcut_AndAltTab_ClearTheBuffer()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                rig.Down(0xA4, null);
                rig.Press(0x09, null);   // Alt+Tab
                rig.Up(0xA4);
                rig.Type("g");
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        [Test]
        public static void WinShortcut_ClearsTheBuffer()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                rig.Down(0x5B, null);
                rig.Press(0x45, null);   // Win+E
                rig.Up(0x5B);
                rig.Type("g");
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        [Test]
        public static void ReleasingTheModifier_ReturnsToNormalTyping()
        {
            using (var rig = Inline())
            {
                rig.Down(0xA2, null);
                rig.Press(0x43, null);
                rig.Up(0xA2);
                rig.Type("/shrug");
                Assert.Equal(5, rig.Backspaces);
            }
        }

        [Test]
        public static void ShiftedLetters_AreKept()
        {
            using (var rig = Inline())
            {
                rig.Down(0xA0, null);
                rig.Type("/SHRUG");
                rig.Up(0xA0);
                Assert.Equal(5, rig.Backspaces);
            }
        }

        [Test]
        public static void AltGr_CharactersAreKept()
        {
            using (var rig = Inline(PhraseRig.Make("5€", "five euros")))
            {
                rig.Type("5");
                rig.Down(0xA2, null);    // AltGr arrives as Ctrl + right Alt
                rig.Down(0xA5, null);
                bool swallowed = rig.Press(0x45, "€");
                rig.Up(0xA5);
                rig.Up(0xA2);
                Assert.True(swallowed, "the euro sign completed the trigger");
                Assert.Equal("five euros", rig.Text);
                Assert.Equal(1, rig.Backspaces);
            }
        }

        [Test]
        public static void AltGr_WithoutACharacterClears()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                rig.Down(0xA2, null);
                rig.Down(0xA5, null);
                rig.Press(0x70, null);
                rig.Up(0xA5);
                rig.Up(0xA2);
                rig.Type("g");
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        // ---- ignored input ----

        [Test]
        public static void InjectedKeys_AreIgnoredCompletely()
        {
            using (var rig = Inline())
            {
                foreach (char c in "/shrug")
                {
                    Assert.False(rig.Down(PhraseRig.VkOf(c), c.ToString(), injected: true));
                    Assert.False(rig.Up(PhraseRig.VkOf(c), injected: true));
                }
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        [Test]
        public static void InjectedKeys_DoNotDisturbTheBuffer()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                rig.Down(0x58, "x", injected: true);
                rig.Down(0x08, "\b", injected: true);
                rig.Down(0x1B, null, injected: true);
                rig.Type("g");
                Assert.Equal(5, rig.Backspaces);
            }
        }

        [Test]
        public static void InjectedModifiers_DoNotCountAsHeld()
        {
            using (var rig = Inline())
            {
                rig.Down(0xA2, null, injected: true);
                rig.Type("/shrug");
                Assert.Equal(5, rig.Backspaces);
            }
        }

        [Test]
        public static void Disabled_NothingExpandsAndNothingIsSwallowed()
        {
            using (var rig = Inline())
            {
                rig.Engine.Enabled = false;
                Assert.False(rig.Engine.Enabled);
                Assert.Equal(0, rig.Type("/shrug"));
                Assert.Equal("/shrug", rig.Text);
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        [Test]
        public static void Enabled_ChangeResetsTheBuffer()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                rig.Engine.Enabled = false;
                rig.Engine.Enabled = true;
                rig.Type("g");
                Assert.Equal(0, rig.Sink.Log.Count);
                rig.Type("/shrug");
                Assert.Equal(5, rig.Backspaces);
            }
        }

        [Test]
        public static void Disabled_TypingWhileOffIsNotRememberedWhenTurnedBackOn()
        {
            using (var rig = Inline())
            {
                rig.Engine.Enabled = false;
                rig.Type("/shru");
                rig.Engine.Enabled = true;
                rig.Type("g");
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        [Test]
        public static void ExcludedProcess_MatchesWithOrWithoutExeAndAnyCase()
        {
            foreach (string foreground in new[] { "game.exe", "GAME.EXE", "Game", "game" })
            {
                foreach (string listed in new[] { "game.exe", "Game.EXE", "GAME", " game " })
                {
                    using (var rig = Inline())
                    {
                        rig.Engine.SetExcluded(new[] { listed });
                        rig.Process = foreground;
                        Assert.Equal(0, rig.Type("/shrug"), foreground + " vs " + listed);
                        Assert.Equal(0, rig.Sink.Log.Count);
                    }
                }
            }
        }

        [Test]
        public static void ExcludedProcess_OtherProgramsStillExpand()
        {
            using (var rig = Inline())
            {
                rig.Engine.SetExcluded(new[] { "game.exe" });
                rig.Process = "notepad.exe";
                rig.Type("/shrug");
                Assert.Equal(PhraseRig.Shrug, rig.Text);
                rig.Process = "gamestop.exe";
                rig.Type("/shrug");
                Assert.Equal(PhraseRig.Shrug + PhraseRig.Shrug, rig.Text);
            }
        }

        [Test]
        public static void ExcludedProcess_ListCanBeReplacedAndCleared()
        {
            using (var rig = Inline())
            {
                rig.Engine.SetExcluded(new[] { "notepad.exe", null, "", "  " });
                Assert.Equal(0, rig.Type("/shrug"));
                rig.Engine.SetExcluded(null);
                Assert.Equal(1, rig.Type("/shrug"));
            }
        }

        [Test]
        public static void ExcludedProcess_TypingThereIsNotRememberedElsewhere()
        {
            using (var rig = Inline())
            {
                rig.Engine.SetExcluded(new[] { "game.exe" });
                rig.Process = "game.exe";
                rig.Type("/shru");
                rig.Process = "notepad.exe";
                rig.Type("g");
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        [Test]
        public static void OwnProcess_IsAlwaysExcluded()
        {
            foreach (string own in new[] { "Macro Kit.exe", "macro kit", "MACRO KIT.EXE" })
            {
                using (var rig = Inline())
                {
                    rig.Process = own;
                    Assert.Equal(0, rig.Type("/shrug"), own);
                    Assert.Equal(0, rig.Sink.Log.Count);
                }
            }
        }

        [Test]
        public static void OwnProcess_IsExcludedEvenWhenTheExcludedListIsReplaced()
        {
            using (var rig = Inline())
            {
                rig.Engine.SetExcluded(new string[0]);
                rig.Process = "Macro Kit.exe";
                Assert.Equal(0, rig.Type("/shrug"));
            }
        }

        [Test]
        public static void OwnProcess_DefaultsToTheRunningProcess()
        {
            var engine = new ExpansionEngine(new FakeInputSink(), new FakeClipboard(), new FakeClock());
            using (engine)
            {
                Assert.NotNull(engine.OwnProcessName);
                Assert.Equal(System.Diagnostics.Process.GetCurrentProcess().ProcessName, engine.OwnProcessName);
            }
        }

        [Test]
        public static void UnknownForegroundProcess_StillExpands()
        {
            using (var rig = Inline())
            {
                rig.Process = null;
                rig.Type("/shrug");
                Assert.Equal(PhraseRig.Shrug, rig.Text);
            }
        }

        // ---- which phrase wins ----

        [Test]
        public static void LongestTrigger_WinsWhenSeveralEndAtTheSameKey()
        {
            using (var rig = Inline(PhraseRig.Make("rug", "SHORT"), PhraseRig.Make("/shrug", "LONG")))
            {
                rig.Type("/shrug");
                Assert.Equal("LONG", rig.Text);
                Assert.Equal(5, rig.Backspaces);
            }
        }

        [Test]
        public static void LongestTrigger_DoesNotDependOnPhraseOrder()
        {
            using (var rig = Inline(PhraseRig.Make("/shrug", "LONG"), PhraseRig.Make("rug", "SHORT")))
            {
                rig.Type("/shrug");
                Assert.Equal("LONG", rig.Text);
            }
        }

        [Test]
        public static void ShortAndLongTriggers_EachWorkOnTheirOwn()
        {
            using (var rig = Inline(PhraseRig.Make("/s", "S", ExpandWhen.AfterDelimiter), PhraseRig.Make("/shrug", "LONG")))
            {
                rig.Type("/shrug");
                Assert.Equal("LONG", rig.Text);
            }
            using (var rig = Inline(PhraseRig.Make("/s", "S", ExpandWhen.AfterDelimiter), PhraseRig.Make("/shrug", "LONG")))
            {
                rig.Type("/s ");
                Assert.Equal("S ", rig.Text);
            }
        }

        [Test]
        public static void LongestDelimitedTrigger_WinsAtTheDelimiter()
        {
            using (var rig = Inline(PhraseRig.Make("b", "ONE", ExpandWhen.AfterDelimiter), PhraseRig.Make("a b", "TWO", ExpandWhen.AfterDelimiter)))
            {
                rig.Type("a b ");
                Assert.Equal("TWO ", rig.Text);
            }
        }

        [Test]
        public static void DisabledAndEmptyPhrases_AreIgnored()
        {
            var off = PhraseRig.Make("/off", "NO");
            off.Enabled = false;
            using (var rig = Inline(off, PhraseRig.Make("", "EMPTY"), PhraseRig.Make(null, "NULL"), null, PhraseRig.Make("/on", "YES")))
            {
                rig.Type("/off");
                Assert.Equal("/off", rig.Text);
                rig.Type("/on");
                Assert.Equal("/offYES", rig.Text);
            }
        }

        [Test]
        public static void EmptyReplacement_JustRemovesTheTrigger()
        {
            using (var rig = Inline(PhraseRig.Make("xx", "")))
            {
                rig.Type("axx");
                Assert.Equal("a", rig.Text);
                Assert.Equal(0, rig.Sink.Log.Count(l => l.StartsWith("text")));
            }
        }

        [Test]
        public static void SingleCharacterTrigger_NeedsNoBackspaces()
        {
            using (var rig = Inline(PhraseRig.Make("@", "at")))
            {
                rig.Type("@");
                Assert.Equal("at", rig.Text);
                Assert.Equal(0, rig.Backspaces);
            }
        }

        // ---- typing during an expansion ----

        [Test]
        public static void KeysTypedDuringAnExpansion_AreSwallowedThenReplayedInOrder()
        {
            using (var rig = Inline())
            {
                bool sawExpanding = false;
                var swallowed = new List<bool>();
                bool injected = false;
                rig.During = line =>
                {
                    if (!line.StartsWith("text ") || injected) return;
                    injected = true;
                    sawExpanding = rig.Engine.IsExpanding;
                    swallowed.Add(rig.Down(0x58, "x"));
                    swallowed.Add(rig.Up(0x58));
                    swallowed.Add(rig.Down(0x59, "y"));
                    swallowed.Add(rig.Up(0x59));
                };
                rig.Type("/shrug");
                Assert.True(sawExpanding, "IsExpanding is true during the expansion");
                Assert.SequenceEqual(new[] { true, true, true, true }, swallowed);
                Assert.False(rig.Engine.IsExpanding, "IsExpanding is false afterwards");
                Assert.SequenceEqual(new[] { "down X", "up X", "down Y", "up Y" }, rig.Sink.Log.Skip(rig.Sink.Log.Count - 4));
                Assert.Equal(PhraseRig.Shrug + "xy", rig.Text);
            }
        }

        [Test]
        public static void ReplayedKeys_AreNotInterleavedWithTheReplacement()
        {
            using (var rig = Inline())
            {
                bool injected = false;
                rig.During = line =>
                {
                    if (injected || !line.StartsWith("down BACK")) return;
                    injected = true;
                    rig.Press(0x5A, "z");   // typed while the backspaces are still being sent
                };
                rig.Type("/shrug");
                int textIndex = rig.Sink.Log.FindIndex(l => l.StartsWith("text "));
                int replayIndex = rig.Sink.Log.IndexOf("down Z");
                Assert.True(textIndex >= 0 && replayIndex > textIndex, "the replacement comes before the replayed key");
            }
        }

        [Test]
        public static void ReplayedModifiers_KeepTheirOrder()
        {
            using (var rig = Inline())
            {
                bool injected = false;
                rig.During = line =>
                {
                    if (injected || !line.StartsWith("text ")) return;
                    injected = true;
                    rig.Down(0xA0, null);
                    rig.Press(0x41, "A");
                    rig.Up(0xA0);
                };
                rig.Type("/shrug");
                Assert.SequenceEqual(new[] { "down LSHIFT", "down A", "up A", "up LSHIFT" }, rig.Sink.Log.Skip(rig.Sink.Log.Count - 4));
            }
        }

        [Test]
        public static void KeyUpOfTheTriggerKey_ArrivingDuringTheExpansion_IsSwallowedNotReplayed()
        {
            using (var rig = Inline())
            {
                bool swallowedUp = false, swallowedRepeat = false, once = false;
                rig.During = line =>
                {
                    if (once || !line.StartsWith("text ")) return;
                    once = true;
                    swallowedRepeat = rig.Down(PhraseRig.VkOf('g'), "g");   // auto-repeat of the held trigger key
                    swallowedUp = rig.Up(PhraseRig.VkOf('g'));
                };
                rig.Type("/shru");
                rig.Down(PhraseRig.VkOf('g'), "g");
                Assert.True(swallowedRepeat, "repeat swallowed");
                Assert.True(swallowedUp, "key-up swallowed");
                Assert.False(rig.Sink.Log.Contains("down G"), "not replayed");
                Assert.False(rig.Sink.Log.Contains("up G"), "not replayed");
                Assert.False(rig.Up(PhraseRig.VkOf('g')), "the real key-up is no longer pending");
            }
        }

        [Test]
        public static void KeysTypedDuringAnExpansion_FeedTheBufferAfterwards()
        {
            using (var rig = Inline(PhraseRig.Make("/shrug", "S"), PhraseRig.Make("qz", "Q")))
            {
                bool once = false;
                rig.During = line =>
                {
                    if (once || !line.StartsWith("text ")) return;
                    once = true;
                    rig.Press(PhraseRig.VkOf('q'), "q");
                };
                rig.Type("/shrug");
                rig.Type("z");
                Assert.Equal("S" + "Q", rig.Text);
            }
        }

        [Test]
        public static void ShortcutTypedDuringAnExpansion_ClearsTheBufferAfterwards()
        {
            using (var rig = Inline(PhraseRig.Make("/shrug", "S"), PhraseRig.Make("qz", "Q")))
            {
                bool once = false;
                rig.During = line =>
                {
                    if (once || !line.StartsWith("text ")) return;
                    once = true;
                    rig.Press(PhraseRig.VkOf('q'), "q");
                    rig.Down(0xA2, null);
                    rig.Press(0x43, null);
                    rig.Up(0xA2);
                };
                rig.Type("/shrug");
                rig.Type("z");
                Assert.Equal("Sqz", rig.Text);
            }
        }

        [Test]
        public static void AfterTheExpansion_KeysFlowAgain()
        {
            using (var rig = Inline())
            {
                rig.Type("/shrug");
                Assert.False(rig.Down(0x41, "a"), "normal typing is not swallowed");
                Assert.False(rig.Engine.IsExpanding);
            }
        }

        // ---- paste mode ----

        [Test]
        public static void Paste_SetsClipboardPastesWaitsAndRestoresText()
        {
            using (var rig = Inline(PhraseRig.Make("/addr", "1 Main Street", method: InsertMethod.Paste)))
            {
                rig.Clip.Text = "original";
                rig.Type("/addr");
                Assert.SequenceEqual(new[] { "1 Main Street", "original" }, rig.Clip.History);
                Assert.Equal("original", rig.Clip.Text);
                Assert.True(rig.Sink.Log.Contains("combo LCONTROL+V"), "Ctrl+V sent");
                Assert.False(rig.Sink.Log.Any(l => l.StartsWith("text ")), "nothing typed");
                Assert.True(rig.Clock.Sleeps.Contains(250), "waited 250 ms before restoring");
                Assert.Equal(4, rig.Backspaces);
            }
        }

        [Test]
        public static void Paste_PastesBeforeRestoring()
        {
            using (var rig = Inline(PhraseRig.Make("/addr", "1 Main Street", method: InsertMethod.Paste)))
            {
                rig.Clip.Text = "original";
                string clipboardWhenPasting = null;
                rig.During = line => { if (line.StartsWith("combo")) clipboardWhenPasting = rig.Clip.Text; };
                rig.Type("/addr");
                Assert.Equal("1 Main Street", clipboardWhenPasting);
            }
        }

        [Test]
        public static void Paste_WithNoTextOnTheClipboardLeavesItEmpty()
        {
            using (var rig = Inline(PhraseRig.Make("/addr", "1 Main Street", method: InsertMethod.Paste)))
            {
                rig.Clip.Text = null;
                rig.Type("/addr");
                Assert.Equal("", rig.Clip.Text);
            }
        }

        [Test]
        public static void Paste_DelimitedPhraseResendsTheDelimiterAfterTheRestore()
        {
            using (var rig = Inline(PhraseRig.Make("addr", "1 Main Street", ExpandWhen.AfterDelimiter, InsertMethod.Paste)))
            {
                rig.Clip.Text = "keep";
                rig.Type("addr ");
                Assert.Equal("keep", rig.Clip.Text);
                Assert.Equal("text  ", rig.Sink.Log.Last());
                Assert.Equal(4, rig.Backspaces);
            }
        }

        // ---- text content ----

        [Test]
        public static void MultiLineReplacement_IsHandedToTheSinkWithItsLineBreaks()
        {
            using (var rig = Inline(PhraseRig.Make("/sig", "Thanks,\nSam")))
            {
                rig.Type("/sig");
                Assert.True(rig.Sink.Log.Contains("text Thanks,\\nSam"));
                Assert.Equal("Thanks,\nSam", rig.Text);
            }
        }

        [Test]
        public static void Unicode_SurrogatePairsPassThroughUnchanged()
        {
            string emoji = "ok 😀 👍 done";
            using (var rig = Inline(PhraseRig.Make("/ok", emoji)))
            {
                rig.Type("/ok");
                Assert.True(rig.Sink.Log.Contains("text " + emoji));
                Assert.Equal(emoji, rig.Text);
            }
        }

        [Test]
        public static void Unicode_TriggerWithNonAsciiCharacters()
        {
            using (var rig = Inline(PhraseRig.Make("café", "coffee")))
            {
                rig.Press(0x43, "c");
                rig.Press(0x41, "a");
                rig.Press(0x46, "f");
                Assert.True(rig.Press(0xBA, "é"));
                Assert.Equal("coffee", rig.Text);
                Assert.Equal(3, rig.Backspaces);
            }
        }

        [Test]
        public static void Unicode_CaseInsensitiveMatchAcrossNonAscii()
        {
            using (var rig = Inline(PhraseRig.Make("café", "coffee")))
            {
                rig.Press(0x43, "C");
                rig.Press(0x41, "A");
                rig.Press(0x46, "F");
                Assert.True(rig.Press(0xBA, "É"));
            }
        }

        // ---- events and failures ----

        [Test]
        public static void ExpandedEvent_ReportsThePhraseAfterTyping()
        {
            using (var rig = Inline())
            {
                PhraseDef reported = null;
                int logCountAtEvent = -1;
                rig.Engine.Expanded += p => { reported = p; logCountAtEvent = rig.Sink.Log.Count; };
                rig.Type("/shrug");
                Assert.NotNull(reported);
                Assert.Equal("/shrug", reported.Trigger);
                Assert.Equal(11, logCountAtEvent);   // 5 backspaces (down + up) and the text, all sent before the event
            }
        }

        [Test]
        public static void ExpandedEvent_NotRaisedWhenNothingExpands()
        {
            using (var rig = Inline())
            {
                int raised = 0;
                rig.Engine.Expanded += p => raised++;
                rig.Type("/shru");
                Assert.Equal(0, raised);
            }
        }

        [Test]
        public static void ExpandedEvent_AHandlerThatThrowsDoesNotBreakTheEngine()
        {
            using (var rig = Inline())
            {
                rig.Engine.Expanded += p => { throw new InvalidOperationException("handler bug"); };
                rig.Type("/shrug");
                Assert.False(rig.Engine.IsExpanding);
                rig.Type("/shrug");
                Assert.Equal(PhraseRig.Shrug + PhraseRig.Shrug, rig.Text);
            }
        }

        [Test]
        public static void SinkFailure_IsContainedAndTheEngineRecovers()
        {
            using (var rig = Inline())
            {
                bool failOnce = true;
                rig.Sink.FailWhen = line =>
                {
                    if (failOnce && line.StartsWith("text ")) { failOnce = false; return true; }
                    return false;
                };
                int raised = 0;
                var failures = new List<string>();
                rig.Engine.Expanded += p => raised++;
                rig.Engine.ExpansionFailed += failures.Add;
                rig.Type("/shrug");   // the text call fails inside the engine; OnKey must not throw
                Assert.False(rig.Engine.IsExpanding, "not stuck expanding");
                Assert.Equal(0, raised);
                Assert.Equal(1, failures.Count, "the user is told");
                Assert.Equal("/shrug", rig.Text, "the erased trigger was typed back");
                rig.Type("/shrug");
                Assert.Equal(1, raised);
                Assert.Equal("/shrug" + PhraseRig.Shrug, rig.Text);
            }
        }

        [Test]
        public static void SinkFailure_KeysTypedMeanwhileAreStillReplayed()
        {
            using (var rig = Inline())
            {
                bool failOnce = true;
                rig.Sink.FailWhen = line =>
                {
                    if (failOnce && line.StartsWith("text ")) { failOnce = false; rig.Press(0x4B, "k"); return true; }
                    return false;
                };
                rig.Type("/shrug");
                Assert.True(rig.Sink.Log.Contains("down K"));
                Assert.False(rig.Engine.IsExpanding);
            }
        }

        [Test]
        public static void OnKey_NeverThrows()
        {
            using (var rig = Inline())
            {
                var e = new KeyEvent { Vk = 0xFFFF, Down = true };
                Assert.False(rig.Engine.OnKey(e, null, null));
                Assert.False(rig.Engine.OnKey(e, new string('x', 1000), "x"));
                Assert.False(rig.Engine.OnKey(new KeyEvent { Vk = 0x41 }, "a", null));
                Assert.False(rig.Engine.OnKey(new KeyEvent { Vk = 0x8041, Down = true }, "a", "x.exe"));
            }
        }

        [Test]
        public static void LongTypedString_RollsTheBufferWithoutError()
        {
            using (var rig = Inline())
            {
                Assert.False(rig.Engine.OnKey(new KeyEvent { Vk = 0x41, Down = true }, new string('x', 500), "notepad.exe"));
            }
        }

        // ---- changing phrases ----

        [Test]
        public static void SetPhrases_ReplacesThePhraseSet()
        {
            using (var rig = Inline())
            {
                rig.Engine.SetPhrases(new[] { PhraseRig.Make("/hi", "HELLO") });
                rig.Type("/shrug");
                Assert.Equal("/shrug", rig.Text);
                rig.Type("/hi");
                Assert.Equal("/shrugHELLO", rig.Text);
            }
        }

        [Test]
        public static void SetPhrases_NullOrEmptyDisablesExpansion()
        {
            using (var rig = Inline())
            {
                rig.Engine.SetPhrases(null);
                rig.Type("/shrug");
                rig.Engine.SetPhrases(new PhraseDef[0]);
                rig.Type("/shrug");
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        [Test]
        public static void SetPhrases_LaterEditsToThePhraseObjectDoNotAffectTheSnapshot()
        {
            var phrase = PhraseRig.Make("/ed", "OLD");
            using (var rig = Inline(phrase))
            {
                phrase.Replacement = "NEW";
                phrase.Trigger = "/zz";
                rig.Type("/ed");
                Assert.Equal("OLD", rig.Text);
            }
        }

        [Test]
        public static void SetPhrases_WhileTyping_IsSafe()
        {
            using (var rig = Inline())
            {
                var stop = new ManualResetEventSlim(false);
                Exception failure = null;
                var swapper = new Thread(() =>
                {
                    try
                    {
                        int i = 0;
                        while (!stop.IsSet)
                        {
                            rig.Engine.SetPhrases(new[] { PhraseRig.Make("/shrug", PhraseRig.Shrug), PhraseRig.Make("/n" + (i++ % 7), "N") });
                            rig.Engine.SetExcluded(new[] { "other.exe" });
                        }
                    }
                    catch (Exception ex) { failure = ex; }
                });
                swapper.Start();
                int expanded = 0;
                for (int i = 0; i < 300; i++)
                {
                    if (rig.Type("/shrug") == 1) expanded++;
                }
                stop.Set();
                swapper.Join();
                Assert.True(failure == null, failure == null ? "" : failure.Message);
                Assert.Equal(300, expanded);
            }
        }

        // ---- the worker thread ----

        [Test]
        public static void Worker_ExpandsOffTheCallingThread()
        {
            using (var rig = new PhraseRig(false))
            {
                int callerThread = Thread.CurrentThread.ManagedThreadId;
                int sinkThread = -1, eventThread = -1;
                rig.During = line => { if (line.StartsWith("text ")) sinkThread = Thread.CurrentThread.ManagedThreadId; };
                rig.Engine.Expanded += p => eventThread = Thread.CurrentThread.ManagedThreadId;
                rig.Type("/shrug");
                Assert.True(rig.Engine.WaitIdle(5000), "worker finished");
                Assert.Equal(PhraseRig.Shrug, rig.Text);
                Assert.True(sinkThread != -1 && sinkThread != callerThread, "typing ran on the worker");
                Assert.Equal(sinkThread, eventThread);
            }
        }

        [Test]
        public static void Worker_KeysDuringAnExpansionAreHeldBackAndReplayed()
        {
            using (var rig = new PhraseRig(false))
            {
                var inText = new ManualResetEventSlim(false);
                var release = new ManualResetEventSlim(false);
                rig.During = line =>
                {
                    if (!line.StartsWith("text ")) return;
                    inText.Set();
                    release.Wait(5000);
                };
                rig.Type("/shrug");
                Assert.True(inText.Wait(5000), "worker reached the typing step");
                Assert.True(rig.Engine.IsExpanding);
                Assert.True(rig.Down(0x41, "a"), "swallowed while expanding");
                Assert.True(rig.Up(0x41), "key-up swallowed while expanding");
                Assert.True(rig.Down(0x42, "b"));
                Assert.True(rig.Up(0x42));
                release.Set();
                Assert.True(rig.Engine.WaitIdle(5000), "worker finished");
                Assert.False(rig.Engine.IsExpanding);
                Assert.SequenceEqual(new[] { "down A", "up A", "down B", "up B" }, rig.Sink.Log.Skip(rig.Sink.Log.Count - 4));
                Assert.False(rig.Down(0x43, "c"), "flowing again");
            }
        }

        [Test]
        public static void Worker_HandlesManyExpansionsInARow()
        {
            using (var rig = new PhraseRig(false))
            {
                for (int i = 0; i < 25; i++)
                {
                    rig.Type("/shrug");
                    Assert.True(rig.Engine.WaitIdle(5000));
                }
                Assert.Equal(25, rig.Sink.Log.Count(l => l.StartsWith("text ")));
            }
        }

        [Test]
        public static void Dispose_StopsTheWorkerAndFurtherKeysPassThrough()
        {
            var rig = new PhraseRig(false);
            rig.Type("/shrug");
            Assert.True(rig.Engine.WaitIdle(5000));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            rig.Engine.Dispose();
            rig.Engine.Dispose();   // twice is fine
            Assert.True(watch.ElapsedMilliseconds < 1500, "Dispose returns promptly");
            int before = rig.Sink.Log.Count;
            Assert.Equal(0, rig.Type("/shrug"));
            Assert.Equal(before, rig.Sink.Log.Count);
        }

        [Test]
        public static void Dispose_WithoutEverExpandingIsFine()
        {
            var engine = new ExpansionEngine(new FakeInputSink(), new FakeClipboard(), new FakeClock());
            engine.Dispose();
            Assert.False(engine.OnKey(new KeyEvent { Vk = 0x41, Down = true }, "a", "notepad.exe"));
        }

        [Test]
        public static void Constructor_RejectsNullDependencies()
        {
            Assert.Throws<ArgumentNullException>(() => new ExpansionEngine(null, new FakeClipboard(), new FakeClock()));
            Assert.Throws<ArgumentNullException>(() => new ExpansionEngine(new FakeInputSink(), null, new FakeClock()));
            Assert.Throws<ArgumentNullException>(() => new ExpansionEngine(new FakeInputSink(), new FakeClipboard(), null));
        }

        // ---- a full 64-character buffer ----

        [Test]
        public static void AfterDelimiter_WorksWhenTheBufferIsFull()
        {
            for (int pad = 50; pad <= 80; pad++)
            {
                using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
                {
                    string typed = new string('x', pad) + " brb ";
                    rig.Type(typed);
                    Assert.Equal(new string('x', pad) + " be right back ", rig.Text, "with " + pad + " characters typed before the trigger");
                    Assert.Equal(3, rig.Backspaces, "backspaces with " + pad);
                }
            }
        }

        [Test]
        public static void AfterDelimiter_StillNeedsAWordStartWhenTheBufferIsFull()
        {
            for (int pad = 60; pad <= 70; pad++)
            {
                using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
                {
                    rig.Type(new string('x', pad) + "brb ");   // "xxxxbrb": the trigger is the end of a longer word
                    Assert.Equal(0, rig.Backspaces, "no expansion inside a word, pad " + pad);
                    Assert.Equal(new string('x', pad) + "brb ", rig.Text);
                }
            }
        }

        [Test]
        public static void AfterDelimiter_FullBufferWithPunctuationDelimiter()
        {
            using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
            {
                rig.Type(new string('y', 70) + " brb.");
                Assert.Equal(new string('y', 70) + " be right back.", rig.Text);
            }
        }

        [Test]
        public static void Immediate_WorksWhenTheBufferIsFull()
        {
            for (int pad = 60; pad <= 70; pad++)
            {
                using (var rig = Inline())
                {
                    rig.Type(new string('x', pad) + "/shrug");
                    Assert.Equal(new string('x', pad) + PhraseRig.Shrug, rig.Text, "pad " + pad);
                    Assert.Equal(5, rig.Backspaces);
                }
            }
        }

        [Test]
        public static void ManyPhrasesInARowWithAFullBuffer()
        {
            using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter), PhraseRig.Make("/shrug", "S")))
            {
                rig.Type(new string('z', 90) + " brb ");
                rig.Type("and then /shrug");
                rig.Type(" brb ");
                Assert.Equal(new string('z', 90) + " be right back and then S be right back ", rig.Text);
            }
        }

        // ---- Enter and Tab as the delimiter ----

        [Test]
        public static void AfterDelimiter_EnterIsSentBackAsTheEnterKeyText()
        {
            using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
            {
                rig.Type("brb");
                bool swallowed = rig.Down(0x0D, "\r");
                rig.Up(0x0D);
                Assert.True(swallowed, "the Enter key-down was swallowed");
                Assert.Equal("text \\n", rig.Sink.Log.Last(), "the sink gets a newline, which it presses as the real Enter key");
                Assert.Equal("be right back\n", rig.Text);
            }
        }

        [Test]
        public static void AfterDelimiter_TabIsSentBackAsTheTabKeyText()
        {
            using (var rig = Inline(PhraseRig.Make("brb", "be right back", ExpandWhen.AfterDelimiter)))
            {
                rig.Type("brb");
                rig.Down(0x09, "\t");
                rig.Up(0x09);
                Assert.Equal("text \\t", rig.Sink.Log.Last());
                Assert.Equal("be right back\t", rig.Text);
            }
        }

        // ---- modifier flags follow the real key state ----

        [Test]
        public static void ALostCtrlKeyUpDoesNotStopExpansion()
        {
            using (var rig = Inline())
            {
                rig.Down(0xA2, null);                  // the engine sees Ctrl go down...
                rig.Sink.PhysicallyDown.Remove(0xA2);  // ...but never its key-up (Win+L, UAC, a recording that started with the hotkey held)
                rig.Type("/shrug");
                Assert.Equal(PhraseRig.Shrug, rig.Text);
                Assert.Equal(5, rig.Backspaces);
            }
        }

        [Test]
        public static void ALostAltGrKeyUpDoesNotBreakBackspaceCorrection()
        {
            using (var rig = Inline())
            {
                rig.Down(0xA2, null);
                rig.Down(0xA5, null);                  // AltGr = Ctrl + right Alt, both key-ups lost
                rig.Sink.PhysicallyDown.Remove(0xA2);
                rig.Sink.PhysicallyDown.Remove(0xA5);
                rig.Type("/shrx");
                rig.Press(0x08);
                rig.Type("ug");
                Assert.Equal(PhraseRig.Shrug, rig.Text, "backspace still edits the buffer");
            }
        }

        [Test]
        public static void ALostWinKeyUpDoesNotMakeEveryKeyAShortcut()
        {
            using (var rig = Inline())
            {
                rig.Down(0x5B, null);
                rig.Sink.PhysicallyDown.Remove(0x5B);
                rig.Type("/shrug");
                Assert.Equal(PhraseRig.Shrug, rig.Text);
            }
        }

        [Test]
        public static void AModifierHeldBeforeTheEngineWatchedStillCountsAsHeld()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                rig.Sink.PhysicallyDown.Add(0xA2);     // Ctrl went down without the engine seeing the event
                rig.Press(0x43, null);                 // Ctrl+C
                rig.Sink.PhysicallyDown.Remove(0xA2);
                rig.Type("g");
                Assert.Equal(0, rig.Sink.Log.Count, "the shortcut cleared the buffer, so no expansion");
            }
        }

        [Test]
        public static void ModifierEventsStillTrackWhileTheKeyIsHeld()
        {
            using (var rig = Inline())
            {
                rig.Type("/shru");
                rig.Down(0xA2, null);
                rig.Press(0x08);                       // Ctrl+Backspace clears
                rig.Up(0xA2);
                rig.Type("g");
                Assert.Equal(0, rig.Sink.Log.Count);
            }
        }

        // ---- paste mode keeps the user's clipboard ----

        static PhraseRig PasteRig()
        {
            return Inline(PhraseRig.Make("/addr", "1 Main Street", method: InsertMethod.Paste));
        }

        [Test]
        public static void Paste_PutsPicturesAndOtherFormatsBack()
        {
            using (var rig = PasteRig())
            {
                rig.Clip.Text = "original";
                rig.Clip.Other = "a picture";
                rig.Type("/addr");
                Assert.Equal("original", rig.Clip.Text);
                Assert.Equal("a picture", rig.Clip.Other);
                Assert.Equal(1, rig.Clip.Restores);
            }
        }

        [Test]
        public static void Paste_PutsAnEmptyClipboardBackEmpty()
        {
            using (var rig = PasteRig())
            {
                rig.Clip.Text = null;
                rig.Clip.Other = null;
                rig.Type("/addr");
                Assert.Equal("", rig.Clip.Text);
                Assert.Null(rig.Clip.Other);
            }
        }

        [Test]
        public static void Paste_KeepsTheTextOutOfClipboardHistory()
        {
            using (var rig = PasteRig())
            {
                rig.Clip.Text = "original";
                rig.Type("/addr");
                Assert.SequenceEqual(new[] { "1 Main Street" }, rig.Clip.PrivateHistory);
            }
        }

        [Test]
        public static void Paste_WritesTheClipboardBeforeTheFirstBackspace()
        {
            using (var rig = PasteRig())
            {
                rig.Clip.Text = "original";
                string clipboardAtFirstBackspace = null;
                rig.During = line => { if (line == "down BACK" && clipboardAtFirstBackspace == null) clipboardAtFirstBackspace = rig.Clip.Text; };
                rig.Type("/addr");
                Assert.Equal("1 Main Street", clipboardAtFirstBackspace);
            }
        }

        [Test]
        public static void Paste_BusyClipboardLeavesTheTriggerTypedAndSaysSo()
        {
            using (var rig = PasteRig())
            {
                rig.Clip.Text = "original";
                rig.Clip.FailWrites = true;
                var failures = new List<string>();
                int expanded = 0;
                rig.Engine.ExpansionFailed += failures.Add;
                rig.Engine.Expanded += p => expanded++;
                rig.Type("/addr");
                Assert.Equal("/addr", rig.Text, "the trigger stays typed, including the key that was held back");
                Assert.Equal(0, rig.Backspaces, "nothing was erased");
                Assert.False(rig.Sink.Log.Any(l => l.StartsWith("combo")), "nothing was pasted");
                Assert.Equal("original", rig.Clip.Text);
                Assert.Equal(1, failures.Count);
                Assert.Contains(failures[0], "clipboard");
                Assert.False(failures[0].Contains("Main Street") || failures[0].Contains("/addr"), "the message never repeats phrase text");
                Assert.Equal(0, expanded);
                Assert.False(rig.Engine.IsExpanding);
                rig.Clip.FailWrites = false;
                rig.Engine.Reset();
                rig.Type("/addr");
                Assert.True(rig.Sink.Log.Contains("combo LCONTROL+V"), "the next try pastes");
                Assert.Equal(1, expanded);
                Assert.Equal("original", rig.Clip.Text);
            }
        }

        [Test]
        public static void Paste_UnreadableClipboardLeavesTheTriggerTypedToo()
        {
            using (var rig = PasteRig())
            {
                rig.Clip.Text = "original";
                rig.Clip.FailSnapshot = true;
                var failures = new List<string>();
                rig.Engine.ExpansionFailed += failures.Add;
                rig.Type("/addr");
                Assert.Equal("/addr", rig.Text);
                Assert.Equal(0, rig.Backspaces);
                Assert.Equal(1, failures.Count);
                Assert.Equal("original", rig.Clip.Text, "never overwritten when it could not be saved first");
            }
        }

        [Test]
        public static void Paste_BusyClipboardWithADelimiterTypesTheDelimiterBack()
        {
            using (var rig = Inline(PhraseRig.Make("addr", "1 Main Street", ExpandWhen.AfterDelimiter, InsertMethod.Paste)))
            {
                rig.Clip.FailWrites = true;
                rig.Type("addr ");
                Assert.Equal("addr ", rig.Text);
            }
        }

        [Test]
        public static void Paste_DoesNotRestoreOverSomethingTheUserCopiedMeanwhile()
        {
            using (var rig = PasteRig())
            {
                rig.Clip.Text = "original";
                rig.During = line => { if (line.StartsWith("combo")) rig.Clip.SimulateOtherAppCopy("copied by the user"); };
                rig.Type("/addr");
                Assert.Equal("copied by the user", rig.Clip.Text);
                Assert.Equal(0, rig.Clip.Restores, "the newer copy wins");
            }
        }

        [Test]
        public static void Paste_RestoresEvenWhenCtrlVFails()
        {
            using (var rig = PasteRig())
            {
                rig.Clip.Text = "original";
                rig.Sink.FailWhen = line => line.StartsWith("combo");
                var failures = new List<string>();
                rig.Engine.ExpansionFailed += failures.Add;
                rig.Type("/addr");
                Assert.Equal("original", rig.Clip.Text);
                Assert.Equal(1, rig.Clip.Restores);
                Assert.Equal("/addr", rig.Text, "the erased trigger was typed back");
                Assert.Equal(1, failures.Count);
            }
        }

        [Test]
        public static void Paste_FailedRestoreIsContained()
        {
            using (var rig = PasteRig())
            {
                rig.Clip.Text = "original";
                rig.Clip.FailRestore = true;
                int expanded = 0;
                rig.Engine.Expanded += p => expanded++;
                rig.Type("/addr");
                Assert.False(rig.Engine.IsExpanding);
                Assert.Equal(1, expanded, "the paste itself worked");
            }
        }

        [Test]
        public static void Paste_ShiftHeldIsReleasedBeforeTheBackspacesAndNeverPressedAgain()
        {
            using (var rig = PasteRig())
            {
                rig.Clip.Text = "original";
                rig.Sink.PhysicallyDown.Add(0xA0);     // the ':' of a trigger such as ":)" is still held
                rig.Type("/addr");
                List<string> log = rig.Sink.Log;
                int shiftUp = log.IndexOf("up LSHIFT");
                Assert.True(shiftUp >= 0, "Shift was released");
                Assert.True(shiftUp < log.IndexOf("down BACK"), "before the first backspace");
                Assert.True(shiftUp < log.IndexOf("combo LCONTROL+V"), "so the paste is Ctrl+V and not Ctrl+Shift+V");
                Assert.False(log.Contains("down LSHIFT"), "and it was not pressed again");
                Assert.True(rig.Clock.Sleeps.Count(ms => ms == 10) >= 29, "it waited about 300 ms first");
            }
        }

        [Test]
        public static void Paste_ShiftReleasedInTimeIsLeftAlone()
        {
            using (var rig = PasteRig())
            {
                rig.Sink.PhysicallyDown.Add(0xA0);
                rig.Clock.After(100, () => rig.Sink.PhysicallyDown.Remove(0xA0));
                rig.Type("/addr");
                Assert.False(rig.Sink.Log.Contains("up LSHIFT"), "no key-up needed");
                Assert.True(rig.Sink.Log.Contains("combo LCONTROL+V"));
                Assert.True(rig.Clock.Sleeps.Count(ms => ms == 10) < 29, "it did not wait the full 300 ms");
            }
        }

        // ---- held modifiers when typing ----

        [Test]
        public static void Type_HeldCtrlIsReleasedBeforeTheBackspaces()
        {
            using (var rig = Inline())
            {
                rig.Engine.BeforeExecute = () => rig.Sink.PhysicallyDown.Add(0xA2);   // pressed in the instant before the worker starts
                rig.Type("/shrug");
                List<string> log = rig.Sink.Log;
                Assert.Equal("up LCONTROL", log[0], "Ctrl goes up first, otherwise Backspace would be Ctrl+Backspace");
                Assert.Equal(5, rig.Backspaces);
                Assert.False(log.Contains("down LCONTROL"));
                Assert.Equal(PhraseRig.Shrug, rig.Text);
            }
        }

        [Test]
        public static void Type_HeldWinAndAltAreReleasedToo()
        {
            using (var rig = Inline())
            {
                rig.Engine.BeforeExecute = () => { rig.Sink.PhysicallyDown.Add(0x5B); rig.Sink.PhysicallyDown.Add(0xA4); };
                rig.Type("/shrug");
                Assert.True(rig.Sink.Log.IndexOf("up LWIN") < rig.Sink.Log.IndexOf("down BACK"));
                Assert.True(rig.Sink.Log.IndexOf("up LALT") < rig.Sink.Log.IndexOf("down BACK"));
            }
        }

        [Test]
        public static void Type_ModifierReleasedInTimeIsNotTouched()
        {
            using (var rig = Inline())
            {
                rig.Engine.BeforeExecute = () =>
                {
                    rig.Sink.PhysicallyDown.Add(0xA2);
                    rig.Clock.After(100, () => rig.Sink.PhysicallyDown.Remove(0xA2));
                };
                rig.Type("/shrug");
                Assert.False(rig.Sink.Log.Contains("up LCONTROL"));
                Assert.Equal(5, rig.Backspaces);
            }
        }

        [Test]
        public static void Type_ShiftIsNotWaitedForOrReleased()
        {
            using (var rig = Inline())
            {
                rig.Sink.PhysicallyDown.Add(0xA0);
                rig.Type("/shrug");
                Assert.False(rig.Sink.Log.Contains("up LSHIFT"));
                Assert.Equal(0, rig.Clock.Sleeps.Count, "typing Unicode ignores Shift, so there is nothing to wait for");
            }
        }

        [Test]
        public static void Type_NothingHeldMeansNoWaitingAndNoExtraKeys()
        {
            using (var rig = Inline())
            {
                rig.Type("/shrug");
                Assert.Equal(0, rig.Clock.Sleeps.Count);
                Assert.False(rig.Sink.Log.Any(l => l.StartsWith("up ") && l != "up BACK"));
            }
        }

        // ---- failures put the trigger back ----

        [Test]
        public static void Type_FailureAfterTheBackspacesRetypesTheTriggerAndTellsTheUser()
        {
            using (var rig = Inline())
            {
                bool failOnce = true;
                rig.Sink.FailWhen = line => { if (failOnce && line.StartsWith("text ¯")) { failOnce = false; return true; } return false; };
                var failures = new List<string>();
                rig.Engine.ExpansionFailed += failures.Add;
                rig.Type("well /shrug");
                Assert.Equal("well /shrug", rig.Text);
                Assert.Equal(1, failures.Count);
                Assert.False(failures[0].Contains("/shrug"));
                Assert.Contains(failures[0], "put back");
            }
        }

        [Test]
        public static void Type_FailureWithADelimiterRetypesTheTriggerAndTheDelimiter()
        {
            using (var rig = Inline(PhraseRig.Make("addr", "1 Main Street", ExpandWhen.AfterDelimiter)))
            {
                bool failOnce = true;
                rig.Sink.FailWhen = line => { if (failOnce && line.StartsWith("text 1 Main")) { failOnce = false; return true; } return false; };
                int failures = 0;
                rig.Engine.ExpansionFailed += m => failures++;
                rig.Type("addr ");
                Assert.Equal("addr ", rig.Text);
                Assert.Equal(1, failures);
            }
        }

        [Test]
        public static void Type_FailureHalfWayThroughTheBackspacesRestoresOnlyWhatWasErased()
        {
            using (var rig = Inline())
            {
                int downs = 0;
                rig.Sink.FailWhen = line => line == "down BACK" && ++downs == 3;
                int failures = 0;
                rig.Engine.ExpansionFailed += m => failures++;
                rig.Type("/shrug");
                Assert.Equal("/shrug", rig.Text, "two characters were erased and typed back");
                Assert.Equal(1, failures);
            }
        }

        [Test]
        public static void Type_FailureThatCannotBeRepairedSaysSo()
        {
            using (var rig = Inline())
            {
                rig.Sink.FailWhen = line => line.StartsWith("text ");
                var failures = new List<string>();
                rig.Engine.ExpansionFailed += failures.Add;
                rig.Type("/shrug");
                Assert.Equal(1, failures.Count);
                Assert.Contains(failures[0], "may be missing");
                Assert.False(rig.Engine.IsExpanding);
            }
        }

        [Test]
        public static void Type_FailureTypingTheDelimiterAfterTheTextIsReported()
        {
            using (var rig = Inline(PhraseRig.Make("addr", "1 Main Street", ExpandWhen.AfterDelimiter)))
            {
                rig.Sink.FailWhen = line => line == "text  ";
                var failures = new List<string>();
                int expanded = 0;
                rig.Engine.ExpansionFailed += failures.Add;
                rig.Engine.Expanded += p => expanded++;
                rig.Type("addr ");
                Assert.Equal("1 Main Street", rig.Text, "the phrase itself went in");
                Assert.Equal(1, failures.Count);
                Assert.Equal(0, expanded);
            }
        }

        [Test]
        public static void SuccessfulExpansionsRaiseNoFailure()
        {
            using (var rig = Inline())
            {
                int failures = 0;
                rig.Engine.ExpansionFailed += m => failures++;
                rig.Type("/shrug");
                Assert.Equal(0, failures);
            }
        }

        [Test]
        public static void AFailingFailureHandlerDoesNotBreakTheEngine()
        {
            using (var rig = Inline())
            {
                bool failOnce = true;
                rig.Sink.FailWhen = line => { if (failOnce && line.StartsWith("text ¯")) { failOnce = false; return true; } return false; };
                rig.Engine.ExpansionFailed += m => { throw new InvalidOperationException("handler bug"); };
                rig.Type("/shrug");
                Assert.False(rig.Engine.IsExpanding);
                rig.Type(" x");
                Assert.Contains(rig.Text, "x");
            }
        }

        [Test]
        public static void FailuresOnTheWorkerThreadAreReportedToo()
        {
            using (var rig = new PhraseRig(false, PhraseRig.Make("/addr", "1 Main Street", method: InsertMethod.Paste)))
            {
                rig.Clip.FailWrites = true;
                string message = null;
                rig.Engine.ExpansionFailed += m => message = m;
                rig.Type("/addr");
                Assert.True(rig.Engine.WaitIdle(3000), "the worker finished");
                Assert.NotNull(message);
                Assert.Equal("/addr", rig.Text);
            }
        }
    }
}
