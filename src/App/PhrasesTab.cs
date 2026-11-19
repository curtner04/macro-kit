using System;
using System.Collections.Generic;
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
        readonly TextBox listSearch = new TextBox();
        readonly Button deleteButton, undoButton, removeExamples;
        readonly ComboBox group;
        // What Undo puts back: the phrase just deleted, or the examples just removed, at their old places.
        List<KeyValuePair<int, PhraseDef>> undoItems = new List<KeyValuePair<int, PhraseDef>>();
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
            var newButton = Ui.Button("New phrase", NewPhrase, null, "Add a phrase.");
            deleteButton = Ui.Button("Delete", Delete, "Ghost", "Delete the selected phrase. You can undo it.");
            undoButton = Ui.Button("Undo delete", UndoDelete, "Ghost", "Put back the phrase you just deleted.");
            undoButton.Visibility = Visibility.Collapsed;
            undoButton.Margin = new Thickness(6, 0, 0, 0);
            removeExamples = Ui.Button("Remove examples", RemoveExamples, "Small", "Remove the example phrases you have not changed. You can undo it.");
            removeExamples.VerticalAlignment = VerticalAlignment.Center;

            var listGrid = new Grid();
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition());
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(removeExamples, Dock.Right);
            heading.Children.Add(removeExamples);
            heading.Children.Add(Ui.Heading("Phrases"));
            listGrid.Children.Add(heading);
            listSearch.ToolTip = "Show only the phrases whose trigger or text holds these words.";
            listSearch.TextChanged += (s, e) => FilterList();
            var searchBox = Ui.Hinted(listSearch, "Search phrases");
            searchBox.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(searchBox, 1);
            listGrid.Children.Add(searchBox);
            var host = new Grid();
            host.Children.Add(list);
            host.Children.Add(listEmpty);
            Grid.SetRow(host, 2);
            listGrid.Children.Add(host);
            var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(newButton);
            buttons.Children.Add(undoButton);
            DockPanel.SetDock(deleteButton, Dock.Right);
            buttons.Children.Add(deleteButton);
            Grid.SetRow(buttons, 3);
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

            group = new ComboBox { IsEditable = true, IsTextSearchEnabled = false, ToolTip = "Phrases with the same group are listed together. Leave it empty for no group." };
            group.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => Apply(p => p.Group = (group.Text ?? "").Trim())));
            group.LostKeyboardFocus += (s, e) => { if (!loading && !group.IsKeyboardFocusWithin) RegroupKeepingSelection(); };
            group.SelectionChanged += (s, e) => { if (!loading && group.SelectedItem is string) Dispatcher.CurrentDispatcher.BeginInvoke(new Action(RegroupKeepingSelection)); };

            var placeholderNote = Ui.Para("Put in the date, the time, what you copied, where the caret ends up, or a fill-in you are asked for: " + MacroKit.Phrases.Placeholders.Help + ".", "Muted", 11.5);
            placeholderNote.TextWrapping = TextWrapping.Wrap;
            var form = Ui.Stack(10,
                Ui.Field("Trigger", trigger), triggerNote,
                Ui.Field("Replacement", replacement), placeholderNote,
                Ui.Field("Group (optional)", group),
                Ui.Stack(6, Ui.Caption("EXPAND"), immediately, afterDelimiter),
                Ui.Stack(6, Ui.Caption("INSERT"), typeIt, pasteIt, pasteNote),
                Ui.Row(22, matchCase, phraseEnabled));
            triggerNote.Margin = new Thickness(0, -4, 0, 0);
            placeholderNote.Margin = new Thickness(0, -4, 0, 0);
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
            c.LibraryAdded += (macro, phrase) => { if (phrase != null) { listSearch.Text = ""; Reload(phrase); } };

            Reload(null);
            UpdateWarning();
        }

        PhraseDef Selected
        {
            get { var item = list.SelectedItem as ListBoxItem; return item == null ? null : item.Tag as PhraseDef; }
        }

        void Reload(PhraseDef select)
        {
            removeExamples.Visibility = c.Phrases.Any(Examples.IsUnchanged) ? Visibility.Visible : Visibility.Collapsed;
            loading = true;
            list.Items.Clear();
            // Phrases without a group first, in their saved order, then each group under its heading, A to Z.
            foreach (IGrouping<string, PhraseDef> g in c.Phrases.GroupBy(GroupOf, StringComparer.OrdinalIgnoreCase)
                                                         .OrderBy(g => g.Key.Length == 0 ? 0 : 1).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                if (g.Key.Length > 0) list.Items.Add(MakeHeader(g.Key));
                foreach (PhraseDef p in g) list.Items.Add(MakeItem(p));
            }
            ListBoxItem match = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, select)) ?? list.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Tag is PhraseDef);
            list.SelectedItem = match;
            loading = false;
            FilterList();
            LoadEditor();
        }

        /// <summary>Hides the phrases that don't match the search box, and says so when none are left.</summary>
        void FilterList()
        {
            int shown = 0;
            ListBoxItem header = null;
            bool headerUsed = false;
            foreach (ListBoxItem item in list.Items)
            {
                var p = item.Tag as PhraseDef;
                if (p == null)
                {
                    if (header != null) header.Visibility = headerUsed ? Visibility.Visible : Visibility.Collapsed;
                    header = item;
                    headerUsed = false;
                    continue;
                }
                bool match = Ui.Matches(listSearch.Text, p.Trigger, p.Replacement, p.Group);
                item.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                if (match) { shown++; headerUsed = true; }
            }
            if (header != null) header.Visibility = headerUsed ? Visibility.Visible : Visibility.Collapsed;
            listEmpty.Text = c.Phrases.Count == 0
                ? "No phrases yet. Press New, then type a trigger such as /shrug and the text to put in its place."
                : "No phrases match “" + listSearch.Text.Trim() + "”.";
            listEmpty.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Self-test: the list's search box.</summary>
        public string ListSearch { get { return listSearch.Text; } set { listSearch.Text = value; } }

        /// <summary>Self-test: the triggers of the phrases the list shows.</summary>
        public List<string> ShownPhrases
        {
            get { return list.Items.Cast<ListBoxItem>().Where(i => i.Visibility == Visibility.Visible && i.Tag is PhraseDef).Select(i => ((PhraseDef)i.Tag).Trigger).ToList(); }
        }

        /// <summary>Self-test: the group headings the list shows, in order.</summary>
        public List<string> ShownGroups
        {
            get { return list.Items.Cast<ListBoxItem>().Where(i => i.Visibility == Visibility.Visible && i.Tag is string).Select(i => (string)i.Tag).ToList(); }
        }

        /// <summary>Self-test: sets the selected phrase's group as if typed, then leaves the box.</summary>
        public void SetGroup(string name) { group.Text = name; RegroupKeepingSelection(); }

        /// <summary>Self-test: deletes the selected phrase; true when Undo delete is offered.</summary>
        public bool DeleteSelected() { Delete(); return undoButton.Visibility == Visibility.Visible; }

        /// <summary>Self-test: presses Undo delete.</summary>
        public void Undo() { UndoDelete(); }

        static string GroupOf(PhraseDef p) { return (p.Group ?? "").Trim(); }

        static ListBoxItem MakeHeader(string name)
        {
            var t = Ui.Text(name.ToUpperInvariant(), "Muted", true, 11);
            t.Margin = new Thickness(0, 6, 0, 0);
            return new ListBoxItem { Content = t, Tag = name, Focusable = false, IsHitTestVisible = false, Padding = new Thickness(6, 4, 6, 2) };
        }

        void RegroupKeepingSelection()
        {
            PhraseDef p = Selected;
            if (p == null) return;
            string typed = (group.Text ?? "").Trim();
            if (typed != GroupOf(p)) Apply(x => x.Group = typed);
            Reload(p);
            if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
        }

        void FillGroupChoices(PhraseDef p)
        {
            group.Items.Clear();
            foreach (string g in c.Phrases.Select(GroupOf).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase))
                group.Items.Add(g);
            group.Text = GroupOf(p);
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
            ListBoxItem fresh = MakeItem(p);
            fresh.Visibility = item.Visibility;
            list.Items[index] = fresh;
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
                group.Text = "";
                triggerNote.Text = "";
                triggerNote.Visibility = Visibility.Collapsed;
            }
            else
            {
                trigger.Text = p.Trigger;
                replacement.Text = p.Replacement;
                FillGroupChoices(p);
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
            ListBoxItem fresh = MakeItem(p);
            fresh.Visibility = item.Visibility;
            list.Items[index] = fresh;
            list.SelectedIndex = index;
            loading = false;
        }

        void NewPhrase()
        {
            PhraseDef at = Selected;
            var p = new PhraseDef { Group = at == null ? "" : GroupOf(at) };
            c.Phrases.Add(p);
            c.MarkDirty(SaveParts.Phrases);
            Reload(p);
            trigger.Focus();
        }

        void Delete()
        {
            PhraseDef p = Selected;
            if (p == null) return;
            string name = p.Trigger.Length == 0 ? "the phrase" : "“" + p.Trigger + "”";
            int index = c.Phrases.IndexOf(p);
            c.Phrases.Remove(p);
            c.MarkDirty(SaveParts.Phrases);
            undoItems = new List<KeyValuePair<int, PhraseDef>> { new KeyValuePair<int, PhraseDef>(index, p) };
            undoButton.Content = "Undo delete";
            undoButton.Visibility = Visibility.Visible;
            Reload(c.Phrases.Count == 0 ? null : c.Phrases[Math.Min(index, c.Phrases.Count - 1)]);
            c.SetStatus("Deleted " + name + ". Press Undo delete to put it back.", false);
        }

        void UndoDelete()
        {
            List<KeyValuePair<int, PhraseDef>> items = undoItems;
            undoItems = new List<KeyValuePair<int, PhraseDef>>();
            undoButton.Visibility = Visibility.Collapsed;
            if (items.Count == 0) return;
            foreach (var item in items.OrderBy(i => i.Key)) c.Phrases.Insert(Math.Min(Math.Max(0, item.Key), c.Phrases.Count), item.Value);
            c.MarkDirty(SaveParts.Phrases);
            listSearch.Text = "";
            PhraseDef p = items[0].Value;
            Reload(p);
            c.SetStatus(items.Count > 1 ? "Put back " + items.Count + " example phrases." : "Put back " + (p.Trigger.Length == 0 ? "the phrase" : "“" + p.Trigger + "”") + ".", false);
        }

        /// <summary>Removes every example phrase still as shipped in one go; Undo puts them all back.</summary>
        void RemoveExamples()
        {
            var items = c.Phrases.Select((p, i) => new KeyValuePair<int, PhraseDef>(i, p)).Where(x => Examples.IsUnchanged(x.Value)).ToList();
            if (items.Count == 0) return;
            foreach (var item in items) c.Phrases.Remove(item.Value);
            c.MarkDirty(SaveParts.Phrases);
            undoItems = items;
            undoButton.Content = "Undo remove";
            undoButton.Visibility = Visibility.Visible;
            Reload(c.Phrases.Count == 0 ? null : c.Phrases[0]);
            c.SetStatus("Removed " + items.Count + " example phrases. Press Undo remove to put them back.", false);
        }

        /// <summary>Self-test: presses Remove examples; returns how many phrases it removed.</summary>
        public int RemoveExampleItems() { int before = c.Phrases.Count; RemoveExamples(); return before - c.Phrases.Count; }

        /// <summary>Self-test: true while Remove examples is offered.</summary>
        public bool OffersRemoveExamples { get { return removeExamples.Visibility == Visibility.Visible; } }

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
