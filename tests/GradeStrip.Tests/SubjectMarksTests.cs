using System.Collections.Generic;
using System.IO;
using System.Linq;
using GradeStrip.Core.Analysis;
using GradeStrip.Core.Import;
using GradeStrip.Core.Layout;
using Xunit;

namespace GradeStrip.Tests;

public class SubjectMarksTests
{
    [Fact]
    public void JuniorHigh_OfficialConvertedFullMarks_SumsTo850()
    {
        var subjects = new[]
        {
            "语文", "数学", "英语", "物理", "化学", "历史", "政治", "体育", "生物学", "地理"
        };

        var expectedMarks = new Dictionary<string, int>
        {
            ["语文"] = 120,
            ["数学"] = 120,
            ["英语"] = 120,
            ["物理"] = 100,
            ["化学"] = 70,
            ["历史"] = 80,
            ["政治"] = 70,
            ["体育"] = 70,
            ["生物学"] = 50,
            ["地理"] = 50,
        };

        foreach (var sub in subjects)
        {
            Assert.Equal(expectedMarks[sub], SubjectMarks.FullMark(sub, highestObserved: null, SchoolStage.JuniorHigh));
        }

        var total = SubjectMarks.TotalFullMark(subjects, null, SchoolStage.JuniorHigh);
        Assert.Equal(850, total);
    }

    [Theory]
    // 化学：折分 70，未折分 100
    [InlineData("化学", 65.1, 70)]
    [InlineData("化学", 70.0, 70)]
    [InlineData("化学", 70.5, 100)]
    [InlineData("化学", 98.0, 100)]
    // 历史：折分 80，未折分 100
    [InlineData("历史", 73.6, 80)]
    [InlineData("历史", 80.0, 80)]
    [InlineData("历史", 80.5, 100)]
    [InlineData("历史", 95.0, 100)]
    // 政治/道德与法治：折分 70，未折分 100
    [InlineData("政治", 65.8, 70)]
    [InlineData("政治", 70.0, 70)]
    [InlineData("政治", 72.0, 100)]
    [InlineData("道德与法治", 69.5, 70)]
    [InlineData("道德与法治", 85.0, 100)]
    // 生物/生物学：折分 50，未折分 100
    [InlineData("生物学", 50.0, 50)]
    [InlineData("生物学", 50.5, 100)]
    [InlineData("生物", 45.0, 50)]
    [InlineData("生物", 78.0, 100)]
    // 地理：折分 50，未折分 100
    [InlineData("地理", 49.0, 50)]
    [InlineData("地理", 62.0, 100)]
    // 语文：基准 120，高中/高考 150
    [InlineData("语文", 102.0, 120)]
    [InlineData("语文", 120.0, 120)]
    [InlineData("语文", 128.5, 150)]
    public void RawScore_AutoLeaps_WhenExceedingConvertedFullMark(string subject, double highest, int expectedFull)
    {
        var mark = SubjectMarks.FullMark(subject, highest, SchoolStage.JuniorHigh);
        Assert.Equal(expectedFull, mark);
    }

    [Fact]
    public void SeniorHigh_Uses150ForMainAnd100ForOthers()
    {
        Assert.Equal(150, SubjectMarks.FullMark("语文", null, SchoolStage.SeniorHigh));
        Assert.Equal(150, SubjectMarks.FullMark("数学", null, SchoolStage.SeniorHigh));
        Assert.Equal(150, SubjectMarks.FullMark("英语", null, SchoolStage.SeniorHigh));
        Assert.Equal(100, SubjectMarks.FullMark("物理", null, SchoolStage.SeniorHigh));
        Assert.Equal(100, SubjectMarks.FullMark("化学", null, SchoolStage.SeniorHigh));
        Assert.Equal(100, SubjectMarks.FullMark("历史", null, SchoolStage.SeniorHigh));
        Assert.Equal(100, SubjectMarks.FullMark("生物", null, SchoolStage.SeniorHigh));
        Assert.Equal(100, SubjectMarks.FullMark("政治", null, SchoolStage.SeniorHigh));
        Assert.Equal(100, SubjectMarks.FullMark("地理", null, SchoolStage.SeniorHigh));
    }

    [Fact]
    public void FixtureData_NineGradeSixClass_AccuratelyDerives850FullMarks()
    {
        var fixturePath = Path.Combine("..", "..", "..", "..", "fixtures", "智学网-九年级6班-第一次段考.xlsx");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.Combine("tests", "fixtures", "智学网-九年级6班-第一次段考.xlsx");
        }
        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");

        var data = GradeSheetImporter.Load(fixturePath);
        var grade = GradeDetector.Detect(data.ExamName, data.ClassName, data.SourceTitle);

        Assert.Equal(SchoolStage.JuniorHigh, grade.Stage);

        var expectedMarks = new Dictionary<string, int>
        {
            ["语文"] = 120,
            ["数学"] = 120,
            ["英语"] = 120,
            ["物理"] = 100,
            ["化学"] = 70,  // 最高 65.1 <= 70
            ["生物学"] = 50, // 最高 50.0 <= 50
            ["政治"] = 70,  // 最高 65.8 <= 70
            ["历史"] = 80,  // 最高 73.6 <= 80
            ["地理"] = 50,  // 最高 49.0 <= 50
            ["体育"] = 70,  // 最高 70.0 <= 70
        };

        var derivedMarks = new Dictionary<string, int>();
        foreach (var sub in data.Subjects)
        {
            var maxScore = data.Students
                .Select(s => s[sub].Score)
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .Max();

            var full = SubjectMarks.FullMark(sub, maxScore, grade);
            derivedMarks[sub] = full;

            Assert.Equal(expectedMarks[sub], full);
        }

        var totalFull = derivedMarks.Values.Sum();
        Assert.Equal(850, totalFull);
    }
}
