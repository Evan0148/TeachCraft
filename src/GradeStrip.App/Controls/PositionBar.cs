using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GradeStrip.App.ViewModels;

namespace GradeStrip.App.Controls;

/// <summary>
/// 班级名次位置条：在一条轨道上用一个标记点表示该生所在的位置。
/// 具备平滑划入动效 (Entrance Slide)。
/// </summary>
public sealed class PositionBar : Control
{
    public static readonly StyledProperty<double> RatioProperty =
        AvaloniaProperty.Register<PositionBar, double>(nameof(Ratio));

    public static readonly StyledProperty<IBrush?> MarkerBrushProperty =
        AvaloniaProperty.Register<PositionBar, IBrush?>(nameof(MarkerBrush));

    public double Ratio
    {
        get => GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    public IBrush? MarkerBrush
    {
        get => GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    private double _progress = 1.0;
    private Stopwatch? _stopwatch;
    private bool _animating;

    static PositionBar()
    {
        AffectsRender<PositionBar>(RatioProperty, MarkerBrushProperty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartEntranceAnimation();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RatioProperty)
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

        const double durationMs = 320;
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
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var radius = bounds.Height / 2;
        using (context.PushClip(new RoundedRect(bounds, radius)))
        {
            context.FillRectangle(Palette.PaperDeep, bounds);

            const double markerWidth = 4;
            var animatedRatio = Ratio * _progress;
            var ratio = Math.Clamp(animatedRatio, 0, 1);
            var x = ratio * (bounds.Width - markerWidth);
            context.FillRectangle(MarkerBrush ?? Palette.Ink,
                new Rect(x, 0, markerWidth, bounds.Height));
        }
    }
}
