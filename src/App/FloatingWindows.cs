using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MacroKit.Core;
using MacroKit.Win32;

namespace MacroKit.App
{
    /// <summary>
    /// A small always-on-top tool window that never takes focus: no taskbar button, not in Alt+Tab, and clicking it does not
    /// activate it. Nothing in these windows may call Activate(), Focus() or give a control keyboard focus.
    /// </summary>
    public abstract class FloatingWindow : Window
    {
        protected FloatingWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Theme.Style(this);
            SourceInitialized += (s, e) =>
            {
                WindowStyles.ApplyNoActivate((HwndSource)PresentationSource.FromVisual(this));
                Theme.RoundCorners(this);
            };
        }

        /// <summary>A button that never takes keyboard focus.</summary>
        protected static Button NoFocus(Button b)
        {
            b.Focusable = false;
            b.IsTabStop = false;
            return b;
        }
    }

    /// <summary>
    /// The small status bar at the bottom-right of the screen while something runs: what is happening, how far along it is, and a
    /// Stop button that works without stealing focus from the program the macro is running in.
    /// </summary>
    public sealed class RunPill : FloatingWindow
    {
        readonly AppController c;
        readonly Ellipse dot = new Ellipse { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock title = Ui.Text("", "Text", true, 13);
        readonly TextBlock detail = Ui.Text("", "Muted", false, 12);
        readonly TextBlock warning = Ui.Text("", "Warn", false, 12, true);
        readonly StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        readonly DispatcherTimer tick;
        readonly DispatcherTimer hideTimer;
        Rect anchorPixels = Rect.Empty;
        bool anchored;

        public RunPill(AppController controller)
        {
            c = controller;
            Title = "Macro Kit status";
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            warning.Visibility = Visibility.Collapsed;
            var text = Ui.Stack(1, title, detail, warning);
            text.VerticalAlignment = VerticalAlignment.Center;
            text.MinWidth = 190;
            text.MaxWidth = 380;
            dot.Margin = new Thickness(0, 0, 11, 0);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 12, 10) };
            row.Children.Add(dot);
            row.Children.Add(text);
            row.Children.Add(buttons);
            var frame = new Border { Child = row, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) };
            Ui.Ref(frame, Border.BackgroundProperty, "Card");
            Ui.Ref(frame, Border.BorderBrushProperty, "Border");
            Content = frame;

            tick = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
            tick.Tick += (s, e) => { if (c.Activity.Kind != ActivityKind.None) Render(c.Activity, null, false); };
            hideTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(8) };
            hideTimer.Tick += (s, e) => { hideTimer.Stop(); Hide(); };
            SizeChanged += (s, e) => Reposition();
        }

        /// <summary>Shows, updates or hides the pill to match the controller's current activity.</summary>
        public void Present()
        {
            hideTimer.Stop();
            if (c.Activity.Kind != ActivityKind.None)
            {
                anchored = false;
                Render(c.Activity, null, false);
                tick.Start();
                Reveal();
            }
            else
            {
                tick.Stop();
                if (c.LastOutcomeIsError && c.LastOutcome.Length > 0)
                {
                    Render(new ActivityInfo(), c.LastOutcome, true);
                    Reveal();
                    hideTimer.Interval = TimeSpan.FromSeconds(8);
                    hideTimer.Start();
                }
                else Hide();
            }
        }

        /// <summary>
        /// Shows a short message for about five seconds when nothing is running (a hotkey or a Deck button that could not start
        /// anything, a phrase that could not be inserted). While something runs the pill already shows that, so this does nothing.
        /// </summary>
        public void Flash(string heading, string message, string dotKey)
        {
            if (c.Activity.Kind != ActivityKind.None) return;
            hideTimer.Stop();
            anchored = false;
            Render(new ActivityInfo(), message, dotKey == "Bad", heading, dotKey);
            Reveal();
            hideTimer.Interval = TimeSpan.FromSeconds(5);
            hideTimer.Start();
        }

        /// <summary>Redraws the text and buttons for the current activity (called as progress arrives).</summary>
        public void Refresh()
        {
            if (IsVisible && c.Activity.Kind != ActivityKind.None) Render(c.Activity, null, false);
        }

        void Reveal()
        {
            if (!IsVisible) Show();
            UpdateLayout();
            Reposition();
        }

        void Reposition()
        {
            if (!IsVisible) return;
            if (!anchored)
            {
                anchorPixels = WorkAreaOfTarget();
                anchored = true;
            }
            Rect self;
            if (anchorPixels.IsEmpty || !NativeUi.TryGetRect(this, out self)) return;
            const int margin = 18;
            NativeUi.MoveTo(this, (int)(anchorPixels.Right - self.Width - margin), (int)(anchorPixels.Bottom - self.Height - margin));
        }

        Rect WorkAreaOfTarget()
        {
            try
            {
                IntPtr target = c.Windows.LastExternalForeground;
                System.Windows.Forms.Screen screen = target != IntPtr.Zero ? System.Windows.Forms.Screen.FromHandle(target) : System.Windows.Forms.Screen.PrimaryScreen;
                System.Drawing.Rectangle area = screen.WorkingArea;
                return new Rect(area.X, area.Y, area.Width, area.Height);
            }
            catch (Exception) { return Rect.Empty; }
        }

        /// <summary>Fills the pill from explicit values (also used by the self test to show every state).</summary>
        public void Render(ActivityInfo a, string outcome, bool outcomeIsError, string heading = null, string dotKey = null)
        {
            buttons.Children.Clear();
            string stopKeys = c.Settings.StopHotkey;
            bool warn = !string.IsNullOrEmpty(a.Warning) && string.IsNullOrEmpty(outcome);
            warning.Text = warn ? a.Warning : "";
            warning.Visibility = warn ? Visibility.Visible : Visibility.Collapsed;
            if (!string.IsNullOrEmpty(outcome))
            {
                Ui.Ref(dot, Shape.FillProperty, dotKey ?? (outcomeIsError ? "Bad" : "Good"));
                title.Text = heading ?? (outcomeIsError ? "Stopped" : "Done");
                detail.TextWrapping = TextWrapping.Wrap;
                detail.TextTrimming = TextTrimming.None;
                detail.Text = outcome;
                detail.ToolTip = outcome;
                buttons.Children.Add(NoFocus(Ui.Button("Dismiss", () => { hideTimer.Stop(); Hide(); }, "Small")));
                return;
            }
            detail.ToolTip = null;
            detail.TextWrapping = TextWrapping.NoWrap;
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            switch (a.Kind)
            {
                case ActivityKind.Recording:
                    Ui.Ref(dot, Shape.FillProperty, "Rec");
                    title.Text = "Recording…  " + Clock((DateTime.Now - a.Started).TotalSeconds);
                    detail.Text = "Press " + stopKeys + " or Stop to finish";
                    buttons.Children.Add(StopButton(stopKeys));
                    break;
                case ActivityKind.Playing:
                    Ui.Ref(dot, Shape.FillProperty, "Accent");
                    title.Text = "Playing “" + a.Name + "”";
                    detail.Text = SpeedText(a.Speed) + "  ·  " + PassText(a) + (a.DurationMs > 0 ? "  ·  " + AppController.FormatDuration(a.ElapsedMs) + " of " + AppController.FormatDuration(a.DurationMs) : "") + EscHint();
                    buttons.Children.Add(StopButton(stopKeys));
                    break;
                case ActivityKind.Macro:
                    Ui.Ref(dot, Shape.FillProperty, "Accent");
                    title.Text = "Running “" + a.Name + "”";
                    detail.Text = PassText(a) + (a.StepCount > 0 && a.StepIndex > 0 ? "  ·  step " + a.StepIndex + " of " + a.StepCount : "") + (string.IsNullOrEmpty(a.Message) ? "" : "  ·  " + a.Message) + EscHint();
                    buttons.Children.Add(StopButton(stopKeys));
                    break;
                case ActivityKind.Picking:
                    Ui.Ref(dot, Shape.FillProperty, "Warn");
                    title.Text = "Pick a window";
                    detail.TextWrapping = TextWrapping.Wrap;
                    detail.TextTrimming = TextTrimming.None;
                    detail.Text = "Switch to the window you want. For the window that is already in front" + (a.Candidate == null ? "" : " (“" + Shorten(a.Candidate.Title, 28) + "”)") + ", press Use this window.";
                    buttons.Children.Add(NoFocus(Ui.Button("Use this window", () => c.AcceptPickCandidate(), "Primary")));
                    var cancel = NoFocus(Ui.Button("Cancel", () => c.Stop(), "Ghost"));
                    cancel.Margin = new Thickness(6, 0, 0, 0);
                    buttons.Children.Add(cancel);
                    break;
            }
        }

        string EscHint() { return c.Settings.EscStops ? "  ·  Esc stops" : ""; }

        Button StopButton(string keys)
        {
            var icon = Ui.Shape("stop", 10, "Bad");
            var label = new TextBlock { Text = "Stop", Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var hint = Ui.Text(keys, "Muted", false, 11);
            hint.Margin = new Thickness(10, 0, 0, 0);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(icon);
            content.Children.Add(label);
            content.Children.Add(hint);
            var b = NoFocus(Ui.Button(content, () => c.Stop(), "Danger", "Stop (" + keys + ")"));
            b.Padding = new Thickness(12, 6, 12, 6);
            return b;
        }

        static string PassText(ActivityInfo a)
        {
            if (a.TotalPasses == 0) return a.Pass <= 1 ? "running continuously" : "pass " + a.Pass + " (continuous)";
            if (a.TotalPasses == 1) return "single pass";
            return "pass " + Math.Max(1, a.Pass) + " of " + a.TotalPasses;
        }

        static string SpeedText(double speed) { return speed.ToString(speed < 10 ? "0.0#" : "0", System.Globalization.CultureInfo.InvariantCulture) + "×"; }

        static string Clock(double seconds)
        {
            int total = (int)seconds;
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        static string Shorten(string text, int max) { return text.Length <= max ? text : text.Substring(0, max - 1) + "…"; }
    }

    /// <summary>
    /// The Deck: a small always-on-top panel of buttons, like the virtual Stream Deck. Clicking a button never takes focus, so a macro
    /// started here runs in the window that already has it.
    /// </summary>
    public sealed class DeckWindow : FloatingWindow
    {
        readonly AppController c;
        readonly Border frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) };
        string signature = "";
        bool placed;

        public DeckWindow(AppController controller)
        {
            c = controller;
            Title = "Macro Kit Deck";
            Ui.Ref(frame, Border.BackgroundProperty, "Card");
            Ui.Ref(frame, Border.BorderBrushProperty, "Border");
            Content = frame;
            LocationChanged += (s, e) => RememberPlace();
            Rebuild();
        }

        /// <summary>Rebuilds the buttons when something that shows on the Deck changed.</summary>
        public void Update()
        {
            string now = Signature();
            if (now == signature) return;
            Rebuild();
        }

        string Signature()
        {
            return string.Join("|", c.Macros.Where(m => m.ShowOnDeck).Select(m => m.Id + ":" + m.Name)) + "#" + c.Settings.DeckColumns + "#" + c.SelectedRecordingName()
                + "#" + (int)c.Activity.Kind + c.Activity.MacroId;
        }

        void Rebuild()
        {
            signature = Signature();
            var strip = new DockPanel { Background = Brushes.Transparent, Height = 26 };
            var close = NoFocus(Ui.GlyphButton(Ui.GlyphClose, "Hide the Deck", () => c.ToggleDeck(), "Link"));
            close.Padding = new Thickness(6, 2, 6, 2);
            DockPanel.SetDock(close, Dock.Right);
            strip.Children.Add(close);
            var caption = Ui.Text("Macro Kit Deck", "Muted", true, 11);
            caption.Margin = new Thickness(12, 0, 0, 0);
            strip.Children.Add(caption);
            strip.ToolTip = "Drag to move the Deck.";
            strip.MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch (InvalidOperationException) { } };

            var grid = new UniformGrid { Columns = Math.Max(1, Math.Min(8, c.Settings.DeckColumns)), Margin = new Thickness(8, 2, 8, 8) };
            foreach (MacroDef m in c.Macros.Where(x => x.ShowOnDeck))
            {
                MacroDef macro = m;
                bool running = c.Activity.Kind == ActivityKind.Macro && c.Activity.MacroId == macro.Id;
                var b = DeckButton(macro.Name.Length == 0 ? "(unnamed)" : macro.Name, running, () =>
                {
                    if (running) c.Stop(); else c.StartMacro(macro, RunOrigin.Deck);
                });
                b.ToolTip = running ? "Click to stop “" + macro.Name + "”." : "Run “" + macro.Name + "” in the window that has focus.";
                grid.Children.Add(b);
            }
            string recording = c.SelectedRecordingName();
            bool playing = c.Activity.Kind == ActivityKind.Playing;
            var play = DeckButton("▶  " + (recording.Length == 0 ? "No recording" : recording), playing, () => { if (playing) c.Stop(); else c.StartPlayback(RunOrigin.Deck); });
            play.ToolTip = "Play the recording selected on the Recorder tab.";
            grid.Children.Add(play);
            var stop = DeckButton("■  Stop", false, () => c.Stop());
            stop.ToolTip = "Stop anything that is running.";
            stop.IsEnabled = c.IsBusy;
            grid.Children.Add(stop);

            frame.Child = new StackPanel { Children = { strip, grid } };
        }

        Button DeckButton(string text, bool active, Action click)
        {
            var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, MaxHeight = 36 };
            var b = NoFocus(Ui.Button(label, click, active ? "Primary" : "Chip"));
            b.Width = 124;
            b.Height = 46;
            b.Margin = new Thickness(3);
            b.Padding = new Thickness(8, 4, 8, 4);
            b.FontSize = 12.5;
            return b;
        }

        /// <summary>Puts the Deck where it was last time (or near the top-right of the main screen the first time). Call before showing.</summary>
        public void Place()
        {
            if (placed) return;
            placed = true;
            UpdateLayout();
            double left = c.Settings.DeckLeft, top = c.Settings.DeckTop;
            // A saved position on a monitor that is gone would hide the Deck; fall back to the default spot.
            if (!double.IsNaN(left) && !double.IsNaN(top) && NativeUi.IsOnScreen(left, top, 200, 100))
            {
                NativeUi.MoveTo(this, (int)left, (int)top);
                return;
            }
            System.Drawing.Rectangle area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            NativeUi.MoveTo(this, area.Right - 420, area.Top + 80);
        }

        void RememberPlace()
        {
            if (!placed || !IsVisible) return;
            Rect pixels;
            if (!NativeUi.TryGetRect(this, out pixels)) return;
            if (Math.Abs(pixels.X - c.Settings.DeckLeft) < 1 && Math.Abs(pixels.Y - c.Settings.DeckTop) < 1) return;
            c.Settings.DeckLeft = pixels.X;
            c.Settings.DeckTop = pixels.Y;
            c.MarkDirty(SaveParts.Settings);
        }
    }
}
