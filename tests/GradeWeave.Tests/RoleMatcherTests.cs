using GradeWeave.Core.Models;

namespace GradeWeave.Tests;

public sealed class RoleMatcherTests
{
    [Fact]
    public void MatchesPrefixedRoles_WithoutTreatingHeadTeacherAsDirector()
    {
        Assert.True(TeacherRoleMatcher.Matches("德育处副主任", "副主任"));
        Assert.True(TeacherRoleMatcher.Matches("党政办副主任", "副主任"));
        Assert.True(TeacherRoleMatcher.Matches("教务处主任", "主任"));
        Assert.False(TeacherRoleMatcher.Matches("党政办副主任", "主任"));
        Assert.False(TeacherRoleMatcher.Matches("班主任", "主任"));
        Assert.True(TeacherRoleMatcher.Matches("班主任", "班主任"));
        Assert.True(TeacherRoleMatcher.Matches("副校长", "校长"));
        Assert.False(TeacherRoleMatcher.Matches("德育处干事", "副主任"));
    }
}
