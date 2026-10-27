using GradeWeave.Core;
using GradeWeave.Core.Import;
using GradeWeave.Core.Models;

namespace GradeWeave.Tests;

public sealed class ImporterTests
{
    [Fact]
    public void Import_ReadsClassesSubjectsTeachersAndAssignments()
    {
        var result = ImportSynthetic();
        var project = result.Project;

        Assert.Equal(6, project.Classes.Count);
        Assert.Equal(4, project.Subjects.Count);
        Assert.Equal(8, project.Teachers.Count);
        Assert.Equal(24, project.Assignments.Count);

        Assert.Equal(6, project.Subjects.Single(s => s.Name == "语文").WeeklyHours);
        Assert.Equal(6, project.Subjects.Single(s => s.Name == "数学").WeeklyHours);
        Assert.Equal(6, project.Subjects.Single(s => s.Name == "英语").WeeklyHours);
        Assert.Equal(4, project.Subjects.Single(s => s.Name == "物理").WeeklyHours);

        Assert.True(project.Subjects.Single(s => s.Name == "语文").IsMain);
        Assert.True(project.Subjects.Single(s => s.Name == "数学").IsMain);
        Assert.True(project.Subjects.Single(s => s.Name == "英语").IsMain);
        Assert.False(project.Subjects.Single(s => s.Name == "物理").IsMain);

        Assert.Equal(8, project.Teachers.Select(t => t.Name).Distinct().Count());
        Assert.All(project.Teachers, t => Assert.DoesNotContain(" ", t.Name));

        var classOne = project.Classes.Single(c => c.Name == "九（1）班");
        Assert.Equal("沈青禾", classOne.HeadTeacher);

        var chineseAssignment = project.Assignments.Single(
            a => a.ClassId == classOne.Id && project.FindSubject(a.SubjectId)?.Name == "语文");
        Assert.Equal("沈青禾", project.FindTeacher(chineseAssignment.TeacherId)?.Name);
        Assert.Equal(6, chineseAssignment.WeeklyHours);

        var shen = project.Teachers.Single(t => t.Name == "沈青禾");
        Assert.Equal("语文", shen.Subject);
        Assert.Contains("班主任", shen.Roles);
        Assert.Contains("科组长", shen.Roles);

        var gu = project.Teachers.Single(t => t.Name == "顾知远");
        Assert.Contains("年级长", gu.Roles);

        var wen = project.Teachers.Single(t => t.Name == "温以宁");
        Assert.Equal("物理", wen.Subject);
        Assert.Equal(4, wen.WeeklyHours);
        Assert.Contains("党政办副主任", wen.Roles);

        Assert.Equal(SyntheticWorkbookFactory.SchoolName, project.SchoolName);
        Assert.Equal(SyntheticWorkbookFactory.GradeName, project.GradeName);
        Assert.Equal(SyntheticWorkbookFactory.TermName, project.TermName);

        Assert.Contains(result.Sheets, s => s.Kind.Contains("分工表", StringComparison.Ordinal));
        Assert.Contains(result.Sheets, s => s.Kind.Contains("确认表", StringComparison.Ordinal));
        Assert.Contains(result.Sheets, s => s.Kind.Contains("课程表", StringComparison.Ordinal));
    }

    [Fact]
    public void Import_ParsesScheduleAsReferenceLessons()
    {
        var result = ImportSynthetic();
        var project = result.Project;

        Assert.NotEmpty(project.Lessons);

        var classOne = project.Classes.Single(c => c.Name == "九（1）班");
        var lesson = project.Lessons.Single(
            l => l.ClassId == classOne.Id && l.DayIndex == 2 && l.Period == 1);

        Assert.Equal("物理", project.FindSubject(lesson.SubjectId)?.Name);
        Assert.Equal("温以宁", project.FindTeacher(lesson.TeacherId)?.Name);
        Assert.False(lesson.IsFixed);

        var classSix = project.Classes.Single(c => c.Name == "九（6）班");
        Assert.DoesNotContain(
            project.Lessons,
            l => l.ClassId == classSix.Id && l.DayIndex == 5 && l.Period == 7);
        Assert.DoesNotContain(
            project.Lessons,
            l => l.ClassId == classSix.Id && l.DayIndex == 4 && l.Period == 6);

        Assert.Contains(result.Warnings, w => w.Contains("具体安排见附件1", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("轮流", StringComparison.Ordinal));

        // 课程表只是参考基线，不应改写分工表的任课关系。
        Assert.Equal(24, project.Assignments.Count);
    }

    [Fact]
    public void Import_ParsesMeetingNotesIntoSuggestions()
    {
        var result = ImportSynthetic();
        var project = result.Project;

        var physics = project.Subjects.Single(s => s.Name == "物理");
        var english = project.Subjects.Single(s => s.Name == "英语");
        var chinese = project.Subjects.Single(s => s.Name == "语文");

        Assert.Contains(result.SuggestedResearchBlocks, b =>
            b.DayIndex == 2 && b.StartPeriod == 1 && b.EndPeriod == 4 &&
            b.SubjectIds.Contains(physics.Id));

        Assert.Contains(result.SuggestedResearchBlocks, b =>
            b.DayIndex == 2 && b.StartPeriod == 5 && b.EndPeriod == 7 &&
            b.SubjectIds.Contains(english.Id));

        Assert.Contains(result.SuggestedResearchBlocks, b =>
            b.DayIndex == 4 && b.StartPeriod == 3 && b.EndPeriod == 4 &&
            b.SubjectIds.Contains(chinese.Id));

        var headTeacherMeeting = Assert.Single(
            result.SuggestedMeetingBlocks, b => b.Name == "班主任会议");
        Assert.Equal(1, headTeacherMeeting.DayIndex);
        Assert.Equal(6, headTeacherMeeting.StartPeriod);
        Assert.Equal(7, headTeacherMeeting.EndPeriod);
        Assert.Contains("班主任", headTeacherMeeting.Roles);

        var researchMeeting = Assert.Single(
            result.SuggestedMeetingBlocks, b => b.Name == "科组长、备课组长会议");
        Assert.Equal(2, researchMeeting.DayIndex);
        Assert.Equal(6, researchMeeting.StartPeriod);
        Assert.Equal(7, researchMeeting.EndPeriod);
        Assert.Contains("科组长", researchMeeting.Roles);
        Assert.Contains("备课组长", researchMeeting.Roles);

        var allTeachersMeeting = Assert.Single(
            result.SuggestedMeetingBlocks, b => b.Name == "全体教师大会");
        Assert.True(allTeachersMeeting.AppliesToAllTeachers);
        Assert.Equal(2, allTeachersMeeting.DayIndex);
        Assert.Equal(7, allTeachersMeeting.StartPeriod);
        Assert.Equal(7, allTeachersMeeting.EndPeriod);
        Assert.Contains("第8节", allTeachersMeeting.Note);

        var leadershipMeeting = Assert.Single(
            result.SuggestedMeetingBlocks, b => b.IsLeadership);
        Assert.Equal(5, leadershipMeeting.DayIndex);
        Assert.Equal(1, leadershipMeeting.StartPeriod);
        Assert.Equal(4, leadershipMeeting.EndPeriod);
        Assert.Contains("年级长", leadershipMeeting.Roles);
        Assert.Contains("科组长", leadershipMeeting.Roles);

        Assert.NotEmpty(project.Notes);
        Assert.NotEmpty(result.UnparsedNotes);

        // 建议结果必须等待界面确认，不能自动写入工程。
        Assert.Empty(project.ResearchBlocks);
        Assert.Empty(project.MeetingBlocks);
    }

    [Fact]
    public void Import_MissingFileAndLegacyExcelThrowChineseErrors()
    {
        var importer = new WeaveWorkbookImporter();

        var missing = Path.Combine(
            Path.GetTempPath(), "GradeWeave.Tests", Guid.NewGuid().ToString("N") + ".xlsx");
        var missingError = Assert.Throws<GradeWeaveException>(() => importer.Import(missing));
        Assert.Contains("找不到", missingError.Message, StringComparison.Ordinal);

        var directory = Path.Combine(
            Path.GetTempPath(), "GradeWeave.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var xls = Path.Combine(directory, "课堂分工表.xls");
            File.WriteAllText(xls, "not an excel file");

            var xlsError = Assert.Throws<GradeWeaveException>(() => importer.Import(xls));
            Assert.Contains("xlsx", xlsError.Message, StringComparison.OrdinalIgnoreCase);

            var broken = Path.Combine(directory, "损坏的工作簿.xlsx");
            File.WriteAllText(broken, "not a workbook at all");

            var brokenError = Assert.Throws<GradeWeaveException>(() => importer.Import(broken));
            Assert.Contains("无法读取", brokenError.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Import_SameFileTwiceDoesNotDuplicateTeachers()
    {
        var path = SyntheticWorkbookFactory.CreateWorkbook();
        try
        {
            var importer = new WeaveWorkbookImporter();
            var first = importer.Import(path).Project;
            var second = importer.Import(path).Project;

            Assert.Equal(8, first.Teachers.Count);
            Assert.Equal(8, second.Teachers.Count);

            Assert.Equal(8, first.Teachers.Select(t => t.Name).Distinct().Count());
            Assert.Equal(8, second.Teachers.Select(t => t.Name).Distinct().Count());

            Assert.DoesNotContain(
                first.Teachers.GroupBy(t => t.Name),
                g => g.Count() > 1);
            Assert.Contains(first.Teachers, t => t.Name == "沈青禾");
        }
        finally
        {
            SyntheticWorkbookFactory.Cleanup(path);
        }
    }

    private static WeaveImportResult ImportSynthetic()
    {
        var path = SyntheticWorkbookFactory.CreateWorkbook();
        try
        {
            return new WeaveWorkbookImporter().Import(path);
        }
        finally
        {
            SyntheticWorkbookFactory.Cleanup(path);
        }
    }
}
