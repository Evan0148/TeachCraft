using MeetRoster.Core.Models;

namespace MeetRoster.Core.Scheduling;

/// <summary>
/// 按当前报名数据刷新赛程中的参赛记录。当前版本只负责把名单映射到示例时段，
/// 赛程生成规则与班级高亮留待后续接入。
/// </summary>
public static class MeetScheduleBuilder
{
    public static void RefreshEntries(MeetProject project)
    {
        // 按 (性别, 项目) 建一次索引，之后每个时段只查字典——
        // 复杂度从 O(Slots × Classes × Athletes) 降到 O(Classes × Athletes + Slots × k)（Plan 3.3）。
        var individualIndex =
            new Dictionary<(string Gender, string Event), List<(MeetClass Class, Athlete Athlete)>>();
        var teamRegistered = new Dictionary<string, List<MeetClass>>(StringComparer.Ordinal);

        foreach (var meetClass in project.Classes)
        {
            foreach (var athlete in meetClass.Athletes)
            {
                if (string.IsNullOrWhiteSpace(athlete.Name)) continue;

                foreach (var ev in new[] { athlete.IndividualEvent1, athlete.IndividualEvent2 })
                {
                    if (string.IsNullOrEmpty(ev)) continue;
                    var key = (athlete.Gender, ev);
                    if (!individualIndex.TryGetValue(key, out var list)) individualIndex[key] = list = [];
                    list.Add((meetClass, athlete));
                }

                foreach (var ev in new[] { athlete.TeamEvent1, athlete.TeamEvent2, athlete.TeamEvent3 })
                {
                    if (string.IsNullOrEmpty(ev)) continue;
                    if (!teamRegistered.TryGetValue(ev, out var classes)) teamRegistered[ev] = classes = [];
                    if (!classes.Contains(meetClass)) classes.Add(meetClass);
                }
            }
        }

        foreach (var day in project.Days)
        {
            foreach (var slot in day.Slots)
            {
                slot.Entries.Clear();
                if (slot.Category == MeetEventCategory.Ceremony) continue;

                if (IsTeamEvent(slot.EventName))
                {
                    FillTeamEntries(project, slot, teamRegistered);
                    continue;
                }

                FillIndividualEntries(slot, individualIndex);
            }
        }
    }

    private static void FillIndividualEntries(
        MeetSlot slot,
        Dictionary<(string Gender, string Event), List<(MeetClass Class, Athlete Athlete)>> index)
    {
        if (!index.TryGetValue((slot.Gender, slot.EventName), out var matches)) return;

        var order = 1;
        foreach (var (meetClass, athlete) in matches)
        {
            slot.Entries.Add(new MeetEntry
            {
                ClassName = meetClass.Name,
                AthleteName = athlete.Name,
                Lane = slot.Category == MeetEventCategory.Track
                    ? $"第 {order++} 道"
                    : $"第 {order++} 组",
            });
        }
    }

    private static void FillTeamEntries(
        MeetProject project,
        MeetSlot slot,
        Dictionary<string, List<MeetClass>> teamRegistered)
    {
        var requiresRegistration = slot.EventName == "4x100米接力";
        var group = 1;
        var ordered = requiresRegistration
            ? teamRegistered.TryGetValue(slot.EventName, out var classes) ? classes : []
            : project.Classes;

        foreach (var meetClass in ordered)
        {
            slot.Entries.Add(new MeetEntry
            {
                ClassName = meetClass.Name,
                AthleteName = TeamLabel(slot.EventName),
                Lane = $"第 {group++} 组",
            });
        }
    }

    private static string TeamLabel(string eventName) => eventName switch
    {
        "4x100米接力" => "4x100 接力队",
        "20*50米男女混合接力" => "混合接力队",
        "一分钟跳绳" => "跳绳队",
        _ => "代表队",
    };

    private static bool IsTeamEvent(string eventName) =>
        eventName.Contains("接力", StringComparison.Ordinal) ||
        eventName.Contains("跳绳", StringComparison.Ordinal);
}
