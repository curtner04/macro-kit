using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace MacroKit.App
{
    /// <summary>
    /// 1.13.3: a red outline drawn around a control, to point at it. The how-to guides' Show me button flashes it for a few
    /// seconds, and the self test leaves it on while it takes the guides' pictures.
    /// </summary>
    public sealed class Highlight : Adorner
    {
        static readonly Pen Outline = MakePen();
        static readonly System.Collections.Generic.List<Highlight> flashing = new System.Collections.Generic.List<Highlight>();

        static Pen MakePen()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
            brush.Freeze();
            var pen = new Pen(brush, 3);
            pen.Freeze();
            return pen;
        }

        Highlight(UIElement element) : base(element) { IsHitTestVisible = false; }

        protected override void OnRender(DrawingContext dc)
        {
            var r = new Rect(AdornedElement.RenderSize);
            r.Inflate(4, 4);
            dc.DrawRoundedRectangle(null, Outline, r, 7, 7);
        }

        /// <summary>Outlines <paramref name="element"/> until <see cref="Remove"/>; null when it has no adorner layer (not shown).</summary>
        public static Highlight Add(UIElement element)
        {
            if (element == null) return null;
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(element);
            if (layer == null) return null;
            var h = new Highlight(element);
            layer.Add(h);
            return h;
        }

        public void Remove()
        {
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(AdornedElement);
            if (layer != null) layer.Remove(this);
        }

        /// <summary>Outlines the element for a few seconds.</summary>
        public static void Flash(UIElement element, int milliseconds = 3500)
        {
            Highlight h = Add(element);
            if (h == null) return;
            flashing.Add(h);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (s, e) => { timer.Stop(); h.Remove(); flashing.Remove(h); };
            timer.Start();
        }

        /// <summary>Takes away every outline still flashing now (the self test, before its next pictures).</summary>
        public static void ClearFlashes()
        {
            foreach (Highlight h in flashing) h.Remove();
            flashing.Clear();
        }
    }
}
