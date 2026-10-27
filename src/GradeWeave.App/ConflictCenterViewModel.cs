using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GradeWeave.Core.Models;

namespace GradeWeave.App;

public sealed class ConflictKindFilterOption
{
    public ConflictKindFilterOption(string name, ConflictKind? kind)
    {
        Name = name;
        Kind = kind;
    }

    public string Name { get; }
    public ConflictKind? Kind { get; }
    public override string ToString() => Name;
}

public sealed class ConflictClassFilterOption
{
    public ConflictClassFilterOption(string name, SchoolClass? schoolClass)
    {
        Name = name;
        SchoolClass = schoolClass;
    }

    public string Name { get; }
    public SchoolClass? SchoolClass { get; }
    public override string ToString() => Name;
}

public sealed class ConflictTeacherFilterOption
{
    public ConflictTeacherFilterOption(string name, Teacher? teacher)
    {
        Name = name;
        Teacher = teacher;
    }

    public string Name { get; }
    public Teacher? Teacher { get; }
    public override string ToString() => Name;
}

/// <summary>冲突聚合台：按类型分组，支持类型/班级/教师筛选、搜索和定位。</summary>
public partial class ConflictCenterViewModel : ObservableObject
{
    private List<WeaveConflict> _conflicts = [];

    public ConflictCenterViewModel(WeaveProject project, IEnumerable<WeaveConflict> conflicts)
    {
        _conflicts = conflicts.ToList();

        KindFilters.Add(new ConflictKindFilterOption("全部类型", null));
        foreach (var kind in Enum.GetValues<ConflictKind>())
            KindFilters.Add(new ConflictKindFilterOption(ConflictItemViewModel.KindLabel(kind), kind));

        ClassFilters.Add(new ConflictClassFilterOption("全部班级", null));
        foreach (var schoolClass in project.Classes.OrderBy(item => item.Name, StringComparer.Ordinal))
            ClassFilters.Add(new ConflictClassFilterOption(schoolClass.Name, schoolClass));

        TeacherFilters.Add(new ConflictTeacherFilterOption("全部教师", null));
        foreach (var teacher in project.Teachers.OrderBy(item => item.Name, StringComparer.Ordinal))
            TeacherFilters.Add(new ConflictTeacherFilterOption(teacher.Name, teacher));

        _selectedKindFilter = KindFilters[0];
        _selectedClassFilter = ClassFilters[0];
        _selectedTeacherFilter = TeacherFilters[0];
        RefreshGroups();
    }

    public ObservableCollection<ConflictKindFilterOption> KindFilters { get; } = [];
    public ObservableCollection<ConflictClassFilterOption> ClassFilters { get; } = [];
    public ObservableCollection<ConflictTeacherFilterOption> TeacherFilters { get; } = [];
    public ObservableCollection<ConflictGroupViewModel> Groups { get; } = [];

    [ObservableProperty] private ConflictKindFilterOption? _selectedKindFilter;
    [ObservableProperty] private ConflictClassFilterOption? _selectedClassFilter;
    [ObservableProperty] private ConflictTeacherFilterOption? _selectedTeacherFilter;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _filteredCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _locatedCount;
    [ObservableProperty] private int _unknownLocationCount;
    [ObservableProperty] private string _filterSummary = "暂无冲突";

    public bool IsEmpty => Groups.Count == 0;

    partial void OnSelectedKindFilterChanged(ConflictKindFilterOption? value) => RefreshGroups();
    partial void OnSelectedClassFilterChanged(ConflictClassFilterOption? value) => RefreshGroups();
    partial void OnSelectedTeacherFilterChanged(ConflictTeacherFilterOption? value) => RefreshGroups();
    partial void OnSearchTextChanged(string value) => RefreshGroups();

    public void Refresh(IEnumerable<WeaveConflict> conflicts)
    {
        _conflicts = conflicts.ToList();
        RefreshGroups();
    }

    private void RefreshGroups()
    {
        var query = _conflicts.AsEnumerable();

        if (SelectedKindFilter?.Kind is { } kind)
            query = query.Where(conflict => conflict.Kind == kind);

        if (SelectedClassFilter?.SchoolClass is { } schoolClass)
            query = query.Where(conflict => conflict.ClassId == schoolClass.Id);

        if (SelectedTeacherFilter?.Teacher is { } teacher)
            query = query.Where(conflict => conflict.TeacherId == teacher.Id);

        var keyword = (SearchText ?? string.Empty).Trim();
        if (keyword.Length > 0)
        {
            query = query.Where(conflict =>
                conflict.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                ConflictItemViewModel.KindLabel(conflict.Kind)
                    .Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                conflict.Location.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var items = query
            .Select(conflict => new ConflictItemViewModel(conflict))
            .OrderBy(item => item.Conflict.DayIndex)
            .ThenBy(item => item.Conflict.Period)
            .ThenBy(item => item.Conflict.ClassId, StringComparer.Ordinal)
            .ToList();

        // 刷新重建分组时不打断老师的折叠状态：沿用该类型上次的展开 / 收起。
        var expandedByKind = Groups.ToDictionary(group => group.Kind, group => group.IsExpanded);

        Groups.Clear();
        foreach (var kindItem in Enum.GetValues<ConflictKind>())
        {
            var groupItems = items.Where(item => item.Conflict.Kind == kindItem).ToList();
            if (groupItems.Count > 0)
            {
                var group = new ConflictGroupViewModel(kindItem, groupItems)
                {
                    IsExpanded = !expandedByKind.TryGetValue(kindItem, out var wasExpanded) || wasExpanded,
                };
                Groups.Add(group);
            }
        }

        TotalCount = _conflicts.Count;
        FilteredCount = items.Count;
        LocatedCount = items.Count(item => item.HasLocation);
        UnknownLocationCount = FilteredCount - LocatedCount;
        FilterSummary = FilteredCount == 0
            ? $"没有匹配的冲突 · 共 {TotalCount} 条"
            : $"显示 {FilteredCount} / 共 {TotalCount} 条 · 可定位 {LocatedCount} · 位置未知 {UnknownLocationCount}";

        OnPropertyChanged(nameof(IsEmpty));
    }
}
