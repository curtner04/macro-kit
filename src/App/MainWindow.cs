using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MacroKit.Core;

namespace MacroKit.App
{
    /// <summary>The main window: header, five tabs and a status line.</summary>
    public sealed class MainWindow : Window
    {
        public const double MinimumWidth = 900, MinimumHeight = 620;

        readonly AppController c;
        readonly AppShell shell;
        readonly RadioButton[] tabButtons = new RadioButton[5];
        // Page indexes in the order the tabs are shown (Global Keybinds is page 4 but sits before Settings).
        static readonly int[] shownOrder = { 0, 1, 2, 4, 3 };
        readonly FrameworkElement[] pages = new FrameworkElement[5];
        readonly TextBlock status = Ui.Text("", "Muted", false, 12.5);
        // Clears an ordinary status message after a while, so an old one is never read as news; errors stay until replaced.
        readonly System.Windows.Threading.DispatcherTimer statusTimer;
        readonly TextBlock inputState = Ui.Text("", "Muted", false, 12);
        readonly Border unsavedBadge;
        readonly Border readOnlyBadge;
        readonly Button deckButton;
        readonly Button restartButton;
        readonly Button moreButton;
        readonly System.Windows.Controls.Primitives.Popup moreMenu;

        public readonly RecorderTab Recorder;
        public readonly PhrasesTab Phrases;
        public readonly MacrosTab Macros;
        public readonly SettingsTab Settings;
        public readonly KeybindsTab Keybinds;

        public MainWindow(AppController controller, AppShell shell)
        {
            c = controller;
            this.shell = shell;
            Title = "Macro Kit";
            MinWidth = MinimumWidth;
            MinHeight = MinimumHeight;
            Theme.Style(this);
            Icon = LoadIcon();
            RestorePlacement();

            // ---- header
            var name = new TextBlock { Text = "Macro Kit", VerticalAlignment = VerticalAlignment.Center };
            Ui.Style(name, "H1");
            var version = Ui.Text("v" + Release.Version, "Muted", false, 12);
            version.Margin = new Thickness(10, 5, 0, 0);
            unsavedBadge = Ui.Pill("Unsaved", "Warn", "WarnBg");
            unsavedBadge.ToolTip = "Your latest changes could not be written to disk yet. Macro Kit keeps trying.";
            unsavedBadge.Margin = new Thickness(0, 0, 12, 0);
            unsavedBadge.Visibility = Visibility.Collapsed;
            readOnlyBadge = Ui.Pill("Data file in use", "Warn", "WarnBg");
            readOnlyBadge.Margin = new Thickness(0, 0, 12, 0);
            readOnlyBadge.Visibility = Visibility.Collapsed;
            deckButton = Ui.IconButton(Ui.GlyphDeck, "Deck", () => c.ToggleDeck(), "Ghost", "Show or hide the Deck: a small always-on-top panel of macro buttons that never takes focus.");
            restartButton = Ui.IconButton(Ui.GlyphRefresh, "Restart", () => shell.Restart(), "Ghost", "Save and reopen Macro Kit. It waits while something is running and never runs an installer.");
            moreButton = Ui.GlyphButton(Ui.GlyphMore, "More: export or import settings, changelog, data folder", null);
            moreMenu = Ui.Menu(moreButton,
                new System.Collections.Generic.KeyValuePair<string, Action>("Export / import configuration…", shell.ExportImport),
                new System.Collections.Generic.KeyValuePair<string, Action>("Changelog", shell.ShowChangelog),
                new System.Collections.Generic.KeyValuePair<string, Action>("Open data folder", shell.OpenDataFolder),
                new System.Collections.Generic.KeyValuePair<string, Action>("Exit Macro Kit", shell.Exit));
            moreButton.Click += (s, e) => moreMenu.IsOpen = true;

            var headerRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            headerRight.Children.Add(readOnlyBadge);
            headerRight.Children.Add(unsavedBadge);
            headerRight.Children.Add(deckButton);
            restartButton.Margin = new Thickness(8, 0, 0, 0);
            headerRight.Children.Add(restartButton);
            moreButton.Margin = new Thickness(8, 0, 0, 0);
            headerRight.Children.Add(moreButton);
            var header = new Grid { Margin = new Thickness(22, 16, 22, 0) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(name);
            title.Children.Add(version);
            header.Children.Add(title);
            Grid.SetColumn(headerRight, 1);
            header.Children.Add(headerRight);

            // ---- tabs
            Recorder = new RecorderTab(c, this);
            Phrases = new PhrasesTab(c, this);
            Macros = new MacrosTab(c, this);
            Settings = new SettingsTab(c, () => { Theme.Apply(c.Settings.Theme); }, shell.ShowChangelog, shell.OpenDataFolder, () => shell.Pill);
            Keybinds = new KeybindsTab(c);
            // Page indexes are what Settings.SelectedTab remembers, so Global Keybinds is page 4 although it is shown before Settings.
            string[] names = { "Recorder", "Phrases", "Macros", "Settings", "Global Keybinds" };
            FrameworkElement[] roots = { Recorder.Root, Phrases.Root, Macros.Root, Settings.Root, Keybinds.Root };
            var tabRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 8, 22, 0) };
            var content = new Grid { Margin = new Thickness(22, 0, 22, 0) };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var tab = new RadioButton { Content = names[i], GroupName = "tabs" };
                Ui.Style(tab, "TabBtn");
                tab.Checked += (s, e) => ShowPage(index);
                tabButtons[i] = tab;
                pages[i] = roots[i];
                roots[i].Visibility = Visibility.Collapsed;
                content.Children.Add(roots[i]);
            }
            for (int position = 0; position < shownOrder.Length; position++)
            {
                RadioButton tab = tabButtons[shownOrder[position]];
                tab.ToolTip = names[shownOrder[position]] + " (Ctrl+" + (position + 1) + ")";
                tabRow.Children.Add(tab);
            }
            var rule = new Border { Height = 1, Margin = new Thickness(22, 0, 22, 0) };
            Ui.Ref(rule, Border.BackgroundProperty, "Border");

            // ---- status line
            var statusBar = new Grid { Margin = new Thickness(22, 8, 22, 12) };
            statusBar.ColumnDefinitions.Add(new ColumnDefinition());
            statusBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusBar.Children.Add(status);
            Grid.SetColumn(inputState, 1);
            statusBar.Children.Add(inputState);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(header);
            Grid.SetRow(tabRow, 1); root.Children.Add(tabRow);
            Grid.SetRow(rule, 2); root.Children.Add(rule);
            Grid.SetRow(content, 3); root.Children.Add(content);
            Grid.SetRow(statusBar, 4); root.Children.Add(statusBar);
            Content = root;

            statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            statusTimer.Tick += (s, e) => { statusTimer.Stop(); ShowStatus("", false); };
            c.StatusChanged += ShowStatus;
            c.SaveStateChanged += () => unsavedBadge.Visibility = c.IsUnsaved ? Visibility.Visible : Visibility.Collapsed;
            c.RecordingsChanged += UpdateReadOnlyBadge;
            c.DeckVisibilityChanged += UpdateDeckButton;
            c.ActivityChanged += UpdateActivity;
            c.Edited += parts => { if ((parts & SaveParts.Settings) != 0) UpdateInputState(); };

            int selected = Math.Max(0, Math.Min(pages.Length - 1, c.Settings.SelectedTab));
            tabButtons[selected].IsChecked = true;
            UpdateDeckButton();
            UpdateInputState();
            UpdateActivity();
            UpdateReadOnlyBadge();
            SizeChanged += (s, e) => RememberPlacement();
            LocationChanged += (s, e) => RememberPlacement();
            StateChanged += (s, e) => RememberMaximized();
            Closing += OnClosing;
            KeyDown += OnShortcutKey;
        }

        // KeyDown bubbles up from the focused control, so a box that uses the key itself (a hotkey box, the step editor's
        // suggestions) has it first and marks it handled. Ctrl+Tab gets here before WPF's own focus moving, which runs afterwards.
        void OnShortcutKey(object sender, KeyEventArgs e)
        {
            if (e.Handled || Keyboard.FocusedElement is HotkeyBox) return;
            e.Handled = Shortcut(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
        }

        /// <summary>
        /// The window's keyboard shortcuts: Ctrl+1 to 5 and Ctrl+Tab (Shift goes back) switch tabs, Ctrl+F goes to the tab's search box,
        /// Ctrl+N makes a new phrase or macro, F5 plays the recording or runs the macro. True when the key was one of them.
        /// </summary>
        public bool Shortcut(Key key, ModifierKeys modifiers)
        {
            int current = CurrentPage;
            if (modifiers == ModifierKeys.Control && (key >= Key.D1 && key <= Key.D5 || key >= Key.NumPad1 && key <= Key.NumPad5))
            {
                int position = key >= Key.NumPad1 ? key - Key.NumPad1 : key - Key.D1;
                if (position >= shownOrder.Length || tabButtons[shownOrder[position]].Visibility != Visibility.Visible) return false;
                tabButtons[shownOrder[position]].IsChecked = true;
                return true;
            }
            if (key == Key.Tab && (modifiers == ModifierKeys.Control || modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
            {
                int step = (modifiers & ModifierKeys.Shift) != 0 ? -1 : 1;
                int at = Array.IndexOf(shownOrder, current);
                for (int tries = 0; tries < shownOrder.Length; tries++)
                {
                    at = (at + step + shownOrder.Length) % shownOrder.Length;
                    if (tabButtons[shownOrder[at]].Visibility == Visibility.Visible) { tabButtons[shownOrder[at]].IsChecked = true; return true; }
                }
                return false;
            }
            if (modifiers == ModifierKeys.Control && key == Key.F)
            {
                if (current == 0) Recorder.FocusSearch();
                else if (current == 1) Phrases.FocusSearch();
                else if (current == 2) Macros.FocusSearch();
                else if (current == 4) Keybinds.FocusSearch();
                else return false;
                return true;
            }
            if (modifiers == ModifierKeys.Control && key == Key.N)
            {
                if (current == 1) Phrases.AddNew();
                else if (current == 2) Macros.PressNew();
                else return false;
                return true;
            }
            if (modifiers == ModifierKeys.None && key == Key.F5)
            {
                if (current == 0) Recorder.PressPlay();
                else if (current == 2) Macros.ShortcutRun();
                else return false;
                return true;
            }
            return false;
        }

        /// <summary>The page shown now (0 Recorder, 1 Phrases, 2 Macros, 3 Settings, 4 Global Keybinds).</summary>
        public int CurrentPage
        {
            get { for (int i = 0; i < tabButtons.Length; i++) if (tabButtons[i].IsChecked == true) return i; return 0; }
        }

        static ImageSource LoadIcon()
        {
            try
            {
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("App.Icon.png"))
                {
                    if (stream == null) return null;
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch (Exception) { return null; }
        }

        void RestorePlacement()
        {
            AppSettings s = c.Settings;
            Width = Math.Max(MinimumWidth, Math.Min(s.WindowWidth, SystemParameters.VirtualScreenWidth));
            Height = Math.Max(MinimumHeight, Math.Min(s.WindowHeight, SystemParameters.VirtualScreenHeight));
            bool placed = !double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop)
                && s.WindowLeft + 120 < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth && s.WindowLeft + Width > SystemParameters.VirtualScreenLeft + 120
                && s.WindowTop >= SystemParameters.VirtualScreenTop - 8 && s.WindowTop + 80 < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
            if (placed)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = s.WindowLeft;
                Top = s.WindowTop;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            // The bounds above are the normal ones, so the window still restores to them after it is un-maximized.
            if (s.WindowMaximized) WindowState = WindowState.Maximized;
        }

        void RememberMaximized()
        {
            if (!IsLoaded || WindowState == WindowState.Minimized) return;
            bool maximized = WindowState == WindowState.Maximized;
            if (c.Settings.WindowMaximized == maximized) return;
            c.Settings.WindowMaximized = maximized;
            c.MarkPlacementDirty();
        }

        void RememberPlacement()
        {
            if (!IsLoaded || WindowState != WindowState.Normal) return;
            c.Settings.WindowLeft = Left;
            c.Settings.WindowTop = Top;
            c.Settings.WindowWidth = Width;
            c.Settings.WindowHeight = Height;
            c.MarkPlacementDirty();   // every step of a drag or resize: only saved, nothing else is redone
        }

        void ShowPage(int index)
        {
            for (int i = 0; i < pages.Length; i++) pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            if (pages[index] == Keybinds.Root) Keybinds.Refresh();   // other apps may have changed their shortcuts meanwhile
            if (c.Settings.SelectedTab != index) { c.Settings.SelectedTab = index; c.MarkDirty(SaveParts.Settings); }
        }

        /// <summary>Selects a tab without saving it as the remembered one (self test).</summary>
        public void SelectTab(int index)
        {
            tabButtons[index].IsChecked = true;
        }

        void ShowStatus(string text, bool error)
        {
            statusTimer.Stop();
            status.Text = text;
            status.ToolTip = string.IsNullOrEmpty(text) ? null : text;
            Ui.Ref(status, TextBlock.ForegroundProperty, error ? "Bad" : "Muted");
            if (!error && !string.IsNullOrEmpty(text)) statusTimer.Start();
        }

        /// <summary>The text the status line shows right now (self test).</summary>
        public string StatusText { get { return status.Text; } }

        /// <summary>Self test: what the status line does when its time is up.</summary>
        public void ExpireStatusForTest()
        {
            if (statusTimer.IsEnabled) { statusTimer.Stop(); ShowStatus("", false); }
        }

        /// <summary>Shown while a data file could not be read: Macro Kit runs on defaults for it and never saves over the real file.</summary>
        void UpdateReadOnlyBadge()
        {
            string notice = c.ReadOnlyNotice();
            readOnlyBadge.Visibility = notice == null ? Visibility.Collapsed : Visibility.Visible;
            readOnlyBadge.ToolTip = notice;
        }

        void UpdateDeckButton()
        {
            // An open Deck is marked quietly (selected fill, accent edge); a Primary button would be the loudest thing on every tab.
            Ui.Style(deckButton, "Ghost");
            if (c.Settings.DeckVisible)
            {
                Ui.Ref(deckButton, Control.BackgroundProperty, "Selected");
                Ui.Ref(deckButton, Control.BorderBrushProperty, "Accent");
            }
            else
            {
                deckButton.ClearValue(Control.BackgroundProperty);
                deckButton.ClearValue(Control.BorderBrushProperty);
            }
        }

        void UpdateInputState()
        {
            inputState.Text = !c.InputEnabled ? "Input is off in this session" : c.Settings.ExpansionEnabled ? "Phrases on" : "Phrases off";
        }

        void UpdateActivity()
        {
            // Restart needs a quiet moment; the button stays visible and explains itself when pressed.
            restartButton.ToolTip = c.IsBusy ? "Restart is available once the current " + (c.Activity.Kind == ActivityKind.Macro ? "macro" : "activity") + " has stopped." : "Save and reopen Macro Kit. It never runs an installer.";
        }

        /// <summary>
        /// Opens the window because the person asked for it (tray, a second launch, the end of a window pick): it is restored if
        /// minimized and takes the focus, using the same careful switch as macros do, with the plain WPF Activate as a fallback.
        /// </summary>
        public void Reveal(bool activate)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            if (!activate) { ShowWithoutActivating(); return; }
            ShowActivated = true;
            Show();
            bool ok = false;
            try { ok = c.Windows.Activate(new WindowInteropHelper(this).Handle, 600); }
            catch (Exception ex) { Log.Warn("Bringing Macro Kit forward failed: " + ex.Message); }
            if (!ok) Activate();
        }

        /// <summary>
        /// Brings the window back after a run without taking focus. Minimized stays minimized (the window state is never touched);
        /// otherwise it is shown and placed straight away directly behind the window in front, or at the very bottom, so it never
        /// covers the window the person is working in.
        /// </summary>
        public void RestoreQuietly(bool wasMinimized, bool behindForeground)
        {
            ShowWithoutActivating();
            if (wasMinimized) NativeUi.ShowMinimizedNoActivate(this);
            else NativeUi.PlaceBehind(this, behindForeground);
        }

        /// <summary>
        /// Shows the window without taking the focus. WPF refuses that for a maximized window ("Cannot show Window when
        /// ShowActivated is false and WindowState is set to Maximized"), so a maximized window is shown as Normal: the hidden
        /// window keeps its maximized frame, Windows shows it as it is (SW_SHOWNA), and WPF reads Maximized back from it.
        /// If it did not come back maximized it is maximized again; that one step may take the focus, which is better than a crash.
        /// </summary>
        void ShowWithoutActivating()
        {
            bool maximized = WindowState == WindowState.Maximized && !IsVisible;
            if (maximized) WindowState = WindowState.Normal;
            ShowActivated = false;
            Show();
            if (maximized && WindowState != WindowState.Maximized)
            {
                Log.Warn("The window did not come back maximized by itself; maximizing it again.");
                WindowState = WindowState.Maximized;
            }
            ShowActivated = true;
        }

        public const string TrayCloseNotice = "Macro Kit is in the notification area by the clock (it may be under the ^ arrow). Phrases and hotkeys keep working. Right-click it and choose Exit Macro Kit to quit.";

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (shell.Exiting) return;
            e.Cancel = true;
            if (c.Settings.CloseToTray && shell.TrayAvailable)
            {
                Hide();
                // The first time, say where it went: new tray icons often sit in the hidden part of the notification area.
                if (!c.Settings.TrayCloseNoticeShown && shell.Pill.Flash("Still running", TrayCloseNotice, "Warn", 15))
                {
                    c.Settings.TrayCloseNoticeShown = true;
                    c.MarkDirty(SaveParts.Settings);
                }
                return;
            }
            // Leaving for real happens after this event has finished: closing a window from inside its own Closing event throws.
            Dispatcher.BeginInvoke(new Action(shell.Exit));
        }
    }
}
