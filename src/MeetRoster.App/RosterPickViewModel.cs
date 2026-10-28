using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MeetRoster.Core.Import;

namespace MeetRoster.App;

/// <summary>名单弹窗里的一名候选学生。</summary>
public sealed class RosterCandidateViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public RosterCandidateViewModel(RosterEntry entry, bool alreadyInClass)
    {
        Entry = entry;
        AlreadyInClass = alreadyInClass;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RosterEntry Entry { get; }
    public string Name => Entry.Name;
    public string GenderLabel => string.IsNullOrEmpty(Entry.Gender) ? "性别待填" : Entry.Gender;
    public string ClassLabel => Entry.ClassName;
    public bool HasClassLabel => ClassLabel.Length > 0;

    /// <summary>当前班级里已有同名运动员：置灰，不可选。</summary>
    public bool AlreadyInClass { get; }

    public bool CanSelect => !AlreadyInClass;

    public string StatusLabel => AlreadyInClass ? "已在表内" : _isSelected ? "已选" : "选择";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(StatusLabel));
        }
    }

    public string AutomationName => $"{Name}，{GenderLabel}，{(AlreadyInClass ? "已在表内" : "可添加")}";

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 名单挑选弹窗：与「选择项目」同构——搜索 + 列表点选 + 完成写回。
/// 支持一次勾选多人，已在当前班级的学生置灰。
/// </summary>
public sealed partial class RosterPickViewModel : ObservableObject
{
    private readonly List<RosterCandidateViewModel> _all;

    public RosterPickViewModel(IReadOnlyList<RosterCandidateViewModel> candidates, string className)
    {
        _all = candidates.ToList();
        ClassName = className;
        SearchText = string.Empty;
        RefreshFilter();
    }

    public string ClassName { get; }
    public ObservableCollection<RosterCandidateViewModel> Candidates { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;

    public string SelectedCountText => $"已选 {_all.Count(c => c.IsSelected)} 人";
    public bool IsEmpty => Candidates.Count == 0;
    public string EmptyText => _all.Count == 0 ? "名单里没有学生。" : "没有匹配的学生。";
    public string ActionHint => "点击学生即可勾选 / 取消；点“完成”写入报名表。";

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    public void Toggle(RosterCandidateViewModel candidate)
    {
        if (!candidate.CanSelect) return;
        candidate.IsSelected = !candidate.IsSelected;
        OnPropertyChanged(nameof(SelectedCountText));
    }

    public IReadOnlyList<RosterEntry> GetPicked() => _all
        .Where(c => c.IsSelected)
        .Select(c => c.Entry)
        .ToList();

    public void SelectAllVisible()
    {
        foreach (var candidate in Candidates.Where(c => c.CanSelect))
            candidate.IsSelected = true;
        OnPropertyChanged(nameof(SelectedCountText));
    }

    public void ClearSelection()
    {
        foreach (var candidate in _all)
            candidate.IsSelected = false;
        OnPropertyChanged(nameof(SelectedCountText));
    }

    private void RefreshFilter()
    {
        var keyword = (SearchText ?? string.Empty).Trim();
        Candidates.Clear();
        foreach (var candidate in _all)
        {
            if (keyword.Length == 0 ||
                candidate.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                candidate.ClassLabel.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                Candidates.Add(candidate);
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(SelectedCountText));
    }
}
