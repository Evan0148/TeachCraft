using ClosedXML.Excel;
using GradeWeave.Core.Models;

namespace GradeWeave.Tests;

/// <summary>
/// 生成完全合成的工作簿，供导入测试使用。
/// 学校、教师、电话均为虚构，不含任何真实可识别信息。
/// </summary>
internal static class SyntheticWorkbookFactory
{
    public const string SchoolName = "示例中学";
    public const string GradeName = "九年级";
    public const string TermName = "2026年秋季学期";

    public static readonly string[] ClassNames =
        ["九（1）班", "九（2）班", "九（3）班", "九（4）班", "九（5）班", "九（6）班"];

    private static readonly string[] HeadTeachers =
        ["沈青禾", "顾知远", "程叙白", "温以宁", "陆明川", "苏映棠"];

    private static readonly string[] ChineseTeachers =
        ["沈青禾", "沈青禾", "沈 青禾", "陆明川", "陆明川", "陆明川"];

    private static readonly string[] MathTeachers =
        ["顾知远", "顾知远", "顾知远", "苏映棠", "苏映棠", "苏映棠"];

    private static readonly string[] EnglishTeachers =
        ["程叙白", "程叙白", "程叙白", "叶临溪", "叶临溪", "叶临溪"];

    private static readonly string[] PhysicsTeachers =
        ["温以宁", "温以宁", "温以宁", "韩知微", "韩知微", "韩知微"];

    private const string MeetingNoteText =
        "会议活动备注：每周科组活动安排（集备或公开课）：周二上午物化科组，下午英语科组；" +
        "周三上午政史科组；周三上午1、2节地理科组；周三下午数学科组；周四上午生物科组；" +
        "周四1、2节综合科组，3、4节语文科组。科组长、备课组长会议，周二6、7节。" +
        "班主任会议，周一6、7节。周二7、8节全体教师大会。" +
        "周五上午 校办会、行政会、年级管委会。升旗、早读等说明见原表。";

    public static string CreateWorkbook()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "GradeWeave.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "示例中学九年级教师分工表.xlsx");

        using var workbook = new XLWorkbook();
        BuildStaffingSheet(workbook);
        BuildConfirmSheet(workbook);
        BuildScheduleSheet(workbook);
        workbook.SaveAs(path);

        return path;
    }

    public static void Cleanup(string workbookPath)
    {
        var directory = Path.GetDirectoryName(workbookPath);
        if (directory is not null && Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private static void BuildStaffingSheet(XLWorkbook workbook)
    {
        var sheet = workbook.AddWorksheet("分工表");
        var titleRange = sheet.Range(1, 1, 1, 6);
        titleRange.Merge();
        titleRange.FirstCell().Value = $"{SchoolName} {GradeName} 2026 年秋季学期 年级课表教师分工表";

        string[] headers = ["班级", "班主任", "语文", "数学", "英语", "物理"];
        for (var column = 1; column <= headers.Length; column++)
            sheet.Cell(2, column).Value = headers[column - 1];

        var row = 3;
        for (var i = 0; i < ClassNames.Length; i++)
        {
            sheet.Cell(row, 1).Value = ClassNames[i];
            sheet.Cell(row, 2).Value = HeadTeachers[i];
            sheet.Cell(row, 3).Value = ChineseTeachers[i];
            sheet.Cell(row, 4).Value = MathTeachers[i];
            sheet.Cell(row, 5).Value = EnglishTeachers[i];
            sheet.Cell(row, 6).Value = PhysicsTeachers[i];

            sheet.Cell(row + 1, 1).Value = "课时-37节";
            sheet.Cell(row + 1, 3).Value = 6;
            sheet.Cell(row + 1, 4).Value = 6;
            sheet.Cell(row + 1, 5).Value = 6;
            sheet.Cell(row + 1, 6).Value = 4;

            sheet.Cell(row + 2, 1).Value = "人数-62人";
            row += 3;
        }
    }

    private static void BuildConfirmSheet(XLWorkbook workbook)
    {
        var sheet = workbook.AddWorksheet("确认表");
        string[] headers =
            ["序号", "姓名", "学科", "兼职岗位", "任教班级", "学科周课时", "联系电话"];
        for (var column = 1; column <= headers.Length; column++)
            sheet.Cell(1, column).Value = headers[column - 1];

        (string Name, string Subject, string Roles, int Hours)[] teachers =
        [
            ("沈青禾", "语文", "班主任/科组长", 6),
            ("陆明川", "语文", "班主任/备课组长", 6),
            ("顾知远", "数学", "班主任/年级长", 6),
            ("苏映棠", "数学", "班主任/德育处干事", 6),
            ("程叙白", "英语", "班主任/备课组长", 6),
            ("叶临溪", "英语", "科组长", 6),
            ("温以宁", "物理", "班主任/党政办副主任/科组长", 4),
            ("韩知微", "物理", "备课组长", 4),
        ];

        for (var i = 0; i < teachers.Length; i++)
        {
            var row = i + 2;
            sheet.Cell(row, 1).Value = i + 1;
            sheet.Cell(row, 2).Value = teachers[i].Name;
            sheet.Cell(row, 3).Value = teachers[i].Subject;
            sheet.Cell(row, 4).Value = teachers[i].Roles;
            sheet.Cell(row, 5).Value = "九（1）班、九（2）班";
            sheet.Cell(row, 6).Value = teachers[i].Hours;
            sheet.Cell(row, 7).Value = $"000-0000-{i + 1:0000}";
        }
    }

    private static void BuildScheduleSheet(XLWorkbook workbook)
    {
        var sheet = workbook.AddWorksheet("课程表");
        sheet.Cell(1, 1).Value = $"{SchoolName} {GradeName} 课程表";

        sheet.Cell(2, 1).Value = "班级";
        for (var day = 0; day < WeaveDefaults.Days.Length; day++)
        {
            var start = 2 + day * 10;
            var range = sheet.Range(2, start, 2, start + 9);
            range.Merge();
            range.FirstCell().Value = WeaveDefaults.Days[day];

            sheet.Cell(3, start + 7).Value = "课后服务1";
            sheet.Cell(3, start + 8).Value = "课后服务2";
            sheet.Cell(3, start + 9).Value = "晚修";

            for (var period = 1; period <= 7; period++)
                sheet.Cell(3, start + period - 1).Value = period;
        }

        string[] subjects = ["语文", "数学", "英语", "物理"];
        string[][] teachers = [ChineseTeachers, MathTeachers, EnglishTeachers, PhysicsTeachers];

        for (var classIndex = 0; classIndex < ClassNames.Length; classIndex++)
        {
            var row = 4 + classIndex;
            sheet.Cell(row, 1).Value = ClassNames[classIndex];

            for (var day = 0; day < WeaveDefaults.Days.Length; day++)
            {
                var start = 2 + day * 10;
                for (var period = 1; period <= 7; period++)
                {
                    var subjectIndex = (day + period) % subjects.Length;
                    sheet.Cell(row, start + period - 1).Value =
                        subjects[subjectIndex] + teachers[subjectIndex][classIndex];
                }
            }
        }

        sheet.Cell(4, 2).Value = "语文沈青禾";
        sheet.Cell(4, 12).Value = "物理温以宁";
        sheet.Cell(5, 12).Value = "班会沈青禾";
        sheet.Cell(9, 37).Value = "轮流";
        sheet.Cell(9, 48).Value = "具体安排见附件1";
        sheet.Cell(11, 1).Value = MeetingNoteText;
    }
}
