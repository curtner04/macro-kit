using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MacroKit.Core;
using MacroKit.Win32;

namespace MacroKit.App
{
    /// <summary>The Settings tab: global hotkeys, how Macro Kit behaves around runs, startup, theme and excluded programs.</summary>
    public sealed class SettingsTab
    {
        public readonly FrameworkElement Root;
        readonly AppController c;
        readonly Action themeChanged;
        readonly Dictionary<string, TextBlock> hotkeyErrors = new Dictionary<string, TextBlock>();
        readonly CheckBox startWithWindows;
        readonly ComboBox theme = new ComboBox { Width = 180 };
        readonly TextBox excluded;
        readonly Func<RunPill> pill;
        readonly TextBlock pillPlace = Ui.Text("", "Muted", false, 12, true);
        readonly Button pillReset;
        bool loading;

        public SettingsTab(AppController controller, Action themeChanged, Action showChangelog, Action openDataFolder, Func<RunPill> pill)
        {
            c = controller;
            this.themeChanged = themeChanged;
            this.pill = pill;

            // ---- hotkeys
            var hotkeys = new StackPanel();
            hotkeys.Children.Add(Ui.Heading("Hotkeys"));
            var note = Ui.Para(c.InputEnabled ? "These work from any program. Click a box, then press the keys." : "Hotkeys are off in this session because input is turned off.", "Muted", 12);
            note.Margin = new Thickness(0, 3, 0, 8);
            hotkeys.Children.Add(note);
            AddHotkey(hotkeys, "Record", "Record / stop recording", () => c.Settings.RecordHotkey, v => c.Settings.RecordHotkey = v);
            AddHotkey(hotkeys, "Play", "Play / stop playback", () => c.Settings.PlayHotkey, v => c.Settings.PlayHotkey = v);
            AddHotkey(hotkeys, "Stop", "Stop everything", () => c.Settings.StopHotkey, v => c.Settings.StopHotkey = v);
            AddHotkey(hotkeys, "Deck", "Show or hide the Deck", () => c.Settings.DeckHotkey, v => c.Settings.DeckHotkey = v);
            var hotkeyCard = Ui.Card(hotkeys);

            // ---- behavior
            var esc = new CheckBox { Content = "Esc stops playback and macros", IsChecked = c.Settings.EscStops, ToolTip = "A real Esc key press ends a playback or macro run right away. It never stops a recording." };
            esc.Click += (s, e) => Change(() => c.Settings.EscStops = esc.IsChecked == true);
            var showAfter = new CheckBox { Content = "Show Macro Kit again after a run", IsChecked = c.Settings.ShowWindowAfterRun, ToolTip = "Macro Kit hides while something runs. Off leaves it in the tray afterwards." };
            showAfter.Click += (s, e) => Change(() => c.Settings.ShowWindowAfterRun = showAfter.IsChecked == true);
            var closeToTray = new CheckBox { Content = "Closing the window keeps Macro Kit running in the tray", IsChecked = c.Settings.CloseToTray, ToolTip = "Phrases and hotkeys only work while Macro Kit is running." };
            closeToTray.Click += (s, e) => Change(() => c.Settings.CloseToTray = closeToTray.IsChecked == true);
            startWithWindows = new CheckBox { Content = "Start Macro Kit when I sign in to Windows", ToolTip = "Starts quietly in the tray." };
            startWithWindows.Click += (s, e) => ToggleStartup();
            if (!c.InputEnabled) { startWithWindows.IsEnabled = false; startWithWindows.ToolTip = "Not available in this session."; }
            var deckColumns = new NumberBox(c.Settings.DeckColumns, 1, 8, 50);
            deckColumns.Changed += () => Change(() => c.Settings.DeckColumns = deckColumns.Value);
            theme.Items.Add("Follow Windows");
            theme.Items.Add("Light");
            theme.Items.Add("Dark");
            theme.SelectedIndex = c.Settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? 1 : c.Settings.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
            theme.SelectionChanged += (s, e) =>
            {
                if (loading) return;
                c.Settings.Theme = theme.SelectedIndex == 1 ? "Light" : theme.SelectedIndex == 2 ? "Dark" : "System";
                c.MarkDirty(SaveParts.Settings);
                this.themeChanged();
            };
            var behavior = Ui.Card(Ui.Stack(9,
                Ui.Heading("Behavior"),
                esc, showAfter, closeToTray, startWithWindows,
                Ui.Row(10, Ui.Text("Deck columns", "Muted", false, 12), deckColumns),
                Ui.Row(10, Ui.Text("Theme", "Muted", false, 12), theme)));

            // ---- status popup
            var pillShow = Ui.Button("Show popup", ShowPill, "Small", "Show the status popup now, so you can drag it where you want it. Press Done on it when it is in place.");
            pillReset = Ui.Button("Reset position", ResetPill, "Small", "Put the popup back in the bottom-right corner of the screen you are working on.");
            var popupCard = Ui.Card(Ui.Stack(8,
                Ui.Heading("Status popup"),
                Ui.Para("The popup shows what is running and has a Stop button. Drag it to move it; it stays where you leave it.", "Muted", 12),
                pillPlace,
                Ui.Row(8, pillShow, pillReset)));

            // ---- excluded programs
            excluded = new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 84, MaxHeight = 120, FontFamily = Ui.MonoFont,
                VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8, 6, 8, 6),
                Text = string.Join(Environment.NewLine, c.Settings.ExcludedPrograms),
                ToolTip = "One program per line, for example game.exe."
            };
            excluded.TextChanged += (s, e) =>
            {
                if (loading) return;
                c.Settings.ExcludedPrograms = excluded.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                c.MarkDirty(SaveParts.Settings);
            };
            var excludedCard = Ui.Card(Ui.Stack(6,
                Ui.Heading("Programs where phrases never expand"),
                Ui.Para("One program per line, for example game.exe. Phrases are always off inside Macro Kit itself.", "Muted", 12),
                excluded));

            // ---- about
            string version = "Macro Kit " + Release.Version;
            var data = Ui.Text(c.Storage.DataRoot, "Text", false, 12);
            data.FontFamily = Ui.MonoFont;
            data.TextWrapping = TextWrapping.Wrap;
            data.TextTrimming = TextTrimming.None;
            var links = Ui.Row(8, Ui.Button("Open data folder", openDataFolder, "Small", "Show the folder where your settings, phrases, macros and recordings are kept."),
                Ui.Button("Changelog", showChangelog, "Small", "What changed in each version."));
            string readOnly = c.ReadOnlyNotice();
            var readOnlyNote = Ui.Text(readOnly ?? "", "Bad", false, 12, true);
            readOnlyNote.Visibility = readOnly == null ? Visibility.Collapsed : Visibility.Visible;
            var about = Ui.Card(Ui.Stack(8,
                Ui.Heading("About"),
                readOnlyNote,
                Ui.Text(version, "Muted", false, 12),
                Ui.Text("Your data is kept in", "Muted", false, 12), data,
                links,
                Ui.Para("Windows stops programs from typing into, or watching, programs that run as administrator. To use Macro Kit with those, run Macro Kit as administrator too. Macro Kit warns you when the window it works in runs as administrator.", "Muted", 12)));

            var left = Ui.Stack(0, hotkeyCard, excludedCard, popupCard);
            var right = Ui.Stack(0, behavior, about);
            var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 380 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });
            grid.Children.Add(left);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            Root = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            c.HotkeysChanged += ShowHotkeyErrors;
            c.Edited += parts => { if ((parts & SaveParts.Settings) != 0) ShowPillPlace(); };
            ShowPillPlace();
            RefreshStartup();
            ShowHotkeyErrors();
        }

        void AddHotkey(StackPanel panel, string key, string label, Func<string> get, Action<string> set)
        {
            var box = new HotkeyBox(true, 200);
            box.SetText(get());
            box.Changed += () =>
            {
                set(box.Value.IsEmpty ? "" : box.Value.ToString());
                c.MarkDirty(SaveParts.Settings);
            };
            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(Ui.Text(label, "Text"));
            FrameworkElement field = box.WithClear();
            field.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(field, 1);
            row.Children.Add(field);
            panel.Children.Add(row);
            var error = Ui.Text("", "Bad", false, 12);
            error.TextWrapping = TextWrapping.Wrap;
            error.Margin = new Thickness(170, 3, 0, 0);
            error.Visibility = Visibility.Collapsed;
            hotkeyErrors[key] = error;
            panel.Children.Add(error);
        }

        void ShowPill()
        {
            RunPill p = pill();
            if (p == null) return;
            if (c.Activity.Kind != ActivityKind.None) { c.SetStatus("The popup is already showing for what is running. Drag it from there.", false); return; }
            p.ShowForMoving(Window.GetWindow(Root));
        }

        void ResetPill()
        {
            RunPill p = pill();
            if (p != null) p.ResetPlace();
            ShowPillPlace();
        }

        void ShowPillPlace()
        {
            bool custom = !double.IsNaN(c.Settings.PillLeft);
            pillPlace.Text = custom ? "Position: where you dragged it." : "Position: bottom-right corner of the screen you are working on.";
            pillReset.IsEnabled = custom;
        }

        void ShowHotkeyErrors()
        {
            foreach (var pair in hotkeyErrors)
            {
                string message;
                bool has = c.HotkeyErrors.TryGetValue(pair.Key, out message);
                pair.Value.Text = has ? message : "";
                pair.Value.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        void Change(Action apply)
        {
            if (loading) return;
            apply();
            c.MarkDirty(SaveParts.Settings);
        }

        void RefreshStartup()
        {
            loading = true;
            bool on = c.InputEnabled && StartupEntry.IsEnabled;
            startWithWindows.IsChecked = on;
            if (c.InputEnabled && c.Settings.StartWithWindows != on) { c.Settings.StartWithWindows = on; c.MarkDirty(SaveParts.Settings); }
            loading = false;
        }

        void ToggleStartup()
        {
            bool want = startWithWindows.IsChecked == true;
            try
            {
                if (want) StartupEntry.Enable(Process.GetCurrentProcess().MainModule.FileName); else StartupEntry.Disable();
            }
            catch (Exception ex)
            {
                Log.Error("Changing the startup entry failed", ex);
                c.SetStatus("Could not change the Windows startup entry: " + ex.Message, true);
            }
            RefreshStartup();
        }
    }
}
