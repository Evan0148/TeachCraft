using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace GradeStrip.App.Controls;

/// <summary>
/// 类似 Microsoft PowerPoint 平滑 (Morph) 切换的文本控件：
/// 文本变动时，公共标点（如 " / "）顺滑横向平移，变动的词语以垂直轻浮 + 透明度淡变就位。
/// 严格修复了字体排版引擎吞掉末尾空格导致右侧文本突变跳动的问题。
/// </summary>
public sealed class MorphTextBlock : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MorphTextBlock, string?>(nameof(Text));

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextBlock.FontFamilyProperty.AddOwner<MorphTextBlock>();

    public static readonly StyledProperty<double> FontSizeProperty =
        TextBlock.FontSizeProperty.AddOwner<MorphTextBlock>();

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextBlock.FontWeightProperty.AddOwner<MorphTextBlock>();

    public static readonly StyledProperty<FontStyle> FontStyleProperty =
        TextBlock.FontStyleProperty.AddOwner<MorphTextBlock>();

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextBlock.ForegroundProperty.AddOwner<MorphTextBlock>();

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontStyle FontStyle
    {
        get => GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private string? _oldText;
    private string? _currentText;
    private double _progress = 1.0;
    private Stopwatch? _stopwatch;
    private bool _animating;

    static MorphTextBlock()
    {
        AffectsMeasure<MorphTextBlock>(TextProperty, FontSizeProperty, FontFamilyProperty, FontWeightProperty);
        AffectsRender<MorphTextBlock>(ForegroundProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            var oldVal = change.GetOldValue<string?>();
            var newVal = change.GetNewValue<string?>();

            if (oldVal is not null && newVal is not null && oldVal != newVal)
            {
                _oldText = oldVal;
                _currentText = newVal;
                StartMorphAnimation();
            }
            else
            {
                _currentText = newVal;
                _progress = 1.0;
                _animating = false;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    private void StartMorphAnimation()
    {
        _stopwatch = Stopwatch.StartNew();
        _animating = true;
        _progress = 0;
        Dispatcher.UIThread.Post(TickAnimation, DispatcherPriority.Render);
    }

    private void TickAnimation()
    {
        if (!_animating || _stopwatch is null) return;

        const double durationMs = 240;
        var elapsed = _stopwatch.Elapsed.TotalMilliseconds;
        var t = Math.Clamp(elapsed / durationMs, 0, 1);

        // Apple / PPT 风格 CubicEaseOut 缓动
        _progress = 1 - Math.Pow(1 - t, 3);
        InvalidateMeasure();
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
            _oldText = null;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private FormattedText CreateFormattedText(string text)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily ?? FontFamily.Default, FontStyle, FontWeight),
            FontSize,
            Foreground ?? Brushes.Black
        );
    }

    /// <summary>
    /// 精确测量一个空格的实际排版宽度，解决字形排版引擎默认丢弃尾部空格导致宽度偏窄的系统级问题。
    /// </summary>
    private double GetSpaceWidth()
    {
        var withSpace = CreateFormattedText("A A").Width;
        var withoutSpace = CreateFormattedText("AA").Width;
        var diff = withSpace - withoutSpace;
        return diff > 0 ? diff : FontSize * 0.45;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (string.IsNullOrEmpty(_currentText))
            return new Size(0, FontSize * 1.4);

        var currentFt = CreateFormattedText(_currentText);

        if (_animating && !string.IsNullOrEmpty(_oldText))
        {
            var oldFt = CreateFormattedText(_oldText);
            var w = oldFt.Width + (currentFt.Width - oldFt.Width) * _progress;
            var h = Math.Max(oldFt.Height, currentFt.Height);
            return new Size(w, h);
        }

        return new Size(currentFt.Width, currentFt.Height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (string.IsNullOrEmpty(_currentText) && string.IsNullOrEmpty(_oldText))
            return;

        // 统一解析带有 " / " 的模式，无论是否在动画中，均采用一致的段落对齐标准
        const string sep = " / ";
        var textToRender = _currentText ?? string.Empty;

        if (!_animating || string.IsNullOrEmpty(_oldText))
        {
            var parts = textToRender.Split(sep, 2);
            if (parts.Length == 2)
            {
                // 静止态拆分渲染，确保静止与动画过程中的坐标 100% 严丝合缝
                RenderSplitStatic(context, parts[0], parts[1]);
            }
            else
            {
                var ft = CreateFormattedText(textToRender);
                context.DrawText(ft, new Point(0, 0));
            }
            return;
        }

        var oldParts = _oldText.Split(sep, 2);
        var newParts = textToRender.Split(sep, 2);

        if (oldParts.Length == 2 && newParts.Length == 2)
        {
            RenderMorphSplit(context, oldParts[0], oldParts[1], newParts[0], newParts[1]);
        }
        else
        {
            RenderMorphFull(context, _oldText, textToRender);
        }
    }

    private void RenderSplitStatic(DrawingContext context, string left, string right)
    {
        var spaceW = GetSpaceWidth();
        var ftLeft = CreateFormattedText(left);
        var ftSlash = CreateFormattedText("/");
        var ftRight = CreateFormattedText(right);

        var slashX = ftLeft.Width + spaceW;
        var rightX = slashX + ftSlash.Width + spaceW;

        context.DrawText(ftLeft, new Point(0, 0));
        context.DrawText(ftSlash, new Point(slashX, 0));
        context.DrawText(ftRight, new Point(rightX, 0));
    }

    private void RenderMorphSplit(DrawingContext context, string oldLeft, string oldRight, string newLeft, string newRight)
    {
        const double drift = 4.5;
        var p = _progress;
        var spaceW = GetSpaceWidth();

        var ftOldLeft = CreateFormattedText(oldLeft);
        var ftNewLeft = CreateFormattedText(newLeft);
        var ftSlash = CreateFormattedText("/");
        var ftOldRight = CreateFormattedText(oldRight);
        var ftNewRight = CreateFormattedText(newRight);

        // 1. 左侧词语平滑 Morph (旧词上浮淡出，新词下方就位)
        using (context.PushOpacity(Math.Clamp(1.0 - p, 0, 1)))
        {
            context.DrawText(ftOldLeft, new Point(0, -drift * p));
        }
        using (context.PushOpacity(Math.Clamp(p, 0, 1)))
        {
            context.DrawText(ftNewLeft, new Point(0, drift * (1.0 - p)));
        }

        // 2. 中间分隔符 "/" 横向平滑滑移
        // 关键点：斜杠左侧始终保证刚好空出 1 个精准的空格宽度 spaceW！
        var currentLeftWidth = ftOldLeft.Width + (ftNewLeft.Width - ftOldLeft.Width) * p;
        var slashX = currentLeftWidth + spaceW;
        context.DrawText(ftSlash, new Point(slashX, 0));

        // 3. 右侧词语平滑 Morph
        // 关键点：右侧起始位置始终为 slashX + ftSlash.Width + spaceW，右侧也有完整 1 个空格！
        var rightX = slashX + ftSlash.Width + spaceW;
        using (context.PushOpacity(Math.Clamp(1.0 - p, 0, 1)))
        {
            context.DrawText(ftOldRight, new Point(rightX, -drift * p));
        }
        using (context.PushOpacity(Math.Clamp(p, 0, 1)))
        {
            context.DrawText(ftNewRight, new Point(rightX, drift * (1.0 - p)));
        }
    }

    private void RenderMorphFull(DrawingContext context, string oldText, string newText)
    {
        const double drift = 5.0;
        var p = _progress;

        var ftOld = CreateFormattedText(oldText);
        var ftNew = CreateFormattedText(newText);

        using (context.PushOpacity(Math.Clamp(1.0 - p, 0, 1)))
        {
            context.DrawText(ftOld, new Point(0, -drift * p));
        }
        using (context.PushOpacity(Math.Clamp(p, 0, 1)))
        {
            context.DrawText(ftNew, new Point(0, drift * (1.0 - p)));
        }
    }
}
