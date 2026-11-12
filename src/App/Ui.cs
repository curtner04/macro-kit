using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MacroKit.App
{
    /// <summary>
    /// Small factory helpers for code-built pages. Colours are theme resource keys (see Theme.cs and Styles.xaml):
    /// Bg, Card, Card2, Input, Border, Text, Muted, Accent, AccentFg, AccentText, BtnBg, Good, GoodBg, Warn, WarnBg, Bad, BadBg,
    /// Info, InfoBg, Selected, Rec. Style keys: Primary, Danger, Ghost, Small, Chip, Transport, Link, MenuBtn (buttons),
    /// Switch (check box), H1, H2, Cap, MutedText (text), CardBorder (border).
    /// </summary>
    public static class Ui
    {
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly FontFamily MonoFont = new FontFamily("Cascadia Mono, Consolas, Courier New");

        // Segoe MDL2 Assets code points used in the app.
        public const string GlyphRefresh = "";
        public const string GlyphMore = "";
        public const string GlyphDeck = "";
        public const string GlyphClose = "";
        public const string GlyphTarget = "";
        public const string GlyphWarn = "";
        public const string GlyphInfo = "";

        public static void Ref(FrameworkElement element, DependencyProperty property, string key) { element.SetResourceReference(property, key); }

        public static T Style<T>(T element, string styleKey) where T : FrameworkElement
        {
            if (styleKey != null) element.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
            return element;
        }

        public static TextBlock Text(string text, string foregroundKey = null, bool bold = false, double size = 0, bool wrap = false)
        {
            var t = new TextBlock { Text = text ?? "", TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
            if (!wrap) t.TextTrimming = TextTrimming.CharacterEllipsis;
            if (foregroundKey != null) Ref(t, TextBlock.ForegroundProperty, foregroundKey);
            if (bold) t.FontWeight = FontWeights.SemiBold;
            if (size > 0) t.FontSize = size;
            return t;
        }

        /// <summary>Wrapped paragraph text (muted by default).</summary>
        public static TextBlock Para(string text, string foregroundKey = "Muted", double size = 0)
        {
            var t = Text(text, foregroundKey, false, size, true);
            t.VerticalAlignment = VerticalAlignment.Top;
            return t;
        }

        public static TextBlock Heading(string text, string styleKey = "H2")
        {
            return Style(new TextBlock { Text = text ?? "", TextWrapping = TextWrapping.Wrap }, styleKey);
        }

        /// <summary>A small muted caption above a group of controls.</summary>
        public static TextBlock Caption(string text)
        {
            return Style(new TextBlock { Text = text ?? "" }, "Cap");
        }

        public static TextBlock Glyph(string code, double size = 14, string foregroundKey = null)
        {
            var t = new TextBlock { Text = code ?? "", FontFamily = IconFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
            if (foregroundKey != null) Ref(t, TextBlock.ForegroundProperty, foregroundKey);
            return t;
        }

        /// <summary>Vector icons drawn by hand so they look the same on every Windows version: "play", "stop", "record".</summary>
        public static FrameworkElement Shape(string kind, double size, string fillKey)
        {
            FrameworkElement shape;
            switch (kind)
            {
                case "record": shape = new Ellipse { Width = size, Height = size }; break;
                case "stop": shape = new Rectangle { Width = size * 0.82, Height = size * 0.82, RadiusX = 1.5, RadiusY = 1.5 }; break;
                default:
                    shape = new Path
                    {
                        Width = size * 0.9, Height = size, Stretch = Stretch.Fill,
                        Data = Geometry.Parse("M0,0 L10,6 L0,12 Z")
                    };
                    break;
            }
            shape.VerticalAlignment = VerticalAlignment.Center;
            Ref(shape, System.Windows.Shapes.Shape.FillProperty, fillKey);
            return shape;
        }

        public static Border Pill(string text, string foregroundKey, string backgroundKey, double size = 11)
        {
            var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 8, 2), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            Ref(b, Border.BackgroundProperty, backgroundKey);
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold };
            Ref(t, TextBlock.ForegroundProperty, foregroundKey);
            b.Child = t;
            return b;
        }

        public static Button Button(object content, Action click, string styleKey = null, string tooltip = null)
        {
            var b = new Button { Content = content };
            if (styleKey != null) b.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
            if (tooltip != null) b.ToolTip = tooltip;
            if (click != null) b.Click += (s, e) => click();
            return b;
        }

        /// <summary>A button with an icon glyph and a label.</summary>
        public static Button IconButton(string glyph, string label, Action click, string styleKey = null, string tooltip = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Glyph(glyph, 13));
            row.Children.Add(new TextBlock { Text = label, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            return Button(row, click, styleKey, tooltip);
        }

        /// <summary>A button that only shows an icon glyph; the tooltip is required because there is no label.</summary>
        public static Button GlyphButton(string glyph, string tooltip, Action click, string styleKey = "Ghost")
        {
            var b = Button(Glyph(glyph, 13), click, styleKey, tooltip);
            b.Padding = new Thickness(8, 4, 8, 4);
            System.Windows.Automation.AutomationProperties.SetName(b, tooltip);
            return b;
        }

        public static Border Card(UIElement child, Thickness? padding = null)
        {
            var b = Style(new Border { Child = child }, "CardBorder");
            if (padding.HasValue) b.Padding = padding.Value;
            return b;
        }

        /// <summary>A tinted note: kind is "Warn", "Info", "Bad" or "Good".</summary>
        public static Border Note(string text, string kind = "Info", string glyph = null)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = Glyph(glyph ?? (kind == "Info" ? GlyphInfo : GlyphWarn), 13, kind);
            icon.Margin = new Thickness(0, 2, 9, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(icon);
            var t = Para(text, "Text");
            Grid.SetColumn(t, 1);
            grid.Children.Add(t);
            var b = new Border { Child = grid, CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 8, 11, 8), Margin = new Thickness(0, 0, 0, 8) };
            Ref(b, Border.BackgroundProperty, kind + "Bg");
            return b;
        }

        public static StackPanel Row(double gap, params UIElement[] children)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < children.Length; i++)
            {
                var fe = children[i] as FrameworkElement;
                if (fe != null && i > 0) fe.Margin = new Thickness(gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
                p.Children.Add(children[i]);
            }
            return p;
        }

        public static StackPanel Stack(double gap, params UIElement[] children)
        {
            var p = new StackPanel();
            for (int i = 0; i < children.Length; i++)
            {
                var fe = children[i] as FrameworkElement;
                if (fe != null && i > 0) fe.Margin = new Thickness(fe.Margin.Left, gap, fe.Margin.Right, fe.Margin.Bottom);
                p.Children.Add(children[i]);
            }
            return p;
        }

        /// <summary>A wrapping row of controls with even spacing (used for button groups).</summary>
        public static WrapPanel Wrap(params UIElement[] children)
        {
            var p = new WrapPanel();
            foreach (UIElement c in children) p.Children.Add(c);
            return p;
        }

        /// <summary>Like <see cref="Row"/> but wraps onto a second line when the space is narrow. Controls that already have a margin keep it.</summary>
        public static WrapPanel WrapRow(double gap, params UIElement[] children)
        {
            var p = new WrapPanel();
            foreach (UIElement child in children)
            {
                var fe = child as FrameworkElement;
                if (fe != null)
                {
                    if (fe.Margin == new Thickness(0)) fe.Margin = new Thickness(0, 0, gap, 5);
                    fe.VerticalAlignment = VerticalAlignment.Center;
                }
                p.Children.Add(child);
            }
            return p;
        }

        /// <summary>A label above a control.</summary>
        public static StackPanel Field(string label, UIElement control)
        {
            var p = new StackPanel();
            var l = Text(label, "Muted", false, 12);
            l.Margin = new Thickness(0, 0, 0, 3);
            p.Children.Add(l);
            p.Children.Add(control);
            return p;
        }

        public static FrameworkElement Spacer(double height) { return new Border { Height = height }; }

        public static bool Confirm(Window owner, string message, string title = "Macro Kit")
        {
            return Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        public static void Info(Window owner, string message, string title = "Macro Kit")
        {
            Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        static MessageBoxResult Show(Window owner, string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            if (owner != null && owner.IsVisible) return MessageBox.Show(owner, message, title, buttons, image, buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK);
            return MessageBox.Show(message, title, buttons, image, buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK);
        }

        /// <summary>A small themed pop-up menu of buttons. Each item closes the menu, then runs.</summary>
        public static Popup Menu(UIElement anchor, params KeyValuePair<string, Action>[] items)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };
            var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
            foreach (var item in items)
            {
                Action action = item.Value;
                var button = Button(item.Key, null, "MenuBtn");
                button.MinWidth = 220;
                button.Click += (s, e) => { popup.IsOpen = false; action(); };
                panel.Children.Add(button);
            }
            var border = new Border { Child = panel, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 4, 8, 12), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35 } };
            Ref(border, Border.BackgroundProperty, "Card");
            Ref(border, Border.BorderBrushProperty, "Border");
            popup.Child = border;
            return popup;
        }
    }
}
