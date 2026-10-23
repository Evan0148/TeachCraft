using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using SeatGrid.Core.Models;
using SeatGrid.Core.Rendering;

namespace SeatGrid.Core.Export;

/// <summary>把当前座位表导出成一份带颜色与边框的 Excel，并附一张完整名单。</summary>
public static class SeatChartExcelExporter
{
    public static void ExportSeatChart(
        SeatChartDocument document,
        IReadOnlyDictionary<SeatPosition, Student> assignments,
        IReadOnlyList<Student> students,
        string path)
    {
        var settings = document.Settings;
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("座位表");

        var hints = settings.ShowGroupRowHints;
        var firstSeatColumn = hints ? 2 : 1;
        var seatColumns = document.Layout.Groups * document.Layout.Columns + (document.Layout.Groups - 1);
        var lastColumn = firstSeatColumn + seatColumns - 1;

        var cursor = 1;

        // 标题
        var titleCell = sheet.Cell(cursor, 1);
        sheet.Range(cursor, 1, cursor, lastColumn).Merge();
        titleCell.Value = document.Title;
        titleCell.Style.Font.FontName = "Microsoft YaHei";
        titleCell.Style.Font.FontSize = 18;
        titleCell.Style.Font.Bold = settings.BoldTitle;
        titleCell.Style.Font.FontColor = XLColor.FromHtml("#141413");
        titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titleCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(cursor).Height = 32;
        cursor++;

        // 信息行
        var infoCell = sheet.Cell(cursor, 1);
        sheet.Range(cursor, 1, cursor, lastColumn).Merge();
        infoCell.Value = document.InfoLine;
        infoCell.Style.Font.FontName = "Microsoft YaHei";
        infoCell.Style.Font.FontSize = 9.5;
        infoCell.Style.Font.FontColor = XLColor.FromHtml("#85806F");
        infoCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        infoCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(cursor).Height = 18;
        cursor++;

        if (settings.ShowGroupRowHints)
            sheet.Column(1).Width = 6;

        for (var group = 0; group < document.Layout.Groups; group++)
        {
            var groupStart = firstSeatColumn + group * (document.Layout.Columns + 1);
            for (var column = 0; column < document.Layout.Columns; column++)
                sheet.Column(groupStart + column).Width = Math.Max(6, settings.CellSizePx / 7.0);

            if (group < document.Layout.Groups - 1)
                sheet.Column(groupStart + document.Layout.Columns).Width = 2;
        }

        if (document.PodiumOnTop)
        {
            DrawPodium(sheet, cursor, firstSeatColumn, lastColumn, settings);
            cursor++;
        }

        // 组标题
        if (settings.ShowGroupRowHints)
        {
            for (var groupIndex = 0; groupIndex < document.Groups.Count; groupIndex++)
            {
                var group = document.Groups[groupIndex];
                var start = firstSeatColumn + groupIndex * (document.Layout.Columns + 1);
                var end = start + document.Layout.Columns - 1;
                sheet.Range(cursor, start, cursor, end).Merge();
                var cell = sheet.Cell(cursor, start);
                cell.Value = group.Label;
                cell.Style.Font.FontName = "Microsoft YaHei";
                cell.Style.Font.FontSize = 9;
                cell.Style.Font.FontColor = XLColor.FromHtml(settings.GroupLabel);
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            sheet.Row(cursor).Height = 18;
            cursor++;
        }

        // 座位
        var rowCount = document.Groups.Count == 0 ? 0 : document.Groups[0].Rows.Count;

        for (var displayRow = 0; displayRow < rowCount; displayRow++)
        {
            if (settings.ShowGroupRowHints && document.Groups.Count > 0)
            {
                var labelCell = sheet.Cell(cursor, 1);
                labelCell.Value = document.Groups[0].Rows[displayRow].Label;
                labelCell.Style.Font.FontName = "Microsoft YaHei";
                labelCell.Style.Font.FontSize = 8.5;
                labelCell.Style.Font.FontColor = XLColor.FromHtml(settings.RowLabel);
                labelCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                labelCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            for (var groupIndex = 0; groupIndex < document.Groups.Count; groupIndex++)
            {
                var group = document.Groups[groupIndex];
                var row = group.Rows[displayRow];
                var groupStart = firstSeatColumn + groupIndex * (document.Layout.Columns + 1);

                for (var columnIndex = 0; columnIndex < row.Seats.Count; columnIndex++)
                {
                    var seat = row.Seats[columnIndex];
                    var cell = sheet.Cell(cursor, groupStart + columnIndex);
                    cell.Value = seat.Name;

                    cell.Style.Font.FontName = "Microsoft YaHei";
                    cell.Style.Font.FontSize = Math.Max(7, settings.NameFontSize * 0.75);
                    cell.Style.Font.Bold = settings.BoldNames;
                    cell.Style.Font.FontColor = XLColor.FromHtml(
                        !seat.IsEmpty && settings.MarkFemales && seat.IsFemale
                            ? settings.FemaleMark
                            : settings.SeatText);

                    if (!seat.IsEmpty)
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(settings.SeatBackground);
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = XLColor.FromHtml(settings.SeatBorder);
                    }
                    else if (!settings.HideEmptySeats)
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(settings.EmptySeatBackground);
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Dashed;
                        cell.Style.Border.OutsideBorderColor = XLColor.FromHtml(settings.SeatBorder);
                    }

                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    cell.Style.Alignment.WrapText = false;
                }
            }

            sheet.Row(cursor).Height = Math.Max(22, settings.CellSizePx * 0.75);
            cursor++;
        }

        if (!document.PodiumOnTop)
        {
            DrawPodium(sheet, cursor, firstSeatColumn, lastColumn, settings);
            cursor++;
        }

        // 名单页
        var roster = workbook.Worksheets.Add("名单");
        var headers = new[]
        {
            "序号", "姓名", "性别", "学号", "身高", "视力", "成绩", "原小组", "备注", "组", "排", "列",
        };

        for (var i = 0; i < headers.Length; i++)
        {
            var cell = roster.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.FontName = "Microsoft YaHei";
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F2EC");
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#E7E3D8");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        var seatLookup = assignments
            .GroupBy(pair => pair.Value.Id)
            .ToDictionary(g => g.Key, g => g.First().Key);

        for (var index = 0; index < students.Count; index++)
        {
            var student = students[index];
            var row = index + 2;
            seatLookup.TryGetValue(student.Id, out var seat);

            roster.Cell(row, 1).Value = index + 1;
            roster.Cell(row, 2).Value = student.Name;
            roster.Cell(row, 3).Value = student.GenderLabel;
            roster.Cell(row, 4).Value = student.Number;
            roster.Cell(row, 5).Value = student.Height is null ? string.Empty : student.Height.Value.ToString("0.#");
            roster.Cell(row, 6).Value = student.Vision is null ? string.Empty : student.Vision.Value.ToString("0.0");
            roster.Cell(row, 7).Value = student.Score is null ? string.Empty : student.Score.Value.ToString("0.##");
            roster.Cell(row, 8).Value = student.OriginalGroup;
            roster.Cell(row, 9).Value = student.Notes;
            roster.Cell(row, 10).Value = seat.Group >= 0 ? seat.Group + 1 : string.Empty;
            roster.Cell(row, 11).Value = seat.Row >= 0 ? seat.Row + 1 : string.Empty;
            roster.Cell(row, 12).Value = seat.Column >= 0 ? seat.Column + 1 : string.Empty;
        }

        roster.Columns().AdjustToContents();
        roster.SheetView.FreezeRows(1);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        workbook.SaveAs(path);
    }

    private static void DrawPodium(IXLWorksheet sheet, int row, int firstColumn, int lastColumn, SeatChartSettings settings)
    {
        var range = sheet.Range(row, firstColumn, row, lastColumn);
        range.Merge();
        var cell = sheet.Cell(row, firstColumn);
        cell.Value = "讲　台";
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(settings.PodiumBackground);
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.OutsideBorderColor = XLColor.FromHtml(settings.SeatBorder);
        cell.Style.Font.FontName = "Microsoft YaHei";
        cell.Style.Font.FontSize = 10;
        cell.Style.Font.FontColor = XLColor.FromHtml(settings.PodiumText);
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(row).Height = 24;
    }
}
