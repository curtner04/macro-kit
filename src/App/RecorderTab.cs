using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MacroKit.Core;
using Rec = MacroKit.Core.Recording;

namespace MacroKit.App
{
    /// <summary>The Recorder tab: record, save and play back mouse and keyboard recordings.</summary>
    public sealed class RecorderTab
    {
        public readonly FrameworkElement Root;
        readonly AppController c;
        readonly Window owner;
        readonly ListBox list = new ListBox();
        readonly TextBlock listEmpty;
        readonly Button recordButton, playButton, stopButton, saveButton, renameButton, deleteButton, exportButton;
        readonly TextBlock recordHint = Ui.Text("", "Muted", false, 11), playHint = Ui.Text("", "Muted", false, 11), stopHint = Ui.Text("", "Muted", false, 11);
        readonly Slider speed = new Slider { Minimum = -1, Maximum = 2, SmallChange = 0.01, LargeChange = 0.1, IsMoveToPointEnabled = true };
        readonly TextBlock speedValue = Ui.Text("", null, true);
        readonly List<KeyValuePair<double, Button>> speedChips = new List<KeyValuePair<double, Button>>();
        readonly RadioButton once, times, continuous;
        readonly NumberBox repeatTimes, repeatDelay;
        bool loading;

        public RecorderTab(AppController controller, Window owner)
        {
            c = controller;
            this.owner = owner;

            // ---- recordings list
            listEmpty = Ui.Para("No recordings yet. Press Record, do something, then press Stop. Macro Kit hides while it records so it never gets in the way.");
            listEmpty.Margin = new Thickness(10, 8, 10, 0);
            list.SelectionChanged += (s, e) =>
            {
                if (loading) return;
                var item = list.SelectedItem as ListBoxItem;
                c.SelectRecording(item == null ? "" : (string)item.Tag);
                UpdateButtons();
            };
            saveButton = Ui.Button("Save", () => c.SavePending(), "Small", "Keep the new recording.");
            renameButton = Ui.Button("Rename…", Rename, "Small", "Give the selected recording a new name.");
            deleteButton = Ui.Button("Delete", Delete, "Small", "Delete the selected recording.");
            exportButton = Ui.Button("Export file…", Export, "Small", "Save the selected recording as a file you can share.");
            var importButton = Ui.Button("Import file…", Import, "Small", "Add a recording from a file.");
            var listButtons = Ui.Wrap(saveButton, renameButton, deleteButton, exportButton, importButton);
            foreach (UIElement b in listButtons.Children) ((FrameworkElement)b).Margin = new Thickness(0, 0, 6, 6);

            var listGrid = new Grid();
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition());
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var listTitle = Ui.Heading("Recordings");
            listTitle.Margin = new Thickness(0, 0, 0, 8);
            listGrid.Children.Add(listTitle);
            var listHost = new Grid();
            listHost.Children.Add(list);
            listHost.Children.Add(listEmpty);
            Grid.SetRow(listHost, 1);
            listGrid.Children.Add(listHost);
            listButtons.Margin = new Thickness(0, 8, 0, -6);
            Grid.SetRow(listButtons, 2);
            listGrid.Children.Add(listButtons);
            Border listCard = Ui.Card(listGrid);
            listCard.Margin = new Thickness(0, 0, 10, 0);

            // ---- transport
            recordButton = Transport("record", "Record", recordHint, () => c.StartRecording(RunOrigin.MainWindow), "Start recording mouse and keyboard. Macro Kit hides while it records.");
            playButton = Transport("play", "Play", playHint, () => c.StartPlayback(RunOrigin.MainWindow), "Play the selected recording.");
            stopButton = Transport("stop", "Stop", stopHint, () => c.Stop(), "Stop recording or playback.");
            playButton.Style = (Style)Application.Current.FindResource("Primary");
            var transport = new UniformGrid { Columns = 3 };
            foreach (Button b in new[] { recordButton, playButton, stopButton }) { b.Margin = new Thickness(0, 0, 8, 0); transport.Children.Add(b); }
            ((FrameworkElement)transport.Children[2]).Margin = new Thickness(0);

            // ---- playback options
            speed.ValueChanged += (s, e) => { if (!loading) SetSpeed(SnapSpeed(Math.Pow(10, speed.Value)), false); };
            foreach (double chip in new[] { 0.5, 1, 2, 5, 10 })
            {
                double target = chip;
                var b = Ui.Button(chip == 0.5 ? "½×" : chip.ToString(CultureInfo.InvariantCulture) + "×", () => SetSpeed(target, true), "Chip", "Play at " + chip.ToString(CultureInfo.InvariantCulture) + "× speed.");
                speedChips.Add(new KeyValuePair<double, Button>(chip, b));
            }
            var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var chip in speedChips) chips.Children.Add(chip.Value);

            var speedHead = new DockPanel();
            speedValue.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(speedValue, Dock.Right);
            speedHead.Children.Add(speedValue);
            speedHead.Children.Add(Ui.Text("Speed", "Muted", false, 12));

            once = new RadioButton { Content = "Once", GroupName = "playRepeat" };
            times = new RadioButton { Content = "", GroupName = "playRepeat", VerticalAlignment = VerticalAlignment.Center };
            continuous = new RadioButton { Content = "Continuously", GroupName = "playRepeat" };
            repeatTimes = new NumberBox(2, 1, 100000, 60);
            repeatDelay = new NumberBox(0, 0, 3600000, 70);
            once.Checked += (s, e) => RepeatChanged(RepeatMode.Once);
            times.Checked += (s, e) => RepeatChanged(RepeatMode.Times);
            continuous.Checked += (s, e) => RepeatChanged(RepeatMode.Continuous);
            repeatTimes.Changed += () => { if (loading) return; if (times.IsChecked != true) times.IsChecked = true; c.Settings.Playback.RepeatTimes = repeatTimes.Value; c.MarkDirty(SaveParts.Settings); };
            repeatDelay.Changed += () => { if (loading) return; c.Settings.Playback.RepeatDelayMs = repeatDelay.Value; c.MarkDirty(SaveParts.Settings); };
            var timesRow = Ui.Row(0, times, repeatTimes, Ui.Text("times", null));
            repeatTimes.Margin = new Thickness(2, 0, 7, 0);
            var repeatRow = Ui.Row(18, once, timesRow, continuous);
            var delayRow = Ui.Row(7, Ui.Text("Wait", "Muted", false, 12), repeatDelay, Ui.Text("ms between repeats", "Muted", false, 12));

            var playback = Ui.Card(Ui.Stack(8,
                Ui.Heading("Playback"),
                speedHead, speed, chips,
                Ui.Spacer(2),
                Ui.Text("Repeat", "Muted", false, 12), repeatRow, delayRow));

            var mouseMoves = new CheckBox { Content = "Record mouse movement" , ToolTip = "Off records clicks and keys only; the cursor jumps straight to each click." };
            mouseMoves.IsChecked = c.Settings.RecordMouseMoves;
            mouseMoves.Click += (s, e) => { c.Settings.RecordMouseMoves = mouseMoves.IsChecked == true; c.MarkDirty(SaveParts.Settings); };
            var autoSave = new CheckBox { Content = "Save new recordings automatically", ToolTip = "Off keeps a new recording unsaved until you press Save." };
            autoSave.IsChecked = c.Settings.AutoSaveRecordings;
            autoSave.Click += (s, e) => { c.Settings.AutoSaveRecordings = autoSave.IsChecked == true; c.MarkDirty(SaveParts.Settings); };
            var recording = Ui.Card(Ui.Stack(8, Ui.Heading("Recording"), mouseMoves, autoSave,
                Ui.Para("Macro Kit hides while it records or plays, and comes back when you stop. Esc stops a playback; hotkeys work from any program.", "Muted", 12)));

            var right = new StackPanel();
            right.Children.Add(Ui.Card(Ui.Stack(0, transport)));
            right.Children.Add(playback);
            right.Children.Add(recording);
            var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 380 });
            grid.Children.Add(listCard);
            Grid.SetColumn(rightScroll, 1);
            grid.Children.Add(rightScroll);
            Root = grid;

            c.RecordingsChanged += Reload;
            c.ActivityChanged += UpdateButtons;
            c.Edited += parts => { if ((parts & SaveParts.Settings) != 0) UpdateHints(); };
            Reload();
            LoadPlayback();
            UpdateHints();
        }

        Button Transport(string icon, string label, TextBlock hint, Action click, string tip)
        {
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var top = Ui.Row(8, Ui.Shape(icon, 12, icon == "record" ? "Rec" : icon == "stop" ? "Text" : "AccentFg"), new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            top.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(top);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.Margin = new Thickness(0, 3, 0, 0);
            content.Children.Add(hint);
            var b = Ui.Button(content, click, "Transport", tip);
            return b;
        }

        // ---- list

        void Reload()
        {
            loading = true;
            string selected = c.Settings.SelectedRecordingId;
            list.Items.Clear();
            if (c.Pending != null) list.Items.Add(MakeItem(c.Pending.Id, c.Pending.Name, c.Pending.DurationMs, c.Pending.Events.Count, c.Pending.Created, true));
            foreach (RecordingInfo info in c.Recordings)
                if (c.Pending == null || c.Pending.Id != info.Id) list.Items.Add(MakeItem(info.Id, info.Name, info.DurationMs, info.EventCount, info.Created, false));
            ListBoxItem match = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == selected);
            if (match == null && list.Items.Count > 0)
            {
                match = (ListBoxItem)list.Items[0];
                c.Settings.SelectedRecordingId = (string)match.Tag;
                c.MarkDirty(SaveParts.Settings);
            }
            list.SelectedItem = match;
            listEmpty.Visibility = list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            loading = false;
            UpdateButtons();
        }

        static ListBoxItem MakeItem(string id, string name, int durationMs, int events, string created, bool unsaved)
        {
            var panel = new StackPanel();
            var head = new DockPanel();
            if (unsaved)
            {
                var badge = Ui.Pill("Not saved", "Warn", "WarnBg", 10.5);
                badge.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(badge, Dock.Right);
                head.Children.Add(badge);
            }
            head.Children.Add(Ui.Text(name, "Text", true));
            panel.Children.Add(head);
            var sub = Ui.Text(AppController.FormatDuration(durationMs) + "  ·  " + events.ToString("N0", CultureInfo.CurrentCulture) + " events  ·  " + FormatCreated(created), "Muted", false, 11.5);
            sub.Margin = new Thickness(0, 2, 0, 0);
            panel.Children.Add(sub);
            return new ListBoxItem { Content = panel, Tag = id };
        }

        static string FormatCreated(string iso)
        {
            DateTimeOffset when;
            return DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out when) ? when.LocalDateTime.ToString("MMM d, h:mm tt", CultureInfo.CurrentCulture) : "";
        }

        string SelectedId { get { var item = list.SelectedItem as ListBoxItem; return item == null ? null : (string)item.Tag; } }

        void UpdateButtons()
        {
            bool idle = !c.IsBusy;
            bool has = SelectedId != null;
            bool pendingSelected = c.Pending != null && SelectedId == c.Pending.Id;
            recordButton.IsEnabled = idle;
            playButton.IsEnabled = idle && has;
            stopButton.IsEnabled = !idle;
            saveButton.IsEnabled = pendingSelected;
            renameButton.IsEnabled = has;
            deleteButton.IsEnabled = has;
            exportButton.IsEnabled = has;
        }

        void UpdateHints()
        {
            recordHint.Text = c.Settings.RecordHotkey;
            playHint.Text = c.Settings.PlayHotkey;
            stopHint.Text = c.Settings.StopHotkey;
            Ui.Ref(playHint, TextBlock.ForegroundProperty, "AccentFg");
            playHint.Opacity = 0.8;
        }

        void Rename()
        {
            string id = SelectedId;
            if (id == null) return;
            var item = (ListBoxItem)list.SelectedItem;
            string current = c.Pending != null && c.Pending.Id == id ? c.Pending.Name : c.Recordings.Where(r => r.Id == id).Select(r => r.Name).FirstOrDefault();
            string name = Dialogs.Prompt(owner, "Rename recording", "Choose a name you will recognize later.", current, "Rename");
            if (name != null) c.RenameRecording(id, name);
        }

        void Delete()
        {
            string id = SelectedId;
            if (id == null) return;
            string name = c.Pending != null && c.Pending.Id == id ? c.Pending.Name : c.Recordings.Where(r => r.Id == id).Select(r => r.Name).FirstOrDefault();
            if (Ui.Confirm(owner, "Delete “" + name + "”? This can't be undone.")) c.DeleteRecording(id);
        }

        void Export()
        {
            string id = SelectedId;
            if (id == null) return;
            string name = c.Pending != null && c.Pending.Id == id ? c.Pending.Name : c.Recordings.Where(r => r.Id == id).Select(r => r.Name).FirstOrDefault();
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export recording", Filter = "Macro Kit recording (*.json)|*.json", DefaultExt = ".json",
                FileName = string.Concat((name ?? "recording").Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch))
            };
            if (dialog.ShowDialog(owner) == true) c.ExportRecording(id, dialog.FileName);
        }

        void Import()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import recording", Filter = "Macro Kit recording (*.json)|*.json|All files (*.*)|*.*" };
            if (dialog.ShowDialog(owner) == true) c.ImportRecording(dialog.FileName);
        }

        // ---- playback options

        void LoadPlayback()
        {
            loading = true;
            PlaybackSettings p = c.Settings.Playback;
            speed.Value = Math.Log10(Math.Max(0.1, Math.Min(100, p.Speed)));
            repeatTimes.Value = p.RepeatTimes;
            repeatDelay.Value = p.RepeatDelayMs;
            once.IsChecked = p.Repeat == RepeatMode.Once;
            times.IsChecked = p.Repeat == RepeatMode.Times;
            continuous.IsChecked = p.Repeat == RepeatMode.Continuous;
            loading = false;
            ShowSpeed();
        }

        void RepeatChanged(RepeatMode mode)
        {
            if (loading) return;
            c.Settings.Playback.Repeat = mode;
            c.MarkDirty(SaveParts.Settings);
        }

        static double SnapSpeed(double v)
        {
            if (Math.Abs(v - 1) < 0.035) return 1;
            return v < 1 ? Math.Round(v, 2) : v < 10 ? Math.Round(v, 1) : Math.Round(v);
        }

        void SetSpeed(double value, bool moveSlider)
        {
            value = Math.Max(0.1, Math.Min(100, value));
            c.Settings.Playback.Speed = value;
            c.MarkDirty(SaveParts.Settings);
            if (moveSlider)
            {
                loading = true;
                speed.Value = Math.Log10(value);
                loading = false;
            }
            ShowSpeed();
        }

        void ShowSpeed()
        {
            double v = c.Settings.Playback.Speed;
            speedValue.Text = (v < 10 ? v.ToString("0.0#", CultureInfo.CurrentCulture) : v.ToString("0", CultureInfo.CurrentCulture)) + "×";
            foreach (var chip in speedChips)
            {
                bool current = Math.Abs(chip.Key - v) < 0.001;
                chip.Value.SetResourceReference(Control.BackgroundProperty, current ? "Selected" : "Card2");
                chip.Value.SetResourceReference(Control.BorderBrushProperty, current ? "Accent" : "Border");
            }
        }
    }
}
