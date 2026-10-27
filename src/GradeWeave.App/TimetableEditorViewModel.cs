using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GradeWeave.Core;
using GradeWeave.Core.Models;
using GradeWeave.Core.Scheduling;
using GradeWeave.Core.Timetable;

namespace GradeWeave.App;

/// <summary>
/// 「作息时间表」编辑窗口的状态。构造时把工程的作息克隆成草稿，
/// 编辑只改草稿；点「确认并应用」时才调用 <see cref="TimetableService.Confirm"/> 写回工程。
/// </summary>
public partial class TimetableEditorViewModel : ObservableObject
{
    private readonly WeaveProject _project;
    private readonly WeaveTimetable _draft;
    private readonly string _initialSnapshot;
    private bool _loading;

    public TimetableEditorViewModel(WeaveProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _draft = TimetableService.Ensure(project).Clone();

        foreach (var kind in Enum.GetValues<TimetableRowKind>())
            KindOptions.Add(new TimetableKindOption(kind));
        foreach (var preset in WeaveTimetables.Presets)
            Presets.Add(new TimetablePresetOption(preset));

        SelectedPreset = Presets.FirstOrDefault();
        SelectedNewKind = KindOptions.FirstOrDefault(option => option.Kind == TimetableRowKind.Lesson);

        ReloadRows(null);
        _initialSnapshot = Snapshot();
    }

    /// <summary>草稿与打开时的作息有差异时为真，关闭窗口前用于询问是否放弃。</summary>
    public bool IsDirty => Snapshot() != _initialSnapshot;

    private string Snapshot() => _draft.Name + "丨" + string.Join("丨", _draft.Rows.Select(row =>
        $"{row.Kind}‖{row.Name}‖{row.StartTime}‖{row.EndTime}"));

    public ObservableCollection<TimetableRowItemViewModel> Rows { get; } = new();
    public ObservableCollection<TimetableKindOption> KindOptions { get; } = new();
    public ObservableCollection<TimetablePresetOption> Presets { get; } = new();
    public ObservableCollection<TimetablePeriodPreviewViewModel> PeriodPreview { get; } = new();
    public IReadOnlyList<TimetableLegendItem> LegendItems => TimetablePalette.Legend;

    /// <summary>时段或时间变化后触发；窗口用它重绘顶部时间轴。</summary>
    public event EventHandler? RowsChanged;

    public string TableName => string.IsNullOrWhiteSpace(_draft.Name) ? "作息时间表" : _draft.Name;

    public string RowCountText
    {
        get
        {
            var lessons = Rows.Count(item => item.Row.Kind == TimetableRowKind.Lesson);
            var extensions = Rows.Count(item => WeaveTimetables.IsExtension(item.Row.Kind));
            return extensions > 0 ? $"{lessons} 节正课 · {extensions} 个扩展" : $"{lessons} 节正课";
        }
    }

    public bool IsEmpty => Rows.Count == 0;
    public bool HasSelection => SelectedRow is not null;
    public bool CanConfirm => !HasErrors && Rows.Count > 0;

    [ObservableProperty] private TimetableRowItemViewModel? _selectedRow;
    [ObservableProperty] private TimetableKindOption? _selectedKind;
    [ObservableProperty] private TimetableKindOption? _selectedNewKind;
    [ObservableProperty] private TimetablePresetOption? _selectedPreset;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _startTime = string.Empty;
    [ObservableProperty] private string _endTime = string.Empty;
    [ObservableProperty] private string _namePlaceholder = string.Empty;
    [ObservableProperty] private string _detailHint = string.Empty;
    [ObservableProperty] private string _validationSummary = "还没有时段";
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private string _warningText = string.Empty;
    [ObservableProperty] private bool _hasErrors;
    [ObservableProperty] private bool _hasWarnings;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private string _previewSummary = "先修正下面的问题";

    partial void OnSelectedRowChanged(TimetableRowItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        LoadDetail(value);
    }

    partial void OnSelectedKindChanged(TimetableKindOption? value)
    {
        if (_loading || SelectedRow is not { } item || value is null) return;

        var row = item.Row;
        row.Kind = value.Kind;
        if (value.Kind == TimetableRowKind.Lesson)
        {
            if (string.IsNullOrWhiteSpace(row.Name)) row.Name = PlaceholderFor(row);
        }
        else if (string.IsNullOrWhiteSpace(row.Name) || LooksAutoLessonName(row.Name))
        {
            row.Name = string.Empty;
        }

        _loading = true;
        Name = row.Name;
        _loading = false;

        UpdateDetailHint();
        Apply();
    }

    partial void OnNameChanged(string value)
    {
        if (_loading || SelectedRow is not { } item) return;
        item.Row.Name = value ?? string.Empty;
        UpdateDetailHint();
        Apply();
    }

    partial void OnStartTimeChanged(string value)
    {
        if (_loading || SelectedRow is not { } item) return;
        item.Row.StartTime = value ?? string.Empty;
        UpdateDetailHint();
        Apply();
    }

    partial void OnEndTimeChanged(string value)
    {
        if (_loading || SelectedRow is not { } item) return;
        item.Row.EndTime = value ?? string.Empty;
        UpdateDetailHint();
        Apply();
    }

    partial void OnHasErrorsChanged(bool value) => OnPropertyChanged(nameof(CanConfirm));

    /// <summary>按左侧下拉选中的类型在列表末尾新增一个时段。</summary>
    public void AddRow()
    {
        var kind = SelectedNewKind?.Kind ?? TimetableRowKind.Lesson;
        var lessonCount = _draft.Rows.Count(row => row.Kind == TimetableRowKind.Lesson);
        var duration = TimetablePalette.DefaultDuration(kind);
        var start = NextStart(kind);
        var end = start + duration;
        if (end > TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59))
        {
            start = TimetablePalette.DefaultStart(kind);
            end = start + duration;
        }

        var row = new TimetableRow
        {
            Kind = kind,
            Name = kind == TimetableRowKind.Lesson
                ? WeaveTimetables.DefaultName(kind, lessonCount)
                : string.Empty,
            StartTime = ActivityResolver.Format(start),
            EndTime = ActivityResolver.Format(end),
        };

        _draft.Rows.Add(row);
        var item = new TimetableRowItemViewModel(row);
        Rows.Add(item);
        SelectedRow = item;
        Apply();
    }

    public void DeleteSelected()
    {
        if (SelectedRow is not { } item) return;

        var index = Rows.IndexOf(item);
        if (index < 0) return;

        Rows.RemoveAt(index);
        _draft.Rows.Remove(item.Row);
        SelectedRow = Rows.Count == 0 ? null : Rows[Math.Clamp(index, 0, Rows.Count - 1)];
        LoadDetail(SelectedRow);
        Apply();
    }

    public void MoveUp()
    {
        if (SelectedRow is not { } item) return;

        var index = Rows.IndexOf(item);
        if (index <= 0) return;

        Rows.Move(index, index - 1);
        SyncDraftOrder();
        Apply();
    }

    public void MoveDown()
    {
        if (SelectedRow is not { } item) return;

        var index = Rows.IndexOf(item);
        if (index < 0 || index >= Rows.Count - 1) return;

        Rows.Move(index, index + 1);
        SyncDraftOrder();
        Apply();
    }

    /// <summary>复制选中时段的类型与时长，排在其后 10 分钟，正课按已有节数重新编号。</summary>
    public void DuplicateSelected()
    {
        if (SelectedRow is not { } item) return;

        var source = item.Row;
        var duration = TimetablePalette.DefaultDuration(source.Kind);
        var start = TimetablePalette.DefaultStart(source.Kind);
        if (ActivityResolver.TryParseTime(source.StartTime, out var sourceStart) &&
            ActivityResolver.TryParseTime(source.EndTime, out var sourceEnd) &&
            sourceEnd > sourceStart)
        {
            duration = sourceEnd - sourceStart;
            start = sourceEnd + TimeSpan.FromMinutes(10);
            if (start > TimeSpan.FromHours(23)) start = TimetablePalette.DefaultStart(source.Kind);
        }

        var copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.StartTime = ActivityResolver.Format(start);
        copy.EndTime = ActivityResolver.Format(start + duration);
        if (copy.Kind == TimetableRowKind.Lesson)
            copy.Name = WeaveTimetables.DefaultName(
                TimetableRowKind.Lesson,
                _draft.Rows.Count(row => row.Kind == TimetableRowKind.Lesson));

        var index = Rows.IndexOf(item);
        _draft.Rows.Insert(index + 1, copy);
        var copyItem = new TimetableRowItemViewModel(copy);
        Rows.Insert(index + 1, copyItem);
        SelectedRow = copyItem;
        Apply();
    }

    public void SortByTime()
    {
        var selectedId = SelectedRow?.Row.Id;
        _draft.Rows = WeaveTimetables.OrderRows(_draft.Rows).ToList();
        ReloadRows(selectedId);
    }

    /// <summary>用内置预设整体替换草稿，窗口保持打开可继续微调。</summary>
    public void ApplyPreset()
    {
        if (SelectedPreset is null) return;

        var preset = WeaveTimetables.FromPreset(SelectedPreset.Preset.Id);
        _draft.Name = preset.Name;
        _draft.Rows = preset.Rows.Select(row => row.Clone()).ToList();
        OnPropertyChanged(nameof(TableName));
        ReloadRows(null);
    }

    /// <summary>校验并把草稿写回工程；失败时返回 null，原因写入 ErrorText。</summary>
    public TimetableConfirmResult? TryConfirm()
    {
        var validation = TimetableService.Validate(_draft);
        if (!validation.IsValid)
        {
            RefreshValidation();
            return null;
        }

        try
        {
            return TimetableService.Confirm(_project, _draft);
        }
        catch (GradeWeaveException ex)
        {
            ErrorText = string.IsNullOrWhiteSpace(ErrorText) ? ex.Message : $"{ErrorText}\n· {ex.Message}";
            return null;
        }
    }

    private void ReloadRows(string? selectId)
    {
        Rows.Clear();
        foreach (var row in _draft.Rows)
            Rows.Add(new TimetableRowItemViewModel(row));

        SelectedRow = Rows.FirstOrDefault(item => item.Row.Id == selectId) ?? Rows.FirstOrDefault();
        LoadDetail(SelectedRow);
        Apply();
    }

    private void SyncDraftOrder()
    {
        _draft.Rows.Clear();
        foreach (var item in Rows) _draft.Rows.Add(item.Row);
    }

    private void LoadDetail(TimetableRowItemViewModel? item)
    {
        _loading = true;
        if (item is null)
        {
            SelectedKind = KindOptions.FirstOrDefault();
            Name = string.Empty;
            StartTime = string.Empty;
            EndTime = string.Empty;
        }
        else
        {
            SelectedKind = KindOptions.FirstOrDefault(option => option.Kind == item.Row.Kind);
            Name = item.Row.Name;
            StartTime = item.Row.StartTime;
            EndTime = item.Row.EndTime;
        }

        _loading = false;
        UpdateDetailHint();
    }

    private void UpdateDetailHint()
    {
        if (SelectedRow is null)
        {
            NamePlaceholder = string.Empty;
            DetailHint = string.Empty;
            return;
        }

        var row = SelectedRow.Row;
        NamePlaceholder = PlaceholderFor(row);
        if (ActivityResolver.TryParseTime(row.StartTime, out var start) &&
            ActivityResolver.TryParseTime(row.EndTime, out var end) &&
            end > start)
        {
            DetailHint = $"显示为「{SelectedRow.DisplayName}」· 共 {(int)(end - start).TotalMinutes} 分钟";
        }
        else
        {
            DetailHint = "时间按 08:00 填写，结束时间要晚于开始时间。";
        }
    }

    private void Apply()
    {
        var lessonIndex = 0;
        foreach (var item in Rows)
        {
            item.LessonIndex = item.Row.Kind == TimetableRowKind.Lesson ? ++lessonIndex : 0;
            item.Refresh();
        }

        RefreshValidation();
        UpdateDetailHint();
        OnPropertyChanged(nameof(RowCountText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanConfirm));
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshValidation()
    {
        var validation = TimetableService.Validate(_draft);
        HasErrors = !validation.IsValid;
        HasWarnings = validation.Warnings.Count > 0;
        ValidationSummary = validation.Summary;

        ErrorText = validation.Errors.Count == 0
            ? string.Empty
            : string.Join("\n", validation.Errors.Take(4).Select(error => "· " + error)) +
              (validation.Errors.Count > 4 ? $"\n· 另有 {validation.Errors.Count - 4} 处问题" : string.Empty);
        WarningText = validation.Warnings.Count == 0
            ? string.Empty
            : string.Join("\n", validation.Warnings.Take(3).Select(warning => "· " + warning)) +
              (validation.Warnings.Count > 3 ? "\n· 另有未列出的提示" : string.Empty);

        PeriodPreview.Clear();
        if (!validation.IsValid)
        {
            HasPreview = false;
            PreviewSummary = "先修正校验问题";
            return;
        }

        try
        {
            foreach (var period in TimetableService.BuildPeriods(_draft))
                PeriodPreview.Add(new TimetablePeriodPreviewViewModel(period));
            HasPreview = true;
            PreviewSummary = validation.Summary;
        }
        catch (GradeWeaveException ex)
        {
            HasPreview = false;
            PreviewSummary = ex.Message;
        }
    }

    private TimeSpan NextStart(TimetableRowKind kind)
    {
        var latest = _draft.Rows
            .Select(row => ActivityResolver.TryParseTime(row.EndTime, out var end) ? end : (TimeSpan?)null)
            .Where(end => end.HasValue)
            .Select(end => end!.Value)
            .DefaultIfEmpty(TimeSpan.MinValue)
            .Max();

        return latest > TimeSpan.MinValue && latest < TimeSpan.FromHours(23)
            ? latest + TimeSpan.FromMinutes(10)
            : TimetablePalette.DefaultStart(kind);
    }

    private string PlaceholderFor(TimetableRow row)
    {
        if (row.Kind != TimetableRowKind.Lesson) return WeaveTimetables.KindLabel(row.Kind);

        var lessons = _draft.Rows.Where(item => item.Kind == TimetableRowKind.Lesson).ToList();
        var position = lessons.IndexOf(row);
        return WeaveTimetables.DefaultName(row.Kind, position < 0 ? lessons.Count : position);
    }

    private static bool LooksAutoLessonName(string name) =>
        name.Length > 2 && name[0] == '第' && name[^1] == '节' && name[1..^1].All(char.IsDigit);
}

/// <summary>左侧列表里的一行：颜色条、名称、时间、类型与时长。</summary>
public sealed class TimetableRowItemViewModel : ObservableObject
{
    public TimetableRowItemViewModel(TimetableRow row) => Row = row;

    public TimetableRow Row { get; }

    /// <summary>在列表里是第几节正课（1 起）；名称留空时用它生成「第 N 节」。</summary>
    public int LessonIndex { get; set; }

    public string DisplayName
    {
        get
        {
            var name = (Row.Name ?? string.Empty).Trim();
            if (name.Length > 0) return name;
            if (Row.Kind == TimetableRowKind.Lesson && LessonIndex > 0) return $"第{LessonIndex}节";
            return WeaveTimetables.KindLabel(Row.Kind);
        }
    }

    public string KindLabel => WeaveTimetables.KindLabel(Row.Kind);
    public IBrush Color => TimetablePalette.ForKind(Row.Kind);
    public IBrush TextColor => TimetablePalette.TextForKind(Row.Kind);

    public string TimeText
    {
        get
        {
            var start = WeaveTimetables.NormalizeTime(Row.StartTime);
            var end = WeaveTimetables.NormalizeTime(Row.EndTime);
            return start.Length == 0 || end.Length == 0 ? "时间待填写" : $"{start}—{end}";
        }
    }

    public string DurationText
    {
        get
        {
            if (!ActivityResolver.TryParseTime(Row.StartTime, out var start) ||
                !ActivityResolver.TryParseTime(Row.EndTime, out var end) ||
                end <= start)
                return "—";

            return $"{(int)(end - start).TotalMinutes} 分";
        }
    }

    public int StartMinutes =>
        ActivityResolver.TryParseTime(Row.StartTime, out var start) ? (int)start.TotalMinutes : -1;

    public int EndMinutes =>
        ActivityResolver.TryParseTime(Row.EndTime, out var end) ? (int)end.TotalMinutes : -1;

    public void Refresh()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(TextColor));
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(StartMinutes));
        OnPropertyChanged(nameof(EndMinutes));
    }
}

/// <summary>类型下拉项，顺序与 <see cref="TimetableRowKind"/> 一致。</summary>
public sealed class TimetableKindOption
{
    public TimetableKindOption(TimetableRowKind kind) => Kind = kind;

    public TimetableRowKind Kind { get; }
    public string Label => WeaveTimetables.KindLabel(Kind);
    public IBrush Color => TimetablePalette.ForKind(Kind);

    public override string ToString() => Label;
}

/// <summary>内置预设下拉项。</summary>
public sealed class TimetablePresetOption
{
    public TimetablePresetOption(WeaveTimetables.Preset preset) => Preset = preset;

    public WeaveTimetables.Preset Preset { get; }
    public string Name => Preset.Name;
    public string Description => Preset.Description;

    public override string ToString() => Name;
}

/// <summary>右侧节次预览的一行：第 N 节 / 名称 / 时间 / 正课或扩展。</summary>
public sealed class TimetablePeriodPreviewViewModel
{
    public TimetablePeriodPreviewViewModel(PeriodSlot slot) => Slot = slot;

    public PeriodSlot Slot { get; }
    public string Label => Slot.Label;
    public string Ordinal => $"第 {Slot.Index} 节";
    public string TimeText => Slot.TimeText;
    public string Tag => Slot.IsExtension ? "扩展" : "正课";
    public IBrush Color => Slot.IsExtension ? TimetablePalette.Extension : TimetablePalette.Lesson;
}

/// <summary>时间轴上方的小色块说明。</summary>
public sealed class TimetableLegendItem
{
    public TimetableLegendItem(string label, IBrush color)
    {
        Label = label;
        Color = color;
    }

    public string Label { get; }
    public IBrush Color { get; }
}

/// <summary>作息时间表的配色与默认时间；仅本窗口使用。</summary>
internal static class TimetablePalette
{
    public static readonly IBrush Lesson = Brush("#D97757");
    public static readonly IBrush MorningReading = Brush("#E8B49E");
    public static readonly IBrush Neutral = Brush("#D6D1C4");
    public static readonly IBrush Lunch = Brush("#C9C2AE");
    public static readonly IBrush EyeExercise = Brush("#B9C6C8");
    public static readonly IBrush Extension = Brush("#A8B39A");
    public static readonly IBrush EveningStudy = Brush("#7E8A9C");

    private static readonly IBrush InkText = Brush("#3D3B36");
    private static readonly IBrush LightText = Brush("#FFFFFF");

    public static IReadOnlyList<TimetableLegendItem> Legend { get; } =
    [
        new("正课", Lesson),
        new("早读", MorningReading),
        new("课间 / 作息", Neutral),
        new("午餐 / 午休", Lunch),
        new("眼保健操", EyeExercise),
        new("课服", Extension),
        new("晚修", EveningStudy),
    ];

    public static IBrush ForKind(TimetableRowKind kind) => kind switch
    {
        TimetableRowKind.Lesson => Lesson,
        TimetableRowKind.MorningReading => MorningReading,
        TimetableRowKind.Lunch or TimetableRowKind.Rest => Lunch,
        TimetableRowKind.EyeExercise => EyeExercise,
        TimetableRowKind.AfterSchool => Extension,
        TimetableRowKind.EveningStudy => EveningStudy,
        _ => Neutral,
    };

    public static IBrush TextForKind(TimetableRowKind kind) =>
        kind is TimetableRowKind.Lesson or TimetableRowKind.EveningStudy ? LightText : InkText;

    public static TimeSpan DefaultStart(TimetableRowKind kind) => kind switch
    {
        TimetableRowKind.MorningReading => TimeSpan.FromMinutes(7 * 60 + 20),
        TimetableRowKind.Break => TimeSpan.FromMinutes(9 * 60 + 30),
        TimetableRowKind.Lunch => TimeSpan.FromMinutes(11 * 60 + 40),
        TimetableRowKind.NoonStudy or TimetableRowKind.Rest => TimeSpan.FromMinutes(12 * 60 + 30),
        TimetableRowKind.EyeExercise => TimeSpan.FromMinutes(15 * 60),
        TimetableRowKind.AfterSchool => TimeSpan.FromMinutes(16 * 60 + 50),
        TimetableRowKind.EveningStudy => TimeSpan.FromMinutes(19 * 60 + 20),
        _ => TimeSpan.FromMinutes(8 * 60),
    };

    public static TimeSpan DefaultDuration(TimetableRowKind kind) => kind switch
    {
        TimetableRowKind.Lesson => TimeSpan.FromMinutes(40),
        TimetableRowKind.MorningReading => TimeSpan.FromMinutes(30),
        TimetableRowKind.Break => TimeSpan.FromMinutes(20),
        TimetableRowKind.NoonStudy => TimeSpan.FromMinutes(30),
        TimetableRowKind.Lunch => TimeSpan.FromMinutes(40),
        TimetableRowKind.Rest => TimeSpan.FromMinutes(60),
        TimetableRowKind.EyeExercise => TimeSpan.FromMinutes(5),
        TimetableRowKind.AfterSchool => TimeSpan.FromMinutes(30),
        TimetableRowKind.EveningStudy => TimeSpan.FromMinutes(60),
        _ => TimeSpan.FromMinutes(20),
    };

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
