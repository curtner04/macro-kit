using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        readonly ComboBox pillAnchor = new ComboBox { Width = 180 };
        readonly StackPanel edition = new StackPanel();
        bool loading;

        public SettingsTab(AppController controller, Action themeChanged, Action showChangelog, Action openDataFolder, Func<RunPill> pill)
        {
            c = controller;
            this.themeChanged = themeChanged;
            this.pill = pill;

            // ---- hotkeys
            var hotkeys = new StackPanel();
            hotkeys.Children.Add(Ui.Heading("Hotkeys"));
            var note = Ui.Para(c.InputEnabled
                ? "These work from any program. Click a box, then press the keys. Shorter ones such as Ctrl+Alt+1 work too, and so does a lone F13 to F24 key from a programmable keyboard or mouse."
                : "Hotkeys are off in this session because input is turned off.", "Muted", 12);
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
            deckColumns.ToolTip = "How many buttons per row on the Deck, the small always-on-top button panel.";
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
                LabelRows(new KeyValuePair<string, UIElement>("Deck columns", deckColumns), new KeyValuePair<string, UIElement>("Theme", theme))));

            // ---- status popup
            var pillShow = Ui.Button("Show popup", ShowPill, "Small", "Show the status popup now, so you can drag it where you want it. Press Done on it when it is in place.");
            pillReset = Ui.Button("Reset position", ResetPill, "Small", "Put the popup back in the bottom-right corner of the screen you are working on.");
            // 1.13.2: which corner stays put when the text makes the popup wider or taller, so it never reaches past that spot.
            foreach (string label in RunPill.AnchorLabels) pillAnchor.Items.Add(label);
            pillAnchor.SelectedIndex = Math.Max(0, Array.IndexOf(RunPill.AnchorValues, c.Settings.PillAnchor ?? ""));
            pillAnchor.ToolTip = "The corner of the popup that stays where you put it. The popup grows away from it, so it never reaches past that corner onto another screen.";
            System.Windows.Automation.AutomationProperties.SetName(pillAnchor, "Keep this corner in place");
            pillAnchor.SelectionChanged += (s, e) =>
            {
                if (loading || pillAnchor.SelectedIndex < 0) return;
                c.Settings.PillAnchor = RunPill.AnchorValues[pillAnchor.SelectedIndex];
                c.MarkDirty(SaveParts.Settings);
                RunPill p = pill();
                if (p != null) p.PlaceChanged();
                ShowPillPlace();
            };
            var popupCard = Ui.Card(Ui.Stack(8,
                Ui.Heading("Status popup"),
                Ui.Para("The popup shows what is running and has a Stop button. Drag it to move it; it stays where you leave it.", "Muted", 12),
                pillPlace,
                LabelRows(new KeyValuePair<string, UIElement>("Fixed corner", pillAnchor)),
                Ui.Row(8, pillShow, pillReset)));

            // ---- excluded programs
            excluded = new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 84, MaxHeight = 120, FontFamily = Ui.MonoFont,
                VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8, 6, 8, 6),
                Text = string.Join(Environment.NewLine, c.Settings.ExcludedPrograms),
                ToolTip = "One program per line, for example game.exe."
            };
            System.Windows.Automation.AutomationProperties.SetName(excluded, "Programs where phrases never expand");
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
            // The path in full (%LOCALAPPDATA% shortened), wrapped at its backslashes rather than cut off, with Open and Copy beside it.
            string root = c.Storage.DataRoot ?? "";
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string shortRoot = local.Length > 0 && root.StartsWith(local, StringComparison.OrdinalIgnoreCase) ? "%LOCALAPPDATA%" + root.Substring(local.Length) : root;
            dataPath = Ui.Text(shortRoot.Replace(@"\", "\\" + ZeroWidthSpace), "Text", false, 12, true);   // a zero-width space after each \ lets it wrap there
            dataPath.FontFamily = Ui.MonoFont;
            dataPath.ToolTip = root;
            System.Windows.Automation.AutomationProperties.SetName(dataPath, root);
            var pathButtons = Ui.Row(8,
                Ui.Button("Open folder", openDataFolder, "Small", "Show the folder where your settings, phrases, macros and recordings are kept."),
                Ui.Button("Copy path", () => CopyDataPath(root), "Small", "Copy the folder's full path, to paste into Explorer or a message."));
            var links = Ui.Row(8,
                Ui.Button("Changelog", showChangelog, "Small", "What changed in each version."),
                Ui.Button("Check input", () => new InputCheckWindow(Window.GetWindow(Root), c).ShowDialog(), "Small", "Checks that Windows lets Macro Kit send keys and see your typing on this PC."));
            string readOnly = c.ReadOnlyNotice();
            var readOnlyNote = Ui.Text(readOnly ?? "", "Bad", false, 12, true);
            readOnlyNote.Visibility = readOnly == null ? Visibility.Collapsed : Visibility.Visible;
            var about = Ui.Card(Ui.Stack(8,
                Ui.Heading("About"),
                readOnlyNote,
                edition,
                Ui.Text("Your data is kept in", "Muted", false, 12), dataPath,
                pathButtons,
                links,
                Ui.Help("To use Macro Kit with programs that run as administrator, run it as administrator too.",
                    "Windows stops programs from typing into, or watching, programs that run as administrator. Macro Kit warns you when the window it works in runs as administrator.")));

            // The macro editor: typed lines or key tiles. Asked once on the first launch.
            var typed = new RadioButton { Content = "Lines: one step per line, like ctrl+c or wait 50ms", GroupName = "macroEditor" };
            var tiles = new RadioButton { Content = "Key tiles: build it from tiles like Ctrl + C, with a + button", GroupName = "macroEditor" };
            typed.IsChecked = EditorKinds.Normal(c.Settings.MacroEditor) == EditorKinds.Text;
            tiles.IsChecked = !typed.IsChecked;
            typed.Checked += (s, e) => Change(() => c.Settings.MacroEditor = EditorKinds.Text);
            tiles.Checked += (s, e) => Change(() => c.Settings.MacroEditor = EditorKinds.Tiles);
            c.Edited += parts =>
            {
                if ((parts & SaveParts.Settings) == 0) return;
                bool t = EditorKinds.Normal(c.Settings.MacroEditor) == EditorKinds.Tiles;
                loading = true;
                if (tiles.IsChecked != t) tiles.IsChecked = t;
                if (typed.IsChecked == t) typed.IsChecked = !t;
                loading = false;
            };
            var editorCard = Ui.Card(Ui.Stack(8,
                Ui.Heading("Macro editor"),
                Ui.Para("How the Macros tab shows a macro's steps. Both make the same macros, so you can switch whenever you like.", "Muted", 12),
                typed, tiles));

            var left = Ui.Stack(0, hotkeyCard, excludedCard, popupCard);
            var right = Ui.Stack(0, behavior, editorCard, about);
            var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 380 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });
            grid.Children.Add(left);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            Root = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            ShowEdition();
            Edition.Changed += ShowEdition;
            c.HotkeysChanged += ShowHotkeyErrors;
            c.Edited += parts => { if ((parts & SaveParts.Settings) != 0) ShowPillPlace(); };
            ShowPillPlace();
            RefreshStartup();
            ShowHotkeyErrors();
        }

        // Labels in one column and their controls in another, so the controls line up.
        static FrameworkElement LabelRows(params KeyValuePair<string, UIElement>[] rows)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < rows.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var control = (FrameworkElement)rows[i].Value;
                if (string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(control))) System.Windows.Automation.AutomationProperties.SetName(control, rows[i].Key);
                var label = Ui.Text(rows[i].Key, "Muted", false, 12);
                label.Margin = new Thickness(0, i == 0 ? 0 : 9, 0, 0);
                label.ToolTip = control.ToolTip;
                Grid.SetRow(label, i);
                grid.Children.Add(label);
                control.HorizontalAlignment = HorizontalAlignment.Left;
                control.Margin = new Thickness(0, i == 0 ? 0 : 9, 0, 0);
                Grid.SetRow(control, i);
                Grid.SetColumn(control, 1);
                grid.Children.Add(control);
            }
            return grid;
        }

        const char ZeroWidthSpace = '​';
        TextBlock dataPath;

        /// <summary>The About card's data folder as shown, without the zero-width wrap points.</summary>
        public string DataPathShown { get { return dataPath == null ? "" : dataPath.Text.Replace(ZeroWidthSpace.ToString(), ""); } }

        void CopyDataPath(string root)
        {
            try
            {
                Clipboard.SetText(root);
                c.SetStatus("Copied the data folder's path.");
            }
            catch (Exception ex) { c.SetStatus("Could not copy the path: " + ex.Message, true); }   // another program can hold the clipboard
        }

        void AddHotkey(StackPanel panel, string key, string label, Func<string> get, Action<string> set)
        {
            var box = new HotkeyBox(true, 200);
            box.SetText(get());
            // The Recorder's first-run card can change these too; a box being typed in is left alone.
            c.Edited += parts => { if ((parts & SaveParts.Settings) != 0 && !box.IsKeyboardFocusWithin) box.SetText(get()); };
            System.Windows.Automation.AutomationProperties.SetName(box, label);
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

        // Version, Free or Pro badge, and the license buttons. Rebuilt when a key is entered or removed.
        void ShowEdition()
        {
            edition.Children.Clear();
            var badge = Edition.IsPro ? Ui.Pill("PRO", "Good", "GoodBg") : Ui.Pill("FREE", "Info", "InfoBg");
            badge.VerticalAlignment = VerticalAlignment.Center;
            var title = Ui.Row(8, Ui.Text("Macro Kit " + Release.Version, "Text", true, 13), badge);
            edition.Children.Add(title);
            if (Edition.IsPro)
            {
                var who = Ui.Text(Edition.License.Describe(), "Muted", false, 12, true);
                who.Margin = new Thickness(0, 4, 0, 6);
                edition.Children.Add(who);
                edition.Children.Add(Ui.Row(8, Ui.Button("Remove license key", () =>
                {
                    if (Ui.Confirm(Window.GetWindow(Root), "Remove the license key from this PC? Conditions and schedules stop working until you enter it again.")) c.RemoveLicense();
                }, "Small", "Take the key off this PC, for example before you give the PC away.")));
            }
            else
            {
                var pitch = Ui.Help("Everything you see works free, forever.",
                    "Macro Kit Pro adds conditions in macros and scheduled macros, and supports the work on Macro Kit.");
                pitch.Margin = new Thickness(0, 4, 0, 6);
                edition.Children.Add(pitch);
                edition.Children.Add(Ui.Row(8,
                    Ui.Button("Enter license key", () => new LicenseWindow(Window.GetWindow(Root), c).ShowDialog(), "Small", "Paste the key from your purchase email."),
                    ProPage.GetProButton()));
            }
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
                if (want) StartupEntry.Enable(Path.Combine(AppShared.LaunchReadiness.InstallationRoot(AppDomain.CurrentDomain.BaseDirectory), "Macro Kit.exe"));
                else StartupEntry.Disable();
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
