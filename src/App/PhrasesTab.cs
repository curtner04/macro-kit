using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MacroKit.Core;

namespace MacroKit.App
{
    /// <summary>The Phrases tab: type a trigger anywhere and it turns into the saved text.</summary>
    public sealed class PhrasesTab
    {
        /// <summary>Lets the self test show the PhraseExpress warning without starting PhraseExpress.</summary>
        public static Func<bool> PhraseExpressRunning = DefaultPhraseExpressCheck;

        public readonly FrameworkElement Root;
        readonly AppController c;
        readonly Window owner;
        readonly CheckBox enabledSwitch;
        readonly FrameworkElement warning;
        readonly ListBox list = new ListBox();
        readonly TextBlock listEmpty;
        readonly Button deleteButton;
        readonly TextBox trigger, replacement;
        readonly RadioButton immediately, afterDelimiter, typeIt, pasteIt;
        readonly CheckBox matchCase, phraseEnabled;
        readonly TextBlock triggerNote;
        readonly FrameworkElement editor;
        readonly DispatcherTimer warningTimer;
        bool loading;

        public PhrasesTab(AppController controller, Window owner)
        {
            c = controller;
            this.owner = owner;

            enabledSwitch = new CheckBox { Content = "Expand phrases as I type" };
            Ui.Style(enabledSwitch, "Switch");
            enabledSwitch.IsChecked = c.Settings.ExpansionEnabled;
            enabledSwitch.Click += (s, e) => c.SetPhrasesEnabled(enabledSwitch.IsChecked == true);
            if (!c.InputEnabled) enabledSwitch.ToolTip = "Typing is not watched in this session (input is turned off).";

            warning = Ui.Note("PhraseExpress is also running. Both programs may expand the same trigger, so you could see the text twice. Close one of them, or use different triggers.", "Warn");
            warning.Margin = new Thickness(0, 10, 0, 0);

            listEmpty = Ui.Para("No phrases yet. Press New, then type a trigger such as /shrug and the text to put in its place.");
            listEmpty.Margin = new Thickness(10, 8, 10, 0);
            list.SelectionChanged += (s, e) => { if (!loading) LoadEditor(); };
            var newButton = Ui.Button("New phrase", NewPhrase, "Small", "Add a phrase.");
            deleteButton = Ui.Button("Delete", Delete, "Small", "Delete the selected phrase.");
            deleteButton.Margin = new Thickness(6, 0, 0, 0);

            var listGrid = new Grid();
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition());
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = Ui.Heading("Phrases");
            heading.Margin = new Thickness(0, 0, 0, 8);
            listGrid.Children.Add(heading);
            var host = new Grid();
            host.Children.Add(list);
            host.Children.Add(listEmpty);
            Grid.SetRow(host, 1);
            listGrid.Children.Add(host);
            var buttons = Ui.Row(0, newButton, deleteButton);
            buttons.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(buttons, 2);
            listGrid.Children.Add(buttons);
            Border listCard = Ui.Card(listGrid);
            listCard.Margin = new Thickness(0, 0, 10, 0);

            // ---- editor
            trigger = new TextBox { FontFamily = Ui.MonoFont, ToolTip = "What you type, for example /shrug. Keep it short but unusual so it doesn't appear in normal words." };
            triggerNote = Ui.Text("", "Warn", false, 12);
            triggerNote.TextWrapping = TextWrapping.Wrap;
            replacement = new TextBox
            {
                FontFamily = Ui.MonoFont, AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap, MinHeight = 130,
                VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8, 6, 8, 6),
                ToolTip = "The text that replaces the trigger. Line breaks are typed as Enter."
            };
            trigger.TextChanged += (s, e) => Apply(p => p.Trigger = trigger.Text);
            replacement.TextChanged += (s, e) => Apply(p => p.Replacement = replacement.Text);

            immediately = new RadioButton { Content = "Right away, as soon as I finish typing the trigger", GroupName = "when" };
            afterDelimiter = new RadioButton { Content = "After I press space, Enter, Tab or punctuation", GroupName = "when" };
            immediately.Checked += (s, e) => Apply(p => p.When = ExpandWhen.Immediately);
            afterDelimiter.Checked += (s, e) => Apply(p => p.When = ExpandWhen.AfterDelimiter);
            afterDelimiter.ToolTip = "The trigger only counts at the start of a word, so it never fires in the middle of one.";
            typeIt = new RadioButton { Content = "Type the text", GroupName = "method" };
            pasteIt = new RadioButton { Content = "Paste the text (faster for long text)", GroupName = "method" };
            typeIt.Checked += (s, e) => Apply(p => p.Method = InsertMethod.Type);
            pasteIt.Checked += (s, e) => Apply(p => p.Method = InsertMethod.Paste);
            var pasteNote = Ui.Para("Pasting puts your clipboard text back afterwards. Pictures and files on the clipboard are not restored.", "Muted", 11.5);
            pasteNote.Margin = new Thickness(26, 0, 0, 0);
            matchCase = new CheckBox { Content = "Match upper and lower case exactly" };
            matchCase.Click += (s, e) => Apply(p => p.MatchCase = matchCase.IsChecked == true);
            phraseEnabled = new CheckBox { Content = "This phrase is on" };
            phraseEnabled.Click += (s, e) => { Apply(p => p.Enabled = phraseEnabled.IsChecked == true); RefreshSelectedItem(); };

            var form = Ui.Stack(10,
                Ui.Field("Trigger", trigger), triggerNote,
                Ui.Field("Replacement", replacement),
                Ui.Stack(6, Ui.Caption("EXPAND"), immediately, afterDelimiter),
                Ui.Stack(6, Ui.Caption("INSERT"), typeIt, pasteIt, pasteNote),
                Ui.Row(22, matchCase, phraseEnabled));
            triggerNote.Margin = new Thickness(0, -4, 0, 0);
            editor = form;
            var editorScroll = new ScrollViewer { Content = Ui.Card(form, new Thickness(16)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            editorScroll.Margin = new Thickness(0);

            var top = Ui.Stack(0, enabledSwitch, warning);
            var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star), MinWidth = 380 });
            Grid.SetColumnSpan(top, 2);
            top.Margin = new Thickness(2, 0, 0, 10);
            grid.Children.Add(top);
            Grid.SetRow(listCard, 1);
            grid.Children.Add(listCard);
            Grid.SetRow(editorScroll, 1);
            Grid.SetColumn(editorScroll, 1);
            grid.Children.Add(editorScroll);
            Root = grid;

            warningTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(8) };
            warningTimer.Tick += (s, e) => UpdateWarning();
            Root.IsVisibleChanged += (s, e) => { if (Root.IsVisible) { UpdateWarning(); warningTimer.Start(); } else warningTimer.Stop(); };

            // The tray menu can switch phrases on or off too.
            c.Edited += parts => { if ((parts & SaveParts.Settings) != 0) enabledSwitch.IsChecked = c.Settings.ExpansionEnabled; };

            Reload(null);
            UpdateWarning();
        }

        PhraseDef Selected
        {
            get { var item = list.SelectedItem as ListBoxItem; return item == null ? null : item.Tag as PhraseDef; }
        }

        void Reload(PhraseDef select)
        {
            loading = true;
            list.Items.Clear();
            foreach (PhraseDef p in c.Phrases) list.Items.Add(MakeItem(p));
            ListBoxItem match = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, select)) ?? (list.Items.Count > 0 ? (ListBoxItem)list.Items[0] : null);
            list.SelectedItem = match;
            listEmpty.Visibility = list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            loading = false;
            LoadEditor();
        }

        static ListBoxItem MakeItem(PhraseDef p)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star), MinWidth = 70 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
            string name = p.Trigger.Length == 0 ? "(no trigger yet)" : p.Trigger;
            var t = Ui.Text(name, p.Enabled && p.Trigger.Length > 0 ? "AccentText" : "Muted", true);
            t.FontFamily = Ui.MonoFont;
            t.Margin = new Thickness(0, 0, 10, 0);
            string first = (p.Replacement ?? "").Replace("\r", "").Split('\n')[0];
            var r = Ui.Text(first.Length == 0 ? "(empty)" : first, p.Enabled ? "Text" : "Muted");
            Grid.SetColumn(r, 1);
            grid.Children.Add(t);
            grid.Children.Add(r);
            if (!p.Enabled) grid.Opacity = 0.65;
            return new ListBoxItem { Content = grid, Tag = p };
        }

        void RefreshSelectedItem()
        {
            var item = list.SelectedItem as ListBoxItem;
            PhraseDef p = Selected;
            if (item == null || p == null) return;
            int index = list.Items.IndexOf(item);
            loading = true;
            list.Items[index] = MakeItem(p);
            list.SelectedIndex = index;
            loading = false;
        }

        void LoadEditor()
        {
            PhraseDef p = Selected;
            editor.IsEnabled = p != null;
            deleteButton.IsEnabled = p != null;
            loading = true;
            if (p == null)
            {
                trigger.Text = "";
                replacement.Text = "";
                triggerNote.Text = "";
                triggerNote.Visibility = Visibility.Collapsed;
            }
            else
            {
                trigger.Text = p.Trigger;
                replacement.Text = p.Replacement;
                immediately.IsChecked = p.When == ExpandWhen.Immediately;
                afterDelimiter.IsChecked = p.When == ExpandWhen.AfterDelimiter;
                typeIt.IsChecked = p.Method == InsertMethod.Type;
                pasteIt.IsChecked = p.Method == InsertMethod.Paste;
                matchCase.IsChecked = p.MatchCase;
                phraseEnabled.IsChecked = p.Enabled;
                UpdateTriggerNote(p);
            }
            loading = false;
        }

        void UpdateTriggerNote(PhraseDef p)
        {
            if (p.Trigger.Length == 0) triggerNote.Text = "Type the trigger first; a phrase without one does nothing.";
            else if (p.Trigger.Trim().Length != p.Trigger.Length) triggerNote.Text = "The trigger starts or ends with a space. That works, but make sure it is what you want.";
            else if (c.Phrases.Any(o => !ReferenceEquals(o, p) && o.Enabled && o.Trigger.Equals(p.Trigger, p.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)))
                triggerNote.Text = "Another phrase uses the same trigger. Only one of them will expand.";
            else triggerNote.Text = "";
            triggerNote.Visibility = triggerNote.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        void Apply(Action<PhraseDef> change)
        {
            PhraseDef p = Selected;
            if (loading || p == null) return;
            change(p);
            UpdateTriggerNote(p);
            c.MarkDirty(SaveParts.Phrases);
            var item = (ListBoxItem)list.SelectedItem;
            // Rebuild only the list row, not the editor (the caret would jump).
            int index = list.Items.IndexOf(item);
            loading = true;
            list.Items[index] = MakeItem(p);
            list.SelectedIndex = index;
            loading = false;
        }

        void NewPhrase()
        {
            var p = new PhraseDef();
            c.Phrases.Add(p);
            c.MarkDirty(SaveParts.Phrases);
            Reload(p);
            trigger.Focus();
        }

        void Delete()
        {
            PhraseDef p = Selected;
            if (p == null) return;
            string name = p.Trigger.Length == 0 ? "this phrase" : "“" + p.Trigger + "”";
            if (!Ui.Confirm(owner, "Delete " + name + "?")) return;
            int index = c.Phrases.IndexOf(p);
            c.Phrases.Remove(p);
            c.MarkDirty(SaveParts.Phrases);
            Reload(c.Phrases.Count == 0 ? null : c.Phrases[Math.Min(index, c.Phrases.Count - 1)]);
        }

        void UpdateWarning()
        {
            bool running = false;
            try { running = PhraseExpressRunning(); } catch (Exception) { }
            warning.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        }

        static bool DefaultPhraseExpressCheck()
        {
            Process[] found = Process.GetProcessesByName("PhraseExpress");
            foreach (Process p in found) p.Dispose();
            return found.Length > 0;
        }

        /// <summary>Selects a phrase by index (self test).</summary>
        public void Select(int index)
        {
            if (index >= 0 && index < list.Items.Count) list.SelectedIndex = index;
        }

        public void RefreshWarning() { UpdateWarning(); }
    }
}
