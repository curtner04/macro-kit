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
            var code = Ui.Text(item.IsPhrase ? "Phrase: type " + item.Trigger : item.Script, "Muted", false, 11.5, true);
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
