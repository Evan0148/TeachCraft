using System;
using System.Collections.ObjectModel;
using System.Linq;
using ClassBlock.Core.Models;
using ClassBlock.Core.Timetable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClassBlock.App;

public sealed class KindOption
{
    public KindOption(TimetableRowKind kind, string display)
    {
        Kind = kind;
        Display = display;
    }

    public TimetableRowKind Kind { get; }
    public string Display { get; }
}

public sealed class SlotOption
{
    public SlotOption(TemplateSlot slot, string display)
    {
        Slot = slot;
        Display = display;
    }

    public TemplateSlot Slot { get; }
    public string Display { get; }
}

public partial class TemplateRowEditViewModel : ObservableObject
{
    [ObservableProperty] private string _time1 = string.Empty;
    [ObservableProperty] private string _time2 = string.Empty;
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private KindOption _kind;
    [ObservableProperty] private SlotOption _slot;
    [ObservableProperty] private string _staticText = string.Empty;
    [ObservableProperty] private bool _mergeDays;

    public TemplateRowEditViewModel(KindOption kind, SlotOption slot)
    {
        _kind = kind;
        _slot = slot;
    }

    public static TemplateRowEditViewModel FromSpec(
        TemplateRowSpec spec,
        IReadOnlyList<KindOption> kinds,
        IReadOnlyList<SlotOption> slots)
    {
        return new TemplateRowEditViewModel(
            kinds.First(k => k.Kind == spec.Kind),
            slots.First(s => s.Slot == spec.Slot))
        {
            Time1 = spec.Times.Count > 0 ? spec.Times[0] : string.Empty,
            Time2 = spec.Times.Count > 1 ? spec.Times[1] : string.Empty,
            Label = spec.Label,
            StaticText = spec.StaticText,
            MergeDays = spec.MergeDays,
        };
    }

    public TemplateRowSpec ToSpec()
    {
        var times = new List<string>();
        if (Time1.Trim().Length > 0) times.Add(Time1.Trim());
        if (Time2.Trim().Length > 0) times.Add(Time2.Trim());

        return new TemplateRowSpec
        {
            Times = times,
            Label = Label.Trim(),
            Kind = Kind.Kind,
            Slot = Slot.Slot,
            StaticText = StaticText.Trim(),
            MergeDays = MergeDays,
        };
    }
}

public partial class TimetableTemplateEditorViewModel : ObservableObject
{
    public IReadOnlyList<KindOption> KindOptions { get; } = new[]
    {
        new KindOption(TimetableRowKind.Normal, "普通行"),
        new KindOption(TimetableRowKind.Break, "大课间（合并行）"),
        new KindOption(TimetableRowKind.Lunch, "午餐（整行红字）"),
        new KindOption(TimetableRowKind.Dinner, "晚餐（整行红字）"),
        new KindOption(TimetableRowKind.News, "新闻联播（合并行）"),
        new KindOption(TimetableRowKind.Evening, "晚修（双时间高行）"),
    };

    public IReadOnlyList<SlotOption> SlotOptions { get; } = new[]
    {
        new SlotOption(TemplateSlot.None, "静态内容"),
        new SlotOption(TemplateSlot.Period1, "第 1 节"),
        new SlotOption(TemplateSlot.Period2, "第 2 节"),
        new SlotOption(TemplateSlot.Period3, "第 3 节"),
        new SlotOption(TemplateSlot.Period4, "第 4 节"),
        new SlotOption(TemplateSlot.Period5, "第 5 节"),
        new SlotOption(TemplateSlot.Period6, "第 6 节"),
        new SlotOption(TemplateSlot.Period7, "第 7 节"),
        new SlotOption(TemplateSlot.EarlyReading, "早读"),
        new SlotOption(TemplateSlot.LunchStudy, "午学"),
        new SlotOption(TemplateSlot.AfterSchool1, "课服1"),
        new SlotOption(TemplateSlot.AfterSchool2, "课服2"),
        new SlotOption(TemplateSlot.Evening, "晚修教师"),
    };

    public ObservableCollection<TemplateRowEditViewModel> Rows { get; } = new();

    [ObservableProperty] private string _templateName = string.Empty;
    [ObservableProperty] private bool _isBuiltin;
    [ObservableProperty] private TemplateRowEditViewModel? _selectedRow;

    public string TemplateId { get; private set; } = string.Empty;
    public string GradeTag { get; }
    public bool IsPersisted { get; }
    public bool CanEdit => !IsBuiltin;
    public bool CanDelete => !IsBuiltin && IsPersisted;
    public bool CanSave => CanEdit && TemplateName.Trim().Length > 0 && Rows.Count > 0;

    public TimetableTemplateEditorViewModel(TimetableTemplate source, bool startAsCopy)
    {
        TemplateId = source.Id;
        GradeTag = source.GradeTag;
        TemplateName = source.Name;
        IsBuiltin = source.IsBuiltin;
        IsPersisted = !startAsCopy && !source.IsBuiltin;

        foreach (var spec in source.Rows)
            Rows.Add(TemplateRowEditViewModel.FromSpec(spec, KindOptions, SlotOptions));

        SelectedRow = Rows.Count > 0 ? Rows[0] : null;
    }

    [RelayCommand]
    private void CopyAsCustom()
    {
        TemplateId = Guid.NewGuid().ToString("N");
        TemplateName = TemplateName.Trim().Length == 0 ? "自定义作息" : $"{TemplateName.Trim()} · 自定义";
        IsBuiltin = false;
    }

    [RelayCommand]
    private void AddRow()
    {
        var row = new TemplateRowEditViewModel(KindOptions[0], SlotOptions[0]);
        var index = SelectedRow is null ? Rows.Count : Rows.IndexOf(SelectedRow) + 1;
        Rows.Insert(index, row);
        SelectedRow = row;
    }

    [RelayCommand]
    private void RemoveRow()
    {
        if (SelectedRow is null) return;

        var index = Rows.IndexOf(SelectedRow);
        Rows.RemoveAt(index);
        SelectedRow = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)];
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedRow is null) return;

        var index = Rows.IndexOf(SelectedRow);
        if (index > 0) Rows.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedRow is null) return;

        var index = Rows.IndexOf(SelectedRow);
        if (index < Rows.Count - 1) Rows.Move(index, index + 1);
    }

    public TimetableTemplate BuildTemplate() => new()
    {
        Id = TemplateId,
        Name = TemplateName.Trim(),
        IsBuiltin = false,
        GradeTag = GradeTag,
        Rows = Rows.Select(r => r.ToSpec()).ToList(),
    };

    partial void OnTemplateNameChanged(string value) => OnPropertyChanged(nameof(CanSave));

    partial void OnIsBuiltinChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanSave));
    }
}
