using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GradeWeave.Core;
using GradeWeave.Core.Export;
using GradeWeave.Core.Import;
using GradeWeave.Core.Models;
using GradeWeave.Core.Project;
using GradeWeave.Core.Scheduling;
using GradeWeave.Core.Timetable;
using TeachCraft.Shared;

namespace GradeWeave.App;

public partial class GradeWeaveWindow : Window
{
    private static readonly IBrush CardBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush PaperDeepBrush = new SolidColorBrush(Color.Parse("#F4F2EC"));
    private static readonly IBrush PinnedBrush = new SolidColorBrush(Color.Parse("#F7F4EB"));
    private static readonly IBrush LineBrush = new SolidColorBrush(Color.Parse("#E7E3D8"));
    private static readonly IBrush InkBrush = new SolidColorBrush(Color.Parse("#141413"));
    private static readonly IBrush InkSoftBrush = new SolidColorBrush(Color.Parse("#3D3B36"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#6F6A5E"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#D97757"));
    private static readonly IBrush AccentDeepBrush = new SolidColorBrush(Color.Parse("#C6613F"));
    private static readonly IBrush AccentFill = new SolidColorBrush(Color.Parse("#33D97757"));
    private static readonly IBrush AccentSoftFill = new SolidColorBrush(Color.Parse("#1FD97757"));
    private static readonly FontFamily MonoFamily = new("Consolas, Menlo, monospace");

    private enum DragState { None, Candidate, Dragging }

    private sealed record ScheduleCellTag(string ClassId, int DayIndex, int Period);

    /// <summary>一次网格刷新内复用的只读索引，避免每格线性扫描整份工程。</summary>
    private sealed class ScheduleIndex
    {
        public Dictionary<(string ClassId, int Day, int Period), List<LessonPlacement>> ClassLessons { get; } = [];
        public Dictionary<(string TeacherId, int Day, int Period), LessonPlacement> TeacherLessons { get; } = [];
        public Dictionary<(string ClassId, int Day, int Period), LayeredClassGroup> LayeredByClass { get; } = [];
        public Dictionary<(string TeacherId, int Day, int Period), LayeredClassGroup> LayeredByTeacher { get; } = [];
        public Dictionary<(string ClassId, int Day, int Period), List<ActivityBlock>> Overlays { get; } = [];
        public HashSet<(int Day, int Period, string ClassId)> ConflictClasses { get; } = [];
        public HashSet<(int Day, int Period, string TeacherId)> ConflictTeachers { get; } = [];
        public Dictionary<string, SchoolClass> Classes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Subject> Subjects { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Teacher> Teachers { get; } = new(StringComparer.Ordinal);
    }

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private readonly Dictionary<(string ClassId, int Day, int Period), (Border Border, TextBlock Text)>
        _masterCells = new();
    private string _masterGridSignature = string.Empty;

    private DragState _dragState;
    private ScheduleCellTag? _dragSource;
    private ScheduleCellTag? _pendingSelectTag;
    private Border? _pendingSelectBorder;
    private ScheduleCellTag? _dropTargetTag;
    private Border? _dropTargetBorder;
    private IBrush? _dropTargetBrush;
    private Thickness _dropTargetThickness;
    private ScheduleCellTag? _selectedTag;
    private Border? _selectedBorder;
    private IBrush? _selectedBrush;
    private Thickness _selectedThickness;
    private Point _pressPoint;
    private Point _grabOffset;          // 按下点相对源格左上角的偏移
    private Size _dragSourceSize;       // 源格实际尺寸，幽灵不再用固定 112×58
    private Border? _dragGhost;
    private readonly VelocitySampler _dragVelocity = new();

    // 松手后的幽灵飞行：120–200ms 临界阻尼飞向落点格中心，到位后同一帧提交数据。
    private bool _ghostFlying;
    private Point _flyCurrent;          // 幽灵当前左上角（DragLayer 层坐标）
    private Point _flyTarget;           // 幽灵目标左上角（DragLayer 层坐标）
    private TimeSpan _flyDuration;
    private double _flyLastTime;
    private DispatcherTimer? _flyTimer;
    private Stopwatch? _flyClock;
    private Action? _flyCommit;

    private static double NowSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private bool _suppressClick;
    private bool _lessonEditorOpen;
    private ConflictCenterWindow? _conflictCenter;

    private readonly WeaveAutosaveStore _autosaveStore = new();
    private readonly DispatcherTimer _autosaveTimer;
    private string? _currentProjectPath;
    private string _lastSavedJson = string.Empty;
    private int _autosaveVersion;
    private string _autosaveSessionId = Guid.NewGuid().ToString("N");
    private bool _autosaveDirty;
    private bool _autosaveWriting;
    private bool _isClosing;
    private bool _closeReady;

    public GradeWeaveWindow()
    {
        InitializeComponent();
        DataContext ??= new GradeWeaveViewModel();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.ProjectChanged += OnProjectChanged;
        ViewModel.ResearchGridInvalidated += OnResearchGridInvalidated;
        ViewModel.LayeredPreviewInvalidated += OnLayeredPreviewInvalidated;
        ViewModel.ScheduleInvalidated += OnScheduleInvalidated;

        AddHandler(PointerPressedEvent, OnGridPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnGridPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnGridPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnGridPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autosaveTimer.Tick += OnAutosaveTick;
        _autosaveTimer.Start();
        ViewModel.SetAutosaveStatus("自动保存待命", "工程改动会每 2 秒自动保存到本机。", false, false);
        RefreshRecoveryState();

        BuildCurrentStep();

        Opened += (_, _) => WindowFit.Clamp(this);
    }

    private GradeWeaveViewModel ViewModel => (GradeWeaveViewModel)DataContext!;

    // ----------------------------- 宿主入口 -----------------------------

    /// <summary>宿主可直接注入工程，窗口会立即刷新五步数据。</summary>
    public void LoadProject(WeaveProject project)
    {
        StartAutosaveSession(null, clearRecovery: true);
        ViewModel.LoadProject(project);
    }

    /// <summary>演示与截图入口：合成数据，不含任何真实学校或教师信息。</summary>
    public void LoadDemoProject()
    {
        StartAutosaveSession(null, clearRecovery: true);
        ViewModel.LoadDemoProject();
    }

    /// <summary>设计走查 / 命令行入口：读取 xlsx、xls 或 weave.json，不弹文件选择器。</summary>
    public async Task LoadProjectFromFileAsync(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
        {
            StartAutosaveSession(path, clearRecovery: true);
            ViewModel.LoadProject(await Task.Run(() => WeaveProjectIO.Load(path)));
            ViewModel.SetStatus("已打开织序工程。");
            return;
        }

        await ImportProjectAsync(path);
    }

    /// <summary>设计走查：切到指定步骤（1–5）。</summary>
    public void SetStepForCapture(int step)
    {
        ViewModel.ActiveStep = Math.Clamp(step, 1, 5);
        BuildCurrentStep();
    }

    /// <summary>设计走查：在演示工程上直接生成课表。</summary>
    public Task GenerateForCaptureAsync() => ViewModel.GenerateAsync();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeReady && ViewModel.IsBusy)
        {
            // 生成 / 导入在跑：此时序列化会读到半成品，宁可拦一次也不静默丢盘。
            e.Cancel = true;
            ViewModel.SetStatus("正在处理中，完成后再关闭窗口。");
            return;
        }

        if (!_closeReady)
        {
            _isClosing = true;
            _autosaveTimer.Stop();
            FlushAutosaveSync();
            _closeReady = true;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _autosaveTimer.Stop();

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.ProjectChanged -= OnProjectChanged;
        ViewModel.ResearchGridInvalidated -= OnResearchGridInvalidated;
        ViewModel.LayeredPreviewInvalidated -= OnLayeredPreviewInvalidated;
        ViewModel.ScheduleInvalidated -= OnScheduleInvalidated;

        _conflictCenter?.Close();
        _conflictCenter = null;
        ScheduleDragLayer.Children.Clear();
        _dragGhost = null;
        base.OnClosed(e);
    }

    // ----------------------------- 刷新 -----------------------------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GradeWeaveViewModel.ActiveStep))
            BuildCurrentStep();
    }

    private void BuildCurrentStep()
    {
        if (ViewModel.ActiveStep == 2) BuildResearchGrid();
        else if (ViewModel.ActiveStep == 3) BuildLayeredPreviewGrid();
        else if (ViewModel.ActiveStep == 5) BuildScheduleGrid();
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        MarkProjectDirty();
        BuildCurrentStep();
    }

    private void OnResearchGridInvalidated(object? sender, EventArgs e)
    {
        MarkProjectDirty();
        if (ViewModel.ActiveStep == 2) BuildResearchGrid();
    }

    private void OnLayeredPreviewInvalidated(object? sender, EventArgs e)
    {
        MarkProjectDirty();
        if (ViewModel.ActiveStep == 3) BuildLayeredPreviewGrid();
    }

    private void OnScheduleInvalidated(object? sender, EventArgs e)
    {
        MarkProjectDirty();
        if (ViewModel.ActiveStep == 5) BuildScheduleGrid();
        if (_conflictCenter is { IsVisible: true }) _conflictCenter.RefreshConflicts();
    }

    // ----------------------------- 自动保存 -----------------------------

    private void OnAutosaveTick(object? sender, EventArgs e) => _ = AutosaveTickAsync();

    private void MarkProjectDirty()
    {
        if (!ViewModel.HasProject) return;
        _autosaveDirty = true;
        _autosaveVersion++;
    }

    /// <summary>开始一份新工程会话：记录工程文件路径，清掉上一份工程的恢复文件。</summary>
    private void StartAutosaveSession(string? projectPath, bool clearRecovery)
    {
        _currentProjectPath = string.IsNullOrWhiteSpace(projectPath)
            ? null
            : Path.GetFullPath(projectPath);
        _lastSavedJson = string.Empty;
        _autosaveDirty = true;
        _autosaveVersion++;
        _autosaveSessionId = Guid.NewGuid().ToString("N");
        _autosaveStore.BeginSession(_autosaveSessionId);

        if (clearRecovery) _autosaveStore.Clear();
        RefreshRecoveryState();
        ViewModel.SetAutosaveStatus(
            "自动保存已启用",
            _currentProjectPath is null
                ? "没有工程文件路径，改动会保存到恢复文件。"
                : $"改动会原子写回：{_currentProjectPath}",
            false,
            false);
    }

    private void RefreshRecoveryState() => ViewModel.SetRecoveryState(_autosaveStore.TryGetInfo());

    private async Task AutosaveTickAsync()
    {
        if (_isClosing || _autosaveWriting || !ViewModel.HasProject || ViewModel.IsBusy) return;
        if (!_autosaveDirty) return;

        var version = _autosaveVersion;
        var projectPath = _currentProjectPath;
        WeaveProject snapshot;
        try
        {
            // 只在 UI 线程复制集合，JSON 序列化与元数据组装放到后台，避免每 2 秒卡住编辑。
            snapshot = ViewModel.Project.Snapshot();
        }
        catch (Exception ex)
        {
            ViewModel.SetAutosaveStatus("自动保存失败", ErrorText.Describe(ex, "保存工程"), true, false);
            return;
        }

        _autosaveWriting = true;
        try
        {
            var (json, info) = await Task.Run(() => (
                WeaveProjectIO.Serialize(snapshot),
                WeaveAutosaveInfo.FromProject(snapshot, projectPath)));

            if (_isClosing) return;

            if (string.Equals(json, _lastSavedJson, StringComparison.Ordinal))
            {
                if (_autosaveVersion == version) _autosaveDirty = false;
                return;
            }

            var result = await Task.Run(() =>
                _autosaveStore.SaveJson(json, projectPath, info, version, _autosaveSessionId));
            if (_isClosing) return;
            if (result.Skipped) return;
            if (!result.Success)
            {
                ViewModel.SetAutosaveStatus(
                    "自动保存失败", result.Warning ?? "请手动保存工程。", true, false);
                return;
            }

            _lastSavedJson = json;
            if (_autosaveVersion == version) _autosaveDirty = false;

            var target = result.Target == WeaveAutosaveTarget.ProjectFile ? "当前工程" : "恢复文件";
            var text = result.Target == WeaveAutosaveTarget.ProjectFile
                ? $"自动保存 · {DateTime.Now:HH:mm:ss}"
                : $"自动保存 · {DateTime.Now:HH:mm:ss}（恢复）";
            ViewModel.SetAutosaveStatus(
                text,
                result.Warning ?? $"已保存到{target}：{result.Path}",
                false,
                result.Warning is null);
        }
        catch (Exception ex)
        {
            if (!_isClosing) ViewModel.SetAutosaveStatus("自动保存失败", ErrorText.Describe(ex, "保存工程"), true, false);
        }
        finally
        {
            _autosaveWriting = false;
        }
    }

    /// <summary>关闭窗口前同步落一次盘，避免最后几秒的改动丢失。</summary>
    private void FlushAutosaveSync()
    {
        if (!ViewModel.HasProject || ViewModel.IsBusy) return;

        try
        {
            var json = WeaveProjectIO.Serialize(ViewModel.Project);
            if (string.Equals(json, _lastSavedJson, StringComparison.Ordinal)) return;

            var info = WeaveAutosaveInfo.FromProject(ViewModel.Project, _currentProjectPath);
            var result = _autosaveStore.SaveJson(
                json, _currentProjectPath, info, _autosaveVersion, _autosaveSessionId);
            if (!result.Success || result.Skipped) return;

            _lastSavedJson = json;
            _autosaveDirty = false;
        }
        catch
        {
            // 关闭阶段不再弹错误；定时自动保存和手动保存仍然可用。
        }
    }

    private void OnRestoreAutosave(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy) return;

        if (!_autosaveStore.TryLoad(out var project, out var info) || project is null || info is null)
        {
            ViewModel.SetStatus("自动保存恢复文件已损坏或不存在，请手动打开工程。", true);
            RefreshRecoveryState();
            return;
        }

        StartAutosaveSession(null, clearRecovery: false);
        ViewModel.LoadProject(project);
        ViewModel.SetStatus($"已恢复上次自动保存（{info.SavedAt:MM-dd HH:mm}）。");
        MarkProjectDirty();
    }

    // ----------------------------- 01 教研网格 -----------------------------

    private void BuildResearchGrid()
    {
        var host = ResearchGridHost;
        host.Children.Clear();
        host.RowDefinitions.Clear();
        host.ColumnDefinitions.Clear();

        if (!ViewModel.HasProject) return;

        var periods = TeachingPeriods();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        for (var day = 0; day < 5; day++)
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        for (var row = 0; row < periods.Count; row++)
            host.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star),
                MinHeight = 44,
            });

        var corner = HeaderCell("节次", true);
        Grid.SetRow(corner, 0);
        Grid.SetColumn(corner, 0);
        host.Children.Add(corner);

        for (var day = 0; day < 5; day++)
        {
            var header = HeaderCell(WeaveDefaults.Days[day], false);
            Grid.SetRow(header, 0);
            Grid.SetColumn(header, day + 1);
            host.Children.Add(header);
        }

        var subject = ViewModel.SelectedResearchSubject;
        for (var rowIndex = 0; rowIndex < periods.Count; rowIndex++)
        {
            var period = periods[rowIndex].Index;
            var periodOption = ViewModel.PeriodOptions.FirstOrDefault(option => option.Index == period);
            var label = new TextBlock
            {
                Text = periodOption?.Label ?? periods[rowIndex].Label,
                FontSize = 10.5,
                Foreground = InkSoftBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var time = new TextBlock
            {
                Text = periodOption?.TimeText ?? string.Empty,
                FontFamily = MonoFamily,
                FontSize = 8,
                Foreground = MutedBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var header = new Border
            {
                Background = PaperDeepBrush,
                Padding = new Thickness(4, 2),
                Child = new StackPanel
                {
                    Spacing = 1,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { label, time },
                },
            };
            Grid.SetRow(header, rowIndex + 1);
            Grid.SetColumn(header, 0);
            host.Children.Add(header);

            for (var day = 1; day <= 5; day++)
            {
                var hasResearch = subject is not null &&
                                  ViewModel.HasResearchAt(subject.Subject.Id, day, period);
                var isSelected = subject is not null &&
                                 ViewModel.ResearchSlotDay == day &&
                                 ViewModel.ResearchSlotPeriod == period;

                var text = new TextBlock
                {
                    Text = hasResearch ? "教研" : string.Empty,
                    FontSize = 10.5,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = AccentDeepBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var cell = new Border
                {
                    Background = hasResearch ? AccentFill : CardBrush,
                    BorderBrush = isSelected ? AccentBrush : null,
                    BorderThickness = isSelected ? new Thickness(2) : new Thickness(0),
                    Child = text,
                    Cursor = new Cursor(StandardCursorType.Hand),
                };

                var capturedDay = day;
                var capturedPeriod = period;
                cell.PointerPressed += (_, args) =>
                {
                    if (!args.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                    ViewModel.ToggleResearch(capturedDay, capturedPeriod);
                    args.Handled = true;
                };
                ToolTip.SetTip(cell, hasResearch
                    ? $"{WeaveDefaults.SlotText(day, period, period)} · {subject?.Name} 教研（点击取消）"
                    : $"{WeaveDefaults.SlotText(day, period, period)} · 点击设为 {subject?.Name} 教研");

                Grid.SetRow(cell, rowIndex + 1);
                Grid.SetColumn(cell, day);
                host.Children.Add(cell);
            }
        }
    }

    // ----------------------------- 03 走班锁定预览 -----------------------------

    private void BuildLayeredPreviewGrid()
    {
        var host = LayeredPreviewGridHost;
        host.Children.Clear();
        host.RowDefinitions.Clear();
        host.ColumnDefinitions.Clear();

        if (!ViewModel.HasProject) return;

        var periods = TeachingPeriods();
        var teachingIndices = periods.Select(period => period.Index).ToHashSet();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        for (var day = 0; day < 5; day++)
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        for (var row = 0; row < periods.Count; row++)
            host.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star),
                MinHeight = 28,
            });

        var corner = SmallHeaderCell(string.Empty);
        Grid.SetRow(corner, 0);
        Grid.SetColumn(corner, 0);
        host.Children.Add(corner);

        for (var day = 0; day < 5; day++)
        {
            var header = SmallHeaderCell(WeaveDefaults.Days[day].Replace("星期", "周"));
            Grid.SetRow(header, 0);
            Grid.SetColumn(header, day + 1);
            host.Children.Add(header);
        }

        var draftDay = Math.Clamp(ViewModel.LayeredDayIndex, 0, 4) + 1;
        var draftStart = ViewModel.LayeredStartPeriod?.Index ?? 1;
        var draftEnd = ViewModel.LayeredEndPeriod?.Index ?? draftStart;
        if (draftEnd < draftStart) (draftStart, draftEnd) = (draftEnd, draftStart);
        var draftClasses = ViewModel.LayeredClassOptions.Count(option => option.IsSelected);
        var draftActive = draftClasses > 0 && ViewModel.LayeredModeIndex == 0;

        // 各走班组锁定的格子：固定时段按配置区间，自动安排组按生成课表后的落位。
        var lockedCells = new HashSet<(int Day, int Period)>();
        var lockLabels = new Dictionary<(int Day, int Period), List<string>>();
        foreach (var group in ViewModel.Project.LayeredGroups)
        {
            var slots = group.IsFlexible
                ? group.ResolvedSlots.Select(LayeredClassGroup.DecodeSlot)
                : group.Periods.Select(period => (group.DayIndex, period));
            foreach (var (day, period) in slots)
            {
                if (day is < 1 or > 5 || !teachingIndices.Contains(period)) continue;
                lockedCells.Add((day, period));
                if (!lockLabels.TryGetValue((day, period), out var labels))
                    lockLabels[(day, period)] = labels = [];
                labels.Add(string.IsNullOrWhiteSpace(group.Name) ? "走班组" : group.Name);
            }
        }

        for (var rowIndex = 0; rowIndex < periods.Count; rowIndex++)
        {
            var period = periods[rowIndex].Index;
            var label = new TextBlock
            {
                Text = period.ToString(),
                FontFamily = MonoFamily,
                FontSize = 9,
                Foreground = MutedBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var rowHeader = new Border
            {
                Background = PaperDeepBrush,
                Child = label,
            };
            Grid.SetRow(rowHeader, rowIndex + 1);
            Grid.SetColumn(rowHeader, 0);
            host.Children.Add(rowHeader);

            for (var day = 1; day <= 5; day++)
            {
                var selected = draftActive && day == draftDay && period >= draftStart && period <= draftEnd;
                var isLocked = lockedCells.Contains((day, period));

                string text;
                IBrush background;
                IBrush foreground;
                if (selected)
                {
                    text = "锁定";
                    background = AccentFill;
                    foreground = AccentDeepBrush;
                }
                else if (isLocked)
                {
                    text = "走班";
                    background = PinnedBrush;
                    foreground = MutedBrush;
                }
                else
                {
                    text = string.Empty;
                    background = CardBrush;
                    foreground = MutedBrush;
                }

                var cell = new Border
                {
                    Background = background,
                    Child = new TextBlock
                    {
                        Text = text,
                        FontSize = 8.5,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = foreground,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                if (lockLabels.TryGetValue((day, period), out var labels) && labels.Count > 0)
                    ToolTip.SetTip(cell, string.Join(" / ", labels.Distinct()));
                else if (selected)
                    ToolTip.SetTip(cell, $"{draftClasses} 个班在{WeaveDefaults.SlotText(day, period, period)}走班");
                Grid.SetRow(cell, rowIndex + 1);
                Grid.SetColumn(cell, day);
                host.Children.Add(cell);
            }
        }
    }

    // ----------------------------- 05 课表网格 -----------------------------

    private void BuildScheduleGrid()
    {
        ClearDragVisuals();
        ClearSelectionVisual();
        _dragState = DragState.None;
        _dragSource = null;

        var master = ViewModel.IsMasterView;
        ScheduleGridHost.IsVisible = !master;
        MasterViewport.IsVisible = master;

        if (!ViewModel.HasProject) return;

        var periods = TeachingPeriods();
        var index = BuildScheduleIndex();
        if (master) BuildMasterGrid(index, periods);
        else BuildClassOrTeacherGrid(index, periods);
    }

    /// <summary>一次网格刷新内复用的只读索引，避免每格线性扫描整份工程。</summary>
    private ScheduleIndex BuildScheduleIndex()
    {
        var project = ViewModel.Project;
        var index = new ScheduleIndex();
        foreach (var item in project.Classes) index.Classes[item.Id] = item;
        foreach (var item in project.Subjects) index.Subjects[item.Id] = item;
        foreach (var item in project.Teachers) index.Teachers[item.Id] = item;

        foreach (var lesson in project.Lessons)
        {
            var key = (lesson.ClassId, lesson.DayIndex, lesson.Period);
            if (!index.ClassLessons.TryGetValue(key, out var list))
            {
                list = [];
                index.ClassLessons[key] = list;
            }

            list.Add(lesson);
            if (!string.IsNullOrWhiteSpace(lesson.TeacherId))
                index.TeacherLessons.TryAdd((lesson.TeacherId, lesson.DayIndex, lesson.Period), lesson);
        }

        foreach (var group in project.LayeredGroups)
        {
            foreach (var classId in group.ClassIds)
            {
                foreach (var (day, period) in group.Slots())
                    index.LayeredByClass[(classId, day, period)] = group;
            }

            foreach (var layer in group.Layers)
            {
                if (string.IsNullOrWhiteSpace(layer.TeacherId)) continue;
                foreach (var (day, period) in group.Slots())
                    index.LayeredByTeacher.TryAdd((layer.TeacherId, day, period), group);
            }
        }

        foreach (var activity in project.Activities.Where(item => item.Mode == ActivityMode.Overlay))
        {
            var activityPeriods = ActivityResolver.ResolvePeriods(project, activity);
            if (activityPeriods.Count == 0) continue;

            var classIds = activity.ClassIds.Count == 0
                ? project.Classes.Select(item => item.Id)
                : activity.ClassIds;
            foreach (var classId in classIds)
            {
                foreach (var period in activityPeriods)
                {
                    var key = (classId, activity.DayIndex, period);
                    if (!index.Overlays.TryGetValue(key, out var list))
                    {
                        list = [];
                        index.Overlays[key] = list;
                    }

                    list.Add(activity);
                }
            }
        }

        foreach (var item in ViewModel.Conflicts)
        {
            var conflict = item.Conflict;
            if (!string.IsNullOrWhiteSpace(conflict.ClassId))
                index.ConflictClasses.Add((conflict.DayIndex, conflict.Period, conflict.ClassId));
            if (!string.IsNullOrWhiteSpace(conflict.TeacherId))
                index.ConflictTeachers.Add((conflict.DayIndex, conflict.Period, conflict.TeacherId));
        }

        return index;
    }

    private void BuildClassOrTeacherGrid(ScheduleIndex index, List<PeriodSlot> periods)
    {
        var host = ScheduleGridHost;
        host.Children.Clear();
        host.RowDefinitions.Clear();
        host.ColumnDefinitions.Clear();

        var classView = ViewModel.IsClassView;
        var classItem = ViewModel.SelectedClass;
        var teacherItem = ViewModel.SelectedTeacher;

        if (classView && classItem is null) return;
        if (!classView && teacherItem is null) return;

        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
        for (var day = 0; day < 5; day++)
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        for (var row = 0; row < periods.Count; row++)
            host.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star),
                MinHeight = 52,
            });

        var corner = HeaderCell(classView ? classItem!.Name : teacherItem!.Name, false);
        Grid.SetRow(corner, 0);
        Grid.SetColumn(corner, 0);
        host.Children.Add(corner);

        for (var day = 0; day < 5; day++)
        {
            var header = HeaderCell(WeaveDefaults.Days[day], false);
            Grid.SetRow(header, 0);
            Grid.SetColumn(header, day + 1);
            host.Children.Add(header);
        }

        for (var rowIndex = 0; rowIndex < periods.Count; rowIndex++)
        {
            var period = periods[rowIndex].Index;
            var periodOption = ViewModel.PeriodOptions.FirstOrDefault(option => option.Index == period);
            var rowHeader = new Border
            {
                Background = PaperDeepBrush,
                Padding = new Thickness(3, 2),
                Child = new StackPanel
                {
                    Spacing = 1,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = periodOption?.Label ?? $"第{period}节",
                            FontSize = 10,
                            Foreground = InkSoftBrush,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                        new TextBlock
                        {
                            Text = periodOption?.TimeText ?? string.Empty,
                            FontFamily = MonoFamily,
                            FontSize = 7.5,
                            Foreground = MutedBrush,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                    },
                },
            };
            Grid.SetRow(rowHeader, rowIndex + 1);
            Grid.SetColumn(rowHeader, 0);
            host.Children.Add(rowHeader);

            for (var day = 1; day <= 5; day++)
            {
                LessonPlacement? lesson;
                LayeredClassGroup? layeredGroup;
                IReadOnlyList<LessonPlacement> slotLessons;
                if (classView)
                {
                    var key = (classItem!.Class.Id, day, period);
                    slotLessons = index.ClassLessons.GetValueOrDefault(key) ?? [];
                    lesson = slotLessons.FirstOrDefault(item => !item.IsLayered)
                             ?? slotLessons.FirstOrDefault();
                    layeredGroup = index.LayeredByClass.GetValueOrDefault(key);
                }
                else
                {
                    var key = (teacherItem!.Teacher.Id, day, period);
                    lesson = index.TeacherLessons.GetValueOrDefault(key);
                    slotLessons = lesson is null ? [] : [lesson];
                    layeredGroup = index.LayeredByTeacher.GetValueOrDefault(key);
                }

                var tag = classView
                    ? new ScheduleCellTag(classItem!.Class.Id, day, period)
                    : lesson is null
                        ? null
                        : new ScheduleCellTag(lesson.ClassId, day, period);

                var cell = BuildLessonCell(
                    index, day, period, lesson, layeredGroup, slotLessons, tag, classView,
                    classView ? classItem!.Class.Id : null);
                Grid.SetRow(cell, rowIndex + 1);
                Grid.SetColumn(cell, day);
                host.Children.Add(cell);
            }
        }
    }

    private Border BuildLessonCell(
        ScheduleIndex index,
        int day,
        int period,
        LessonPlacement? lesson,
        LayeredClassGroup? layeredGroup,
        IReadOnlyList<LessonPlacement> slotLessons,
        ScheduleCellTag? tag,
        bool classView,
        string? classId)
    {
        string? subjectName = null;
        string? secondLine = null;
        string? layerText = null;
        var isActivity = lesson?.IsActivity == true;
        var layeredLessons = slotLessons.Where(item => item.IsLayered).ToList();
        var isLayered = lesson?.IsLayered == true || layeredLessons.Count > 0;

        if (isActivity)
        {
            subjectName = string.IsNullOrWhiteSpace(lesson!.ActivityName) ? "活动" : lesson.ActivityName;
            secondLine = index.Teachers.GetValueOrDefault(lesson.TeacherId)?.Name ?? lesson.ActivityTime;
            layerText = string.IsNullOrWhiteSpace(lesson.ActivityTime)
                ? "活动"
                : $"活动 · {lesson.ActivityTime}";
        }
        else if (lesson is not null)
        {
            subjectName = index.Subjects.GetValueOrDefault(lesson.SubjectId)?.Name ?? "课程";
            if (lesson.IsLayered)
            {
                var groupClasses = layeredGroup?.ClassIds.Count
                                   ?? layeredLessons.Select(item => item.ClassId).Distinct().Count();
                var layerSummary = string.Join(" / ", layeredLessons
                    .Select(item => $"{item.LayerName} {index.Teachers.GetValueOrDefault(item.TeacherId)?.Name}".Trim())
                    .Where(text => text.Length > 0));

                if (classView)
                {
                    secondLine = layerSummary.Length > 0 ? layerSummary : "分层走班";
                    layerText = groupClasses > 1 ? $"走班 · {groupClasses} 个班同节" : "走班";
                }
                else
                {
                    secondLine = index.Classes.GetValueOrDefault(lesson.ClassId)?.Name;
                    layerText = string.IsNullOrWhiteSpace(lesson.LayerName) ? "走班" : lesson.LayerName;
                }
            }
            else
            {
                secondLine = classView
                    ? index.Teachers.GetValueOrDefault(lesson.TeacherId)?.Name
                    : index.Classes.GetValueOrDefault(lesson.ClassId)?.Name;
                layerText = null;
            }
        }
        else if (classView && layeredGroup is not null)
        {
            subjectName = index.Subjects.GetValueOrDefault(layeredGroup.SubjectId)?.Name;
            secondLine = "分层走班";
            layerText = "待生成";
        }

        // 叠加式活动不改课程，只在格子里补一行陶土色提醒。
        var overlays = classView && classId is not null
            ? index.Overlays.GetValueOrDefault((classId, day, period)) ?? []
            : [];

        var teacherId = lesson?.TeacherId;
        var conflict = HasConflict(index, tag?.ClassId, teacherId, day, period);

        var stack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(6, 4),
        };

        if (!string.IsNullOrWhiteSpace(subjectName))
        {
            stack.Children.Add(new TextBlock
            {
                Text = subjectName,
                FontSize = 12.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = isActivity ? AccentDeepBrush : InkBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        if (!string.IsNullOrWhiteSpace(secondLine))
        {
            stack.Children.Add(new TextBlock
            {
                Text = secondLine,
                FontSize = 10.5,
                Foreground = MutedBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        if (!string.IsNullOrWhiteSpace(layerText))
        {
            stack.Children.Add(new Border
            {
                Background = AccentSoftFill,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = layerText,
                    FontSize = 8.5,
                    Foreground = AccentDeepBrush,
                },
            });
        }

        foreach (var overlay in overlays)
        {
            stack.Children.Add(new Border
            {
                Background = AccentSoftFill,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = new TextBlock
                {
                    Text = $"＋{overlay.Name}",
                    FontSize = 8.5,
                    Foreground = AccentDeepBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            });
        }

        var hasActivity = isActivity || overlays.Count > 0;
        var content = new Grid();
        content.Children.Add(stack);
        if (lesson is not null && lesson.IsFixed && !lesson.IsActivity && !lesson.IsLayered)
        {
            content.Children.Add(new Border
            {
                Background = AccentSoftFill,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(3, 2),
                Child = new TextBlock
                {
                    Text = "锁",
                    FontSize = 8,
                    Foreground = AccentDeepBrush,
                },
            });
        }

        if (conflict)
        {
            content.Children.Add(new TextBlock
            {
                Text = "!",
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                Foreground = AccentDeepBrush,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4, 2),
            });
        }

        var isLayeredCell = isLayered || (lesson is null && layeredGroup is not null);
        var cell = new Border
        {
            Background = hasActivity ? AccentSoftFill : isLayeredCell ? PinnedBrush : CardBrush,
            BorderBrush = conflict ? AccentBrush : null,
            BorderThickness = conflict ? new Thickness(1.5) : new Thickness(0),
            Child = content,
            Tag = tag,
            Cursor = HandCursor,
        };

        var subjectText = subjectName ?? string.Empty;
        var teacherText = lesson is null
            ? string.Empty
            : ViewModel.Project.FindTeacher(lesson.TeacherId)?.Name ?? string.Empty;
        var tip = string.IsNullOrWhiteSpace(subjectText)
            ? $"{WeaveDefaults.SlotText(day, period, period)} · 空"
            : $"{WeaveDefaults.SlotText(day, period, period)} · {subjectText} {teacherText}";
        if (lesson is not null && lesson.IsFixed && !lesson.IsActivity && !lesson.IsLayered)
            tip += " · 已锁定：重新生成时保留";
        if (overlays.Count > 0)
            tip += " · " + string.Join(" / ",
                overlays.Select(item => $"{item.Name} {ActivityResolver.TimeText(item)}"));
        if (layeredGroup is not null)
        {
            var layerTexts = layeredGroup.Layers
                .Select(layer => $"{layer.Name}：{ViewModel.Project.FindTeacher(layer.TeacherId)?.Name ?? "未指定"}")
                .ToList();
            tip += $" · 走班组「{layeredGroup.Name}」{layeredGroup.ClassIds.Count} 个班同节，参与班均上走班课";
            if (layerTexts.Count > 0) tip += " · " + string.Join(" / ", layerTexts);
            if (lesson is null) tip += " · 生成课表时自动填入";
        }

        ToolTip.SetTip(cell, tip);

        return cell;
    }

    private void BuildMasterGrid(ScheduleIndex index, List<PeriodSlot> periods)
    {
        var host = MasterGridHost;
        var classes = ViewModel.Project.Classes;
        var signature = string.Join('|', classes.Select(item => item.Id)) +
                        "#" + string.Join(',', periods.Select(item => item.Index));

        if (signature == _masterGridSignature && _masterCells.Count > 0)
        {
            RefreshMasterCells(index);
            return;
        }

        _masterGridSignature = signature;
        _masterCells.Clear();
        host.Children.Clear();
        host.RowDefinitions.Clear();
        host.ColumnDefinitions.Clear();

        const int periodColumnWidth = 44;
        const int classColumnWidth = 116;

        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(classColumnWidth) });
        for (var column = 0; column < 5 * periods.Count; column++)
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(periodColumnWidth) });

        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        for (var row = 0; row < classes.Count; row++)
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

        var corner = HeaderCell("班级", false);
        Grid.SetRow(corner, 0);
        Grid.SetColumn(corner, 0);
        Grid.SetRowSpan(corner, 2);
        host.Children.Add(corner);

        for (var day = 0; day < 5; day++)
        {
            var dayHeader = new Border
            {
                Background = PaperDeepBrush,
                Child = new TextBlock
                {
                    Text = WeaveDefaults.Days[day],
                    FontSize = 9.5,
                    Foreground = InkSoftBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Grid.SetRow(dayHeader, 0);
            Grid.SetColumn(dayHeader, 1 + day * periods.Count);
            Grid.SetColumnSpan(dayHeader, periods.Count);
            host.Children.Add(dayHeader);

            for (var periodIndex = 0; periodIndex < periods.Count; periodIndex++)
            {
                var periodHeader = new Border
                {
                    Background = PaperDeepBrush,
                    Child = new TextBlock
                    {
                        Text = periods[periodIndex].Index.ToString(),
                        FontFamily = MonoFamily,
                        FontSize = 8,
                        Foreground = MutedBrush,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                Grid.SetRow(periodHeader, 1);
                Grid.SetColumn(periodHeader, 1 + day * periods.Count + periodIndex);
                host.Children.Add(periodHeader);
            }
        }

        for (var row = 0; row < classes.Count; row++)
        {
            var schoolClass = classes[row];
            var label = new Border
            {
                Background = PaperDeepBrush,
                Padding = new Thickness(7, 2),
                Child = new TextBlock
                {
                    Text = schoolClass.Name,
                    FontSize = 10.5,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = InkBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            Grid.SetRow(label, row + 2);
            Grid.SetColumn(label, 0);
            host.Children.Add(label);

            for (var day = 1; day <= 5; day++)
            {
                for (var periodIndex = 0; periodIndex < periods.Count; periodIndex++)
                {
                    var period = periods[periodIndex].Index;
                    var text = new TextBlock
                    {
                        FontSize = 9.5,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    var cell = new Border
                    {
                        Tag = new ScheduleCellTag(schoolClass.Id, day, period),
                        Cursor = HandCursor,
                        Child = text,
                    };

                    ApplyMasterCell(cell, text, schoolClass, day, period, index);
                    _masterCells[(schoolClass.Id, day, period)] = (cell, text);

                    Grid.SetRow(cell, row + 2);
                    Grid.SetColumn(cell, 1 + (day - 1) * periods.Count + periodIndex);
                    host.Children.Add(cell);
                }
            }
        }
    }

    /// <summary>复用已有格子，只更新课程文字、底色、冲突边框与悬浮提示。</summary>
    private void RefreshMasterCells(ScheduleIndex index)
    {
        foreach (var (key, cell) in _masterCells)
        {
            var schoolClass = index.Classes.GetValueOrDefault(key.ClassId);
            if (schoolClass is null) continue;
            ApplyMasterCell(cell.Border, cell.Text, schoolClass, key.Day, key.Period, index);
        }
    }

    private void ApplyMasterCell(
        Border cell,
        TextBlock text,
        SchoolClass schoolClass,
        int day,
        int period,
        ScheduleIndex index)
    {
        var key = (schoolClass.Id, day, period);
        var lesson = index.ClassLessons.GetValueOrDefault(key)?.FirstOrDefault();
        var layeredGroup = index.LayeredByClass.GetValueOrDefault(key);
        var overlays = index.Overlays.GetValueOrDefault(key) ?? [];
        var isActivity = lesson?.IsActivity == true;
        var isLayered = lesson?.IsLayered == true || layeredGroup is not null;

        var subjectName = isActivity
            ? lesson!.ActivityName
            : lesson is null
                ? layeredGroup is not null
                    ? "走班"
                    : string.Join("/", overlays.Select(item => item.Name))
                : index.Subjects.GetValueOrDefault(lesson.SubjectId)?.Name ?? "课程";
        var conflict = HasConflict(index, schoolClass.Id, lesson?.TeacherId, day, period);
        var hasActivity = isActivity || overlays.Count > 0;

        cell.Background = hasActivity ? AccentSoftFill : isLayered ? PinnedBrush : CardBrush;
        cell.BorderBrush = conflict ? AccentBrush : null;
        cell.BorderThickness = conflict ? new Thickness(1.5) : new Thickness(0);
        text.Text = subjectName;
        text.Foreground = hasActivity ? AccentDeepBrush
            : lesson is null && layeredGroup is null ? MutedBrush : InkSoftBrush;

        if (lesson is null && overlays.Count == 0 && layeredGroup is null)
        {
            ToolTip.SetTip(cell, null);
            return;
        }

        var tip = $"{schoolClass.Name} · {WeaveDefaults.SlotText(day, period, period)} · {subjectName}";
        if (overlays.Count > 0)
            tip += " · " + string.Join(" / ",
                overlays.Select(item => $"{item.Name} {ActivityResolver.TimeText(item)}"));
        if (layeredGroup is not null)
        {
            tip += $" · 走班组「{layeredGroup.Name}」{layeredGroup.ClassIds.Count} 个班同节，参与班均上走班课";
            if (lesson is null) tip += " · 生成课表时自动填入";
        }

        ToolTip.SetTip(cell, tip);
    }


    /// <summary>界面网格只画正课节次；作息里的课服 / 晚修不进入班级网格。</summary>
    private List<PeriodSlot> TeachingPeriods()
    {
        var periods = ViewModel.Project.Periods
            .Where(period => !period.IsExtension)
            .OrderBy(period => period.Index)
            .ToList();
        return periods.Count > 0 ? periods : WeaveDefaults.CreatePeriods();
    }

    private static bool HasConflict(
        ScheduleIndex index, string? classId, string? teacherId, int day, int period)
    {
        if (!string.IsNullOrEmpty(classId) &&
            index.ConflictClasses.Contains((day, period, classId))) return true;
        if (!string.IsNullOrEmpty(teacherId) &&
            index.ConflictTeachers.Contains((day, period, teacherId))) return true;
        return false;
    }

    // ----------------------------- 拖拽与选中 -----------------------------

    private void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_ghostFlying) FinalizeGhostFly(); // 飞行中被再次按下：先立刻到位提交，手感不留断点

        _suppressClick = false;
        var pointer = e.GetCurrentPoint(this);
        var source = e.Source;

        if (pointer.Properties.IsRightButtonPressed)
        {
            if (ViewModel.IsBusy || _isClosing) return;

            var tag = FindScheduleCell(source, out var hitBorder);
            if (tag is not null && hitBorder is not null)
            {
                SelectCell(tag, hitBorder);
                ShowCellContextMenu(tag, hitBorder);
                e.Handled = true;
            }
            return;
        }

        if (!pointer.Properties.IsLeftButtonPressed || ViewModel.IsBusy) return;

        var cellTag = FindScheduleCell(source, out var border);
        if (cellTag is null) return;

        _pendingSelectTag = cellTag;
        _pendingSelectBorder = border;
        _pressPoint = e.GetPosition(this);
        _dragState = DragState.Candidate;
        _dragSource = null;
        _dragVelocity.Begin();

        // 记录抓取偏移与源格尺寸：拖拽时幽灵跟手，而不是居中吸附。
        if (border is not null)
        {
            _grabOffset = e.GetPosition(border);
            _dragSourceSize = border.Bounds.Size;
        }

        if (!ViewModel.IsClassView) return;

        var lesson = ViewModel.Project.LessonAt(cellTag.ClassId, cellTag.DayIndex, cellTag.Period);
        if (lesson is null || lesson.IsActivity) return;

        _dragState = DragState.Candidate;
        _dragSource = cellTag;
    }

    private void OnGridPointerMoved(object? sender, PointerEventArgs e)
    {
        // Candidate=尚未过 10px 迟滞，Dragging=已起拖。两者都要放行：
        // 只认 Candidate 的话，BeginDrag 切到 Dragging 后后续移动全被吞掉，
        // 幽灵永远停在第一次 UpdateDrag 的位置（实测发现的既有缺陷）。
        if (_dragState is not (DragState.Candidate or DragState.Dragging)) return;
        if (_dragSource is null || ViewModel.IsBusy) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var position = e.GetPosition(this);
        _dragVelocity.Add(position, NowSeconds());
        if (_dragState == DragState.Candidate)
        {
            var dx = position.X - _pressPoint.X;
            var dy = position.Y - _pressPoint.Y;
            if (dx * dx + dy * dy < 100) return;
            BeginDrag();
        }

        if (_dragState != DragState.Dragging) return;
        UpdateDrag(position);
    }

    private void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragState == DragState.Dragging)
        {
            var sourceTag = _dragSource;
            var releasePoint = e.GetPosition(this);
            var velocity = _dragVelocity.Velocity(NowSeconds());
            var bounds = ScheduleCardBounds();

            EndDragTracking();
            _suppressClick = true;
            BeginLessonDropFlight(sourceTag, releasePoint, velocity, bounds);
            return;
        }

        Border? border = null;
        var tag = _pendingSelectTag;
        if (tag is null) tag = FindScheduleCell(e.Source, out border);
        var cellBorder = _pendingSelectBorder ?? border;
        _dragState = DragState.None;
        _dragSource = null;
        _pendingSelectTag = null;
        _pendingSelectBorder = null;

        if (tag is not null && !_suppressClick)
        {
            SelectCell(tag, cellBorder);
            if (ViewModel.IsClassView || ViewModel.IsMasterView)
                _ = OpenLessonEditorAsync(tag);
        }
    }

    /// <summary>点击课程格：弹出本班「学科 + 任课教师」列表，点一行即可放入或替换。</summary>
    private async Task OpenLessonEditorAsync(ScheduleCellTag tag)
    {
        if (_lessonEditorOpen || ViewModel.IsBusy || !ViewModel.HasProject) return;
        if (!ViewModel.IsTimetableConfirmed)
        {
            ViewModel.SetStatus("请先在 01 步「作息时间」里确认时间表，再手动编排课表。", true);
            return;
        }

        var lessons = ViewModel.Project.Lessons
            .Where(item => item.ClassId == tag.ClassId &&
                           item.DayIndex == tag.DayIndex &&
                           item.Period == tag.Period)
            .ToList();
        if (lessons.Any(item => item.IsActivity))
        {
            ViewModel.SetStatus("这是活动占位，请先在「活动」列表里修改或删除活动。", true);
            return;
        }

        ConstraintRules.TryGetLayeredGroupAt(
            ViewModel.Project, tag.ClassId, tag.DayIndex, tag.Period, out var layeredGroup);
        if (lessons.Any(item => item.IsLayered) || layeredGroup is not null)
        {
            var groupText = layeredGroup is null ? string.Empty : $"「{layeredGroup.Name}」";
            ViewModel.SetStatus($"这是走班组{groupText}的时段，生成课表时自动填入，不能手动放课。", true);
            return;
        }

        _lessonEditorOpen = true;
        try
        {
            var dialog = new LessonEditorWindow(
                ViewModel.Project, tag.ClassId, tag.DayIndex, tag.Period);
            var result = await dialog.ShowDialog<LessonEditorResult?>(this);
            if (result is null) return;

            if (result.Clear)
            {
                ViewModel.ClearLessonAt(result.ClassId, result.DayIndex, result.Period);
                return;
            }

            ViewModel.PlaceLesson(
                result.ClassId, result.DayIndex, result.Period, result.SubjectId, result.TeacherId);
        }
        finally
        {
            _lessonEditorOpen = false;
        }
    }

    /// <summary>右键课程格：放入课程、锁定/解锁本节、清空或批量锁定本班。</summary>
    private void ShowCellContextMenu(ScheduleCellTag tag, Border border)
    {
        var lesson = ViewModel.Project.LessonAt(tag.ClassId, tag.DayIndex, tag.Period);
        ConstraintRules.TryGetLayeredGroupAt(
            ViewModel.Project, tag.ClassId, tag.DayIndex, tag.Period, out var layeredGroup);
        var isActivity = lesson?.IsActivity == true;
        var isLayered = lesson?.IsLayered == true || layeredGroup is not null;
        var isLocked = lesson is not null && lesson.IsFixed && !isActivity;
        var canEdit = ViewModel.IsTimetableConfirmed && !isActivity && !isLayered;
        var canLock = lesson is not null && !isActivity && !isLayered;

        var menu = new ContextMenu();

        if (canEdit)
        {
            menu.Items.Add(CreateMenuItem(
                lesson is null ? "放入课程…" : "替换课程…",
                () => _ = OpenLessonEditorAsync(tag)));
        }

        if (canLock)
        {
            menu.Items.Add(CreateMenuItem(
                isLocked ? "解除本节锁定" : "锁定本节（重排保留）",
                () =>
                {
                    ViewModel.ToggleLessonLock(tag.ClassId, tag.DayIndex, tag.Period);
                    TrySelectScheduleCell(tag);
                }));
            menu.Items.Add(CreateMenuItem(
                "清空本节",
                () => ViewModel.ClearLessonAt(tag.ClassId, tag.DayIndex, tag.Period)));
        }

        if (isActivity)
            menu.Items.Add(CreateMenuItem("活动占位由「活动」列表管理", null));
        else if (isLayered)
            menu.Items.Add(CreateMenuItem("走班时段由 03 步走班组管理", null));

        menu.Items.Add(new Separator());

        if (ViewModel.IsClassView)
        {
            menu.Items.Add(CreateMenuItem(
                "锁定本班全部普通课",
                () => ViewModel.SetClassLessonsLocked(tag.ClassId, locked: true)));
            menu.Items.Add(CreateMenuItem(
                "解除本班全部锁定",
                () => ViewModel.SetClassLessonsLocked(tag.ClassId, locked: false)));
        }

        menu.Items.Add(CreateMenuItem(
            "刷新冲突检查",
            () => _ = ViewModel.RefreshConflictsAsync()));

        menu.Open(border);
    }

    private static MenuItem CreateMenuItem(string header, Action? action)
    {
        var item = new MenuItem { Header = header, IsEnabled = action is not null };
        if (action is not null) item.Click += (_, _) => action();
        return item;
    }

    private void OnLockClass(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedClass is { } classItem)
            ViewModel.SetClassLessonsLocked(classItem.Class.Id, locked: true);
    }

    private void OnUnlockClass(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedClass is { } classItem)
            ViewModel.SetClassLessonsLocked(classItem.Class.Id, locked: false);
    }

    private void OnGridPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_dragState != DragState.Dragging) return;
        EndDragVisuals();
        _dragState = DragState.None;
        _dragSource = null;
        _pendingSelectTag = null;
        _pendingSelectBorder = null;
    }

    private void BeginDrag()
    {
        _dragState = DragState.Dragging;
        if (_dragSource is not { } source) return;

        var lesson = ViewModel.Project.LessonAt(source.ClassId, source.DayIndex, source.Period);
        if (lesson is null || lesson.IsActivity)
        {
            _dragState = DragState.None;
            _dragSource = null;
            return;
        }

        var subject = ViewModel.Project.FindSubject(lesson.SubjectId)?.Name ?? "课程";
        var teacher = ViewModel.Project.FindTeacher(lesson.TeacherId)?.Name;

        _dragGhost = new Border
        {
            Width = _dragSourceSize.Width > 1 ? _dragSourceSize.Width : 112,
            Height = _dragSourceSize.Height > 1 ? _dragSourceSize.Height : 58,
            Background = CardBrush,
            BorderBrush = InkBrush,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Opacity = 0.94,
            IsHitTestVisible = false,
            Child = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = subject,
                        FontSize = 12.5,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = InkBrush,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = teacher ?? string.Empty,
                        FontSize = 9.5,
                        Foreground = MutedBrush,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };
        ScheduleDragLayer.Children.Add(_dragGhost);
    }

    private void UpdateDrag(Point windowPosition)
    {
        if (_dragGhost is { } ghost)
        {
            // 界内「抓哪儿是哪儿」；拖出课表卡片后幽灵中心受橡皮筋阻力。
            // 浮层已移到窗口根（P1-B），拖出卡片 Border 也不会被裁掉。
            _flyCurrent = ToLayerPoint(RubberbandedGhostTopLeft(windowPosition, ScheduleCardBounds()));
            Canvas.SetLeft(ghost, _flyCurrent.X);
            Canvas.SetTop(ghost, _flyCurrent.Y);
        }

        var hit = this.InputHitTest(windowPosition) as Visual;
        var tag = FindScheduleCell(hit, out var border);
        if (tag is not null && _dragSource is not null && tag.ClassId != _dragSource.ClassId)
            tag = null;

        SetDropTarget(tag, border);
    }

    // ----------------------------- 落点投影与幽灵飞行（P1-B，与归位同套物理） -----------------------------

    /// <summary>课表卡片在窗口坐标系里的范围，投影与橡皮筋都以它为界。</summary>
    private Rect ScheduleCardBounds()
    {
        if (ScheduleCard is not { } host) return default;
        if (host.TranslatePoint(new Point(0, 0), this) is not Point origin) return default;
        return new Rect(origin, host.Bounds.Size);
    }

    /// <summary>窗口坐标 → 拖拽浮层坐标。</summary>
    private Point ToLayerPoint(Point windowPoint) =>
        this.TranslatePoint(windowPoint, ScheduleDragLayer) ?? windowPoint;

    /// <summary>拖出卡片才有的阻力：幽灵中心被橡皮筋压回边界，左上角随之收拢。</summary>
    private Point RubberbandedGhostTopLeft(Point position, Rect bounds)
    {
        var raw = new Point(position.X - _grabOffset.X, position.Y - _grabOffset.Y);
        if (bounds.Width <= 0 || bounds.Height <= 0) return raw;

        var half = new Vector(_dragSourceSize.Width / 2, _dragSourceSize.Height / 2);
        var center = raw + half;
        var clamped = DragPhysics.RubberbandPoint(center, bounds);
        return clamped == center ? raw : clamped - half;
    }

    /// <summary>投影后的有效落点：界内释放沿速度滑行，滑出边界则被橡皮筋压回。</summary>
    private static Point EffectiveDropPoint(Rect bounds, Point releasePoint, Vector velocity)
    {
        var projected = DragPhysics.ProjectedPoint(releasePoint, velocity);
        return bounds.Contains(projected) ? projected : DragPhysics.RubberbandPoint(projected, bounds);
    }

    /// <summary>距离给定点最近的本班课程格及其中心（窗口坐标）。</summary>
    private (Border Border, ScheduleCellTag Tag, Point Center)? NearestScheduleCellTo(Point windowPoint, string classId)
    {
        Border? nearest = null;
        var nearestTag = default(ScheduleCellTag);
        var nearestCenter = default(Point);
        var best = double.MaxValue;

        foreach (var child in ScheduleGridHost.Children)
        {
            if (child is not Border cell || cell.Tag is not ScheduleCellTag tag) continue;
            if (tag.ClassId != classId) continue;
            if (!cell.IsVisible || cell.Bounds.Width < 1 || cell.Bounds.Height < 1) continue;
            if (cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), this) is not Point center) continue;

            var dx = center.X - windowPoint.X;
            var dy = center.Y - windowPoint.Y;
            var distance = dx * dx + dy * dy;
            if (distance >= best) continue;

            best = distance;
            nearest = cell;
            nearestTag = tag;
            nearestCenter = center;
        }

        return nearest is null ? null : (nearest, nearestTag!, nearestCenter);
    }

    /// <summary>按标签找源格（取消时让幽灵飞回原位）。</summary>
    private Point? CellCenter(ScheduleCellTag tag)
    {
        foreach (var child in ScheduleGridHost.Children)
        {
            if (child is not Border cell || cell.Tag is not ScheduleCellTag cellTag) continue;
            if (cellTag != tag) continue;
            if (cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), this) is Point center)
                return center;
        }

        return null;
    }

    /// <summary>
    /// 课程格松手：按投影落点决定去处，幽灵以 120–200ms 临界阻尼飞向目标格中心，
    /// 到位后与 MoveLesson 同帧完成。拖出课表卡片或找不到本班格子则飞回源格取消。
    /// </summary>
    private void BeginLessonDropFlight(ScheduleCellTag? source, Point releasePoint, Vector velocity, Rect bounds)
    {
        if (source is null)
        {
            EndDragVisuals();
            return;
        }

        var target = bounds.Contains(releasePoint)
            ? NearestScheduleCellTo(EffectiveDropPoint(bounds, releasePoint, velocity), source.ClassId)
            : null;

        if (target is not null)
        {
            var sourceTag = source;
            var targetTag = target.Value.Tag;
            BeginGhostFly(
                target.Value.Center,
                velocity.Length,
                () => ViewModel.MoveLesson(
                    sourceTag.ClassId,
                    sourceTag.DayIndex,
                    sourceTag.Period,
                    targetTag.DayIndex,
                    targetTag.Period));
            return;
        }

        // 拖出课表卡片（拖出即取消）或投影找不到本班格子：幽灵飞回源格后取消。
        var sourceCenter = CellCenter(source);
        if (sourceCenter is Point center)
        {
            BeginGhostFly(center, velocity.Length, () => ViewModel.SetStatus("拖拽未落在课程格上，未移动。"));
            return;
        }

        EndDragVisuals();
        ViewModel.SetStatus("拖拽未落在课程格上，未移动。");
    }

    /// <summary>启动幽灵飞行：速度越快时长越短（快甩 120ms，慢放 200ms）。</summary>
    private void BeginGhostFly(Point targetCenterWindow, double speed, Action commit)
    {
        if (_dragGhost is null)
        {
            commit();
            return;
        }

        _flyCommit = commit;
        _flyTarget = ToLayerPoint(targetCenterWindow - new Vector(_dragSourceSize.Width / 2, _dragSourceSize.Height / 2));
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

        if (remaining.Length > 0.7 && elapsed < _flyDuration) return;
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
        RestoreDropTarget();
        _dragState = DragState.None;
        _dragSource = null;
        _pendingSelectTag = null;
        _pendingSelectBorder = null;
    }

    private void EndDragVisuals()
    {
        EndDragTracking();
        ScheduleDragLayer.Children.Clear();
        _dragGhost = null;
        _flyCommit = null;
        StopGhostFly();
    }

    private void SetDropTarget(ScheduleCellTag? tag, Border? border)
    {
        if (ReferenceEquals(tag, _dropTargetTag) && ReferenceEquals(border, _dropTargetBorder)) return;

        RestoreDropTarget();
        if (tag is null || border is null) return;

        _dropTargetTag = tag;
        _dropTargetBorder = border;
        _dropTargetBrush = border.BorderBrush;
        _dropTargetThickness = border.BorderThickness;
        border.BorderBrush = AccentBrush;
        border.BorderThickness = new Thickness(2);
    }

    private void RestoreDropTarget()
    {
        if (_dropTargetBorder is not null)
        {
            _dropTargetBorder.BorderBrush = _dropTargetBrush;
            _dropTargetBorder.BorderThickness = _dropTargetThickness;
        }

        _dropTargetTag = null;
        _dropTargetBorder = null;
        _dropTargetBrush = null;
        _dropTargetThickness = default;
    }

    private void ClearDragVisuals()
    {
        RestoreDropTarget();
        ScheduleDragLayer.Children.Clear();
        _dragGhost = null;
        _flyCommit = null;
        StopGhostFly();
    }

    private void SelectCell(ScheduleCellTag tag, Border? border)
    {
        if (_selectedTag is not null && tag == _selectedTag && ReferenceEquals(border, _selectedBorder))
            return;

        RestoreSelection();
        _selectedTag = tag;
        _selectedBorder = border;
        if (border is null) return;

        _selectedBrush = border.BorderBrush;
        _selectedThickness = border.BorderThickness;
        border.BorderBrush = InkBrush;
        border.BorderThickness = new Thickness(2);
    }

    private void RestoreSelection()
    {
        if (_selectedBorder is not null)
        {
            _selectedBorder.BorderBrush = _selectedBrush;
            _selectedBorder.BorderThickness = _selectedThickness;
        }

        _selectedTag = null;
        _selectedBorder = null;
        _selectedBrush = null;
        _selectedThickness = default;
    }

    private void ClearSelectionVisual() => RestoreSelection();

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!ViewModel.IsScheduleStep || e.Source is TextBox || ViewModel.IsBusy || _isClosing)
            return;

        if (e.Key == Key.Delete)
        {
            if (_selectedTag is { } tag)
            {
                ViewModel.ClearLessonAt(tag.ClassId, tag.DayIndex, tag.Period);
                e.Handled = true;
            }
            else
            {
                ViewModel.SetStatus("先选中一个课程格，再按 Delete 清除。", true);
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.L && e.KeyModifiers == KeyModifiers.None)
        {
            if (_selectedTag is { } tag)
            {
                ViewModel.ToggleLessonLock(tag.ClassId, tag.DayIndex, tag.Period);
                TrySelectScheduleCell(tag);
                e.Handled = true;
            }
            else
            {
                ViewModel.SetStatus("先选中一个课程格，再按 L 锁定/解锁。", true);
                e.Handled = true;
            }
        }
    }

    private static ScheduleCellTag? FindScheduleCell(object? source, out Border? border)
    {
        border = null;
        var visual = source as Visual;
        while (visual is not null)
        {
            if (visual is Border candidate && candidate.Tag is ScheduleCellTag tag)
            {
                border = candidate;
                return tag;
            }

            visual = visual.GetVisualParent();
        }

        return null;
    }

    // ----------------------------- 顶栏动作 -----------------------------

    /// <summary>
    /// 切换工程前的确认。无路径工程的实体只存在于恢复文件里，而各入口都会 clearRecovery，
    /// 所以这里必须拦一次，否则误点「载入示例数据」会连恢复副本一起抹掉。
    /// </summary>
    private async Task<bool> ConfirmSwitchProjectAsync(string action)
    {
        if (!ViewModel.HasProject) return true;

        var detail = string.IsNullOrEmpty(_currentProjectPath)
            ? "当前工程还没有保存到文件，切换后只留在恢复文件里，且会被新工程覆盖。"
            : $"当前工程未保存的改动会丢失。已保存的部分在：{_currentProjectPath}";

        return await ConfirmDialog.ShowAsync(
            this, $"切换到「{action}」？", detail, "继续切换", "留在当前工程", danger: true);
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenAsync("导入课表", new FilePickerFileType("Excel 工作簿")
        {
            Patterns = new[] { "*.xlsx", "*.xls" },
        });
        if (path is null) return;

        if (!await ConfirmSwitchProjectAsync("导入课表")) return;

        await ImportProjectAsync(path);
    }

    private async void OnNewProject(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy) return;
        if (!await ConfirmSwitchProjectAsync("全新制作")) return;

        var dialog = new NewProjectWindow();
        var config = await dialog.ShowDialog<NewProjectConfig?>(this);
        if (config is null) return;

        StartAutosaveSession(null, clearRecovery: true);
        ViewModel.CreateNewProject(config);

        // 新工程还没有任课关系，直接接上教师分工，避免老师在五步界面里找不到入口。
        ViewModel.SetStatus("空白工程已创建，接下来填写教师分工。");
        var assignmentDialog = new AssignmentEditorWindow(ViewModel.Project);
        var changed = await assignmentDialog.ShowDialog<bool?>(this);
        if (changed == true) ViewModel.RefreshAfterAssignmentEdit();
    }

    private async void OnEditAssignments(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasProject || ViewModel.IsBusy) return;

        var dialog = new AssignmentEditorWindow(ViewModel.Project);
        var changed = await dialog.ShowDialog<bool?>(this);
        if (changed == true) ViewModel.RefreshAfterAssignmentEdit();
    }

    private async void OnEditTimetable(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasProject || ViewModel.IsBusy) return;

        var dialog = new TimetableEditorWindow(ViewModel.Project);
        var result = await dialog.ShowDialog<TimetableConfirmResult?>(this);
        if (result is not null) ViewModel.AfterTimetableConfirmed(result);
    }

    private void OnConfirmTimetable(object? sender, RoutedEventArgs e) => ViewModel.ConfirmTimetable();

    private void OnGoTimetable(object? sender, RoutedEventArgs e)
    {
        ViewModel.ActiveStep = 1;
        BuildCurrentStep();
    }

    private async void OnLoadDemo(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmSwitchProjectAsync("载入示例数据")) return;
        LoadDemoProject();
    }

    private async void OnAddActivity(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasProject || ViewModel.IsBusy) return;
        await OpenActivityEditorAsync(null);
    }

    private async void OnEditActivity(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy) return;
        if ((sender as Control)?.DataContext is ActivityItemViewModel item)
            await OpenActivityEditorAsync(item.Activity);
    }

    private async Task OpenActivityEditorAsync(ActivityBlock? existing)
    {
        if (!ViewModel.IsTimetableConfirmed)
        {
            ViewModel.SetStatus("请先在 01 步「作息时间」里确认时间表，再编辑活动。", true);
            return;
        }

        var editor = new ActivityEditorWindow(ViewModel.Project, existing);
        var result = await editor.ShowDialog<ActivityEditorResult?>(this);
        if (result is null) return;

        if (result.Delete)
        {
            if (existing is not null) ViewModel.DeleteActivity(existing.Id);
            return;
        }

        if (result.Activity is not null) ViewModel.SaveActivity(result.Activity);
    }

    private async Task ImportProjectAsync(string path)
    {
        if (ViewModel.IsBusy) return;

        ViewModel.IsBusy = true;
        ViewModel.SetStatus("正在读取工作簿…");
        try
        {
            var result = await Task.Run(() => new WeaveWorkbookImporter().Import(path));
            StartAutosaveSession(null, clearRecovery: true);
            ViewModel.LoadImportResult(result, path);
        }
        catch (Exception ex)
        {
            Report(ex, "导入课表失败");
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    private async void OnOpenProject(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenAsync("打开织序工程", new FilePickerFileType("织序工程")
        {
            Patterns = new[] { "*.weave.json", "*.json" },
        });
        if (path is null) return;

        if (!await ConfirmSwitchProjectAsync("打开工程")) return;

        ViewModel.IsBusy = true;
        try
        {
            var project = await Task.Run(() => WeaveProjectIO.Load(path));
            StartAutosaveSession(path, clearRecovery: true);
            ViewModel.LoadProject(project);
            ViewModel.SetStatus("已打开织序工程。");
        }
        catch (Exception ex)
        {
            Report(ex, "打开工程失败");
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    private async void OnSaveProject(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasProject) return;

        var path = await PickSaveAsync("保存织序工程", SafeFileName(".weave.json"), "json",
            new FilePickerFileType("织序工程") { Patterns = new[] { "*.weave.json", "*.json" } });
        if (path is null) return;

        if (IsSamePath(path, ViewModel.Project.SourceFile))
        {
            ViewModel.SetStatus("工程保存路径不能覆盖已导入的课表文件。", true);
            return;
        }

        ViewModel.IsBusy = true;
        try
        {
            var json = WeaveProjectIO.Serialize(ViewModel.Project);
            await Task.Run(() => _autosaveStore.SaveProjectFile(json, path));

            _currentProjectPath = Path.GetFullPath(path);
            _lastSavedJson = json;
            _autosaveDirty = false;
            _autosaveVersion++;
            _autosaveStore.Clear();
            RefreshRecoveryState();
            ViewModel.SetAutosaveStatus(
                $"已保存工程 · {DateTime.Now:HH:mm}",
                $"自动保存会继续写回：{_currentProjectPath}");
            ViewModel.SetStatus("已保存织序工程。");
        }
        catch (Exception ex)
        {
            Report(ex, "保存工程失败");
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    private async void OnGenerate(object? sender, RoutedEventArgs e) => await ViewModel.GenerateAsync();

    private async void OnRefreshConflicts(object? sender, RoutedEventArgs e) =>
        await ViewModel.RefreshConflictsAsync();

    private void OnOpenConflictCenter(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasProject) return;

        if (_conflictCenter is { IsVisible: true })
        {
            _conflictCenter.Activate();
            return;
        }

        _conflictCenter = new ConflictCenterWindow(ViewModel, NavigateToConflict);
        _conflictCenter.Closed += (_, _) => _conflictCenter = null;
        _conflictCenter.Show(this);
    }

    /// <summary>从冲突聚合台跳回主窗口：优先定位班级/教师，有节次时选中课程格。</summary>
    private void NavigateToConflict(WeaveConflict conflict)
    {
        if (!ViewModel.HasProject) return;
        Activate();
        ViewModel.ActiveStep = 5;

        var classId = conflict.ClassId;
        if (string.IsNullOrWhiteSpace(classId) &&
            !string.IsNullOrWhiteSpace(conflict.TeacherId) &&
            conflict.DayIndex >= 1 &&
            conflict.Period >= 1)
        {
            classId = ViewModel.Project.Lessons.FirstOrDefault(lesson =>
                lesson.TeacherId == conflict.TeacherId &&
                lesson.DayIndex == conflict.DayIndex &&
                lesson.Period == conflict.Period)?.ClassId;
        }

        var routedToClass = !string.IsNullOrWhiteSpace(classId);
        if (routedToClass)
        {
            ViewModel.SearchText = string.Empty;
            ViewModel.ViewMode = 0;
            ViewModel.SelectedClass = ViewModel.FilteredClasses
                .FirstOrDefault(item => item.Class.Id == classId);
            BuildScheduleGrid();
        }
        else if (!string.IsNullOrWhiteSpace(conflict.TeacherId))
        {
            ViewModel.ViewMode = 1;
            ViewModel.SelectedTeacher = ViewModel.TeacherItems
                .FirstOrDefault(item => item.Teacher.Id == conflict.TeacherId);
            BuildScheduleGrid();
        }
        else
        {
            ViewModel.SetStatus("这条冲突没有可定位的班级或教师，请在冲突说明中核对。", true);
            return;
        }

        if (!string.IsNullOrWhiteSpace(classId) && conflict.DayIndex >= 1 && conflict.Period >= 1)
        {
            var tag = new ScheduleCellTag(classId, conflict.DayIndex, conflict.Period);
            if (TrySelectScheduleCell(tag))
            {
                ViewModel.SetStatus(
                    $"已定位到 {WeaveDefaults.SlotText(conflict.DayIndex, conflict.Period, conflict.Period)} 的冲突格。");
                return;
            }

            ViewModel.SetStatus("已定位到相关班级，但对应课程格不在当前视图里。");
            return;
        }

        // 没有班级可定位的冲突走教师视角；带节次但反查不到课时，说明该时段没有这位教师的课。
        ViewModel.SetStatus(routedToClass
            ? "这条冲突没有具体节次，已定位到相关班级；请按说明核对。"
            : conflict.DayIndex >= 1
                ? "已定位到相关教师；该时段没有这位教师的课程，请按说明核对。"
                : "这条冲突没有具体节次，已定位到相关教师；请按说明核对。");
    }

    private bool TrySelectScheduleCell(ScheduleCellTag tag)
    {
        if (ViewModel.IsMasterView) return false;

        foreach (var child in ScheduleGridHost.Children)
        {
            if (child is not Border { Tag: ScheduleCellTag candidate } border) continue;
            if (candidate != tag) continue;

            SelectCell(tag, border);
            border.BringIntoView();
            return true;
        }

        return false;
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasProject || ViewModel.IsBusy) return;

        var dialog = new ExportOptionsWindow(
            ViewModel.Project,
            ViewModel.SelectedClass?.Class,
            ViewModel.SelectedTeacher?.Teacher,
            ViewModel.IsTeacherView);
        var request = await dialog.ShowDialog<WeaveExportRequest?>(this);
        if (request is null) return;

        switch (request.Format)
        {
            case WeaveExportFormat.Excel:
                await ExportExcelAsync(request);
                break;
            case WeaveExportFormat.Png:
                await ExportPngAsync(request);
                break;
            case WeaveExportFormat.Pdf:
                await ExportPdfAsync(request);
                break;
        }
    }

    private async Task ExportExcelAsync(WeaveExportRequest request)
    {
        var path = await PickSaveAsync("导出课表 Excel", SafeFileName(".xlsx"), "xlsx",
            new FilePickerFileType("Excel 工作簿") { Patterns = new[] { "*.xlsx" } });
        if (path is null) return;

        if (IsSamePath(path, ViewModel.Project.SourceFile))
        {
            ViewModel.SetStatus("导出路径不能覆盖已导入的课表文件。", true);
            return;
        }

        ViewModel.IsBusy = true;
        try
        {
            var project = ViewModel.Project;
            var conflicts = ViewModel.ConflictModels;
            var options = request.Options;
            await Task.Run(() => new WeaveExcelExporter().Export(project, conflicts, path, options));
            var style = options.Style == WeaveExportStyle.Reference ? "参考校样式" : "软件自带样式";
            ViewModel.SetStatus($"已导出 Excel 课表（{style}）。");
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

    private async Task ExportPngAsync(WeaveExportRequest request)
    {
        if (request.ClassId is null && request.TeacherId is null)
        {
            ViewModel.SetStatus("请先选择一个班级或教师，再导出图片。", true);
            return;
        }

        var path = await PickSaveAsync("导出课表图片", SafeFileName($"-{request.TargetLabel}.png"), "png",
            new FilePickerFileType("PNG 图片") { Patterns = new[] { "*.png" } });
        if (path is null) return;

        if (IsSamePath(path, ViewModel.Project.SourceFile))
        {
            ViewModel.SetStatus("导出路径不能覆盖已导入的课表文件。", true);
            return;
        }

        ViewModel.IsBusy = true;
        try
        {
            var project = ViewModel.Project;
            var classId = request.ClassId;
            var teacherId = request.TeacherId;
            var bytes = await Task.Run(() => teacherId is not null
                ? new WeaveImageRenderer().RenderTeacherPng(project, teacherId, 2)
                : new WeaveImageRenderer().RenderClassPng(project, classId!, 2));
            await File.WriteAllBytesAsync(path, bytes);
            ViewModel.SetStatus("已导出课表图片。");
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

    private async Task ExportPdfAsync(WeaveExportRequest request)
    {
        if (request.ClassId is null && request.TeacherId is null)
        {
            ViewModel.SetStatus("请先选择一个班级或教师，再打印 PDF。", true);
            return;
        }

        var path = await PickSaveAsync("打印课表 PDF", SafeFileName($"-{request.TargetLabel}.pdf"), "pdf",
            new FilePickerFileType("PDF 文件") { Patterns = new[] { "*.pdf" } });
        if (path is null) return;

        if (IsSamePath(path, ViewModel.Project.SourceFile))
        {
            ViewModel.SetStatus("打印路径不能覆盖已导入的课表文件。", true);
            return;
        }

        ViewModel.IsBusy = true;
        try
        {
            var project = ViewModel.Project;
            var classId = request.ClassId;
            var teacherId = request.TeacherId;
            await Task.Run(() =>
            {
                var exporter = new WeavePdfExporter();
                if (teacherId is not null) exporter.ExportTeacherPdf(project, teacherId, path);
                else exporter.ExportClassPdf(project, classId!, path);
            });
            ViewModel.SetStatus("已生成打印 PDF。");
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

    // ----------------------------- 拖放导入 -----------------------------

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && !ViewModel.IsBusy
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel.IsBusy) return;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;

        var path = files.Select(GetLocalPath).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
        if (string.IsNullOrWhiteSpace(path)) return;

        var extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase))
        {
            ViewModel.SetStatus("织序支持 .xlsx 与 .xls 工作簿，其他格式请先在 Excel 中另存。", true);
            return;
        }

        await ImportProjectAsync(path!);
    }

    private static string? GetLocalPath(IStorageItem? item)
    {
        if (item is null) return null;

        var path = item.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            if (item.Path.IsAbsoluteUri && item.Path.IsFile)
                path = item.Path.LocalPath;
            else if (item.Path.IsAbsoluteUri)
                path = Uri.UnescapeDataString(item.Path.AbsolutePath);
            else
                path = item.Path.ToString();
        }

        return path?.Normalize(System.Text.NormalizationForm.FormC);
    }

    // ----------------------------- 辅助 -----------------------------

    private void OnRemoveLayer(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is LayerEditViewModel layer)
            ViewModel.RemoveLayer(layer);
    }

    private static Border HeaderCell(string text, bool mono)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = mono ? 9 : 10,
            Foreground = mono ? MutedBrush : InkSoftBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (mono) label.FontFamily = MonoFamily;

        return new Border
        {
            Background = PaperDeepBrush,
            Child = label,
        };
    }

    private static Border SmallHeaderCell(string text)
    {
        return new Border
        {
            Background = PaperDeepBrush,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 8,
                Foreground = MutedBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private async Task<string?> PickOpenAsync(string title, FilePickerFileType fileType)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new[] { fileType },
            });

            return files.Count == 0 ? null : files[0].TryGetLocalPath();
        }
        catch (Exception ex)
        {
            Report(ex, "选择文件失败");
            return null;
        }
    }

    private async Task<string?> PickSaveAsync(
        string title,
        string suggestedName,
        string extension,
        FilePickerFileType fileType)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                DefaultExtension = extension,
                FileTypeChoices = new[] { fileType },
                ShowOverwritePrompt = true,
            });

            return file?.TryGetLocalPath();
        }
        catch (Exception ex)
        {
            Report(ex, "选择保存位置失败");
            return null;
        }
    }

    private string SafeFileName(string suffix)
    {
        var name = string.IsNullOrWhiteSpace(ViewModel.Project.GradeName)
            ? "织序课表"
            : ViewModel.Project.GradeName + "课表";
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '-');

        return name + suffix;
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
        if (ex is GradeWeaveException)
        {
            ViewModel.SetStatus(ex.Message, true);
            return;
        }

        ViewModel.SetStatus($"{prefix}：{ex.Message}", true);
    }
}
