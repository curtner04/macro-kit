using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MacroKit.Core;

namespace MacroKit.App
{
    /// <summary>
    /// Settings, Check input: proves on this PC that Windows lets Macro Kit send keys (it types into its own box) and watch
    /// typing (one temporary phrase expands in the second box). Nothing is typed anywhere else, and nothing is saved.
    /// </summary>
    public sealed class InputCheckWindow : Window
    {
        const string Expected = "Macro Kit ✓ ä";
        const string Trigger = ";mkcheck";
        const string Expansion = "Phrases work ✓";

        readonly AppController c;
        readonly TextBox typingBox = new TextBox { Height = 30, IsReadOnly = false };
        readonly TextBox phraseBox = new TextBox { Height = 30 };
        readonly TextBlock typingResult = Ui.Text("", "Muted", false, 12, true);
        readonly TextBlock phraseResult = Ui.Text("", "Muted", false, 12, true);
        readonly DispatcherTimer phraseTimer;
        readonly Button typeButton;
        volatile bool closed;   // the check thread must not type once this window is gone

        public InputCheckWindow(Window owner, AppController controller)
        {
            c = controller;
            Title = "Check input";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            if (owner != null && owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Theme.Style(this);

            typeButton = Ui.Button("Type into the box", RunTypingCheck, "Primary");
            phraseBox.TextChanged += (s, e) => CheckPhrase();
            phraseBox.GotKeyboardFocus += (s, e) => StartPhraseCheck();
            phraseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
            phraseTimer.Tick += (s, e) => { StopPhraseCheck(); if (phraseResult.Text.Length == 0 || !phraseResult.Text.StartsWith("Pass")) Show(phraseResult, "Click the box to try again.", false); };

            bool can = c.CanCheckInput;
            var unavailable = Ui.Note("Input is off in this session (or the keyboard hook did not start), so there is nothing to check.", "Warn");
            unavailable.Visibility = can ? Visibility.Collapsed : Visibility.Visible;
            typeButton.IsEnabled = can;
            phraseBox.IsEnabled = can;

            Content = Ui.Stack(10,
                Ui.Heading("Check input on this PC"),
                Ui.Para("Two quick checks. Macro Kit types only into the boxes below, and nothing is saved."),
                unavailable,
                Ui.Caption("1. Sending keys"),
                Ui.Para("Press the button. Macro Kit types a short line into the box with real key presses, the way a macro does."),
                Ui.Row(8, typeButton),
                typingBox,
                typingResult,
                Ui.Caption("2. Phrases"),
                Ui.Para("Click the box and type " + Trigger + " followed by a space. Macro Kit should replace it with “" + Expansion + "”."),
                phraseBox,
                phraseResult,
                Ui.Row(8, Ui.Button("Close", Close, null)));
            ((FrameworkElement)Content).Margin = new Thickness(24, 20, 24, 20);
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            phraseBox.PreviewMouseDown += (s, e) => StartPhraseCheck();   // a click in an already-focused box starts a new try after a timeout
            Closed += (s, e) => { closed = true; StopPhraseCheck(); };
        }

        void RunTypingCheck()
        {
            typeButton.IsEnabled = false;
            typingBox.Text = "";
            typingBox.Focus();
            Keyboard.Focus(typingBox);
            Show(typingResult, "Typing…", false);
            var thread = new Thread(() =>
            {
                string error = null;
                try
                {
                    Thread.Sleep(250);   // let the box take focus
                    // Real key presses go to whoever has the keyboard, so only type while this window's box has it.
                    bool ready = !closed && (bool)Dispatcher.Invoke(new Func<bool>(() => IsActive && typingBox.IsKeyboardFocusWithin));
                    if (!ready) error = "";
                    else
                    {
                        c.TypeForCheck(input =>
                        {
                            input.KeyCombo(new ushort[] { 0x10, 0x4D });     // Shift+M: a shortcut
                            if (closed) return;
                            input.TypeText("acro Kit ✓ äö"); // Unicode text
                            if (closed) return;
                            input.KeyCombo(new ushort[] { 0x08 });          // Backspace removes the ö
                        });
                        Thread.Sleep(400);
                    }
                }
                catch (Exception ex) { error = ex.Message; }
                Dispatcher.BeginInvoke(new Action(() => FinishTypingCheck(error)));
            }) { IsBackground = true, Name = "Input check" };
            thread.Start();
        }

        void FinishTypingCheck(string error)
        {
            if (closed) return;
            typeButton.IsEnabled = true;
            if (error == "") { Show(typingResult, "Keep this window in front and press the button again.", false); return; }
            if (error != null) { Show(typingResult, "Fail: sending keys stopped with an error: " + error, false); return; }
            if (typingBox.Text == Expected)
            {
                Show(typingResult, "Pass: shortcuts, Unicode text and Backspace all arrived.", true);
                Log.Info("Input check: typing passed.");
                return;
            }
            Show(typingResult, "Fail: expected “" + Expected + "” but the box holds “" + typingBox.Text + "”. If another window took focus, try again and keep this window in front. If it still fails, security software may be blocking simulated input.", false);
            Log.Warn("Input check: typing gave a different text (" + typingBox.Text.Length + " characters).");
        }

        void StartPhraseCheck()
        {
            if (!c.CanCheckInput || phraseTimer.IsEnabled) return;
            if (!c.Settings.ExpansionEnabled)
            {
                Show(phraseResult, "Phrases are switched off. Turn on “Expand phrases as I type” on the Phrases tab, then try again.", false);
                return;
            }
            phraseBox.Text = "";   // a leftover expansion from an earlier try must not count as a pass
            c.BeginPhraseCheck(new PhraseDef { Trigger = Trigger, Replacement = Expansion });
            phraseTimer.Start();
            Show(phraseResult, "Waiting for you to type " + Trigger + " and a space…", false);
        }

        void StopPhraseCheck()
        {
            phraseTimer.Stop();
            c.EndPhraseCheck();
        }

        void CheckPhrase()
        {
            if (!phraseTimer.IsEnabled) return;
            if (phraseBox.Text.Contains(Expansion))
            {
                StopPhraseCheck();
                Show(phraseResult, "Pass: Macro Kit saw your typing and replaced the phrase.", true);
                Log.Info("Input check: phrase passed.");
            }
        }

        static void Show(TextBlock block, string text, bool good)
        {
            block.Text = text;
            Ui.Ref(block, TextBlock.ForegroundProperty, good ? "Good" : text.StartsWith("Fail", StringComparison.Ordinal) ? "Bad" : "Muted");
        }
    }
}
