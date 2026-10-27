using ClosedXML.Excel;

namespace TeachCraft.Tests;

/// <summary>按真实总表骨架造的小号合成工作簿：课程表 + 分工表 + 午学 + 课后服务。全部为合成数据。</summary>
internal static class ClassBlockFixtures
{
    internal static readonly string[] DayHeaders = { "星期一", "星期二", "星期三", "星期四", "星期五" };

    /// <summary>按真实总表的骨架造一份小号工作簿：课程表 + 分工表 + 午学 + 课后服务。</summary>
    public static string CreateMasterWorkbook(string rootDirectory, string fileName = "master.xlsx")
    {
        var path = Path.Combine(rootDirectory, fileName);

        using var workbook = new XLWorkbook();
        BuildScheduleSheet(workbook);
        BuildHeadTeacherSheet(workbook);
        BuildLunchStudySheet(workbook);
        BuildAfterSchoolSheet(workbook);
        workbook.SaveAs(path);

        return path;
    }

    private static void BuildScheduleSheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("2-课程表");

        sheet.Cell(1, 1).Value = "表二：示例中学2026年春季学期九年级教师课程表（时间：2026年5月）";

        var starts = new[] { 2, 12, 22, 32, 42 };
        for (var day = 0; day < 5; day++)
        {
            var start = starts[day];
            sheet.Range(sheet.Cell(2, start), sheet.Cell(2, start + 9)).Merge();
            sheet.Cell(2, start).Value = DayHeaders[day];

            for (var period = 0; period < 7; period++)
                sheet.Cell(4, start + period).Value = period + 1;

            sheet.Range(sheet.Cell(4, start + 7), sheet.Cell(4, start + 8)).Merge();
            sheet.Cell(4, start + 7).Value = "课后服务";
        }

        sheet.Cell(5, 1).Value = "九（1）班";
        sheet.Cell(6, 1).Value = "九（2）班";

        string[] subjects = { "语文张兰", "数学李梅", "英语王芳", "物理赵强", "化学陈静", "历史周敏", "体育吴刚" };
        for (var day = 0; day < 5; day++)
        {
            var start = starts[day];
            for (var period = 0; period < 7; period++)
            {
                sheet.Cell(5, start + period).Value = subjects[period];
                sheet.Cell(6, start + period).Value = subjects[(period + 1) % 7];
            }

            sheet.Cell(5, start + 9).Value = "张兰";
            sheet.Cell(6, start + 9).Value = "李梅";

            sheet.Cell(5, start + 8).Value = "阳光体育";
            sheet.Cell(6, start + 8).Value = "自习";
        }

        sheet.Range("I5:I6").Merge();
        sheet.Cell(5, 9).Value = "培优辅差";

        sheet.Cell(8, 1).Value = "备注：早读（二、四语文，三、五英语），周一升旗。";
    }

    private static void BuildHeadTeacherSheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("1-分工表");
        sheet.Cell(2, 1).Value = "班级";
        sheet.Cell(2, 2).Value = "班主任";
        sheet.Cell(3, 1).Value = "九（1）班";
        sheet.Cell(3, 2).Value = "王兰";
        sheet.Cell(4, 1).Value = "九（2）班";
        sheet.Cell(4, 2).Value = "李梅";
    }

    private static void BuildLunchStudySheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("4-午学");
        sheet.Cell(2, 1).Value = "班级";
        string[] values = { "周兰", "吴梅", "郑兰", "王梅", "周兰" };

        for (var day = 0; day < 5; day++)
            sheet.Cell(2, day + 2).Value = $"{DayHeaders[day]} 12:00—12:30";

        sheet.Cell(3, 1).Value = "九（1）班";
        sheet.Cell(4, 1).Value = "九（2）班";
        for (var day = 0; day < 5; day++)
        {
            sheet.Cell(3, day + 2).Value = values[day];
            sheet.Cell(4, day + 2).Value = values[(day + 1) % 5];
        }
    }

    private static void BuildAfterSchoolSheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("5-课后服务");

        var starts = new[] { 2, 5, 8, 11, 14 };
        for (var day = 0; day < 5; day++)
        {
            var start = starts[day];
            sheet.Range(sheet.Cell(2, start), sheet.Cell(2, start + 2)).Merge();
            sheet.Cell(2, start).Value = DayHeaders[day];
            sheet.Cell(3, start).Value = "课后服务1";
            sheet.Cell(3, start + 1).Value = "课后服务2";
            sheet.Cell(3, start + 2).Value = "晚修";
        }

        sheet.Cell(4, 1).Value = "班级";
        sheet.Cell(5, 1).Value = "九（1）班";
        sheet.Cell(6, 1).Value = "九（2）班";

        for (var day = 0; day < 5; day++)
        {
            var start = starts[day];
            sheet.Cell(5, start).Value = "赵兰";
            sheet.Cell(5, start + 1).Value = "钱红";
            sheet.Cell(5, start + 2).Value = "孙丽";
            sheet.Cell(6, start).Value = "李梅";
            sheet.Cell(6, start + 1).Value = "周敏";
            sheet.Cell(6, start + 2).Value = "吴刚";
        }
    }
}
