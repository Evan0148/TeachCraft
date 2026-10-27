using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SeatGrid.Core;
using SeatGrid.Core.Export;
using SeatGrid.Core.Rendering;
using SeatGrid.Core.Roster;
using TeachCraft.Shared;

namespace SeatGrid.App;

public partial class SeatGridWindow : Window
{
    private string? _rosterPath;

    // ----------------------------- 座位 / 行列拖拽 -----------------------------
    private enum DragMode { None, Seat, Row, Column }

    private DragMode _dragMode;
    private SeatCellViewModel? _dragSource;   // Seat 模式：被拖的学生卡
    private int _dragRow;                     // Row 模式：源排（0 基）
    private int _dragGroup;                   // Column 模式：源组（0 基）
    private int _dragColumn;                  // Column 模式：源列（0 基组内列）
    private Border? _dragGhost;
    private SeatCellViewModel? _dropTarget;
    private (int? Row, int? Group, int? Column) _hoverLine;
    private bool _dragStarted;
    private bool _suppressSeatClick;
    private Point _pressPoint;
    private Point _grabOffset;          // 按下点相对源卡左上角的偏移，拖拽时保持"抓哪儿是哪儿"
    private Size _dragSourceSize;       // 源卡实际尺寸，幽灵不再用固定 92×56
    private readonly List<SeatCellViewModel> _lineHighlight = new();
    private readonly VelocitySampler _dragVelocity = new();

    // 松手后的幽灵飞行：120–200ms 临界阻尼飞向落点，到位后同一帧提交数据，不留空档。
    private bool _ghostFlying;
    private Point _flyCurrent;          // 幽灵当前左上角（窗口坐标，与 DragLayer 同系）
    private Point _flyTarget;           // 幽灵目标左上角
    private TimeSpan _flyDuration;
    private double _flyLastTime;
    private DispatcherTimer? _flyTimer;
    private Stopwatch? _flyClock;
    private Action? _flyCommit;

    private static double NowSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public SeatGridWindow() : this(new SeatGridViewModel())
    {
    }

    public SeatGridWindow(SeatGridViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // 座位是 Button，Avalonia 的 Button 会把 PointerPressed / PointerReleased 标记为已处理，
        // XAML 里默认挂的指针处理器收不到这两个事件，拖拽会永远停在按下状态。
        // 统一在窗口层用 handledEventsToo 接管：按下/释放走隧道，保证在 Button 改状态之前处理。
        AddHandler(PointerPressedEvent, OnSeatPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnSeatPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnSeatPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnSeatPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);

        // 布局滑块按下时先挂起重建，松手后一次性刷新，避免逐 tick 重建整张座位表。
        AddHandler(PointerPressedEvent, OnLayoutSliderPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnLayoutSliderPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnLayoutSliderCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

        // 小屏（1366×768 / 125% 缩放）上把窗口钳进工作区，避免底部状态栏被任务栏挡住。
        Opened += (_, _) => WindowFit.Clamp(this);
    }

    private SeatGridViewModel ViewModel => (SeatGridViewModel)DataContext!;

    private bool _closeConfirmed;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // 名单与排座只存在内存里：关窗前拦一次，避免一节备课被误触 X 归零。
        if (!_closeConfirmed && ViewModel.StudentCount > 0)
        {
            e.Cancel = true;
            Dispatcher.UIThread.Post(async () =>
            {
                if (await ConfirmDialog.ShowAsync(
                        this, "关闭归位？",
                        $"当前 {ViewModel.StudentCount} 人名单与排座只保存在内存中，关闭后不会保留。可先用「导出配置」留档。",
                        "放弃并关闭", "继续编辑", danger: true))
                {
                    _closeConfirmed = true;
                    Close();
                }
            });
        }

        base.OnClosing(e);
    }

    /// <summary>设计走查用：装载演示名单并截图。</summary>
    public void LoadDemoData()
    {
        ViewModel.LoadRoster(DemoRoster.Create());
        ViewModel.ArrangeModeIndex = 1;
        ViewModel.AutoArrange();
        ViewModel.SetStatus("演示名单 · 已按男女交替自动排座");
    }

    public void CaptureTo(string path)
    {
        var pixelSize = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        bitmap.Render(this);
        using var stream = File.Create(path);
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }

    // ----------------------------- 名单 -----------------------------

    private async void OnImportRoster(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenAsync("导入名单", new FilePickerFileType("名单文件")
        {
            Patterns = new[] { "*.xlsx", "*.xls", "*.csv" },
        });
        if (path is null) return;

        if (ViewModel.StudentCount > 0 && !await ConfirmDialog.ShowAsync(
                this, "替换当前名单？",
                $"当前已有 {ViewModel.StudentCount} 人名单与排座，导入新名单会整体替换，且无法撤销。",
                "替换名单", "取消", danger: true))
            return;

        ViewModel.IsBusy = true;
        try
        {
            var result = await Task.Run(() => RosterImporter.Import(path));
            ViewModel.LoadRoster(result);
            _rosterPath = path;
        }
        catch (Exception ex)
        {
            Report(ex, "导入名单失败");
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    // ----------------------------- 排座 / 座位操作 -----------------------------

    /// <summary>ArrangeModes 中「男女交替」的下标（= ArrangeMode.GenderAlternate）。</summary>
    private const int GenderAlternateIndex = 1;
    /// <summary>ArrangeModes 中「同性同桌」的下标（= ArrangeMode.GenderPair）。</summary>
    private const int GenderPairIndex = 2;

    private enum ArrangeGenderChoice { Opposite, Same, KeepCurrent, Cancel }

    private static readonly IBrush DialogPaper = new SolidColorBrush(Color.Parse("#FAF9F5"));
    private static readonly IBrush DialogCard = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush DialogLine = new SolidColorBrush(Color.Parse("#E7E3D8"));
    private static readonly IBrush DialogInk = new SolidColorBrush(Color.Parse("#141413"));
    private static readonly IBrush DialogInkSoft = new SolidColorBrush(Color.Parse("#3D3B36"));
    private static readonly IBrush DialogMuted = new SolidColorBrush(Color.Parse("#6F6A5E"));
    private static readonly IBrush DialogTerracotta = new SolidColorBrush(Color.Parse("#A34A36"));

    private async void OnAutoArrange(object? sender, RoutedEventArgs e)
    {
        var choice = await ShowArrangeGenderDialogAsync();
        switch (choice)
        {
            case ArrangeGenderChoice.Opposite:
                ViewModel.ArrangeModeIndex = GenderAlternateIndex;
                ViewModel.AutoArrange();
                break;
            case ArrangeGenderChoice.Same:
                ViewModel.ArrangeModeIndex = GenderPairIndex;
                ViewModel.AutoArrange();
                break;
            case ArrangeGenderChoice.KeepCurrent:
                ViewModel.AutoArrange();
                break;
        }
    }

    /// <summary>
    /// 自动排座先问同桌按异性还是同性安排；名单顺序 / 随机 / 按身高 / 按视力
    /// 仍保留下拉框入口，此时弹窗里补一条「按当前方式」直达，避免被弹窗堵死。
    /// </summary>
    private async Task<ArrangeGenderChoice> ShowArrangeGenderDialogAsync()
    {
        var dialog = new Window
        {
            Title = "自动排座",
            Width = 404,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = DialogPaper,
        };

        var result = ArrangeGenderChoice.Cancel;

        Button MakeButton(string text, ArrangeGenderChoice value,
            IBrush background, IBrush foreground, IBrush? border,
            bool isDefault = false, bool isCancel = false)
        {
            var button = new Button
            {
                Content = text,
                Padding = new Thickness(16, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = background,
                Foreground = foreground,
                BorderBrush = border,
                BorderThickness = border is null ? new Thickness(0) : new Thickness(1),
                IsDefault = isDefault,
                IsCancel = isCancel,
            };
            button.Click += (_, _) =>
            {
                result = value;
                dialog.Close();
            };
            return button;
        }

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "自动排座",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = DialogInk,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "同桌之间按哪种性别方式安排？重新排座会覆盖现有座位安排。",
            FontSize = 12.5,
            Foreground = DialogMuted,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
        });

        var options = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        options.Children.Add(MakeButton(
            "异性同桌 · 男女交替", ArrangeGenderChoice.Opposite, DialogInk, DialogPaper, null, isDefault: true));
        options.Children.Add(MakeButton(
            "同性同桌 · 男男 / 女女成对", ArrangeGenderChoice.Same, DialogCard, DialogTerracotta, DialogTerracotta));

        if (ViewModel.ArrangeModeIndex is not (GenderAlternateIndex or GenderPairIndex))
        {
            options.Children.Add(MakeButton(
                $"按当前方式「{ViewModel.ArrangeModes[ViewModel.ArrangeModeIndex]}」排座",
                ArrangeGenderChoice.KeepCurrent, DialogPaper, DialogMuted, DialogLine));
        }

        options.Children.Add(MakeButton(
            "取消", ArrangeGenderChoice.Cancel, DialogCard, DialogInkSoft, DialogLine, isCancel: true));
        panel.Children.Add(options);
        dialog.Content = panel;

        await dialog.ShowDialog(this);
        return result;
    }

    private void OnTeacherView(object? sender, RoutedEventArgs e) => ViewModel.IsStudentView = false;

    private void OnStudentView(object? sender, RoutedEventArgs e) => ViewModel.IsStudentView = true;

    private void OnSeatClick(object? sender, RoutedEventArgs e)
    {
        if (_suppressSeatClick)
        {
            _suppressSeatClick = false;
            return;
        }
        if ((sender as Button)?.Tag is SeatCellViewModel seat)
            ViewModel.SelectSeat(seat);
    }

    private void OnSeatPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_ghostFlying) FinalizeGhostFly(); // 飞行中被再次按下：先立刻到位提交，手感不留断点

        _suppressSeatClick = false;
        if (ViewModel.IsBusy) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 座位卡片与行列把手都是 Button；在窗口层按 Tag 分流拖拽模式。
        var button = (e.Source as Visual)?.FindAncestorOfType<Button>(true);
        switch (button?.Tag)
        {
            case SeatCellViewModel seat when !seat.IsEmpty:
                _dragMode = DragMode.Seat;
                _dragSource = seat;
                break;
            case RowViewModel row when button.Classes.Contains("line-handle"):
                _dragMode = DragMode.Row;
                _dragRow = row.Number - 1;
                break;
            case ColumnHandleViewModel column:
                _dragMode = DragMode.Column;
                _dragGroup = column.Group;
                _dragColumn = column.Column;
                break;
            default:
                return;
        }

        _dragStarted = false;
        _pressPoint = e.GetPosition(this);
        _dragVelocity.Begin();

        // 记录抓取偏移与源卡尺寸：幽灵跟着手感走，而不是永远居中吸附到指针。
        if (button is not null)
        {
            _grabOffset = e.GetPosition(button);
            _dragSourceSize = button.Bounds.Size;
        }
    }

    private void OnSeatPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragMode == DragMode.None || ViewModel.IsBusy) return;

        var position = e.GetPosition(this);
        _dragVelocity.Add(position, NowSeconds());
        if (!_dragStarted)
        {
            var dx = position.X - _pressPoint.X;
            var dy = position.Y - _pressPoint.Y;
            if (dx * dx + dy * dy < 100) return; // 10px 迟滞内仍视为点击（原 6px 易把点击误判为拖拽）
            BeginDrag();
        }

        UpdateDrag(position);
    }

    private void OnSeatPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragStarted)
        {
            _dragMode = DragMode.None;
            _dragSource = null;
            return; // 普通点击，交给 Click 处理
        }

        var mode = _dragMode;
        var sourceSeat = _dragSource;
        var sourceRow = _dragRow;
        var sourceGroup = _dragGroup;
        var sourceColumn = _dragColumn;
        var releasePoint = e.GetPosition(this);
        var velocity = _dragVelocity.Velocity(NowSeconds());
        var bounds = ChartBounds();

        // 保留幽灵与拖拽态快照，先结束跟踪（清高亮、复位拖拽字段）。
        EndDragTracking();
        _suppressSeatClick = true;

        switch (mode)
        {
            case DragMode.Row when ProjectedRowTarget(bounds, releasePoint, velocity) is int targetRow:
                if (targetRow != sourceRow) ViewModel.SwapRows(sourceRow, targetRow);
                else ViewModel.SetStatus("已取消整排调整。");
                break;
            case DragMode.Column when ProjectedColumnTarget(bounds, releasePoint, velocity) is (int group, int column):
                if (group != sourceGroup || column != sourceColumn)
                    ViewModel.SwapColumns(sourceGroup, sourceColumn, group, column);
                else ViewModel.SetStatus("已取消整列调整。");
                break;
            case DragMode.Seat:
                BeginSeatDropFlight(sourceSeat, releasePoint, velocity, bounds);
                break;
            default:
                ViewModel.SetStatus(mode switch
                {
                    DragMode.Row => "已取消整排调整。",
                    DragMode.Column => "已取消整列调整。",
                    _ => "已取消移动。",
                });
                break;
        }
    }

    private void OnSeatPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => CancelDrag();

    private void OnLayoutSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsSliderSource(e.Source)) ViewModel.BeginInteractiveLayoutChange();
    }

    private void OnLayoutSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (IsSliderSource(e.Source)) ViewModel.EndInteractiveLayoutChange();
    }

    private void OnLayoutSliderCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        ViewModel.EndInteractiveLayoutChange();

    private static bool IsSliderSource(object? source)
    {
        var visual = source as Visual;
        while (visual is not null)
        {
            if (visual is Slider) return true;
            visual = visual.GetVisualParent();
        }

        return false;
    }

    private void BeginDrag()
    {
        _dragStarted = true;
        if (_dragMode != DragMode.Seat) return; // 行列拖拽不显示浮层，靠高亮目标线反馈

        var source = _dragSource!;
        var ghostWidth = _dragSourceSize.Width > 1 ? _dragSourceSize.Width : 92;
        var ghostHeight = _dragSourceSize.Height > 1 ? _dragSourceSize.Height : 56;
        _dragGhost = new Border
        {
            Width = ghostWidth,
            Height = ghostHeight,
            Background = source.Fill,
            BorderBrush = new SolidColorBrush(Color.Parse("#141413")),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Opacity = 0.94,
            IsHitTestVisible = false,
            RenderTransform = new ScaleTransform(1.04, 1.04),
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Child = new TextBlock
            {
                Text = source.DisplayName,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = source.NameBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0),
            },
        };
        DragLayer.Children.Add(_dragGhost);
    }

    private void UpdateDrag(Point position)
    {
        if (_dragMode == DragMode.Seat)
        {
            if (_dragGhost is { } ghost)
            {
                // 界内「抓哪儿是哪儿」；拖出网格后幽灵中心受橡皮筋阻力，暗示落不出去。
                _flyCurrent = RubberbandedGhostTopLeft(position, ChartBounds());
                Canvas.SetLeft(ghost, _flyCurrent.X);
                Canvas.SetTop(ghost, _flyCurrent.Y);
            }

            var seat = FindSeatUnder(this.InputHitTest(position) as Visual);
            if (!ReferenceEquals(seat, _dropTarget))
            {
                _dropTarget?.SetDropTarget(false);
                _dropTarget = seat;
                _dropTarget?.SetDropTarget(true);
            }
            return;
        }

        // 行列拖拽：高亮指针下的整排 / 整列
        var button = (this.InputHitTest(position) as Visual)?.FindAncestorOfType<Button>(true);
        int? hoverRow = null;
        int? hoverGroup = null;
        int? hoverColumn = null;

        if (_dragMode == DragMode.Row)
        {
            if (button?.Tag is SeatCellViewModel seat) hoverRow = seat.Position.Row;
            else if (button?.Tag is RowViewModel row) hoverRow = row.Number - 1;
        }
        else if (_dragMode == DragMode.Column)
        {
            if (button?.Tag is SeatCellViewModel seatColumn)
            {
                hoverGroup = seatColumn.Position.Group;
                hoverColumn = seatColumn.Position.Column;
            }
            else if (button?.Tag is ColumnHandleViewModel handle)
            {
                hoverGroup = handle.Group;
                hoverColumn = handle.Column;
            }
        }

        var hover = (hoverRow, hoverGroup, hoverColumn);
        if (hover == _hoverLine) return;

        ClearLineHighlight();
        if (hoverRow is int targetRow)
            HighlightSeats(seat => seat.Position.Row == targetRow);
        else if (hoverGroup is int group && hoverColumn is int column)
            HighlightSeats(seat => seat.Position.Group == group && seat.Position.Column == column);
        _hoverLine = hover;
    }

    private SeatCellViewModel? FindSeatUnder(Visual? visual)
    {
        var seat = FindSeatFromSource(visual);
        return ReferenceEquals(seat, _dragSource) ? null : seat;
    }

    /// <summary>指针事件源可能落在座位卡片内的 TextBlock 上，向上找到座位 Button 再取 Tag。</summary>
    private static SeatCellViewModel? FindSeatFromSource(object? source) =>
        (source as Visual)?.FindAncestorOfType<Button>(true)?.Tag as SeatCellViewModel;

    // ----------------------------- 落点投影与幽灵飞行（P1-A） -----------------------------

    /// <summary>座位表网格在窗口坐标系里的范围，投影与橡皮筋都以它为界。</summary>
    private Rect ChartBounds()
    {
        if (SeatChartHost is not { } host) return default;
        if (host.TranslatePoint(new Point(0, 0), this) is not Point origin) return default;
        return new Rect(origin, host.Bounds.Size);
    }

    /// <summary>投影后的有效落点：界内释放沿速度滑行，滑出边界则被橡皮筋压回。</summary>
    private static Point EffectiveDropPoint(Rect bounds, Point releasePoint, Vector velocity)
    {
        var projected = DragPhysics.ProjectedPoint(releasePoint, velocity);
        return bounds.Contains(projected) ? projected : DragPhysics.RubberbandPoint(projected, bounds);
    }

    /// <summary>拖出网格才有的阻力：幽灵中心被橡皮筋压回边界，左上角随之收拢。</summary>
    private Point RubberbandedGhostTopLeft(Point position, Rect bounds)
    {
        var raw = new Point(position.X - _grabOffset.X, position.Y - _grabOffset.Y);
        if (bounds.Width <= 0 || bounds.Height <= 0) return raw;

        var half = new Vector(_dragSourceSize.Width / 2, _dragSourceSize.Height / 2);
        var center = raw + half;
        var clamped = DragPhysics.RubberbandPoint(center, bounds);
        return clamped == center ? raw : clamped - half;
    }

    /// <summary>距离给定点最近的可见座位及其中心（窗口坐标），找不到返回 null。</summary>
    private (SeatCellViewModel Seat, Point Center)? NearestSeatTo(Point windowPoint)
    {
        SeatCellViewModel? nearest = null;
        var nearestCenter = default(Point);
        var best = double.MaxValue;

        foreach (var button in this.GetVisualDescendants().OfType<Button>())
        {
            if (button.Tag is not SeatCellViewModel seat) continue;
            if (!button.IsVisible || button.Bounds.Width < 1 || button.Bounds.Height < 1) continue;
            if (button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), this) is not Point center) continue;

            var dx = center.X - windowPoint.X;
            var dy = center.Y - windowPoint.Y;
            var distance = dx * dx + dy * dy;
            if (distance >= best) continue;

            best = distance;
            nearest = seat;
            nearestCenter = center;
        }

        return nearest is null ? null : (nearest, nearestCenter);
    }

    /// <summary>找指定座位卡片的中心（窗口坐标），用于取消时让幽灵飞回原位。</summary>
    private Point? SeatCenter(SeatCellViewModel seat)
    {
        foreach (var button in this.GetVisualDescendants().OfType<Button>())
        {
            if (!ReferenceEquals(button.Tag, seat)) continue;
            if (button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), this) is Point center)
                return center;
        }

        return null;
    }

    /// <summary>整排拖拽的投影落点：指针出界即取消，界内取投影点最近座位所在排。</summary>
    private int? ProjectedRowTarget(Rect bounds, Point releasePoint, Vector velocity)
    {
        if (!bounds.Contains(releasePoint)) return null;
        return NearestSeatTo(EffectiveDropPoint(bounds, releasePoint, velocity))?.Seat.Position.Row;
    }

    /// <summary>整列拖拽的投影落点：同上，取投影点最近座位所在的组与列。</summary>
    private (int Group, int Column)? ProjectedColumnTarget(Rect bounds, Point releasePoint, Vector velocity)
    {
        if (!bounds.Contains(releasePoint)) return null;
        var seat = NearestSeatTo(EffectiveDropPoint(bounds, releasePoint, velocity))?.Seat;
        return seat is null ? null : (seat.Position.Group, seat.Position.Column);
    }

    /// <summary>
    /// 座位卡松手：按投影落点决定去处，幽灵以 120–200ms 临界阻尼飞向目标格中心，
    /// 到位后与数据提交同帧完成，卡片不再凭空消失。拖出网格或落回原座位则取消。
    /// </summary>
    private void BeginSeatDropFlight(SeatCellViewModel? source, Point releasePoint, Vector velocity, Rect bounds)
    {
        if (source is null)
        {
            EndDragVisuals();
            return;
        }

        var target = bounds.Contains(releasePoint)
            ? NearestSeatTo(EffectiveDropPoint(bounds, releasePoint, velocity))
            : null;

        if (target is null || ReferenceEquals(target.Value.Seat, source))
        {
            // 拖出网格（拖出即取消的既有约定）或落回原座位：幽灵飞回原位后取消。
            var sourceCenter = SeatCenter(source);
            if (sourceCenter is Point center)
            {
                BeginGhostFly(center, velocity.Length, () => ViewModel.SetStatus("已取消移动。"));
                return;
            }

            EndDragVisuals();
            ViewModel.SetStatus("已取消移动。");
            return;
        }

        BeginGhostFly(
            target.Value.Center,
            velocity.Length,
            () => ViewModel.MoveSeatStudent(source, target.Value.Seat));
    }

    /// <summary>启动幽灵飞行：速度越快时长越短（快甩 120ms，慢放 200ms）。</summary>
    private void BeginGhostFly(Point targetCenter, double speed, Action commit)
    {
        if (_dragGhost is null)
        {
            commit();
            return;
        }

        _flyCommit = commit;
        _flyTarget = targetCenter - new Vector(_dragSourceSize.Width / 2, _dragSourceSize.Height / 2);
        _flyDuration = TimeSpan.FromMilliseconds(200 - 80 * Math.Clamp(speed / 1500.0, 0, 1));
        _ghostFlying = true;

        if (_flyTimer is null)
        {
            _flyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _flyTimer.Tick += OnGhostFlyTick;
            _flyClock = Stopwatch.StartNew();
        }

        _flyLastTime = 0;
        _flyTimer.Start();
    }

    private void OnGhostFlyTick(object? sender, EventArgs e)
    {
        if (_dragGhost is null)
        {
            StopGhostFly();
            return;
        }

        var elapsed = _flyClock!.Elapsed;
        var dt = Math.Clamp(elapsed.TotalSeconds - _flyLastTime, 0.001, 0.05);
        _flyLastTime = elapsed.TotalSeconds;

        // 指数趋近 ≈ 临界阻尼（不过冲）：tau 取时长的 1/4.5，到位即收。
        var tau = _flyDuration.TotalSeconds / 4.5;
        var remaining = new Vector(_flyTarget.X - _flyCurrent.X, _flyTarget.Y - _flyCurrent.Y);
        var step = remaining * (1 - Math.Exp(-dt / tau));
        _flyCurrent = new Point(_flyCurrent.X + step.X, _flyCurrent.Y + step.Y);
        Canvas.SetLeft(_dragGhost, _flyCurrent.X);
        Canvas.SetTop(_dragGhost, _flyCurrent.Y);

        if (Math.Sqrt(remaining.X * remaining.X + remaining.Y * remaining.Y) > 0.7 &&
            elapsed < _flyDuration) return;
        FinalizeGhostFly();
    }

    /// <summary>飞行收尾：移除幽灵与数据提交在同一 UI pass 内，不留一帧空白。</summary>
    private void FinalizeGhostFly()
    {
        StopGhostFly();
        var commit = _flyCommit;
        _flyCommit = null;
        EndDragVisuals();
        commit?.Invoke();
    }

    private void StopGhostFly()
    {
        _flyTimer?.Stop();
        _ghostFlying = false;
    }

    /// <summary>结束拖拽跟踪（高亮与拖拽字段），幽灵与飞行动画保留给落点提交。</summary>
    private void EndDragTracking()
    {
        _dropTarget?.SetDropTarget(false);
        _dropTarget = null;
        ClearLineHighlight();
        _hoverLine = (null, null, null);
        _dragMode = DragMode.None;
        _dragSource = null;
        _dragStarted = false;
    }

    private void EndDragVisuals()
    {
        EndDragTracking();
        DragLayer.Children.Clear();
        _dragGhost = null;
        _flyCommit = null;
        StopGhostFly();
    }

    private void ClearLineHighlight()
    {
        foreach (var seat in _lineHighlight)
            seat.SetDropTarget(false);
        _lineHighlight.Clear();
    }

    private void HighlightSeats(Func<SeatCellViewModel, bool> predicate)
    {
        foreach (var seat in ViewModel.Seats)
        {
            if (!predicate(seat)) continue;
            seat.SetDropTarget(true);
            _lineHighlight.Add(seat);
        }
    }

    private void CancelDrag()
    {
        if (!_dragStarted) return;
        EndDragVisuals();
        _dragMode = DragMode.None;
        _dragSource = null;
        _dragStarted = false;
    }

    private void OnAssign(object? sender, RoutedEventArgs e) => ViewModel.AssignSelectedStudent();

    private async void OnClearSeat(object? sender, RoutedEventArgs e)
    {
        var name = ViewModel.SelectedStudent?.Name;
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!await ConfirmDialog.ShowAsync(
                this, "清空这个座位？",
                $"将清空「{name}」的座位，学生仍保留在名单中。",
                "清空座位", "取消", danger: true))
            return;
        ViewModel.ClearSelectedSeat();
    }

    private void OnApplyStudent(object? sender, RoutedEventArgs e) => ViewModel.ApplyStudentEdit();

    private void OnAddStudent(object? sender, RoutedEventArgs e) => ViewModel.AddStudent();

    private async void OnDeleteStudent(object? sender, RoutedEventArgs e)
    {
        await DeleteSelectedStudentAsync();
    }

    private async Task DeleteSelectedStudentAsync()
    {
        var name = ViewModel.SelectedStudent?.Name;
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!await ConfirmDialog.ShowAsync(
                this, "从名单中删除？",
                $"将移除「{name}」并清空其座位，此操作无法撤销。",
                "删除学生", "取消", danger: true))
            return;
        ViewModel.DeleteSelectedStudent();
    }

    /// <summary>窗口级键盘：Esc 取消拖拽，Delete 删除选中学生（走统一确认）。</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_dragStarted)
            {
                CancelDrag();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Delete && ViewModel.SelectedStudent is not null)
        {
            _ = DeleteSelectedStudentAsync();
            e.Handled = true;
        }
    }

    private void OnResetStyle(object? sender, RoutedEventArgs e) => ViewModel.ResetStyle();

    private void OnShiftLeft(object? sender, RoutedEventArgs e) => ViewModel.ShiftAllSeats(-1, 0);

    private void OnShiftRight(object? sender, RoutedEventArgs e) => ViewModel.ShiftAllSeats(1, 0);

    private void OnShiftUp(object? sender, RoutedEventArgs e) => ViewModel.ShiftAllSeats(0, -1);

    private void OnShiftDown(object? sender, RoutedEventArgs e) => ViewModel.ShiftAllSeats(0, 1);

    // ----------------------------- 预览 / 打印 / 导出 -----------------------------

    private async void OnPreview(object? sender, RoutedEventArgs e)
    {
        ViewModel.IsBusy = true;
        try
        {
            var document = ViewModel.ComposeDocument();
            var path = await Task.Run(() => SeatChartRenderer.RenderPngToTemp(document, 2.0));
            var preview = new SeatPreviewWindow(path, ViewModel.Title, ViewModel.SeatStats);
            await preview.ShowDialog(this);
            ViewModel.SetStatus("预览已生成，导出图片会得到同一张图。");
        }
        catch (Exception ex)
        {
            Report(ex, "生成预览失败");
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    private async void OnPrint(object? sender, RoutedEventArgs e)
    {
        ViewModel.IsBusy = true;
        try
        {
            var document = ViewModel.ComposeDocument();
            var directory = Path.Combine(Path.GetTempPath(), "TeachCraft", "SeatGrid");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, SafeFileName("-打印.pdf"));
            await Task.Run(() => SeatChartRenderer.RenderPdf(document, path));

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            ViewModel.SetStatus("已生成打印 PDF，请在打开的阅读器里打印。");
        }
        catch (Exception ex)
        {
            Report(ex, "生成打印文件失败");
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    private async void OnExportImage(object? sender, RoutedEventArgs e)
    {
        var path = await PickSaveAsync("导出座位表图片", SafeFileName(".png"), ".png",
            new FilePickerFileType("PNG 图片") { Patterns = new[] { "*.png" } });
        if (path is null) return;
        if (IsSamePath(path, _rosterPath))
        {
            ViewModel.SetStatus("导出路径不能覆盖已导入的名单文件。", true);
            return;
        }

        ViewModel.IsBusy = true;
        try
        {
            var document = ViewModel.ComposeDocument();
            await Task.Run(() => SeatChartRenderer.RenderPng(document, path, 2.0));
            ViewModel.SetStatus($"已导出图片：{Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Report(ex, "导出图片失败");
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    private async void OnExportExcel(object? sender, RoutedEventArgs e)
    {
        var path = await PickSaveAsync("导出座位表 Excel", SafeFileName(".xlsx"), ".xlsx",
            new FilePickerFileType("Excel 工作簿") { Patterns = new[] { "*.xlsx" } });
        if (path is null) return;
        if (IsSamePath(path, _rosterPath))
        {
            ViewModel.SetStatus("导出路径不能覆盖已导入的名单文件。", true);
            return;
        }

        ViewModel.IsBusy = true;
        try
        {
            var document = ViewModel.ComposeDocument();
            var assignments = ViewModel.GetCoreAssignments();
            var students = ViewModel.GetModels();
            await Task.Run(() => SeatChartExcelExporter.ExportSeatChart(document, assignments, students, path));
            ViewModel.SetStatus($"已导出 Excel：{Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Report(ex, "导出 Excel 失败");
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    private async void OnExportConfig(object? sender, RoutedEventArgs e)
    {
        var path = await PickSaveAsync("导出座位配置", SafeFileName("-配置.xlsx"), ".xlsx",
            new FilePickerFileType("Excel 工作簿") { Patterns = new[] { "*.xlsx" } });
        if (path is null) return;
        if (IsSamePath(path, _rosterPath))
        {
            ViewModel.SetStatus("配置路径不能覆盖已导入的名单文件。", true);
            return;
        }

        ViewModel.IsBusy = true;
        try
        {
            var settings = ViewModel.ToSettings();
            var assignments = ViewModel.GetCoreAssignments();
            var students = ViewModel.GetModels();
            await Task.Run(() => SeatGridConfigIO.Export(path, settings, students, assignments));
            ViewModel.SetStatus($"已导出配置：{Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Report(ex, "导出配置失败");
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    private async void OnImportConfig(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenAsync("导入座位配置", new FilePickerFileType("座位配置")
        {
            Patterns = new[] { "*.xlsx" },
        });
        if (path is null) return;

        if (ViewModel.StudentCount > 0 && !await ConfirmDialog.ShowAsync(
                this, "导入配置会替换当前排座",
                $"当前名单（{ViewModel.StudentCount} 人）与排座将被配置里的内容整体覆盖，且无法撤销。",
                "导入并覆盖", "取消", danger: true))
            return;

        ViewModel.IsBusy = true;
        try
        {
            var config = await Task.Run(() => SeatGridConfigIO.Import(path));
            ViewModel.ApplyConfig(config);
            _rosterPath = null;
        }
        catch (Exception ex)
        {
            Report(ex, "导入配置失败");
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    // ----------------------------- 文件选择 -----------------------------

    private async Task<string?> PickOpenAsync(string title, FilePickerFileType fileType)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { fileType },
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    private async Task<string?> PickSaveAsync(
        string title,
        string suggestedName,
        string extension,
        FilePickerFileType fileType)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = new[] { fileType },
            ShowOverwritePrompt = true,
        });

        return file?.TryGetLocalPath();
    }

    private string SafeFileName(string suffix)
    {
        var title = string.IsNullOrWhiteSpace(ViewModel.Title) ? "座位表" : ViewModel.Title.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            title = title.Replace(invalid, '-');

        return title + suffix;
    }

    private static bool IsSamePath(string path, string? other)
    {
        if (string.IsNullOrWhiteSpace(other)) return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(path),
                Path.GetFullPath(other),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void Report(Exception ex, string prefix)
    {
        ViewModel.SetStatus(ex is SeatGridException ? ex.Message : $"{prefix}：{ex.Message}", true);
    }
}
