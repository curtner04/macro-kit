using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MacroKit.Core;
using MacroKit.Macros;

namespace MacroKit.App
{
    /// <summary>Paste a Macro Kit Pro license key. The key is checked offline; nothing is sent anywhere.</summary>
    public sealed class LicenseWindow : Window
    {
        public LicenseWindow(Window owner, AppController controller)
        {
            Title = "Enter your Macro Kit Pro license key";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            if (owner != null && owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Theme.Style(this);

            var box = Ui.Style(new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 92,
                FontFamily = Ui.MonoFont,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            }, "Input");
            AutomationName(box, "License key");
            var problem = Ui.Text("", "Bad", false, 12, true);
            problem.Visibility = Visibility.Collapsed;

            Action unlock = () =>
            {
                string error = controller.EnterLicense(box.Text);
                if (error == null) { DialogResult = true; return; }
                problem.Text = error;
                problem.Visibility = Visibility.Visible;
                box.Focus();
            };
            var ok = Ui.Button("Unlock Pro", unlock, "Primary");
            ok.IsDefault = false;   // the box takes Enter for pasted line breaks
            var cancel = Ui.Button("Cancel", () => { DialogResult = false; }, null);
            cancel.IsCancel = true;
            var buttons = Ui.Row(8, ok, cancel);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;

            Content = Ui.Stack(10,
                Ui.Heading("Unlock Macro Kit Pro"),
                Ui.Para("Paste the key from your purchase email. It starts with " + License.Prefix + ". Spaces and line breaks are fine. The key is checked on this PC; Macro Kit never sends it anywhere."),
                box,
                problem,
                buttons);
            ((FrameworkElement)Content).Margin = new Thickness(24, 20, 24, 20);
            Loaded += (s, e) => box.Focus();
            KeyDown += (s, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { unlock(); e.Handled = true; } };
        }

        static void AutomationName(DependencyObject element, string name)
        {
            System.Windows.Automation.AutomationProperties.SetName(element, name);
        }
    }

    /// <summary>The macro library: packs of ready-made macros and phrases, all free to add.</summary>
    public sealed class LibraryWindow : Window
    {
        readonly AppController c;
        readonly StackPanel stack = new StackPanel { Margin = new Thickness(26, 20, 26, 24) };

        public LibraryWindow(Window owner, AppController controller)
        {
            c = controller;
            Title = "Macro library";
            Width = 720;
            Height = 640;
            MinWidth = 480;
            MinHeight = 360;
            ShowInTaskbar = false;
            if (owner != null && owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Theme.Style(this);
            Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Fill();
        }

        void Fill()
        {
            stack.Children.Clear();
            stack.Children.Add(Ui.Heading("Macro library", "H1"));
            var intro = Ui.Para("Add a ready-made macro or phrase with one click. It becomes your own copy, so change it however you like.");
            intro.Margin = new Thickness(0, 4, 0, 8);
            stack.Children.Add(intro);
            foreach (LibraryPack pack in MacroLibrary.Packs)
            {
                bool locked = pack.Pro && !Edition.IsPro;
                // Every pack is free today, so only a Pro pack carries a badge.
                var heading = Ui.Row(8, Ui.Heading(pack.Name));
                if (pack.Pro)
                {
                    var badge = Ui.Pill("PRO", "Good", "GoodBg"); badge.Margin = new Thickness(8, 0, 0, 0);
                    badge.VerticalAlignment = VerticalAlignment.Center;
                    heading.Children.Add(badge);
                }
                heading.Margin = new Thickness(0, 16, 0, 2);
                stack.Children.Add(heading);
                stack.Children.Add(Ui.Para(pack.Description));
                if (locked)
                {
                    var unlock = Ui.Row(8,
                        Ui.Button("Enter license key", () => { if (new LicenseWindow(this, c).ShowDialog() == true) Fill(); }, "Small"),
                        ProPage.GetProButton());
                    unlock.Margin = new Thickness(0, 6, 0, 2);
                    stack.Children.Add(unlock);
                }
                foreach (LibraryItem item in pack.Items) stack.Children.Add(ItemRow(item, locked));
            }
        }

        FrameworkElement ItemRow(LibraryItem item, bool locked)
        {
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var code = Ui.Text(item.IsPhrase ? "Phrase: type " + item.Trigger : item.Script, "Muted", false, 12, true);
            code.FontFamily = Ui.MonoFont;
            var text = Ui.Stack(2, Ui.Text(item.Name, "Text", true, 13), Ui.Para(item.Description, "Muted", 12), code);
            row.Children.Add(text);
            Button add = null;
            add = Ui.Button(item.IsPhrase ? "Add phrase" : "Add macro", () =>
            {
                string problem = c.AddFromLibrary(item);
                if (problem != null) { Ui.Info(this, problem); return; }
                add.Content = "Added";
                add.IsEnabled = false;
            }, "Small", locked ? "Comes with Macro Kit Pro." : null);
            add.IsEnabled = !locked;
            add.VerticalAlignment = VerticalAlignment.Top;
            add.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(add, 1);
            row.Children.Add(add);
            return row;
        }
    }

    /// <summary>
    /// 1.13.5: what Macro Kit Pro adds, for a free user who presses Upgrade to Pro in the header. It names the three Pro
    /// features with a short example each, then Get Pro and Enter license key. No price: the founder price ends after
    /// 100 buyers, and an installed copy would go on showing it, so the website states today's price.
    /// </summary>
    public sealed class ProUpgradeWindow : Window
    {
        /// <summary>The window that is open now, if any, so a second press brings it forward instead of opening another.</summary>
        public static ProUpgradeWindow Current { get; private set; }

        /// <summary>For the self test: the feature titles shown, in order.</summary>
        public readonly List<string> Features = new List<string>();
        public readonly Button LicenseButton;

        public static ProUpgradeWindow Show(Window owner, AppController controller)
        {
            if (Current != null) { Current.Activate(); return Current; }
            var w = new ProUpgradeWindow(owner, controller);
            Current = w;
            w.Closed += (s, e) => { if (ReferenceEquals(Current, w)) Current = null; };
            w.Show();
            return w;
        }

        ProUpgradeWindow(Window owner, AppController controller)
        {
            Title = "Upgrade to Macro Kit Pro";
            Width = 600;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            if (owner != null && owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Theme.Style(this);

            var badge = Ui.Pill("PRO", "Good", "GoodBg");
            badge.Margin = new Thickness(10, 0, 0, 0);
            var heading = Ui.Row(0, Ui.Heading("Macro Kit Pro", "H1"), badge);
            var intro = Ui.Para("Everything you use today stays free, forever. Pro is a one-time purchase that adds three power features to the same app, and supports the work on Macro Kit.");
            intro.Margin = new Thickness(0, 2, 0, 4);

            var stack = Ui.Stack(10, heading, intro,
                Feature("Conditions", "Run steps only when something is true, such as when the clipboard holds a link or a certain window is in front. For example, press Ctrl+S only when Excel is in front:",
                    "{{IFWINDOW:Excel}}{{CTRL}{S}}{{ENDIF}}"),
                Feature("Scheduled macros", "A macro runs by itself every day at a time, every few minutes, or once at a date and time. Set it in the macro's options.", null),
                Feature("Picture steps", "Wait for a button or icon to appear on screen, wherever it is, and click it. Good for programs whose buttons move around.",
                    "{{WAITIMAGE}} and {{CLICKIMAGE}}"));

            var how = Ui.Para("Pay once, keep it: no subscription, on all your own PCs. After you buy on the Macro Kit website, your license key arrives by email within 24 hours. Paste it here or in Settings, About.", "Muted", 12);
            how.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(how);

            LicenseButton = Ui.Button("Enter license key", () =>
            {
                if (new LicenseWindow(this, controller).ShowDialog() == true) Close();
            }, "Small", "Paste the key from your purchase email.");
            var close = Ui.Button("Close", Close, "Small");
            close.IsCancel = true;
            FrameworkElement get = ProPage.GetProButton("Opens the Macro Kit website, where you can buy Pro.");
            var getButton = get as Button;
            if (getButton != null) getButton.SetResourceReference(FrameworkElement.StyleProperty, "Primary");
            var buttons = Ui.Row(8, get, LicenseButton, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(buttons);

            stack.Margin = new Thickness(26, 20, 26, 22);
            Content = stack;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            // Entering a key anywhere else (Settings) while this is open: nothing is left to sell.
            Action proNow = () => Dispatcher.BeginInvoke(new Action(() => { if (Edition.IsPro && IsVisible) Close(); }));
            Edition.Changed += proNow;
            Closed += (s, e) => Edition.Changed -= proNow;
        }

        FrameworkElement Feature(string title, string text, string example)
        {
            Features.Add(title);
            var star = Ui.Glyph(Ui.GlyphStar, 14);
            Ui.Ref(star, TextBlock.ForegroundProperty, "Accent");
            star.VerticalAlignment = VerticalAlignment.Top;
            star.Margin = new Thickness(0, 2, 10, 0);
            var body = Ui.Stack(3, Ui.Text(title, "Text", true, 13.5), Ui.Para(text, "Muted", 12.5));
            if (example != null)
            {
                var code = Ui.Text(example, "Muted", false, 12, true);
                code.FontFamily = Ui.MonoFont;
                body.Children.Add(code);
            }
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(star);
            Grid.SetColumn(body, 1);
            row.Children.Add(body);
            return Ui.Card(row, new Thickness(14, 12, 14, 12));
        }
    }

    /// <summary>Opens the Macro Kit website's Pro section in the default browser.</summary>
    public static class ProPage
    {
        /// <summary>
        /// "Get Pro" (opens the website) once checkout is open; until then a muted "Pro goes on sale soon" label,
        /// so no button leads to a page where Pro can't be bought.
        /// </summary>
        public static FrameworkElement GetProButton(string tooltip = null)
        {
            if (Edition.CheckoutOpen) return Ui.Button("Get Pro", Open, "Small", tooltip ?? "Opens the Macro Kit website.");
            var soon = Ui.Text("Pro goes on sale soon", "Muted", false, 12);
            soon.VerticalAlignment = VerticalAlignment.Center;
            soon.ToolTip = "Macro Kit Pro can't be bought yet. Everything else is free.";
            return soon;
        }

        public static void Open()
        {
            try { Process.Start(new ProcessStartInfo(Edition.ProPageUrl) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Warn("Opening the Pro page failed: " + ex.Message); }
        }
    }
}
