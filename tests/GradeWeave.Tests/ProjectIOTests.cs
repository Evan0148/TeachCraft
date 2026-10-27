using GradeWeave.Core.Models;
using GradeWeave.Core.Project;

namespace GradeWeave.Tests;

public sealed class ProjectIOTests
{
    [Fact]
    public void Project_RoundTripsClassesConstraintsAndLessons()
    {
        var project = WeaveSamples.CreateDemo();
        new GradeWeave.Core.Scheduling.WeaveScheduler().Generate(project);
        project.MeetingBlocks.Add(new MeetingBlock
        {
            Name = "行政会",
            DayIndex = 5,
            StartPeriod = 1,
            EndPeriod = 2,
            Roles = ["年级长", "副主任"],
            IsLeadership = true,
            Note = "测试",
        });

        var path = Path.Combine(Path.GetTempPath(), "gradeweave-io-" + Guid.NewGuid().ToString("N") + ".weave.json");
        try
        {
            WeaveProjectIO.Save(project, path);
            var loaded = WeaveProjectIO.Load(path);

            Assert.Equal(project.Classes.Count, loaded.Classes.Count);
            Assert.Equal(project.Teachers.Count, loaded.Teachers.Count);
            Assert.Equal(project.Assignments.Count, loaded.Assignments.Count);
            Assert.Equal(project.ResearchBlocks.Count, loaded.ResearchBlocks.Count);
            Assert.Equal(project.MeetingBlocks.Count, loaded.MeetingBlocks.Count);
            Assert.Equal(project.LayeredGroups.Count, loaded.LayeredGroups.Count);
            Assert.Equal(project.Lessons.Count, loaded.Lessons.Count);
            Assert.Equal(project.MeetingBlocks[^1].Roles, loaded.MeetingBlocks[^1].Roles);
            Assert.Equal(project.Periods.Count, loaded.Periods.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
