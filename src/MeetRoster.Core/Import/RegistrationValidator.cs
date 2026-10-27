using MeetRoster.Core.Models;

namespace MeetRoster.Core.Import;

/// <summary>按学校报名规则检查一个班级：单项男女各限 2 人、每人限报 2 个单项。</summary>
public static class RegistrationValidator
{
    public static IReadOnlyList<RegistrationIssue> Validate(MeetClass meetClass)
    {
        var issues = new List<RegistrationIssue>();
        var athletes = meetClass.Athletes
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .ToList();

        foreach (var gender in MeetCatalog.Genders)
        {
            foreach (var eventName in MeetCatalog.IndividualEvents)
            {
                var selected = athletes.Where(a =>
                    a.Gender == gender &&
                    (a.IndividualEvent1 == eventName || a.IndividualEvent2 == eventName)).ToList();
                if (selected.Count > 2)
                {
                    var names = string.Join("、", selected.Select(a => a.Name));
                    issues.Add(new RegistrationIssue(
                        $"{gender} {eventName} 已报 {selected.Count} 人（{names}），超过每班最多 2 人。"));
                }
            }
        }

        foreach (var athlete in athletes)
        {
            if (athlete.Gender != "男子" && athlete.Gender != "女子")
                issues.Add(new RegistrationIssue($"{athlete.Name} 的性别“{athlete.Gender}”无法识别。"));

            var individual = new[] { athlete.IndividualEvent1, athlete.IndividualEvent2 }
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .ToList();
            if (individual.Count > 2)
                issues.Add(new RegistrationIssue(
                    $"{athlete.Name} 填了 {individual.Count} 个单项，每人最多 2 个单项。"));

            if (individual.Count >= 2 && individual[0] == individual[1])
                issues.Add(new RegistrationIssue($"{athlete.Name} 重复填写了“{individual[0]}”。"));

            var allowed = athlete.Gender == "女子"
                ? MeetCatalog.FemaleIndividualEvents
                : MeetCatalog.MaleIndividualEvents;
            foreach (var eventName in individual)
            {
                if (!MeetCatalog.IndividualEvents.Contains(eventName))
                    issues.Add(new RegistrationIssue(
                        $"{athlete.Name} 的比赛项目“{eventName}”不在模板项目列表中。", false));
                else if (!allowed.Contains(eventName))
                    issues.Add(new RegistrationIssue(
                        $"{athlete.Name} 是{athlete.Gender}，没有“{eventName}”这一项。"));
            }

            var team = new[] { athlete.TeamEvent1, athlete.TeamEvent2, athlete.TeamEvent3 }
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .ToList();
            if (team.Count >= 2 && team.Distinct(StringComparer.Ordinal).Count() != team.Count)
                issues.Add(new RegistrationIssue(
                    $"{athlete.Name} 重复填写了团体项目“{team.GroupBy(e => e, StringComparer.Ordinal).First(g => g.Count() > 1).Key}”。"));

            foreach (var teamEvent in team)
            {
                if (!MeetCatalog.TeamEvents.Contains(teamEvent))
                    issues.Add(new RegistrationIssue(
                        $"{athlete.Name} 的团体项目“{teamEvent}”不在模板项目列表中。", false));
            }
        }

        foreach (var duplicate in athletes
                     .GroupBy(a => a.Name, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            issues.Add(new RegistrationIssue(
                $"名单里出现重复姓名“{duplicate.Key}”{duplicate.Count()} 次，请确认。", false));
        }

        return issues
            .GroupBy(i => (i.Message, i.IsError))
            .Select(g => g.First())
            .ToList();
    }
}
