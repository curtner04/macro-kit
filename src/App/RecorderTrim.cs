using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MacroKit.Core;
using MacroKit.Recording;
using Rec = MacroKit.Core.Recording;

namespace MacroKit.App
{
    /// <summary>
    /// 1.11.0: the Recorder tab's Trim card. A timeline of the selected recording with two handles that cut its start and end,
    /// "Shorten pauses over [2 s] to [0.5 s]", and Undo. The edits themselves are <see cref="RecordingEdit"/>; this only shows them.
    /// </summary>
    public sealed class RecorderTrim
    {
        public readonly Border Card;
        readonly AppController c;
        readonly Func<string> selectedId;
        readonly TrimTimeline timeline = new TrimTimeline();
        readonly TextBlock rangeText = Ui.Text("", "Muted", false, 12);
        readonly TextBlock pauseHint = Ui.Text("", "Muted", false, 12);
        readonly Button trimButton, undoButton, shortenButton;
        readonly ComboBox over = new ComboBox { Width = 72 }, to = new ComboBox { Width = 72 };
        readonly Dictionary<string, List<Rec>> undo = new Dictionary<string, List<Rec>>();
        static readonly int[] OverChoices = { 1000, 2000, 3000, 5000, 10000 };
        static readonly int[] ToChoices = { 100, 200, 500, 1000 };
        const int UndoDepth = 10;
        Rec current;
        bool loadQueued;

        public RecorderTrim(AppController controller, Func<string> selectedId)
        {
            c = controller;
            this.selectedId = selectedId;
            timeline.Changed += ShowRange;
            trimButton = Ui.Button("Trim", Trim, "Small", "Keep only the part between the handles.");
            undoButton = Ui.Button("Undo", Undo, "Ghost", "Put back the recording as it was before the last trim or shortening.");
            shortenButton = Ui.Button("Shorten", Shorten, "Small", "Cut each long pause down, so the recording plays without the waiting.");
            foreach (int ms in OverChoices) over.Items.Add(Seconds(ms));
            foreach (int ms in ToChoices) to.Items.Add(Seconds(ms));
            over.SelectedIndex = Array.IndexOf(OverChoices, 2000);
            to.SelectedIndex = Array.IndexOf(ToChoices, 500);
            over.SelectionChanged += (s, e) => ShowPauses();
            to.SelectionChanged += (s, e) => ShowPauses();
            System.Windows.Automation.AutomationProperties.SetName(over, "Shorten pauses longer than");
            System.Windows.Automation.AutomationProperties.SetName(to, "Shorten them to");
            over.ToolTip = "Pauses longer than this are cut down.";
            to.ToolTip = "What a long pause is cut down to.";

            var buttons = Ui.Row(6, trimButton, undoButton);
            var pauses = Ui.WrapRow(6, Ui.Text("Shorten pauses over", "Muted", false, 12), over, Ui.Text("to", "Muted", false, 12), to, shortenButton);
            Card = Ui.Card(Ui.Stack(8,
                Ui.Heading("Trim"),
                timeline,
                rangeText,
                buttons,
                Ui.Spacer(2),
                pauses,
                pauseHint,
                Ui.Help("Drag the handles, or Tab to one and use the arrow keys.",
                    "Arrows move a handle by a tenth of a second, Shift+arrows by a second. A pause while a key or button is held stays as it is.")));
            c.ActivityChanged += UpdateButtons;
            Load();
        }

        /// <summary>Shows the selected recording (after a change of selection, or a save). Queued, so a fast scroll loads once.</summary>
        public void QueueLoad()
        {
            if (loadQueued) return;
            loadQueued = true;
            Card.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { loadQueued = false; Load(); }));
        }

        public void Load()
        {
            string id = selectedId();
            current = string.IsNullOrEmpty(id) ? null : c.LoadRecording(id);
            bool show = current != null && current.Events.Count > 0;
            Card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            timeline.Show(show ? current : null);
            ShowRange();
            ShowPauses();
        }

        void ShowRange()
        {
            int length = timeline.Length;
            rangeText.Text = current == null ? "" : timeline.Start == 0 && timeline.End == length
                ? "All " + Seconds(length) + " kept."
                : "Keeps " + Seconds(timeline.Start) + " to " + Seconds(timeline.End) + " (" + Seconds(timeline.End - timeline.Start) + " of " + Seconds(length) + ").";
            UpdateButtons();
        }

        void ShowPauses()
        {
            int n = current == null ? 0 : RecordingEdit.CountLongPauses(current, OverMs);
            pauseHint.Text = n == 0 ? "No pauses longer than " + Seconds(OverMs) + "."
                : n == 1 ? "1 pause is longer than " + Seconds(OverMs) + "."
                : n + " pauses are longer than " + Seconds(OverMs) + ".";
            UpdateButtons();
        }

        void UpdateButtons()
        {
            bool idle = !c.IsBusy && current != null;
            trimButton.IsEnabled = idle && (timeline.Start > 0 || timeline.End < timeline.Length) && timeline.End - timeline.Start >= TrimTimeline.MinKeptMs;
            shortenButton.IsEnabled = idle && ToMs < OverMs && RecordingEdit.CountLongPauses(current, OverMs) > 0;
            List<Rec> stack;
            undoButton.Visibility = current != null && undo.TryGetValue(current.Id, out stack) && stack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            undoButton.IsEnabled = !c.IsBusy;
        }

        int OverMs { get { return OverChoices[Math.Max(0, over.SelectedIndex)]; } }
        int ToMs { get { return ToChoices[Math.Max(0, to.SelectedIndex)]; } }

        void Trim()
        {
            if (current == null || !trimButton.IsEnabled) return;
            Rec before = current;
            Rec after = RecordingEdit.Trim(before, timeline.Start, timeline.End);
            Save(before, after, "Trimmed to " + Seconds(after.DurationMs) + ".");
        }

        void Shorten()
        {
            if (current == null || !shortenButton.IsEnabled) return;
            Rec before = current;
            int n;
            Rec after = RecordingEdit.ShortenPauses(before, OverMs, ToMs, out n);
            Save(before, after, "Shortened " + n + (n == 1 ? " pause" : " pauses") + ": now " + Seconds(after.DurationMs) + ", was " + Seconds(RecordingEdit.Length(before)) + ".");
        }

        void Save(Rec before, Rec after, string done)
        {
            if (!c.ReplaceRecording(after, done)) return;
            List<Rec> stack;
            if (!undo.TryGetValue(before.Id, out stack)) undo[before.Id] = stack = new List<Rec>();
            stack.Add(before);
            if (stack.Count > UndoDepth) stack.RemoveAt(0);
            Load();   // ReplaceRecording already announced the change; this makes sure the new length shows at once
        }

        void Undo()
        {
            List<Rec> stack;
            if (current == null || !undo.TryGetValue(current.Id, out stack) || stack.Count == 0) return;
            Rec previous = stack[stack.Count - 1];
            if (!c.ReplaceRecording(previous, "Put the recording back as it was (" + Seconds(RecordingEdit.Length(previous)) + ").")) return;
            stack.RemoveAt(stack.Count - 1);
            Load();
        }

        /// <summary>"0.5 s", "12.4 s", "1:05.3".</summary>
        public static string Seconds(int ms)
        {
            ms = Math.Max(0, ms);
            if (ms < 60000)
            {
                double s = ms / 1000.0;
                return (s == Math.Floor(s) ? s.ToString("0", CultureInfo.CurrentCulture) : s.ToString("0.0", CultureInfo.CurrentCulture)) + " s";
            }
            int minutes = ms / 60000;
            return minutes + ":" + ((ms % 60000) / 1000.0).ToString("00.0", CultureInfo.CurrentCulture);
        }

        // ---- for the self test
        public bool Shown { get { return Card.Visibility == Visibility.Visible; } }
        public int RangeStart { get { return timeline.Start; } }
        public int RangeEnd { get { return timeline.End; } }
        public string RangeText { get { return rangeText.Text; } }
        public string PauseText { get { return pauseHint.Text; } }
        public bool CanUndo { get { return undoButton.Visibility == Visibility.Visible; } }
        public void SetRangeForTest(int start, int end) { timeline.SetRange(start, end); }
        public void PressKeyOnStartHandleForTest(Key key, bool shift) { timeline.Nudge(true, key, shift); }
        public void TrimForTest() { Trim(); }
        public void ShortenForTest() { Shorten(); }
        public void UndoForTest() { Undo(); }
        public bool TrimEnabled { get { return trimButton.IsEnabled; } }
    }

    /// <summary>The recording drawn as a strip (clicks tall, keys shorter, scrolls shortest), the cut parts shaded, two handles.</summary>
    public sealed class TrimTimeline : Panel
    {
        public const int MinKeptMs = 100;
        const double Pad = 6, StripTop = 4, StripHeight = 30, HandleWidth = 10;
        public readonly TrimHandle StartHandle = new TrimHandle("Trim start"), EndHandle = new TrimHandle("Trim end");
        public event Action Changed = () => { };
        byte[] marks = new byte[0];   // per tenth of a second: 0 none, 1 scroll or window, 2 key, 3 click
        int length, start, end;
        TrimHandle dragging;

        public static readonly DependencyProperty TrackBrushProperty = Brush("TrackBrush");
        public static readonly DependencyProperty KeptBrushProperty = Brush("KeptBrush");
        public static readonly DependencyProperty MarkBrushProperty = Brush("MarkBrush");
        public static readonly DependencyProperty ClickBrushProperty = Brush("ClickBrush");
        public static readonly DependencyProperty CutBrushProperty = Brush("CutBrush");

        static DependencyProperty Brush(string name)
        {
            return DependencyProperty.Register(name, typeof(Brush), typeof(TrimTimeline), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
        }

        public TrimTimeline()
        {
            Height = StripTop * 2 + StripHeight;
            Background = Brushes.Transparent;   // the whole strip takes clicks
            Cursor = Cursors.Hand;
            SetResourceReference(TrackBrushProperty, "Card2");
            SetResourceReference(KeptBrushProperty, "Selected");
            SetResourceReference(MarkBrushProperty, "Muted");
            SetResourceReference(ClickBrushProperty, "Accent");
            SetResourceReference(CutBrushProperty, "Bg");
            Children.Add(StartHandle);
            Children.Add(EndHandle);
            foreach (TrimHandle h in new[] { StartHandle, EndHandle })
            {
                TrimHandle handle = h;
                handle.PreviewMouseLeftButtonDown += (s, e) => { dragging = handle; CaptureMouse(); handle.Focus(); e.Handled = true; };
                handle.KeyDown += (s, e) =>
                {
                    if (Nudge(handle == StartHandle, e.Key, (Keyboard.Modifiers & ModifierKeys.Shift) != 0)) e.Handled = true;
                };
            }
            SizeChanged += (s, e) => InvalidateArrange();
        }

        public int Length { get { return length; } }
        public int Start { get { return start; } }
        public int End { get { return end; } }

        public void Show(Rec rec)
        {
            length = rec == null ? 0 : RecordingEdit.Length(rec);
            start = 0;
            end = length;
            marks = new byte[length / 100 + 1];
            if (rec != null)
                foreach (RecEvent e in rec.Events)
                {
                    byte m = e.Kind == RecKind.ButtonDown ? (byte)3 : e.Kind == RecKind.KeyDown ? (byte)2
                        : e.Kind == RecKind.Wheel || e.Kind == RecKind.HWheel ? (byte)1 : (byte)0;
                    int slot = Math.Max(0, Math.Min(marks.Length - 1, e.T / 100));
                    if (m > marks[slot]) marks[slot] = m;
                }
            UpdateNames();
            InvalidateArrange();
            InvalidateVisual();
            Changed();
        }

        public void SetRange(int newStart, int newEnd)
        {
            newStart = Math.Max(0, Math.Min(newStart, length));
            newEnd = Math.Max(0, Math.Min(newEnd, length));
            if (newEnd - newStart < MinKeptMs)
            {
                if (newStart != start) newStart = Math.Max(0, newEnd - MinKeptMs);
                else newEnd = Math.Min(length, newStart + MinKeptMs);
            }
            if (newStart == start && newEnd == end) return;
            start = newStart;
            end = newEnd;
            UpdateNames();
            InvalidateArrange();
            InvalidateVisual();
            Changed();
        }

        /// <summary>Arrow keys move a handle a tenth of a second (Shift: a second); Home and End send it to the end it is nearest.</summary>
        public bool Nudge(bool startHandle, Key key, bool shift)
        {
            int step = shift ? 1000 : 100, value = startHandle ? start : end;
            switch (key)
            {
                case Key.Left: case Key.Down: value -= step; break;
                case Key.Right: case Key.Up: value += step; break;
                case Key.PageDown: value -= 1000; break;
                case Key.PageUp: value += 1000; break;
                case Key.Home: value = startHandle ? 0 : start + MinKeptMs; break;
                case Key.End: value = startHandle ? end - MinKeptMs : length; break;
                default: return false;
            }
            if (startHandle) SetRange(Math.Min(value, end - MinKeptMs), end);
            else SetRange(start, Math.Max(value, start + MinKeptMs));
            return true;
        }

        void UpdateNames()
        {
            StartHandle.Say("Trim start, " + RecorderTrim.Seconds(start) + " of " + RecorderTrim.Seconds(length));
            EndHandle.Say("Trim end, " + RecorderTrim.Seconds(end) + " of " + RecorderTrim.Seconds(length));
        }

        double Width0 { get { return Math.Max(1, ActualWidth - 2 * Pad); } }
        double X(int ms) { return Pad + (length <= 0 ? 0 : Width0 * ms / length); }
        int Ms(double x) { return length <= 0 ? 0 : (int)Math.Round((x - Pad) / Width0 * length); }

        protected override Size MeasureOverride(Size available)
        {
            foreach (UIElement child in InternalChildren) child.Measure(new Size(HandleWidth, StripHeight + 2 * StripTop));
            return new Size(double.IsInfinity(available.Width) ? 300 : available.Width, StripTop * 2 + StripHeight);
        }

        protected override Size ArrangeOverride(Size final)
        {
            StartHandle.Arrange(new Rect(X(start) - HandleWidth / 2, 0, HandleWidth, final.Height));
            EndHandle.Arrange(new Rect(X(end) - HandleWidth / 2, 0, HandleWidth, final.Height));
            return final;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth;
            var strip = new Rect(Pad, StripTop, Width0, StripHeight);
            dc.DrawRoundedRectangle((Brush)GetValue(TrackBrushProperty), null, strip, 4, 4);
            if (length <= 0) return;
            dc.DrawRectangle((Brush)GetValue(KeptBrushProperty), null, new Rect(X(start), StripTop, Math.Max(0, X(end) - X(start)), StripHeight));
            var mark = new Pen((Brush)GetValue(MarkBrushProperty), 1);
            var click = new Pen((Brush)GetValue(ClickBrushProperty), 1.5);
            mark.Freeze();
            click.Freeze();
            double lastX = -10;
            byte lastMark = 0;
            for (int slot = 0; slot < marks.Length; slot++)
            {
                byte m = marks[slot];
                if (m == 0) continue;
                double x = Math.Round(X(Math.Min(length, slot * 100 + 50))) + 0.5;
                if (x - lastX < 2 && m <= lastMark) continue;   // one mark per couple of pixels on a long recording
                lastX = x;
                lastMark = m;
                double h = m == 3 ? StripHeight - 8 : m == 2 ? StripHeight / 2 : StripHeight / 4;
                double y = StripTop + (StripHeight - h) / 2;
                dc.DrawLine(m == 3 ? click : mark, new Point(x, y), new Point(x, y + h));
            }
            var cut = ((Brush)GetValue(CutBrushProperty)).Clone();
            cut.Opacity = 0.6;
            cut.Freeze();
            if (start > 0) dc.DrawRectangle(cut, null, new Rect(Pad, StripTop, X(start) - Pad, StripHeight));
            if (end < length) dc.DrawRectangle(cut, null, new Rect(X(end), StripTop, Pad + Width0 - X(end), StripHeight));
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (length <= 0) return;
            int ms = Ms(e.GetPosition(this).X);
            dragging = Math.Abs(ms - start) <= Math.Abs(ms - end) ? StartHandle : EndHandle;
            MoveDragged(ms);
            CaptureMouse();
            dragging.Focus();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging != null && IsMouseCaptured) MoveDragged(Ms(e.GetPosition(this).X));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            dragging = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            dragging = null;
        }

        void MoveDragged(int ms)
        {
            if (dragging == StartHandle) SetRange(Math.Min(ms, end - MinKeptMs), end);
            else SetRange(start, Math.Max(ms, start + MinKeptMs));
        }
    }

    /// <summary>One of the timeline's two handles: a bar you can drag or Tab to and move with the arrow keys.</summary>
    public sealed class TrimHandle : Control
    {
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(TrimHandle),
            new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty RingProperty = DependencyProperty.Register("Ring", typeof(Brush), typeof(TrimHandle),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty GripProperty = DependencyProperty.Register("Grip", typeof(Brush), typeof(TrimHandle),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public TrimHandle(string name)
        {
            Focusable = true;
            IsTabStop = true;
            Cursor = Cursors.SizeWE;
            FocusVisualStyle = null;
            SetResourceReference(FillProperty, "Accent");
            SetResourceReference(RingProperty, "Text");
            SetResourceReference(GripProperty, "AccentFg");
            Say(name);
            ToolTip = "Drag, or use the arrow keys (Shift for a second at a time).";
        }

        public void Say(string text) { System.Windows.Automation.AutomationProperties.SetName(this, text); }

        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() { return new Peer(this); }

        // Read as a slider named "Trim start, 1.5 s of 12 s".
        sealed class Peer : System.Windows.Automation.Peers.FrameworkElementAutomationPeer
        {
            public Peer(TrimHandle owner) : base(owner) { }
            protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() { return System.Windows.Automation.Peers.AutomationControlType.Slider; }
            protected override string GetClassNameCore() { return "TrimHandle"; }
            protected override bool IsKeyboardFocusableCore() { return true; }
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

        protected override void OnRender(DrawingContext dc)
        {
            var bar = new Rect(1, 0, Math.Max(2, ActualWidth - 2), ActualHeight);
            dc.DrawRoundedRectangle((Brush)GetValue(FillProperty), IsKeyboardFocused ? new Pen((Brush)GetValue(RingProperty), 2) : null, bar, 3, 3);
            var grip = new Pen((Brush)GetValue(GripProperty), 1);
            double mid = ActualWidth / 2;
            dc.DrawLine(grip, new Point(mid, ActualHeight / 2 - 6), new Point(mid, ActualHeight / 2 + 6));
        }
    }
}
