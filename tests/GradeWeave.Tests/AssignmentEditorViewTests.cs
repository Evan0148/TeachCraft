using GradeWeave.App;
using GradeWeave.Core.Models;

namespace GradeWeave.Tests;

public sealed class AssignmentEditorViewTests
{
    [Fact]
    public void DefaultView_IsTeacherRosterWithImportedTeachers()
    {
        var viewModel = new AssignmentEditorViewModel(WeaveSamples.CreateDemo());

        Assert.Equal(0, viewModel.ViewMode);
        Assert.True(viewModel.IsRosterView);
        Assert.True(viewModel.HasTeachers);
        Assert.Equal(9, viewModel.TeacherRoster.Count);
        Assert.Contains(viewModel.TeacherRoster, item => item.Name == "沈青禾" && item.SubjectName == "语文");
        Assert.Contains("9 位教师", viewModel.DraftSummary, StringComparison.Ordinal);
        Assert.Contains("9 位教师", viewModel.RosterSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void AddTeacher_WithSubject_AppearsInRosterAndTeacherView()
    {
        var viewModel = new AssignmentEditorViewModel(WeaveSamples.CreateDemo());
        viewModel.NewTeacherName = "新教师";
        viewModel.NewTeacherSubject = "化学";

        viewModel.AddTeacherCommand.Execute(null);

        var added = Assert.Single(viewModel.TeacherRoster, item => item.Name == "新教师");
        Assert.Equal("化学", added.SubjectName);
        Assert.Contains("已添加 新教师", viewModel.RosterMessage, StringComparison.Ordinal);

        viewModel.NewTeacherName = "新教师";
        viewModel.AddTeacherCommand.Execute(null);
        Assert.Contains("已经在名单里", viewModel.Error, StringComparison.Ordinal);
        Assert.Equal(10, viewModel.TeacherRoster.Count);

        viewModel.ShowTeacher("新教师");
        Assert.True(viewModel.IsTeacherView);
        Assert.Equal("新教师", viewModel.SelectedTeacherItem!.Name);
    }

    [Fact]
    public void BulkAddTeachers_ParsesSubjectsAndSkipsDuplicates()
    {
        var viewModel = new AssignmentEditorViewModel(WeaveSamples.CreateDemo());
        viewModel.BulkTeacherInput = "赵一,化学\n钱二 数学\n沈青禾,语文\n孙三 不懂学科";

        viewModel.BulkAddTeachersCommand.Execute(null);

        Assert.Equal(12, viewModel.TeacherRoster.Count);
        Assert.Equal("化学", viewModel.TeacherRoster.First(item => item.Name == "赵一").SubjectName);
        Assert.Equal("数学", viewModel.TeacherRoster.First(item => item.Name == "钱二").SubjectName);
        Assert.Equal(string.Empty, viewModel.TeacherRoster.First(item => item.Name == "孙三").SubjectName);
        Assert.Contains("新增 3 位", viewModel.RosterMessage, StringComparison.Ordinal);
        Assert.Contains("跳过重复 1 位", viewModel.RosterMessage, StringComparison.Ordinal);
        Assert.Contains("1 位学科未识别", viewModel.RosterMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TeacherView_TogglesAssignmentOnAndOff()
    {
        var viewModel = new AssignmentEditorViewModel(WeaveSamples.CreateDemo());
        viewModel.ViewMode = 2;

        var teacher = viewModel.TeacherRoster.First(item => item.Name == "陆明川");
        viewModel.SelectedTeacherItem = teacher;

        var cell = viewModel.TeacherClassRows
            .SelectMany(row => row.Cells)
            .First(item => !item.IsAssigned && item.HasOtherTeacher);
        var row = cell.Row;
        var index = cell.SubjectIndex;

        viewModel.ToggleTeacherAssignment(cell);

        Assert.Equal("陆明川", row.Cells[index].Text);
        var refreshedCell = viewModel.TeacherClassRows
            .First(item => ReferenceEquals(item.Row, row))
            .Cells[index];
        Assert.True(refreshedCell.IsAssigned);

        viewModel.ToggleTeacherAssignment(refreshedCell);

        Assert.Equal(string.Empty, row.Cells[index].Text);
    }

    [Fact]
    public void SubjectView_BulkFillsOnlyEmptyThenAll()
    {
        var viewModel = new AssignmentEditorViewModel(WeaveSamples.CreateDemo());
        viewModel.NewTeacherName = "新教师";
        viewModel.NewTeacherSubject = "化学";
        viewModel.AddTeacherCommand.Execute(null);

        viewModel.ViewMode = 3;
        var subject = viewModel.SubjectItems.First(item => item.Name == "化学");
        viewModel.SelectedSubjectItem = subject;
        var index = viewModel.Subjects
            .Select((item, position) => (item, position))
            .First(pair => pair.item.Id == subject.Subject.Id)
            .position;

        viewModel.Rows[0].Cells[index].Text = string.Empty;
        var untouched = viewModel.Rows[1].Cells[index].Text;
        viewModel.SelectedBulkTeacher = viewModel.BulkTeacherOptions
            .First(item => item.Name == "新教师");

        viewModel.FillEmptySubjectClassesCommand.Execute(null);

        Assert.Equal("新教师", viewModel.Rows[0].Cells[index].Text);
        Assert.Equal(untouched, viewModel.Rows[1].Cells[index].Text);
        Assert.Contains(viewModel.SubjectTeacherLoads, item => item.Name == "新教师");

        viewModel.FillAllSubjectClassesCommand.Execute(null);

        Assert.All(viewModel.Rows, row => Assert.Equal("新教师", row.Cells[index].Text));
        Assert.Single(viewModel.SubjectTeacherLoads);
        Assert.DoesNotContain(viewModel.BulkTeacherOptions, option => option.Name == "沈青禾");
    }

    [Fact]
    public void EmptyRoster_ShowsGuidanceAndKeepsCellsUnassignable()
    {
        var project = WeaveSamples.CreateDemo();
        project.Teachers.Clear();
        project.Assignments.Clear();

        var viewModel = new AssignmentEditorViewModel(project);

        Assert.True(viewModel.IsRosterView);
        Assert.Empty(viewModel.TeacherRoster);
        Assert.False(viewModel.HasTeachers);
        Assert.False(viewModel.CanStartAssignment);
        Assert.Contains("教师名单为空", viewModel.DraftSummary, StringComparison.Ordinal);
        Assert.All(viewModel.Rows.SelectMany(row => row.Cells),
            cell => Assert.Single(cell.TeacherOptions, option => option.IsEmpty));

        viewModel.StartAssignmentCommand.Execute(null);

        Assert.Contains("先添加", viewModel.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void CellOptions_FilterRosterBySubjectAndSelectionUpdatesText()
    {
        var viewModel = new AssignmentEditorViewModel(WeaveSamples.CreateDemo());
        var chemistryIndex = viewModel.Subjects
            .Select((item, position) => (item, position))
            .First(pair => pair.item.Name == "化学")
            .position;
        var cell = viewModel.Rows[0].Cells[chemistryIndex];

        Assert.Contains(cell.TeacherOptions, option => option.Name == "程叙白");
        Assert.DoesNotContain(cell.TeacherOptions, option => option.Name == "沈青禾");

        var chemistryTeacher = cell.TeacherOptions.First(option => option.Name == "程叙白");
        cell.SelectedTeacher = chemistryTeacher;

        Assert.Equal("程叙白", cell.Text);

        cell.SelectedTeacher = cell.TeacherOptions.First(option => option.IsEmpty);

        Assert.Equal(string.Empty, cell.Text);
    }

    [Fact]
    public void Overview_TracksMissingAssignmentsAndTeacherLoads()
    {
        var viewModel = new AssignmentEditorViewModel(WeaveSamples.CreateDemo());
        var total = viewModel.Rows.Count * viewModel.Subjects.Count;

        viewModel.Rows[0].Cells[0].Text = string.Empty;
        viewModel.Rows[1].Cells[1].Text = string.Empty;
        viewModel.ViewMode = 4;

        Assert.Equal(2, viewModel.OverviewMissingCount);
        Assert.Equal(total - 2, viewModel.OverviewFilledCount);
        Assert.Equal(2, viewModel.MissingAssignments.Count);
        Assert.Equal(9, viewModel.OverviewTeacherCount);
        Assert.Contains(viewModel.MissingAssignments, item => item.SubjectName == "语文");
        Assert.Contains(viewModel.MissingAssignments, item => item.SubjectName == "数学");
        Assert.Contains(viewModel.TeacherLoads, item => item.Name == "沈青禾" && item.WeeklyHours > 0);
    }

    [Fact]
    public void Apply_AfterTeacherViewEdit_WritesAssignmentAndHours()
    {
        var project = WeaveSamples.CreateDemo();
        var viewModel = new AssignmentEditorViewModel(project);
        viewModel.ViewMode = 2;

        var teacher = viewModel.TeacherRoster.First(item => item.Name == "苏映棠");
        viewModel.SelectedTeacherItem = teacher;

        var cell = viewModel.TeacherClassRows
            .SelectMany(row => row.Cells)
            .First(item => item.SubjectName == "物理" && !item.IsAssigned);
        viewModel.ToggleTeacherAssignment(cell);

        Assert.True(viewModel.Apply(), viewModel.Error);
        var classId = cell.Row.Class!.Id;
        var subjectId = viewModel.Subjects.First(item => item.Name == "物理").Id;
        var assignment = project.Assignments.First(item =>
            item.ClassId == classId && item.SubjectId == subjectId);

        Assert.Equal("苏映棠", project.FindTeacher(assignment.TeacherId)!.Name);
        Assert.Equal(
            project.Assignments.Where(item => item.TeacherId == assignment.TeacherId)
                .Sum(item => item.WeeklyHours),
            project.FindTeacher(assignment.TeacherId)!.WeeklyHours);
    }

    [Fact]
    public void Apply_PersistsUnassignedTeacherAndRemovesItFromRoster()
    {
        var project = WeaveSamples.CreateDemo();
        var viewModel = new AssignmentEditorViewModel(project);
        viewModel.NewTeacherName = "新教师";
        viewModel.NewTeacherSubject = "化学";
        viewModel.AddTeacherCommand.Execute(null);

        Assert.True(viewModel.Apply(), viewModel.Error);

        var added = Assert.Single(project.Teachers, item => item.Name == "新教师");
        Assert.Equal("化学", added.Subject);
        Assert.Equal(0, added.WeeklyHours);

        var reopened = new AssignmentEditorViewModel(project);
        Assert.Contains(reopened.TeacherRoster, item => item.Name == "新教师");

        reopened.RemoveTeacher(reopened.TeacherRoster.First(item => item.Name == "新教师"));
        Assert.Empty(reopened.Error);
        Assert.True(reopened.Apply(), reopened.Error);
        Assert.DoesNotContain(project.Teachers, item => item.Name == "新教师");
    }

    [Fact]
    public void RemoveTeacher_BlocksAssignedAndHeadTeacher()
    {
        var viewModel = new AssignmentEditorViewModel(WeaveSamples.CreateDemo());

        var assigned = viewModel.TeacherRoster.First(item => item.Name == "程叙白");
        viewModel.RemoveTeacher(assigned);

        Assert.Contains("还有任课", viewModel.Error, StringComparison.Ordinal);
        Assert.Contains(viewModel.TeacherRoster, item => item.Name == "程叙白");

        var headTeacher = viewModel.TeacherRoster.First(item => item.Name == "沈青禾");
        viewModel.RemoveTeacher(headTeacher);

        Assert.Contains("班主任", viewModel.Error, StringComparison.Ordinal);
        Assert.Equal(9, viewModel.TeacherRoster.Count);
    }

    [Fact]
    public void CrossViewNavigation_SelectsSubjectTeacherAndRoster()
    {
        var viewModel = new AssignmentEditorViewModel(WeaveSamples.CreateDemo());

        viewModel.ShowSubject("s3");

        Assert.True(viewModel.IsSubjectView);
        Assert.Equal("s3", viewModel.SelectedSubjectItem!.Subject.Id);

        viewModel.ShowTeacher("沈青禾");

        Assert.True(viewModel.IsTeacherView);
        Assert.Equal("沈青禾", viewModel.SelectedTeacherItem!.Name);

        viewModel.ShowRosterViewCommand.Execute(null);

        Assert.True(viewModel.IsRosterView);
    }

    [Fact]
    public void TeacherSearch_FiltersByNameAndSubject()
    {
        var viewModel = new AssignmentEditorViewModel(WeaveSamples.CreateDemo());
        viewModel.ViewMode = 2;

        viewModel.TeacherSearchText = "数学";

        Assert.NotEmpty(viewModel.FilteredTeacherItems);
        Assert.All(viewModel.FilteredTeacherItems,
            item => Assert.Contains("数学", item.SubjectText, StringComparison.Ordinal));

        viewModel.TeacherSearchText = "沈";

        Assert.Contains(viewModel.FilteredTeacherItems, item => item.Name == "沈青禾");

        viewModel.NewTeacherName = "新教师";
        viewModel.NewTeacherSubject = "化学";
        viewModel.AddTeacherCommand.Execute(null);

        Assert.Contains(viewModel.TeacherRoster, item => item.Name == "新教师");
    }
}
