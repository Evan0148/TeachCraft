using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MeetRoster.Core.Models;

namespace MeetRoster.App;

/// <summary>赛程日期标签。</summary>
public sealed partial class ScheduleDayItemViewModel : ObservableObject
{
    public ScheduleDayItemViewModel(MeetDay source)
    {
        Source = source;
    }

    public MeetDay Source { get; }
    public string Name => Source.Name;
    public string DateText => Source.DateText;
    public string MetaLabel => $"{Source.Slots.Count} 个项目 · {Source.Slots.Sum(s => s.Entries.Count)} 人次";

    public void RefreshMeta() => OnPropertyChanged(nameof(MetaLabel));

    [ObservableProperty] private bool _isSelected;
}

/// <summary>赛程项目筛选（全部 / 径赛 / 田赛 / 团体 / 仪式）。</summary>
public sealed partial class ScheduleCategoryItemViewModel : ObservableObject
{
    public ScheduleCategoryItemViewModel(string key, string name, int count, bool isActive)
    {
        Key = key;
        Name = name;
        Count = count;
        _isActive = isActive;
    }

    public string Key { get; }
    public string Name { get; }
    public int Count { get; }
    public string CountLabel => Count.ToString("00");

    [ObservableProperty] private bool _isActive;
}

/// <summary>上午 / 下午分组。</summary>
public sealed class ScheduleGroupViewModel
{
    public required string Label { get; init; }
    public required string English { get; init; }
    public required string TimeRange { get; init; }
    public required IReadOnlyList<ScheduleSlotViewModel> Slots { get; init; }
    public string CountLabel => $"{Slots.Count} 项";
}

/// <summary>赛程中的一个时段与它的参赛名单。</summary>
public sealed class ScheduleSlotViewModel
{
    private static readonly IBrush TrackBrush = new SolidColorBrush(Color.Parse("#A34A36"));
    private static readonly IBrush TrackTint = new SolidColorBrush(Color.Parse("#14A34A36"));
    private static readonly IBrush FieldBrush = new SolidColorBrush(Color.Parse("#5A7A4B"));
    private static readonly IBrush FieldTint = new SolidColorBrush(Color.Parse("#145A7A4B"));
    private static readonly IBrush TeamBrush = new SolidColorBrush(Color.Parse("#8A6A3C"));
    private static readonly IBrush TeamTint = new SolidColorBrush(Color.Parse("#148A6A3C"));
    private static readonly IBrush FunBrush = new SolidColorBrush(Color.Parse("#4F6D7A"));
    private static readonly IBrush FunTint = new SolidColorBrush(Color.Parse("#144F6D7A"));
    private static readonly IBrush CeremonyBrush = new SolidColorBrush(Color.Parse("#7C7668"));
    private static readonly IBrush CeremonyTint = new SolidColorBrush(Color.Parse("#127C7668"));

    public ScheduleSlotViewModel(MeetSlot source, string? highlightClass = null)
    {
        Source = source;
        HighlightClass = highlightClass;
        Entries = source.Entries
            .Select(entry => new ScheduleEntryViewModel(entry, highlightClass))
            .ToList();
        HasClassHighlight = highlightClass is not null &&
                           source.Entries.Any(e => e.ClassName == highlightClass);

        (CategoryLabel, CategoryBrush, CategoryTint) = source.Category switch
        {
            MeetEventCategory.Track => ("径赛", TrackBrush, TrackTint),
            MeetEventCategory.Field => ("田赛", FieldBrush, FieldTint),
            MeetEventCategory.Fun => ("趣味", FunBrush, FunTint),
            MeetEventCategory.Team => ("团体", TeamBrush, TeamTint),
            _ => ("仪式", CeremonyBrush, CeremonyTint),
        };
    }

    public MeetSlot Source { get; }
    public string? HighlightClass { get; }
    public bool HasClassHighlight { get; }
    public string Time => Source.Time;
    public string Period => Source.Period;
    public string EventName => Source.EventName;
    public string Gender => Source.Gender;
    public string Stage => Source.Stage;
    public string Venue => Source.Venue;
    public string Grade => Source.Grade;
    public string CategoryLabel { get; }
    public IBrush CategoryBrush { get; }
    public IBrush CategoryTint { get; }
    public IReadOnlyList<ScheduleEntryViewModel> Entries { get; }

    public bool IsCeremony => Source.Category == MeetEventCategory.Ceremony;
    public bool ShowGender => !IsCeremony && Source.Gender is "男子" or "女子" or "混合";
    public bool ShowGrade => !string.IsNullOrWhiteSpace(Source.Grade);
    public bool HasEntries => Entries.Count > 0;
    public string DetailText => Source.Venue;
    public string GroupSummary => BuildGroupSummary();
    public bool ShowGroupSummary => GroupSummary.Length > 0;
    public bool IsTeamEvent => EventName.Contains("接力", StringComparison.Ordinal) ||
                               EventName.Contains("跳绳", StringComparison.Ordinal);
    public string EntryCountLabel => IsCeremony
        ? "全体"
        : IsTeamEvent
            ? $"{Entries.Count} 支队伍"
            : $"{Entries.Count} 人次";
    public string EmptyEntryText => IsCeremony ? "全体班级" : "暂无报名";
    public string HighlightedEntryCountLabel => HasClassHighlight
        ? $"{Entries.Count(e => e.IsHighlighted)} 条高亮"
        : string.Empty;

    private string BuildGroupSummary()
    {
        if (string.IsNullOrWhiteSpace(Source.GroupCount) && string.IsNullOrWhiteSpace(Source.AdvanceCount))
            return string.Empty;

        var group = Source.GroupCount.Length > 0 ? $"{Source.GroupCount} 组" : string.Empty;
        var advance = Source.AdvanceCount.Length > 0 ? $"取 {Source.AdvanceCount} 名" : string.Empty;
        return string.Join(" · ", new[] { group, advance }.Where(s => s.Length > 0));
    }
}

/// <summary>赛程中的一条参赛记录。</summary>
public sealed class ScheduleEntryViewModel
{
    public ScheduleEntryViewModel(MeetEntry source, string? highlightClass = null)
    {
        ClassName = source.ClassName;
        AthleteName = source.AthleteName;
        Lane = source.Lane;
        Bib = source.Bib;
        Group = source.Group;
        IsHighlighted = highlightClass is not null &&
                        string.Equals(source.ClassName, highlightClass, StringComparison.Ordinal);
        Opacity = highlightClass is null || IsHighlighted ? 1 : 0.7;
    }

    public string ClassName { get; }
    public string AthleteName { get; }
    public string Lane { get; }
    public string Bib { get; }
    public string Group { get; }
    public bool IsHighlighted { get; }
    public double Opacity { get; }
    public string Display => $"{ClassName} · {AthleteName}";
}
