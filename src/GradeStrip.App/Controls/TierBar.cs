using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GradeStrip.App.ViewModels;

namespace GradeStrip.App.Controls;

/// <summary>按权重铺满宽度的堆叠条，用于班次分布。支持由左至右平滑展开。</summary>
public sealed class TierBar : Control
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<TierBar, IEnumerable?>(nameof(ItemsSource));

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private double _progress = 1.0;
    private Stopwatch? _stopwatch;
    private bool _animating;

    static TierBar()
    {
        AffectsRender<TierBar>(ItemsSourceProperty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartEntranceAnimation();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
        {
            StartEntranceAnimation();
        }
    }

    private void StartEntranceAnimation()
    {
        _stopwatch = Stopwatch.StartNew();
        _animating = true;
        _progress = 0;
        Dispatcher.UIThread.Post(TickAnimation, DispatcherPriority.Render);
    }

    private void TickAnimation()
    {
        if (!_animating || _stopwatch is null) return;

        const double durationMs = 350;
        var elapsed = _stopwatch.Elapsed.TotalMilliseconds;
        var t = Math.Clamp(elapsed / durationMs, 0, 1);

        _progress = 1 - Math.Pow(1 - t, 3);
        InvalidateVisual();

        if (t < 1)
        {
            Dispatcher.UIThread.Post(TickAnimation, DispatcherPriority.Render);
        }
        else
        {
            _progress = 1.0;
            _animating = false;
            _stopwatch.Stop();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var items = new List<TierStat>();
        if (ItemsSource is not null)
            foreach (var item in ItemsSource)
                if (item is TierStat stat && stat.Weight > 0) items.Add(stat);

        var total = 0d;
        foreach (var item in items) total += item.Weight;
        if (total <= 0) return;

        using (context.PushClip(new RoundedRect(bounds, bounds.Height / 2)))
        {
            context.FillRectangle(Palette.PaperDeep, bounds);

            var activeWidth = bounds.Width * _progress;
            var x = 0d;
            for (var i = 0; i < items.Count; i++)
            {
                var width = i == items.Count - 1
                    ? activeWidth - x
                    : activeWidth * items[i].Weight / total;
                if (width > 0)
                {
                    context.FillRectangle(items[i].Fill, new Rect(x, 0, width, bounds.Height));
                    x += width;
                }
            }
        }
    }
}
