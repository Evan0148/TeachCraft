using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MeetRoster.Core.Models;

namespace MeetRoster.App;

/// <summary>报名表中的一行运动员，字段与导入模板的七列一一对应。</summary>
public sealed partial class AthleteRowViewModel : ObservableObject
{
    public const string NotReported = "（未报）";
    public const string SlotTeam1 = "T1";
    public const string SlotTeam2 = "T2";
    public const string SlotTeam3 = "T3";
    public const string SlotIndividual1 = "I1";
    public const string SlotIndividual2 = "I2";

    /// <summary>三个团体项目格的顺序，供分组弹窗与写回使用。</summary>
    public static readonly IReadOnlyList<string> TeamSlots = [SlotTeam1, SlotTeam2, SlotTeam3];

    /// <summary>两个单项格的顺序，供分组弹窗与写回使用。</summary>
    public static readonly IReadOnlyList<string> IndividualSlots = [SlotIndividual1, SlotIndividual2];

    private readonly Athlete _source;
    private readonly Action? _changed;
    private string _teamEvent1;

    /// <summary>该行背后的 Athlete 档案，供人数统计按引用排除本人。</summary>
    public Athlete Source => _source;

    private string _teamEvent2;
    private string _teamEvent3;
    private string _individualEvent1;
    private string _individualEvent2;
    private int _number;

    public AthleteRowViewModel(Athlete source, Action? changed = null)
    {
        _source = source;
        _changed = changed;
        _name = source.Name;
        _gender = source.Gender;
        _teamEvent1 = source.TeamEvent1;
        _teamEvent2 = source.TeamEvent2;
        _teamEvent3 = source.TeamEvent3;
        _individualEvent1 = source.IndividualEvent1;
        _individualEvent2 = source.IndividualEvent2;
        RefreshChips();
    }

    public ObservableCollection<EventChipViewModel> TeamChips { get; } = [];
    public ObservableCollection<EventChipViewModel> IndividualChips { get; } = [];

    public bool HasTeamChips => TeamChips.Count > 0;
    public bool HasIndividualChips => IndividualChips.Count > 0;

    public IReadOnlyList<string> GenderOptions { get; } = MeetCatalog.Genders;

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _gender;

    public int Number
    {
        get => _number;
        set
        {
            if (_number == value) return;
            _number = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NumberLabel));
        }
    }

    public string NumberLabel => _number.ToString("00");

    public string TeamEvent1
    {
        get => Display(_teamEvent1);
        set => SetEvent(ref _teamEvent1, value, source => _source.TeamEvent1 = source);
    }

    public string TeamEvent2
    {
        get => Display(_teamEvent2);
        set => SetEvent(ref _teamEvent2, value, source => _source.TeamEvent2 = source);
    }

    public string TeamEvent3
    {
        get => Display(_teamEvent3);
        set => SetEvent(ref _teamEvent3, value, source => _source.TeamEvent3 = source);
    }

    public string IndividualEvent1
    {
        get => Display(_individualEvent1);
        set => SetEvent(ref _individualEvent1, value, source => _source.IndividualEvent1 = source);
    }

    public string IndividualEvent2
    {
        get => Display(_individualEvent2);
        set => SetEvent(ref _individualEvent2, value, source => _source.IndividualEvent2 = source);
    }

    partial void OnNameChanged(string value)
    {
        _source.Name = value;
        _changed?.Invoke();
    }

    partial void OnGenderChanged(string value)
    {
        _source.Gender = value;

        // 性别变了以后，原来选的项目可能在新性别里不存在（如女子没有 1000 米），直接摘掉并留提示。
        var allowed = value == "女子" ? MeetCatalog.FemaleIndividualEvents : MeetCatalog.MaleIndividualEvents;
        var dropped = new List<string>();
        foreach (var slot in IndividualSlots)
        {
            var current = RawEvent(slot);
            if (string.IsNullOrWhiteSpace(current) || allowed.Contains(current)) continue;
            SetEvent(slot, string.Empty);
            dropped.Add(current);
        }

        LastGenderAdjustment = dropped.Count == 0
            ? string.Empty
            : $"{value}没有“{string.Join("、", dropped)}”，已从这一行移除";

        _changed?.Invoke();
    }

    /// <summary>最近一次因性别切换被移除的项目说明；供状态栏提示。</summary>
    public string LastGenderAdjustment { get; private set; } = string.Empty;

    private void SetEvent(ref string field, string value, Action<string> write)
    {
        var raw = value == NotReported ? string.Empty : value;
        if (field == raw) return;

        field = raw;
        write(raw);
        OnPropertyChanged();
        RefreshChips();
        _changed?.Invoke();
    }

    /// <summary>读取槽位原始值；空字符串表示未报。</summary>
    public string RawEvent(string slotKey) => slotKey switch
    {
        SlotTeam1 => _teamEvent1,
        SlotTeam2 => _teamEvent2,
        SlotTeam3 => _teamEvent3,
        SlotIndividual1 => _individualEvent1,
        SlotIndividual2 => _individualEvent2,
        _ => string.Empty,
    };

    /// <summary>写入槽位原始值；空字符串表示未报。供项目弹窗与模板回填使用。</summary>
    public void SetEvent(string slotKey, string rawValue)
    {
        var raw = rawValue == NotReported ? string.Empty : rawValue;
        switch (slotKey)
        {
            case SlotTeam1:
                SetEvent(ref _teamEvent1, raw, v => _source.TeamEvent1 = v);
                break;
            case SlotTeam2:
                SetEvent(ref _teamEvent2, raw, v => _source.TeamEvent2 = v);
                break;
            case SlotTeam3:
                SetEvent(ref _teamEvent3, raw, v => _source.TeamEvent3 = v);
                break;
            case SlotIndividual1:
                SetEvent(ref _individualEvent1, raw, v => _source.IndividualEvent1 = v);
                break;
            case SlotIndividual2:
                SetEvent(ref _individualEvent2, raw, v => _source.IndividualEvent2 = v);
                break;
        }

        OnPropertyChanged(nameof(IsTeam1Empty));
        OnPropertyChanged(nameof(IsTeam2Empty));
        OnPropertyChanged(nameof(IsTeam3Empty));
        OnPropertyChanged(nameof(IsIndividual1Empty));
        OnPropertyChanged(nameof(IsIndividual2Empty));
    }

    /// <summary>把一组团体项目按顺序写回三个格；多余的清空。</summary>
    public void SetTeamEvents(IReadOnlyList<string> events)
    {
        for (var i = 0; i < TeamSlots.Count; i++)
            SetEvent(TeamSlots[i], i < events.Count ? events[i] : string.Empty);
    }

    /// <summary>把一组单项按顺序写回两个格；多余的清空。</summary>
    public void SetIndividualEvents(IReadOnlyList<string> events)
    {
        for (var i = 0; i < IndividualSlots.Count; i++)
            SetEvent(IndividualSlots[i], i < events.Count ? events[i] : string.Empty);
    }

    private void RefreshChips()
    {
        TeamChips.Clear();
        if (!string.IsNullOrWhiteSpace(_teamEvent1))
            TeamChips.Add(new EventChipViewModel(this, _teamEvent1));
        if (!string.IsNullOrWhiteSpace(_teamEvent2))
            TeamChips.Add(new EventChipViewModel(this, _teamEvent2));
        if (!string.IsNullOrWhiteSpace(_teamEvent3))
            TeamChips.Add(new EventChipViewModel(this, _teamEvent3));

        IndividualChips.Clear();
        if (!string.IsNullOrWhiteSpace(_individualEvent1))
            IndividualChips.Add(new EventChipViewModel(this, _individualEvent1));
        if (!string.IsNullOrWhiteSpace(_individualEvent2))
            IndividualChips.Add(new EventChipViewModel(this, _individualEvent2));

        OnPropertyChanged(nameof(HasTeamChips));
        OnPropertyChanged(nameof(HasIndividualChips));
    }

    public bool IsTeam1Empty => string.IsNullOrWhiteSpace(_teamEvent1);
    public bool IsTeam2Empty => string.IsNullOrWhiteSpace(_teamEvent2);
    public bool IsTeam3Empty => string.IsNullOrWhiteSpace(_teamEvent3);
    public bool IsIndividual1Empty => string.IsNullOrWhiteSpace(_individualEvent1);
    public bool IsIndividual2Empty => string.IsNullOrWhiteSpace(_individualEvent2);

    private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? NotReported : value;
}

/// <summary>项目格里的一枚项目标签；点击整格打开多选弹窗。</summary>
public sealed class EventChipViewModel
{
    public EventChipViewModel(AthleteRowViewModel row, string eventName)
    {
        Row = row;
        EventName = eventName;
    }

    public AthleteRowViewModel Row { get; }
    public string EventName { get; }
}
