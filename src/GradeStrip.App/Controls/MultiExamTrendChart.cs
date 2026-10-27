using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GradeStrip.App.ViewModels;

namespace GradeStrip.App.Controls;

public enum TrendChartMode
{
    Score,
    ClassRank,
    SchoolRank
}

/// <summary>
/// 多次考试走势折线图：以纸质风格绘制学生在历次考试中的分数或名次变动。
/// 支持分值模式（向上为高分）与名次模式（向上为高名次，Y轴反转）。
/// </summary>
public sealed class MultiExamTrendChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartPoint>?> PointsProperty =
        AvaloniaProperty.Register<MultiExamTrendChart, IReadOnlyList<ChartPoint>?>(nameof(Points));

    public static readonly StyledProperty<TrendChartMode> ModeProperty =
        AvaloniaProperty.Register<MultiExamTrendChart, TrendChartMode>(nameof(Mode));

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<MultiExamTrendChart, IBrush?>(nameof(LineBrush));

    public IReadOnlyList<ChartPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public TrendChartMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    private double _progress = 1.0;
    private Stopwatch? _stopwatch;
    private bool _animating;

    static MultiExamTrendChart()
    {
        AffectsRender<MultiExamTrendChart>(PointsProperty, ModeProperty, LineBrushProperty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartEntranceAnimation();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PointsProperty || change.Property == ModeProperty)
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

        const double durationMs = 360;
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
        if (bounds.Width <= 100 || bounds.Height <= 80 || Points is null || Points.Count == 0)
            return;

        var points = Points;
        var isRankMode = Mode is TrendChartMode.ClassRank or TrendChartMode.SchoolRank;

        const double marginLeft = 48;
        const double marginRight = 36;
        const double marginTop = 36;
        const double marginBottom = 42;

        var plotWidth = bounds.Width - marginLeft - marginRight;
        var plotHeight = bounds.Height - marginTop - marginBottom;

        if (plotWidth <= 20 || plotHeight <= 20) return;

        var validPoints = points.Where(p => !p.IsAbsent).ToList();
        if (validPoints.Count == 0) return;

        // 确定 Y 轴区间
        double minY, maxY;
        if (!isRankMode)
        {
            var minVal = validPoints.Min(p => p.Value);
            var maxVal = validPoints.Max(p => p.Value);
            var span = maxVal - minVal;
            if (span < 10) span = 10;
            minY = Math.Max(0, minVal - span * 0.2);
            maxY = maxVal + span * 0.2;
        }
        else
        {
            // 名次模式：第 1 名在最顶端
            var minRank = validPoints.Min(p => p.Value); // 最好名次 (如 1)
            var maxRank = validPoints.Max(p => p.Value); // 最差名次 (如 25)
            var span = maxRank - minRank;
            if (span < 4) span = 4;
            minY = Math.Max(1, minRank - span * 0.15); // 顶端名次
            maxY = maxRank + span * 0.2;              // 底端名次
        }

        // 绘制背景参考虚线 (3 条)
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#EBE7DE")), 1,
            new DashStyle(new double[] { 4, 3 }, 0));
        var axisTextBrush = new SolidColorBrush(Color.Parse("#8C877D"));
        var baseFont = FontFamily.Default;
        var axisTypeface = new Typeface(baseFont, FontStyle.Normal, FontWeight.Normal);

        for (var step = 0; step <= 3; step++)
        {
            var yRatio = (double)step / 3.0;
            var curY = marginTop + yRatio * plotHeight;
            context.DrawLine(gridPen, new Point(marginLeft, curY), new Point(bounds.Width - marginRight, curY));

            double labelVal;
            if (!isRankMode)
            {
                labelVal = maxY - yRatio * (maxY - minY);
            }
            else
            {
                // 名次模式下，yRatio=0 对应 minY (最优)，yRatio=1 对应 maxY (末尾)
                labelVal = minY + yRatio * (maxY - minY);
            }

            var labelText = isRankMode ? $"{labelVal:0}名" : $"{labelVal:0}";
            var ft = new FormattedText(
                labelText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                axisTypeface,
                9,
                axisTextBrush
            );
            context.DrawText(ft, new Point(marginLeft - ft.Width - 8, curY - ft.Height / 2));
        }

        // 计算各数据点的像素坐标
        var coords = new Point[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var x = points.Count == 1
                ? marginLeft + plotWidth / 2
                : marginLeft + i * (plotWidth / (points.Count - 1));

            var pt = points[i];
            double y;
            if (pt.IsAbsent)
            {
                y = marginTop + plotHeight; // 缺考置于底部
            }
            else
            {
                double ratio;
                if (!isRankMode)
                {
                    ratio = (pt.Value - minY) / Math.Max(0.001, maxY - minY);
                    var targetY = marginTop + (1 - ratio) * plotHeight;
                    y = (marginTop + plotHeight) - (marginTop + plotHeight - targetY) * _progress;
                }
                else
                {
                    // 名次模式：名次越小 Y 越靠近 marginTop
                    ratio = (pt.Value - minY) / Math.Max(0.001, maxY - minY);
                    var targetY = marginTop + ratio * plotHeight;
                    y = (marginTop + plotHeight) - (marginTop + plotHeight - targetY) * _progress;
                }
            }

            coords[i] = new Point(x, y);
        }

        var themeBrush = LineBrush ?? (isRankMode
            ? new SolidColorBrush(Color.Parse("#1F3864")) // 藏蓝
            : new SolidColorBrush(Color.Parse("#D97757"))); // 陶土橙

        var themeColor = ((SolidColorBrush)themeBrush).Color;

        // 绘制折线下方的柔和半透明面积渐变
        if (coords.Length >= 2 && _progress > 0.05)
        {
            var areaGeometry = new StreamGeometry();
            using (var sgc = areaGeometry.Open())
            {
                sgc.BeginFigure(new Point(coords[0].X, marginTop + plotHeight), isFilled: true);
                for (var i = 0; i < coords.Length; i++)
                {
                    sgc.LineTo(coords[i]);
                }
                sgc.LineTo(new Point(coords[^1].X, marginTop + plotHeight));
                sgc.EndFigure(isClosed: true);
            }

            var areaGradient = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(48, themeColor.R, themeColor.G, themeColor.B), 0),
                    new GradientStop(Color.FromArgb(4, themeColor.R, themeColor.G, themeColor.B), 1)
                }
            };
            context.DrawGeometry(areaGradient, null, areaGeometry);
        }

        // 绘制折线
        var linePen = new Pen(themeBrush, 2.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        for (var i = 0; i < coords.Length - 1; i++)
        {
            if (points[i].IsAbsent || points[i + 1].IsAbsent) continue;
            context.DrawLine(linePen, coords[i], coords[i + 1]);
        }

        // 绘制跨考进退步角标 (在两点连线中间上方)
        for (var i = 1; i < coords.Length; i++)
        {
            if (points[i].IsAbsent || points[i - 1].IsAbsent) continue;
            var deltaText = points[i].DeltaText;
            if (!string.IsNullOrEmpty(deltaText) && deltaText != "—")
            {
                var midX = (coords[i - 1].X + coords[i].X) / 2;
                var midY = (coords[i - 1].Y + coords[i].Y) / 2 - 14;

                var deltaBrush = points[i].DeltaBrush ?? axisTextBrush;
                var ftDelta = new FormattedText(
                    deltaText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    axisTypeface,
                    9.5,
                    deltaBrush
                );
                context.DrawText(ftDelta, new Point(midX - ftDelta.Width / 2, midY));
            }
        }

        // 绘制各数据节点与标签
        var whiteBrush = new SolidColorBrush(Color.Parse("#FAF9F5"));
        var inkBrush = new SolidColorBrush(Color.Parse("#1C1917"));
        var valueTypeface = new Typeface(baseFont, FontStyle.Normal, FontWeight.SemiBold);
        var subTypeface = new Typeface(baseFont, FontStyle.Normal, FontWeight.Normal);

        for (var i = 0; i < coords.Length; i++)
        {
            var pt = points[i];
            var center = coords[i];

            if (!pt.IsAbsent)
            {
                // 外圈光晕/白底
                context.DrawEllipse(whiteBrush, null, center, 6, 6);
                // 实体圆点
                context.DrawEllipse(themeBrush, null, center, 4.2, 4.2);

                // 点上方数值标签
                var valText = pt.ValueText;
                var ftVal = new FormattedText(
                    valText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    valueTypeface,
                    10.5,
                    inkBrush
                );
                context.DrawText(ftVal, new Point(center.X - ftVal.Width / 2, center.Y - ftVal.Height - 6));
            }
            else
            {
                // 缺考节点用空心小圆点标示
                context.DrawEllipse(null, new Pen(axisTextBrush, 1.5), center, 4, 4);
                var ftAbsent = new FormattedText(
                    "缺考",
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    subTypeface,
                    9.5,
                    axisTextBrush
                );
                context.DrawText(ftAbsent, new Point(center.X - ftAbsent.Width / 2, center.Y - ftAbsent.Height - 6));
            }

            // X 轴下方考次标签
            var ftOrder = new FormattedText(
                pt.ExamOrderText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                valueTypeface,
                10,
                inkBrush
            );
            context.DrawText(ftOrder, new Point(center.X - ftOrder.Width / 2, marginTop + plotHeight + 8));

            var ftName = new FormattedText(
                pt.ExamName,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                subTypeface,
                8.5,
                axisTextBrush
            );
            context.DrawText(ftName, new Point(center.X - ftName.Width / 2, marginTop + plotHeight + 22));
        }
    }
}
