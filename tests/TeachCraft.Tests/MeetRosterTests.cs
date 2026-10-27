using MeetRoster.Core.Models;
using MeetRoster.Core.Scheduling;

namespace TeachCraft.Tests;

public sealed class MeetRosterTests
{
    [Fact]
    public void Demo_HasTwoDaysAndCompleteRegistration()
    {
        var project = MeetSamples.CreateDemo();

        Assert.Equal(6, project.Classes.Count);
        Assert.Equal(48, project.Classes.Sum(c => c.Athletes.Count));
        Assert.Equal(2, project.Days.Count);
        Assert.Equal(20, project.Days.Sum(d => d.Slots.Count));
        Assert.Equal(132, project.Days.Sum(d => d.Slots.Sum(s => s.Entries.Count)));
    }

    [Fact]
    public void RefreshEntries_MapsIndividualEventByGenderAndClass()
    {
        var project = MeetSamples.CreateDemo();
        var slot = FindSlot(project, "100米", "女子", "预赛");

        Assert.Equal(12, slot.Entries.Count);
        Assert.Equal(6, slot.Entries.Select(e => e.ClassName).Distinct().Count());
        Assert.Equal(12, slot.Entries.Select(e => e.Lane).Distinct().Count());

        var registered = project.Classes
            .SelectMany(c => c.Athletes)
            .Where(a => a.Gender == "女子" &&
                        (a.IndividualEvent1 == "100米" || a.IndividualEvent2 == "100米"))
            .Select(a => a.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(slot.Entries, entry => Assert.Contains(entry.AthleteName, registered));
    }

    [Fact]
    public void RefreshEntries_MapsTeamEventOneEntryPerClass()
    {
        var project = MeetSamples.CreateDemo();
        var slot = FindSlot(project, "4x100米接力", "混合", "预赛");

        Assert.Equal(project.Classes.Count, slot.Entries.Count);
        Assert.All(slot.Entries, entry => Assert.Equal("4x100 接力队", entry.AthleteName));
        Assert.Equal(
            project.Classes.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal),
            slot.Entries.Select(e => e.ClassName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void RefreshEntries_ReflectsRemovedAthleteAndIsIdempotent()
    {
        var project = MeetSamples.CreateDemo();
        var meetClass = project.Classes[0];
        var removed = meetClass.Athletes.First(a =>
            a.Gender == "女子" &&
            (a.IndividualEvent1 == "100米" || a.IndividualEvent2 == "100米"));

        meetClass.Athletes.Remove(removed);
        MeetScheduleBuilder.RefreshEntries(project);

        var slot = FindSlot(project, "100米", "女子", "预赛");
        Assert.Equal(11, slot.Entries.Count);
        Assert.DoesNotContain(slot.Entries, entry => entry.AthleteName == removed.Name);

        MeetScheduleBuilder.RefreshEntries(project);
        Assert.Equal(11, slot.Entries.Count);
    }

    [Fact]
    public void RefreshEntries_IgnoresAthleteWithoutName()
    {
        var project = MeetSamples.CreateDemo();
        project.Classes[0].Athletes.Add(new Athlete
        {
            Name = string.Empty,
            Gender = "女子",
            IndividualEvent1 = "100米",
        });

        MeetScheduleBuilder.RefreshEntries(project);

        var slot = FindSlot(project, "100米", "女子", "预赛");
        Assert.Equal(12, slot.Entries.Count);
    }

    private static MeetSlot FindSlot(MeetProject project, string eventName, string gender, string stage) =>
        project.Days
            .SelectMany(day => day.Slots)
            .First(slot => slot.EventName == eventName && slot.Gender == gender && slot.Stage == stage);
}
