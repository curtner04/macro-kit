using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MouseButton = MacroKit.Core.MouseButton;
using System.Windows.Threading;
using MacroKit.Core;
using MacroKit.Macros;

namespace MacroKit.App
{
    /// <summary>The Macros tab: write or click together Super Macro scripts and run them in the window you choose.</summary>
    public sealed class MacrosTab
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
        readonly CheckBox deckSwitch;
        readonly Button viewLines, viewTiles, allCommands, optionsButton;
        Border problemsBox;
        readonly TextBlock optionsSummary = Ui.Text("", "Muted", false, 12.5);
        FrameworkElement palette, deckCard;
        FrameworkElement[] optionCards;
        bool syncing, codeDirty, codeTarget;

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
            var newButton = Ui.Button("New macro", NewMacro, null, "Add an empty macro.");
            duplicateButton = Ui.Button("Duplicate", Duplicate, null, "Copy the selected macro.");
            deleteButton = Ui.Button("Delete", Delete, "Ghost", "Delete the selected macro. You can undo it.");
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
            listSearch.ToolTip = "Show only the macros whose name, steps or hotkey hold these words.";
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
            name.TextChanged += (s, e) => Edit(m => m.Name = name.Text, true);
            runButton = Ui.Button(Ui.Row(7, Ui.Shape("play", 11, "AccentFg"), new TextBlock { Text = "Run" }), () => { if (current != null) c.StartMacro(current, RunOrigin.MainWindow); }, "Primary", "Run this macro. Macro Kit steps aside and the macro runs in the window you chose below.");
            stopButton = Ui.Button(Ui.Row(7, Ui.Shape("stop", 11, "Text"), new TextBlock { Text = "Stop" }), () => c.Stop(), null, "Stop the running macro.");
            deckSwitch = new CheckBox { Content = "Show code", IsChecked = c.Settings.ShowStreamDeckCode, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Show this macro as code (Super Macro text). Copy it into a Super Macro action on an Elgato Stream Deck, or edit it there." };
            Ui.Style(deckSwitch, "Switch");
            deckSwitch.Margin = new Thickness(14, 0, 4, 0);
            deckSwitch.Click += (s, e) => { c.Settings.ShowStreamDeckCode = deckSwitch.IsChecked == true; c.MarkDirty(SaveParts.Settings); ApplyLayout(); };
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(name);
            Grid.SetColumn(deckSwitch, 1);
            header.Children.Add(deckSwitch);
            Grid.SetColumn(runButton, 2);
            runButton.Margin = new Thickness(10, 0, 0, 0);
            runButton.MinWidth = 84;
            header.Children.Add(runButton);
            Grid.SetColumn(stopButton, 3);
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
            script.GotKeyboardFocus += (s, e) => codeTarget = true;
            parseTimer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
            parseTimer.Tick += (s, e) => { parseTimer.Stop(); ShowSteps(); if (codeDirty) { codeDirty = false; PlainFromCode(); } };

            steps.SelectionChanged += (s, e) => SelectStepInScript();
            stepsEmpty = Ui.Para("Nothing yet. Add commands above or type a script.");
            stepsEmpty.FontSize = 12;
            stepsEmpty.Margin = new Thickness(8, 4, 0, 0);

            // ---- the steps, in the editor the user chose
            editorHost.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, e) => codeTarget = false));
            viewLines = Ui.Button("Lines", () => ChooseEditor(EditorKinds.Text), "Chip", "Type the steps, one per line.");
            viewTiles = Ui.Button("Tiles", () => ChooseEditor(EditorKinds.Tiles), "Chip", "The same steps as key tiles you can click, drag and add to.");
            viewLines.Margin = new Thickness(0, 0, 4, 0);
            allCommands = Ui.Button("", () => { c.Settings.ShowAllCommands = !c.Settings.ShowAllCommands; c.MarkDirty(SaveParts.Settings); ApplyLayout(); }, "Link",
                "Every command Macro Kit has, in groups, with boxes to fill in.");
            palette = BuildPalette();
            var stepsTitle = new DockPanel();
            var viewToggle = Ui.Row(0, viewLines, viewTiles);
            viewToggle.ToolTip = "Two views of the same steps. Settings has this too.";
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

            // ---- Stream Deck code (only with the Stream Deck switch on)
            var copyCode = Ui.Button("Copy", CopyCode, "Small", "Copy the Super Macro text, ready to paste into a Super Macro action on your Stream Deck.");
            var deckTitle = new DockPanel();
            DockPanel.SetDock(copyCode, Dock.Right);
            deckTitle.Children.Add(copyCode);
            deckTitle.Children.Add(Ui.Heading("Stream Deck code"));

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
            repeatTimes.Margin = new Thickness(2, 0, 7, 0);
            var timesRow = Ui.Row(0, times, repeatTimes, Ui.Text("times", null));
            var repeatRow = Ui.Row(18, once, timesRow, continuous);
            var delays = Ui.Row(7, Ui.Text("Wait", "Muted", false, 12), repeatDelay, Ui.Text("ms between repeats", "Muted", false, 12),
                Ui.Text("·", "Muted"), keyDelay, Ui.Text("ms after each key or click", "Muted", false, 12));
            ((FrameworkElement)delays.Children[3]).Margin = new Thickness(12, 0, 0, 0);
            ((FrameworkElement)delays.Children[4]).Margin = new Thickness(12, 0, 0, 0);
            pasteText = new CheckBox { Content = "Paste text instead of typing it (much faster)" };
            pasteText.ToolTip = "A whole block of text appears at once, through the clipboard, and your clipboard is put back afterwards. Short text (under 4 characters) is still typed. Turn it off for games, remote desktops or boxes that don't accept pasting.";
            pasteText.Click += (s, e) => Edit(m => m.PasteText = pasteText.IsChecked == true, false);
            var repeatCard = Ui.Card(Ui.Stack(8, Ui.Heading("Repeat and speed"), repeatRow, delays, pasteText));
            deckCard = Ui.Card(Ui.Stack(10, deckTitle,
                Ui.Para("The same macro as Super Macro text. Paste it into a Super Macro action on your Stream Deck, or change it here and the steps above follow. Problems in the text are listed under it.", "Muted", 12),
                BuildScriptAndSteps(), newLines));

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
            var altCaption = Ui.Caption("WHEN THE SCRIPT PRESSES ALT+TAB, GO TO");
            var altNote = Ui.Para("Choose a window here and Alt+Tab always jumps between the same two windows, which keeps a copy-and-paste macro on track.", "Muted", 12);
            var whereCard = Ui.Card(Ui.Stack(8,
                Ui.Heading("Where it runs"),
                Ui.Caption("TYPE INTO"), runPrevious, runSpecific, targetBlock,
                altCaption, altNote, altWindows, altSpecific, partnerBlock));
            altCaption.Margin = new Thickness(0, 16, 0, 0);
            altNote.Margin = new Thickness(0, 0, 0, 2);

            // ---- hotkey and deck
            hotkey = new HotkeyBox(true, 190);
            hotkey.Changed += () => Edit(m => m.Hotkey = hotkey.Value.IsEmpty ? "" : hotkey.Value.ToString(), true);
            if (!c.InputEnabled) hotkey.ToolTip = "Hotkeys are off in this session (input is turned off).";
            showOnDeck = new CheckBox { Content = "Show this macro as a button on the Deck" };
            showOnDeck.Click += (s, e) => { Edit(m => m.ShowOnDeck = showOnDeck.IsChecked == true, true); deckLook.IsEnabled = showOnDeck.IsChecked == true; };
            deckColor = new ComboBox { Width = 110, ToolTip = "A coloured stripe on the macro's Deck button, so it is quick to find." };
            deckColor.Items.Add("No colour");
            foreach (string name in DeckWindow.Colors) deckColor.Items.Add(name);
            deckColor.SelectionChanged += (s, e) => Edit(m => m.DeckColor = deckColor.SelectedIndex <= 0 ? "" : (string)deckColor.SelectedItem, false);
            deckIcon = new TextBox { Width = 52, MaxLength = 4, ToolTip = "An emoji or symbol shown before the name on the Deck, such as 📋 or ★. Press Win+. for emoji." };
            deckIcon.TextChanged += (s, e) => Edit(m => m.DeckIcon = deckIcon.Text.Trim(), false);
            deckLook = Ui.Row(10, Ui.Text("Colour", "Muted", false, 12), deckColor, Ui.Text("Icon", "Muted", false, 12), deckIcon);
            deckLook.Margin = new Thickness(26, 0, 0, 0);
            var launchCard = Ui.Card(Ui.Stack(8,
                Ui.Heading("Start it with"),
                Ui.Row(10, Ui.Text("Hotkey", "Muted", false, 12), hotkey.WithClear()),
                hotkeyError,
                showOnDeck,
                deckLook));
            hotkeyError.TextWrapping = TextWrapping.Wrap;

            var editorStack = new StackPanel();
            var headerCard = Ui.Card(header);
            editorStack.Children.Add(headerCard);
            editorStack.Children.Add(scriptCard);
            editorStack.Children.Add(deckCard);
            // ---- options: one line saying how it runs; the cards open under it
            optionsButton = Ui.Button("", () => { c.Settings.ShowMacroOptions = !c.Settings.ShowMacroOptions; c.MarkDirty(SaveParts.Settings); ApplyLayout(); }, null,
                "Repeat, which window it runs in, its hotkey and schedule.");
            optionsButton.MinWidth = 110;
            var optionsRow = new DockPanel();
            DockPanel.SetDock(optionsButton, Dock.Right);
            optionsRow.Children.Add(optionsButton);
            var optionsLabel = Ui.Text("Options", "Text", true, 13);
            optionsLabel.Margin = new Thickness(0, 0, 12, 0);
            optionsRow.Children.Add(Ui.Row(0, optionsLabel, optionsSummary));
            var optionsCard = Ui.Card(optionsRow);
            editorStack.Children.Add(optionsCard);
            editorStack.Children.Add(repeatCard);
            editorStack.Children.Add(whereCard);
            editorStack.Children.Add(launchCard);
            schedule = new ScheduleEditor(c, () => owner);
            editorStack.Children.Add(schedule.Root);
            optionCards = new FrameworkElement[] { repeatCard, whereCard, launchCard, schedule.Root };
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

        static FrameworkElement Indent(FrameworkElement e) { e.Margin = new Thickness(26, e.Margin.Top, 0, e.Margin.Bottom); return e; }

        // ------------------------------------------------------------------ palette

        /// <summary>One collapsible group of commands. Which groups are open is saved in Settings.OpenCommandGroups.</summary>
        sealed class CommandGroup
        {
            public string Id, Words;
            public FrameworkElement Root, Body;
            public TextBlock Chevron;
            public bool Open;
        }

        const string ChevronOpen = "\uE70D", ChevronClosed = "\uE76C";

        FrameworkElement BuildPalette()
        {
            functionKey.ToolTip = "Function keys";
            for (int i = 1; i <= 24; i++) functionKey.Items.Add("F" + i);
            functionKey.SelectedIndex = 4;
            foreach (string key in new[] { "CTRL", "SHIFT", "ALT", "WIN", "SPACE", "ENTER", "TAB", "ESC", "UP", "DOWN", "LEFT", "RIGHT" }) holdKey.Items.Add(key);
            for (char ch = 'A'; ch <= 'Z'; ch++) holdKey.Items.Add(ch.ToString());
            holdKey.SelectedIndex = 0;
            // Show the selection while a palette button has the focus, so it is clear what a block will wrap.
            script.IsInactiveSelectionHighlightEnabled = true;

            var list = new StackPanel();
            list.Children.Add(Group("keys", "Keys", "Shortcuts, F keys, hold and release", "copy paste cut select all alt tab enter esc arrows home end page function hotkey combination modifier ctrl shift win", BuildKeysGroup()));
            list.Children.Add(Group("multi", "Multi action", "Repeat steps, run another macro, take turns, pick at random", "loop repeat times again switch toggle alternate random choice macro chain nested", BuildMultiGroup()));
            list.Children.Add(Group("wait", "Wait and repeat a key", "Pause, press one key several times", "pause delay sleep wait ms seconds keypress repeat", BuildWaitGroup()));
            list.Children.Add(Group("text", "Type text and notes", "Text to type, a note to yourself", "type text string phrase comment note", BuildTextGroup()));
            list.Children.Add(Group("mouse", "Mouse", "Click, move, scroll", "mouse click left right middle double move position scroll wheel pointer cursor", BuildMouseGroup()));
            list.Children.Add(Group("apps", "Apps and websites", "Open a program, file or web page, close a program, lock the PC", "open run launch start program app exe file folder website url browser link close quit exit kill lock", BuildAppsGroup()));
            list.Children.Add(Group("windows", "Windows", "Switch to a window, wait for one to open", "window focus activate switch bring front wait title", BuildWindowsGroup()));
            list.Children.Add(Group("media", "Media and sound", "Play, pause, skip, volume, play a sound file", "media play pause next previous track stop mute volume louder quieter sound audio wav mp3 music", BuildMediaGroup()));
            list.Children.Add(Group("vars", "Variables and clipboard", "Remember a value, type it, use the clipboard", "variable var value set output clipboard copy remember", BuildVariablesGroup()));
            list.Children.Add(Group("conditions", "Conditions", "Do steps only if a value or window matches", "if else condition compare window when check", BuildConditionsGroup(), Ui.Pill("PRO", "Good", "GoodBg", 10)));
            noMatches = Ui.Text("No command matches. Try another word.", "Muted", false, 12);
            noMatches.Margin = new Thickness(12, 4, 0, 6);
            noMatches.Visibility = Visibility.Collapsed;
            list.Children.Add(noMatches);

            // The search box, with its hint drawn behind the text.
            commandSearch.Width = 190;
            commandSearch.ToolTip = "Type a word such as copy, loop, volume or window to find the command.";
            var searchHint = Ui.Text("Find a command", "Muted", false, 12);
            searchHint.IsHitTestVisible = false;
            searchHint.Margin = new Thickness(8, 0, 0, 0);
            var search = new Grid { VerticalAlignment = VerticalAlignment.Center };
            search.Children.Add(commandSearch);
            search.Children.Add(searchHint);
            commandSearch.TextChanged += (s, e) => { searchHint.Visibility = commandSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; ApplySearch(); };
            var closeAll = Ui.Button("Close all", () => { commandSearch.Text = ""; foreach (CommandGroup g in groups) SetOpen(g, false, false); SaveOpenGroups(); }, "Link", "Fold every group away.");
            closeAll.Margin = new Thickness(10, 0, 0, 0);

            var title = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var tools = Ui.Row(0, search, closeAll);
            DockPanel.SetDock(tools, Dock.Right);
            title.Children.Add(tools);
            title.Children.Add(Ui.Text("Add commands at the cursor", "Muted", false, 12));

            var frame = new Border { Padding = new Thickness(8, 8, 8, 6), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
            Ui.Ref(frame, Border.BackgroundProperty, "Card2");
            Ui.Ref(frame, Border.BorderBrushProperty, "Border");
            title.Margin = new Thickness(4, 0, 0, 6);
            frame.Child = Ui.Stack(0, title, list);

            // Only the first saved open group opens (earlier versions could keep several open at once).
            var open = (c.Settings.OpenCommandGroups ?? new List<string>()).Take(1).ToList();
            if (c.Settings.OpenCommandGroups != null && c.Settings.OpenCommandGroups.Count > 1) c.Settings.OpenCommandGroups = open;
            foreach (CommandGroup g in groups) SetOpen(g, open.Contains(g.Id), false);
            RefreshProCommands();
            return frame;
        }

        FrameworkElement BuildKeysGroup()
        {
            // One labelled row per kind of key, each in its own colour, so a key is quick to find.
            var keys = new Grid();
            keys.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            keys.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            KeyGroup(keys, "Clipboard", "KeyClip",
                Chip("Copy", () => Keys("CTRL", "C")), Chip("Cut", () => Keys("CTRL", "X")), Chip("Paste", () => Keys("CTRL", "V")), Chip("Select all", () => Keys("CTRL", "A")));
            KeyGroup(keys, "Switch", "KeySwitch",
                Chip("Alt+Tab", () => Keys("ALT", "TAB")), Chip("Ctrl+Tab", () => Keys("CTRL", "TAB")));
            KeyGroup(keys, "Edit", "KeyEdit",
                Chip("Enter", () => Keys("ENTER")), Chip("Tab", () => Keys("TAB")), Chip("Esc", () => Keys("ESC")),
                Chip("Backspace", () => Keys("BACK")), Chip("Delete", () => Keys("DELETE")));
            KeyGroup(keys, "Arrows", "KeyArrow",
                Chip("↑", () => Keys("UP"), "Up arrow"), Chip("↓", () => Keys("DOWN"), "Down arrow"), Chip("←", () => Keys("LEFT"), "Left arrow"), Chip("→", () => Keys("RIGHT"), "Right arrow"));
            KeyGroup(keys, "Jump", "KeyJump",
                Chip("Home", () => Keys("HOME")), Chip("End", () => Keys("END")), Chip("PgUp", () => Keys("PGUP"), "Page Up"), Chip("PgDn", () => Keys("PGDN"), "Page Down"));
            var fLabel = Ui.Text("F keys", "Muted", true, 11);
            AddKeyRow(keys, fLabel, Ui.WrapRow(6, functionKey, AddButton(() => Keys((string)functionKey.SelectedItem), "Insert the function key.")));
            fLabel.VerticalAlignment = VerticalAlignment.Center;
            fLabel.Margin = new Thickness(0, 0, 12, 5);

            var customRow = Ui.WrapRow(6, customShortcut, AddButton(() => { var k = customShortcut.Keys(); return k.Count == 0 ? null : MacroScript.Shortcut(k); }, "Press these keys together."));
            var holdRow = Ui.WrapRow(6, holdKey, Ui.Button("Hold down", null, "Chip"), Ui.Button("Release", null, "Chip"));
            ((Button)holdRow.Children[1]).Click += (s, e) => InsertKey(true);
            ((Button)holdRow.Children[2]).Click += (s, e) => InsertKey(false);
            return Ui.Stack(8, keys,
                Ui.Stack(4, Ui.Caption("YOUR OWN SHORTCUT"), customRow),
                Ui.Stack(4, Ui.Caption("HOLD OR RELEASE A KEY"), holdRow));
        }

        FrameworkElement BuildMultiGroup()
        {
            var loopRow = Ui.WrapRow(6, Ui.Text("Repeat", "Muted", false, 12), loopTimes, Ui.Text("times", "Muted", false, 12),
                ActionButton("Add", () => { InsertBlock(MacroScript.Loop(loopTimes.Value), 0, MacroScript.EndLoop); return ""; },
                    "Repeat the steps you selected in the script. With nothing selected, an empty repeat block goes in at the cursor for you to fill."));
            otherMacro.ToolTip = "The macro to run. It runs once, in the same window, then this macro carries on.";
            otherMacro.DropDownOpened += (s, e) => FillMacroNames();
            var macroRow = Ui.WrapRow(6, Ui.Text("Run the macro", "Muted", false, 12), otherMacro,
                AddButton(() => otherMacro.Text.Trim().Length == 0 ? null : MacroScript.Macro(otherMacro.Text), "Run another of your macros here, then carry on."));
            var choiceRow = Ui.WrapRow(6, choiceCount, Ui.Text("choices:", "Muted", false, 12),
                ActionButton("Take turns", () => { InsertBlock(MacroScript.Switch, choiceCount.Value - 1, MacroScript.EndSwitch); return ""; },
                    "Each time the macro runs it does the next choice: mute, then unmute; or scene 1, 2, 3 and round again. Choices are split by {{OR}}."),
                ActionButton("Pick at random", () => { InsertBlock(MacroScript.Random, choiceCount.Value - 1, MacroScript.EndRandom); return ""; },
                    "Each time, one of the choices runs, picked at random. Choices are split by {{OR}}."),
                Chip("Next choice", () => MacroScript.Or, "Starts the next choice inside a take-turns or random block."));
            var hint = Ui.Para("Select steps in the script first and the block goes around them. Blocks can sit inside each other.", "Muted", 12);
            return Ui.Stack(6,
                Ui.Stack(4, Ui.Caption("REPEAT STEPS"), loopRow),
                Ui.Stack(4, Ui.Caption("ANOTHER MACRO"), macroRow),
                Ui.Stack(4, Ui.Caption("TAKE TURNS OR PICK ONE"), choiceRow),
                hint);
        }

        FrameworkElement BuildWaitGroup()
        {
            var pauseRow = Ui.WrapRow(6, pauseMs, Ui.Text("ms", "Muted", false, 12), AddButton(() => MacroScript.Pause(pauseMs.Value), "Wait this long before the next command."),
                Chip("50", () => MacroScript.Pause(50), "Wait 50 ms"), Chip("100", () => MacroScript.Pause(100), "Wait 100 ms"),
                Chip("250", () => MacroScript.Pause(250), "Wait 250 ms"), Chip("500", () => MacroScript.Pause(500), "Wait 500 ms"), Chip("1000", () => MacroScript.Pause(1000), "Wait 1 second"));
            var repeatRow = Ui.WrapRow(6, repeatKey, Ui.Text("×", "Muted"), repeatCount, Ui.Text("every", "Muted", false, 12), repeatEvery, Ui.Text("ms", "Muted", false, 12),
                AddButton(() => repeatKey.Value.IsEmpty ? null : MacroScript.KeyPress(repeatKey.Value.Vk, repeatCount.Value, repeatEvery.Value), "Press this key several times."));
            repeatKey.SingleKeyOnly = true;
            repeatKey.ToolTip = "Click, then press the one key to repeat. Use \"Your own shortcut\" under Keys for a key with Ctrl, Alt, Shift or Win.";
            repeatKey.Placeholder = "Press a key";
            repeatKey.SetText("");
            return Ui.Stack(6,
                Ui.Stack(4, Ui.Caption("PAUSE"), pauseRow),
                Ui.Stack(4, Ui.Caption("REPEAT A KEY"), repeatRow));
        }

        FrameworkElement BuildTextGroup()
        {
            var typeRow = DockedAdd(typeText, AddButton(() => typeText.Text.Length == 0 ? null : MacroScript.Text(typeText.Text), "Type this text."));
            typeText.ToolTip = "Text to type, exactly as written.";
            var commentRow = DockedAdd(commentText, AddButton(() => commentText.Text.Trim().Length == 0 ? null : MacroScript.Comment(commentText.Text) + "\n", "A note to yourself; it is not run."));
            commentText.ToolTip = "A note to yourself on its own line.";
            return Ui.Stack(6,
                Ui.Stack(4, Ui.Caption("TYPE TEXT"), typeRow),
                Ui.Stack(4, Ui.Caption("NOTE"), commentRow));
        }

        FrameworkElement BuildMouseGroup()
        {
            var clicks = Ui.Wrap(Chip("Left click", () => MacroScript.Click(MouseButton.Left)), Chip("Right click", () => MacroScript.Click(MouseButton.Right)),
                Chip("Middle click", () => MacroScript.Click(MouseButton.Middle)), Chip("Double click", () => MacroScript.DoubleClick(MouseButton.Left)));
            pickPosition = Ui.Button("Pick…", PickPosition, "Small", "Move the pointer to the spot you want. After 3 seconds its position is filled in.");
            var moveRow = Ui.WrapRow(6, Ui.Text("Move to", "Muted", false, 12), mouseX, Ui.Text(",", "Muted"), mouseY, pickPosition,
                AddButton(() => MacroScript.MouseTo(mouseX.Value, mouseY.Value), "Move the pointer to this spot on the screen."));
            var scrollRow = Ui.WrapRow(6, Ui.Text("Scroll", "Muted", false, 12),
                Ui.Button("Up", null, "Chip"), Ui.Button("Down", null, "Chip"), scrollNotches, Ui.Text("notches", "Muted", false, 12));
            ((Button)scrollRow.Children[1]).Click += (s, e) => Insert(MacroScript.ScrollUp(scrollNotches.Value));
            ((Button)scrollRow.Children[2]).Click += (s, e) => Insert(MacroScript.ScrollDown(scrollNotches.Value));
            return Ui.Stack(4, clicks, moveRow, scrollRow);
        }

        FrameworkElement BuildAppsGroup()
        {
            openTarget.ToolTip = "A program, file or folder, such as notepad, C:\\Reports\\week.xlsx or a shortcut. Put a path in quotes to add arguments after it.";
            var openRow = DockedAdd(openTarget, Ui.Row(6,
                Ui.Button("Browse…", () => Browse(openTarget, "Choose what to open", "Programs and shortcuts (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All files (*.*)|*.*"), "Small", "Pick a program or file."),
                AddButton(() =>
                {
                    if (openTarget.Text.Trim().Length == 0) return null;
                    string target, arguments;
                    MacroParser.SplitRun(openTarget.Text, out target, out arguments);
                    return MacroScript.Run(target, arguments);
                }, "Open it, as if you double-clicked it.")));
            websiteText.ToolTip = "A web address, such as example.com. It opens in your default browser.";
            var websiteRow = DockedAdd(websiteText, AddButton(() => websiteText.Text.Trim().Length == 0 ? null : MacroScript.Website(websiteText.Text), "Open this web page."));
            closeProgram.ToolTip = "The program's name, such as notepad or chrome. Pick one that is open now or type it.";
            closeProgram.DropDownOpened += (s, e) => FillCombo(closeProgram, c.Windows.ListSwitchableWindows()
                .Select(w => ProgramName(w.ProcessName)).Where(n => n.Length > 0 && !n.Equals("MacroKit", StringComparison.OrdinalIgnoreCase)));
            var closeRow = Ui.WrapRow(6, closeProgram,
                AddButton(() => closeProgram.Text.Trim().Length == 0 ? null : MacroScript.Close(ProgramName(closeProgram.Text)), "Ask the program's windows to close, as if you pressed their close button. Unsaved work still asks first."),
                Chip("Lock the PC", () => MacroScript.Lock, "Lock Windows, like Win+L"));
            return Ui.Stack(6,
                Ui.Stack(4, Ui.Caption("OPEN A PROGRAM, FILE OR FOLDER"), openRow),
                Ui.Stack(4, Ui.Caption("OPEN A WEBSITE"), websiteRow),
                Ui.Stack(4, Ui.Caption("CLOSE A PROGRAM"), closeRow));
        }

        FrameworkElement BuildWindowsGroup()
        {
            windowName.ToolTip = "Part of the window's title or its program name. Pick an open window or type it.";
            windowName.DropDownOpened += (s, e) => FillCombo(windowName, c.Windows.ListSwitchableWindows()
                .Select(w => (w.Title ?? "").Trim()).Where(t => t.Length > 0));
            var focusRow = Ui.WrapRow(6, windowName,
                ActionButton("Switch to it", () => windowName.Text.Trim().Length == 0 ? null : MacroScript.Focus(windowName.Text),
                    "Bring this window to the front. The rest of the macro runs in it."));
            var waitRow = Ui.WrapRow(6, Ui.Text("Wait up to", "Muted", false, 12), waitWindowSeconds, Ui.Text("seconds for it to open", "Muted", false, 12),
                ActionButton("Add", () => windowName.Text.Trim().Length == 0 ? null : MacroScript.WaitWindow(windowName.Text, waitWindowSeconds.Value * 1000),
                    "Wait until a window like this opens (after Open, for example), then carry on in it. The macro stops if it doesn't open in time."));
            return Ui.Stack(4, focusRow, waitRow);
        }

        FrameworkElement BuildMediaGroup()
        {
            var mediaChips = Ui.Wrap(Chip("Play/Pause", () => Keys("MEDIA_PLAY_PAUSE")), Chip("Next", () => Keys("MEDIA_NEXT_TRACK"), "Next track"),
                Chip("Previous", () => Keys("MEDIA_PREV_TRACK"), "Previous track"), Chip("Stop", () => Keys("MEDIA_STOP"), "Stop playing"),
                Chip("Mute", () => Keys("VOLUME_MUTE"), "Mute or unmute"), Chip("Volume up", () => Keys("VOLUME_UP"), "The volume key: one step up"),
                Chip("Volume down", () => Keys("VOLUME_DOWN"), "The volume key: one step down"));
            var volumeRow = Ui.WrapRow(6, Ui.Text("Set the volume to", "Muted", false, 12), volumePercent, Ui.Text("%", "Muted", false, 12),
                AddButton(() => MacroScript.Volume(volumePercent.Value), "Set the speaker volume to exactly this."),
                Chip("+10%", () => MacroScript.VolumeBy(10), "Turn the volume up by 10%"), Chip("−10%", () => MacroScript.VolumeBy(-10), "Turn the volume down by 10%"));
            soundFile.ToolTip = "A .wav or .mp3 file to play. The macro carries on while it plays.";
            var soundRow = DockedAdd(soundFile, Ui.Row(6,
                Ui.Button("Browse…", () => Browse(soundFile, "Choose a sound", "Sounds (*.wav;*.mp3;*.wma)|*.wav;*.mp3;*.wma|All files (*.*)|*.*"), "Small", "Pick a sound file."),
                AddButton(() => soundFile.Text.Trim().Length == 0 ? null : MacroScript.PlaySound(soundFile.Text), "Play this sound.")));
            return Ui.Stack(6,
                Ui.Stack(4, Ui.Caption("MEDIA KEYS"), mediaChips),
                Ui.Stack(4, Ui.Caption("VOLUME"), volumeRow),
                Ui.Stack(4, Ui.Caption("PLAY A SOUND"), soundRow));
        }

        FrameworkElement BuildVariablesGroup()
        {
            varName.Width = 90;
            varName.ToolTip = "The variable's name, such as row. Use it in the script as $row.";
            varValue.Width = 150;
            varValue.ToolTip = "The value. It can use other variables, such as $row.";
            var setRow = Ui.WrapRow(6, Ui.Text("Name", "Muted", false, 12), varName, Ui.Text("value", "Muted", false, 12), varValue,
                ActionButton("Set", () => VarName() == null ? null : MacroScript.SetVar(VarName(), varValue.Text), "Remember this value under this name."),
                ActionButton("Type it", () => VarName() == null ? null : MacroScript.OutputVar(VarName()), "Type the variable's value."),
                ActionButton("From the clipboard", () => VarName() == null ? null : MacroScript.VarFromClipboard(VarName()), "Remember what is on the clipboard under this name."),
                ActionButton("Value to the clipboard", () => MacroScript.SetClipboard(varValue.Text), "Put the value on the clipboard."));
            return setRow;
        }

        FrameworkElement BuildConditionsGroup()
        {
            ifLeft.Width = 90;
            ifLeft.ToolTip = "Usually a variable, such as $count.";
            ifRight.Width = 90;
            foreach (string op in new[] { "=", "!=", "<", ">", "<=", ">=", "~" }) ifOp.Items.Add(op);
            ifOp.SelectedItem = "=";
            ifOp.ToolTip = "~ means \"contains\".";
            var ifRow = Ui.WrapRow(6, Ui.Text("If", "Muted", false, 12), ifLeft, ifOp, ifRight,
                ActionButton("Add", () => { if (ifLeft.Text.Trim().Length == 0) return null; InsertBlock(MacroScript.If(ifLeft.Text.Trim(), (string)ifOp.SelectedItem, ifRight.Text.Trim()), 0, MacroScript.EndIf); return ""; },
                    "Do the selected steps only when this is true."));
            ifWindow.ToolTip = "Part of the title or program name of the window the macro is typing into.";
            var windowRow = DockedAdd(ifWindow, Ui.Row(6,
                ActionButton("Add", () => { if (ifWindow.Text.Trim().Length == 0) return null; InsertBlock(MacroScript.IfWindow(ifWindow.Text.Trim()), 0, MacroScript.EndIf); return ""; },
                    "Do the selected steps only when the macro's window matches."),
                Chip("Otherwise", () => MacroScript.Else, "{{ELSE}}: the steps after it run when the condition is false.")));
            proCommands = Ui.Stack(6,
                Ui.Stack(4, Ui.Caption("IF A VALUE MATCHES"), ifRow),
                Ui.Stack(4, Ui.Caption("IF THE WINDOW IS"), windowRow));
            proNote = Ui.Note("Conditions come with Macro Kit Pro. Enter your license key in Settings. Repeat, take turns and random blocks in Multi action are free.", "Info");
            return Ui.Stack(0, proNote, proCommands);
        }

        void RefreshProCommands()
        {
            if (proCommands == null) return;
            bool pro = Edition.IsPro;
            proCommands.IsEnabled = pro;
            proNote.Visibility = pro ? Visibility.Collapsed : Visibility.Visible;
        }

        FrameworkElement Group(string id, string title, string hint, string words, FrameworkElement body, FrameworkElement badge = null)
        {
            var g = new CommandGroup { Id = id, Words = (title + " " + hint + " " + words).ToLowerInvariant(), Body = body };
            g.Chevron = Ui.Glyph(ChevronClosed, 10, "Muted");
            g.Chevron.Width = 16;
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.Children.Add(g.Chevron);
            var titleText = Ui.Text(title, "Text", true, 12.5);
            Grid.SetColumn(titleText, 1);
            head.Children.Add(titleText);
            if (badge != null)
            {
                badge.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(badge, 2);
                head.Children.Add(badge);
            }
            var hintText = Ui.Text(hint, "Muted", false, 12);
            hintText.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(hintText, 3);
            head.Children.Add(hintText);
            var header = Ui.Button(head, null, "MenuBtn", "Show or hide these commands.");
            header.Padding = new Thickness(6, 5, 8, 5);
            header.HorizontalAlignment = HorizontalAlignment.Stretch;
            System.Windows.Automation.AutomationProperties.SetName(header, title);
            header.Click += (s, e) =>
            {
                bool opening = !g.Open;
                // One group at a time, so the commands never grow into a page of their own. A search can still show several.
                if (opening && commandSearch.Text.Trim().Length == 0)
                    foreach (CommandGroup other in groups) if (!ReferenceEquals(other, g) && other.Open) SetOpen(other, false, false);
                SetOpen(g, opening, true);
                if (g.Open && id == "conditions") RefreshProCommands();
                if (g.Open) header.BringIntoView();
            };
            body.Margin = new Thickness(28, 4, 6, 10);
            g.Root = Ui.Stack(0, header, body);
            groups.Add(g);
            return g.Root;
        }

        void SetOpen(CommandGroup g, bool open, bool save)
        {
            g.Open = open;
            g.Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            g.Chevron.Text = open ? ChevronOpen : ChevronClosed;
            if (save) SaveOpenGroups();
        }

        void SaveOpenGroups()
        {
            // While a search is showing its matches, opening and closing is only for the moment.
            if (commandSearch.Text.Trim().Length > 0) return;
            var open = groups.Where(g => g.Open).Select(g => g.Id).ToList();
            var saved = c.Settings.OpenCommandGroups ?? new List<string>();
            if (open.SequenceEqual(saved)) return;
            c.Settings.OpenCommandGroups = open;
            c.MarkDirty(SaveParts.Settings);
        }

        /// <summary>Shows only the groups that match every word typed in the search box, opened; an empty box brings back the saved layout.</summary>
        void ApplySearch()
        {
            string[] words = commandSearch.Text.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int shown = 0;
            var saved = c.Settings.OpenCommandGroups ?? new List<string>();
            foreach (CommandGroup g in groups)
            {
                bool match = words.All(w => g.Words.Contains(w));
                g.Root.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                if (match) shown++;
                SetOpen(g, words.Length > 0 ? match : saved.Contains(g.Id), false);
            }
            noMatches.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (words.Length > 0) RefreshProCommands();
        }

        /// <summary>
        /// Puts a block into the script around the selected text: start, the selection, an {{OR}} for each further choice, end.
        /// With nothing selected the cursor lands inside the empty block. Line breaks only go in when they don't press Enter.
        /// </summary>
        void InsertBlock(string start, int separators, string end)
        {
            if (current == null) return;
            if (!CodeIsTarget)
            {
                // The block in plain words: "repeat 3 times" ... "end repeat".
                string[] words = PlainMacro.ToPlain(start + string.Concat(Enumerable.Repeat(MacroScript.Or, separators)) + end, true)
                    .Split(new[] { "\r\n" }, StringSplitOptions.None);
                editor.WrapBlock(words[0], separators, words[words.Length - 1]);
                return;
            }
            string gap = current.NewLineIsEnter ? "" : "\n";
            string inside = script.SelectedText;
            string text = start + gap + inside;
            int caret = inside.Length == 0 ? text.Length : -1;
            for (int i = 0; i < separators; i++) text += gap + MacroScript.Or + gap;
            text += gap + end;
            if (caret < 0) caret = text.Length;
            int at = Math.Min(script.SelectionStart, script.Text.Length);
            script.SelectedText = text;
            script.CaretIndex = at + caret;
            script.SelectionLength = 0;
            script.Focus();
            script.ScrollToLine(script.GetLineIndexFromCharacterIndex(script.CaretIndex));
        }

        void FillMacroNames()
        {
            FillCombo(otherMacro, c.Macros.Where(m => m != current).Select(m => (m.Name ?? "").Trim()).Where(n => n.Length > 0));
        }

        /// <summary>Refills an editable combo box's list and keeps what is typed in it.</summary>
        static void FillCombo(ComboBox box, IEnumerable<string> items)
        {
            string typed = box.Text;
            box.Items.Clear();
            foreach (string item in items.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) box.Items.Add(item);
            box.Text = typed;
        }

        static string ProgramName(string processName)
        {
            string name = (processName ?? "").Trim();
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
        }

        string VarName()
        {
            string n = varName.Text.Trim().TrimStart('$');
            return n.Length == 0 || n.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_') ? null : n;
        }

        void Browse(TextBox box, string title, string filter)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, DereferenceLinks = false };
            if (dialog.ShowDialog(owner) == true) box.Text = dialog.FileName;
        }

        /// <summary>Adds a row of key chips tinted with a key-group colour (tint = theme key; tint + "Bg" is the chip fill).</summary>
        static void KeyGroup(Grid grid, string label, string tint, params Button[] chips)
        {
            foreach (Button chip in chips)
            {
                Ui.Ref(chip, Control.BackgroundProperty, tint + "Bg");
                Ui.Ref(chip, Control.BorderBrushProperty, tint + "Bg");
            }
            var wrap = Ui.Wrap(chips);
            wrap.Margin = new Thickness(0, 0, 0, 2);
            AddKeyRow(grid, Ui.Text(label, tint, true, 11), wrap);
        }

        static void AddKeyRow(Grid grid, TextBlock label, FrameworkElement content)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            label.VerticalAlignment = VerticalAlignment.Top;
            label.Margin = new Thickness(0, 4, 12, 0);
            Grid.SetRow(label, row);
            grid.Children.Add(label);
            Grid.SetRow(content, row);
            Grid.SetColumn(content, 1);
            grid.Children.Add(content);
        }

        Button Chip(string label, Func<string> token, string tooltip = null)
        {
            string text = token();
            var b = Ui.Button(label, () => Insert(token()), "Chip", (tooltip ?? label) + "  →  " + text.Replace("\n", ""));
            return b;
        }

        Button AddButton(Func<string> token, string tooltip) { return ActionButton("Add", token, tooltip); }

        /// <summary>A small button that inserts what token gives. Null means a box still needs filling in; "" means token inserted it itself.</summary>
        Button ActionButton(string label, Func<string> token, string tooltip)
        {
            var b = Ui.Button(label, () => { string t = token(); if (t == null) c.SetStatus("Fill in the box first, then press " + label + ".", true); else Insert(t); }, "Small", tooltip);
            return b;
        }

        static FrameworkElement DockedAdd(TextBox box, FrameworkElement add)
        {
            var dock = new DockPanel();
            DockPanel.SetDock(add, Dock.Right);
            add.Margin = new Thickness(6, 0, 0, 0);
            dock.Children.Add(add);
            dock.Children.Add(box);
            return dock;
        }

        static string Keys(params string[] names)
        {
            var vks = new List<ushort>();
            foreach (string n in names) { ushort vk; if (KeyNames.TryGetVk(n, out vk)) vks.Add(vk); }
            return MacroScript.Shortcut(vks);
        }

        void InsertKey(bool down)
        {
            ushort vk;
            if (KeyNames.TryGetVk((string)holdKey.SelectedItem, out vk)) Insert(down ? MacroScript.KeyDown(vk) : MacroScript.KeyUp(vk));
        }

        /// <summary>Puts text into the script at the cursor (replacing the selection) and keeps the cursor after it.</summary>
        public void Insert(string token)
        {
            if (current == null || string.IsNullOrEmpty(token)) return;
            if (!CodeIsTarget) { editor.InsertLines(PlainMacro.ToPlain(token, true)); return; }
            int at = Math.Min(script.SelectionStart, script.Text.Length);
            script.SelectedText = token;
            script.CaretIndex = at + token.Length;
            script.SelectionLength = 0;
            script.Focus();
            script.ScrollToLine(script.GetLineIndexFromCharacterIndex(script.CaretIndex));
        }

        void PickPosition()
        {
            if (countdown != null) return;
            int left = 3;
            pickPosition.Content = left + "…";
            countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            countdown.Tick += (s, e) =>
            {
                left--;
                if (left > 0) { pickPosition.Content = left + "…"; return; }
                countdown.Stop();
                countdown = null;
                ScreenPoint p = c.Input.CursorPosition;
                mouseX.Value = p.X;
                mouseY.Value = p.Y;
                pickPosition.Content = "Pick…";
                c.SetStatus("Pointer position " + p + " filled in.", false);
            };
            countdown.Start();
        }

        // ------------------------------------------------------------------ steps preview

        /// <summary>The script box and the live list of steps next to it (problems show in red).</summary>
        FrameworkElement BuildScriptAndSteps()
        {
            var host = new Grid();
            host.Children.Add(steps);
            host.Children.Add(stepsEmpty);
            problemsBox = new Border { Padding = new Thickness(4), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = host, MaxHeight = 150, Margin = new Thickness(0, 4, 0, 0) };
            Ui.Ref(problemsBox, Border.BackgroundProperty, "Input");
            Ui.Ref(problemsBox, Border.BorderBrushProperty, "Border");
            stepsHeader.Margin = new Thickness(0, 6, 0, 0);
            stepsSummary.Margin = new Thickness(0, 3, 0, 0);
            var scriptLabel = Ui.Text("Script text", "Muted", false, 12);
            scriptLabel.Margin = new Thickness(0, 0, 0, 3);
            return Ui.Stack(0, scriptLabel, script, stepsHeader, problemsBox);   // the time per pass is shown with the steps above
        }

        void ShowSteps()
        {
            stepsLoading = true;
            steps.Items.Clear();
            if (current == null) { stepsLoading = false; return; }
            parsed = MacroParser.Parse(current.Script, current.NewLineIsEnter);
            foreach (ParseError error in parsed.Errors)
            {
                var row = Ui.Text("Line " + error.Line + ", column " + error.Column + ":  " + error.Message, "Bad", false, 12.5, true);
                steps.Items.Add(new ListBoxItem { Content = row, Tag = error, Padding = new Thickness(8, 4, 8, 4) });
            }
            // The steps themselves are the editor above; here only what needs fixing is listed (click one to find it in the text).
            int number = 1, missing = 0;
            foreach (MacroStep step in parsed.Steps)
            {
                if (step.Kind == StepKind.RunMacro && (step.Value ?? "").IndexOf('$') < 0
                    && !c.Macros.Any(m => string.Equals((m.Name ?? "").Trim(), (step.Value ?? "").Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    var row = Ui.Text("Step " + number.ToString(CultureInfo.InvariantCulture) + ":  " + step.Describe() + "  (you have no macro by this name)", "Bad", false, 12.5, true);
                    steps.Items.Add(new ListBoxItem { Content = row, Tag = step, Padding = new Thickness(8, 3, 8, 3) });
                    missing++;
                }
                number++;
            }
            int problems = parsed.Errors.Count + missing;
            stepsEmpty.Visibility = Visibility.Collapsed;
            if (problemsBox != null) problemsBox.Visibility = problems > 0 ? Visibility.Visible : Visibility.Collapsed;
            stepsHeader.Text = (parsed.Steps.Count == 1 ? "1 step" : parsed.Steps.Count + " steps")
                + (problems > 0 ? "   ·   " + problems + (problems == 1 ? " problem" : " problems") : parsed.Steps.Count > 0 ? ", no problems" : "");
            Ui.Ref(stepsHeader, TextBlock.ForegroundProperty, problems > 0 ? "Bad" : "Muted");
            stepsSummary.Text = parsed.Steps.Count == 0 ? "" : "About " + AppController.FormatDuration(parsed.EstimatedMs(current)) + " per pass" + RepeatTotal();
            stepsLoading = false;
        }

        string RepeatTotal()
        {
            if (current.Repeat != RepeatMode.Times || current.RepeatTimes < 2) return "";
            long total = parsed.EstimatedMs(current) * current.RepeatTimes + (long)current.RepeatDelayMs * (current.RepeatTimes - 1);
            return ", about " + AppController.FormatDuration(total) + " in all";
        }

        void SelectStepInScript()
        {
            if (stepsLoading) return;
            var item = steps.SelectedItem as ListBoxItem;
            if (item == null) return;
            int index = -1, length = 0;
            var step = item.Tag as MacroStep;
            var error = item.Tag as ParseError;
            if (step != null) { index = step.Index; length = step.Length; }
            else if (error != null) { index = error.Index; length = error.Length; }
            if (index < 0 || index > script.Text.Length) return;
            script.Select(index, Math.Min(length, script.Text.Length - index));
            script.ScrollToLine(script.GetLineIndexFromCharacterIndex(index));
        }

        // ------------------------------------------------------------------ plain editors

        string EditorKind { get { return editor != null && editor == tileEditor ? EditorKinds.Tiles : EditorKinds.Text; } }

        /// <summary>Shows the typed editor or the key tiles editor; both edit the same plain text.</summary>
        void UseEditor(string kind)
        {
            if (EditorKinds.Normal(kind) == EditorKinds.Tiles)
            {
                if (tileEditor == null) { tileEditor = new KeyTileEditor(() => owner); tileEditor.Changed += PlainEdited; }
                editor = tileEditor;
            }
            else
            {
                if (textEditor == null) { textEditor = new PlainTextEditor(() => owner); textEditor.Changed += PlainEdited; }
                editor = textEditor;
            }
            editorHost.Child = editor.Root;
            if (current != null) ShowPlain(PlainMacro.PlainFor(current));
            else ShowPlain("");
            ApplyLayout();
        }

        /// <summary>Lines or Tiles: two views of the same steps.</summary>
        void ChooseEditor(string kind)
        {
            if (EditorKinds.Normal(kind) == EditorKind) return;
            c.Settings.MacroEditor = EditorKinds.Normal(kind);
            c.MarkDirty(SaveParts.Settings);
            if (EditorKinds.Normal(c.Settings.MacroEditor) != EditorKind) UseEditor(c.Settings.MacroEditor);
        }

        /// <summary>Shows or hides the Stream Deck code, the full command list and the option cards, as saved in Settings.</summary>
        void ApplyLayout()
        {
            if (deckCard == null || palette == null || optionCards == null) return;
            bool deck = c.Settings.ShowStreamDeckCode;
            deckSwitch.IsChecked = deck;
            deckCard.Visibility = deck ? Visibility.Visible : Visibility.Collapsed;
            palette.Visibility = c.Settings.ShowAllCommands ? Visibility.Visible : Visibility.Collapsed;
            allCommands.Content = c.Settings.ShowAllCommands ? "Hide all commands" : "Show all commands";
            foreach (FrameworkElement card in optionCards) card.Visibility = c.Settings.ShowMacroOptions ? Visibility.Visible : Visibility.Collapsed;
            optionsButton.Content = c.Settings.ShowMacroOptions ? "Hide options" : "Change options";
            bool tiles = EditorKind == EditorKinds.Tiles;
            foreach (var pair in new[] { new KeyValuePair<Button, bool>(viewLines, !tiles), new KeyValuePair<Button, bool>(viewTiles, tiles) })
            {
                pair.Key.SetResourceReference(Control.BackgroundProperty, pair.Value ? "Selected" : "Card2");
                pair.Key.SetResourceReference(Control.BorderBrushProperty, pair.Value ? "Accent" : "Border");
            }
            if (deck) ShowSteps();
        }

        /// <summary>Puts plain text into the editor without counting it as an edit.</summary>
        void ShowPlain(string text)
        {
            if (editor == null) return;
            syncing = true;
            editor.Text = text ?? "";
            syncing = false;
            PlainResult result = PlainMacro.ToScript(text ?? "");
            editor.Show(result);
            ShowPlainSummary(result);
        }

        /// <summary>The user changed the steps: the macro's Super Macro text follows.</summary>
        void PlainEdited()
        {
            if (syncing || loading || current == null) return;
            string text = editor.Text;
            PlainResult result = PlainMacro.ToScript(text);
            current.Plain = text;
            current.Script = result.Script;
            c.MarkDirty(SaveParts.Macros);
            syncing = true;
            script.Text = result.Script;
            syncing = false;
            codeDirty = false;
            editor.Show(result);
            ShowPlainSummary(result);
            parseTimer.Stop();
            parseTimer.Start();
        }

        /// <summary>The Stream Deck code was changed: the steps follow.</summary>
        void PlainFromCode()
        {
            if (current == null) return;
            ShowPlain(PlainMacro.ToPlain(current.Script, current.NewLineIsEnter));
        }

        void ShowPlainSummary(PlainResult result)
        {
            var problems = result.Lines.Where(l => l.Error != null).ToList();
            int count = result.Lines.Count(l => l.Kind != PlainKind.Empty && l.Kind != PlainKind.Note);
            if (problems.Count > 0)
            {
                PlainLine first = problems[0];
                string source = first.Source.Length > 30 ? first.Source.Substring(0, 30) + "…" : first.Source;
                string where = EditorKind == EditorKinds.Tiles ? "“" + source + "”" : "Line " + (first.Number + 1);
                plainSummary.Text = (problems.Count == 1 ? "1 step needs" : problems.Count + " steps need") + " fixing before the macro can run.  " + where + ": " + first.Error;
                Ui.Ref(plainSummary, TextBlock.ForegroundProperty, "Bad");
                return;
            }
            Ui.Ref(plainSummary, TextBlock.ForegroundProperty, "Muted");
            if (count == 0 || current == null) { plainSummary.Text = ""; return; }
            ParseResult p = MacroParser.Parse(current.Script, current.NewLineIsEnter);
            long pass = p.EstimatedMs(current);
            string text = (count == 1 ? "1 step" : count + " steps") + "  ·  about " + AppController.FormatDuration(pass) + " per pass";
            if (current.Repeat == RepeatMode.Times && current.RepeatTimes > 1)
                text += ", about " + AppController.FormatDuration(pass * current.RepeatTimes + (long)current.RepeatDelayMs * (current.RepeatTimes - 1)) + " in all";
            plainSummary.Text = text;
        }

        void UpdateOptionsSummary()
        {
            MacroDef m = current;
            if (m == null) { optionsSummary.Text = ""; return; }
            var parts = new List<string>();
            parts.Add(m.Repeat == RepeatMode.Once ? "Runs once" : m.Repeat == RepeatMode.Times ? "Runs " + m.RepeatTimes + " times" : "Runs continuously");
            if (m.Target == TargetKind.PreviousWindow) parts.Add("in the window you used before");
            else
            {
                WindowMatch w = m.TargetWindow ?? new WindowMatch();
                string which = !string.IsNullOrWhiteSpace(w.TitleContains) ? "“" + w.TitleContains.Trim() + "”" : !string.IsNullOrWhiteSpace(w.ProcessName) ? w.ProcessName.Trim() : "a window you choose";
                parts.Add("in " + which);
            }
            parts.Add(string.IsNullOrEmpty(m.Hotkey) ? "no hotkey" : "hotkey " + m.Hotkey);
            if (m.PasteText) parts.Add("pastes text");
            if (m.ShowOnDeck) parts.Add("on the Deck");
            if (m.Schedule != null && m.Schedule.Kind != ScheduleKind.Off) parts.Add("on a schedule");
            optionsSummary.Text = string.Join("  ·  ", parts);
        }

        void CopyCode()
        {
            if (current == null || string.IsNullOrEmpty(current.Script)) { c.SetStatus("There is no code to copy yet.", true); return; }
            try { Clipboard.SetText(current.Script); c.SetStatus("Stream Deck code copied. Paste it into a Super Macro action.", false); }
            catch (Exception ex) { c.SetStatus("Could not copy: " + ex.Message, true); }
        }

        /// <summary>The palette adds to the Stream Deck code only when that box was the last one you typed in; otherwise to the steps.</summary>
        bool CodeIsTarget { get { return codeTarget && deckCard != null && deckCard.Visibility == Visibility.Visible; } }

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
            panel.Children.Add(Ui.Text(m.Name.Length == 0 ? "(unnamed)" : m.Name, "Text", true));
            var sub = new List<string> { RepeatSummary(m) };
            if (!string.IsNullOrEmpty(m.Hotkey)) sub.Add(m.Hotkey);
            if (m.ShowOnDeck) sub.Add("Deck");
            var line = Ui.Text(string.Join("  ·  ", sub), "Muted", false, 11.5);
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
            copy.Name = m.Name + " copy";
            copy.Hotkey = "";
            c.Macros.Insert(c.Macros.IndexOf(m) + 1, copy);
            c.MarkDirty(SaveParts.Macros);
            Reload(copy);
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
        public string CurrentEditor { get { return EditorKind; } }
        public PlainTextEditor TextEditor { get { return textEditor; } }
        public KeyTileEditor TileEditor { get { return tileEditor; } }
        /// <summary>Self test: the palette adds to the Stream Deck code (true) or the steps (false).</summary>
        public bool InsertIntoCode { get { return CodeIsTarget; } set { codeTarget = value; } }
        public bool StreamDeckCodeVisible { get { return deckCard.Visibility == Visibility.Visible; } }
        public bool OptionsVisible { get { return optionCards[0].Visibility == Visibility.Visible; } }
        public void ToggleStreamDeck(bool on) { deckSwitch.IsChecked = on; c.Settings.ShowStreamDeckCode = on; c.MarkDirty(SaveParts.Settings); ApplyLayout(); }
        public void ToggleOptions(bool on) { c.Settings.ShowMacroOptions = on; c.MarkDirty(SaveParts.Settings); ApplyLayout(); }
        public void ToggleAllCommands(bool on) { c.Settings.ShowAllCommands = on; c.MarkDirty(SaveParts.Settings); ApplyLayout(); }
        public int VisibleCommandGroups { get { return groups.Count(g => g.Root.Visibility == Visibility.Visible); } }
        public string[] OpenCommandGroups { get { return groups.Where(g => g.Open && g.Root.Visibility == Visibility.Visible).Select(g => g.Id).ToArray(); } }
        /// <summary>Self test: opens every group for a screenshot without saving it; SearchCommands("") puts the saved layout back.</summary>
        public void OpenAllCommandGroups() { palette.Visibility = Visibility.Visible; foreach (CommandGroup g in groups) SetOpen(g, true, false); RefreshProCommands(); }
    }
}
