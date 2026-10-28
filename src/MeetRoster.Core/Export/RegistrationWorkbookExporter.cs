using ClosedXML.Excel;
using MeetRoster.Core.Models;

namespace MeetRoster.Core.Export;

/// <summary>按班回写原始报名表，文件名遵循“**年级**班运动会报名表.xlsx”。</summary>
public static class RegistrationWorkbookExporter
{
    public static string Export(MeetClass meetClass, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(meetClass.SourcePath) || !File.Exists(meetClass.SourcePath))
            throw new InvalidOperationException($"{meetClass.Name} 没有原始报名表，无法按模板回写。");

        Directory.CreateDirectory(outputDirectory);
        var fileName = SafeFileName(meetClass.Name + "运动会报名表") + ".xlsx";
        var target = Path.GetFullPath(Path.Combine(outputDirectory, fileName));
        var source = Path.GetFullPath(meetClass.SourcePath);
        if (string.Equals(target, source, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("导出文件不能覆盖原始报名表，请选择其他目录。");

        Write(meetClass, source, target);
        return target;
    }

    /// <summary>直接保存回原始报名表：先写临时文件，成功后备份为 .bak 再替换，避免写坏原件。
    /// 备份与替换走文件重试（实时扫描会短暂锁定刚写完的文件）；失败时报告具体步骤。</summary>
    public static string Save(MeetClass meetClass)
    {
        if (string.IsNullOrWhiteSpace(meetClass.SourcePath) || !File.Exists(meetClass.SourcePath))
            throw new InvalidOperationException($"{meetClass.Name} 没有原始报名表，无法保存。");

        var target = Path.GetFullPath(meetClass.SourcePath);
        var temp = target + ".saving.xlsx";
        var step = "生成临时文件";
        try
        {
            Write(meetClass, target, temp);
            step = "备份原件（.bak）";
            FileRetry.Run(() => File.Copy(target, target + ".bak", overwrite: true));
            step = "替换原文件";
            ReplaceFile(temp, target);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"{meetClass.Name} 在{step}时失败：{ex.Message}", ex);
        }
        finally
        {
            try
            {
                FileRetry.Run(() =>
                {
                    if (File.Exists(temp)) File.Delete(temp);
                });
            }
            catch
            {
                // 清理失败不影响保存结果，残留的临时文件会被下次保存覆盖。
            }
        }

        return target;
    }

    /// <summary>
    /// 用临时文件替换原文件。File.Move 覆盖需要目标的 DELETE 权限——Windows 索引 /
    /// 实时扫描服务会以“允许写、不允许删”的方式短暂打开新文件，此时降级为直接
    /// 覆盖写入（Copy 只需要写权限），仍失败再重试。
    /// </summary>
    private static void ReplaceFile(string temp, string target)
    {
        FileRetry.Run(() =>
        {
            try
            {
                File.Move(temp, target, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                File.Copy(temp, target, overwrite: true);
                try { File.Delete(temp); } catch (IOException) { }
            }
        });
    }

    private static void Write(MeetClass meetClass, string source, string target)
    {
        // stream 构造：读完即释放句柄（Plan 4.2，与导入侧一致）。
        using var stream = Import.WorkbookIO.OpenReadShared(source);
        using var workbook = new XLWorkbook(stream);
        var sheet = FindSheet(workbook);
        var headerRow = FindHeaderRow(sheet);
        var dataStart = headerRow + 1;
        if (IsExampleRow(sheet, dataStart)) dataStart++;

        sheet.Cell(2, 2).Value = meetClass.Name;
        if (!string.IsNullOrWhiteSpace(meetClass.Code)) sheet.Cell(3, 2).Value = meetClass.Code;
        if (!string.IsNullOrWhiteSpace(meetClass.LeaderContact)) sheet.Cell(4, 2).Value = meetClass.LeaderContact;
        if (!string.IsNullOrWhiteSpace(meetClass.Coach)) sheet.Cell(5, 2).Value = meetClass.Coach;

        var templateEnd = Math.Max(sheet.LastRowUsed()?.RowNumber() ?? dataStart, dataStart + 28);
        // 一次性清空模板区内容（Plan 4.2：替代逐格 196 次 Cell().Clear()）。
        sheet.Range(dataStart, 1, templateEnd, 7).Clear(XLClearOptions.Contents);

        var sourceStyleRow = dataStart;
        // 源样式只取一次缓存复用（Plan 4.2：替代循环内每行每列重复解析）。
        var sourceStyles = new IXLStyle[8];
        for (var column = 1; column <= 7; column++)
            sourceStyles[column] = sheet.Cell(sourceStyleRow, column).Style;
        var sourceHeight = sheet.Row(sourceStyleRow).Height;

        var athletes = meetClass.Athletes
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .ToList();

        for (var index = 0; index < athletes.Count; index++)
        {
            var athlete = athletes[index];
            var row = dataStart + index;
            if (row > templateEnd)
            {
                for (var column = 1; column <= 7; column++)
                    sheet.Cell(row, column).Style = sourceStyles[column];
                sheet.Row(row).Height = sourceHeight;
            }

            sheet.Cell(row, 1).Value = athlete.Name.Trim();
            sheet.Cell(row, 2).Value = athlete.Gender;
            sheet.Cell(row, 3).Value = AllowedTeamEvent(athlete.TeamEvent1);
            sheet.Cell(row, 4).Value = AllowedTeamEvent(athlete.TeamEvent2);
            sheet.Cell(row, 5).Value = AllowedTeamEvent(athlete.TeamEvent3);
            sheet.Cell(row, 6).Value = athlete.IndividualEvent1;
            sheet.Cell(row, 7).Value = athlete.IndividualEvent2;
        }

        workbook.SaveAs(target);
    }

    private static string AllowedTeamEvent(string value) =>
        MeetCatalog.TeamEvents.Contains(value) ? value : string.Empty;

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim();
    }

    private static IXLWorksheet FindSheet(XLWorkbook workbook)
    {
        var named = workbook.Worksheets.FirstOrDefault(w =>
            w.Name.Contains("导入模板", StringComparison.OrdinalIgnoreCase));
        if (named is not null) return named;

        foreach (var sheet in workbook.Worksheets)
        {
            if (TryFindHeaderRow(sheet, out _)) return sheet;
        }

        throw new InvalidDataException("没有找到报名表工作表。");
    }

    private static int FindHeaderRow(IXLWorksheet sheet)
    {
        if (TryFindHeaderRow(sheet, out var row)) return row;
        throw new InvalidDataException("报名表缺少“姓名 / 性别”表头。");
    }

    private static bool TryFindHeaderRow(IXLWorksheet sheet, out int headerRow)
    {
        var lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 20, 30);
        for (var row = 1; row <= lastRow; row++)
        {
            var hasName = false;
            var hasGender = false;
            for (var column = 1; column <= 10; column++)
            {
                var value = sheet.Cell(row, column).GetString().Trim();
                if (value == "姓名") hasName = true;
                if (value == "性别") hasGender = true;
            }

            if (hasName && hasGender)
            {
                headerRow = row;
                return true;
            }
        }

        headerRow = 0;
        return false;
    }

    private static bool IsExampleRow(IXLWorksheet sheet, int row) =>
        sheet.Cell(row, 1).GetString().Trim().StartsWith("例", StringComparison.Ordinal);
}
