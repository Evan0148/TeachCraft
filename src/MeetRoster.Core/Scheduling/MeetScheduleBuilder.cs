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
        foreach (var day in project.Days)
        {
            foreach (var slot in day.Slots)
            {
                slot.Entries.Clear();
                if (slot.Category == MeetEventCategory.Ceremony) continue;

                if (slot.Category == MeetEventCategory.Team)
                {
                    FillTeamEntries(project, slot);
                    continue;
                }

                FillIndividualEntries(project, slot);
            }
        }
    }

    private static void FillIndividualEntries(MeetProject project, MeetSlot slot)
    {
        var order = 1;
        foreach (var meetClass in project.Classes)
        {
            foreach (var athlete in meetClass.Athletes)
            {
                if (string.IsNullOrWhiteSpace(athlete.Name)) continue;
                if (athlete.Gender != slot.Gender) continue;
                if (athlete.IndividualEvent1 != slot.EventName &&
                    athlete.IndividualEvent2 != slot.EventName) continue;

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
    }

    private static void FillTeamEntries(MeetProject project, MeetSlot slot)
    {
        var group = 1;
        foreach (var meetClass in project.Classes)
        {
            var registered = meetClass.Athletes.Any(a =>
                a.TeamEvent1 == slot.EventName ||
                a.TeamEvent2 == slot.EventName ||
                a.TeamEvent3 == slot.EventName);
            if (!registered) continue;

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
}
