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
using Rec = MacroKit.Core.Recording;

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
                // From the handle: the status popup creates its handle before it shows (to size and place it first), and then
                // PresentationSource.FromVisual is still null.
                WindowStyles.ApplyNoActivate(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle));
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
    /// Stop button that works without stealing focus from the program the macro is running in. Dragging it moves it there for good
    /// (Settings can show it while nothing runs, and reset it to the corner).
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
        bool dragging;
        // Shown from Settings so it can be dragged into place while nothing runs; ends with Done or when something starts.
        bool moving;
        Window movingNear;

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
            // Buttons handle their own clicks, so only the background and the text start a drag.
            frame.MouseLeftButtonDown += (s, e) => DragToNewPlace();
            Content = frame;

            tick = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
            tick.Tick += (s, e) => { if (c.Activity.Kind != ActivityKind.None) Render(c.Activity, null, false); };
            hideTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(8) };
            hideTimer.Tick += (s, e) => { hideTimer.Stop(); Hide(); };
            // 1.13.2: the popup sizes itself (Fit), so a new size and its place arrive in one move. With SizeToContent, Windows first
            // grew it from its left edge and only then was it moved back, so for a moment it reached past its corner, onto the
            // next screen when it sat near one. A DPI change (moving to another screen) still resizes it, so it is fitted again.
            SizeToContent = SizeToContent.Manual;
            SizeChanged += (s, e) => { if (!fitting) Reposition(); };
        }

        bool fitting;

        /// <summary>The choices for Settings, Status popup, Fixed corner, and the values saved for them.</summary>
        public static readonly string[] AnchorLabels = { "Automatic", "Top left", "Top right", "Bottom left", "Bottom right" };
        public static readonly string[] AnchorValues = { "", "TopLeft", "TopRight", "BottomLeft", "BottomRight" };

        /// <summary>Settings changed the fixed corner: place the popup again if it is showing.</summary>
        public void PlaceChanged() { Reposition(); }

        /// <summary>Shows, updates or hides the pill to match the controller's current activity.</summary>
        public void Present()
        {
            hideTimer.Stop();
            if (c.Activity.Kind == ActivityKind.Recording)
            {
                // The recorder bar shows a recording; the popup stays out of the way.
                moving = false;
                tick.Stop();
                Hide();
            }
            else if (c.Activity.Kind != ActivityKind.None)
            {
                moving = false;
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
                    // The run often went on in another program, so the reason it stopped stays up long enough to be seen.
                    Render(new ActivityInfo(), c.LastOutcome, true, c.LastOutcomeHeading);
                    Reveal();
                    hideTimer.Interval = TimeSpan.FromSeconds(30);
                    hideTimer.Start();
                }
                else if (c.LastOutcome.Length > 0 && !moving)
                {
                    // A short confirmation that it finished: the main window comes back behind the window in front (or not at all),
                    // so the status line alone is easy to miss.
                    Render(new ActivityInfo(), c.LastOutcome, false, c.LastOutcomeHeading);
                    Reveal();
                    hideTimer.Interval = TimeSpan.FromSeconds(3);
                    hideTimer.Start();
                }
                else if (moving) RenderMoving();
                else Hide();
            }
        }

        /// <summary>
        /// Shows the popup with nothing running so it can be dragged into place (Settings → Show popup). Without a dragged place it
        /// appears in the bottom-right corner of the screen <paramref name="near"/> is on. Does nothing while something runs, because
        /// the popup is already on screen then and can be dragged as it is.
        /// </summary>
        public void ShowForMoving(Window near)
        {
            if (c.Activity.Kind != ActivityKind.None) return;
            hideTimer.Stop();
            moving = true;
            movingNear = near;
            anchored = false;
            RenderMoving();
            Reveal();
        }

        /// <summary>True while the popup is shown from Settings for moving.</summary>
        public bool IsShownForMoving { get { return moving && IsVisible; } }

        /// <summary>Forgets the dragged place, so the popup goes back to the bottom-right corner of the screen being worked on.</summary>
        public void ResetPlace()
        {
            bool had = !double.IsNaN(c.Settings.PillLeft);
            c.Settings.PillLeft = c.Settings.PillTop = c.Settings.PillRight = c.Settings.PillBottom = double.NaN;
            if (had) c.MarkDirty(SaveParts.Settings);
            anchored = false;
            Reposition();
        }

        /// <summary>Hides the popup shown from Settings (its Done button).</summary>
        public void EndMoving()
        {
            moving = false;
            movingNear = null;
            Hide();
        }

        void RenderMoving()
        {
            Render(new ActivityInfo(), null, false);
            Ui.Ref(dot, Shape.FillProperty, "Accent");
            title.Text = "Status popup";
            detail.Text = "Drag it to where it should show during runs";
            buttons.Children.Add(NoFocus(Ui.Button("Done", EndMoving, "Primary")));
            Reposition();
        }

        void DragToNewPlace()
        {
            Rect before, after;
            if (!NativeUi.TryGetRect(this, out before)) return;
            dragging = true;
            try { DragMove(); }
            catch (InvalidOperationException) { }
            finally { dragging = false; }
            // A plain click (no real move) keeps the place it had, so clicking the text never pins the default corner.
            if (!NativeUi.TryGetRect(this, out after) || (Math.Abs(after.X - before.X) < 3 && Math.Abs(after.Y - before.Y) < 3))
            {
                Reposition();
                return;
            }
            c.Settings.PillLeft = after.Left;
            c.Settings.PillTop = after.Top;
            c.Settings.PillRight = after.Right;
            c.Settings.PillBottom = after.Bottom;
            c.MarkDirty(SaveParts.Settings);
            Reposition();
        }

        /// <summary>
        /// Shows a short message for about five seconds (or <paramref name="seconds"/>) when nothing is running (a hotkey or a Deck
        /// button that could not start anything, a phrase that could not be inserted). While something runs the pill already shows
        /// that, so this does nothing and returns false.
        /// </summary>
        public bool Flash(string heading, string message, string dotKey, int seconds = 5)
        {
            if (c.Activity.Kind != ActivityKind.None) return false;
            hideTimer.Stop();
            moving = false;
            anchored = false;
            Render(new ActivityInfo(), message, dotKey == "Bad", heading, dotKey);
            Reveal();
            hideTimer.Interval = TimeSpan.FromSeconds(seconds);
            hideTimer.Start();
            return true;
        }

        /// <summary>Redraws the text and buttons for the current activity (called as progress arrives).</summary>
        public void Refresh()
        {
            if (IsVisible && c.Activity.Kind != ActivityKind.None) Render(c.Activity, null, false);
        }

        void Reveal()
        {
            // Sized and placed before it shows, so it never flashes where it was last time or at its old size.
            if (!IsVisible) { Fit(); Show(); }
            Reposition();
        }

        void Reposition() { if (IsVisible) Fit(); }

        /// <summary>
        /// Gives the popup the size its content needs and puts it in its place, in one move. While the user drags it only the size
        /// follows the text (moving it then would fight the drag).
        /// </summary>
        void Fit()
        {
            if (fitting) return;
            var content = Content as UIElement;
            if (content == null) return;
            fitting = true;
            try
            {
                new WindowInteropHelper(this).EnsureHandle();
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                if (scale <= 0) scale = 1;
                // Layout first: new text marks only its own TextBlock for measuring, so measuring the frame alone would give back
                // the size it had before.
                UpdateLayout();
                content.InvalidateMeasure();
                content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = new Size(Math.Ceiling(content.DesiredSize.Width * scale), Math.Ceiling(content.DesiredSize.Height * scale));
                if (size.Width < 1 || size.Height < 1) return;
                Rect self;
                bool known = NativeUi.TryGetRect(this, out self);
                Point at;
                if (dragging || !TargetPlace(size, out at)) at = known ? self.TopLeft : new Point(0, 0);
                if (known && Math.Abs(self.X - at.X) < 1 && Math.Abs(self.Y - at.Y) < 1 && Math.Abs(self.Width - size.Width) < 1 && Math.Abs(self.Height - size.Height) < 1) return;
                NativeUi.SetBounds(this, (int)Math.Round(at.X), (int)Math.Round(at.Y), (int)size.Width, (int)size.Height);
            }
            finally { fitting = false; }
        }

        /// <summary>Where a popup of <paramref name="size"/> (physical pixels) goes: its dragged place, or the corner of the screen being worked on.</summary>
        bool TargetPlace(Size size, out Point at)
        {
            at = new Point();
            Rect saved;
            if (TrySavedPlace(out saved)) { at = PlaceFromSaved(saved, size, c.Settings.PillAnchor); return true; }
            if (!anchored)
            {
                anchorPixels = WorkAreaOfTarget();
                anchored = true;
            }
            if (anchorPixels.IsEmpty) return false;
            const int margin = 18;
            at = new Point(anchorPixels.Right - size.Width - margin, anchorPixels.Bottom - size.Height - margin);
            return true;
        }

        /// <summary>The dragged place, when there is one and it is still on a connected screen.</summary>
        bool TrySavedPlace(out Rect saved)
        {
            saved = Rect.Empty;
            AppSettings s = c.Settings;
            if (double.IsNaN(s.PillLeft) || double.IsNaN(s.PillTop) || double.IsNaN(s.PillRight) || double.IsNaN(s.PillBottom)) return false;
            if (s.PillRight <= s.PillLeft || s.PillBottom <= s.PillTop) return false;
            saved = new Rect(s.PillLeft, s.PillTop, s.PillRight - s.PillLeft, s.PillBottom - s.PillTop);
            return NativeUi.IsOnScreen(saved.X, saved.Y, saved.Width, saved.Height);
        }

        /// <summary>
        /// Where the popup goes for a dragged place. Its size changes with the text, so one corner stays put: the one chosen in
        /// Settings (<paramref name="anchor"/>), or else the corner nearest the screen's edges where it was dropped (dropped on the
        /// right half: the right edge stays put and it grows to the left). It always stays on that screen.
        /// </summary>
        static Point PlaceFromSaved(Rect saved, Size size, string anchor)
        {
            Rect area;
            try
            {
                var center = new System.Drawing.Point((int)(saved.X + saved.Width / 2), (int)(saved.Y + saved.Height / 2));
                System.Drawing.Rectangle work = System.Windows.Forms.Screen.FromPoint(center).WorkingArea;
                area = new Rect(work.X, work.Y, work.Width, work.Height);
            }
            catch (Exception) { return new Point(saved.X, saved.Y); }
            return PlaceIn(saved, size, area, anchor);
        }

        /// <summary>The top-left for a popup of <paramref name="size"/> dropped at <paramref name="saved"/> on a screen whose work area is <paramref name="area"/>.</summary>
        public static Point PlaceIn(Rect saved, Size size, Rect area, string anchor)
        {
            bool right = saved.X + saved.Width / 2 > area.X + area.Width / 2;
            bool bottom = saved.Y + saved.Height / 2 > area.Y + area.Height / 2;
            switch (anchor ?? "")
            {
                case "TopLeft": right = false; bottom = false; break;
                case "TopRight": right = true; bottom = false; break;
                case "BottomLeft": right = false; bottom = true; break;
                case "BottomRight": right = true; bottom = true; break;
            }
            double x = right ? saved.Right - size.Width : saved.Left;
            double y = bottom ? saved.Bottom - size.Height : saved.Top;
            x = Math.Max(area.Left, Math.Min(x, area.Right - size.Width));
            y = Math.Max(area.Top, Math.Min(y, area.Bottom - size.Height));
            return new Point(x, y);
        }

        Rect WorkAreaOfTarget()
        {
            try
            {
                System.Windows.Forms.Screen screen;
                IntPtr near = movingNear != null && moving ? new WindowInteropHelper(movingNear).Handle : IntPtr.Zero;
                IntPtr target = near != IntPtr.Zero ? near : c.Windows.LastExternalForeground;
                screen = target != IntPtr.Zero ? System.Windows.Forms.Screen.FromHandle(target) : System.Windows.Forms.Screen.PrimaryScreen;
                System.Drawing.Rectangle area = screen.WorkingArea;
                return new Rect(area.X, area.Y, area.Width, area.Height);
            }
            catch (Exception) { return Rect.Empty; }
        }

        /// <summary>Fills the pill from explicit values (also used by the self test to show every state).</summary>
        public void Render(ActivityInfo a, string outcome, bool outcomeIsError, string heading = null, string dotKey = null)
        {
            RenderContent(a, outcome, outcomeIsError, heading, dotKey);
            Reposition();
        }

        void RenderContent(ActivityInfo a, string outcome, bool outcomeIsError, string heading, string dotKey)
        {
            buttons.Children.Clear();
            // A Stop hotkey that could not be set up (another program has it) is not offered: pressing it would do nothing.
            string stopKeys = c.HotkeyFailed("Stop") ? "" : c.Settings.StopHotkey ?? "";
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
                    detail.Text = stopKeys.Length > 0 ? "Press " + stopKeys + " or Stop to finish" : "Press Stop to finish";
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
            if (keys.Length > 0) content.Children.Add(hint);
            var b = NoFocus(Ui.Button(content, () => c.Stop(), "Danger", keys.Length > 0 ? "Stop (" + keys + ")" : "Stop"));
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
    /// The small recorder bar shown while a recording runs, in place of the main window: a blinking dot with the time, and Stop,
    /// Restart and Discard. It appears where the main window's top edge was (or at the top of the screen being worked on), never
    /// takes focus, and clicks on it are never recorded. Dragging it moves it there for the next recordings too.
    /// </summary>
    public sealed class RecorderBar : FloatingWindow
    {
        readonly AppController c;
        readonly Ellipse dot = new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Center };
        readonly TextBlock time = Ui.Text("0:00", "Text", true, 13);
        readonly TextBlock warning = Ui.Text("", "Warn", false, 11, true);
        readonly Button stop, restart, discard;
        readonly DispatcherTimer tick;
        // A longer take is not thrown away by one click: the first click on Discard asks "Sure?" for a few seconds.
        readonly DispatcherTimer discardTimer;
        bool discardArmed;
        const int DiscardAsksAfterSeconds = 10;
        ActivityInfo shown;
        bool dragging;

        public RecorderBar(AppController controller)
        {
            c = controller;
            Title = "Macro Kit recorder";
            Ui.Ref(dot, Shape.FillProperty, "Rec");
            time.HorizontalAlignment = HorizontalAlignment.Center;
            time.Margin = new Thickness(0, 5, 0, 0);
            var status = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 62 };
            status.Children.Add(dot);
            status.Children.Add(time);
            var rec = Ui.Text("REC", "Rec", true, 9.5);
            rec.HorizontalAlignment = HorizontalAlignment.Center;
            status.Children.Add(rec);
            status.ToolTip = "Recording. Drag the bar to move it.";

            stop = Tool(Ui.Shape("stop", 15, "Bad"), "Stop", () => c.StopRecording());
            restart = Tool(Ui.Glyph("", 16, "Text"), "Restart", () => { DisarmDiscard(); c.RestartRecording(); });
            discard = Tool(Ui.Glyph("", 15, "Muted"), "Discard", ClickDiscard);
            restart.Margin = new Thickness(10, 0, 0, 0);   // Stop is kept apart from the two buttons that throw the take away
            discardTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(3) };
            discardTimer.Tick += (s, e) => DisarmDiscard();
            restart.ToolTip = "Throw this take away and start recording again.";
            discard.ToolTip = "Stop and throw this take away.";

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(status);
            row.Children.Add(stop);
            row.Children.Add(restart);
            row.Children.Add(discard);
            warning.MaxWidth = 250;
            warning.Margin = new Thickness(4, 6, 4, 0);
            warning.Visibility = Visibility.Collapsed;
            var body = new StackPanel { Margin = new Thickness(6) };
            body.Children.Add(row);
            body.Children.Add(warning);
            var frame = new Border { Child = body, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) };
            Ui.Ref(frame, Border.BackgroundProperty, "Card");
            Ui.Ref(frame, Border.BorderBrushProperty, "Border");
            frame.MouseLeftButtonDown += (s, e) => DragToNewPlace();
            Content = frame;

            tick = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
            tick.Tick += (s, e) => { if (shown != null) Render(shown); };
        }

        Button Tool(FrameworkElement icon, string label, Action click)
        {
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            var iconHost = new Grid { Height = 18 };
            iconHost.Children.Add(icon);
            var text = Ui.Text(label, "Text", false, 11.5);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.Margin = new Thickness(0, 4, 0, 0);
            var content = new StackPanel();
            content.Children.Add(iconHost);
            content.Children.Add(text);
            var b = NoFocus(Ui.Button(content, click, "Ghost"));
            b.Width = 62;
            b.Height = 52;
            b.Padding = new Thickness(2);
            b.Margin = new Thickness(2, 0, 0, 0);
            return b;
        }

        /// <summary>The Discard button: a take longer than a few seconds needs a second click within three seconds.</summary>
        void ClickDiscard()
        {
            bool longTake = c.Activity.Kind == ActivityKind.Recording && (DateTime.Now - c.Activity.Started).TotalSeconds > DiscardAsksAfterSeconds;
            if (longTake && !discardArmed)
            {
                discardArmed = true;
                ((TextBlock)((StackPanel)discard.Content).Children[1]).Text = "Sure?";
                discardTimer.Start();
                return;
            }
            DisarmDiscard();
            c.DiscardRecording();
        }

        void DisarmDiscard()
        {
            discardTimer.Stop();
            discardArmed = false;
            ((TextBlock)((StackPanel)discard.Content).Children[1]).Text = "Discard";
        }

        /// <summary>Shows the bar while a recording runs and hides it otherwise.</summary>
        public void Present(Rect mainPixels)
        {
            if (c.Activity.Kind != ActivityKind.Recording)
            {
                DisarmDiscard();
                tick.Stop();
                shown = null;
                Hide();
                return;
            }
            bool fresh = !IsVisible;
            Render(c.Activity);
            tick.Start();
            if (fresh)
            {
                Show();
                UpdateLayout();
                Place(mainPixels);
            }
        }

        /// <summary>Fills the bar for an activity (also used by the self test).</summary>
        public void Render(ActivityInfo a)
        {
            shown = a;
            int seconds = Math.Max(0, (int)(DateTime.Now - a.Started).TotalSeconds);
            time.Text = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            // The dot blinks once a second, like a camera's.
            dot.Opacity = (DateTime.Now.Millisecond < 500 || seconds == 0) ? 1 : 0.25;
            string keys = c.HotkeyFailed("Stop") ? "" : c.Settings.StopHotkey;
            stop.ToolTip = string.IsNullOrEmpty(keys) ? "Stop and keep this recording." : "Stop and keep this recording (" + keys + ").";
            bool warn = !string.IsNullOrEmpty(a.Warning);
            warning.Text = warn ? a.Warning : "";
            warning.Visibility = warn ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The dragged place when it is still on a screen; otherwise the top middle of the main window it replaces, or of the screen.</summary>
        void Place(Rect mainPixels)
        {
            Rect self;
            if (!NativeUi.TryGetRect(this, out self)) return;
            AppSettings s = c.Settings;
            if (!double.IsNaN(s.RecorderBarLeft) && !double.IsNaN(s.RecorderBarTop) && NativeUi.IsOnScreen(s.RecorderBarLeft, s.RecorderBarTop, self.Width, self.Height))
            {
                NativeUi.MoveTo(this, (int)s.RecorderBarLeft, (int)s.RecorderBarTop);
                return;
            }
            Rect area = mainPixels;
            if (area.IsEmpty)
            {
                try
                {
                    IntPtr target = c.Windows.LastExternalForeground;
                    var screen = target != IntPtr.Zero ? System.Windows.Forms.Screen.FromHandle(target) : System.Windows.Forms.Screen.PrimaryScreen;
                    System.Drawing.Rectangle work = screen.WorkingArea;
                    area = new Rect(work.X, work.Y, work.Width, work.Height);
                }
                catch (Exception) { return; }
            }
            NativeUi.MoveTo(this, (int)(area.Left + (area.Width - self.Width) / 2), (int)area.Top + 12);
        }

        void DragToNewPlace()
        {
            Rect before, after;
            if (dragging || !NativeUi.TryGetRect(this, out before)) return;
            dragging = true;
            try { DragMove(); }
            catch (InvalidOperationException) { }
            finally { dragging = false; }
            if (!NativeUi.TryGetRect(this, out after) || (Math.Abs(after.X - before.X) < 3 && Math.Abs(after.Y - before.Y) < 3)) return;
            c.Settings.RecorderBarLeft = after.Left;
            c.Settings.RecorderBarTop = after.Top;
            c.MarkPlacementDirty();
        }

        /// <summary>Button labels, left to right (self test).</summary>
        public IList<string> ButtonLabels
        {
            get { return new[] { stop, restart, discard }.Select(b => ((TextBlock)((StackPanel)b.Content).Children[1]).Text).ToList(); }
        }
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
            return string.Join("|", c.Macros.Where(m => m.ShowOnDeck).Select(m => m.Id + ":" + m.Name + ":" + m.DeckColor + ":" + m.DeckIcon + ":" + m.Hotkey + ":" + m.Enabled + ":" + Broken(m)))
                + "#" + string.Join("|", c.DeckRecordings().Select(o => o.Id + ":" + c.RecordingName(o.Id) + ":" + o.Hotkey))
                + "#" + c.Settings.DeckColumns + "#" + c.Settings.DeckCompact + "#" + c.SelectedRecordingName()
                + "#" + (int)c.Activity.Kind + c.Activity.MacroId + c.Activity.RecordingId;
        }

        /// <summary>The colours a Deck button's stripe can have, by the name saved in <see cref="MacroDef.DeckColor"/>.</summary>
        public static readonly string[] Colors = { "Blue", "Green", "Orange", "Red", "Purple" };

        static Color? ColorFor(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "blue": return Color.FromRgb(0x3B, 0x82, 0xF6);
                case "green": return Color.FromRgb(0x22, 0xA0, 0x5A);
                case "orange": return Color.FromRgb(0xF0, 0x8A, 0x24);
                case "red": return Color.FromRgb(0xE0, 0x45, 0x45);
                case "purple": return Color.FromRgb(0x8B, 0x5C, 0xF6);
                default: return null;
            }
        }

        // The first error in the macro, or null when it can run. A macro that needs fixing gets a warning style on the Deck.
        static string Broken(MacroDef m)
        {
            try
            {
                MacroKit.Macros.ParseResult parsed = MacroKit.Macros.MacroParser.Parse(m.Script ?? "", m.NewLineIsEnter);
                if (!parsed.Ok) return MacroKit.Macros.PlainMacro.FirstProblem(m) ?? "Line " + parsed.Errors[0].Line + ": " + parsed.Errors[0].Message;
                return parsed.Steps.Count == 0 ? "It has no steps yet." : null;
            }
            catch (Exception) { return "It can't be read."; }
        }

        /// <summary>Self-test: the Deck buttons' labels in order, and which of them show the warning style.</summary>
        public List<string> ButtonLabels { get; private set; }
        public List<string> WarnedButtons { get; private set; }
        /// <summary>Self-test: the Deck buttons that show a hotkey under their name.</summary>
        public List<string> HintedButtons { get; private set; }
        /// <summary>Self-test: each Deck button's tooltip by its label.</summary>
        public Dictionary<string, string> ButtonTips { get; private set; }

        void Rebuild()
        {
            signature = Signature();
            var strip = new DockPanel { Background = Brushes.Transparent, Height = 26 };
            var close = NoFocus(Ui.GlyphButton(Ui.GlyphClose, "Hide the Deck", () => c.ToggleDeck(), "Link"));
            close.Padding = new Thickness(6, 2, 6, 2);
            DockPanel.SetDock(close, Dock.Right);
            strip.Children.Add(close);
            var compact = NoFocus(Ui.Button(c.Settings.DeckCompact ? "Bigger" : "Compact", () =>
            {
                c.Settings.DeckCompact = !c.Settings.DeckCompact;
                c.MarkDirty(SaveParts.Settings);
                Rebuild();
            }, "Link", c.Settings.DeckCompact ? "Show bigger buttons in rows." : "Show smaller buttons in one row."));
            compact.Padding = new Thickness(6, 2, 6, 2);
            compact.FontSize = 11;
            DockPanel.SetDock(compact, Dock.Right);
            strip.Children.Add(compact);
            var caption = Ui.Text("Macro Kit Deck", "Muted", true, 11);
            caption.Margin = new Thickness(12, 0, 0, 0);
            strip.Children.Add(caption);
            strip.ToolTip = "Drag to move the Deck.";
            strip.MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch (InvalidOperationException) { } };

            bool small = c.Settings.DeckCompact;
            List<MacroDef> shown = c.Macros.Where(x => x.ShowOnDeck).ToList();
            List<RecordingOptions> recordings = c.DeckRecordings();
            var grid = new UniformGrid { Columns = small ? shown.Count + recordings.Count + 2 : Math.Max(1, Math.Min(8, c.Settings.DeckColumns)), Margin = new Thickness(8, 2, 8, 8) };
            ButtonLabels = new List<string>();
            WarnedButtons = new List<string>();
            HintedButtons = new List<string>();
            ButtonTips = new Dictionary<string, string>();
            foreach (MacroDef m in shown)
            {
                MacroDef macro = m;
                bool running = c.Activity.Kind == ActivityKind.Macro && c.Activity.MacroId == macro.Id;
                string name = macro.Name.Length == 0 ? "(unnamed)" : macro.Name;
                string icon = (macro.DeckIcon ?? "").Trim();
                string problem = running ? null : Broken(macro);
                string label = icon.Length > 0 ? icon + "  " + name : name;
                string hint = small || !macro.Enabled ? null : HotkeyHint(macro.Hotkey);   // a switched-off macro's hotkey does nothing
                var b = DeckButton(label, running, () =>
                {
                    if (running) c.Stop(); else c.StartMacro(macro, RunOrigin.Deck);
                }, hint, ColorFor(macro.DeckColor), problem != null);
                string tip = running ? "Click to stop “" + macro.Name + "”." : "Run “" + macro.Name + "” in the window that has focus.";
                if (!macro.Enabled) tip += " Switched off: its hotkey and schedule are paused.";
                else if (!string.IsNullOrEmpty(macro.Hotkey)) tip += " Hotkey: " + HotkeyHint(macro.Hotkey) + ".";
                if (problem != null) tip = "“" + macro.Name + "” needs fixing before it can run. " + problem;
                b.ToolTip = tip;
                grid.Children.Add(b);
                ButtonLabels.Add(label);
                if (problem != null) WarnedButtons.Add(label);
                if (!string.IsNullOrEmpty(hint)) HintedButtons.Add(label);
                ButtonTips[label] = tip;
            }
            foreach (RecordingOptions o in recordings)
            {
                string id = o.Id;
                string name = c.RecordingName(id);
                bool playingThis = c.Activity.Kind == ActivityKind.Playing && c.Activity.RecordingId == id;
                string label = "▶  " + Rec.DeckName(name);
                var b = DeckButton(label, playingThis, () => { if (playingThis) c.Stop(); else c.StartPlayback(RunOrigin.Deck, id); }, small ? null : HotkeyHint(o.Hotkey), null, false);
                b.ToolTip = playingThis ? "Click to stop “" + name + "”." : "Play the recording “" + name + "”." + (string.IsNullOrEmpty(o.Hotkey) ? "" : " Hotkey: " + HotkeyHint(o.Hotkey) + ".");
                grid.Children.Add(b);
                ButtonLabels.Add(label);
            }
            string recording = c.SelectedRecordingName();
            bool playing = c.Activity.Kind == ActivityKind.Playing;
            var play = DeckButton("▶  " + (recording.Length == 0 ? "No recording" : Rec.DeckName(recording)), playing, () => { if (playing) c.Stop(); else c.StartPlayback(RunOrigin.Deck); }, null, null, false);
            play.ToolTip = "Play the recording selected on the Recorder tab.";
            grid.Children.Add(play);
            var stop = DeckButton("■  Stop", false, () => c.Stop(), null, null, false);
            stop.ToolTip = "Stop anything that is running.";
            stop.IsEnabled = c.IsBusy;
            grid.Children.Add(stop);

            frame.Child = new StackPanel { Children = { strip, grid } };
        }

        // "Ctrl+Shift+1" as it reads on a button; "" without a hotkey.
        static string HotkeyHint(string hotkey)
        {
            if (string.IsNullOrEmpty(hotkey)) return "";
            Hotkey parsed;
            return Hotkey.TryParse(hotkey, out parsed) && !parsed.IsEmpty ? parsed.ToString() : hotkey;
        }

        Button DeckButton(string text, bool active, Action click, string hint, Color? stripe, bool warn)
        {
            bool small = c.Settings.DeckCompact;
            var label = new TextBlock { Text = text, TextWrapping = small ? TextWrapping.NoWrap : TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, MaxHeight = 36 };
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            if (warn)
            {
                var mark = Ui.Text("⚠ needs fixing", "Warn", true, 10.5);
                mark.HorizontalAlignment = HorizontalAlignment.Center;
                if (!small) content.Children.Add(mark);
                else label.Text = "⚠ " + text;
            }
            content.Children.Add(label);
            if (!string.IsNullOrEmpty(hint) && !warn)
            {
                var keys = Ui.Text(hint, "Muted", false, 10.5);
                keys.HorizontalAlignment = HorizontalAlignment.Center;
                keys.TextTrimming = TextTrimming.CharacterEllipsis;
                content.Children.Add(keys);
            }
            var b = NoFocus(Ui.Button(content, click, active ? "Primary" : "Chip"));
            b.Width = small ? 96 : 124;
            b.Height = small ? 30 : 58;   // two lines of name plus the hotkey line
            b.Margin = new Thickness(3);
            b.Padding = new Thickness(8, small ? 2 : 4, 8, small ? 2 : 4);
            b.FontSize = small ? 12 : 12.5;
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            if (stripe.HasValue && !active)
            {
                b.BorderBrush = new SolidColorBrush(stripe.Value);
                b.BorderThickness = new Thickness(4, 1, 1, 1);
            }
            if (warn)
            {
                b.SetResourceReference(Control.BorderBrushProperty, "Warn");
                b.BorderThickness = new Thickness(1.5);
                b.Opacity = 0.85;
            }
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
            c.MarkPlacementDirty();   // every step of a drag: no Edited, so the Deck is not rebuilt meanwhile
        }
    }
}
