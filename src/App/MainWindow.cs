using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MacroKit.Core;

namespace MacroKit.App
{
    /// <summary>The main window: header, four tabs and a status line.</summary>
    public sealed class MainWindow : Window
    {
        public const double MinimumWidth = 900, MinimumHeight = 620;

        readonly AppController c;
        readonly AppShell shell;
        readonly RadioButton[] tabButtons = new RadioButton[4];
        readonly FrameworkElement[] pages = new FrameworkElement[4];
        readonly TextBlock status = Ui.Text("", "Muted", false, 12.5);
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
            string[] names = { "Recorder", "Phrases", "Macros", "Settings" };
            FrameworkElement[] roots = { Recorder.Root, Phrases.Root, Macros.Root, Settings.Root };
            var tabRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 8, 22, 0) };
            var content = new Grid { Margin = new Thickness(22, 0, 22, 0) };
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var tab = new RadioButton { Content = names[i], GroupName = "tabs" };
                Ui.Style(tab, "TabBtn");
                tab.Checked += (s, e) => ShowPage(index);
                tabButtons[i] = tab;
                pages[i] = roots[i];
                roots[i].Visibility = Visibility.Collapsed;
                tabRow.Children.Add(tab);
                content.Children.Add(roots[i]);
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

            c.StatusChanged += ShowStatus;
            c.SaveStateChanged += () => unsavedBadge.Visibility = c.IsUnsaved ? Visibility.Visible : Visibility.Collapsed;
            c.RecordingsChanged += UpdateReadOnlyBadge;
            c.DeckVisibilityChanged += UpdateDeckButton;
            c.ActivityChanged += UpdateActivity;
            c.Edited += parts => { if ((parts & SaveParts.Settings) != 0) UpdateInputState(); };

            int selected = Math.Max(0, Math.Min(3, c.Settings.SelectedTab));
            tabButtons[selected].IsChecked = true;
            UpdateDeckButton();
            UpdateInputState();
            UpdateActivity();
            UpdateReadOnlyBadge();
            SizeChanged += (s, e) => RememberPlacement();
            LocationChanged += (s, e) => RememberPlacement();
            Closing += OnClosing;
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
        }

        void RememberPlacement()
        {
            if (!IsLoaded || WindowState != WindowState.Normal) return;
            c.Settings.WindowLeft = Left;
            c.Settings.WindowTop = Top;
            c.Settings.WindowWidth = Width;
            c.Settings.WindowHeight = Height;
            c.MarkDirty(SaveParts.Settings);
        }

        void ShowPage(int index)
        {
            for (int i = 0; i < pages.Length; i++) pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            if (c.Settings.SelectedTab != index) { c.Settings.SelectedTab = index; c.MarkDirty(SaveParts.Settings); }
        }

        /// <summary>Selects a tab without saving it as the remembered one (self test).</summary>
        public void SelectTab(int index)
        {
            tabButtons[index].IsChecked = true;
        }

        void ShowStatus(string text, bool error)
        {
            status.Text = text;
            status.ToolTip = text;
            Ui.Ref(status, TextBlock.ForegroundProperty, error ? "Bad" : "Muted");
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
            Ui.Style(deckButton, c.Settings.DeckVisible ? "Primary" : "Ghost");
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
            ShowActivated = activate;
            Show();
            if (!activate) return;
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
            ShowActivated = false;
            Show();
            if (wasMinimized) NativeUi.ShowMinimizedNoActivate(this);
            else NativeUi.PlaceBehind(this, behindForeground);
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (shell.Exiting) return;
            e.Cancel = true;
            if (c.Settings.CloseToTray && shell.TrayAvailable) { Hide(); return; }
            // Leaving for real happens after this event has finished: closing a window from inside its own Closing event throws.
            Dispatcher.BeginInvoke(new Action(shell.Exit));
        }
    }
}
