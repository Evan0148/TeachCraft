using System.IO;
using ClosedXML.Excel;
using GradeStrip.Core.Import;
using GradeStrip.Core.Model;
using Xunit;

namespace GradeStrip.Tests;

public class MultiClassStripFilterTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gradestrip_multiclass_{Path.GetRandomFileName()}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static StudentRecord Student(string name, string className, double total, int? classRank = null)
    {
        return new StudentRecord
        {
            Name = name,
            ClassName = className,
            Subjects = new Dictionary<string, SubjectScore>(StringComparer.Ordinal),
            Total = new SubjectScore { Score = total, ClassRank = classRank },
        };
    }

    private static ExamData Exam(params StudentRecord[] students)
    {
        return new ExamData
        {
            SourceTitle = "全部考生成绩汇总",
            ClassName = string.Empty,
            ExamName = "2026-2027学年第一学期九年级第一次段考",
            Subjects = new[] { "语文", "数学" },
            Students = students,
        };
    }

    [Fact]
    public void AvailableClasses_ReturnsNaturalSortedDistinctClasses()
    {
        var exam = Exam(
            Student("张三", "九2班", 100),
            Student("李四", "九1班", 90),
            Student("王五", "九1班", 80));

        Assert.Equal(new[] { "九1班", "九2班" }, exam.AvailableClasses);
    }

    [Fact]
    public void AvailableClasses_SortsTenAfterNine()
    {
        var exam = Exam(
            Student("张三", "九10班", 100),
            Student("李四", "九2班", 90),
            Student("王五", "九1班", 80));

        Assert.Equal(new[] { "九1班", "九2班", "九10班" }, exam.AvailableClasses);
    }

    [Fact]
    public void AvailableClasses_IgnoresEmptyAndPlaceholder()
    {
        var exam = Exam(
            Student("张三", "九1班", 100),
            Student("李四", string.Empty, 90),
            Student("王五", "-", 80));

        Assert.Equal(new[] { "九1班" }, exam.AvailableClasses);
    }

    /// <summary>按班筛选保留原分数与名次，且不改动原始数据。</summary>
    [Fact]
    public void FilterByClass_KeepsScoresAndRanks()
    {
        var exam = Exam(
            Student("张三", "九1班", 88, 1),
            Student("李四", "九1班", 80, 2),
            Student("王五", "九2班", 95, 1),
            Student("赵六", "九2班", 70, 2));

        var filtered = exam.FilterByClass("九1班");

        Assert.NotSame(exam, filtered);
        Assert.Equal("九1班", filtered.ClassName);
        Assert.Equal(2, filtered.Students.Count);
        Assert.Equal("张三", filtered.Students[0].Name);
        Assert.Equal(88, filtered.Students[0].Total.Score);
        Assert.Equal(1, filtered.Students[0].Total.ClassRank);
        Assert.Equal(4, exam.Students.Count);
        Assert.Equal(string.Empty, exam.ClassName);
    }

    /// <summary>「全年级」、null 与空白都返回当前实例，不复制。</summary>
    [Fact]
    public void FilterByClass_AllGradeOrNull_ReturnsSameInstance()
    {
        var exam = Exam(Student("张三", "九1班", 88));

        Assert.Same(exam, exam.FilterByClass("全年级"));
        Assert.Same(exam, exam.FilterByClass(null));
        Assert.Same(exam, exam.FilterByClass(" "));
    }

    /// <summary>跨班汇总表导入后，班级名留在每个学生身上，可按班筛选。</summary>
    [Fact]
    public void Load_CrossClassWorkbook_StudentsCarryClassName()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "全部考生成绩汇总.xlsx");
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("成绩汇总");
            ws.Cell(1, 1).Value = "全部考生成绩汇总";
            ws.Cell(2, 1).Value = "班级";
            ws.Cell(2, 2).Value = "姓名";
            ws.Cell(2, 3).Value = "语文";
            ws.Cell(2, 4).Value = "数学";
            ws.Cell(3, 1).Value = "九1班";
            ws.Cell(3, 2).Value = "张三";
            ws.Cell(3, 3).Value = 88;
            ws.Cell(3, 4).Value = 95;
            ws.Cell(4, 1).Value = "九2班";
            ws.Cell(4, 2).Value = "李四";
            ws.Cell(4, 3).Value = 90;
            ws.Cell(4, 4).Value = 92;
            workbook.SaveAs(path);

            var exam = GradeSheetImporter.Load(path);

            Assert.Equal(string.Empty, exam.ClassName);
            Assert.Equal(new[] { "九1班", "九2班" }, exam.AvailableClasses);

            var filtered = exam.FilterByClass("九2班");
            Assert.Equal("李四", filtered.Students[0].Name);
            Assert.Equal(182, filtered.Students[0].Total.Score);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
