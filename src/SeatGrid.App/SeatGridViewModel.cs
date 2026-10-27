using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SeatGrid.Core.Export;
using SeatGrid.Core.Models;
using SeatGrid.Core.Rendering;
using SeatGrid.Core.Roster;
using SeatGrid.Core.Seating;

namespace SeatGrid.App;

public partial class SeatGridViewModel : ObservableObject
{
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#5A7A4B"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#C6613F"));

    private readonly List<StudentViewModel> _students = new();
    private readonly Dictionary<SeatPosition, StudentViewModel> _assignments = new();
    private readonly Dictionary<SeatPosition, SeatCellViewModel> _seats = new();
    private bool _suppressRebuild;
    private bool _interactiveLayoutEdit;
    private bool _structureDirty;
    private bool _scaleDirty;
    private bool _layoutRefreshQueued;

    // 画刷是绑定高频读取的属性，按颜色缓存，颜色变化时统一失效。
    private IBrush? _seatBackgroundBrush;
    private IBrush? _emptySeatBrush;
    private IBrush? _seatTextBrush;
    private IBrush? _seatBorderBrush;
    private IBrush? _podiumBackgroundBrush;
    private IBrush? _podiumTextBrush;
    private IBrush? _groupLabelBrush;
    private IBrush? _femaleMarkBrush;

    /// <summary>用列表整体替换，避免 3000 条逐项 Add 触发等量集合通知。</summary>
    [ObservableProperty] private IReadOnlyList<StudentViewModel> _filteredStudents = Array.Empty<StudentViewModel>();

    public ObservableCollection<GroupViewModel> Groups { get; } = new();

    public IReadOnlyList<string> ArrangeModes { get; } = new[]
    {
        "名单顺序", "男女交替", "同性同桌", "随机", "按身高", "按视力",
    };

    public IReadOnlyList<string> GenderOptions { get; } = new[] { "未知", "男", "女" };

    public SeatGridViewModel()
    {
        BuildGrid();
        SelectedStudent = null;
    }

    // ----------------------------- 座位表设置 -----------------------------

    [ObservableProperty] private string _title = "班级座位表";
    [ObservableProperty] private bool _boldTitle = true;
    [ObservableProperty] private string _headTeacher = string.Empty;
    [ObservableProperty] private string _monitor = string.Empty;

    [ObservableProperty] private bool _isStudentView;
    [ObservableProperty] private double _groupCount = 4;
    [ObservableProperty] private double _rowCount = 6;
    [ObservableProperty] private double _columnCount = 2;
    [ObservableProperty] private double _cellScale = 85;
    [ObservableProperty] private double _podiumGap = 16;
    [ObservableProperty] private double _nameFontSize = 14;

    [ObservableProperty] private bool _boldNames;
    [ObservableProperty] private bool _hideEmptySeats;
    [ObservableProperty] private bool _markFemales = true;
    [ObservableProperty] private bool _spaceTwoCharNames = true;
    [ObservableProperty] private bool _showGroupRowHints = true;

    [ObservableProperty] private Color _seatBackgroundColor = Color.Parse("#FFFFFF");
    [ObservableProperty] private Color _emptySeatColor = Color.Parse("#FAF9F5");
    [ObservableProperty] private Color _seatTextColor = Color.Parse("#141413");
    [ObservableProperty] private Color _seatBorderColor = Color.Parse("#E7E3D8");
    [ObservableProperty] private Color _podiumBackgroundColor = Color.Parse("#F4F2EC");
    [ObservableProperty] private Color _podiumTextColor = Color.Parse("#85806F");
    [ObservableProperty] private Color _groupLabelColor = Color.Parse("#C6613F");
    [ObservableProperty] private Color _rowLabelColor = Color.Parse("#85806F");
    [ObservableProperty] private Color _femaleMarkColor = Color.Parse("#C6613F");

    [ObservableProperty] private int _arrangeModeIndex;

    [ObservableProperty] private double _podiumWidth = 320;
    [ObservableProperty] private Thickness _podiumTopMargin = new(0);
    [ObservableProperty] private Thickness _podiumBottomMargin = new(0, 16, 0, 0);

    // ----------------------------- 名单、座位状态 -----------------------------

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private StudentViewModel? _selectedStudent;
    [ObservableProperty] private SeatCellViewModel? _selectedSeat;

    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editNumber = string.Empty;
    [ObservableProperty] private int _editGenderIndex;
    [ObservableProperty] private string _editHeight = string.Empty;
    [ObservableProperty] private string _editVision = string.Empty;
    [ObservableProperty] private string _editNotes = string.Empty;

    [ObservableProperty] private string _statusText = "导入名单后即可自动排座。";
    [ObservableProperty] private IBrush _statusBrush = SuccessBrush;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private string _rosterHint = string.Empty;
    [ObservableProperty] private string _importSource = "尚未导入名单";

    // ----------------------------- 计算属性 -----------------------------

    public bool IsIdle => !IsBusy;
    public bool IsTeacherView => !IsStudentView;
    public bool PodiumOnTop => IsStudentView;
    public bool PodiumOnBottom => !IsStudentView;
    public bool HasStudents => _students.Count > 0;
    public bool CanArrange => HasStudents && !IsBusy;
    public bool CanExport => HasStudents && !IsBusy;
    public bool IsChartEmpty => _students.Count == 0;
    public bool HasRosterHint => RosterHint.Length > 0;
    public bool IsRosterEmpty => FilteredStudents.Count == 0;
    public bool ShowGenderLegend => MarkFemales;
    public string RosterEmptyText => _students.Count == 0 ? "还没有名单" : "没有匹配的学生";

    public int StudentCount => _students.Count;
    public int MaleCount => _students.Count(s => s.Model.Gender == Gender.Male);
    public int FemaleCount => _students.Count(s => s.Model.Gender == Gender.Female);
    public int AssignedCount => _assignments.Count;
    public int UnassignedCount => _students.Count(s => !s.IsAssigned);
    public int SeatCount => CurrentLayout.SeatCount;

    public string RosterStats => $"男 {MaleCount} · 女 {FemaleCount} · 未排 {UnassignedCount}";
    public string SeatStats => $"已排 {AssignedCount} / {SeatCount} 座";

    public string InfoLine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(HeadTeacher)) parts.Add($"班主任：{HeadTeacher.Trim()}");
            parts.Add($"人数：{StudentCount}");
            if (!string.IsNullOrWhiteSpace(Monitor)) parts.Add($"班长：{Monitor.Trim()}");
            return string.Join("　　", parts);
        }
    }

    public string GroupCountLabel => $"{Math.Clamp((int)Math.Round(GroupCount), 1, 8)}";
    public string RowCountLabel => $"{Math.Clamp((int)Math.Round(RowCount), 1, 10)}";
    public string ColumnCountLabel => $"{Math.Clamp((int)Math.Round(ColumnCount), 1, 4)}";
    public string CellSizeLabel => $"{CellSizeValue} × {CellSizeValue}px";
    public string PodiumGapLabel => $"{(int)Math.Round(PodiumGap)}px";
    public string NameFontSizeLabel => $"{(int)Math.Round(NameFontSize)}px";

    public FontWeight TitleWeight => BoldTitle ? FontWeight.Bold : FontWeight.Normal;

    public string SelectedSeatLabel =>
        SelectedSeat is null ? "未选座位" : $"已选：{SelectedSeat.PositionLabel}";

    public bool CanAssign => SelectedStudent is not null && SelectedSeat is not null;
    public bool CanClearSeat => SelectedSeat is not null;

    public IBrush SeatBackgroundBrush => _seatBackgroundBrush ??= new SolidColorBrush(SeatBackgroundColor);
    public IBrush EmptySeatBrush => _emptySeatBrush ??= new SolidColorBrush(EmptySeatColor);
    public IBrush SeatTextBrush => _seatTextBrush ??= new SolidColorBrush(SeatTextColor);
    public IBrush SeatBorderBrush => _seatBorderBrush ??= new SolidColorBrush(SeatBorderColor);
    public IBrush PodiumBackgroundBrush => _podiumBackgroundBrush ??= new SolidColorBrush(PodiumBackgroundColor);
    public IBrush PodiumTextBrush => _podiumTextBrush ??= new SolidColorBrush(PodiumTextColor);
    public IBrush GroupLabelBrush => _groupLabelBrush ??= new SolidColorBrush(GroupLabelColor);
    public IBrush FemaleMarkBrush => _femaleMarkBrush ??= new SolidColorBrush(FemaleMarkColor);

    private SeatChartLayout CurrentLayout => new(
        Math.Clamp((int)Math.Round(GroupCount), 1, 8),
        Math.Clamp((int)Math.Round(RowCount), 1, 10),
        Math.Clamp((int)Math.Round(ColumnCount), 1, 4));

    private int CellSizeValue => Math.Max(40, (int)Math.Round(SeatChartSettings.BaseCellSize * (CellScale / 100.0)));

    private int FontSizeValue => Math.Clamp((int)Math.Round(NameFontSize), 10, 36);

    // ----------------------------- 设置变更 -----------------------------

    /// <summary>
    /// 滑块按下时进入交互编辑。压测（P1-C，2026-10-27）实测 130 人名单 / 96 座（96 人入座）：
    /// 尺寸刷新 p50 2.2ms / p95 3.1ms，BuildGrid p50 2.2ms / p95 2.8ms，
    /// 远低于 8ms 阈值——拖动中实时跟随的代价可以接受，不再悬置到松手。
    /// 中间刷新按输入批合并（同批多次改动只刷一次），收尾时再做一次确定性刷新。
    /// </summary>
    public void BeginInteractiveLayoutChange()
    {
        _interactiveLayoutEdit = true;
        _structureDirty = false;
        _scaleDirty = false;
        _layoutRefreshQueued = false;
    }

    public void EndInteractiveLayoutChange()
    {
        if (!_interactiveLayoutEdit) return;
        _interactiveLayoutEdit = false;
        _layoutRefreshQueued = false; // 待执行的合并刷新由下面的收尾刷新覆盖

        if (_structureDirty)
        {
            _structureDirty = false;
            _scaleDirty = false;
            BuildGrid();
            NotifyCounts();
            return;
        }

        if (_scaleDirty)
        {
            _scaleDirty = false;
            RefreshSeatVisuals();
            UpdatePodium();
        }
    }

    /// <summary>拖动中的合并刷新：同一输入批内的多次改动只排一次后台优先级刷新。</summary>
    private void QueueInteractiveRefresh(bool structure)
    {
        if (!_interactiveLayoutEdit) return;
        if (structure) _structureDirty = true;
        else _scaleDirty = true;
        if (_layoutRefreshQueued) return;

        _layoutRefreshQueued = true;
        Dispatcher.UIThread.Post(ApplyInteractiveRefresh, DispatcherPriority.Background);
    }

    private void ApplyInteractiveRefresh()
    {
        _layoutRefreshQueued = false;
        if (!_interactiveLayoutEdit) return; // 松手后的收尾刷新已覆盖

        if (_structureDirty)
        {
            _structureDirty = false;
            _scaleDirty = false;
            BuildGrid();
            NotifyCounts();
            return;
        }

        if (_scaleDirty)
        {
            _scaleDirty = false;
            RefreshSeatVisuals();
            UpdatePodium();
        }
    }

    partial void OnGroupCountChanged(double value)
    {
        if (_suppressRebuild) return;
        OnPropertyChanged(nameof(GroupCountLabel));
        if (_interactiveLayoutEdit)
        {
            QueueInteractiveRefresh(structure: true);
            return;
        }
        BuildGrid();
        NotifyCounts();
    }

    partial void OnRowCountChanged(double value)
    {
        if (_suppressRebuild) return;
        OnPropertyChanged(nameof(RowCountLabel));
        if (_interactiveLayoutEdit)
        {
            QueueInteractiveRefresh(structure: true);
            return;
        }
        BuildGrid();
        NotifyCounts();
    }

    partial void OnColumnCountChanged(double value)
    {
        if (_suppressRebuild) return;
        OnPropertyChanged(nameof(ColumnCountLabel));
        if (_interactiveLayoutEdit)
        {
            QueueInteractiveRefresh(structure: true);
            return;
        }
        BuildGrid();
        NotifyCounts();
    }

    partial void OnIsStudentViewChanged(bool value)
    {
        if (_suppressRebuild) return;
        OnPropertyChanged(nameof(IsTeacherView));
        OnPropertyChanged(nameof(PodiumOnTop));
        OnPropertyChanged(nameof(PodiumOnBottom));
        BuildGrid();
    }

    partial void OnCellScaleChanged(double value)
    {
        if (_suppressRebuild) return;
        OnPropertyChanged(nameof(CellSizeLabel));
        if (_interactiveLayoutEdit)
        {
            QueueInteractiveRefresh(structure: false);
            return;
        }
        RefreshSeatVisuals();
        UpdatePodium();
    }

    partial void OnPodiumGapChanged(double value)
    {
        OnPropertyChanged(nameof(PodiumGapLabel));
        UpdatePodium();
    }

    partial void OnNameFontSizeChanged(double value)
    {
        OnPropertyChanged(nameof(NameFontSizeLabel));
        RefreshSeatVisuals();
    }

    partial void OnBoldNamesChanged(bool value) => RefreshSeatVisuals();

    partial void OnHideEmptySeatsChanged(bool value) => RefreshSeatVisuals();

    partial void OnMarkFemalesChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGenderLegend));
        RefreshSeatVisuals();
    }

    partial void OnSpaceTwoCharNamesChanged(bool value) => RefreshSeatVisuals();

    partial void OnShowGroupRowHintsChanged(bool value)
    {
        if (_suppressRebuild) return;
        BuildGrid();
        UpdatePodium();
    }

    partial void OnBoldTitleChanged(bool value) => OnPropertyChanged(nameof(TitleWeight));

    partial void OnHeadTeacherChanged(string value) => OnPropertyChanged(nameof(InfoLine));

    partial void OnMonitorChanged(string value) => OnPropertyChanged(nameof(InfoLine));

    partial void OnSeatBackgroundColorChanged(Color value) => ColorChanged();

    partial void OnEmptySeatColorChanged(Color value) => ColorChanged();

    partial void OnSeatTextColorChanged(Color value) => ColorChanged();

    partial void OnSeatBorderColorChanged(Color value) => ColorChanged();

    partial void OnPodiumBackgroundColorChanged(Color value) => ColorChanged();

    partial void OnPodiumTextColorChanged(Color value) => ColorChanged();

    partial void OnGroupLabelColorChanged(Color value) => ColorChanged();

    partial void OnRowLabelColorChanged(Color value) => ColorChanged();

    partial void OnFemaleMarkColorChanged(Color value) => ColorChanged();

    partial void OnSearchTextChanged(string value) => RefreshStudents();

    partial void OnSelectedStudentChanged(StudentViewModel? value)
    {
        if (value is null)
        {
            EditName = string.Empty;
            EditNumber = string.Empty;
            EditGenderIndex = 0;
            EditHeight = string.Empty;
            EditVision = string.Empty;
            EditNotes = string.Empty;
        }
        else
        {
            EditName = value.Model.Name;
            EditNumber = value.Model.Number;
            EditGenderIndex = value.Model.Gender switch
            {
                Gender.Male => 1,
                Gender.Female => 2,
                _ => 0,
            };
            EditHeight = value.Model.Height?.ToString("0.#", CultureInfo.InvariantCulture) ?? string.Empty;
            EditVision = value.Model.Vision?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty;
            EditNotes = value.Model.Notes;
        }

        OnPropertyChanged(nameof(CanAssign));
        OnPropertyChanged(nameof(HasSelectedStudent));
        OnPropertyChanged(nameof(EditPanelTitle));
    }

    partial void OnSelectedSeatChanged(SeatCellViewModel? value)
    {
        foreach (var seat in _seats.Values) seat.SetSelected(seat == value);
        OnPropertyChanged(nameof(SelectedSeatLabel));
        OnPropertyChanged(nameof(CanAssign));
        OnPropertyChanged(nameof(CanClearSeat));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanArrange));
        OnPropertyChanged(nameof(CanExport));
    }

    partial void OnRosterHintChanged(string value) => OnPropertyChanged(nameof(HasRosterHint));

    private void ColorChanged()
    {
        _seatBackgroundBrush = null;
        _emptySeatBrush = null;
        _seatTextBrush = null;
        _seatBorderBrush = null;
        _podiumBackgroundBrush = null;
        _podiumTextBrush = null;
        _groupLabelBrush = null;
        _femaleMarkBrush = null;

        OnPropertyChanged(nameof(SeatBackgroundBrush));
        OnPropertyChanged(nameof(EmptySeatBrush));
        OnPropertyChanged(nameof(SeatTextBrush));
        OnPropertyChanged(nameof(SeatBorderBrush));
        OnPropertyChanged(nameof(PodiumBackgroundBrush));
        OnPropertyChanged(nameof(PodiumTextBrush));
        OnPropertyChanged(nameof(GroupLabelBrush));
        OnPropertyChanged(nameof(FemaleMarkBrush));
        RefreshSeatVisuals();
    }

    // ----------------------------- 构建网格 -----------------------------

    private void BuildGrid()
    {
        SelectedSeat = null;

        var layout = CurrentLayout;
        var previous = new Dictionary<SeatPosition, StudentViewModel>(_assignments);

        _assignments.Clear();
        foreach (var (position, student) in previous)
        {
            if (layout.Contains(position)) _assignments[position] = student;
        }

        _seats.Clear();
        Groups.Clear();

        var teacher = !IsStudentView;
        var groupNumbers = teacher
            ? Enumerable.Range(0, layout.Groups)
            : Enumerable.Range(0, layout.Groups).Reverse();

        var rowNumbers = teacher
            ? Enumerable.Range(0, layout.Rows).Reverse()
            : Enumerable.Range(0, layout.Rows);

        foreach (var group in groupNumbers)
        {
            var rows = new List<RowViewModel>();

            foreach (var row in rowNumbers)
            {
                var seats = new List<SeatCellViewModel>();
                for (var column = 0; column < layout.Columns; column++)
                {
                    var seat = new SeatCellViewModel(new SeatPosition(group, row, column));
                    _seats[seat.Position] = seat;
                    seats.Add(seat);
                }

                rows.Add(new RowViewModel(row + 1, seats));
            }

            var columnHandles = new List<ColumnHandleViewModel>();
            for (var column = 0; column < layout.Columns; column++)
                columnHandles.Add(new ColumnHandleViewModel(group, column));

            Groups.Add(new GroupViewModel(group + 1, $"第{group + 1}组", rows, columnHandles));
        }

        OnPropertyChanged(nameof(SeatCount));
        OnPropertyChanged(nameof(PodiumOnTop));
        OnPropertyChanged(nameof(PodiumOnBottom));
        RefreshSeatVisuals();
        UpdatePodium();
        NotifyCounts();
    }

    private void RefreshSeatVisuals()
    {
        var size = CellSizeValue;
        var fontSize = (double)FontSizeValue;
        var seatBackground = SeatBackgroundBrush;
        var emptyBackground = EmptySeatBrush;
        var nameColor = SeatTextBrush;
        var femaleColor = MarkFemales ? FemaleMarkBrush : SeatTextBrush;
        var border = SeatBorderBrush;

        foreach (var seat in _seats.Values)
        {
            _assignments.TryGetValue(seat.Position, out var student);
            var name = student is null ? string.Empty : SeatChartComposer.DisplayName(student.Model.Name, SpaceTwoCharNames);

            seat.Update(
                name,
                student is null,
                student?.Model.Gender == Gender.Female,
                seat == SelectedSeat,
                size,
                fontSize,
                BoldNames,
                seatBackground,
                emptyBackground,
                nameColor,
                femaleColor,
                border,
                HideEmptySeats);
        }

        var reverse = new Dictionary<Guid, SeatPosition>();
        foreach (var (position, student) in _assignments) reverse[student.Id] = position;

        foreach (var student in _students)
        {
            student.SetSeat(reverse.TryGetValue(student.Id, out var position) ? position : null);
        }

        var groupIndex = 0;
        foreach (var group in Groups)
        {
            group.LabelBrush = GroupLabelBrush;
            group.ShowHints = ShowGroupRowHints;
            group.LabelMargin = ShowGroupRowHints && groupIndex == 0
                ? new Thickness(44, 0, 0, 5)
                : new Thickness(0, 0, 0, 5);
            group.ShowRowSpacer = groupIndex == 0;
            foreach (var row in group.Rows)
                row.ShowHandle = groupIndex == 0;
            foreach (var handle in group.ColumnHandles)
                handle.Width = size;

            groupIndex++;
        }

        NotifyCounts();
    }

    private void UpdatePodium()
    {
        var layout = CurrentLayout;
        var groupWidth = layout.Columns * CellSizeValue + (layout.Columns - 1) * 6;
        var gridWidth = layout.Groups * groupWidth + (layout.Groups - 1) * 22;
        var contentWidth = gridWidth + 44; // 行首把手列固定占位

        PodiumWidth = Math.Min(contentWidth * 0.55, 360);
        PodiumTopMargin = new Thickness(0, 0, 0, PodiumGap);
        PodiumBottomMargin = new Thickness(0, PodiumGap, 0, 0);
        OnPropertyChanged(nameof(PodiumOnTop));
    }

    // ----------------------------- 名单 -----------------------------

    public void LoadRoster(RosterImportResult result)
    {
        _suppressRebuild = true;
        _students.Clear();
        _assignments.Clear();
        SelectedStudent = null;
        SelectedSeat = null;
        _suppressRebuild = false;

        foreach (var student in result.Students)
            _students.Add(new StudentViewModel(student));

        ImportSource = result.SourceName;
        RosterHint = string.Join("；", result.Warnings);

        if (string.IsNullOrWhiteSpace(Title) || Title == "班级座位表")
            Title = DeriveTitle(result.SourceName);

        BuildGrid();
        AutoArrange();
        RefreshStudents();
        NotifyCounts();

        SetStatus(RosterHint.Length > 0
            ? $"已识别 {_students.Count} 人 · {RosterHint}"
            : $"已识别 {_students.Count} 人 · 男 {MaleCount} / 女 {FemaleCount}");
    }

    private static string DeriveTitle(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        foreach (var suffix in new[] { "学生名单", "花名册", "名单", "座位表" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
                name = name[..^suffix.Length];
        }

        name = name.Trim();
        return string.IsNullOrEmpty(name) ? "班级座位表" : $"{name}座位表";
    }

    private void RefreshStudents()
    {
        var selected = SelectedStudent;
        var keyword = SearchText.Trim().ToLowerInvariant();

        var matches = new List<StudentViewModel>(_students.Count);
        foreach (var student in _students)
        {
            if (keyword.Length == 0 || student.SearchKey.Contains(keyword))
                matches.Add(student);
        }

        FilteredStudents = matches;

        if (selected is not null)
            SelectedStudent = matches.Contains(selected) ? selected : null;

        OnPropertyChanged(nameof(IsRosterEmpty));
        OnPropertyChanged(nameof(RosterEmptyText));
    }

    public void AutoArrange()
    {
        if (_students.Count == 0)
        {
            SetStatus("先导入一份名单，再自动排座。", true);
            return;
        }

        var mode = (ArrangeMode)Math.Clamp(ArrangeModeIndex, 0, 5);
        var models = _students.Select(s => s.Model).ToList();
        var arranged = SeatingEngine.Arrange(models, CurrentLayout, mode);
        var byId = _students.ToDictionary(s => s.Model.Id);

        _assignments.Clear();
        foreach (var (position, model) in arranged)
        {
            if (byId.TryGetValue(model.Id, out var student))
                _assignments[position] = student;
        }

        RefreshSeatVisuals();
        SetStatus($"已按「{ArrangeModes[ArrangeModeIndex]}」排座：{AssignedCount} 人 / {SeatCount} 座");
    }

    public void ClearSeats()
    {
        _assignments.Clear();
        RefreshSeatVisuals();
        SetStatus("已清空座位，可以重新排座或手动放置。");
    }

    public void SelectSeat(SeatCellViewModel seat)
    {
        SelectedSeat = SelectedSeat == seat ? null : seat;
    }

    public void AssignSelectedStudent()
    {
        if (SelectedStudent is null || SelectedSeat is null)
        {
            SetStatus("先选中一个座位，再从左侧名单选中学生。", true);
            return;
        }

        var student = SelectedStudent;
        var target = SelectedSeat;

        foreach (var pair in _assignments.Where(kv => kv.Value == student).ToList())
            _assignments.Remove(pair.Key);

        _assignments[target.Position] = student;
        RefreshSeatVisuals();
        SetStatus($"已把 {student.Name} 放到 {target.PositionLabel}。");
    }

    /// <summary>拖拽落座：目标有人则交换，目标为空则移动。</summary>
    public void MoveSeatStudent(SeatCellViewModel source, SeatCellViewModel target)
    {
        if (source == target) return;
        if (!_assignments.TryGetValue(source.Position, out var moving)) return;

        if (_assignments.TryGetValue(target.Position, out var occupying))
        {
            _assignments[target.Position] = moving;
            _assignments[source.Position] = occupying;
            RefreshSeatVisuals();
            SetStatus($"已把 {moving.Name} 与 {occupying.Name} 交换座位。");
        }
        else
        {
            _assignments.Remove(source.Position);
            _assignments[target.Position] = moving;
            RefreshSeatVisuals();
            SetStatus($"已把 {moving.Name} 移到 {target.PositionLabel}。");
        }
    }

    public void ClearSelectedSeat()
    {
        if (SelectedSeat is null) return;

        var label = SelectedSeat.PositionLabel;
        _assignments.Remove(SelectedSeat.Position);
        RefreshSeatVisuals();
        SetStatus($"已清空 {label}。");
    }

    // ----------------------------- 行列调整 -----------------------------

    /// <summary>所有座位视图模型，供拖拽高亮遍历。</summary>
    public IEnumerable<SeatCellViewModel> Seats => _seats.Values;

    /// <summary>整班座位沿当前视角平移一格：dx 向右、dy 向下，超出边缘自动绕回。</summary>
    public void ShiftAllSeats(int dx, int dy)
    {
        if (_assignments.Count == 0)
        {
            SetStatus("还没有已安排的座位，先自动排座或手动放入。", true);
            return;
        }

        var layout = CurrentLayout;
        var width = layout.Groups * layout.Columns;
        var shifted = new Dictionary<SeatPosition, StudentViewModel>(_assignments.Count);

        foreach (var (position, student) in _assignments)
        {
            // 教师视角第 1 组在最左、第一排靠下；学生视角整体旋转 180°。平移按屏幕方向换算。
            var displayColumn = IsStudentView
                ? (layout.Groups - 1 - position.Group) * layout.Columns + position.Column
                : position.Group * layout.Columns + position.Column;
            var displayRow = IsStudentView ? position.Row : layout.Rows - 1 - position.Row;

            displayColumn = ((displayColumn + dx) % width + width) % width;
            displayRow = ((displayRow + dy) % layout.Rows + layout.Rows) % layout.Rows;

            var group = IsStudentView
                ? layout.Groups - 1 - displayColumn / layout.Columns
                : displayColumn / layout.Columns;
            var column = displayColumn % layout.Columns;
            var row = IsStudentView ? displayRow : layout.Rows - 1 - displayRow;

            shifted[new SeatPosition(group, row, column)] = student;
        }

        _assignments.Clear();
        foreach (var (position, student) in shifted)
            _assignments[position] = student;

        RefreshSeatVisuals();
        var direction = dy < 0 ? "上" : dy > 0 ? "下" : dx < 0 ? "左" : "右";
        SetStatus($"已整体向{direction}平移一格（边缘自动绕回）。");
    }

    /// <summary>整排对调：两排在所有小组、所有列上互换学生。</summary>
    public void SwapRows(int rowA, int rowB)
    {
        var layout = CurrentLayout;
        if (rowA == rowB || rowA < 0 || rowB < 0 || rowA >= layout.Rows || rowB >= layout.Rows) return;
        if (_assignments.Count == 0)
        {
            SetStatus("还没有已安排的座位。", true);
            return;
        }

        for (var group = 0; group < layout.Groups; group++)
            for (var column = 0; column < layout.Columns; column++)
                SwapAssignments(new SeatPosition(group, rowA, column), new SeatPosition(group, rowB, column));

        RefreshSeatVisuals();
        SetStatus($"已把第 {Math.Min(rowA, rowB) + 1} 排与第 {Math.Max(rowA, rowB) + 1} 排整排对调。");
    }

    /// <summary>整列对调：两列在所有排上互换学生，可以跨小组。</summary>
    public void SwapColumns(int groupA, int columnA, int groupB, int columnB)
    {
        var layout = CurrentLayout;
        if (groupA == groupB && columnA == columnB) return;
        if (groupA < 0 || groupB < 0 || groupA >= layout.Groups || groupB >= layout.Groups ||
            columnA < 0 || columnB < 0 || columnA >= layout.Columns || columnB >= layout.Columns) return;
        if (_assignments.Count == 0)
        {
            SetStatus("还没有已安排的座位。", true);
            return;
        }

        for (var row = 0; row < layout.Rows; row++)
            SwapAssignments(new SeatPosition(groupA, row, columnA), new SeatPosition(groupB, row, columnB));

        RefreshSeatVisuals();
        SetStatus(columnA == columnB
            ? $"已把第 {Math.Min(groupA, groupB) + 1} 组与第 {Math.Max(groupA, groupB) + 1} 组的第 {columnA + 1} 列整列对调。"
            : $"已把第 {groupA + 1} 组第 {columnA + 1} 列与第 {groupB + 1} 组第 {columnB + 1} 列整列对调。");
    }

    private void SwapAssignments(SeatPosition a, SeatPosition b)
    {
        _assignments.TryGetValue(a, out var studentA);
        _assignments.TryGetValue(b, out var studentB);

        if (studentB is not null) _assignments[a] = studentB; else _assignments.Remove(a);
        if (studentA is not null) _assignments[b] = studentA; else _assignments.Remove(b);
    }

    public bool HasSelectedStudent => SelectedStudent is not null;
    public string EditPanelTitle => SelectedStudent is null ? "新增学生" : $"编辑学生 · {SelectedStudent.Name}";

    public void ApplyStudentEdit()
    {
        if (SelectedStudent is null)
        {
            SetStatus("先在名单里选中一名学生。", true);
            return;
        }

        var name = EditName.Trim();
        if (name.Length == 0)
        {
            SetStatus("姓名不能为空。", true);
            return;
        }

        var model = SelectedStudent.Model;
        model.Name = name;
        model.Number = EditNumber.Trim();
        model.Gender = EditGenderIndex switch
        {
            1 => Gender.Male,
            2 => Gender.Female,
            _ => Gender.Unknown,
        };
        model.Height = ParseOptional(EditHeight);
        model.Vision = ParseOptional(EditVision);
        model.Notes = EditNotes.Trim();

        SelectedStudent.Refresh();
        RefreshSeatVisuals();
        RefreshStudents();
        SetStatus($"已保存 {name} 的信息。");
    }

    public void AddStudent()
    {
        var name = EditName.Trim();
        if (name.Length == 0)
        {
            SetStatus("先填写姓名，再点「新增学生」。", true);
            return;
        }

        var student = new StudentViewModel(new Student
        {
            Name = name,
            Number = EditNumber.Trim(),
            Gender = EditGenderIndex switch
            {
                1 => Gender.Male,
                2 => Gender.Female,
                _ => Gender.Unknown,
            },
            Height = ParseOptional(EditHeight),
            Vision = ParseOptional(EditVision),
            Notes = EditNotes.Trim(),
        });
        _students.Add(student);
        RefreshStudents();
        SelectedStudent = student;
        NotifyCounts();
        SetStatus($"已把 {name} 加入名单。");
    }

    public void DeleteSelectedStudent()
    {
        if (SelectedStudent is null)
        {
            SetStatus("先选中要删除的学生。", true);
            return;
        }

        var student = SelectedStudent;
        foreach (var pair in _assignments.Where(kv => kv.Value == student).ToList())
            _assignments.Remove(pair.Key);

        _students.Remove(student);
        SelectedStudent = null;
        RefreshSeatVisuals();
        RefreshStudents();
        SetStatus($"已从名单中删除 {student.Name}。");
    }

    private static double? ParseOptional(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    // ----------------------------- 配置 / 导出 -----------------------------

    public void ResetStyle()
    {
        SeatBackgroundColor = Color.Parse("#FFFFFF");
        EmptySeatColor = Color.Parse("#FAF9F5");
        SeatTextColor = Color.Parse("#141413");
        SeatBorderColor = Color.Parse("#E7E3D8");
        PodiumBackgroundColor = Color.Parse("#F4F2EC");
        PodiumTextColor = Color.Parse("#85806F");
        GroupLabelColor = Color.Parse("#C6613F");
        RowLabelColor = Color.Parse("#85806F");
        FemaleMarkColor = Color.Parse("#C6613F");
        NameFontSize = 14;
        BoldNames = false;
        HideEmptySeats = false;
        MarkFemales = true;
        SpaceTwoCharNames = true;
        ShowGroupRowHints = true;
        PodiumGap = 16;
        SetStatus("座位表样式已重置。");
    }

    public SeatChartSettings ToSettings() => new()
    {
        Title = Title,
        BoldTitle = BoldTitle,
        HeadTeacher = HeadTeacher,
        Monitor = Monitor,
        View = IsStudentView ? ViewMode.Student : ViewMode.Teacher,
        Groups = Math.Clamp((int)Math.Round(GroupCount), 1, 8),
        Rows = Math.Clamp((int)Math.Round(RowCount), 1, 10),
        Columns = Math.Clamp((int)Math.Round(ColumnCount), 1, 4),
        CellScalePercent = Math.Clamp((int)Math.Round(CellScale), 50, 200),
        PodiumGap = Math.Clamp((int)Math.Round(PodiumGap), 0, 100),
        NameFontSize = FontSizeValue,
        BoldNames = BoldNames,
        HideEmptySeats = HideEmptySeats,
        MarkFemales = MarkFemales,
        SpaceTwoCharNames = SpaceTwoCharNames,
        ShowGroupRowHints = ShowGroupRowHints,
        SeatBackground = Hex(SeatBackgroundColor),
        EmptySeatBackground = Hex(EmptySeatColor),
        SeatText = Hex(SeatTextColor),
        SeatBorder = Hex(SeatBorderColor),
        PodiumBackground = Hex(PodiumBackgroundColor),
        PodiumText = Hex(PodiumTextColor),
        GroupLabel = Hex(GroupLabelColor),
        RowLabel = Hex(RowLabelColor),
        FemaleMark = Hex(FemaleMarkColor),
    };

    public SeatChartDocument ComposeDocument() =>
        SeatChartComposer.Compose(ToSettings(), GetCoreAssignments(), _students.Count);

    public Dictionary<SeatPosition, Student> GetCoreAssignments() =>
        _assignments.ToDictionary(kv => kv.Key, kv => kv.Value.Model);

    public List<Student> GetModels() => _students.Select(s => s.Model).ToList();

    public void ApplyConfig(SeatGridConfig config)
    {
        _suppressRebuild = true;
        Title = config.Settings.Title;
        BoldTitle = config.Settings.BoldTitle;
        HeadTeacher = config.Settings.HeadTeacher;
        Monitor = config.Settings.Monitor;
        IsStudentView = config.Settings.View == ViewMode.Student;
        GroupCount = config.Settings.Groups;
        RowCount = config.Settings.Rows;
        ColumnCount = config.Settings.Columns;
        CellScale = config.Settings.CellScalePercent;
        PodiumGap = config.Settings.PodiumGap;
        NameFontSize = config.Settings.NameFontSize;
        BoldNames = config.Settings.BoldNames;
        HideEmptySeats = config.Settings.HideEmptySeats;
        MarkFemales = config.Settings.MarkFemales;
        SpaceTwoCharNames = config.Settings.SpaceTwoCharNames;
        ShowGroupRowHints = config.Settings.ShowGroupRowHints;
        SeatBackgroundColor = Color.Parse(config.Settings.SeatBackground);
        EmptySeatColor = Color.Parse(config.Settings.EmptySeatBackground);
        SeatTextColor = Color.Parse(config.Settings.SeatText);
        SeatBorderColor = Color.Parse(config.Settings.SeatBorder);
        PodiumBackgroundColor = Color.Parse(config.Settings.PodiumBackground);
        PodiumTextColor = Color.Parse(config.Settings.PodiumText);
        GroupLabelColor = Color.Parse(config.Settings.GroupLabel);
        RowLabelColor = Color.Parse(config.Settings.RowLabel);
        FemaleMarkColor = Color.Parse(config.Settings.FemaleMark);
        _suppressRebuild = false;

        _students.Clear();
        _assignments.Clear();
        foreach (var student in config.Students)
            _students.Add(new StudentViewModel(student));

        SelectedStudent = null;
        SelectedSeat = null;
        BuildGrid();

        var byId = _students.ToDictionary(s => s.Model.Id);
        _assignments.Clear();
        foreach (var (position, student) in config.Assignments)
        {
            if (byId.TryGetValue(student.Id, out var vm))
                _assignments[position] = vm;
        }

        ImportSource = "配置文件";
        RosterHint = string.Join("；", config.Warnings);
        RefreshSeatVisuals();
        RefreshStudents();
        NotifyCounts();
        SetStatus(config.Warnings.Count > 0
            ? $"已导入配置 · {string.Join("；", config.Warnings)}"
            : "已导入配置。");
    }

    public void SetStatus(string text, bool error = false)
    {
        StatusText = text;
        StatusBrush = error ? ErrorBrush : SuccessBrush;
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(StudentCount));
        OnPropertyChanged(nameof(MaleCount));
        OnPropertyChanged(nameof(FemaleCount));
        OnPropertyChanged(nameof(AssignedCount));
        OnPropertyChanged(nameof(UnassignedCount));
        OnPropertyChanged(nameof(SeatCount));
        OnPropertyChanged(nameof(RosterStats));
        OnPropertyChanged(nameof(SeatStats));
        OnPropertyChanged(nameof(InfoLine));
        OnPropertyChanged(nameof(HasStudents));
        OnPropertyChanged(nameof(CanArrange));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(IsChartEmpty));
    }
}
