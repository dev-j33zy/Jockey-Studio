using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace JockeyStudio.Wpf.Controls;

/// <summary>Layout panel for the deck area. Replicates the original CSS grid
/// `repeat(auto-fill, minmax(320px, 1fr))` with a 16px gap: columns are computed
/// from the available width, every column is the same size, every tile in a row
/// is stretched to equal height (the row height is the tallest tile), and the
/// grid snaps flush to the container edges.</summary>
public sealed class TileGridPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(TileGridPanel),
        new FrameworkPropertyMetadata(320.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemGapProperty = DependencyProperty.Register(
        nameof(ItemGap), typeof(double), typeof(TileGridPanel),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double ItemGap { get => (double)GetValue(ItemGapProperty); set => SetValue(ItemGapProperty, value); }

    private int _columns;

    private (int columns, double colWidth) ComputeColumns(double width)
    {
        double gap = ItemGap;
        double minW = MinItemWidth;
        int cols = Math.Max(1, (int)Math.Floor((width + gap) / (minW + gap)));
        double colW = cols > 0 ? width / cols - gap : width;
        return (cols, Math.Max(colW, 0.0));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double avail = Math.Max(0.0, availableSize.Width);
        (_columns, double colW) = ComputeColumns(avail);

        double rowH = 0.0;
        int count = 0;
        double totalH = 0.0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Measure(new Size(colW, double.PositiveInfinity));
            rowH = Math.Max(rowH, InternalChildren[i].DesiredSize.Height);
            count++;
            if (count == _columns)
            {
                totalH += rowH + ItemGap;
                rowH = 0.0;
                count = 0;
            }
        }
        if (count > 0) totalH += rowH;
        else if (InternalChildren.Count > 0) totalH -= ItemGap;
        return new Size(avail, Math.Max(0.0, totalH));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double avail = Math.Max(0.0, finalSize.Width);
        (_columns, double colW) = ComputeColumns(avail);

        List<double> rowHeights = GetRowHeights();
        double y = 0.0;
        int idx = 0;
        foreach (double rowH in rowHeights)
        {
            double x = 0.0;
            for (int c = 0; c < _columns && idx < InternalChildren.Count; c++, idx++)
            {
                InternalChildren[idx].Arrange(new Rect(x, y, colW, rowH));
                x += colW + ItemGap;
            }
            y += rowH + ItemGap;
        }
        return finalSize;
    }

    private List<double> GetRowHeights()
    {
        var rows = new List<double>();
        double rowH = 0.0;
        int count = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            rowH = Math.Max(rowH, InternalChildren[i].DesiredSize.Height);
            count++;
            if (count == _columns)
            {
                rows.Add(rowH);
                rowH = 0.0;
                count = 0;
            }
        }
        if (count > 0) rows.Add(rowH);
        return rows;
    }
}