using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MovieShelf.App;

/// <summary>Lays children out left to right and wraps to a new line when the row is full.</summary>
public sealed class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; }
    public double VerticalSpacing { get; set; }

    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
    {
        double x = 0, rowH = 0, w = 0, h = 0;
        foreach (var c in Children)
        {
            c.Measure(new Windows.Foundation.Size(available.Width, available.Height));
            var d = c.DesiredSize;
            if (x > 0 && x + d.Width > available.Width)
            {
                w = Math.Max(w, x - HorizontalSpacing);
                h += rowH + VerticalSpacing;
                x = 0;
                rowH = 0;
            }
            x += d.Width + HorizontalSpacing;
            rowH = Math.Max(rowH, d.Height);
        }
        w = Math.Max(w, x > 0 ? x - HorizontalSpacing : 0);
        return new Windows.Foundation.Size(w, h + rowH);
    }

    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size final)
    {
        double x = 0, y = 0, rowH = 0;
        foreach (var c in Children)
        {
            var d = c.DesiredSize;
            if (x > 0 && x + d.Width > final.Width)
            {
                x = 0;
                y += rowH + VerticalSpacing;
                rowH = 0;
            }
            c.Arrange(new Windows.Foundation.Rect(x, y, d.Width, d.Height));
            x += d.Width + HorizontalSpacing;
            rowH = Math.Max(rowH, d.Height);
        }
        return new Windows.Foundation.Size(final.Width, y + rowH);
    }
}
