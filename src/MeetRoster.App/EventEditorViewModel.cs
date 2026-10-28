using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MeetRoster.Core.Models;

namespace MeetRoster.App;

/// <summary>点击报名项目格后的项目选择窗口：单项格支持多选，团体格单选。</summary>
public partial class EventEditorViewModel : ObservableObject
{
    public const string IndividualGroupKey = "I";
    public const string TeamGroupKey = "T";

    private readonly MeetClass _meetClass;
    private readonly AthleteRowViewModel _row;
    private readonly List<EventChoiceViewModel> _allChoices = [];
    private readonly List<string> _selections = [];
    private readonly int _maxSelections;

    public EventEditorViewModel(MeetClass meetClass, AthleteRowViewModel row, string slotKey)
    {
        ArgumentNullException.ThrowIfNull(meetClass);
        ArgumentNullException.ThrowIfNull(row);

        _meetClass = meetClass;
        _row = row;
        SlotKey = slotKey;
        IsGroupMode = slotKey is IndividualGroupKey or TeamGroupKey;
        IsTeamGroup = slotKey == TeamGroupKey;
        ClassName = meetClass.Name;
        AthleteName = string.IsNullOrWhiteSpace(row.Name) ? "未命名运动员" : row.Name;

        if (IsGroupMode)
        {
            _maxSelections = IsTeamGroup ? AthleteRowViewModel.TeamSlots.Count : AthleteRowViewModel.IndividualSlots.Count;
            SlotLabel = IsTeamGroup ? "团体项目" : "比赛项目";
            CurrentLabel = IsTeamGroup ? "已选团体项目" : "已选项目";
            Hint = IsTeamGroup
                ? "可同时选择最多 3 个团体项目；再点一次已选项目即可取消。"
                : "可同时选择最多 2 个单项；再点一次已选项目即可取消。";
            GroupFooterHint = IsTeamGroup
                ? "可多选最多 3 个团体项目；点“完成”写回表格。"
                : "可多选最多 2 个单项；点“完成”写回表格。";

            var slots = IsTeamGroup ? AthleteRowViewModel.TeamSlots : AthleteRowViewModel.IndividualSlots;
            foreach (var key in slots)
            {
                var eventName = row.RawEvent(key);
                if (!string.IsNullOrWhiteSpace(eventName) && !_selections.Contains(eventName))
                    _selections.Add(eventName);
            }

            CurrentText = _selections.Count == 0 ? "（未报）" : string.Join("、", _selections);
            RefreshSelectedEvents();
            RebuildChoices();
            return;
        }

        var current = row.RawEvent(slotKey);
        SlotLabel = slotKey switch
        {
            AthleteRowViewModel.SlotTeam1 => "团体项目1",
            AthleteRowViewModel.SlotTeam2 => "团体项目2",
            AthleteRowViewModel.SlotTeam3 => "团体项目3",
            AthleteRowViewModel.SlotIndividual1 => "比赛项目1",
            AthleteRowViewModel.SlotIndividual2 => "比赛项目2",
            _ => "报名项目",
        };
        CurrentLabel = "当前项目";
        CurrentText = string.IsNullOrWhiteSpace(current) ? "（未报）" : current;
        IsTeamSlot = SlotKey.StartsWith('T');
        GroupFooterHint = "可多选最多 2 个单项；点“完成”写回表格。";

        if (IsTeamSlot)
        {
            Hint = "点一行即可把该项目选入本格。";
            foreach (var eventName in MeetCatalog.TeamEvents)
                _allChoices.Add(BuildTeamChoice(eventName, current));
        }
        else
        {
            Hint = "每班每个单项男女各限 2 人，每人限报 2 个单项；满员或重复的项目会标红提醒。";
            var events = row.Gender == "女子" ? MeetCatalog.FemaleIndividualEvents : MeetCatalog.MaleIndividualEvents;
            foreach (var eventName in events)
                _allChoices.Add(BuildSingleIndividualChoice(eventName, current));
        }

        RefreshChoices();
    }

    public string SlotKey { get; }
    public string ClassName { get; }
    public string AthleteName { get; }
    public string SlotLabel { get; }
    public string CurrentLabel { get; }
    public string CurrentText { get; }
    public bool CanClear => IsGroupMode ? _selections.Count > 0 : CurrentText != "（未报）";
    public bool IsTeamSlot { get; }
    public bool IsGroupMode { get; }
    public bool IsTeamGroup { get; }
    public string GroupFooterHint { get; }
    public bool ShowClearSingle => !IsGroupMode && CanClear;
    public bool ShowClearGroup => IsGroupMode && CanClear;

    public string ActionHint => IsGroupMode ? "点击一行选中 / 取消" : "点击一行即可选入";

    public ObservableCollection<EventChoiceViewModel> Choices { get; } = [];
    public ObservableCollection<string> SelectedEvents { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _hint;

    public bool IsEmpty => Choices.Count == 0;
    public string EmptyText => "没有匹配的项目。";
    public bool HasSelectedEvents => SelectedEvents.Count > 0;
    public string SelectedCountText => $"已选 {_selections.Count}/{_maxSelections}";

    partial void OnSearchTextChanged(string value) => RefreshChoices();

    public void Toggle(EventChoiceViewModel choice)
    {
        if (!IsGroupMode) return;

        if (_selections.Remove(choice.EventName))
        {
            Hint = "已取消选择；再点一次可重新选入。";
        }
        else if (_selections.Count >= _maxSelections)
        {
            Hint = IsTeamGroup
                ? $"每人最多 {_maxSelections} 个团体项目；先点已选项目取消一个，再选新的。"
                : $"每人限报 {_maxSelections} 个单项；先点已选项目取消一个，再选新的。";
            return;
        }
        else
        {
            _selections.Add(choice.EventName);
            Hint = "已选入；完成后再关闭窗口。";
        }

        RefreshSelectedEvents();
        UpdateGroupChoices();
        OnPropertyChanged(nameof(CanClear));
        OnPropertyChanged(nameof(ShowClearSingle));
        OnPropertyChanged(nameof(ShowClearGroup));
        OnPropertyChanged(nameof(SelectedCountText));
    }

    public void ClearSelection()
    {
        if (!IsGroupMode) return;
        _selections.Clear();
        RefreshSelectedEvents();
        UpdateGroupChoices();
        Hint = IsTeamGroup ? "已清空团体项目。" : "已清空比赛项目。";
        OnPropertyChanged(nameof(CanClear));
        OnPropertyChanged(nameof(ShowClearSingle));
        OnPropertyChanged(nameof(ShowClearGroup));
        OnPropertyChanged(nameof(SelectedCountText));
    }

    public IReadOnlyList<string> GetSelection() => _selections.ToList();

    private void RebuildChoices()
    {
        _allChoices.Clear();
        if (IsTeamGroup)
        {
            foreach (var eventName in MeetCatalog.TeamEvents)
                _allChoices.Add(new EventChoiceViewModel(eventName, isTeam: true));
        }
        else
        {
            var events = _row.Gender == "女子" ? MeetCatalog.FemaleIndividualEvents : MeetCatalog.MaleIndividualEvents;
            foreach (var eventName in events)
                _allChoices.Add(new EventChoiceViewModel(eventName, isTeam: false));
        }

        UpdateGroupChoices();
        RefreshChoices();
    }

    /// <summary>
    /// 原地刷新候选行的状态（人数预判 / 选中态 / 满员提醒），不重建 Choices 集合——
    /// 多选切换时列表滚动位置不跳（Plan ⑥ / P1-11）。
    /// </summary>
    private void UpdateGroupChoices()
    {
        foreach (var choice in _allChoices)
        {
            var selected = _selections.Contains(choice.EventName);
            var others = choice.IsTeam ? CountOtherTeamAthletes(choice.EventName) : CountOtherAthletes(choice.EventName, excludeSelf: true);
            var total = others + (selected ? 1 : 0);

            choice.Meta = choice.IsTeam ? $"本班已有 {total} 人报此项目" : $"{_row.Gender} 已报 {total}/2";
            choice.ReplaceText = selected ? "已选" : "选入";
            choice.IsCurrent = selected;

            if (!selected && _selections.Count >= _maxSelections)
            {
                choice.Warning = IsTeamGroup
                    ? $"已选 {_maxSelections} 个团体项目，先取消一个再选。"
                    : $"已选 {_maxSelections} 个单项，先取消一个再选。";
                choice.HasWarning = true;
            }
            else if (!choice.IsTeam && !selected && total >= 2)
            {
                choice.Warning = $"{_row.Gender}{choice.EventName} 已满 2 人，选入后会提示超限";
                choice.HasWarning = true;
            }
            else
            {
                choice.Warning = string.Empty;
                choice.HasWarning = false;
            }
        }
    }

    private EventChoiceViewModel BuildTeamChoice(string eventName, string current)
    {
        var count = 0;
        foreach (var athlete in _meetClass.Athletes)
        {
            if (string.IsNullOrWhiteSpace(athlete.Name)) continue;
            if (ReferenceEquals(athlete, _row.Source)) continue;

            if (athlete.TeamEvent1 == eventName) count++;
            if (athlete.TeamEvent2 == eventName) count++;
            if (athlete.TeamEvent3 == eventName) count++;
        }

        return new EventChoiceViewModel(eventName, isTeam: true)
        {
            Meta = $"本班已有 {count} 人报此项目",
            ReplaceText = current == eventName ? "已在本格" : "选入本格",
            IsCurrent = current == eventName,
        };
    }

    private EventChoiceViewModel BuildSingleIndividualChoice(string eventName, string current)
    {
        var otherSlotKey = SlotKey == AthleteRowViewModel.SlotIndividual1
            ? AthleteRowViewModel.SlotIndividual2
            : AthleteRowViewModel.SlotIndividual1;
        var selfCurrentSlot = current == eventName;
        var selfOtherSlot = _row.RawEvent(otherSlotKey) == eventName;
        var others = CountOtherAthletes(eventName, excludeSelf: true);
        var total = others + (selfCurrentSlot ? 1 : 0) + (selfOtherSlot ? 1 : 0);

        var choice = new EventChoiceViewModel(eventName, isTeam: false)
        {
            Meta = $"{_row.Gender} 已报 {total}/2",
            ReplaceText = current == eventName ? "已在本格" : "选入本格",
            IsCurrent = current == eventName,
        };

        if (selfCurrentSlot)
        {
            // 已在本格：保持现状不会产生任何新增违规，不标红。
        }
        else if (selfOtherSlot)
        {
            var otherLabel = otherSlotKey == AthleteRowViewModel.SlotIndividual1 ? "比赛项目1" : "比赛项目2";
            choice.Warning = $"本人已在「{otherLabel}」报了此项目，选入会提示重复";
            choice.HasWarning = true;
        }
        else if (total >= 2)
        {
            choice.Warning = $"{_row.Gender}{eventName} 已满 2 人，选入后会提示超限";
            choice.HasWarning = true;
        }

        return choice;
    }

    /// <summary>本班其他运动员里报了这个团体项目的人数。</summary>
    private int CountOtherTeamAthletes(string eventName)
    {
        var count = 0;
        foreach (var athlete in _meetClass.Athletes)
        {
            if (string.IsNullOrWhiteSpace(athlete.Name)) continue;
            if (ReferenceEquals(athlete, _row.Source)) continue;
            if (athlete.TeamEvent1 == eventName) count++;
            if (athlete.TeamEvent2 == eventName) count++;
            if (athlete.TeamEvent3 == eventName) count++;
        }

        return count;
    }

    private int CountOtherAthletes(string eventName, bool excludeSelf)
    {
        var count = 0;
        foreach (var athlete in _meetClass.Athletes)
        {
            if (string.IsNullOrWhiteSpace(athlete.Name) || athlete.Gender != _row.Gender) continue;
            if (excludeSelf && ReferenceEquals(athlete, _row.Source)) continue;
            if (athlete.IndividualEvent1 == eventName || athlete.IndividualEvent2 == eventName) count++;
        }

        return count;
    }

    private void RefreshSelectedEvents()
    {
        SelectedEvents.Clear();
        foreach (var eventName in _selections) SelectedEvents.Add(eventName);
        OnPropertyChanged(nameof(HasSelectedEvents));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(CanClear));
        OnPropertyChanged(nameof(ShowClearSingle));
        OnPropertyChanged(nameof(ShowClearGroup));
    }

    private void RefreshChoices()
    {
        var keyword = (SearchText ?? string.Empty).Trim();
        var matches = keyword.Length == 0
            ? _allChoices
            : _allChoices.Where(choice =>
                choice.EventName.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

        Choices.Clear();
        foreach (var choice in matches) Choices.Add(choice);

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }
}

/// <summary>
/// 选择项目列表里的一行：项目名 + 人数预判 + 满员 / 重复提醒。
/// 状态可原地更新（INPC），多选切换时列表不重建、滚动位置不跳（Plan ⑥ / P1-11）。
/// </summary>
public sealed class EventChoiceViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private string _meta = string.Empty;
    private string _replaceText = string.Empty;
    private bool _isCurrent;
    private string _warning = string.Empty;
    private bool _hasWarning;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public EventChoiceViewModel(string eventName, bool isTeam)
    {
        EventName = eventName;
        IsTeam = isTeam;
    }

    public string EventName { get; }
    public bool IsTeam { get; }
    public string Meta { get => _meta; set => Set(ref _meta, value); }
    public string ReplaceText { get => _replaceText; set => Set(ref _replaceText, value); }
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
    public string Warning { get => _warning; set => Set(ref _warning, value); }
    public bool HasWarning { get => _hasWarning; set => Set(ref _hasWarning, value); }
    public string AutomationName => $"{EventName}，{Meta}";

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        if (name == nameof(Meta))
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(AutomationName)));
    }
}
