using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MacroKit.Core;
using MacroKit.Keybinds;
using MacroKit.Win32;

namespace MacroKit.App
{
    /// <summary>
    /// The Global Keybinds tab: every keyboard shortcut saved in the person's apps, whether each one works in any window or only
    /// while its app is the active window, which ones get in each other's way, and a box that says whether keys are still free.
    /// Nothing here changes another app; the lists are read from each app's own settings file whenever the tab is shown.
    /// </summary>
    public sealed class KeybindsTab
    {
        /// <summary>Reads the catalog. The self test swaps in fixture files.</summary>
        public Func<KeybindCatalog> Load;
        /// <summary>Whether an app is running. The self test fixes this so pictures do not depend on the PC.</summary>
        public Func<AppKeybinds, bool?> IsRunning = DefaultIsRunning;
        /// <summary>Asks Windows whether keys are free; null = not asked.</summary>
        public Func<Hotkey, bool?> Probe;

        public readonly FrameworkElement Root;
        readonly AppController c;
        readonly HotkeyBox checkBox = new HotkeyBox(false, 220);
        readonly Border verdict = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 8, 11, 8), Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        readonly TextBlock verdictText = Ui.Text("", "Text", false, 12.5, true);
        readonly StackPanel apps = new StackPanel();
        readonly TextBox search = new TextBox { MinWidth = 180, ToolTip = "Search keys, apps or actions. Type keys in any order: ctrl alt s lists every shortcut that uses Ctrl, Alt and S; alt ctrl shift lists every Ctrl+Shift+Alt one." };
        readonly ComboBox scopeFilter = new ComboBox { Width = 190 };
        readonly ComboBox appFilter = new ComboBox { Width = 170 };
        readonly ComboBox sort = new ComboBox { Width = 130 };
        readonly CheckBox builtIn = new CheckBox { Content = "Built-in shortcuts", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Also list shortcuts that are built into each app (such as Ctrl+S to save), not only the ones saved in its settings." };
        readonly ListBox list = new ListBox();
        readonly TextBlock listEmpty = Ui.Para("No shortcuts match.");
        readonly TextBlock summary = Ui.Text("", "Muted", false, 12);
        KeybindCatalog catalog;
        Dictionary<Keybind, string> clashNotes = new Dictionary<Keybind, string>();
        bool loading;

        public KeybindsTab(AppController controller)
        {
            c = controller;
            Load = () => KeybindCatalog.Read(KeybindLocations.Default(), c.Settings, c.Macros, c.KeybindApps, KeybindCatalog.ReadFile);
            if (c.InputEnabled) Probe = HotkeyProbe.IsFree;

            // ---- "Is it free?"
            checkBox.ReleasesHotkeys = true;   // Macro Kit's own hotkeys must not run while you press keys here
            checkBox.Placeholder = "Click, then press keys";
            checkBox.Changed += () => Check(checkBox.Value, true);
            var clear = Ui.GlyphButton(Ui.GlyphClose, "Clear", () => { checkBox.SetText(""); verdict.Visibility = Visibility.Collapsed; });
            clear.Margin = new Thickness(6, 0, 0, 0);
            var boxRow = Ui.Row(0, checkBox, clear);
            var verdictGrid = new Grid();
            verdict.Child = verdictText;
            var checkCard = Ui.Card(Ui.Stack(6,
                Ui.Heading("Is it free?"),
                Ui.Para("Press a key combination to see which of your apps already use it, and whether it needs that app to be the active window.", "Muted", 12),
                boxRow,
                verdict,
                Ui.Para("Keys another app grabs in every window (such as Snipline's PrtSc) may run that app instead of appearing here. Type them in the search box instead, for example ctrl+prtsc.", "Muted", 11.5)));

            // ---- apps
            var refresh = Ui.IconButton(Ui.GlyphRefresh, "Refresh", Refresh, "Small", "Read every app's shortcuts again.");
            var add = Ui.IconButton(Ui.GlyphAdd, "Add app", () => EditApp(null), "Small", "Add another program and type in its shortcuts, so they count when you check whether keys are free.");
            add.Margin = new Thickness(0, 0, 6, 0);
            var appsHeader = new Grid();
            appsHeader.ColumnDefinitions.Add(new ColumnDefinition());
            appsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            appsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            appsHeader.Children.Add(Ui.Heading("Apps"));
            Grid.SetColumn(add, 1);
            appsHeader.Children.Add(add);
            Grid.SetColumn(refresh, 2);
            appsHeader.Children.Add(refresh);
            var appsHint = Ui.Para("Missing a program? Add app lets you type in its shortcuts.", "Muted", 11.5);
            var appsCard = Ui.Card(Ui.Stack(8, appsHeader, apps, appsHint));

            var left = Ui.Stack(0, checkCard, appsCard);
            var leftScroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 10, 0) };

            // ---- shortcut list
            scopeFilter.Items.Add("Any window and in-app");
            scopeFilter.Items.Add("Any window (global)");
            scopeFilter.Items.Add("Only when the app is active");
            scopeFilter.SelectedIndex = 0;
            sort.Items.Add("Sort by app");
            sort.Items.Add("Sort by keys");
            sort.SelectedIndex = 0;
            appFilter.Items.Add("All apps");
            appFilter.SelectedIndex = 0;
            search.TextChanged += (s, e) => { if (!loading) { Fill(); CheckTyped(); } };
            scopeFilter.SelectionChanged += (s, e) => { if (!loading) Fill(); };
            appFilter.SelectionChanged += (s, e) => { if (!loading) Fill(); };
            sort.SelectionChanged += (s, e) => { if (!loading) Fill(); };
            builtIn.Click += (s, e) => Fill();
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            VirtualizingPanel.SetIsVirtualizing(list, true);

            var filters = Ui.WrapRow(8, Ui.Field("Search", search), Ui.Field("Show", scopeFilter), Ui.Field("App", appFilter), Ui.Field("Order", sort), builtIn);
            var heading = Ui.Heading("Shortcuts");
            var headRow = new Grid();
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headRow.ColumnDefinitions.Add(new ColumnDefinition());
            headRow.Children.Add(heading);
            summary.Margin = new Thickness(12, 3, 0, 0);
            Grid.SetColumn(summary, 1);
            headRow.Children.Add(summary);

            var columns = RowGrid();
            AddCell(columns, Ui.Text("Keys", "Muted", true, 11.5), 0);
            AddCell(columns, Ui.Text("App", "Muted", true, 11.5), 1);
            AddCell(columns, Ui.Text("Action", "Muted", true, 11.5), 2);
            AddCell(columns, Ui.Text("Works in", "Muted", true, 11.5), 3);
            columns.Margin = new Thickness(10, 8, 26, 4);

            listEmpty.Margin = new Thickness(10, 8, 10, 0);
            listEmpty.Visibility = Visibility.Collapsed;
            var host = new Grid();
            host.Children.Add(list);
            host.Children.Add(listEmpty);

            var listGrid = new Grid();
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition());
            listGrid.Children.Add(headRow);
            filters.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(filters, 1); listGrid.Children.Add(filters);
            Grid.SetRow(columns, 2); listGrid.Children.Add(columns);
            Grid.SetRow(host, 3); listGrid.Children.Add(host);
            var listCard = Ui.Card(listGrid);

            var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 420 });
            grid.Children.Add(leftScroll);
            Grid.SetColumn(listCard, 1);
            grid.Children.Add(listCard);
            Root = grid;

            c.Edited += parts =>
            {
                if ((parts & (SaveParts.Settings | SaveParts.Macros | SaveParts.Keybinds)) != 0 && Root.IsVisible && catalog != null) Refresh();
            };
        }

        // ------------------------------------------------------------------ loading

        /// <summary>Reads every app again (called when the tab is shown and by Refresh).</summary>
        public void Refresh()
        {
            try { catalog = Load(); }
            catch (Exception ex)
            {
                Log.Error("Reading the other apps' shortcuts failed", ex);
                c.SetStatus("Could not read the apps' shortcuts: " + ex.Message, true);
                catalog = new KeybindCatalog();
            }
            clashNotes = new Dictionary<Keybind, string>();
            foreach (Keybind k in catalog.All)
            {
                string note = catalog.ClashNote(k);
                if (note != null) clashNotes[k] = note;
            }

            loading = true;
            string chosen = appFilter.SelectedItem as string;
            appFilter.Items.Clear();
            appFilter.Items.Add("All apps");
            foreach (AppKeybinds app in catalog.Apps) appFilter.Items.Add(app.App);
            appFilter.SelectedItem = chosen != null && appFilter.Items.Contains(chosen) ? chosen : "All apps";
            loading = false;

            FillApps();
            Fill();
            if (!checkBox.Value.IsEmpty) Check(checkBox.Value, false);
            else CheckTyped();
        }

        public KeybindCatalog Catalog { get { return catalog; } }

        static bool? DefaultIsRunning(AppKeybinds app)
        {
            if (app.App == KeybindReaders.MacroKitName && app.CustomId == null) return true;
            if (app.Processes == null || app.Processes.Length == 0) return null;
            try
            {
                foreach (string name in app.Processes)
                {
                    Process[] found = Process.GetProcessesByName(name);
                    bool any = found.Length > 0;
                    foreach (Process p in found) p.Dispose();
                    if (any) return true;
                }
                return false;
            }
            catch (Exception) { return null; }
        }

        void FillApps()
        {
            apps.Children.Clear();
            foreach (AppKeybinds app in catalog.Apps)
            {
                int global = app.Keybinds.Count(k => k.Enabled && k.Scope == KeybindScope.Global);
                int local = app.Keybinds.Count(k => k.Enabled && k.Scope == KeybindScope.AppActive);
                var name = Ui.Text(app.App, "Text", true, 13);
                var top = new Grid();
                top.ColumnDefinitions.Add(new ColumnDefinition());
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                top.Children.Add(name);
                bool? running = IsRunning(app);
                if (running.HasValue)
                {
                    var pill = running.Value ? Ui.Pill("Running", "Good", "GoodBg", 10.5) : Ui.Pill("Not running", "Muted", "Card2", 10.5);
                    pill.ToolTip = running.Value ? "Its shortcuts are active now." : "Its shortcuts do nothing until it runs.";
                    Grid.SetColumn(pill, 1);
                    top.Children.Add(pill);
                }
                var counts = Ui.Text(Count(global, "any window") + " · " + Count(local, "in-app"), "Muted", false, 12);
                var stack = Ui.Stack(2, top, counts);
                if (app.Source.Length > 0)
                {
                    var source = Ui.Text(app.Source, "Muted", false, 11);
                    source.FontFamily = Ui.MonoFont;
                    source.ToolTip = "Read from " + app.Source + (catalog != null ? " at " + catalog.ReadAt.ToString("t") : "");
                    source.TextTrimming = TextTrimming.CharacterEllipsis;
                    stack.Children.Add(source);
                }
                else if (app.CustomId != null)
                {
                    stack.Children.Add(Ui.Text("Added by you", "Muted", false, 11));
                }
                if (app.Problem != null)
                {
                    var problem = Ui.Text(app.Problem, "Warn", false, 11.5, true);
                    stack.Children.Add(problem);
                }
                var button = new Button { Content = stack, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 6), ToolTip = "Show only " + app.App + "'s shortcuts." };
                button.SetResourceReference(FrameworkElement.StyleProperty, "Ghost");
                string appName = app.App;
                button.Click += (s, e) => appFilter.SelectedItem = (appFilter.SelectedItem as string) == appName ? "All apps" : appName;
                if (app.CustomId == null) { apps.Children.Add(button); continue; }

                // An app added by hand: the card filters like the others, and the pencil next to it opens the editor.
                string id = app.CustomId;
                var edit = Ui.GlyphButton(Ui.GlyphEdit, "Edit " + app.App + " (change or delete its shortcuts)", () => EditApp(c.KeybindApps.FirstOrDefault(a => a.Id == id)));
                edit.VerticalAlignment = VerticalAlignment.Top;
                edit.Margin = new Thickness(4, 4, 0, 0);
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(button);
                Grid.SetColumn(edit, 1);
                row.Children.Add(edit);
                apps.Children.Add(row);
            }
        }

        /// <summary>Opens the editor for an added app (null = a new one) and saves what the person keeps.</summary>
        void EditApp(KeybindAppDef existing)
        {
            Window owner = Window.GetWindow(Root);
            var editor = new KeybindAppEditor(existing, TakenNames(existing));
            KeybindAppEditor.Outcome outcome = editor.ShowDialog(owner);
            if (outcome == KeybindAppEditor.Outcome.Cancelled) return;
            Apply(existing, editor, outcome);
        }

        /// <summary>Saves the editor's result: adds, replaces or deletes the app. Public for the self test, which cannot show a dialog.</summary>
        public void Apply(KeybindAppDef existing, KeybindAppEditor editor, KeybindAppEditor.Outcome outcome)
        {
            if (outcome == KeybindAppEditor.Outcome.Deleted)
            {
                if (existing != null) c.KeybindApps.Remove(existing);
                c.SetStatus("Removed " + existing.Name + " from Global Keybinds.", false);
            }
            else if (outcome == KeybindAppEditor.Outcome.Saved)
            {
                KeybindAppDef result = editor.Result;
                int at = existing == null ? -1 : c.KeybindApps.IndexOf(existing);
                if (at >= 0) c.KeybindApps[at] = result; else c.KeybindApps.Add(result);
                c.SetStatus((existing == null ? "Added " : "Saved ") + result.Name + " to Global Keybinds.", false);
            }
            else return;
            c.MarkDirty(SaveParts.Keybinds);
            if (catalog == null || !Root.IsVisible) Refresh();   // MarkDirty refreshes a visible tab
        }

        /// <summary>The other apps' names, which an added app may not reuse.</summary>
        public List<string> TakenNames(KeybindAppDef except)
        {
            var names = new List<string>();
            if (catalog != null) names.AddRange(catalog.Apps.Where(a => a.CustomId == null).Select(a => a.App));
            else names.Add(KeybindReaders.MacroKitName);
            names.AddRange(c.KeybindApps.Where(a => a != except).Select(a => a.Name));
            return names;
        }

        static string Count(int n, string what) { return n + " " + what; }

        // ------------------------------------------------------------------ the list

        void Fill()
        {
            if (catalog == null) return;
            IEnumerable<Keybind> rows = catalog.All;
            if (builtIn.IsChecked != true) rows = rows.Where(k => k.Saved);
            if (scopeFilter.SelectedIndex == 1) rows = rows.Where(k => k.Scope == KeybindScope.Global);
            if (scopeFilter.SelectedIndex == 2) rows = rows.Where(k => k.Scope == KeybindScope.AppActive);
            string app = appFilter.SelectedItem as string;
            if (app != null && app != "All apps") rows = rows.Where(k => k.App == app);
            string query = (search.Text ?? "").Trim();
            if (query.Length > 0)
            {
                // Keys in any order: "ctrl alt s" finds every shortcut that uses Ctrl, Alt and S; "alt ctrl shift" every Ctrl+Shift+Alt one.
                HotkeyModifiers mods;
                ushort key;
                bool keys = KeyText.TryParseLoose(query, out mods, out key);
                rows = rows.Where(k => (keys && KeyText.HasKeys(k.Combo, mods, key))
                    || k.Keys.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || k.App.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || k.Action.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || k.Note.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            var appOrder = catalog.Apps.Select(a => a.App).ToList();
            List<Keybind> sorted = sort.SelectedIndex == 1
                ? rows.OrderBy(k => k.Combo.IsEmpty ? 1 : 0).ThenBy(k => ModifierRank(k.Combo)).ThenBy(k => KeyNames.Display(k.Combo.Vk), StringComparer.OrdinalIgnoreCase).ThenBy(k => k.Scope).ThenBy(k => appOrder.IndexOf(k.App)).ToList()
                : rows.OrderBy(k => appOrder.IndexOf(k.App)).ThenBy(k => k.Scope).ThenBy(k => k.Saved ? 0 : 1).ToList();

            list.Items.Clear();
            foreach (Keybind k in sorted) list.Items.Add(MakeRow(k));
            listEmpty.Visibility = sorted.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            int all = catalog.All.Count();
            int global = catalog.All.Count(k => k.Enabled && k.Scope == KeybindScope.Global);
            int clashes = catalog.Clashes().Count;
            summary.Text = (sorted.Count == all ? all + " shortcuts" : sorted.Count + " of " + all + " shortcuts") + " · " + global + " work in any window"
                + (clashes > 0 ? " · " + clashes + (clashes == 1 ? " overlap" : " overlaps") : "");
        }

        static int ModifierRank(Hotkey combo) { return (int)combo.Modifiers; }

        static Grid RowGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 120 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
            return grid;
        }

        static void AddCell(Grid grid, UIElement element, int column)
        {
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        ListBoxItem MakeRow(Keybind k)
        {
            var grid = RowGrid();
            var keys = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 1, 7, 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(1) };
            Ui.Ref(keys, Border.BackgroundProperty, "Card2");
            Ui.Ref(keys, Border.BorderBrushProperty, "Border");
            var keyText = Ui.Text(k.Keys, k.Enabled ? "Text" : "Muted", true, 12);
            keyText.FontFamily = Ui.MonoFont;
            keys.Child = keyText;
            AddCell(grid, keys, 0);

            var app = Ui.Text(k.App, "Text", false, 12.5);
            app.Margin = new Thickness(0, 0, 8, 0);
            AddCell(grid, app, 1);

            var action = Ui.Stack(1, Ui.Text(k.Action, "Text", false, 12.5, true));
            string note = k.Note;
            if (!k.Saved) note = note.Length == 0 ? "Built in" : "Built in · " + note;
            if (!k.Enabled) note = "Switched off" + (note.Length > 0 ? " · " + note : "");
            if (note.Length > 0) action.Children.Add(Ui.Text(note, "Muted", false, 11.5, true));
            string clash;
            if (clashNotes.TryGetValue(k, out clash))
            {
                var warn = new Grid();
                warn.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                warn.ColumnDefinitions.Add(new ColumnDefinition());
                var icon = Ui.Glyph(Ui.GlyphWarn, 11, "Warn");
                icon.VerticalAlignment = VerticalAlignment.Top;
                icon.Margin = new Thickness(0, 2, 5, 0);
                warn.Children.Add(icon);
                var warnText = Ui.Text(clash, "Warn", false, 11.5, true);
                Grid.SetColumn(warnText, 1);
                warn.Children.Add(warnText);
                action.Children.Add(warn);
            }
            action.Margin = new Thickness(0, 0, 8, 0);
            action.ToolTip = k.Action + (note.Length > 0 ? "\n" + note : "") + (clash != null ? "\n" + clash : "");
            AddCell(grid, action, 2);

            var scope = k.Scope == KeybindScope.Global
                ? Ui.Pill("Any window", "Info", "InfoBg", 11)
                : Ui.Pill("Only when active", "Muted", "Card2", 11);
            scope.ToolTip = k.Scope == KeybindScope.Global
                ? k.App + " reacts to these keys whichever window is active, as long as " + k.App + " is running."
                : "Only while " + k.App + " is the active window. In every other app these keys are free.";
            AddCell(grid, scope, 3);
            if (!k.Enabled) grid.Opacity = 0.6;
            return new ListBoxItem { Content = grid, Tag = k, Padding = new Thickness(10, 5, 10, 5) };
        }

        // ------------------------------------------------------------------ checking keys

        void CheckTyped()
        {
            if (!checkBox.Value.IsEmpty) return;   // the box wins over the search text
            string query = (search.Text ?? "").Trim();
            HotkeyModifiers mods;
            ushort key;
            // A whole shortcut in any order ("s ctrl alt") is checked too; modifiers alone only filter the list.
            if (query.Length > 1 && KeyText.TryParseLoose(query, out mods, out key) && key != 0 && (mods != HotkeyModifiers.None || query.Length > 2)) Check(new Hotkey(mods, key), false);
            else verdict.Visibility = Visibility.Collapsed;
        }

        /// <summary>Shows who uses the keys. fromBox: pressed in the box, so the list is narrowed to them as well.</summary>
        public void Check(Hotkey combo, bool fromBox)
        {
            if (combo.IsEmpty || catalog == null) { verdict.Visibility = Visibility.Collapsed; return; }
            List<Keybind> users = catalog.Users(combo);
            bool? free = null;
            if (Probe != null && !users.Any(u => u.Scope == KeybindScope.Global))
            {
                try { free = Probe(combo); }
                catch (Exception ex) { Log.Warn("Checking a hotkey with Windows failed: " + ex.Message); }
            }
            ComboVerdict judged = ComboCheck.Judge(users, free);
            string kind = judged == ComboVerdict.Free ? "Good" : judged == ComboVerdict.InAppOnly ? "Info" : judged == ComboVerdict.TakenGlobally ? "Bad" : "Warn";
            Ui.Ref(verdict, Border.BackgroundProperty, kind + "Bg");
            verdictText.Text = ComboCheck.Explain(combo, users, free);
            verdict.Visibility = Visibility.Visible;
            if (fromBox && users.Count > 0)
            {
                loading = true;
                search.Text = combo.ToString();
                loading = false;
                Fill();
            }
        }

        /// <summary>Text of the latest answer (self test).</summary>
        public string VerdictText { get { return verdict.Visibility == Visibility.Visible ? verdictText.Text : ""; } }

        /// <summary>The shortcuts the list shows right now (self test).</summary>
        public List<Keybind> ShownRows { get { return list.Items.Cast<ListBoxItem>().Select(i => (Keybind)i.Tag).ToList(); } }

        /// <summary>Puts text in the search box as if typed (self test).</summary>
        public void SetSearch(string text) { search.Text = text; }
    }
}
