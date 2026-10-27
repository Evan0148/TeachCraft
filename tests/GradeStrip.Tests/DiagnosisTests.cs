using System.IO;
using GradeStrip.Core.Analysis;
using GradeStrip.Core.Import;
using Xunit;

namespace GradeStrip.Tests;

public class DiagnosisTests
{
    [Theory]
    [InlineData("2026-2027学年第一学期九年级第一次段考", GradeLevel.Junior9, "九年级 (初三)")]
    [InlineData("初三一模联考", GradeLevel.Junior9, "九年级 (初三)")]
    [InlineData("八年级第二学期期中考试", GradeLevel.Junior8, "八年级 (初二)")]
    [InlineData("七年级期末学业质量评估", GradeLevel.Junior7, "七年级 (初一)")]
    [InlineData("高三年级第一次模拟考", GradeLevel.Senior3, "高三年级")]
    [InlineData("高一(2)班月考", GradeLevel.Senior1, "高一年级")]
    public void GradeDetector_DetectsAccurately(string text, GradeLevel expectedLevel, string expectedDisplay)
    {
        var grade = GradeDetector.Detect(text);
        Assert.Equal(expectedLevel, grade.Level);
        Assert.Equal(expectedDisplay, grade.DisplayName);
    }

    [Fact]
    public void DiagnosisEngine_GeneratesRichDiagnosis_ForJunior9Data()
    {
        var fixturePath = Path.Combine("..", "..", "..", "..", "fixtures", "智学网-九年级6班-第一次段考.xlsx");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.Combine("tests", "fixtures", "智学网-九年级6班-第一次段考.xlsx");
        }

        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");

        var data = GradeSheetImporter.Load(fixturePath);
        var grade = GradeDetector.Detect(data.ExamName, data.ClassName, data.SourceTitle);

        Assert.Equal(GradeLevel.Junior9, grade.Level);

        // 分析数学学科
        var diag = SubjectDiagnosisEngine.Analyze(data, "数学", grade);

        Assert.Equal("数学", diag.SubjectName);
        Assert.Contains("九年级", diag.GradeBadge);
        Assert.Contains("中考", diag.MacroAssessment);
        Assert.NotEmpty(diag.ActionStrategies);
        Assert.True(diag.ActionStrategies.Count >= 3);

        // 验证临界生诊断
        Assert.Contains("中考临界生档案", diag.BorderlineAssessment);
    }
}
