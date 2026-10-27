using System.Text.RegularExpressions;
using GradeWeave.Core.Models;

namespace GradeWeave.Core.Import;

/// <summary>把「会议活动备注」里的科组活动、会议安排解析成结构化的教研与会议时段。</summary>
public sealed class MeetingNoteParser
{
    private static readonly string[] ResearchMarkers =
        ["科组", "教研", "集备", "公开课", "学科组", "备课组"];

    private static readonly string[] DirectSubjectNames =
    [
        "道德与法治", "信息技术", "综合实践", "语文", "数学", "英语", "物理", "化学",
        "道法", "政治", "历史", "地理", "生物", "体育", "音乐", "美术", "劳动", "科学",
        "综合",
    ];

    private static readonly string[] LeadershipRoleCandidates =
        ["年级长", "科组长", "主任", "副主任", "校长"];

    public NoteParseResult Parse(string noteText, WeaveProject project)
    {
        var result = new NoteParseResult();
        if (string.IsNullOrWhiteSpace(noteText)) return result;

        var unparsed = new List<string>();
        var unparsedSet = new HashSet<string>(StringComparer.Ordinal);
        void AddUnparsed(string text)
        {
            var trimmed = text.Trim().Trim('，', ',', '。', '；', ';', '：', ':');
            if (trimmed.Length > 0 && unparsedSet.Add(trimmed)) unparsed.Add(trimmed);
        }

        var text = Regex.Replace(noteText.Trim(), @"^\s*会议活动备注\s*[:：]?", string.Empty);
        var currentDay = 0;

        foreach (var rawStatement in Regex.Split(text, @"[；;。\n\r]+"))
        {
            var statement = rawStatement.Trim().Trim('，', ',', '：', ':', ' ', '\t');
            if (statement.Length == 0) continue;

            var statementDay = WeaveImportText.DayIndex(statement);
            if (statementDay > 0) currentDay = statementDay;

            if (IsMeetingStatement(statement))
            {
                ParseMeeting(
                    statement,
                    statementDay > 0 ? statementDay : currentDay,
                    project,
                    result,
                    AddUnparsed);
                continue;
            }

            var clauseDay = statementDay > 0 ? statementDay : currentDay;

            foreach (var rawClause in Regex.Split(statement, @"[，,]"))
            {
                var clause = rawClause.Trim().Trim('：', ':', ' ', '\t');
                if (clause.Length == 0) continue;

                var dayFromClause = WeaveImportText.DayIndex(clause);
                if (dayFromClause > 0) clauseDay = dayFromClause;

                if (!IsResearchClause(clause) || clauseDay <= 0)
                {
                    AddUnparsed(clause);
                    continue;
                }

                if (!TryParseTimeRange(clause, out var start, out var end))
                {
                    AddUnparsed(clause);
                    continue;
                }

                var subjects = ResolveSubjects(clause, project);
                if (subjects.Count == 0)
                {
                    AddUnparsed(clause);
                    continue;
                }

                var note = clause;
                if (!ClampRange(ref start, ref end, out var suffix))
                {
                    AddUnparsed(clause + "（超出每天 7 节，无法生成教研时段）");
                    continue;
                }

                if (suffix.Length > 0) note += suffix;

                result.ResearchBlocks.Add(new ResearchBlock
                {
                    SubjectIds = subjects.Select(s => s.Id).ToList(),
                    DayIndex = clauseDay,
                    StartPeriod = start,
                    EndPeriod = end,
                    Note = note,
                });
            }
        }

        result.Unparsed.AddRange(unparsed);
        return result;
    }

    // ----------------------------- 会议 -----------------------------

    private static void ParseMeeting(
        string statement,
        int day,
        WeaveProject project,
        NoteParseResult result,
        Action<string> addUnparsed)
    {
        if (day <= 0 || !TryParseTimeRange(statement, out var start, out var end))
        {
            addUnparsed(statement);
            return;
        }

        var note = statement;
        if (!ClampRange(ref start, ref end, out var suffix))
        {
            addUnparsed(statement + "（超出每天 7 节，无法生成会议时段）");
            return;
        }

        if (suffix.Length > 0) note += suffix;

        MeetingBlock? block = null;

        if (statement.Contains("全体教师", StringComparison.Ordinal))
        {
            block = new MeetingBlock
            {
                Name = "全体教师大会",
                AppliesToAllTeachers = true,
            };
        }
        else if (statement.Contains("校办会", StringComparison.Ordinal)
                 || statement.Contains("行政会", StringComparison.Ordinal)
                 || statement.Contains("年级管委会", StringComparison.Ordinal))
        {
            var existingRoles = project.Teachers
                .SelectMany(t => t.Roles)
                .ToHashSet(StringComparer.Ordinal);

            block = new MeetingBlock
            {
                Name = "校办会、行政会、年级管委会",
                IsLeadership = true,
                Roles = LeadershipRoleCandidates
                    .Where(candidate => existingRoles.Any(role => TeacherRoleMatcher.Matches(role, candidate)))
                    .ToList(),
            };
        }
        else if (statement.Contains("科组长", StringComparison.Ordinal)
                 && statement.Contains("备课组长", StringComparison.Ordinal))
        {
            block = new MeetingBlock
            {
                Name = "科组长、备课组长会议",
                Roles = ["科组长", "备课组长"],
            };
        }
        else if (statement.Contains("科组长", StringComparison.Ordinal))
        {
            block = new MeetingBlock
            {
                Name = "科组长会议",
                Roles = ["科组长"],
            };
        }
        else if (statement.Contains("备课组长", StringComparison.Ordinal))
        {
            block = new MeetingBlock
            {
                Name = "备课组长会议",
                Roles = ["备课组长"],
            };
        }
        else if (statement.Contains("班主任", StringComparison.Ordinal)
                 && statement.Contains("会议", StringComparison.Ordinal))
        {
            block = new MeetingBlock
            {
                Name = "班主任会议",
                Roles = ["班主任"],
            };
        }

        if (block is null)
        {
            addUnparsed(statement);
            return;
        }

        block.DayIndex = day;
        block.StartPeriod = start;
        block.EndPeriod = end;
        block.Note = note;
        result.MeetingBlocks.Add(block);
    }

    private static bool IsMeetingStatement(string statement) =>
        statement.Contains("会议", StringComparison.Ordinal)
        || statement.Contains("大会", StringComparison.Ordinal)
        || statement.Contains("校办会", StringComparison.Ordinal)
        || statement.Contains("行政会", StringComparison.Ordinal)
        || statement.Contains("年级管委会", StringComparison.Ordinal)
        || statement.Contains("全体教师", StringComparison.Ordinal);

    // ----------------------------- 教研 -----------------------------

    private static bool IsResearchClause(string clause) =>
        ResearchMarkers.Any(m => clause.Contains(m, StringComparison.Ordinal));

    private static List<Subject> ResolveSubjects(string clause, WeaveProject project)
    {
        var names = new List<string>();

        if (clause.Contains("物化", StringComparison.Ordinal))
        {
            names.Add("物理");
            names.Add("化学");
        }
        else if (clause.Contains("政史", StringComparison.Ordinal))
        {
            names.Add("道法");
            names.Add("历史");
        }
        else
        {
            foreach (var name in DirectSubjectNames)
            {
                if (clause.Contains(name, StringComparison.Ordinal)) names.Add(name);
            }
        }

        return names
            .Select(project.FindSubjectByName)
            .OfType<Subject>()
            .DistinctBy(s => s.Id)
            .ToList();
    }

    // ----------------------------- 时间 -----------------------------

    private static bool TryParseTimeRange(string text, out int start, out int end)
    {
        start = 0;
        end = 0;

        var range = Regex.Match(text, @"(\d{1,2})\s*[、,，]\s*(\d{1,2})\s*节");
        if (!range.Success)
            range = Regex.Match(text, @"(\d{1,2})\s*(?:[-—~～至]|到)\s*(\d{1,2})\s*节");

        if (range.Success)
        {
            start = int.Parse(range.Groups[1].Value);
            end = int.Parse(range.Groups[2].Value);
            if (start > end) (start, end) = (end, start);
            return true;
        }

        var single = Regex.Match(text, @"第?\s*(\d{1,2})\s*节");
        if (single.Success)
        {
            start = end = int.Parse(single.Groups[1].Value);
            return true;
        }

        if (text.Contains("上午", StringComparison.Ordinal))
        {
            start = 1;
            end = 4;
            return true;
        }

        if (text.Contains("下午", StringComparison.Ordinal))
        {
            start = 5;
            end = 7;
            return true;
        }

        return false;
    }

    private static bool ClampRange(ref int start, ref int end, out string suffix)
    {
        suffix = string.Empty;

        if (start < 1) start = 1;
        if (end < start) end = start;
        if (start > 7) return false;

        if (end > 7)
        {
            end = 7;
            suffix = "（原文含第8节及以后，已按每天 7 节截断）";
        }

        return true;
    }
}
