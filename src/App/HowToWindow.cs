using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MacroKit.Core;

namespace MacroKit.App
{
    /// <summary>
    /// 1.13.1: the how-to guides (HowTo.md): the list of guides on the left, the open one on the right. It stays open beside
    /// the main window while you build a macro; opening another guide reuses it.
    /// </summary>
    public sealed class HowToWindow : Window
    {
        static HowToWindow open;

        /// <summary>The open guides window, or null.</summary>
        public static HowToWindow Current { get { return open; } }

        readonly List<HowToTopic> topics;
        readonly StackPanel list = new StackPanel();
        readonly StackPanel page = new StackPanel { Margin = new Thickness(24, 18, 26, 24) };
        readonly ScrollViewer pageScroll;
        readonly Dictionary<string, Button> topicButtons = new Dictionary<string, Button>();

        /// <summary>The guide on show (its id), for the self test.</summary>
        public string TopicId { get; private set; }
        public int TopicCount { get { return topics.Count; } }
        /// <summary>The example macros on the page on show.</summary>
        public int ExampleCount { get; private set; }
        /// <summary>1.13.3: the pictures on the page on show, and the ones this build is missing.</summary>
        public int PictureCount { get; private set; }
        public readonly List<string> MissingPictures = new List<string>();
        /// <summary>1.13.3: the page's Show me button, or null when the guide has none.</summary>
        public Button ShowMeButton { get; private set; }

        /// <summary>Opens the guides at <paramref name="topicId"/> (the first guide when it is null or unknown), reusing an open window.</summary>
        public static HowToWindow Show(Window owner, string topicId)
        {
            if (open == null)
            {
                open = new HowToWindow(owner);
                open.Closed += (s, e) => open = null;
                open.ShowTopic(topicId);
                open.Show();
            }
            else
            {
                open.ShowTopic(topicId);
                if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
                open.Activate();
            }
            return open;
        }

        HowToWindow(Window owner)
        {
            Title = "Macro Kit how-to guides";
            Width = 860;
            Height = 640;
            MinWidth = 560;
            MinHeight = 360;
            ShowInTaskbar = false;
            if (owner != null && owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Theme.Style(this);

            try { topics = HowToGuide.Load(); }
            catch (Exception ex) { Log.Warn("The how-to guides could not be read", ex); topics = new List<HowToTopic>(); }

            list.Margin = new Thickness(10, 14, 6, 14);
            foreach (HowToTopic t in topics)
            {
                HowToTopic topic = t;
                var b = Ui.Button(Ui.Text(topic.Title, "Text", false, 12.5, true), () => ShowTopic(topic.Id), "MenuBtn", null);
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                b.Padding = new Thickness(10, 6, 8, 6);
                System.Windows.Automation.AutomationProperties.SetName(b, topic.Title);
                topicButtons[topic.Id] = b;
                list.Children.Add(b);
            }
            var listScroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Width = 230 };
            var divider = new Border { Width = 1 };
            Ui.Ref(divider, Border.BackgroundProperty, "Border");
            pageScroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(listScroll);
            Grid.SetColumn(divider, 1);
            grid.Children.Add(divider);
            Grid.SetColumn(pageScroll, 2);
            grid.Children.Add(pageScroll);
            Content = grid;
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        }

        public void ShowTopic(string id)
        {
            HowToTopic topic = topics.FirstOrDefault(t => t.Id == id) ?? topics.FirstOrDefault();
            page.Children.Clear();
            ExampleCount = 0;
            PictureCount = 0;
            MissingPictures.Clear();
            ShowMeButton = null;
            if (topic == null)
            {
                TopicId = null;
                page.Children.Add(Ui.Para("The how-to guides are missing from this copy of Macro Kit.", "Muted"));
                return;
            }
            TopicId = topic.Id;
            foreach (KeyValuePair<string, Button> pair in topicButtons)
            {
                if (pair.Key == topic.Id) Ui.Ref(pair.Value, Control.BackgroundProperty, "Selected");
                else pair.Value.ClearValue(Control.BackgroundProperty);
            }
            var title = Ui.Heading(topic.Title, "H1");
            if (HowToGuide.ShowMeTopics.Contains(topic.Id))
            {
                // 1.13.3: takes you to the place the guide is about and outlines it, for when the words aren't enough.
                string guideId = topic.Id;
                ShowMeButton = Ui.Button("Show me", () => ShowMe(guideId), "Small", "Switches Macro Kit to the place this guide is about, opens it and outlines it in red.");
                ShowMeButton.VerticalAlignment = VerticalAlignment.Top;
                ShowMeButton.Margin = new Thickness(12, 2, 0, 0);
                var row = new DockPanel();
                DockPanel.SetDock(ShowMeButton, Dock.Right);
                row.Children.Add(ShowMeButton);
                row.Children.Add(title);
                page.Children.Add(row);
            }
            else page.Children.Add(title);
            Render(topic.Lines);
            pageScroll.ScrollToTop();
            Button selected;
            if (topicButtons.TryGetValue(topic.Id, out selected)) selected.BringIntoView();
        }

        void ShowMe(string id)
        {
            var main = Owner as MainWindow ?? Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (main != null) main.ShowMe(id);
        }

        static readonly Regex Numbered = new Regex(@"^(\d+)\.\s+(.*)$");

        void Render(List<string> lines)
        {
            List<string> example = null;
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.Trim() == "```")
                {
                    if (example == null) { example = new List<string>(); continue; }
                    page.Children.Add(Example(example));
                    ExampleCount++;
                    example = null;
                    continue;
                }
                if (example != null) { example.Add(line); continue; }
                if (line.Length == 0) continue;
                string caption, name;
                if (HowToGuide.TryPicture(line, out caption, out name))
                {
                    FrameworkElement picture = Picture(name, caption);
                    if (picture == null) MissingPictures.Add(name);
                    else { page.Children.Add(picture); PictureCount++; }
                    continue;
                }
                Match n = Numbered.Match(line);
                if (line.StartsWith("- ", StringComparison.Ordinal)) page.Children.Add(ListRow("•", line.Substring(2)));
                else if (n.Success) page.Children.Add(ListRow(n.Groups[1].Value + ".", n.Groups[2].Value));
                else if (line.Length > 4 && line.StartsWith("**", StringComparison.Ordinal) && line.IndexOf("**", 2, StringComparison.Ordinal) == line.Length - 2)
                {
                    var h = Ui.Text(line.Substring(2, line.Length - 4), "Text", true, 13.5);
                    h.Margin = new Thickness(0, 16, 0, 4);
                    page.Children.Add(h);
                }
                else
                {
                    var p = Ui.Para("", "Text");
                    ChangelogWindow.AddInlines(p, line);
                    p.Margin = new Thickness(0, 4, 0, 4);
                    page.Children.Add(p);
                }
            }
        }

        static FrameworkElement ListRow(string marker, string text)
        {
            var row = new Grid { Margin = new Thickness(4, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var m = Ui.Text(marker, "Muted");
            m.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(m);
            var t = Ui.Para("", "Text");
            ChangelogWindow.AddInlines(t, text);
            Grid.SetColumn(t, 1);
            row.Children.Add(t);
            return row;
        }

        /// <summary>A built-in picture, in the theme's colours when there is one in them; null when the build has none.</summary>
        static BitmapSource LoadPicture(string name)
        {
            foreach (string theme in Theme.IsDark ? new[] { "dark", "light" } : new[] { "light", "dark" })
                using (Stream s = typeof(HowToWindow).Assembly.GetManifestResourceStream("App.HowTo." + name + "-" + theme + ".png"))
                {
                    if (s == null) continue;
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = s;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            return null;
        }

        // 1.13.3: a picture of the place a guide is about, outlined in red, with its caption under it. A click shows it full size.
        FrameworkElement Picture(string name, string caption)
        {
            BitmapSource bitmap;
            try { bitmap = LoadPicture(name); }
            catch (Exception ex) { Log.Warn("A how-to picture could not be read: " + name, ex); return null; }
            if (bitmap == null) return null;
            var image = new Image
            {
                Source = bitmap, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = bitmap.PixelWidth,
                HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand, ToolTip = "Click to see it full size.",
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            System.Windows.Automation.AutomationProperties.SetName(image, caption);
            image.MouseLeftButtonUp += (s, e) => Enlarge(bitmap, caption);
            var frame = new Border { Child = image, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
            Ui.Ref(frame, Border.BorderBrushProperty, "Border");
            var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 10) };
            stack.Children.Add(frame);
            if (caption.Length > 0)
            {
                var text = Ui.Para(caption, "Muted", 12);
                text.Margin = new Thickness(0, 4, 0, 0);
                stack.Children.Add(text);
            }
            return stack;
        }

        void Enlarge(BitmapSource bitmap, string caption)
        {
            var w = new Window
            {
                Title = caption.Length > 0 ? caption : "Picture", Owner = this, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, SizeToContent = SizeToContent.WidthAndHeight,
                MaxWidth = SystemParameters.WorkArea.Width * 0.95, MaxHeight = SystemParameters.WorkArea.Height * 0.95,
            };
            Theme.Style(w);
            w.Content = new ScrollViewer
            {
                Content = new Image { Source = bitmap, Stretch = Stretch.None },
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            w.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; w.Close(); } };
            w.Show();
        }

        // An example macro in a box, with a button that copies its steps (to paste into a macro's Lines).
        FrameworkElement Example(List<string> lines)
        {
            string steps = string.Join(Environment.NewLine, lines);
            var code = new TextBlock { Text = steps, FontFamily = Ui.MonoFont, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            Ui.Ref(code, TextBlock.ForegroundProperty, "Text");
            var copy = Ui.Button("Copy", () =>
            {
                try { Clipboard.SetText(steps); }
                catch (Exception ex) { Log.Warn("Copying an example failed", ex); }
            }, "Small", "Copy these steps, then paste them into a macro's steps (Lines).");
            copy.VerticalAlignment = VerticalAlignment.Top;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(code);
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);
            var box = new Border { Child = grid, Padding = new Thickness(12, 9, 9, 9), Margin = new Thickness(0, 6, 0, 6), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
            Ui.Ref(box, Border.BackgroundProperty, "Input");
            Ui.Ref(box, Border.BorderBrushProperty, "Border");
            return box;
        }
    }
}
