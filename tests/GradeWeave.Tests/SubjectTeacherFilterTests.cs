using GradeWeave.Core.Models;

namespace GradeWeave.Tests;

/// <summary>走班组教师下拉只取当前学科的任课教师，不把全年级教师都列进去。</summary>
public sealed class SubjectTeacherFilterTests
{
    [Fact]
    public void TeachersOfSubject_UsesAssignmentsAndProfileSubject()
    {
        var project = new WeaveProject { SchoolName = "示例中学", GradeName = "九年级" };
        var math = new Subject { Id = "s1", Name = "数学" };
        var english = new Subject { Id = "s2", Name = "英语" };
        var politics = new Subject { Id = "s3", Name = "道法" };
        project.Subjects.AddRange([math, english, politics]);

        var assignedMath = new Teacher { Id = "t1", Name = "沈青禾", Subject = "数学" };
        var englishTeacher = new Teacher { Id = "t2", Name = "陆明川", Subject = "英语" };
        var profileMath = new Teacher { Id = "t3", Name = "温以宁", Subject = "数学" };
        var assignedMathOther = new Teacher { Id = "t4", Name = "顾知远", Subject = "物理" };
        var politicsTeacher = new Teacher { Id = "t5", Name = "苏映棠", Subject = "政治" };
        project.Teachers.AddRange([assignedMath, englishTeacher, profileMath, assignedMathOther, politicsTeacher]);

        project.Assignments.Add(new TeachingAssignment
        {
            ClassId = "c1", SubjectId = math.Id, TeacherId = assignedMath.Id, WeeklyHours = 5,
        });
        project.Assignments.Add(new TeachingAssignment
        {
            ClassId = "c1", SubjectId = math.Id, TeacherId = assignedMathOther.Id, WeeklyHours = 2,
        });

        var mathTeachers = project.TeachersOfSubject(math);
        Assert.Equal(3, mathTeachers.Count);
        Assert.Contains(assignedMath, mathTeachers);
        Assert.Contains(profileMath, mathTeachers);
        Assert.Contains(assignedMathOther, mathTeachers);
        Assert.DoesNotContain(englishTeacher, mathTeachers);

        var englishTeachers = project.TeachersOfSubject(english);
        Assert.Single(englishTeachers);
        Assert.Equal(englishTeacher.Id, englishTeachers[0].Id);

        // 教师档案写「政治」也能匹配「道法」学科。
        var politicsTeachers = project.TeachersOfSubject(politics);
        Assert.Single(politicsTeachers);
        Assert.Equal(politicsTeacher.Id, politicsTeachers[0].Id);
        Assert.Empty(project.TeachersOfSubject(null));
    }
}
