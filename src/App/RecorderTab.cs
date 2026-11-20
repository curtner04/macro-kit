using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
        readonly Button recordButton, playButton, stopButton, saveButton, renameButton, duplicateButton, deleteButton, exportButton;
        readonly TextBox listSearch = new TextBox();
        FrameworkElement recordIcon, playIcon;
        readonly TextBlock recordHint = Ui.Text("", "Muted", false, 11), playHint = Ui.Text("", "Muted", false, 11), stopHint = Ui.Text("", "Muted", false, 11);
        readonly Slider speed = new Slider { Minimum = -1, Maximum = 2, SmallChange = 0.01, LargeChange = 0.1, IsMoveToPointEnabled = true };
        readonly TextBlock speedValue = Ui.Text("", null, true);
        readonly List<KeyValuePair<double, Button>> speedChips = new List<KeyValuePair<double, Button>>();
        readonly RadioButton once, times, continuous;
        readonly NumberBox repeatTimes, repeatDelay;
        readonly HotkeyBox hotkey = new HotkeyBox(true, 190);
        readonly TextBlock hotkeyError = Ui.Text("", "Bad", false, 12);
        readonly CheckBox onDeck = new CheckBox { Content = "Show a button for it on the Deck", ToolTip = "The Deck gets a button that plays this recording (and stops it while it plays)." };
        readonly Button convertButton;
        readonly TextBlock playbackFor = Ui.Text("", "Muted", false, 12);
        bool loading;
        string primaryNow = "";
        const string EmptyText = "No recordings yet. Press Record, do something, then press Stop. While it records, Macro Kit shrinks to a small bar with Stop, Restart and Discard.";

        public RecorderTab(AppController controller, Window owner)
        {
            c = controller;
            this.owner = owner;

            // ---- recordings list
            listEmpty = Ui.Para(EmptyText);
            listEmpty.Margin = new Thickness(10, 8, 10, 0);
            listSearch.ToolTip = "Show only the recordings whose name holds these words.";
            listSearch.TextChanged += (s, e) => { FilterList(); KeepSelectionShown(); };
            list.SelectionChanged += (s, e) =>
            {
                if (loading) return;
                var item = list.SelectedItem as ListBoxItem;
                c.SelectRecording(item == null ? "" : (string)item.Tag);
                UpdateButtons();
                LoadPlayback();
                LoadOptions();
            };
            // Standard list shortcuts: double-click or F2 renames, Delete asks to delete.
            list.MouseDoubleClick += (s, e) =>
            {
                var source = e.OriginalSource as DependencyObject;
                if (source != null && ItemsControl.ContainerFromElement(list, source) is ListBoxItem) { e.Handled = true; Rename(); }
            };
            list.KeyDown += (s, e) =>
            {
                if (e.Key == Key.F2) { e.Handled = true; Rename(); }
                else if (e.Key == Key.Delete) { e.Handled = true; Delete(); }
            };
            saveButton = Ui.Button("Save", () => c.SavePending(), "Small", "Keep the new recording.");
            renameButton = Ui.Button("Rename…", Rename, "Small", "Give the selected recording a new name.");
            duplicateButton = Ui.Button("Duplicate", Duplicate, "Small", "Make a copy of the selected recording to change without losing the original. The copy has no hotkey.");
            deleteButton = Ui.Button("Delete", Delete, "Small", "Delete the selected recording.");
            exportButton = Ui.Button("Export file…", Export, "Small", "Save the selected recording as a file you can share.");
            var importButton = Ui.Button("Import file…", Import, "Small", "Add a recording from a file.");
            var listButtons = Ui.Wrap(saveButton, renameButton, duplicateButton, deleteButton, exportButton, importButton);
            foreach (UIElement b in listButtons.Children) ((FrameworkElement)b).Margin = new Thickness(0, 0, 6, 6);

            var listGrid = new Grid();
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition());
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var listTitle = Ui.Heading("Recordings");
            listTitle.Margin = new Thickness(0, 0, 0, 8);
            listGrid.Children.Add(listTitle);
            var searchBox = Ui.Hinted(listSearch, "Search recordings");
            searchBox.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(searchBox, 1);
            listGrid.Children.Add(searchBox);
            var listHost = new Grid();
            listHost.Children.Add(list);
            listHost.Children.Add(listEmpty);
            Grid.SetRow(listHost, 2);
            listGrid.Children.Add(listHost);
            listButtons.Margin = new Thickness(0, 8, 0, -6);
            Grid.SetRow(listButtons, 3);
            listGrid.Children.Add(listButtons);
            Border listCard = Ui.Card(listGrid);
            listCard.Margin = new Thickness(0, 0, 10, 0);

            // ---- transport
            recordButton = Transport("record", "Record", recordHint, () => c.StartRecording(RunOrigin.MainWindow), "Start recording mouse and keyboard. Macro Kit shrinks to a small recorder bar until you stop.");
            playButton = Transport("play", "Play", playHint, () => c.StartPlayback(RunOrigin.MainWindow), "Play the selected recording.");
            stopButton = Transport("stop", "Stop", stopHint, () => c.Stop(), "Stop recording or playback.");
            foreach (Button b in new[] { playButton, stopButton }) ToolTipService.SetShowOnDisabled(b, true);
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
            repeatTimes.Changed += () => { if (loading) return; if (times.IsChecked != true) times.IsChecked = true; int n = repeatTimes.Value; c.EditPlayback(SelectedId, p => p.RepeatTimes = n); };
            repeatDelay.Changed += () => { if (loading) return; int ms = repeatDelay.Value; c.EditPlayback(SelectedId, p => p.RepeatDelayMs = ms); };
            var timesRow = Ui.Row(0, times, repeatTimes, Ui.Text("times", null));
            repeatTimes.Margin = new Thickness(2, 0, 7, 0);
            var repeatRow = Ui.Row(18, once, timesRow, continuous);
            var delayRow = Ui.Row(7, Ui.Text("Wait", "Muted", false, 12), repeatDelay, Ui.Text("ms between repeats", "Muted", false, 12));
            delayRow.ToolTip = "1000 ms = 1 second.";

            // ---- the selected recording's own hotkey, Deck button and macro
            hotkey.Changed += () =>
            {
                if (loading || SelectedId == null) return;
                c.SetRecordingHotkey(SelectedId, hotkey.Value.IsEmpty ? "" : hotkey.Value.ToString());
                ShowHotkeyError();
                UpdateHints();
            };
            if (!c.InputEnabled) hotkey.ToolTip = "Hotkeys are off in this session (input is turned off).";
            hotkeyError.TextWrapping = TextWrapping.Wrap;
            hotkeyError.Visibility = Visibility.Collapsed;
            onDeck.Click += (s, e) => { if (!loading && SelectedId != null) c.SetRecordingOnDeck(SelectedId, onDeck.IsChecked == true); };
            convertButton = Ui.Button("Convert to macro", ConvertToMacro, "Small", "Make a macro from this recording's clicks, typing, keys and pauses, so you can edit it step by step.");
            convertButton.HorizontalAlignment = HorizontalAlignment.Left;
            var options = Ui.Card(Ui.Stack(8,
                Ui.Heading("This recording"),
                Ui.Row(10, Ui.Text("Hotkey", "Muted", false, 12), hotkey.WithClear()),
                hotkeyError,
                Ui.Para("Plays it from any program; press it again to stop.", "Muted", 12),
                onDeck,
                Ui.Spacer(2),
                convertButton,
                Ui.Para("Convert to macro turns the clicks, typing and pauses into steps you can read and change on the Macros tab. Mouse movement between clicks is left out.", "Muted", 12)));

            var playback = Ui.Card(Ui.Stack(8,
                Ui.Heading("Playback"),
                playbackFor,
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
                Ui.Para("While it records, Macro Kit shrinks to a small bar: Stop keeps the take, Restart starts over, Discard throws it away. Clicks on the bar are never recorded. Macro Kit hides while it plays; Esc stops a playback, and hotkeys work from any program.", "Muted", 12)));

            var right = new StackPanel();
            right.Children.Add(Ui.Card(Ui.Stack(0, transport)));
            right.Children.Add(playback);
            right.Children.Add(options);
            right.Children.Add(recording);
            var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 380 });
            grid.Children.Add(listCard);
            Grid.SetColumn(rightScroll, 1);
            grid.Children.Add(rightScroll);
            Root = grid;

            c.RecordingsChanged += () => { Reload(); LoadPlayback(); LoadOptions(); };
            c.ActivityChanged += UpdateButtons;
            c.HotkeysChanged += ShowHotkeyError;
            c.Edited += parts => { if ((parts & SaveParts.Settings) != 0) UpdateHints(); };
            Reload();
            LoadPlayback();
            LoadOptions();
            UpdateHints();
        }

        Button Transport(string icon, string label, TextBlock hint, Action click, string tip)
        {
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            FrameworkElement shape = Ui.Shape(icon, 12, icon == "record" ? "Rec" : "Text");
            if (icon == "record") recordIcon = shape; else if (icon == "play") playIcon = shape;
            var top = Ui.Row(8, shape, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
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
            // A search that hides the selected take (a new one, an import) is cleared so the selection stays put.
            if (match != null && listSearch.Text.Length > 0 && !Ui.Matches(listSearch.Text, c.RecordingName((string)match.Tag))) listSearch.Text = "";
            loading = false;
            FilterList();
            KeepSelectionShown();
            UpdateButtons();
        }

        /// <summary>Hides the recordings that don't match the search box, and says so when none are left.</summary>
        void FilterList()
        {
            int shown = 0;
            foreach (ListBoxItem item in list.Items)
            {
                bool match = Ui.Matches(listSearch.Text, c.RecordingName((string)item.Tag));
                item.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                if (match) shown++;
            }
            listEmpty.Text = list.Items.Count == 0 ? EmptyText : "No recordings match “" + listSearch.Text.Trim() + "”.";
            listEmpty.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // A selection the search hides would leave the right side open on a row nobody can see.
        void KeepSelectionShown()
        {
            var selected = list.SelectedItem as ListBoxItem;
            if (selected == null || selected.Visibility == Visibility.Visible) return;
            ListBoxItem first = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Visibility == Visibility.Visible);
            if (first != null) list.SelectedItem = first;
        }

        /// <summary>Self-test: the recordings the list shows, by name.</summary>
        public List<string> ShownRecordings
        {
            get { return list.Items.Cast<ListBoxItem>().Where(i => i.Visibility == Visibility.Visible).Select(i => c.RecordingName((string)i.Tag)).ToList(); }
        }

        /// <summary>Self-test: the list's search box.</summary>
        public string ListSearch { get { return listSearch.Text; } set { listSearch.Text = value; } }

        static ListBoxItem MakeItem(string id, string name, int durationMs, int events, string created, bool unsaved)
        {
            var panel = new StackPanel();
            var head = new DockPanel();
            if (unsaved)
            {
                var badge = Ui.Pill("New", "Warn", "WarnBg", 10.5);
                badge.ToolTip = "Not saved yet. Save keeps it, and so do Exit, Restart and recording again by hotkey or Deck. Recording again from this window asks before replacing it.";
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
            duplicateButton.IsEnabled = has;
            // The blue button is the next thing to do: Save for a new take, otherwise Record, or Play once there is something to play.
            primaryNow = (has ? "Play" : "Record") + (pendingSelected ? "+Save" : "");
            Ui.Style(saveButton, pendingSelected ? "SmallPrimary" : "Small");
            Ui.Style(recordButton, has ? "Transport" : "Primary");
            Ui.Style(playButton, has ? "Primary" : "Transport");
            Ui.Ref(recordIcon, System.Windows.Shapes.Shape.FillProperty, has ? "Rec" : "AccentFg");
            Ui.Ref(playIcon, System.Windows.Shapes.Shape.FillProperty, has ? "AccentFg" : "Text");
            Ui.Ref(recordHint, TextBlock.ForegroundProperty, has ? "Muted" : "AccentFg");
            Ui.Ref(playHint, TextBlock.ForegroundProperty, has ? "AccentFg" : "Muted");
            playButton.ToolTip = playButton.IsEnabled ? "Play the selected recording." : idle ? "Pick a recording first." : "Wait until the current recording or playback ends.";
            stopButton.ToolTip = stopButton.IsEnabled ? "Stop recording or playback." : "Nothing is recording or playing.";
            deleteButton.IsEnabled = has;
            exportButton.IsEnabled = has;
            hotkey.IsEnabled = has;
            onDeck.IsEnabled = has;
            convertButton.IsEnabled = has;
        }

        void UpdateHints()
        {
            recordHint.Text = c.Settings.RecordHotkey;
            RecordingOptions own = c.OptionsFor(SelectedId);
            playHint.Text = own != null && !string.IsNullOrEmpty(own.Hotkey) ? own.Hotkey : c.Settings.PlayHotkey;
            stopHint.Text = c.Settings.StopHotkey;
            playHint.Opacity = 0.8;
            recordHint.Opacity = 0.8;
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

        void Duplicate()
        {
            string id = SelectedId;
            Rec source = id == null ? null : c.LoadRecording(id);
            if (source == null) return;
            // A copy of the steps and the speed and repeat settings: it has no hotkey or Deck button, which would clash with the original's.
            var copy = new Rec { Name = source.Name + " copy", Created = Rec.NowIso(), DurationMs = source.DurationMs, Events = new List<RecEvent>(source.Events) };
            try
            {
                c.Storage.SaveRecording(copy);
                RecordingOptions own = c.OptionsFor(id);
                if (own != null && own.Playback != null) c.EnsureOptions(copy.Id).Playback = own.Playback.Copy();
                c.Settings.SelectedRecordingId = copy.Id;
                c.MarkDirty(SaveParts.Settings);
                c.RefreshRecordings();
                c.SetStatus("Made “" + copy.Name + "”.", false);
            }
            catch (Exception ex) { c.SetStatus("Could not copy the recording: " + ex.Message, true); }
        }

        /// <summary>Self-test: presses Duplicate.</summary>
        public void DuplicateSelected() { Duplicate(); }

        /// <summary>Self-test: which buttons are the blue ones, for example Play+Save.</summary>
        public string PrimaryButtons
        {
            get { return primaryNow; }
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
            string id = SelectedId;
            PlaybackSettings p = c.PlaybackFor(id);
            playbackFor.Text = id == null ? "New recordings start with these." : "For “" + c.RecordingName(id) + "”. Each recording keeps its own.";
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
            c.EditPlayback(SelectedId, p => p.Repeat = mode);
        }

        static double SnapSpeed(double v)
        {
            if (Math.Abs(v - 1) < 0.035) return 1;
            return v < 1 ? Math.Round(v, 2) : v < 10 ? Math.Round(v, 1) : Math.Round(v);
        }

        void SetSpeed(double value, bool moveSlider)
        {
            value = Math.Max(0.1, Math.Min(100, value));
            c.EditPlayback(SelectedId, p => p.Speed = value);
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
            double v = c.PlaybackFor(SelectedId).Speed;
            speedValue.Text = (v < 10 ? v.ToString("0.0#", CultureInfo.CurrentCulture) : v.ToString("0", CultureInfo.CurrentCulture)) + "×";
            foreach (var chip in speedChips)
            {
                bool current = Math.Abs(chip.Key - v) < 0.001;
                chip.Value.SetResourceReference(Control.BackgroundProperty, current ? "Selected" : "Card2");
                chip.Value.SetResourceReference(Control.BorderBrushProperty, current ? "Accent" : "Border");
            }
        }

        // ---- this recording

        void LoadOptions()
        {
            loading = true;
            RecordingOptions o = c.OptionsFor(SelectedId);
            hotkey.SetText(o == null ? "" : o.Hotkey);
            onDeck.IsChecked = o != null && o.ShowOnDeck;
            loading = false;
            ShowHotkeyError();
            UpdateHints();
        }

        void ShowHotkeyError()
        {
            string error;
            string id = SelectedId;
            hotkeyError.Text = id != null && c.HotkeyErrors.TryGetValue(AppController.RecordingHotkeyKey(id), out error) ? error : "";
            hotkeyError.Visibility = hotkeyError.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        void ConvertToMacro()
        {
            string id = SelectedId;
            if (id == null) return;
            MacroDef macro = c.ConvertRecordingToMacro(id);
            var main = owner as MainWindow;
            if (macro != null && main != null) main.SelectTab(2);
        }

        /// <summary>Self-test: presses Convert to macro without switching tabs; returns the new macro.</summary>
        public MacroDef ConvertSelected() { return SelectedId == null ? null : c.ConvertRecordingToMacro(SelectedId); }

        /// <summary>Self-test: sets the selected recording's hotkey as if it was typed in the box.</summary>
        public void SetHotkeyForTest(string text) { hotkey.SetText(text); c.SetRecordingHotkey(SelectedId, text); ShowHotkeyError(); }

        /// <summary>Self-test: the error shown under the hotkey box, or "".</summary>
        public string HotkeyErrorText { get { return hotkeyError.Visibility == Visibility.Visible ? hotkeyError.Text : ""; } }

        /// <summary>Self-test: ticks Show on the Deck.</summary>
        public void ShowOnDeckForTest(bool show) { onDeck.IsChecked = show; c.SetRecordingOnDeck(SelectedId, show); }
    }
}
