using CommunityToolkit.Mvvm.ComponentModel;
using MeetRoster.Core.Models;

namespace MeetRoster.App;

/// <summary>报名表中的一行运动员，字段与导入模板的七列一一对应。</summary>
public sealed partial class AthleteRowViewModel : ObservableObject
{
    public const string NotReported = "（未报）";

    private readonly Athlete _source;
    private readonly Action? _changed;
    private string _teamEvent1;
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
    }

    public IReadOnlyList<string> GenderOptions { get; } = MeetCatalog.Genders;
    public IReadOnlyList<string> TeamEventOptions { get; } = [NotReported, .. MeetCatalog.TeamEvents];
    public IReadOnlyList<string> IndividualEventOptions { get; } = [NotReported, .. MeetCatalog.IndividualEvents];

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
        _changed?.Invoke();
    }

    private void SetEvent(ref string field, string value, Action<string> write)
    {
        var raw = value == NotReported ? string.Empty : value;
        if (field == raw) return;

        field = raw;
        write(raw);
        OnPropertyChanged();
        _changed?.Invoke();
    }

    private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? NotReported : value;
}
