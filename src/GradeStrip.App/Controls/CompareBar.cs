using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GradeStrip.App.ViewModels;

namespace GradeStrip.App.Controls;

/// <summary>
/// 「个人 vs 班级」对比条：底色为满分轨道，填充到学生得分率，并画出班级均分刻度。
/// 具备平滑展开动效 (Entrance / Progress Sweep)。
/// </summary>
public sealed class CompareBar : Control
{
    public static readonly StyledProperty<double> FillProperty =
        AvaloniaProperty.Register<CompareBar, double>(nameof(Fill));

    public static readonly StyledProperty<double> AverageProperty =
        AvaloniaProperty.Register<CompareBar, double>(nameof(Average));

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<CompareBar, IBrush?>(nameof(FillBrush));

    public static readonly StyledProperty<IBrush?> AverageBrushProperty =
        AvaloniaProperty.Register<CompareBar, IBrush?>(nameof(AverageBrush));

    public double Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double Average
    {
        get => GetValue(AverageProperty);
        set => SetValue(AverageProperty, value);
    }

    public IBrush? FillBrush
    {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public IBrush? AverageBrush
    {
        get => GetValue(AverageBrushProperty);
        set => SetValue(AverageBrushProperty, value);
    }

    private double _progress = 1.0;
    private Stopwatch? _stopwatch;
    private bool _animating;

    static CompareBar()
    {
        AffectsRender<CompareBar>(FillProperty, AverageProperty, FillBrushProperty, AverageBrushProperty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartEntranceAnimation();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FillProperty || change.Property == AverageProperty)
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

        // Apple / Emil Kowalski 黄金缓动曲线 CubicEaseOut: 1 - (1 - t)^3
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

            var animatedFill = Fill * _progress;
            var fill = Math.Clamp(animatedFill, 0, 1) * bounds.Width;
            if (fill > 0)
                context.FillRectangle(FillBrush ?? Palette.Ink, new Rect(0, 0, fill, bounds.Height));
        }

        var animatedAvg = Average * _progress;
        var avg = Math.Clamp(animatedAvg, 0, 1);
        if (avg > 0 && avg < 1)
        {
            var x = Math.Round(avg * bounds.Width) + 0.5;

            // 用一条陶土色细线标出班级均分位置：落在深色填充或浅色轨道上都清晰
            var pen = new Pen(AverageBrush ?? Palette.AccentDeep, 2);
            context.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));
        }
    }
}
