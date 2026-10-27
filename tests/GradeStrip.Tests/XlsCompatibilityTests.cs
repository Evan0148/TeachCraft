using System.IO;
using System.Text;
using GradeStrip.Core.Import;
using GradeStrip.Core.Model;
using Xunit;

namespace GradeStrip.Tests;

public class XlsCompatibilityTests
{
    private static string GetFixturePath(string fileName)
    {
        var path = Path.Combine("..", "..", "..", "..", "fixtures", fileName);
        if (!File.Exists(path))
        {
            path = Path.Combine("tests", "fixtures", fileName);
        }
        Assert.True(File.Exists(path), $"Fixture file not found: {path}");
        return path;
    }

    [Fact]
    public void Load_Xls_TwoLevelHeaderClassSummary_ParsesSuccessfully()
    {
        // 验证两级表头全科班级汇总表（通过 .xls 格式与 ExcelDataReader 引擎读取）
        var originalXlsx = GetFixturePath("智学网-九年级6班-第一次段考.xlsx");
        var tempXls = Path.Combine(Path.GetTempPath(), $"class_summary_test_{Path.GetRandomFileName()}.xls");

        try
        {
            File.Copy(originalXlsx, tempXls, true);

            var exam = GradeSheetImporter.Load(tempXls);

            Assert.Equal("九6班", exam.ClassName);
            Assert.Equal("2026-2027学年第一学期九年级第一次段考", exam.ExamName);
            Assert.Equal(10, exam.Subjects.Count);
            Assert.Equal(49, exam.Students.Count);

            // 验证首位学生：王知行
            var s1 = exam.Students[0];
            Assert.Equal("王知行", s1.Name);
            Assert.Equal(1, s1.No);
            Assert.Equal("77000018", s1.ExamNo);
            Assert.Equal(102, s1["语文"].Score);
            Assert.Equal(1, s1["语文"].ClassRank);
            Assert.Equal(0, s1["语文"].ClassDelta);
            Assert.Equal(754.9, s1.Total.Score);
            Assert.Equal(1, s1.Total.ClassRank);
            Assert.Equal(0, s1.Total.ClassDelta);

            // 验证尾位学生：秦星河（缺考）
            var s49 = exam.Students[48];
            Assert.Equal("秦星河", s49.Name);
            Assert.Equal(49, s49.No);
            Assert.Equal("77000098", s49.ExamNo);
            Assert.Null(s49.Total.Score);
            Assert.Null(s49["语文"].Score);
        }
        finally
        {
            if (File.Exists(tempXls)) File.Delete(tempXls);
        }
    }

    [Fact]
    public void Load_Xls_MacUnicodeNfdPath_NormalizesAndParsesSuccessfully()
    {
        // 模拟 macOS APFS 文件系统中的 NFD（Normalization Form D）Unicode 分解路径
        var realXls = GetFixturePath("智学网-单科个人成绩单-物理.xls");
        var baseDir = Path.GetTempPath();
        // 创建带中文字符的 NFD 目录名
        var nfdDirName = "测试_Mac_NFD_成绩目录".Normalize(NormalizationForm.FormD);
        var testDir = Path.Combine(baseDir, nfdDirName);
        Directory.CreateDirectory(testDir);

        var nfdFileName = "九年级11班_物理_成绩单.xls".Normalize(NormalizationForm.FormD);
        var testFile = Path.Combine(testDir, nfdFileName);

        try
        {
            File.Copy(realXls, testFile, true);

            // 验证即使传入 macOS NFD 路径，也能被安全规范化并成功解析
            var exam = GradeSheetImporter.Load(testFile);

            Assert.Equal("九年级11班", exam.ClassName);
            Assert.Single(exam.Subjects);
            Assert.Equal("物理", exam.Subjects[0]);
            Assert.Equal(49, exam.Students.Count);
            Assert.Equal("蒋念安", exam.Students[0].Name);
            Assert.Equal(97, exam.Students[0].Total.Score);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void Load_Xls_FullAllSubjectsPersonalReport_ParsesAll46Students()
    {
        // 合成 46 人全科个人成绩单 .xls 样本
        var xlsPath = GetFixturePath("智学网-全科个人成绩单-九14班.xls");
        var exam = GradeSheetImporter.Load(xlsPath);

        Assert.Equal("九14班", exam.ClassName);
        Assert.Equal(10, exam.Subjects.Count);
        Assert.Equal(46, exam.Students.Count);

        var s1 = exam.Students[0];
        Assert.Equal("姚北辰", s1.Name);
        Assert.Equal(562.2, s1.Total.Score);
        Assert.Equal(20, s1.Total.ClassRank);
        Assert.Equal(496, s1.Total.SchoolRank);
        Assert.Equal(74, s1["语文"].Score);
        Assert.Equal(25, s1["语文"].ClassRank);
    }
}
