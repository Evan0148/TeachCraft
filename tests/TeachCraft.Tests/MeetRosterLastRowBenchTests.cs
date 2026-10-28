using ClosedXML.Excel;

namespace TeachCraft.Tests;

/// <summary>
/// Plan 3.0 实测：模板把格式刷到几千行时，LastRowUsed() 的行数虚高与代价。
/// 断言只锁"行为差异存在与否"，耗时打印出来供报告取数（outputs/review/meetroster-lastrow-bench.md）。
/// </summary>
public sealed class MeetRosterLastRowBenchTests
{
    [Fact]
    public void LastRowUsed_Vs_ContentsOnly_OnFormatPaintedSheet()
    {
        var path = Path.Combine(Path.GetTempPath(), "MeetRoster.Bench", Guid.NewGuid().ToString("N") + ".xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            const int dataRows = 20;
            const int paintedRows = 5000;
            using (var workbook = new XLWorkbook())
            {
                var build = workbook.AddWorksheet("导入模板");
                build.Cell(1, 1).Value = "示例中学田径运动会";
                build.Cell(2, 1).Value = "参赛单位";
                build.Cell(2, 2).Value = "七年级1班";
                build.Cell(7, 1).Value = "姓名";
                build.Cell(7, 2).Value = "性别";
                for (var i = 0; i < dataRows; i++)
                {
                    build.Cell(8 + i, 1).Value = $"学生{i + 1:00}";
                    build.Cell(8 + i, 2).Value = i % 2 == 0 ? "女子" : "男子";
                }

                // 模拟"格式刷到底"的两种形态：
                // (a) range 级样式（ClosedXML 可能只在样式层记录）；
                // (b) 逐格设置样式（真实 Excel 用户手动刷格式的形态，xlsx 里每行都有样式索引）。
                var range = build.Range(8, 1, 8 + paintedRows - 1, 7);
                range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                range.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
                for (var row = 28; row <= 8 + paintedRows - 1; row++)
                {
                    // (b) cell 级样式
                    for (var col = 1; col <= 7; col++)
                        build.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#FCFAF4");
                    // (c) row 级样式（Excel 里手动调行高/字体的形态，row 元素无 cell 子元素）
                    build.Row(row).Height = 18;
                }
                workbook.SaveAs(path);
            }

            using var reopened = new XLWorkbook(path);
            var sheet = reopened.Worksheet("导入模板");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var rawLast = sheet.LastRowUsed()?.RowNumber() ?? 0;
            sw.Stop();
            var rawElapsed = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var contentsLast = sheet.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 0;
            sw.Stop();
            var contentsElapsed = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var rowsUsedCount = sheet.RowsUsed(XLCellsUsedOptions.Contents).Count();
            sw.Stop();
            var rowsUsedElapsed = sw.Elapsed.TotalMilliseconds;

            Console.WriteLine(
                $"painted={paintedRows} data={dataRows} | " +
                $"LastRowUsed={rawLast} 行 ({rawElapsed:F1} ms) | " +
                $"LastRowUsed(Contents)={contentsLast} 行 ({contentsElapsed:F1} ms) | " +
                $"RowsUsed(Contents)={rowsUsedCount} 行 ({rowsUsedElapsed:F1} ms)");

            // 行为差异：格式行会把无参版本撑大。若此断言在未来失败（ClosedXML 行为变化），本基准的意义随之消失。
            Assert.Equal(8 + dataRows - 1, contentsLast);
            Assert.True(rawLast >= contentsLast);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
