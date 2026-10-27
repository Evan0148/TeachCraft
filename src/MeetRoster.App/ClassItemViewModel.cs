using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MeetRoster.Core.Models;

namespace MeetRoster.App;

/// <summary>左侧班级列表与报名表共享的班级视图模型。</summary>
public sealed partial class ClassItemViewModel : ObservableObject
{
    private readonly MeetClass _source;
    private readonly Action<ClassItemViewModel>? _changed;

    public ClassItemViewModel(MeetClass source, Action<ClassItemViewModel>? changed = null)
    {
        _source = source;
        _changed = changed;

        foreach (var athlete in source.Athletes)
            Athletes.Add(new AthleteRowViewModel(athlete, OnRowChanged));

        Renumber();
    }

    public MeetClass Source => _source;
    public ObservableCollection<AthleteRowViewModel> Athletes { get; } = [];

    public string Name => _source.Name;
    public string Group => _source.Group;
    public string Initial => string.IsNullOrEmpty(_source.Name) ? "班" : _source.Name[..1];

    public string Code
    {
        get => _source.Code;
        set
        {
            if (_source.Code == value) return;
            _source.Code = value;
            OnPropertyChanged();
            _changed?.Invoke(this);
        }
    }

    public string LeaderContact
    {
        get => _source.LeaderContact;
        set
        {
            if (_source.LeaderContact == value) return;
            _source.LeaderContact = value;
            OnPropertyChanged();
            _changed?.Invoke(this);
        }
    }

    public string Coach
    {
        get => _source.Coach;
        set
        {
            if (_source.Coach == value) return;
            _source.Coach = value;
            OnPropertyChanged();
            _changed?.Invoke(this);
        }
    }

    public int RowCount => Athletes.Count;
    public int AthleteCount => Athletes.Count(a => !string.IsNullOrWhiteSpace(a.Name));
    public int MaleCount => Athletes.Count(a => !string.IsNullOrWhiteSpace(a.Name) && a.Gender == "男子");
    public int FemaleCount => Athletes.Count(a => !string.IsNullOrWhiteSpace(a.Name) && a.Gender == "女子");

    public int IndividualTotal => Athletes
        .Where(a => !string.IsNullOrWhiteSpace(a.Name))
        .Sum(a => NonEmpty(RawEvent(a.IndividualEvent1)) + NonEmpty(RawEvent(a.IndividualEvent2)));

    public int TeamTotal => Athletes
        .Where(a => !string.IsNullOrWhiteSpace(a.Name))
        .Sum(a => NonEmpty(RawEvent(a.TeamEvent1)) + NonEmpty(RawEvent(a.TeamEvent2)) + NonEmpty(RawEvent(a.TeamEvent3)));

    public int EntryTotal => IndividualTotal + TeamTotal;

    public string MetaLine => $"男 {MaleCount} · 女 {FemaleCount} · {EntryTotal} 项次";
    public string CountLabel => AthleteCount.ToString("00");

    public int CountFor(string gender, string eventName) => Athletes.Count(a =>
        !string.IsNullOrWhiteSpace(a.Name) &&
        a.Gender == gender &&
        (RawEvent(a.IndividualEvent1) == eventName || RawEvent(a.IndividualEvent2) == eventName));

    public void AddAthlete()
    {
        var athlete = new Athlete { Name = string.Empty, Gender = "男子" };
        _source.Athletes.Add(athlete);
        Athletes.Add(new AthleteRowViewModel(athlete, OnRowChanged));
        Renumber();
        RefreshStats();
        _changed?.Invoke(this);
    }

    public void RemoveAthlete(AthleteRowViewModel row)
    {
        var index = Athletes.IndexOf(row);
        if (index < 0) return;

        Athletes.RemoveAt(index);
        _source.Athletes.RemoveAt(index);
        Renumber();
        RefreshStats();
        _changed?.Invoke(this);
    }

    public void RefreshStats()
    {
        OnPropertyChanged(nameof(RowCount));
        OnPropertyChanged(nameof(AthleteCount));
        OnPropertyChanged(nameof(MaleCount));
        OnPropertyChanged(nameof(FemaleCount));
        OnPropertyChanged(nameof(IndividualTotal));
        OnPropertyChanged(nameof(TeamTotal));
        OnPropertyChanged(nameof(EntryTotal));
        OnPropertyChanged(nameof(MetaLine));
        OnPropertyChanged(nameof(CountLabel));
    }

    private void OnRowChanged()
    {
        RefreshStats();
        _changed?.Invoke(this);
    }

    private void Renumber()
    {
        for (var i = 0; i < Athletes.Count; i++)
            Athletes[i].Number = i + 1;
    }

    private static int NonEmpty(string value) => string.IsNullOrWhiteSpace(value) ? 0 : 1;

    // 行内显示「（未报）」，计数时把占位符视为未报名。
    private static string RawEvent(string value) =>
        string.IsNullOrWhiteSpace(value) || value == AthleteRowViewModel.NotReported
            ? string.Empty
            : value;
}
