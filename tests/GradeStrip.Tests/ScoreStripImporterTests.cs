using System.IO;
using System.Linq;
using GradeStrip.Core.Comparison;
using GradeStrip.Core.Export;
using GradeStrip.Core.Import;
using GradeStrip.Core.Model;
using Xunit;

namespace GradeStrip.Tests;

public class ScoreStripImporterTests
{
    private static string GetFixturePath()
    {
        var fixturePath = Path.Combine("..", "..", "..", "..", "fixtures", "智学网-九年级6班-第一次段考.xlsx");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.Combine("tests", "fixtures", "智学网-九年级6班-第一次段考.xlsx");
        }
        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");
        return fixturePath;
    }

    [Fact]
    public void ScoreStripImporter_RoundTrip_DefaultClassRankMode_RestoresAllStudentsAcrossSheets()
    {
        var fixturePath = GetFixturePath();
        var original = GradeSheetImporter.Load(fixturePath);
        Assert.Equal(49, original.Students.Count);
        Assert.Equal(10, original.Subjects.Count);

        var tempFile = Path.Combine(Path.GetTempPath(), $"gradestrip_roundtrip_{Path.GetRandomFileName()}.xlsx");
        try
        {
            // 导出成绩条（按默认 7 人首表、后续每表 8 人切分，共 7 个工作表）
            var exportResult = ScoreStripExporter.Export(original, tempFile, new StripExportOptions
            {
                UseSchoolRank = false
            });
            Assert.True(exportResult.Sheets.Count > 1, "应该切分为多个工作表");

            // 反向导入
            var imported = GradeSheetImporter.Load(tempFile);

            // 1. 班级与考试信息
            Assert.Equal(original.ClassName, imported.ClassName);
            Assert.Contains("第一次段考", imported.ExamName);

            // 2. 科目列表顺序一致
            Assert.Equal(original.Subjects, imported.Subjects);

            // 3. 学生总人数一致（跨全部分表完整汇聚）
            Assert.Equal(original.Students.Count, imported.Students.Count);

            // 4. 首位学生（Sheet 1 第 1 名）对比
            var s1Orig = original.Students[0];
            var s1Imp = imported.Students[0];
            Assert.Equal(s1Orig.No, s1Imp.No);
            Assert.Equal(s1Orig.Name, s1Imp.Name);
            Assert.Equal(s1Orig.ExamNo, s1Imp.ExamNo);
            Assert.Equal(s1Orig.Total.Score, s1Imp.Total.Score);
            Assert.Equal(s1Orig.Total.ClassRank, s1Imp.Total.ClassRank);
            Assert.Equal(s1Orig.Total.SchoolRank, s1Imp.Total.SchoolRank);

            foreach (var sub in original.Subjects)
            {
                Assert.Equal(s1Orig[sub].Score, s1Imp[sub].Score);
                Assert.Equal(s1Orig[sub].ClassRank, s1Imp[sub].ClassRank);
            }

            // 5. 跨表学生验证：Sheet 2 第一位学生（No 8）
            var s8Orig = original.Students[7];
            var s8Imp = imported.Students[7];
            Assert.Equal(s8Orig.No, s8Imp.No);
            Assert.Equal(s8Orig.Name, s8Imp.Name);
            Assert.Equal(s8Orig.Total.Score, s8Imp.Total.Score);
            Assert.Equal(s8Orig.Total.ClassRank, s8Imp.Total.ClassRank);

            // 6. 最终一位学生（Sheet 7 最后一名，No 49）
            var sLastOrig = original.Students[^1];
            var sLastImp = imported.Students[^1];
            Assert.Equal(sLastOrig.No, sLastImp.No);
            Assert.Equal(sLastOrig.Name, sLastImp.Name);
            Assert.Equal(sLastOrig.Total.Score, sLastImp.Total.Score);
            Assert.Equal(sLastOrig.Total.ClassRank, sLastImp.Total.ClassRank);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ScoreStripImporter_RoundTrip_SchoolRankMode_RestoresSchoolRanksAndDerivesClassRanks()
    {
        var fixturePath = GetFixturePath();
        var original = GradeSheetImporter.Load(fixturePath);

        var tempFile = Path.Combine(Path.GetTempPath(), $"gradestrip_school_{Path.GetRandomFileName()}.xlsx");
        try
        {
            // 导出校排模式成绩条
            ScoreStripExporter.Export(original, tempFile, new StripExportOptions
            {
                UseSchoolRank = true
            });

            var imported = GradeSheetImporter.Load(tempFile);

            Assert.Equal(49, imported.Students.Count);

            for (var i = 0; i < original.Students.Count; i++)
            {
                var orig = original.Students[i];
                var imp = imported.Students[i];

                Assert.Equal(orig.Name, imp.Name);
                Assert.Equal(orig.Total.Score, imp.Total.Score);
                Assert.Equal(orig.Total.SchoolRank, imp.Total.SchoolRank);
                Assert.Equal(orig.Total.ClassRank, imp.Total.ClassRank);

                foreach (var sub in original.Subjects)
                {
                    Assert.Equal(orig[sub].Score, imp[sub].Score);
                    Assert.Equal(orig[sub].SchoolRank, imp[sub].SchoolRank);
                    // 派生的班级排名非空且在合理区间
                    if (orig[sub].HasScore)
                    {
                        Assert.NotNull(imp[sub].ClassRank);
                        Assert.InRange(imp[sub].ClassRank!.Value, 1, 49);
                    }
                }
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ScoreStripImporter_RoundTrip_WithDeltas_AccuratelyRestoresDeltaValues()
    {
        var fixturePath = GetFixturePath();
        var examA = GradeSheetImporter.Load(fixturePath);

        // 构造有进退步的对比数据
        var examB = ExamComparisonEngine.CreateComparedExam(examA, examA);

        var tempFile = Path.Combine(Path.GetTempPath(), $"gradestrip_deltas_{Path.GetRandomFileName()}.xlsx");
        try
        {
            ScoreStripExporter.Export(examB, tempFile, new StripExportOptions
            {
                UseSchoolRank = false
            });

            var imported = GradeSheetImporter.Load(tempFile);
            Assert.Equal(49, imported.Students.Count);

            // 验证总分与各科进退步与导出的对比数据完全一致
            for (var i = 0; i < examB.Students.Count; i++)
            {
                var exp = examB.Students[i];
                var imp = imported.Students[i];
                Assert.Equal(exp.Total.ClassDelta, imp.Total.ClassDelta);
                Assert.Equal(exp.Total.SchoolDelta, imp.Total.SchoolDelta);
                foreach (var sub in imported.Subjects)
                {
                    Assert.Equal(exp[sub].ClassDelta, imp[sub].ClassDelta);
                }
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ScoreStripImporter_WithoutTitleOrLegend_StillRecognizesAndImports()
    {
        var fixturePath = GetFixturePath();
        var original = GradeSheetImporter.Load(fixturePath);

        var tempFile = Path.Combine(Path.GetTempPath(), $"九年级6班-无标题成绩条_{Path.GetRandomFileName()}.xlsx");
        try
        {
            // 不包含大标题与图例
            ScoreStripExporter.Export(original, tempFile, new StripExportOptions
            {
                IncludeTitle = false,
                IncludeLegend = false
            });

            var imported = GradeSheetImporter.Load(tempFile);
            Assert.Equal(49, imported.Students.Count);
            Assert.Equal(original.Subjects, imported.Subjects);
            Assert.Equal(original.Students[0].Name, imported.Students[0].Name);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ScoreStripImporter_Overview_ComputesKpisAndDistributionAccurately()
    {
        var fixturePath = GetFixturePath();
        var original = GradeSheetImporter.Load(fixturePath);

        var tempFile = Path.Combine(Path.GetTempPath(), $"gradestrip_overview_{Path.GetRandomFileName()}.xlsx");
        try
        {
            ScoreStripExporter.Export(original, tempFile);
            var imported = GradeSheetImporter.Load(tempFile);

            // 1. KPI 校验
            var origTotals = original.Students.Where(s => s.Total.Score.HasValue).Select(s => s.Total.Score!.Value).ToList();
            var impTotals = imported.Students.Where(s => s.Total.Score.HasValue).Select(s => s.Total.Score!.Value).ToList();
            Assert.Equal(origTotals.Count, impTotals.Count);
            Assert.Equal(origTotals.Average(), impTotals.Average(), 2);
            Assert.Equal(origTotals.Max(), impTotals.Max());
            Assert.Equal(origTotals.Min(), impTotals.Min());

            // 2. 学科均分与满分校验
            foreach (var sub in original.Subjects)
            {
                var origScores = original.Students.Select(s => s[sub].Score).Where(v => v.HasValue).Select(v => v!.Value).ToList();
                var impScores = imported.Students.Select(s => s[sub].Score).Where(v => v.HasValue).Select(v => v!.Value).ToList();
                Assert.Equal(origScores.Count, impScores.Count);
                Assert.Equal(origScores.Average(), impScores.Average(), 2);
                Assert.Equal(origScores.Max(), impScores.Max());
                Assert.Equal(origScores.Min(), impScores.Min());
            }

            // 3. 分档统计校验 (1-3, 4-10, 11-20, 21-35, 36+)
            var tiers = new (int Min, int Max)[] { (1, 3), (4, 10), (11, 20), (21, 35), (36, 100) };
            foreach (var (min, max) in tiers)
            {
                var origCount = original.Students.Count(s => s.Total.ClassRank is { } r && r >= min && r <= max);
                var impCount = imported.Students.Count(s => s.Total.ClassRank is { } r && r >= min && r <= max);
                Assert.Equal(origCount, impCount);
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void GradeSheetImporter_FallbackToSimpleSheet_WhenSummarySheetIncomplete_AndEnrichesTotalDeltas()
    {
        var path = @"C:\Users\Evan Evan\Downloads\示例市文化中学2025-2026学年第一学期八年级期末考试-八年级6班-1.xlsx";
        if (!File.Exists(path)) return;

        var data = GradeSheetImporter.Load(path);
        Assert.Equal(51, data.Students.Count);
        Assert.Equal(9, data.Subjects.Count);
        Assert.Equal("八6班", data.ClassName);
        Assert.Contains("2025-2026", data.ExamName);

        // 验证提示信息
        Assert.NotNull(data.ImportNotice);
        Assert.Contains("智学网", data.ImportNotice);
        Assert.Contains("班级全学科成绩汇总", data.ImportNotice);
        Assert.Contains("班级全学科成绩简表", data.ImportNotice);

        // 验证总分进退步已从 Sheet 2 (汇总表) 自动补齐
        var s1 = data.Students[0];
        Assert.Equal("王知行", s1.Name);
        Assert.Equal(-6, s1.Total.SchoolDelta);
        Assert.Equal(0, s1.Total.ClassDelta);

        // 验证该数据可以直接导出成绩条
        var tempOut = Path.Combine(Path.GetTempPath(), $"eight_grade_strips_{Path.GetRandomFileName()}.xlsx");
        try
        {
            var res = ScoreStripExporter.Export(data, tempOut);
            Assert.Equal(51, res.StudentCount);
            Assert.True(res.Sheets.Count >= 7);
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }
}
