using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using MouseButton = MacroKit.Core.MouseButton;
using System.Windows.Threading;
using MacroKit.Core;
using MacroKit.Macros;

namespace MacroKit.App
{
    /// <summary>
    /// The Macros tab: write or click together Super Macro scripts and run them in the window you choose. Part 1: the list, the
    /// header and binding. MacrosTab.Editor.cs has the Lines, Tiles and Code views, MacrosTab.Options.cs the option chips and
    /// cards, MacrosTab.Palette.cs the command palette.
    /// </summary>
    public sealed partial class MacrosTab
    {
        public readonly FrameworkElement Root;
        readonly AppController c;
        readonly Window owner;
        readonly ListBox list = new ListBox();
        readonly TextBlock listEmpty;
        readonly TextBox listSearch = new TextBox();
        readonly Button duplicateButton, deleteButton, undoButton, runButton, stopButton, removeExamples;
        // What Undo puts back: the macro just deleted, or the examples just removed, at their old places.
        List<KeyValuePair<int, MacroDef>> undoItems = new List<KeyValuePair<int, MacroDef>>();
        readonly ComboBox deckColor;
        readonly TextBox deckIcon;
        readonly FrameworkElement deckLook;
        readonly FrameworkElement editorPanel;
        readonly TextBox name;
        readonly TextBox script;
        readonly ListBox steps = new ListBox();
        readonly TextBlock stepsHeader = Ui.Text("", "Muted", false, 12);
        readonly TextBlock stepsSummary = Ui.Text("", "Muted", false, 12);
        readonly TextBlock stepsEmpty;
        readonly RadioButton once, times, continuous, runPrevious, runSpecific, altWindows, altSpecific;
        readonly NumberBox repeatTimes, repeatDelay, keyDelay;
        readonly CheckBox newLines, pasteText, stopIfFocusLeaves, showOnDeck;
        readonly HotkeyBox hotkey;
        readonly TextBlock hotkeyError = Ui.Text("", "Bad", false, 12);
        readonly WindowMatchEditor targetEditor, partnerEditor;
        readonly FrameworkElement targetBlock, partnerBlock;
        readonly DispatcherTimer parseTimer;
        MacroDef current;
        PlainResult lastPlain;          // the steps as last translated, so the summary line can follow the options without translating again
        ParseResult summaryParse;       // the macro's script as last parsed for that line
        readonly CheckBox enabled;
        readonly TextBlock exampleNote = Ui.Text("", "Muted", false, 12, true);
        const string RunTip = "Run this macro (F5). Macro Kit steps aside and the macro runs in the window you chose below.";
        ScheduleEditor schedule;
        ParseResult parsed;
        bool loading;
        bool stepsLoading;

        // the plain editors (typed or key tiles), the Stream Deck code and the folded options
        IPlainEditor editor;
        PlainTextEditor textEditor;
        KeyTileEditor tileEditor;
        readonly Border editorHost = new Border();
        readonly TextBlock plainSummary = Ui.Text("", "Muted", false, 12, true);
        readonly Button viewLines, viewTiles, viewCode, allCommands;
        Border problemsBox;
        FrameworkElement palette, codePanel;
        bool syncing, codeDirty;

        // palette inputs
        readonly NumberBox pauseMs = new NumberBox(100, 0, 3600000, 70);
        readonly TextBox typeText = new TextBox();
        readonly HotkeyBox customShortcut = new HotkeyBox(false, 170);
        readonly HotkeyBox repeatKey = new HotkeyBox(false, 140);
        readonly NumberBox repeatCount = new NumberBox(5, 1, 100000, 56);
        readonly NumberBox repeatEvery = new NumberBox(100, 0, 600000, 64);
        readonly NumberBox mouseX = new NumberBox(0, -100000, 100000, 66);
        readonly NumberBox mouseY = new NumberBox(0, -100000, 100000, 66);
        readonly NumberBox scrollNotches = new NumberBox(3, 1, 1000, 50);
        readonly ComboBox holdKey = new ComboBox { Width = 110 };
        readonly ComboBox functionKey = new ComboBox { Width = 76 };
        readonly TextBox commentText = new TextBox();
        readonly NumberBox loopTimes = new NumberBox(3, 1, 100000, 56);
        readonly ComboBox otherMacro = new ComboBox { IsEditable = true, Width = 180 };
        readonly NumberBox choiceCount = new NumberBox(2, 2, 50, 50);
        readonly TextBox openTarget = new TextBox();
        readonly TextBox websiteText = new TextBox();
        readonly ComboBox closeProgram = new ComboBox { IsEditable = true, Width = 170 };
        readonly ComboBox windowName = new ComboBox { IsEditable = true, Width = 240 };
        readonly NumberBox waitWindowSeconds = new NumberBox(10, 1, 3600, 56);
        readonly NumberBox pixelX = new NumberBox(0, -100000, 100000, 66);
        readonly NumberBox pixelY = new NumberBox(0, -100000, 100000, 66);
        readonly TextBox pixelColor = new TextBox { Width = 76 };
        readonly Border pixelSwatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        readonly NumberBox waitPixelSeconds = new NumberBox(10, 1, 3600, 56);
        Button pickPixel;
        // 1.13.0, Pro: saved pictures ({{WAITIMAGE}}, {{CLICKIMAGE}})
        readonly ComboBox pictureName = new ComboBox { IsEditable = true, Width = 170 };
        readonly NumberBox pictureSize = new NumberBox(48, PixelImage.MinSide * 2, PixelImage.MaxSide, 56);
        readonly NumberBox waitPictureSeconds = new NumberBox(10, 1, 3600, 56);
        readonly Image picturePreview = new Image { Width = 40, Height = 40, Stretch = System.Windows.Media.Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        Button capturePicture;
        FrameworkElement pictureCommands;
        Border pictureNote;
        readonly NumberBox volumePercent = new NumberBox(50, 0, 100, 50);
        readonly TextBox soundFile = new TextBox();
        readonly TextBox varName = new TextBox();
        readonly TextBox varValue = new TextBox();
        readonly TextBox ifLeft = new TextBox();
        readonly ComboBox ifOp = new ComboBox { Width = 58 };
        readonly TextBox ifRight = new TextBox();
        readonly TextBox ifWindow = new TextBox();
        readonly TextBox commandSearch = new TextBox();
        readonly List<CommandGroup> groups = new List<CommandGroup>();
        FrameworkElement proCommands;
        Border proNote;
        TextBlock noMatches;
        Button pickPosition;
        DispatcherTimer countdown;

        public MacrosTab(AppController controller, Window owner)
        {
            c = controller;
            this.owner = owner;

            // ---- list
            listEmpty = Ui.Para("No macros yet. Press New macro to make one.");
            listEmpty.Margin = new Thickness(10, 8, 10, 0);
            list.SelectionChanged += (s, e) => { if (!loading) Bind((list.SelectedItem as ListBoxItem) == null ? null : (MacroDef)((ListBoxItem)list.SelectedItem).Tag); };
            list.KeyDown += (s, e) => { if (e.Key == Key.Delete && current != null) { e.Handled = true; Delete(); } };
            var newButton = Ui.Button("New macro", NewMacro, null, "Add an empty macro (Ctrl+N).");
            duplicateButton = Ui.Button("Duplicate", Duplicate, null, "Copy the selected macro.");
            deleteButton = Ui.Button("Delete", Delete, "Ghost", "Delete the selected macro (Delete key). You can undo it.");
            undoButton = Ui.Button("Undo delete", UndoDelete, "Ghost", "Put back the macro you just deleted.");
            undoButton.Visibility = Visibility.Collapsed;
            removeExamples = Ui.Button("Remove examples", RemoveExamples, "Small", "Remove the example macros you have not changed. You can undo it.");
            removeExamples.VerticalAlignment = VerticalAlignment.Center;
            removeExamples.Margin = new Thickness(0, 0, 6, 0);
            var libraryButton = Ui.Button("Library", () => new LibraryWindow(owner, c).ShowDialog(), "Small", "Ready-made macros and phrases to add with one click.");
            c.LibraryAdded += (macro, phrase) => { if (macro != null) { listSearch.Text = ""; Reload(macro); } };
            var listButtons = Ui.Wrap(newButton, duplicateButton, undoButton, deleteButton);
            foreach (UIElement b in listButtons.Children) ((FrameworkElement)b).Margin = new Thickness(0, 0, 6, 6);
            deleteButton.Margin = new Thickness(10, 0, 6, 6);   // kept apart from New and Duplicate
            var listGrid = new Grid();
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition());
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var headingRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
            libraryButton.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(libraryButton, Dock.Right);
            headingRow.Children.Add(libraryButton);
            DockPanel.SetDock(removeExamples, Dock.Right);
            headingRow.Children.Add(removeExamples);
            headingRow.Children.Add(Ui.Heading("Macros"));
            listGrid.Children.Add(headingRow);
            AutomationProperties.SetName(listSearch, "Search macros");
            AutomationProperties.SetName(list, "Macros");
            listSearch.ToolTip = "Show only the macros whose name, steps or hotkey hold these words (Ctrl+F).";
            listSearch.TextChanged += (s, e) => FilterList();
            var searchBox = Ui.Hinted(listSearch, "Search macros");
            searchBox.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(searchBox, 1);
            listGrid.Children.Add(searchBox);
            var host = new Grid();
            host.Children.Add(list);
            host.Children.Add(listEmpty);
            Grid.SetRow(host, 2);
            listGrid.Children.Add(host);
            listButtons.Margin = new Thickness(0, 8, 0, -6);
            Grid.SetRow(listButtons, 3);
            listGrid.Children.Add(listButtons);
            Border listCard = Ui.Card(listGrid);
            listCard.Margin = new Thickness(0, 0, 10, 0);

            // ---- header: name, run, stop
            name = new TextBox { FontWeight = FontWeights.SemiBold, FontSize = 14, MinHeight = 32 };
            AutomationProperties.SetName(name, "Macro name");
            name.TextChanged += (s, e) => Edit(m => m.Name = name.Text, true);
            runButton = Ui.Button(Ui.Row(7, Ui.Shape("play", 11, "AccentFg"), new TextBlock { Text = "Run" }), RunClicked, "Primary", RunTip);
            stopButton = Ui.Button(Ui.Row(7, Ui.Shape("stop", 11, "Text"), new TextBlock { Text = "Stop" }), () => c.Stop(), null, "Stop the running macro.");
            ToolTipService.SetShowOnDisabled(runButton, true);
            ToolTipService.SetShowOnDisabled(stopButton, true);
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(name);
            Grid.SetColumn(runButton, 1);
            runButton.Margin = new Thickness(10, 0, 0, 0);
            runButton.MinWidth = 84;
            header.Children.Add(runButton);
            Grid.SetColumn(stopButton, 2);
            stopButton.Margin = new Thickness(6, 0, 0, 0);
            stopButton.MinWidth = 76;
            header.Children.Add(stopButton);

            // ---- script, palette and steps
            script = new TextBox
            {
                FontFamily = Ui.MonoFont, FontSize = 12.5, AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap, Height = 188,
                VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8, 6, 8, 6),
                SpellCheck = { IsEnabled = false },
                ToolTip = "Super Macro text, for example {{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}}. Anything outside {{ }} is typed as is."
            };
            script.TextChanged += (s, e) =>
            {
                if (syncing || loading) return;
                Edit(m => m.Script = script.Text, false);
                codeDirty = true;
                parseTimer.Stop();
                parseTimer.Start();
            };
            parseTimer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
            parseTimer.Tick += (s, e) => { parseTimer.Stop(); ShowSteps(); if (codeDirty) { codeDirty = false; PlainFromCode(); } };

            steps.SelectionChanged += (s, e) => SelectStepInScript();
            stepsEmpty = Ui.Para("Nothing yet. Add commands above or type a script.");
            stepsEmpty.FontSize = 12;
            stepsEmpty.Margin = new Thickness(8, 4, 0, 0);

            // ---- the steps, in the editor the user chose
            viewLines = Ui.Button("Lines", () => ChooseEditor(EditorKinds.Text), "Chip", "Type the steps, one per line.");
            viewTiles = Ui.Button("Tiles", () => ChooseEditor(EditorKinds.Tiles), "Chip", "The same steps as key tiles you can click and add to. Use ◀ ▶ to move one.");
            viewCode = Ui.Button("Code", ChooseCode, "Chip", "The same steps as Super Macro text, to copy into a Super Macro action on a Stream Deck or to edit there.");
            viewLines.Margin = new Thickness(0, 0, 4, 0);
            viewTiles.Margin = new Thickness(0, 0, 4, 0);
            allCommands = Ui.Button("", () => { c.Settings.ShowAllCommands = !c.Settings.ShowAllCommands; c.MarkDirty(SaveParts.Settings); ApplyLayout(); }, "Link",
                "Every command Macro Kit has, in groups, with boxes to fill in.");
            palette = BuildPalette();
            var stepsTitle = new DockPanel();
            var viewToggle = Ui.Row(0, viewLines, viewTiles, viewCode);
            DockPanel.SetDock(viewToggle, Dock.Right);
            viewToggle.VerticalAlignment = VerticalAlignment.Center;
            stepsTitle.Children.Add(viewToggle);
            stepsTitle.Children.Add(Ui.Heading("What it does"));
            var summaryRow = new DockPanel();
            DockPanel.SetDock(allCommands, Dock.Right);
            allCommands.VerticalAlignment = VerticalAlignment.Top;
            allCommands.Margin = new Thickness(12, 0, 0, 0);
            summaryRow.Children.Add(allCommands);
            summaryRow.Children.Add(plainSummary);
            var scriptCard = Ui.Card(Ui.Stack(10, stepsTitle, editorHost, summaryRow, palette));

            // ---- the Code view: the same steps as Super Macro text
            var copyCode = Ui.Button("Copy", CopyCode, "Small", "Copy the Super Macro text, ready to paste into a Super Macro action on your Stream Deck.");
            var codeTitle = new DockPanel();
            DockPanel.SetDock(copyCode, Dock.Right);
            codeTitle.Children.Add(copyCode);
            var codeNote = Ui.Para("Paste it into a Stream Deck Super Macro action, or change it here.", "Muted", 12);
            codeNote.VerticalAlignment = VerticalAlignment.Center;
            codeTitle.Children.Add(codeNote);

            // ---- repeat
            once = new RadioButton { Content = "Once", GroupName = "macroRepeat" };
            times = new RadioButton { Content = "", GroupName = "macroRepeat", VerticalAlignment = VerticalAlignment.Center };
            continuous = new RadioButton { Content = "Continuously", GroupName = "macroRepeat" };
            repeatTimes = new NumberBox(5, 1, 100000, 60);
            repeatDelay = new NumberBox(0, 0, 3600000, 70);
            keyDelay = new NumberBox(10, 0, 60000, 60);
            once.Checked += (s, e) => Edit(m => m.Repeat = RepeatMode.Once, true);
            times.Checked += (s, e) => Edit(m => m.Repeat = RepeatMode.Times, true);
            continuous.Checked += (s, e) => Edit(m => m.Repeat = RepeatMode.Continuous, true);
            // Only actually typing a count switches to "N times"; merely clicking or tabbing into the box changes nothing.
            repeatTimes.Changed += () => { if (!loading && times.IsChecked != true) times.IsChecked = true; Edit(m => m.RepeatTimes = repeatTimes.Value, true); };
            repeatDelay.Changed += () => Edit(m => m.RepeatDelayMs = repeatDelay.Value, false);
            keyDelay.Changed += () => { Edit(m => m.KeyDelayMs = keyDelay.Value, false); ShowSteps(); };
            newLines = new CheckBox { Content = "New lines in the script press Enter" };
            newLines.ToolTip = "On is how Super Macro behaves. Off ignores line breaks, so you can lay a long script out over several lines.";
            newLines.Click += (s, e) => { Edit(m => m.NewLineIsEnter = newLines.IsChecked == true, false); ShowSteps(); PlainFromCode(); };
            AutomationProperties.SetName(times, "Repeat a number of times");
            AutomationProperties.SetName(repeatTimes, "Number of times");
            AutomationProperties.SetName(repeatDelay, "Milliseconds between repeats");
            AutomationProperties.SetName(keyDelay, "Milliseconds after each key or click");
            repeatTimes.ToolTip = "How many times the macro runs.";
            repeatDelay.ToolTip = "A pause between one run and the next. 1000 ms = 1 second.";
            keyDelay.ToolTip = "A short pause after every key or click. Raise it if a program misses keys.";
            repeatTimes.Margin = new Thickness(2, 0, 7, 0);
            var timesRow = Ui.Row(0, times, repeatTimes, Ui.Text("times", null));
            var repeatRow = Ui.Row(18, once, timesRow, continuous);
            var delays = Ui.Row(7, Ui.Text("Wait", "Muted", false, 12), repeatDelay, Ui.Text("ms between repeats", "Muted", false, 12),
                Ui.Text("·", "Muted"), keyDelay, Ui.Text("ms after each key or click", "Muted", false, 12));
            ((FrameworkElement)delays.Children[2]).ToolTip = "1000 ms = 1 second.";
            ((FrameworkElement)delays.Children[5]).ToolTip = "1000 ms = 1 second.";
            ((FrameworkElement)delays.Children[3]).Margin = new Thickness(12, 0, 0, 0);
            ((FrameworkElement)delays.Children[4]).Margin = new Thickness(12, 0, 0, 0);
            pasteText = new CheckBox { Content = "Paste text instead of typing it (much faster)" };
            pasteText.ToolTip = "A whole block of text appears at once, through the clipboard, and your clipboard is put back afterwards. Short text (under 4 characters) is still typed. Turn it off for games, remote desktops or boxes that don't accept pasting.";
            pasteText.Click += (s, e) => { Edit(m => m.PasteText = pasteText.IsChecked == true, false); ShowSteps(); };
            var repeatCard = Ui.Card(Ui.Stack(8, Ui.Heading("Repeat and speed"), repeatRow, delays, pasteText));
            codePanel = Ui.Stack(8, codeTitle, BuildScriptAndSteps(), newLines);

            // ---- where it runs
            targetEditor = new WindowMatchEditor(c);
            partnerEditor = new WindowMatchEditor(c);
            runPrevious = new RadioButton { Content = "Whichever window I was using before Macro Kit", GroupName = "macroTarget" };
            runSpecific = new RadioButton { Content = "Always the same window", GroupName = "macroTarget" };
            runPrevious.Checked += (s, e) => TargetKindChanged(TargetKind.PreviousWindow);
            runSpecific.Checked += (s, e) => TargetKindChanged(TargetKind.SpecificWindow);
            stopIfFocusLeaves = new CheckBox { Content = "Stop if another window takes focus", ToolTip = "Protects you from typing into the wrong window." };
            stopIfFocusLeaves.Click += (s, e) => Edit(m => m.StopIfFocusLeaves = stopIfFocusLeaves.IsChecked == true, false);
            altWindows = new RadioButton { Content = "The window Windows would switch to (never Macro Kit)", GroupName = "macroAlt" };
            altSpecific = new RadioButton { Content = "Always the same window", GroupName = "macroAlt" };
            altWindows.Checked += (s, e) => AltKindChanged(false);
            altSpecific.Checked += (s, e) => AltKindChanged(true);
            // Each window chooser shows only under its own "Always the same window", so the card stays short.
            targetBlock = Indent(Ui.Stack(8, targetEditor.Root, stopIfFocusLeaves));
            partnerBlock = Indent(partnerEditor.Root);
            var altCaption = Ui.Caption("When the macro presses Alt+Tab, go to");
            var altNote = Ui.Help("Choose one and Alt+Tab always jumps between the same two windows.",
                "That keeps a copy-and-paste macro on track even when other windows open in between.");
            var whereCard = Ui.Card(Ui.Stack(8,
                Ui.Heading("Where it runs"),
                Ui.Caption("Type into"), runPrevious, runSpecific, targetBlock,
                altCaption, altNote, altWindows, altSpecific, partnerBlock));
            altCaption.Margin = new Thickness(0, 16, 0, 0);
            altNote.Margin = new Thickness(0, 0, 0, 2);

            // ---- hotkey and deck
            hotkey = new HotkeyBox(true, 190);
            AutomationProperties.SetName(hotkey, "Macro hotkey");
            hotkey.Changed += () => Edit(m => m.Hotkey = hotkey.Value.IsEmpty ? "" : hotkey.Value.ToString(), true);
            if (!c.InputEnabled) hotkey.ToolTip = "Hotkeys are off in this session (input is turned off).";
            showOnDeck = new CheckBox { Content = "Show this macro as a button on the Deck" };
            showOnDeck.Click += (s, e) => { Edit(m => m.ShowOnDeck = showOnDeck.IsChecked == true, true); deckLook.IsEnabled = showOnDeck.IsChecked == true; };
            deckColor = new ComboBox { Width = 110, ToolTip = "A coloured stripe on the macro's Deck button, so it is quick to find." };
            AutomationProperties.SetName(deckColor, "Deck button colour");
            deckColor.Items.Add("No colour");
            foreach (string name in DeckWindow.Colors) deckColor.Items.Add(name);
            deckColor.SelectionChanged += (s, e) => Edit(m => m.DeckColor = deckColor.SelectedIndex <= 0 ? "" : (string)deckColor.SelectedItem, false);
            deckIcon = new TextBox { Width = 52, MaxLength = 4, ToolTip = "An emoji or symbol shown before the name on the Deck, such as 📋 or ★. Press Win+. for emoji." };
            AutomationProperties.SetName(deckIcon, "Deck button icon");
            deckIcon.TextChanged += (s, e) => Edit(m => m.DeckIcon = deckIcon.Text.Trim(), false);
            deckLook = Ui.Row(10, Ui.Text("Colour", "Muted", false, 12), deckColor, Ui.Text("Icon", "Muted", false, 12), deckIcon);
            deckLook.Margin = new Thickness(26, 0, 0, 0);
            enabled = new CheckBox { Content = "Hotkey and schedule are on",
                ToolTip = "Turn this off to pause the hotkey and schedule without losing them. The macro still runs from its Run button, the Deck and other macros." };
            enabled.Click += (s, e) => EnabledChanged(enabled.IsChecked == true);
            var launchCard = Ui.Card(Ui.Stack(8,
                Ui.Heading("Start it with"),
                enabled,
                Ui.Row(10, Ui.Text("Hotkey", "Muted", false, 12), hotkey.WithClear()),
                hotkeyError,
                showOnDeck,
                deckLook));
            hotkeyError.TextWrapping = TextWrapping.Wrap;

            var editorStack = new StackPanel();
            exampleNote.Margin = new Thickness(0, 8, 0, 0);
            exampleNote.Visibility = Visibility.Collapsed;
            var headerCard = Ui.Card(Ui.Stack(0, header, exampleNote));
            editorStack.Children.Add(headerCard);
            editorStack.Children.Add(scriptCard);
            // ---- options: four chips that say how it runs; a click opens only that card under them
            schedule = new ScheduleEditor(c, () => owner);
            editorStack.Children.Add(BuildOptions(repeatCard, whereCard, launchCard, schedule.Root));
            foreach (FrameworkElement card in optionCards) editorStack.Children.Add(card);
            var scroll = new ScrollViewer { Content = editorStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            editorPanel = scroll;

            var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250), MinWidth = 220 });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(listCard);
            Grid.SetColumn(scroll, 1);
            grid.Children.Add(scroll);
            Root = grid;

            c.ActivityChanged += UpdateRunButtons;
            c.HotkeysChanged += ShowHotkeyError;
            // Settings (or the link above the steps) can change the editor.
            c.Edited += parts => { if ((parts & SaveParts.Settings) != 0 && editor != null && EditorKinds.Normal(c.Settings.MacroEditor) != EditorKind) UseEditor(c.Settings.MacroEditor); };
            UseEditor(c.Settings.MacroEditor);
            ApplyLayout();
            Reload(null);
        }

        /// <summary>Run: a step that is not understood is named here, with its line, instead of the run failing on a cryptic script position.</summary>
        void RunClicked()
        {
            if (current == null) return;
            if (lastPlain != null && !lastPlain.Ok)
            {
                c.SetStatus("“" + current.Name + "” has a step to fix. " + FirstProblem(lastPlain), true);
                return;
            }
            c.StartMacro(current, RunOrigin.MainWindow);
        }

        /// <summary>"Line 3: ..." (or the step in quotes in the tiles view) for the first step that is not understood.</summary>
        string FirstProblem(PlainResult result)
        {
            PlainLine first = result.Lines.First(l => l.Error != null);
            string source = first.Source.Length > 30 ? first.Source.Substring(0, 30) + "…" : first.Source;
            string where = EditorKind == EditorKinds.Tiles ? "“" + source + "”" : "Line " + (first.Number + 1);
            return where + ": " + first.Error;
        }

        static FrameworkElement Indent(FrameworkElement e) { e.Margin = new Thickness(26, e.Margin.Top, 0, e.Margin.Bottom); return e; }

        // ------------------------------------------------------------------ list and binding

        MacroDef Selected
        {
            get { var item = list.SelectedItem as ListBoxItem; return item == null ? null : item.Tag as MacroDef; }
        }

        void Reload(MacroDef select)
        {
            removeExamples.Visibility = c.Macros.Any(Examples.IsUnchanged) ? Visibility.Visible : Visibility.Collapsed;
            loading = true;
            list.Items.Clear();
            foreach (MacroDef m in c.Macros) list.Items.Add(MakeItem(m));
            ListBoxItem match = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, select)) ?? (list.Items.Count > 0 ? (ListBoxItem)list.Items[0] : null);
            list.SelectedItem = match;
            loading = false;
            FilterList();
            Bind(match == null ? null : (MacroDef)match.Tag);
            // With many macros the list would otherwise sit at the top while the editor shows one that is out of sight.
            if (match != null) list.ScrollIntoView(match);
        }

        /// <summary>Hides the macros that don't match the search box, and says so when none are left.</summary>
        void FilterList()
        {
            int shown = 0;
            foreach (ListBoxItem item in list.Items)
            {
                var m = (MacroDef)item.Tag;
                bool match = Ui.Matches(listSearch.Text, m.Name, m.Script, m.Hotkey);
                item.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                if (match) shown++;
            }
            listEmpty.Text = list.Items.Count == 0 ? "No macros yet. Press New macro to make one." : "No macros match “" + listSearch.Text.Trim() + "”.";
            listEmpty.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Self-test: the list's search box.</summary>
        public string ListSearch { get { return listSearch.Text; } set { listSearch.Text = value; } }

        /// <summary>Self-test: the names of the macros the list shows.</summary>
        public List<string> ShownMacros
        {
            get { return list.Items.Cast<ListBoxItem>().Where(i => i.Visibility == Visibility.Visible).Select(i => ((MacroDef)i.Tag).Name).ToList(); }
        }

        ListBoxItem MakeItem(MacroDef m)
        {
            var panel = new StackPanel();
            var head = new DockPanel { LastChildFill = true };
            if (!m.Enabled)
            {
                // A label, not fading, as for phrases: faded text was hard to read.
                var off = Ui.Pill("Off", "Muted", "Card2", 10.5);
                off.Tag = "Off";
                off.Margin = new Thickness(8, 0, 0, 0);
                off.ToolTip = "Switched off: its hotkey and schedule are paused.";
                DockPanel.SetDock(off, Dock.Right);
                head.Children.Add(off);
            }
            head.Children.Add(Ui.Text(m.Name.Length == 0 ? "(unnamed)" : m.Name, "Text", true));
            panel.Children.Add(head);
            var sub = new List<string> { RepeatSummary(m) };
            if (!string.IsNullOrEmpty(m.Hotkey)) sub.Add(m.Hotkey);
            if (m.ShowOnDeck) sub.Add("Deck");
            var line = Ui.Text(string.Join("  ·  ", sub), "Muted", false, 12);
            line.Margin = new Thickness(0, 2, 0, 0);
            panel.Children.Add(line);
            return new ListBoxItem { Content = panel, Tag = m };
        }

        static string RepeatSummary(MacroDef m)
        {
            return m.Repeat == RepeatMode.Once ? "Once" : m.Repeat == RepeatMode.Times ? m.RepeatTimes + " times" : "Continuous";
        }

        void RefreshItem(MacroDef m)
        {
            var item = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, m));
            if (item == null) return;
            int index = list.Items.IndexOf(item);
            bool wasSelected = ReferenceEquals(list.SelectedItem, item);
            loading = true;
            ListBoxItem fresh = MakeItem(m);
            fresh.Visibility = item.Visibility;
            list.Items[index] = fresh;
            if (wasSelected) list.SelectedIndex = index;
            loading = false;
        }

        void Bind(MacroDef m)
        {
            current = m;
            editorPanel.IsEnabled = m != null;
            duplicateButton.IsEnabled = m != null;
            deleteButton.IsEnabled = m != null;
            loading = true;
            if (m != null)
            {
                name.Text = m.Name;
                script.Text = m.Script;
                script.CaretIndex = script.Text.Length;
                ShowPlain(PlainMacro.PlainFor(m));
                once.IsChecked = m.Repeat == RepeatMode.Once;
                times.IsChecked = m.Repeat == RepeatMode.Times;
                continuous.IsChecked = m.Repeat == RepeatMode.Continuous;
                repeatTimes.Value = m.RepeatTimes;
                repeatDelay.Value = m.RepeatDelayMs;
                keyDelay.Value = m.KeyDelayMs;
                newLines.IsChecked = m.NewLineIsEnter;
                pasteText.IsChecked = m.PasteText;
                runPrevious.IsChecked = m.Target == TargetKind.PreviousWindow;
                runSpecific.IsChecked = m.Target == TargetKind.SpecificWindow;
                stopIfFocusLeaves.IsChecked = m.StopIfFocusLeaves;
                targetEditor.Bind(m.TargetWindow, () => Edit(x => { }, false));
                partnerEditor.Bind(m.AltTabPartner, () => Edit(x => { }, false));
                altSpecific.IsChecked = !m.AltTabPartner.IsEmpty;
                altWindows.IsChecked = m.AltTabPartner.IsEmpty;
                hotkey.SetText(m.Hotkey);
                enabled.IsChecked = m.Enabled;
                showOnDeck.IsChecked = m.ShowOnDeck;
                deckLook.IsEnabled = m.ShowOnDeck;
                int colorIndex = Array.FindIndex(DeckWindow.Colors, x => string.Equals(x, m.DeckColor, StringComparison.OrdinalIgnoreCase));
                deckColor.SelectedIndex = colorIndex + 1;
                deckIcon.Text = m.DeckIcon ?? "";
                schedule.Bind(m, () => Edit(x => { }, false));
                UpdateEditorEnabled();
                RefreshProCommands();
            }
            else
            {
                name.Text = "";
                script.Text = "";
                ShowPlain("");
            }
            codeDirty = false;
            UpdateOptionsSummary();
            UpdateExampleNote();
            loading = false;
            ShowSteps();
            ShowHotkeyError();
            UpdateRunButtons();
        }

        void UpdateEditorEnabled()
        {
            bool specific = current != null && current.Target == TargetKind.SpecificWindow;
            targetBlock.Visibility = specific ? Visibility.Visible : Visibility.Collapsed;
            partnerBlock.Visibility = altSpecific.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        void Edit(Action<MacroDef> change, bool refreshItem)
        {
            if (loading || current == null) return;
            change(current);
            c.MarkDirty(SaveParts.Macros);
            if (refreshItem) RefreshItem(current);
            UpdateOptionsSummary();
            ShowSummary();   // the time it takes follows repeat, delays and key delay
            UpdateExampleNote();
        }

        void EnabledChanged(bool on)
        {
            Edit(m => m.Enabled = on, true);
            if (schedule != null) schedule.Refresh();   // the schedule line says when it is paused
        }

        void TargetKindChanged(TargetKind kind)
        {
            Edit(m => m.Target = kind, false);
            UpdateEditorEnabled();
            if (schedule != null) schedule.Refresh();
        }

        void AltKindChanged(bool specific)
        {
            if (loading || current == null) { UpdateEditorEnabled(); return; }
            if (!specific)
            {
                current.AltTabPartner.ProcessName = "";
                current.AltTabPartner.TitleContains = "";
                partnerEditor.Bind(current.AltTabPartner, () => Edit(x => { }, false));
                c.MarkDirty(SaveParts.Macros);
            }
            UpdateEditorEnabled();
        }

        void NewMacro()
        {
            var m = new MacroDef();
            c.Macros.Add(m);
            c.MarkDirty(SaveParts.Macros);
            listSearch.Text = "";   // or a search hides the new macro
            Reload(m);
            name.Focus();
            name.SelectAll();
        }

        void Duplicate()
        {
            MacroDef m = Selected;
            if (m == null) return;
            MacroDef copy = Json.Deserialize<MacroDef>(Json.Serialize(m));
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = CopyName(m.Name);
            // A hotkey can belong to one macro only, and a copied schedule would run the work twice.
            bool left = !string.IsNullOrEmpty(m.Hotkey) || (m.Schedule != null && m.Schedule.Kind != ScheduleKind.Off);
            copy.Hotkey = "";
            copy.Schedule = new MacroSchedule();
            c.Macros.Insert(c.Macros.IndexOf(m) + 1, copy);
            c.MarkDirty(SaveParts.Macros);
            listSearch.Text = "";
            Reload(copy);
            c.SetStatus("Made “" + copy.Name + "”." + (left ? " Its hotkey and schedule are left off." : ""), false);
        }

        /// <summary>"Name copy", then "Name copy 2" and so on, so a macro that runs another by name never finds two with one name.</summary>
        string CopyName(string name)
        {
            string wanted = name + " copy";
            for (int n = 2; c.Macros.Any(x => string.Equals((x.Name ?? "").Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase)); n++)
                wanted = name + " copy " + n;
            return wanted;
        }

        void Delete()
        {
            MacroDef m = Selected;
            if (m == null) return;
            int index = c.Macros.IndexOf(m);
            c.Macros.Remove(m);
            c.MarkDirty(SaveParts.Macros);
            undoItems = new List<KeyValuePair<int, MacroDef>> { new KeyValuePair<int, MacroDef>(index, m) };
            undoButton.Content = "Undo delete";
            undoButton.Visibility = Visibility.Visible;
            listSearch.Text = "";   // or a search hides the macro that comes next
            Reload(c.Macros.Count == 0 ? null : c.Macros[Math.Min(index, c.Macros.Count - 1)]);
            c.SetStatus("Deleted “" + m.Name + "”. Press Undo delete to put it back.", false);
        }

        void UndoDelete()
        {
            List<KeyValuePair<int, MacroDef>> items = undoItems;
            undoItems = new List<KeyValuePair<int, MacroDef>>();
            undoButton.Visibility = Visibility.Collapsed;
            if (items.Count == 0) return;
            foreach (var item in items.OrderBy(i => i.Key)) c.Macros.Insert(Math.Min(Math.Max(0, item.Key), c.Macros.Count), item.Value);
            c.MarkDirty(SaveParts.Macros);
            listSearch.Text = "";
            MacroDef m = items[0].Value;
            Reload(m);
            c.SetStatus(items.Count > 1 ? "Put back " + items.Count + " example macros." : "Put back “" + m.Name + "”.", false);
        }

        /// <summary>Removes every example macro still as shipped in one go; Undo puts them all back.</summary>
        void RemoveExamples()
        {
            var items = c.Macros.Select((m, i) => new KeyValuePair<int, MacroDef>(i, m)).Where(x => Examples.IsUnchanged(x.Value)).ToList();
            if (items.Count == 0) return;
            foreach (var item in items) c.Macros.Remove(item.Value);
            c.MarkDirty(SaveParts.Macros);
            undoItems = items;
            undoButton.Content = "Undo remove";
            undoButton.Visibility = Visibility.Visible;
            listSearch.Text = "";
            Reload(c.Macros.Count == 0 ? null : c.Macros[0]);
            c.SetStatus("Removed " + items.Count + " example macros. Press Undo remove to put them back.", false);
        }

        /// <summary>Self-test: presses Remove examples; returns how many macros it removed.</summary>
        public int RemoveExampleItems() { int before = c.Macros.Count; RemoveExamples(); return before - c.Macros.Count; }

        /// <summary>Self-test: deletes the selected macro; true when Undo delete is offered.</summary>
        public bool DeleteSelected() { Delete(); return undoButton.Visibility == Visibility.Visible; }

        /// <summary>Self-test: presses Undo delete.</summary>
        public void UndoLastDelete() { UndoDelete(); }

        void UpdateRunButtons()
        {
            bool idle = !c.IsBusy;
            runButton.IsEnabled = idle && current != null;
            bool running = c.Activity.Kind == ActivityKind.Macro;
            stopButton.IsEnabled = running;
            // A greyed button says why.
            runButton.ToolTip = runButton.IsEnabled ? RunTip : current == null ? "Pick or make a macro first." : "Wait until the current macro, recording or playback ends.";
            stopButton.ToolTip = running ? "Stop the running macro." : "No macro is running.";
        }

        void ShowHotkeyError()
        {
            string error;
            if (current != null && c.HotkeyErrors.TryGetValue(current.Id, out error)) hotkeyError.Text = error;
            else hotkeyError.Text = "";
            hotkeyError.Visibility = hotkeyError.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        // ------------------------------------------------------------------ self test hooks

        public string ScriptText { get { return script.Text; } }
        public int CaretIndex { get { return script.CaretIndex; } set { script.CaretIndex = value; } }
        public int StepCount { get { return parsed == null ? 0 : parsed.Steps.Count; } }
        public int ErrorCount { get { return parsed == null ? 0 : parsed.Errors.Count; } }
        public bool RepeatKeyIsSingleKey { get { return repeatKey.SingleKeyOnly; } }
        /// <summary>Self test: rebuilds the list from the current macros.</summary>
        public void ReloadList() { Reload(null); }
        /// <summary>Self test: gives the repeat count box the keyboard focus (which must not change the repeat mode).</summary>
        public void FocusRepeatCount() { repeatTimes.Focus(); }
        /// <summary>Self test: types a repeat count.</summary>
        public void TypeRepeatCount(string text) { repeatTimes.Text = text; }
        public void SelectMacro(int index) { if (index >= 0 && index < list.Items.Count) list.SelectedIndex = index; }
        public void SetScript(string text) { script.Text = text; ShowSteps(); if (codeDirty) { codeDirty = false; PlainFromCode(); } }
        /// <summary>Applies pending parse work immediately (the preview normally follows typing after 150 ms).</summary>
        public void RefreshPreview() { parseTimer.Stop(); ShowSteps(); }
        /// <summary>Self test: selects part of the script, as dragging over it would.</summary>
        public void SelectScript(int start, int length) { script.Select(start, length); }
        /// <summary>Self test: what Multi action's Repeat and Take turns buttons do.</summary>
        public void WrapInRepeat(int times) { InsertBlock(MacroScript.Loop(times), 0, MacroScript.EndLoop); }
        public void AddTakeTurns(int choices) { InsertBlock(MacroScript.Switch, choices - 1, MacroScript.EndSwitch); }
        /// <summary>Self test: types in the command search box.</summary>
        public void SearchCommands(string text) { commandSearch.Text = text; if (text.Length == 0) ApplyLayout(); }
        public string PlainText { get { return editor.Text; } }
        /// <summary>Self test: replaces the steps as if they were typed.</summary>
        public void SetPlain(string text) { editor.Text = text; PlainEdited(); }
        public string PlainSummaryText { get { return plainSummary.Text; } }
        public string StepsSummaryText { get { return stepsSummary.Text; } }
        public void ClickPasteText(bool on) { pasteText.IsChecked = on; pasteText.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); }
        public string ExampleNoteText { get { return exampleNote.Visibility == Visibility.Visible ? exampleNote.Text : ""; } }
        /// <summary>Self test: presses New macro, Duplicate and Run, and reads what they show.</summary>
        public void PressNew() { NewMacro(); }
        public void PressDuplicate() { Duplicate(); }
        public void PressRun() { RunClicked(); }
        /// <summary>F5: runs the selected macro, as the Run button does (nothing while it is greyed out).</summary>
        public void ShortcutRun() { if (runButton.IsEnabled) RunClicked(); }
        /// <summary>Ctrl+F: puts the cursor in the search box.</summary>
        public void FocusSearch() { listSearch.Focus(); listSearch.SelectAll(); }
        public string SelectedName { get { return current == null ? null : current.Name; } }
        public void PressEnabled(bool on) { enabled.IsChecked = on; EnabledChanged(on); }
        /// <summary>Self-test: the line under the schedule.</summary>
        public string ScheduleStatusText { get { return schedule.StatusLine; } }
        public bool ShowsAsOff(MacroDef m) { var item = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, m)); return item != null && ((DockPanel)((StackPanel)item.Content).Children[0]).Children.OfType<Border>().Any(b => "Off".Equals(b.Tag)); }
        /// <summary>Self test: a click on one of the option chips ("repeat", "window", "start", "schedule").</summary>
        public void ClickOptionChip(string section) { ToggleOptionSection(section); }
        /// <summary>Self test: the open option card, or "".</summary>
        public string OpenOptionSection { get { string open = OpenSection; return open.Length > 0 && optionCards[Array.IndexOf(OptionSections, open)].Visibility == Visibility.Visible ? open : ""; } }
        /// <summary>Self test: the four chip values as shown.</summary>
        public string[] OptionChipValues { get { return optionValues.Select(v => v.Text).ToArray(); } }
        public string CurrentEditor { get { return EditorKind; } }
        public PlainTextEditor TextEditor { get { return textEditor; } }
        public KeyTileEditor TileEditor { get { return tileEditor; } }
        /// <summary>Self test: the palette adds to the Stream Deck code (true) or the steps (false).</summary>
        public bool InsertIntoCode { get { return CodeIsTarget; } }
        public bool StreamDeckCodeVisible { get { return CodeView && ReferenceEquals(editorHost.Child, codePanel); } }
        public bool OptionsVisible { get { return OpenOptionSection.Length > 0; } }
        /// <summary>Self test: the Code view on, or back to Lines or Tiles.</summary>
        public void ToggleStreamDeck(bool on) { if (on) ChooseCode(); else ChooseEditor(EditorKind); }
        /// <summary>Self test: opens one option card (on, or the given section) or closes it.</summary>
        public void ToggleOptions(bool on, string section = "repeat") { c.Settings.MacroOptionsSection = on ? section : ""; c.MarkDirty(SaveParts.Settings); ShowOptionCard(); }
        public void ToggleAllCommands(bool on) { c.Settings.ShowAllCommands = on; c.MarkDirty(SaveParts.Settings); ApplyLayout(); }
        public int VisibleCommandGroups { get { return groups.Count(g => g.Root.Visibility == Visibility.Visible); } }
        public string[] OpenCommandGroups { get { return groups.Where(g => g.Open && g.Root.Visibility == Visibility.Visible).Select(g => g.Id).ToArray(); } }
        /// <summary>Self test: opens every group for a screenshot without saving it; SearchCommands("") puts the saved layout back.</summary>
        public void OpenAllCommandGroups() { palette.Visibility = Visibility.Visible; foreach (CommandGroup g in groups) SetOpen(g, true, false); RefreshProCommands(); }
        /// <summary>Self test: fills the colour row and returns what its Add would insert (null when the colour isn't valid).</summary>
        public string ColourCommand(int x, int y, string colour, int seconds) { pixelX.Value = x; pixelY.Value = y; pixelColor.Text = colour; waitPixelSeconds.Value = seconds; return PixelToken(); }
        /// <summary>Self test: the swatch shows the colour typed in.</summary>
        public string SwatchColour { get { var b = pixelSwatch.Background as System.Windows.Media.SolidColorBrush; return b == null ? "" : b.Color.ToString(); } }
    }
}
