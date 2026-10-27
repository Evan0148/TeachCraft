namespace GradeWeave.Core.Models;

/// <summary>
/// 会议岗位匹配：确认表里的岗位常带部门前缀（如「德育处副主任」「党政办干事」），
/// 会议规则只写「副主任」时也要能命中，同时避免把「班主任」误判成「主任」。
/// </summary>
public static class TeacherRoleMatcher
{
    public static bool Matches(IEnumerable<string> teacherRoles, IEnumerable<string> meetingRoles)
    {
        foreach (var teacherRole in teacherRoles)
        foreach (var meetingRole in meetingRoles)
            if (Matches(teacherRole, meetingRole))
                return true;

        return false;
    }

    public static bool Matches(string teacherRole, string meetingRole)
    {
        if (string.IsNullOrWhiteSpace(teacherRole) || string.IsNullOrWhiteSpace(meetingRole))
            return false;
        if (string.Equals(teacherRole, meetingRole, StringComparison.Ordinal))
            return true;

        return meetingRole switch
        {
            "班主任" => teacherRole.Contains("班主任", StringComparison.Ordinal),
            "副主任" => teacherRole.Contains("副主任", StringComparison.Ordinal),
            "主任" => teacherRole.Contains("主任", StringComparison.Ordinal)
                      && !teacherRole.Contains("班主任", StringComparison.Ordinal)
                      && !teacherRole.Contains("副主任", StringComparison.Ordinal),
            "校长" => teacherRole.Contains("校长", StringComparison.Ordinal),
            "年级长" => teacherRole.Contains("年级长", StringComparison.Ordinal),
            "科组长" => teacherRole.Contains("科组长", StringComparison.Ordinal),
            "备课组长" => teacherRole.Contains("备课组长", StringComparison.Ordinal),
            _ => false,
        };
    }
}
