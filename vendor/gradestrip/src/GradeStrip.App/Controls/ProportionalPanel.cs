using System;
using Avalonia;
using Avalonia.Controls;

namespace GradeStrip.App.Controls;

/// <summary>按 Weight 比例横向分配宽度的面板，用于班次分布的堆叠条。</summary>
public sealed class ProportionalPanel : Panel
{
    public static readonly StyledProperty<double> WeightProperty =
        AvaloniaProperty.RegisterAttached<ProportionalPanel, Control, double>("Weight", 1);

    public static void SetWeight(Control control, double value) => control.SetValue(WeightProperty, value);

    public static double GetWeight(Control control) => control.GetValue(WeightProperty);

    static ProportionalPanel()
    {
        AffectsParentArrange<ProportionalPanel>(WeightProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0d;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var total = 0d;
        foreach (var child in Children) total += GetWeight(child);
        if (total <= 0) return finalSize;

        var x = 0d;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var width = i == Children.Count - 1
                ? finalSize.Width - x
                : finalSize.Width * GetWeight(child) / total;
            child.Arrange(new Rect(x, 0, Math.Max(0, width), finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
