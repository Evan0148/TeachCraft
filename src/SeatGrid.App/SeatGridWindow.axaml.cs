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
using Avalonia.VisualTree;
using SeatGrid.Core;
using SeatGrid.Core.Export;
using SeatGrid.Core.Rendering;
using SeatGrid.Core.Roster;

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
    private readonly List<SeatCellViewModel> _lineHighlight = new();

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
    }

    private SeatGridViewModel ViewModel => (SeatGridViewModel)DataContext!;

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

    private void OnAutoArrange(object? sender, RoutedEventArgs e) => ViewModel.AutoArrange();

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
    }

    private void OnSeatPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragMode == DragMode.None || ViewModel.IsBusy) return;

        var position = e.GetPosition(this);
        if (!_dragStarted)
        {
            var dx = position.X - _pressPoint.X;
            var dy = position.Y - _pressPoint.Y;
            if (dx * dx + dy * dy < 36) return; // 6px 内仍视为点击
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
        var targetSeat = _dropTarget;
        var sourceRow = _dragRow;
        var sourceGroup = _dragGroup;
        var sourceColumn = _dragColumn;
        var hover = _hoverLine;

        EndDragVisuals();
        _dragMode = DragMode.None;
        _dragSource = null;
        _dragStarted = false;
        _suppressSeatClick = true;

        switch (mode)
        {
            case DragMode.Row when hover.Row is int targetRow && targetRow != sourceRow:
                ViewModel.SwapRows(sourceRow, targetRow);
                break;
            case DragMode.Column when hover.Group is int group && hover.Column is int column:
                ViewModel.SwapColumns(sourceGroup, sourceColumn, group, column);
                break;
            case DragMode.Seat:
                if (sourceSeat is not null && targetSeat is not null)
                    ViewModel.MoveSeatStudent(sourceSeat, targetSeat);
                else
                    ViewModel.SetStatus("已取消移动。");
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
        _dragGhost = new Border
        {
            Width = 92,
            Height = 56,
            Background = source.Fill,
            BorderBrush = new SolidColorBrush(Color.Parse("#141413")),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Opacity = 0.94,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = source.DisplayName,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = source.NameBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
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
                Canvas.SetLeft(ghost, position.X - ghost.Width / 2);
                Canvas.SetTop(ghost, position.Y - ghost.Height / 2);
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

    private void EndDragVisuals()
    {
        _dropTarget?.SetDropTarget(false);
        _dropTarget = null;
        ClearLineHighlight();
        _hoverLine = (null, null, null);
        DragLayer.Children.Clear();
        _dragGhost = null;
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

    private void OnClearSeat(object? sender, RoutedEventArgs e) => ViewModel.ClearSelectedSeat();

    private void OnApplyStudent(object? sender, RoutedEventArgs e) => ViewModel.ApplyStudentEdit();

    private void OnAddStudent(object? sender, RoutedEventArgs e) => ViewModel.AddStudent();

    private void OnDeleteStudent(object? sender, RoutedEventArgs e) => ViewModel.DeleteSelectedStudent();

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
