using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using GradeWeave.Core.Models;
using GradeWeave.Core.Timetable;

namespace GradeWeave.App;

/// <summary>
/// 「作息时间表」编辑窗口：左侧时段列表、中间详情、右侧节次预览，顶部一条横向时间轴。
/// 确认时 <c>Close(TimetableConfirmResult)</c>，取消时 <c>Close(null)</c>；
/// 工程本身只在确认时才通过 <see cref="TimetableService.Confirm"/> 改动。
/// </summary>
public partial class TimetableEditorWindow : Window
{
    private static readonly IBrush TickBrush = new SolidColorBrush(Color.Parse("#E7E3D8"));
    private static readonly IBrush TickTextBrush = new SolidColorBrush(Color.Parse("#6F6A5E"));
    private static readonly IBrush BlockBorderBrush = new SolidColorBrush(Color.Parse("#33201E1A"));

    private readonly TimetableEditorViewModel? _viewModel;
    private bool _closeReady;
    private bool _guardOpen;

    public TimetableEditorWindow()
        : this(new WeaveProject())
    {
    }

    public TimetableEditorWindow(WeaveProject project)
    {
        InitializeComponent();

        _viewModel = new TimetableEditorViewModel(project);
        DataContext = _viewModel;

        _viewModel.RowsChanged += (_, _) => DrawTimeline();
        TimelineCanvas.SizeChanged += (_, _) => DrawTimeline();
        Opened += (_, _) =>
        {
            DrawTimeline();
            ScreenFit.Clamp(this);
        };
        DrawTimeline();
    }

    private void OnAdd(object? sender, RoutedEventArgs e) => _viewModel?.AddRow();

    private void OnDelete(object? sender, RoutedEventArgs e) => _viewModel?.DeleteSelected();

    private void OnMoveUp(object? sender, RoutedEventArgs e) => _viewModel?.MoveUp();

    private void OnMoveDown(object? sender, RoutedEventArgs e) => _viewModel?.MoveDown();

    private void OnDuplicate(object? sender, RoutedEventArgs e) => _viewModel?.DuplicateSelected();

    private void OnSort(object? sender, RoutedEventArgs e) => _viewModel?.SortByTime();

    private void OnApplyPreset(object? sender, RoutedEventArgs e) => _viewModel?.ApplyPreset();

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _closeReady = true;
        Close(null);
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        var result = _viewModel.TryConfirm();
        if (result is null) return;

        _closeReady = true;
        Close(result);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_viewModel is null || _closeReady || _guardOpen || !_viewModel.IsDirty) return;

        e.Cancel = true;
        _guardOpen = true;
        _ = ConfirmDiscardAsync();
    }

    private async Task ConfirmDiscardAsync()
    {
        var discard = await EditorCloseGuard.ShowAsync(this, "作息时间表的改动尚未确认，放弃并关闭？");
        _guardOpen = false;
        if (discard)
        {
            _closeReady = true;
            Close();
        }
    }

    /// <summary>按最早到最晚时间等比绘制每个时段的色块，并在整点画刻度。</summary>
    private void DrawTimeline()
    {
        if (_viewModel is null) return;

        var canvas = TimelineCanvas;
        var width = canvas.Bounds.Width;
        if (width < 80) return;

        canvas.Children.Clear();

        var rows = _viewModel.Rows
            .Where(row => row.StartMinutes >= 0 && row.EndMinutes > row.StartMinutes)
            .OrderBy(row => row.StartMinutes)
            .ToList();

        if (rows.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "还没有可绘制的时间段，先在左侧添加一节正课。",
                FontSize = 10,
                Foreground = TickTextBrush,
            };
            Canvas.SetLeft(empty, 4);
            Canvas.SetTop(empty, 24);
            canvas.Children.Add(empty);
            return;
        }

        var min = rows.Min(row => row.StartMinutes) / 60 * 60;
        var max = (rows.Max(row => row.EndMinutes) + 59) / 60 * 60;
        if (max > 24 * 60) max = 24 * 60;
        if (max - min < 120) max = Math.Min(24 * 60, min + 120);

        const double pad = 4;
        var usable = Math.Max(1, width - pad * 2);
        double ToX(int minutes) => pad + (minutes - min) / (double)(max - min) * usable;

        for (var minute = min; minute <= max; minute += 60)
        {
            var x = ToX(minute);
            canvas.Children.Add(new Line
            {
                StartPoint = new Point(x, 12),
                EndPoint = new Point(x, 52),
                Stroke = TickBrush,
                StrokeThickness = 1,
            });

            var label = new TextBlock
            {
                Text = $"{minute / 60:00}:00",
                FontSize = 10,
                Foreground = TickTextBrush,
            };
            label.Classes.Add("mono");
            Canvas.SetLeft(label, Math.Clamp(x - 14, 0, Math.Max(0, width - 28)));
            Canvas.SetTop(label, 54);
            canvas.Children.Add(label);
        }

        foreach (var row in rows)
        {
            var left = ToX(row.StartMinutes);
            var right = ToX(row.EndMinutes);
            var blockWidth = Math.Max(4, right - left);
            var block = new Border
            {
                Width = blockWidth,
                Height = 32,
                Background = row.Color,
                BorderBrush = BlockBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                ClipToBounds = true,
            };

            if (blockWidth > 54)
            {
                block.Child = new TextBlock
                {
                    Text = row.DisplayName,
                    FontSize = 10,
                    Foreground = row.TextColor,
                    Margin = new Thickness(6, 0),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
            }

            ToolTip.SetTip(block, $"{row.DisplayName} · {row.TimeText}");
            Canvas.SetLeft(block, left);
            Canvas.SetTop(block, 16);
            canvas.Children.Add(block);
        }
    }
}
