using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ExcelDataReader;
using GradeStrip.Core.Model;

namespace GradeStrip.Core.Import;

public sealed class ImportOptions
{
    /// <summary>指定工作表名；为空时优先选择名称含「汇总」的表，否则取第一张表。</summary>
    public string? SheetName { get; set; }

    /// <summary>子表头（得分/校次/班次）所在行的最大搜索深度。</summary>
    public int HeaderSearchDepth { get; set; } = 10;
}

/// <summary>
/// 读取智学网导出的班级成绩表。支持两种版式：
/// 1) 「班级全学科成绩汇总」式两级表头（科目 / 得分·校次·校次进退步·班次·班次进退步）；
/// 2) 「班级全学科成绩简表」式（只有得分·校次·班次，无总分，总分由各科相加得出）；
/// 3) 单行表头的宽表（姓名、班级、语文、数学…），每个科目列直接就是得分。
/// </summary>
public static class GradeSheetImporter
{
    private static readonly string[] TotalNames = { "总分", "总成绩", "合计", "总计" };

    private static readonly string[] BaseColumnNames =
    {
        "序号", "准考证号", "自定义考号", "班级", "姓名", "学生属性", "学号", "考号", "姓名", "考生号",
    };

    private static readonly string[] SubHeaderNames = { "得分", "校次", "校次进退步", "班次", "班次进退步" };

    /// <summary>不是科目、而是排名字段的表头关键字，宽表模式下要跳过。</summary>
    private static readonly string[] RankKeyword =
    {
        "名次", "排名", "校次", "班次", "等第", "等级", "分数段", "年级", "班级", "平均", "最高", "最低",
    };

    static GradeSheetImporter()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public static ExamData Load(string path, ImportOptions? options = null)
    {
        options ??= new ImportOptions();
        path = path.Normalize(System.Text.NormalizationForm.FormC);

        // 优先尝试读取智学网「全科个人成绩单」导出格式（支持 .xls 与 .xlsx，兼容用户勾选任意选项组合）
        if (TryLoadPersonalScoreReport(path, options, out var personalData))
        {
            return personalData;
        }

        var ext = Path.GetExtension(path);
        if (string.Equals(ext, ".xls", StringComparison.OrdinalIgnoreCase))
        {
            if (TryLoadTabularXls(path, options, out var xlsData))
            {
                return xlsData;
            }

            throw new InvalidDataException(
                "检测到该文件为旧版 Excel 二进制格式（.xls），且未能识别为智学网成绩表。" +
                "请确认表格包含学生姓名及有效成绩，或在 Excel 中另存为 .xlsx 后再导入。");
        }

        using var workbook = new XLWorkbook(path);

        if (IsScoreStripWorkbook(workbook))
        {
            return ReadScoreStripWorkbook(workbook, path, options);
        }

        // 如果用户显式指定了工作表名，直接按指定工作表读取
        if (!string.IsNullOrWhiteSpace(options.SheetName))
        {
            var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name == options.SheetName)
                ?? throw new InvalidDataException($"工作簿中不存在工作表「{options.SheetName}」。");
            return ReadSheet(workbook, sheet, path, options, null);
        }

        // 智能候选工作表解析与回退机制：
        // 优先尝试汇总表（通常带总分与进退步）；如果读取失败（如缺少科目信息或结构不完整），
        // 则自动回退读取简表或其它可用工作表，并记录提示信息。
        var candidates = GetCandidateSheets(workbook);
        Exception? firstError = null;
        string? failedSheetInfo = null;

        foreach (var sheet in candidates)
        {
            try
            {
                var data = ReadSheet(workbook, sheet, path, options, failedSheetInfo);
                if (data.Students.Count > 0 && data.Subjects.Count > 0)
                {
                    return data;
                }
            }
            catch (Exception ex)
            {
                firstError ??= ex;
                failedSheetInfo = sheet.Name;
            }
        }

        throw firstError ?? new InvalidDataException("未能从任何工作表中识别到有效学生成绩数据。");
    }

    private static ExamData ReadSheet(
        XLWorkbook workbook, IXLWorksheet sheet, string path, ImportOptions options, string? fallbackFromSheet)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow == 0 || lastCol == 0)
            throw new InvalidDataException($"工作表「{sheet.Name}」为空。");

        var headerRow = FindSubHeaderRow(sheet, lastRow, lastCol, options.HeaderSearchDepth);
        var data = headerRow is null
            ? ReadWideHeader(sheet, lastRow, lastCol, options.HeaderSearchDepth)
            : ReadSubHeader(sheet, headerRow.Value, lastRow, lastCol);

        if (data.Students.Count == 0)
            throw new InvalidDataException($"工作表「{sheet.Name}」中没有读到学生数据，请确认表头与姓名列。");

        if (data.Subjects.Count == 0)
            throw new InvalidDataException($"工作表「{sheet.Name}」中找不到任何科目列。");

        // 尝试从同簿内其他表（如包含进退步的汇总表）补齐总分进退步
        TryEnrichTotalDeltas(workbook, sheet, data.Students);

        var sourceTitle = data.SourceTitle;
        var examName = ExtractExamName(sourceTitle);
        if (examName.Length == 0)
            examName = Path.GetFileNameWithoutExtension(path);

        var notice = BuildImportNotice(sheet, fallbackFromSheet, data);

        return new ExamData
        {
            SourceTitle = sourceTitle,
            ClassName = data.ClassName.Length > 0 ? data.ClassName : ClassFromTitle(sourceTitle),
            ExamName = examName,
            Subjects = data.Subjects,
            Students = data.Students,
            ImportNotice = notice,
        };
    }

    private static List<IXLWorksheet> GetCandidateSheets(XLWorkbook workbook)
    {
        var list = new List<IXLWorksheet>();

        // 1. 优先「汇总」表（通常信息最全）
        foreach (var ws in workbook.Worksheets.Where(w => w.Name.Contains("汇总")))
            if (!list.Contains(ws)) list.Add(ws);

        // 2. 其次「简表」或「成绩」表
        foreach (var ws in workbook.Worksheets.Where(w => w.Name.Contains("简表") || w.Name.Contains("成绩")))
            if (!list.Contains(ws)) list.Add(ws);

        // 3. 最后补充其余未加入的工作表
        foreach (var ws in workbook.Worksheets)
            if (!list.Contains(ws)) list.Add(ws);

        return list;
    }

    private static void TryEnrichTotalDeltas(XLWorkbook workbook, IXLWorksheet currentSheet, IReadOnlyList<StudentRecord> students)
    {
        if (students.Count == 0 || students.Any(s => s.Total.ClassDelta.HasValue || s.Total.SchoolDelta.HasValue))
            return;

        foreach (var ws in workbook.Worksheets)
        {
            if (ws == currentSheet) continue;

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastRow < 2 || lastCol < 3) continue;

            var nameCol = -1;
            var schoolDeltaCol = -1;
            var classDeltaCol = -1;
            var classRankCol = -1;
            var headerRow = -1;

            for (var r = 1; r <= Math.Min(5, lastRow); r++)
            {
                for (var c = 1; c <= lastCol; c++)
                {
                    var t = Text(ws.Cell(r, c));
                    if (t == "姓名") nameCol = c;
                    else if (t == "校次进退步") schoolDeltaCol = c;
                    else if (t == "班次进退步") classDeltaCol = c;
                    else if (t == "班次") classRankCol = c;
                }
                if (nameCol > 0 && (schoolDeltaCol > 0 || classDeltaCol > 0))
                {
                    headerRow = r;
                    break;
                }
            }

            if (nameCol < 0 || (schoolDeltaCol < 0 && classDeltaCol < 0) || headerRow < 0)
                continue;

            var deltaMap = new Dictionary<string, (int? SchoolDelta, int? ClassDelta, int? ClassRank)>(StringComparer.Ordinal);
            for (var r = headerRow + 1; r <= lastRow; r++)
            {
                var name = Text(ws.Cell(r, nameCol));
                if (!IsStudentName(name)) continue;

                var sd = schoolDeltaCol > 0 ? Int(ws.Cell(r, schoolDeltaCol)) : null;
                var cd = classDeltaCol > 0 ? Int(ws.Cell(r, classDeltaCol)) : null;
                var cr = classRankCol > 0 ? Int(ws.Cell(r, classRankCol)) : null;
                deltaMap[name] = (sd, cd, cr);
            }

            if (deltaMap.Count == 0) continue;

            foreach (var student in students)
            {
                if (deltaMap.TryGetValue(student.Name, out var d))
                {
                    student.Total = new SubjectScore
                    {
                        Score = student.Total.Score,
                        SchoolRank = student.Total.SchoolRank,
                        SchoolDelta = student.Total.SchoolDelta ?? d.SchoolDelta,
                        ClassRank = student.Total.ClassRank ?? d.ClassRank,
                        ClassDelta = student.Total.ClassDelta ?? d.ClassDelta,
                    };
                }
            }

            break;
        }
    }

    private static string? BuildImportNotice(IXLWorksheet sheet, string? fallbackFromSheet, ParsedTable data)
    {
        if (data.Subjects.Count == 1)
        {
            var sub = data.Subjects[0];
            return $"提示：当前为科任老师「{sub}」单科成绩表。已完整识别该科目实考成绩、班级/年级名次及进退步。";
        }

        var hasSubjectDeltas = data.Students.Any(s => s.Subjects.Values.Any(sc => sc.ClassDelta.HasValue || sc.SchoolDelta.HasValue));
        var isSimpleSheet = sheet.Name.Contains("简表") || !hasSubjectDeltas;

        if (!string.IsNullOrEmpty(fallbackFromSheet))
        {
            return $"提示：工作表「{fallbackFromSheet}」缺少各科成绩，已自动读取「{sheet.Name}」。当前表格缺少单科进退步等数据，如需完整对比分析，请前往智学网下载详细的成绩单（全学科成绩汇总表）。";
        }

        if (isSimpleSheet && !hasSubjectDeltas)
        {
            return $"提示：当前表格「{sheet.Name}」缺少各科进退步等对比数据。如需查看完整单科进退步与分析，请前往智学网下载详细的成绩单（全学科成绩汇总表）。";
        }

        return null;
    }

    // ---------------------------------------------------------------- 两级表头

    private static int? FindSubHeaderRow(IXLWorksheet sheet, int lastRow, int lastCol, int depth)
    {
        for (var r = 1; r <= Math.Min(depth, lastRow); r++)
        {
            var scoreColumns = 0;
            for (var c = 1; c <= lastCol; c++)
                if (Text(sheet.Cell(r, c)) == "得分") scoreColumns++;

            if (scoreColumns >= 3) return r;
        }

        return null;
    }

    private static ParsedTable ReadSubHeader(IXLWorksheet sheet, int headerRow, int lastRow, int lastCol)
    {
        var baseColumns = ReadBaseColumns(sheet, headerRow, lastCol);
        var blocks = ReadSubjectBlocks(sheet, headerRow, lastCol);
        if (blocks.Count == 0)
            throw new InvalidDataException($"工作表「{sheet.Name}」中找不到任何科目列。");

        var total = blocks.FirstOrDefault(b => TotalNames.Contains(b.Subject));
        var subjects = blocks.Where(b => !ReferenceEquals(b, total)).ToList();

        var nameCol = baseColumns.GetValueOrDefault("姓名");
        if (nameCol == 0)
            throw new InvalidDataException("找不到「姓名」列。");

        var noCol = baseColumns.GetValueOrDefault("序号");
        var examNoCol = baseColumns.GetValueOrDefault("准考证号");
        if (examNoCol == 0) examNoCol = baseColumns.GetValueOrDefault("考号");
        if (examNoCol == 0) examNoCol = baseColumns.GetValueOrDefault("学号");
        var classCol = baseColumns.GetValueOrDefault("班级");

        var students = new List<StudentRecord>();
        var className = string.Empty;

        for (var r = headerRow + 1; r <= lastRow; r++)
        {
            // 表尾的合并备注行会让从属单元格带出同一段文字，必须排除
            if (IsMergedSubCell(sheet.Cell(r, nameCol))) continue;

            var name = Text(sheet.Cell(r, nameCol));
            if (!IsStudentName(name)) continue;

            if (className.Length == 0 && classCol > 0)
                className = Text(sheet.Cell(r, classCol));

            var scores = new Dictionary<string, SubjectScore>(StringComparer.Ordinal);
            foreach (var block in subjects)
                scores[block.Subject] = ReadScore(sheet, r, block);

            students.Add(new StudentRecord
            {
                No = noCol > 0 ? Int(sheet.Cell(r, noCol)) ?? students.Count + 1 : students.Count + 1,
                Name = name,
                ExamNo = ExamNumber(sheet.Cell(r, examNoCol > 0 ? examNoCol : 0)),
                Subjects = scores,
                Total = total is null ? SumSubjects(scores, subjects) : ReadScore(sheet, r, total),
            });
        }

        return new ParsedTable
        {
            SourceTitle = headerRow >= 2 ? Text(sheet.Cell(1, 1)) : string.Empty,
            ClassName = className,
            Subjects = subjects.Select(s => s.Subject).ToList(),
            Students = students,
        };
    }

    private static Dictionary<string, int> ReadBaseColumns(IXLWorksheet sheet, int headerRow, int lastCol)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var c = 1; c <= lastCol; c++)
        {
            var label = Text(sheet.Cell(headerRow, c));
            if (string.IsNullOrEmpty(label) && headerRow > 1)
                label = Text(sheet.Cell(headerRow - 1, c));

            if (SubHeaderNames.Contains(label)) continue;
            if (string.IsNullOrEmpty(label) || map.ContainsKey(label)) continue;
            map[label] = c;
        }

        return map;
    }

    /// <summary>按「得分」列切分科目块，科目名取自上一行（合并单元格向左回溯）。</summary>
    private static List<SubjectBlock> ReadSubjectBlocks(IXLWorksheet sheet, int headerRow, int lastCol)
    {
        var blocks = new List<SubjectBlock>();
        var currentSubject = string.Empty;

        for (var c = 1; c <= lastCol; c++)
        {
            var subjectCell = headerRow > 1 ? Text(sheet.Cell(headerRow - 1, c)) : string.Empty;
            if (!string.IsNullOrEmpty(subjectCell)) currentSubject = subjectCell;

            if (Text(sheet.Cell(headerRow, c)) != "得分") continue;

            var block = new SubjectBlock { Subject = currentSubject, ScoreColumn = c };
            for (var j = 1; j <= 4 && c + j <= lastCol; j++)
            {
                var label = Text(sheet.Cell(headerRow, c + j));
                switch (label)
                {
                    case "校次": block.SchoolRankColumn = c + j; break;
                    case "校次进退步": block.SchoolDeltaColumn = c + j; break;
                    case "班次": block.ClassRankColumn = c + j; break;
                    case "班次进退步": block.ClassDeltaColumn = c + j; break;
                }
            }

            blocks.Add(block);
        }

        return blocks;
    }

    private static SubjectScore ReadScore(IXLWorksheet sheet, int row, SubjectBlock block) => new()
    {
        Score = Number(sheet.Cell(row, block.ScoreColumn)),
        SchoolRank = block.SchoolRankColumn > 0 ? Int(sheet.Cell(row, block.SchoolRankColumn)) : null,
        SchoolDelta = block.SchoolDeltaColumn > 0 ? Int(sheet.Cell(row, block.SchoolDeltaColumn)) : null,
        ClassRank = block.ClassRankColumn > 0 ? Int(sheet.Cell(row, block.ClassRankColumn)) : null,
        ClassDelta = block.ClassDeltaColumn > 0 ? Int(sheet.Cell(row, block.ClassDeltaColumn)) : null,
    };

    /// <summary>没有总分列时，用各科得分相加得到总分。</summary>
    private static SubjectScore SumSubjects(
        Dictionary<string, SubjectScore> scores, IReadOnlyList<SubjectBlock> subjects)
    {
        var values = subjects
            .Select(s => scores.TryGetValue(s.Subject, out var v) ? v.Score : null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        return new SubjectScore
        {
            Score = values.Count == 0 ? null : Math.Round(values.Sum(), 2, MidpointRounding.AwayFromZero),
        };
    }

    private static double? SumScores(Dictionary<string, SubjectScore> scores)
    {
        var values = scores.Values.Select(v => v.Score).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : Math.Round(values.Sum(), 2, MidpointRounding.AwayFromZero);
    }

    // ---------------------------------------------------------------- 单行宽表

    private static ParsedTable ReadWideHeader(IXLWorksheet sheet, int lastRow, int lastCol, int depth)
    {
        var headerRow = -1;
        for (var r = 1; r <= Math.Min(depth, lastRow); r++)
        {
            var hasName = false;
            for (var c = 1; c <= lastCol; c++)
                if (Text(sheet.Cell(r, c)) == "姓名") { hasName = true; break; }
            if (hasName) { headerRow = r; break; }
        }

        if (headerRow < 0)
            throw new InvalidDataException(
                $"工作表「{sheet.Name}」的表头既没有「得分」行，也没有「姓名」列，无法识别。");

        var headers = new Dictionary<int, string>();
        for (var c = 1; c <= lastCol; c++)
        {
            var label = Text(sheet.Cell(headerRow, c));
            if (!string.IsNullOrEmpty(label)) headers[c] = label;
        }

        var nameCol = headers.FirstOrDefault(h => h.Value == "姓名").Key;
        var noCol = headers.FirstOrDefault(h => h.Value == "序号").Key;
        var examNoCol = headers.FirstOrDefault(h => h.Value is "准考证号" or "考号" or "学号").Key;
        var classCol = headers.FirstOrDefault(h => h.Value == "班级").Key;

        if (examNoCol == 0) examNoCol = headers.FirstOrDefault(h => h.Value == "自定义考号").Key;

        var totalCol = headers.FirstOrDefault(h => TotalNames.Contains(h.Value)).Key;

        var subjectColumns = headers
            .Where(h => h.Key != nameCol && h.Key != noCol && h.Key != examNoCol && h.Key != classCol)
            .Where(h => h.Key != totalCol)
            .Where(h => !BaseColumnNames.Contains(h.Value))
            .Where(h => !RankKeyword.Any(k => h.Value.Contains(k)))
            .ToList();

        var rawTitle = headerRow >= 2 ? Text(sheet.Cell(1, 1)) : string.Empty;

        // 科任老师单科成绩表智能识别
        var isSingleSubject = false;
        string? singleSubjectName = null;

        if (subjectColumns.Count == 1 && subjectColumns[0].Value is "得分" or "成绩" or "实得分")
        {
            isSingleSubject = true;
        }
        else if (subjectColumns.Count == 0 && (totalCol > 0 || headers.Values.Any(v => v is "得分" or "成绩")))
        {
            isSingleSubject = true;
        }
        else if (subjectColumns.Count > 1 &&
                 subjectColumns.All(s => s.Value is "得分" or "客观分" or "主观分" or "单选题" or "填空题" or "解答题" || int.TryParse(s.Value, out _)))
        {
            isSingleSubject = true;
        }

        if (isSingleSubject)
        {
            singleSubjectName = ExtractSubjectFromText(rawTitle) ??
                                ExtractSubjectFromText(sheet.Name) ??
                                sheet.Workbook.Worksheets.Select(ws => ExtractSubjectFromText(ws.Name)).FirstOrDefault(s => s != null) ??
                                "单科";

            var scoreCol = headers.FirstOrDefault(h => h.Value is "得分" or "实得分" or "成绩").Key;
            if (scoreCol == 0) scoreCol = totalCol;
            if (scoreCol == 0 && subjectColumns.Count > 0) scoreCol = subjectColumns[0].Key;

            var schoolRankCol = headers.FirstOrDefault(h => h.Value is "校次" or "年级名次" or "年名次" or "校排名").Key;
            var schoolDeltaCol = headers.FirstOrDefault(h => h.Value is "校次进退步" or "校进退步" or "年级进退步").Key;
            var classRankCol = headers.FirstOrDefault(h => h.Value is "班次" or "班级名次" or "班名次" or "班排名").Key;
            var classDeltaCol = headers.FirstOrDefault(h => h.Value is "班次进退步" or "班进退步" or "班级进退步").Key;

            var singleStudents = new List<StudentRecord>();
            var detectedClass = string.Empty;

            for (var r = headerRow + 1; r <= lastRow; r++)
            {
                if (IsMergedSubCell(sheet.Cell(r, nameCol))) continue;

                var name = Text(sheet.Cell(r, nameCol));
                if (!IsStudentName(name)) continue;

                if (detectedClass.Length == 0 && classCol > 0)
                    detectedClass = Text(sheet.Cell(r, classCol));

                var score = scoreCol > 0 ? Number(sheet.Cell(r, scoreCol)) : null;
                var classRank = classRankCol > 0 ? Int(sheet.Cell(r, classRankCol)) : null;
                var classDelta = classDeltaCol > 0 ? Int(sheet.Cell(r, classDeltaCol)) : null;
                var schoolRank = schoolRankCol > 0 ? Int(sheet.Cell(r, schoolRankCol)) : null;
                var schoolDelta = schoolDeltaCol > 0 ? Int(sheet.Cell(r, schoolDeltaCol)) : null;

                var subjScore = new SubjectScore
                {
                    Score = score,
                    ClassRank = classRank,
                    ClassDelta = classDelta,
                    SchoolRank = schoolRank,
                    SchoolDelta = schoolDelta,
                };

                var scores = new Dictionary<string, SubjectScore>(StringComparer.Ordinal)
                {
                    [singleSubjectName] = subjScore
                };

                singleStudents.Add(new StudentRecord
                {
                    No = noCol > 0 ? Int(sheet.Cell(r, noCol)) ?? singleStudents.Count + 1 : singleStudents.Count + 1,
                    Name = name,
                    ExamNo = ExamNumber(sheet.Cell(r, examNoCol > 0 ? examNoCol : 0)),
                    Subjects = scores,
                    Total = subjScore,
                });
            }

            PopulateMissingClassRanks(singleStudents, new[] { singleSubjectName });

            return new ParsedTable
            {
                SourceTitle = rawTitle,
                ClassName = detectedClass.Length > 0 ? detectedClass : ClassFromTitle(rawTitle),
                Subjects = new[] { singleSubjectName },
                Students = singleStudents,
            };
        }

        if (subjectColumns.Count == 0)
            throw new InvalidDataException($"工作表「{sheet.Name}」中找不到任何科目列。");

        var students = new List<StudentRecord>();
        var className = string.Empty;

        for (var r = headerRow + 1; r <= lastRow; r++)
        {
            if (IsMergedSubCell(sheet.Cell(r, nameCol))) continue;

            var name = Text(sheet.Cell(r, nameCol));
            if (!IsStudentName(name)) continue;

            if (className.Length == 0 && classCol > 0)
                className = Text(sheet.Cell(r, classCol));

            var scores = new Dictionary<string, SubjectScore>(StringComparer.Ordinal);
            foreach (var (col, subject) in subjectColumns)
                scores[subject] = new SubjectScore { Score = Number(sheet.Cell(r, col)) };

            var totalScore = totalCol > 0
                ? new SubjectScore { Score = Number(sheet.Cell(r, totalCol)) }
                : new SubjectScore { Score = SumScores(scores) };

            students.Add(new StudentRecord
            {
                No = noCol > 0 ? Int(sheet.Cell(r, noCol)) ?? students.Count + 1 : students.Count + 1,
                Name = name,
                ExamNo = ExamNumber(sheet.Cell(r, examNoCol > 0 ? examNoCol : 0)),
                Subjects = scores,
                Total = totalScore,
            });
        }

        return new ParsedTable
        {
            SourceTitle = headerRow >= 2 ? Text(sheet.Cell(1, 1)) : string.Empty,
            ClassName = className,
            Subjects = subjectColumns.Select(h => h.Value).ToList(),
            Students = students,
        };
    }

    // ---------------------------------------------------------------- 工具

    /// <summary>姓名列不该是长句（表尾备注、说明文字）或纯数字。</summary>
    private static bool IsStudentName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Length > 12) return false;
        if (name.Contains('：') || name.Contains(':')) return false;
        if (name.StartsWith("注", StringComparison.Ordinal)) return false;
        return name.Any(ch => char.IsLetter(ch));
    }

    /// <summary>判断单元格是否为合并区域中非左上角的从属单元格。</summary>
    private static bool IsMergedSubCell(IXLCell cell)
    {
        if (!cell.IsMerged()) return false;
        var range = cell.MergedRange();
        return range is not null && range.FirstCell().Address.ToString() != cell.Address.ToString();
    }

    private static string Text(IXLCell cell)
    {
        if (cell is null || cell.Value.IsBlank) return string.Empty;
        return cell.GetString().Trim();
    }

    private static double? Number(IXLCell cell)
    {
        if (cell.Value.IsNumber) return cell.Value.GetNumber();
        var text = Text(cell);
        if (text is "" or "-" or "—" or "–" or "/" or "／") return null;
        return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static int? Int(IXLCell cell)
    {
        var value = Number(cell);
        return value is null ? null : (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);
    }

    private static string ExamNumber(IXLCell? cell)
    {
        if (cell is null || cell.Value.IsBlank) return string.Empty;
        if (cell.Value.IsNumber) return ((long)Math.Round(cell.Value.GetNumber())).ToString(CultureInfo.InvariantCulture);
        return cell.GetString().Trim();
    }

    private static string ClassFromTitle(string title)
    {
        // 「九6班-全学科成绩汇总-…」→「九6班」
        var head = title.Split('-', '－')[0].Trim();
        return head.Length is > 0 and <= 12 ? head : string.Empty;
    }

    private static string ExtractExamName(string sourceTitle)
    {
        var match = Regex.Match(sourceTitle, @"\d{4}\s*[-－]\s*\d{4}\s*学年.*$");
        return match.Success ? match.Value.Trim() : sourceTitle;
    }

    // ---------------------------------------------------------------- 成绩条反向导入

    private static bool IsScoreStripWorkbook(XLWorkbook workbook)
    {
        // 1. 标题或图例特征（导出的成绩条第1页有标题或图例）
        var firstWs = workbook.Worksheets.FirstOrDefault();
        if (firstWs != null)
        {
            var titleCandidate = Text(firstWs.Cell(1, 1));
            if (titleCandidate.Contains("个人成绩条")) return true;
            var legendCandidate = Text(firstWs.Cell(2, 1));
            if (legendCandidate.Contains("得分底色按【")) return true;
        }

        // 2. 结构特征：某行包含「总分」，A 列纵向合并 3 行，且第 3 行包含「班」或「校」排名文本
        foreach (var ws in workbook.Worksheets)
        {
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastRow < 3 || lastCol < 4) continue;

            for (var r = 1; r <= Math.Min(15, lastRow - 2); r++)
            {
                var cellA = ws.Cell(r, 1);
                if (!cellA.IsMerged()) continue;
                var range = cellA.MergedRange();
                if (range == null || range.RowCount() != 3 || range.ColumnCount() != 1) continue;

                var totalCol = -1;
                for (var c = 2; c <= lastCol; c++)
                {
                    if (Text(ws.Cell(r, c)) == "总分")
                    {
                        totalCol = c;
                        break;
                    }
                }
                if (totalCol < 0) continue;

                // 验证 r+2 行是否有「班」或「校」的排名进退步标记
                var hasRankPrefix = false;
                for (var c = 2; c < totalCol; c++)
                {
                    var t = Text(ws.Cell(r + 2, c));
                    if (t.StartsWith("班") || t.StartsWith("校"))
                    {
                        hasRankPrefix = true;
                        break;
                    }
                }

                if (hasRankPrefix)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ExamData ReadScoreStripWorkbook(XLWorkbook workbook, string filePath, ImportOptions options)
    {
        var firstWs = workbook.Worksheets.FirstOrDefault();
        var rawTitle = string.Empty;
        if (firstWs != null)
        {
            var candidate = Text(firstWs.Cell(1, 1));
            if (candidate.Contains("个人成绩条") || (candidate.Length > 0 && firstWs.Cell(1, 1).IsMerged()))
            {
                rawTitle = candidate;
            }
        }

        var (className, examName) = ParseExamMeta(rawTitle, filePath);

        IEnumerable<IXLWorksheet> sheetsToRead;
        if (!string.IsNullOrWhiteSpace(options.SheetName))
        {
            var target = workbook.Worksheets.FirstOrDefault(w => w.Name == options.SheetName);
            if (target is null)
                throw new InvalidDataException($"工作簿中不存在工作表「{options.SheetName}」。");
            sheetsToRead = new[] { target };
        }
        else
        {
            sheetsToRead = workbook.Worksheets;
        }

        var allStudents = new List<StudentRecord>();
        var subjectOrder = new List<string>();

        foreach (var ws in sheetsToRead)
        {
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastRow < 3 || lastCol < 4) continue;

            var r = 1;
            while (r <= lastRow)
            {
                var cellA = ws.Cell(r, 1);
                if (IsMergedSubCell(cellA))
                {
                    r++;
                    continue;
                }

                var totalCol = -1;
                var schoolRankCol = -1;
                var classRankCol = -1;
                var rowSubjects = new List<(int Col, string Subject)>();

                for (var c = 2; c <= lastCol; c++)
                {
                    var h = Text(ws.Cell(r, c));
                    if (h == "总分")
                    {
                        totalCol = c;
                    }
                    else if (h == "校次")
                    {
                        schoolRankCol = c;
                    }
                    else if (h == "班次")
                    {
                        classRankCol = c;
                    }
                    else if (!string.IsNullOrEmpty(h) && totalCol < 0)
                    {
                        rowSubjects.Add((c, h));
                        if (!subjectOrder.Contains(h))
                            subjectOrder.Add(h);
                    }
                }

                if (totalCol < 0)
                {
                    r++;
                    continue;
                }

                var studentText = Text(cellA);
                var (no, name, examNo) = ParseStudentCell(studentText, allStudents.Count + 1);
                if (string.IsNullOrEmpty(name))
                {
                    r++;
                    continue;
                }

                var scoreRow = r + 1;
                var deltaRow = r + 2;

                var scores = new Dictionary<string, SubjectScore>(StringComparer.Ordinal);
                foreach (var (col, sub) in rowSubjects)
                {
                    var sVal = Number(ws.Cell(scoreRow, col));
                    var deltaText = Text(ws.Cell(deltaRow, col));
                    var (prefix, rank, delta) = ParseSubjectRankAndDelta(deltaText);

                    scores[sub] = new SubjectScore
                    {
                        Score = sVal,
                        ClassRank = prefix == "班" ? rank : null,
                        ClassDelta = prefix == "班" ? delta : null,
                        SchoolRank = prefix == "校" ? rank : null,
                        SchoolDelta = prefix == "校" ? delta : null,
                    };
                }

                var totalScore = Number(ws.Cell(scoreRow, totalCol)) ?? SumScores(scores);
                int? totalSchoolRank = schoolRankCol > 0 ? Int(ws.Cell(scoreRow, schoolRankCol)) : null;
                int? totalSchoolDelta = schoolRankCol > 0 ? ParseDelta(Text(ws.Cell(deltaRow, schoolRankCol))) : null;
                int? totalClassRank = classRankCol > 0 ? Int(ws.Cell(scoreRow, classRankCol)) : null;
                int? totalClassDelta = classRankCol > 0 ? ParseDelta(Text(ws.Cell(deltaRow, classRankCol))) : null;

                var total = new SubjectScore
                {
                    Score = totalScore,
                    SchoolRank = totalSchoolRank,
                    SchoolDelta = totalSchoolDelta,
                    ClassRank = totalClassRank,
                    ClassDelta = totalClassDelta,
                };

                allStudents.Add(new StudentRecord
                {
                    No = no,
                    Name = name,
                    ExamNo = examNo,
                    Subjects = scores,
                    Total = total,
                });

                r += 3;
            }
        }

        if (allStudents.Count == 0)
            throw new InvalidDataException("成绩条工作簿中未识别到任何学生成绩条数据。");

        allStudents = allStudents.OrderBy(s => s.No).ToList();
        PopulateMissingClassRanks(allStudents, subjectOrder);

        return new ExamData
        {
            SourceTitle = rawTitle.Length > 0 ? rawTitle : Path.GetFileNameWithoutExtension(filePath),
            ClassName = className,
            ExamName = examName,
            Subjects = subjectOrder,
            Students = allStudents,
        };
    }

    private static (string ClassName, string ExamName) ParseExamMeta(string title, string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        if (!string.IsNullOrWhiteSpace(title))
        {
            var cleanTitle = title.Replace("个人成绩条", "").Trim(' ', '·', '-', '－', '_');
            if (title.Contains('·'))
            {
                var parts = title.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Where(p => p != "个人成绩条")
                    .ToList();

                if (parts.Count >= 2)
                    return (parts[0], parts[1]);
                if (parts.Count == 1)
                    cleanTitle = parts[0];
            }

            if (cleanTitle.Contains('-') || cleanTitle.Contains('－') || cleanTitle.Contains('_'))
            {
                var parts = cleanTitle.Split(new[] { '-', '－', '_' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var cls = parts[0].Length <= 12 ? parts[0] : string.Empty;
                    return (cls, parts[1]);
                }
            }

            var classMatch = Regex.Match(cleanTitle, @"^(?<class>(?:高|初|九|八|七|一|二|三|四|五|六|[1-9]|1[0-2])(?:年级)?(?:\([0-9]+\)|[0-9]+)?(?:[（(]?[0-9]+[)）]?)?班)");
            if (classMatch.Success)
            {
                var cls = classMatch.Groups["class"].Value;
                var rem = cleanTitle[cls.Length..].Trim(' ', '-', '－', '_', '·');
                return (cls, rem.Length > 0 ? rem : fileName);
            }

            var exam = ExtractExamName(cleanTitle);
            var className = ClassFromTitle(cleanTitle);
            if (className.Length > 0 && exam.Length > 0)
                return (className, exam);

            if (cleanTitle.Length > 0)
                return (className, cleanTitle);
        }

        var fallbackClass = ClassFromTitle(fileName);
        var fallbackExam = ExtractExamName(fileName);
        if (string.IsNullOrEmpty(fallbackExam)) fallbackExam = fileName;

        return (fallbackClass, fallbackExam);
    }

    private static (int No, string Name, string ExamNo) ParseStudentCell(string cellText, int defaultNo)
    {
        if (string.IsNullOrWhiteSpace(cellText))
            return (defaultNo, string.Empty, string.Empty);

        var lines = cellText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return (defaultNo, string.Empty, string.Empty);

        var firstLine = lines[0];
        var examNo = lines.Length > 1 ? lines[1] : string.Empty;

        var match = Regex.Match(firstLine, @"^(\d+)\s+(.+)$");
        if (match.Success)
        {
            var no = int.TryParse(match.Groups[1].Value, out var n) ? n : defaultNo;
            var name = match.Groups[2].Value.Trim();
            return (no, name, examNo);
        }

        return (defaultNo, firstLine.Trim(), examNo);
    }

    private static (string Prefix, int? Rank, int? Delta) ParseSubjectRankAndDelta(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (string.Empty, null, null);

        text = text.Trim();
        var match = Regex.Match(text, @"^(?<prefix>[班校])(?<rank>\d+|—|-|–)?(?:\s+(?<delta>[▲▼+\-–—]?\d*))?$");
        if (!match.Success)
        {
            if (text.StartsWith("班") || text.StartsWith("校"))
            {
                var p = text[..1];
                var rest = text[1..].Trim();
                var tokens = rest.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                int? r = null;
                int? d = null;
                if (tokens.Length > 0 && int.TryParse(tokens[0], out var parsedR)) r = parsedR;
                if (tokens.Length > 1) d = ParseDelta(tokens[1]);
                return (p, r, d);
            }
            return (string.Empty, null, null);
        }

        var prefix = match.Groups["prefix"].Value;

        int? rank = null;
        var rankStr = match.Groups["rank"].Value;
        if (int.TryParse(rankStr, out var rv))
            rank = rv;

        int? delta = null;
        var deltaStr = match.Groups["delta"].Value;
        if (!string.IsNullOrEmpty(deltaStr))
        {
            delta = ParseDelta(deltaStr);
        }

        return (prefix, rank, delta);
    }

    private static int? ParseDelta(string deltaStr)
    {
        deltaStr = deltaStr.Trim();
        if (string.IsNullOrEmpty(deltaStr) || deltaStr is "—" or "/" or "无")
            return null;

        if (deltaStr is "–" or "-" or "0")
            return 0;

        if (deltaStr.StartsWith('▲') || deltaStr.StartsWith('+'))
        {
            var numPart = deltaStr[1..].Trim();
            return int.TryParse(numPart, out var d) ? d : null;
        }

        if (deltaStr.StartsWith('▼'))
        {
            var numPart = deltaStr[1..].Trim();
            return int.TryParse(numPart, out var d) ? -d : null;
        }

        if (deltaStr.StartsWith('-'))
        {
            var numPart = deltaStr[1..].Trim();
            return int.TryParse(numPart, out var d) ? -d : null;
        }

        if (int.TryParse(deltaStr, out var val))
            return val;

        return null;
    }

    private static void PopulateMissingClassRanks(List<StudentRecord> students, IReadOnlyList<string> subjects)
    {
        foreach (var subject in subjects)
        {
            var needClassRank = students.Any(s => s[subject].HasScore && s[subject].ClassRank == null);
            if (!needClassRank) continue;

            var rankedScores = students
                .Where(s => s[subject].HasScore)
                .Select(s => s[subject].Score!.Value)
                .Distinct()
                .OrderByDescending(v => v)
                .ToList();

            if (rankedScores.Count == 0) continue;

            var rankMap = new Dictionary<double, int>();
            var currentRank = 1;
            foreach (var score in rankedScores)
            {
                rankMap[score] = currentRank;
                currentRank += students.Count(s => s[subject].Score == score);
            }

            foreach (var s in students)
            {
                var cur = s[subject];
                if (!cur.HasScore || cur.ClassRank != null) continue;

                if (rankMap.TryGetValue(cur.Score!.Value, out var rank))
                {
                    s.Subjects[subject] = new SubjectScore
                    {
                        Score = cur.Score,
                        ClassRank = rank,
                        ClassDelta = cur.ClassDelta,
                        SchoolRank = cur.SchoolRank,
                        SchoolDelta = cur.SchoolDelta,
                    };
                }
            }
        }

        if (students.Any(s => s.Total.HasScore && s.Total.ClassRank == null))
        {
            var rankedTotals = students
                .Where(s => s.Total.HasScore)
                .Select(s => s.Total.Score!.Value)
                .Distinct()
                .OrderByDescending(v => v)
                .ToList();

            if (rankedTotals.Count > 0)
            {
                var rankMap = new Dictionary<double, int>();
                var currentRank = 1;
                foreach (var score in rankedTotals)
                {
                    rankMap[score] = currentRank;
                    currentRank += students.Count(s => s.Total.Score == score);
                }

                foreach (var s in students)
                {
                    if (!s.Total.HasScore || s.Total.ClassRank != null) continue;
                    if (rankMap.TryGetValue(s.Total.Score!.Value, out var rank))
                    {
                        s.Total = new SubjectScore
                        {
                            Score = s.Total.Score,
                            SchoolRank = s.Total.SchoolRank,
                            SchoolDelta = s.Total.SchoolDelta,
                            ClassRank = rank,
                            ClassDelta = s.Total.ClassDelta,
                        };
                    }
                }
            }
        }
    }

    private sealed class ParsedTable
    {
        public string SourceTitle { get; init; } = string.Empty;
        public string ClassName { get; init; } = string.Empty;
        public IReadOnlyList<string> Subjects { get; init; } = Array.Empty<string>();
        public IReadOnlyList<StudentRecord> Students { get; init; } = Array.Empty<StudentRecord>();
    }

    private sealed class SubjectBlock
    {
        public string Subject { get; init; } = string.Empty;
        public int ScoreColumn { get; init; }
        public int SchoolRankColumn { get; set; }
        public int SchoolDeltaColumn { get; set; }
        public int ClassRankColumn { get; set; }
        public int ClassDeltaColumn { get; set; }
    }

    // ---------------------------------------------------------------- 全科个人成绩单（智学网导出格式）

    private static bool TryLoadPersonalScoreReport(
        string path, ImportOptions options, [NotNullWhen(true)] out ExamData? examData)
    {
        examData = null;
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelReaderFactory.CreateReader(stream);
            var dataSet = reader.AsDataSet();

            DataTable? targetTable = null;
            if (!string.IsNullOrWhiteSpace(options.SheetName))
            {
                targetTable = dataSet.Tables[options.SheetName];
            }
            else
            {
                foreach (DataTable table in dataSet.Tables)
                {
                    if (IsPersonalScoreReportTable(table))
                    {
                        targetTable = table;
                        break;
                    }
                }
            }

            if (targetTable == null || !IsPersonalScoreReportTable(targetTable))
            {
                return false;
            }

            if (HasSubjectRow(targetTable))
            {
                examData = ReadPersonalScoreReport(targetTable, path);
            }
            else
            {
                examData = ReadSingleSubjectPersonalScoreReport(targetTable, path);
            }
            return examData != null && examData.Students.Count > 0;
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            examData = null;
            return false;
        }
    }

    private static bool IsPersonalScoreReportTable(DataTable table)
    {
        if (table.Rows.Count < 3 || table.Columns.Count < 1)
            return false;

        var hasStudentCard = false;
        var hasSubjectHeader = false;
        var hasSingleSubjectMarker = false;

        var checkRowCount = Math.Min(table.Rows.Count, 150);
        for (var r = 0; r < checkRowCount; r++)
        {
            var cell0 = CellStr(table.Rows[r][0]);
            if (!hasStudentCard && IsStudentCardHeader(cell0))
            {
                hasStudentCard = true;
            }
            if (cell0 is "科目" or "学科" or "课程")
            {
                hasSubjectHeader = true;
            }
            if (cell0.Contains("得分明细") || cell0.Contains("考试中成绩为") || cell0 is "题号" || cell0.StartsWith("题号"))
            {
                hasSingleSubjectMarker = true;
            }

            if (hasStudentCard && (hasSubjectHeader || hasSingleSubjectMarker))
                return true;
        }

        return false;
    }

    private static bool HasSubjectRow(DataTable table)
    {
        var checkRowCount = Math.Min(table.Rows.Count, 30);
        for (var r = 0; r < checkRowCount; r++)
        {
            var c0 = CellStr(table.Rows[r][0]);
            if (c0 is "科目" or "学科" or "课程") return true;
        }
        return false;
    }

    private static bool IsStudentCardHeader(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text == "成绩单") return false;
        return Regex.IsMatch(text, @"^.+?(?:_|-|\s+)?成绩单$");
    }

    private static string ExtractStudentNameFromHeader(string text)
    {
        var match = Regex.Match(text.Trim(), @"^(.+?)(?:_|-|\s+)?成绩单$");
        return match.Success ? match.Groups[1].Value.Trim() : text.Trim();
    }

    private static ExamData ReadPersonalScoreReport(DataTable table, string path)
    {
        var studentHeaders = new List<(int RowIndex, string Name)>();
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var cell0 = CellStr(table.Rows[r][0]);
            if (IsStudentCardHeader(cell0))
            {
                var name = ExtractStudentNameFromHeader(cell0);
                if (!string.IsNullOrEmpty(name))
                {
                    studentHeaders.Add((r, name));
                }
            }
        }

        if (studentHeaders.Count == 0)
            throw new InvalidDataException("未能从表格中识别到任何学生个人成绩单。");

        var firstStudentRow = studentHeaders[0].RowIndex;
        var subjectHeaderRow = -1;
        for (var r = firstStudentRow + 1; r < Math.Min(firstStudentRow + 6, table.Rows.Count); r++)
        {
            var c0 = CellStr(table.Rows[r][0]);
            if (c0 is "科目" or "学科" or "课程")
            {
                subjectHeaderRow = r;
                break;
            }
        }

        if (subjectHeaderRow == -1)
            throw new InvalidDataException("未能从学生成绩单卡片中找到「科目」行。");

        var subjectsList = new List<string>();
        var subjectColMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int? totalColIdx = null;

        for (var c = 1; c < table.Columns.Count; c++)
        {
            var colName = CellStr(table.Rows[subjectHeaderRow][c]);
            if (string.IsNullOrWhiteSpace(colName)) continue;

            if (TotalNames.Contains(colName))
            {
                totalColIdx = c;
            }
            else
            {
                if (!subjectColMap.ContainsKey(colName))
                {
                    subjectColMap[colName] = c;
                    subjectsList.Add(colName);
                }
            }
        }

        if (subjectsList.Count == 0 && totalColIdx == null)
            throw new InvalidDataException("「科目」行中未包含任何有效学科名称。");

        var rawTitle = string.Empty;
        for (var r = firstStudentRow + 1; r < subjectHeaderRow; r++)
        {
            var t = CellStr(table.Rows[r][0]);
            if (!string.IsNullOrWhiteSpace(t))
            {
                rawTitle = t;
                break;
            }
        }

        var className = string.Empty;
        if (!string.IsNullOrEmpty(rawTitle))
        {
            var classMatch = Regex.Match(rawTitle, @"[-_\s]?((?:初[一二三]|高[一二三四]|七|八|九|[一二三四五六七八九\d]+年级|[初高七八九\d]+)?\s*[\(（]?\d+[\)）]?班)$");
            if (classMatch.Success) className = classMatch.Groups[1].Value.Trim();
        }
        if (string.IsNullOrEmpty(className) && table.TableName.EndsWith("班"))
        {
            className = table.TableName.Trim();
        }
        if (string.IsNullOrEmpty(className))
        {
            className = ClassFromTitle(rawTitle);
        }

        var examName = string.Empty;
        if (!string.IsNullOrEmpty(className) && rawTitle.EndsWith(className))
        {
            var titleWithoutClass = rawTitle[..^className.Length].TrimEnd('-', '_', ' ');
            examName = ExtractExamName(titleWithoutClass);
        }
        if (string.IsNullOrEmpty(examName))
        {
            examName = ExtractExamName(rawTitle);
        }
        if (string.IsNullOrEmpty(examName))
        {
            examName = Path.GetFileNameWithoutExtension(path);
        }

        var students = new List<StudentRecord>();

        for (var i = 0; i < studentHeaders.Count; i++)
        {
            var (sRow, sName) = studentHeaders[i];
            var nextSRow = (i + 1 < studentHeaders.Count) ? studentHeaders[i + 1].RowIndex : table.Rows.Count;

            var curSubRow = -1;
            for (var r = sRow + 1; r < Math.Min(sRow + 6, nextSRow); r++)
            {
                var c0 = CellStr(table.Rows[r][0]);
                if (c0 is "科目" or "学科" or "课程")
                {
                    curSubRow = r;
                    break;
                }
            }

            if (curSubRow == -1) curSubRow = subjectHeaderRow;

            var curSubjectColMap = subjectColMap;
            var curTotalColIdx = totalColIdx;
            if (curSubRow != subjectHeaderRow && curSubRow > 0)
            {
                var sCols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int? sTot = null;
                for (var c = 1; c < table.Columns.Count; c++)
                {
                    var colName = CellStr(table.Rows[curSubRow][c]);
                    if (string.IsNullOrWhiteSpace(colName)) continue;
                    if (TotalNames.Contains(colName)) sTot = c;
                    else
                    {
                        sCols[colName] = c;
                        if (!subjectsList.Contains(colName)) subjectsList.Add(colName);
                    }
                }
                if (sCols.Count > 0)
                {
                    curSubjectColMap = sCols;
                    curTotalColIdx = sTot;
                }
            }

            var scores = new Dictionary<string, SubjectScore>(StringComparer.Ordinal);
            foreach (var sub in subjectsList)
            {
                scores[sub] = new SubjectScore();
            }
            var totalScore = new SubjectScore();

            for (var r = curSubRow + 1; r < nextSRow; r++)
            {
                var label = CellStr(table.Rows[r][0]).TrimEnd(':', '：');
                if (string.IsNullOrWhiteSpace(label)) break;

                if (label.Contains("明细") || label.StartsWith("题号") || label.Contains("客观题") ||
                    label.Contains("主观题") || label.Contains("评语") || IsStudentCardHeader(label))
                {
                    break;
                }

                if (label is "得分" or "成绩" or "实得分")
                {
                    foreach (var (sub, col) in curSubjectColMap)
                    {
                        var scoreVal = CellDouble(table.Rows[r][col]);
                        var cur = scores[sub];
                        scores[sub] = new SubjectScore
                        {
                            Score = scoreVal,
                            ClassRank = cur.ClassRank,
                            ClassDelta = cur.ClassDelta,
                            SchoolRank = cur.SchoolRank,
                            SchoolDelta = cur.SchoolDelta,
                        };
                    }
                    if (curTotalColIdx.HasValue)
                    {
                        totalScore = new SubjectScore
                        {
                            Score = CellDouble(table.Rows[r][curTotalColIdx.Value]),
                            ClassRank = totalScore.ClassRank,
                            ClassDelta = totalScore.ClassDelta,
                            SchoolRank = totalScore.SchoolRank,
                            SchoolDelta = totalScore.SchoolDelta,
                        };
                    }
                }
                else if (label is "班级名次" or "班名次" or "班次" or "班级排名" or "班排名")
                {
                    foreach (var (sub, col) in curSubjectColMap)
                    {
                        var rankVal = CellInt(table.Rows[r][col]);
                        var cur = scores[sub];
                        scores[sub] = new SubjectScore
                        {
                            Score = cur.Score,
                            ClassRank = rankVal,
                            ClassDelta = cur.ClassDelta,
                            SchoolRank = cur.SchoolRank,
                            SchoolDelta = cur.SchoolDelta,
                        };
                    }
                    if (curTotalColIdx.HasValue)
                    {
                        totalScore = new SubjectScore
                        {
                            Score = totalScore.Score,
                            ClassRank = CellInt(table.Rows[r][curTotalColIdx.Value]),
                            ClassDelta = totalScore.ClassDelta,
                            SchoolRank = totalScore.SchoolRank,
                            SchoolDelta = totalScore.SchoolDelta,
                        };
                    }
                }
                else if (label is "年级名次" or "年名次" or "校次" or "校名次" or "年次" or "年级排名" or "校排名")
                {
                    foreach (var (sub, col) in curSubjectColMap)
                    {
                        var rankVal = CellInt(table.Rows[r][col]);
                        var cur = scores[sub];
                        scores[sub] = new SubjectScore
                        {
                            Score = cur.Score,
                            ClassRank = cur.ClassRank,
                            ClassDelta = cur.ClassDelta,
                            SchoolRank = rankVal,
                            SchoolDelta = cur.SchoolDelta,
                        };
                    }
                    if (curTotalColIdx.HasValue)
                    {
                        totalScore = new SubjectScore
                        {
                            Score = totalScore.Score,
                            ClassRank = totalScore.ClassRank,
                            ClassDelta = totalScore.ClassDelta,
                            SchoolRank = CellInt(table.Rows[r][curTotalColIdx.Value]),
                            SchoolDelta = totalScore.SchoolDelta,
                        };
                    }
                }
            }

            if (!curTotalColIdx.HasValue || !totalScore.Score.HasValue)
            {
                var scoredSubjects = scores.Values.Where(s => s.Score.HasValue).ToList();
                if (scoredSubjects.Count > 0)
                {
                    totalScore = new SubjectScore
                    {
                        Score = scoredSubjects.Sum(s => s.Score!.Value),
                        ClassRank = totalScore.ClassRank,
                        ClassDelta = totalScore.ClassDelta,
                        SchoolRank = totalScore.SchoolRank,
                        SchoolDelta = totalScore.SchoolDelta,
                    };
                }
            }

            students.Add(new StudentRecord
            {
                No = students.Count + 1,
                Name = sName,
                ExamNo = string.Empty,
                Subjects = scores,
                Total = totalScore,
            });
        }

        PopulateMissingClassRanks(students, subjectsList);

        var notice = "提示：已成功从智学网「全科个人成绩单」导入。当前格式未包含上次考试的进退步对比，如需进退步分析，可导入多次考试成绩由系统自动对比计算，或前往智学网下载「全学科成绩汇总表」。";

        return new ExamData
        {
            SourceTitle = rawTitle.Length > 0 ? rawTitle : table.TableName,
            ClassName = className,
            ExamName = examName,
            Subjects = subjectsList,
            Students = students,
            ImportNotice = notice,
        };
    }

    private static string CellStr(object? cell)
    {
        if (cell == null || cell == DBNull.Value) return string.Empty;
        return cell.ToString()?.Trim() ?? string.Empty;
    }

    private static double? CellDouble(object? cell)
    {
        if (cell == null || cell == DBNull.Value) return null;
        if (cell is double d) return d;
        if (cell is float f) return f;
        if (cell is int i) return i;
        if (cell is long l) return l;
        if (cell is decimal dec) return (double)dec;
        var str = cell.ToString()?.Trim();
        if (string.IsNullOrEmpty(str) || str is "/" or "-" or "—" or "–" or "缺考" or "无") return null;
        return double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static int? CellInt(object? cell)
    {
        var d = CellDouble(cell);
        return d.HasValue ? (int)Math.Round(d.Value, MidpointRounding.AwayFromZero) : null;
    }


    private static readonly string[] KnownSubjects =
    {
        "道德与法治", "思想政治", "体育与健康", "生物学", "信息技术", "通用技术",
        "语文", "数学", "英语", "物理", "化学", "生物", "政治", "道法", "历史", "地理", "体育", "科学", "技术", "外语", "日语", "俄语"
    };

    private static string? ExtractSubjectFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var bracketMatch = Regex.Match(text, @"[\(（](?<subcontent>[^\(\)（）]+)[\)）]");
        if (bracketMatch.Success)
        {
            var content = bracketMatch.Groups["subcontent"].Value.Trim();
            foreach (var sub in KnownSubjects)
            {
                if (content.Contains(sub)) return sub;
            }
        }

        foreach (var sub in KnownSubjects)
        {
            if (text.Contains(sub)) return sub;
        }

        return null;
    }

    private static ExamData ReadSingleSubjectPersonalScoreReport(DataTable table, string path)
    {
        var studentHeaders = new List<(int RowIndex, string Name, string ExamNo)>();
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var cell0 = CellStr(table.Rows[r][0]);
            if (IsStudentCardHeader(cell0))
            {
                var match = Regex.Match(cell0, @"^([^\(（\d\s_-]+?)(?:[\(（](\d+)[\)）])?(?:_|-|\s+)?成绩单$");
                if (match.Success)
                {
                    var name = match.Groups[1].Value.Trim();
                    var examNo = match.Groups[2].Success ? match.Groups[2].Value.Trim() : string.Empty;
                    studentHeaders.Add((r, name, examNo));
                }
            }
        }

        if (studentHeaders.Count == 0)
            throw new InvalidDataException("未能从表格中识别到任何学生单科个人成绩单。");

        var firstStudentRow = studentHeaders[0].RowIndex;
        var rawTitle = string.Empty;
        for (var r = firstStudentRow + 1; r < Math.Min(firstStudentRow + 5, table.Rows.Count); r++)
        {
            var t = CellStr(table.Rows[r][0]);
            if (!string.IsNullOrWhiteSpace(t) && !t.Contains("考试中成绩为") && !t.Contains("得分明细"))
            {
                rawTitle = t;
                break;
            }
        }

        // 提取科目
        var subjectName = ExtractSubjectFromText(rawTitle) ??
                          ExtractSubjectFromText(table.TableName);

        if (string.IsNullOrEmpty(subjectName))
        {
            for (var r = firstStudentRow; r < Math.Min(firstStudentRow + 20, table.Rows.Count); r++)
            {
                for (var c = 0; c < Math.Min(5, table.Columns.Count); c++)
                {
                    var cellText = CellStr(table.Rows[r][c]);
                    var found = ExtractSubjectFromText(cellText);
                    if (!string.IsNullOrEmpty(found))
                    {
                        subjectName = found;
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(subjectName)) break;
            }
        }

        subjectName ??= "单科";

        // 提取班级
        var className = string.Empty;
        if (!string.IsNullOrEmpty(rawTitle))
        {
            var classMatch = Regex.Match(rawTitle, @"[-_\s]?((?:初[一二三]|高[一二三四]|七|八|九|[一二三四五六七八九\d]+年级|[初高七八九\d]+)?\s*[\(（]?\d+[\)）]?班)$");
            if (classMatch.Success) className = classMatch.Groups[1].Value.Trim();
        }
        if (string.IsNullOrEmpty(className) && table.TableName.EndsWith("班"))
        {
            className = table.TableName.Trim();
        }
        if (string.IsNullOrEmpty(className))
        {
            className = ClassFromTitle(rawTitle);
        }

        // 提取考试名
        var examName = string.Empty;
        if (!string.IsNullOrEmpty(className) && rawTitle.EndsWith(className))
        {
            var t = rawTitle[..^className.Length].TrimEnd('-', '_', ' ');
            t = Regex.Replace(t, @"[\(（][^\(\)（）]+[\)）]", "").TrimEnd('-', '_', ' ');
            examName = ExtractExamName(t);
        }
        if (string.IsNullOrEmpty(examName))
        {
            var t = Regex.Replace(rawTitle, @"[\(（][^\(\)（）]+[\)）]", "").TrimEnd('-', '_', ' ');
            examName = ExtractExamName(t);
        }
        if (string.IsNullOrEmpty(examName))
        {
            examName = Path.GetFileNameWithoutExtension(path);
        }

        var students = new List<StudentRecord>();

        for (var i = 0; i < studentHeaders.Count; i++)
        {
            var (sRow, sName, sExamNo) = studentHeaders[i];
            var nextSRow = (i + 1 < studentHeaders.Count) ? studentHeaders[i + 1].RowIndex : table.Rows.Count;

            double? score = null;
            int? classRank = null;
            int? schoolRank = null;

            for (var r = sRow + 1; r < nextSRow; r++)
            {
                var rowText = string.Empty;
                for (var c = 0; c < table.Columns.Count; c++)
                {
                    var val = CellStr(table.Rows[r][c]);
                    if (!string.IsNullOrWhiteSpace(val)) rowText += val + " ";
                }

                if (rowText.Contains("成绩为") || rowText.Contains("考试中成绩为"))
                {
                    var mScore = Regex.Match(rowText, @"成绩为\s*(?<score>\d+(?:\.\d+)?)\s*分");
                    if (mScore.Success && double.TryParse(mScore.Groups["score"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var sc))
                    {
                        score ??= sc;
                    }

                    var mClass = Regex.Match(rowText, @"班级名次\s*(?<cr>\d+)\s*名");
                    if (mClass.Success && int.TryParse(mClass.Groups["cr"].Value, out var cr))
                    {
                        classRank ??= cr;
                    }

                    var mSchool = Regex.Match(rowText, @"年级名次\s*(?<sr>\d+)\s*名");
                    if (mSchool.Success && int.TryParse(mSchool.Groups["sr"].Value, out var sr))
                    {
                        schoolRank ??= sr;
                    }
                }

                var c0 = CellStr(table.Rows[r][0]);
                if (c0 is "题号" || c0.StartsWith("题号"))
                {
                    var totalCol = -1;
                    for (var c = 1; c < table.Columns.Count; c++)
                    {
                        var colHeader = CellStr(table.Rows[r][c]);
                        if (TotalNames.Contains(colHeader))
                        {
                            totalCol = c;
                            break;
                        }
                    }

                    if (r + 1 < nextSRow)
                    {
                        var nextC0 = CellStr(table.Rows[r + 1][0]);
                        if (nextC0 is "得分" || nextC0.StartsWith("得分"))
                        {
                            if (totalCol > 0)
                            {
                                var tableScore = CellDouble(table.Rows[r + 1][totalCol]);
                                score ??= tableScore;
                            }
                            else
                            {
                                for (var c = table.Columns.Count - 1; c >= 1; c--)
                                {
                                    var val = CellDouble(table.Rows[r + 1][c]);
                                    if (val.HasValue)
                                    {
                                        score ??= val;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (rowText.Contains("缺考"))
                {
                    score = null;
                }
            }

            var subjScore = new SubjectScore
            {
                Score = score,
                ClassRank = classRank,
                SchoolRank = schoolRank,
            };

            var scores = new Dictionary<string, SubjectScore>(StringComparer.Ordinal)
            {
                [subjectName] = subjScore
            };

            students.Add(new StudentRecord
            {
                No = students.Count + 1,
                Name = sName,
                ExamNo = sExamNo,
                Subjects = scores,
                Total = subjScore,
            });
        }

        PopulateMissingClassRanks(students, new[] { subjectName });

        var notice = $"提示：已成功从科任老师「{subjectName}」单科个人成绩单导入。如需查看全学科综合分析，请导入全科成绩汇总表。";

        return new ExamData
        {
            SourceTitle = rawTitle.Length > 0 ? rawTitle : table.TableName,
            ClassName = className,
            ExamName = examName,
            Subjects = new[] { subjectName },
            Students = students,
            ImportNotice = notice,
        };
    }


    private static string CellExamNumber(object? cell)
    {
        if (cell == null || cell == DBNull.Value) return string.Empty;
        if (cell is double d) return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
        if (cell is float f) return ((long)Math.Round(f)).ToString(CultureInfo.InvariantCulture);
        if (cell is int i) return i.ToString(CultureInfo.InvariantCulture);
        if (cell is long l) return l.ToString(CultureInfo.InvariantCulture);
        if (cell is decimal dec) return ((long)Math.Round(dec)).ToString(CultureInfo.InvariantCulture);
        var str = cell.ToString()?.Trim() ?? string.Empty;
        if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            if (str.Contains('.') && Math.Abs(parsed - Math.Round(parsed)) < 1e-6)
                return ((long)Math.Round(parsed)).ToString(CultureInfo.InvariantCulture);
        }
        return str;
    }
    private static bool TryLoadTabularXls(
        string path, ImportOptions options, [NotNullWhen(true)] out ExamData? examData)
    {
        examData = null;
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelReaderFactory.CreateReader(stream);
            var dataSet = reader.AsDataSet();

            var candidateTables = new List<DataTable>();
            if (!string.IsNullOrWhiteSpace(options.SheetName))
            {
                var t = dataSet.Tables[options.SheetName];
                if (t != null) candidateTables.Add(t);
            }
            else
            {
                foreach (DataTable t in dataSet.Tables)
                    if (t.TableName.Contains("汇总")) candidateTables.Add(t);
                foreach (DataTable t in dataSet.Tables)
                    if (t.TableName.Contains("简表") || t.TableName.Contains("成绩"))
                        if (!candidateTables.Contains(t)) candidateTables.Add(t);
                foreach (DataTable t in dataSet.Tables)
                    if (!candidateTables.Contains(t)) candidateTables.Add(t);
            }

            string? failedSheetInfo = null;
            foreach (var table in candidateTables)
            {
                if (TryReadTabularDataTable(table, path, out examData))
                {
                    TryEnrichTotalDeltasXls(dataSet, examData.Students);

                    if (failedSheetInfo != null && string.IsNullOrEmpty(examData.ImportNotice))
                    {
                        examData.ImportNotice = $"提示：工作表「{failedSheetInfo}」缺少各科成绩，已自动读取「{table.TableName}」。当前表格缺少单科进退步等数据，如需完整对比分析，请前往智学网下载详细的成绩单（全学科成绩汇总表）。";
                    }

                    return true;
                }
                else
                {
                    failedSheetInfo ??= table.TableName;
                }
            }

            return false;
        }
        catch
        {
            examData = null;
            return false;
        }
    }

    private static bool TryReadTabularDataTable(
        DataTable table, string path, [NotNullWhen(true)] out ExamData? examData)
    {
        // 1. 首选：两级表头全科成绩汇总/简表
        if (TryReadTwoLevelHeaderDataTable(table, path, out examData))
            return true;

        // 2. 备选：单行表头宽表（支持多学科与科任老师单科）
        if (TryReadWideDataTable(table, path, out examData))
            return true;

        return false;
    }

    private static bool TryReadTwoLevelHeaderDataTable(
        DataTable table, string path, [NotNullWhen(true)] out ExamData? examData)
    {
        examData = null;
        if (table.Rows.Count < 3 || table.Columns.Count < 2) return false;

        var headerRow = -1;
        for (var r = 0; r < Math.Min(10, table.Rows.Count); r++)
        {
            var scoreCount = 0;
            for (var c = 0; c < table.Columns.Count; c++)
            {
                if (CellStr(table.Rows[r][c]) == "得分") scoreCount++;
            }
            if (scoreCount >= 3)
            {
                headerRow = r;
                break;
            }
        }

        if (headerRow < 0) return false;

        var baseColumns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var label = CellStr(table.Rows[headerRow][c]);
            if (string.IsNullOrEmpty(label) && headerRow > 0)
                label = CellStr(table.Rows[headerRow - 1][c]);

            if (SubHeaderNames.Contains(label)) continue;
            if (string.IsNullOrEmpty(label) || baseColumns.ContainsKey(label)) continue;
            baseColumns[label] = c;
        }

        var nameCol = baseColumns.GetValueOrDefault("姓名", -1);
        if (nameCol < 0) return false;

        var noCol = baseColumns.GetValueOrDefault("序号", -1);
        var examNoCol = baseColumns.GetValueOrDefault("准考证号", -1);
        if (examNoCol < 0) examNoCol = baseColumns.GetValueOrDefault("考号", -1);
        if (examNoCol < 0) examNoCol = baseColumns.GetValueOrDefault("学号", -1);
        if (examNoCol < 0) examNoCol = baseColumns.GetValueOrDefault("自定义考号", -1);
        var classCol = baseColumns.GetValueOrDefault("班级", -1);

        var blocks = new List<SubjectBlock>();
        var currentSubject = string.Empty;

        for (var c = 0; c < table.Columns.Count; c++)
        {
            var subjectCell = headerRow > 0 ? CellStr(table.Rows[headerRow - 1][c]) : string.Empty;
            if (!string.IsNullOrEmpty(subjectCell)) currentSubject = subjectCell;

            if (CellStr(table.Rows[headerRow][c]) != "得分") continue;

            var block = new SubjectBlock { Subject = currentSubject, ScoreColumn = c };
            for (var j = 1; j <= 4 && c + j < table.Columns.Count; j++)
            {
                var label = CellStr(table.Rows[headerRow][c + j]);
                switch (label)
                {
                    case "校次": block.SchoolRankColumn = c + j; break;
                    case "校次进退步": block.SchoolDeltaColumn = c + j; break;
                    case "班次": block.ClassRankColumn = c + j; break;
                    case "班次进退步": block.ClassDeltaColumn = c + j; break;
                }
            }
            blocks.Add(block);
        }

        if (blocks.Count == 0) return false;

        var totalBlock = blocks.FirstOrDefault(b => TotalNames.Contains(b.Subject));
        var subjects = blocks.Where(b => !ReferenceEquals(b, totalBlock)).ToList();
        if (subjects.Count == 0) return false;

        var students = new List<StudentRecord>();
        var className = string.Empty;

        for (var r = headerRow + 1; r < table.Rows.Count; r++)
        {
            var name = CellStr(table.Rows[r][nameCol]);
            if (!IsStudentName(name)) continue;

            if (className.Length == 0 && classCol >= 0)
                className = CellStr(table.Rows[r][classCol]);

            var scores = new Dictionary<string, SubjectScore>(StringComparer.Ordinal);
            foreach (var block in subjects)
            {
                scores[block.Subject] = new SubjectScore
                {
                    Score = CellDouble(table.Rows[r][block.ScoreColumn]),
                    SchoolRank = block.SchoolRankColumn > 0 ? CellInt(table.Rows[r][block.SchoolRankColumn]) : null,
                    SchoolDelta = block.SchoolDeltaColumn > 0 ? CellInt(table.Rows[r][block.SchoolDeltaColumn]) : null,
                    ClassRank = block.ClassRankColumn > 0 ? CellInt(table.Rows[r][block.ClassRankColumn]) : null,
                    ClassDelta = block.ClassDeltaColumn > 0 ? CellInt(table.Rows[r][block.ClassDeltaColumn]) : null,
                };
            }

            SubjectScore totalScore;
            if (totalBlock != null)
            {
                totalScore = new SubjectScore
                {
                    Score = CellDouble(table.Rows[r][totalBlock.ScoreColumn]),
                    SchoolRank = totalBlock.SchoolRankColumn > 0 ? CellInt(table.Rows[r][totalBlock.SchoolRankColumn]) : null,
                    SchoolDelta = totalBlock.SchoolDeltaColumn > 0 ? CellInt(table.Rows[r][totalBlock.SchoolDeltaColumn]) : null,
                    ClassRank = totalBlock.ClassRankColumn > 0 ? CellInt(table.Rows[r][totalBlock.ClassRankColumn]) : null,
                    ClassDelta = totalBlock.ClassDeltaColumn > 0 ? CellInt(table.Rows[r][totalBlock.ClassDeltaColumn]) : null,
                };
            }
            else
            {
                var scored = scores.Values.Where(s => s.Score.HasValue).Select(s => s.Score!.Value).ToList();
                totalScore = new SubjectScore
                {
                    Score = scored.Count == 0 ? null : Math.Round(scored.Sum(), 2, MidpointRounding.AwayFromZero)
                };
            }

            students.Add(new StudentRecord
            {
                No = noCol >= 0 ? CellInt(table.Rows[r][noCol]) ?? students.Count + 1 : students.Count + 1,
                Name = name,
                ExamNo = examNoCol >= 0 ? CellExamNumber(table.Rows[r][examNoCol]) : string.Empty,
                Subjects = scores,
                Total = totalScore,
            });
        }

        if (students.Count == 0) return false;

        var subjectNames = subjects.Select(s => s.Subject).ToList();
        PopulateMissingClassRanks(students, subjectNames);

        var rawTitle = headerRow >= 1 ? CellStr(table.Rows[0][0]) : string.Empty;
        var finalClass = className.Length > 0 ? className : ClassFromTitle(rawTitle);
        var examName = ExtractExamName(rawTitle);
        if (string.IsNullOrEmpty(examName)) examName = Path.GetFileNameWithoutExtension(path);

        var hasDeltas = students.Any(s => s.Total.ClassDelta.HasValue || s.Total.SchoolDelta.HasValue);
        string? notice = null;
        if (table.TableName.Contains("简表") || !hasDeltas)
        {
            notice = $"提示：当前表格「{table.TableName}」未包含单科进退步数据。如需查看完整对比分析，请前往智学网下载详细的成绩单（全学科成绩汇总表）。";
        }

        examData = new ExamData
        {
            SourceTitle = rawTitle.Length > 0 ? rawTitle : table.TableName,
            ClassName = finalClass,
            ExamName = examName,
            Subjects = subjectNames,
            Students = students,
            ImportNotice = notice,
        };
        return true;
    }

    private static bool TryReadWideDataTable(
        DataTable table, string path, [NotNullWhen(true)] out ExamData? examData)
    {
        examData = null;
        if (table.Rows.Count < 2 || table.Columns.Count < 2) return false;

        var headerRow = -1;
        var nameCol = -1;
        for (var r = 0; r < Math.Min(10, table.Rows.Count); r++)
        {
            for (var c = 0; c < table.Columns.Count; c++)
            {
                if (CellStr(table.Rows[r][c]) == "姓名")
                {
                    headerRow = r;
                    nameCol = c;
                    break;
                }
            }
            if (headerRow >= 0) break;
        }

        if (headerRow < 0 || nameCol < 0) return false;

        var headers = new Dictionary<int, string>();
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var label = CellStr(table.Rows[headerRow][c]);
            if (!string.IsNullOrEmpty(label)) headers[c] = label;
        }

        var noCol = headers.FirstOrDefault(h => h.Value == "序号").Key;
        var examNoCol = headers.FirstOrDefault(h => h.Value is "准考证号" or "考号" or "学号" or "自定义考号").Key;
        var classCol = headers.FirstOrDefault(h => h.Value == "班级").Key;
        var totalCol = headers.FirstOrDefault(h => TotalNames.Contains(h.Value)).Key;

        var subjectColumns = headers
            .Where(h => h.Key != nameCol && h.Key != noCol && h.Key != examNoCol && h.Key != classCol)
            .Where(h => h.Key != totalCol)
            .Where(h => !BaseColumnNames.Contains(h.Value))
            .Where(h => !RankKeyword.Any(k => h.Value.Contains(k)))
            .ToList();

        var rawTitle = headerRow >= 1 ? CellStr(table.Rows[0][0]) : string.Empty;

        var isSingleSubject = false;
        string? singleSubjectName = null;

        if (subjectColumns.Count == 1 && subjectColumns[0].Value is "得分" or "成绩" or "实得分")
        {
            isSingleSubject = true;
        }
        else if (subjectColumns.Count == 0 && (totalCol > 0 || headers.Values.Any(v => v is "得分" or "成绩")))
        {
            isSingleSubject = true;
        }
        else if (subjectColumns.Count > 1 &&
                 subjectColumns.All(s => s.Value is "得分" or "客观分" or "主观分" or "单选题" or "填空题" or "解答题" || int.TryParse(s.Value, out _)))
        {
            isSingleSubject = true;
        }

        if (isSingleSubject)
        {
            singleSubjectName = ExtractSubjectFromText(rawTitle) ??
                                ExtractSubjectFromText(table.TableName) ??
                                "单科";

            var scoreCol = headers.FirstOrDefault(h => h.Value is "得分" or "实得分" or "成绩").Key;
            if (scoreCol == 0 && totalCol > 0) scoreCol = totalCol;
            if (scoreCol == 0 && subjectColumns.Count > 0) scoreCol = subjectColumns[0].Key;

            var schoolRankCol = headers.FirstOrDefault(h => h.Value is "校次" or "年级名次" or "年名次" or "校排名").Key;
            var schoolDeltaCol = headers.FirstOrDefault(h => h.Value is "校次进退步" or "校进退步" or "年级进退步").Key;
            var classRankCol = headers.FirstOrDefault(h => h.Value is "班次" or "班级名次" or "班名次" or "班排名").Key;
            var classDeltaCol = headers.FirstOrDefault(h => h.Value is "班次进退步" or "班进退步" or "班级进退步").Key;

            var students = new List<StudentRecord>();
            var detectedClass = string.Empty;

            for (var r = headerRow + 1; r < table.Rows.Count; r++)
            {
                var name = CellStr(table.Rows[r][nameCol]);
                if (!IsStudentName(name)) continue;

                if (detectedClass.Length == 0 && classCol > 0)
                    detectedClass = CellStr(table.Rows[r][classCol]);

                var score = scoreCol > 0 ? CellDouble(table.Rows[r][scoreCol]) : null;
                var classRank = classRankCol > 0 ? CellInt(table.Rows[r][classRankCol]) : null;
                var classDelta = classDeltaCol > 0 ? CellInt(table.Rows[r][classDeltaCol]) : null;
                var schoolRank = schoolRankCol > 0 ? CellInt(table.Rows[r][schoolRankCol]) : null;
                var schoolDelta = schoolDeltaCol > 0 ? CellInt(table.Rows[r][schoolDeltaCol]) : null;

                var subjScore = new SubjectScore
                {
                    Score = score,
                    ClassRank = classRank,
                    ClassDelta = classDelta,
                    SchoolRank = schoolRank,
                    SchoolDelta = schoolDelta,
                };

                var scores = new Dictionary<string, SubjectScore>(StringComparer.Ordinal)
                {
                    [singleSubjectName] = subjScore
                };

                students.Add(new StudentRecord
                {
                    No = noCol > 0 ? CellInt(table.Rows[r][noCol]) ?? students.Count + 1 : students.Count + 1,
                    Name = name,
                    ExamNo = examNoCol > 0 ? CellExamNumber(table.Rows[r][examNoCol]) : string.Empty,
                    Subjects = scores,
                    Total = subjScore,
                });
            }

            if (students.Count == 0) return false;

            PopulateMissingClassRanks(students, new[] { singleSubjectName });

            var finalClass = detectedClass.Length > 0 ? detectedClass : ClassFromTitle(rawTitle);
            var examName = ExtractExamName(rawTitle);
            if (string.IsNullOrEmpty(examName)) examName = Path.GetFileNameWithoutExtension(path);

            examData = new ExamData
            {
                SourceTitle = rawTitle.Length > 0 ? rawTitle : table.TableName,
                ClassName = finalClass,
                ExamName = examName,
                Subjects = new[] { singleSubjectName },
                Students = students,
                ImportNotice = $"提示：当前为科任老师「{singleSubjectName}」单科成绩表。已完整识别该科目实考成绩、班级/年级名次及进退步。",
            };
            return true;
        }

        // 多学科单行宽表解析
        if (subjectColumns.Count > 0)
        {
            var subjectsList = subjectColumns.Select(s => s.Value).Distinct().ToList();
            var students = new List<StudentRecord>();
            var detectedClass = string.Empty;

            var totalClassRankCol = headers.FirstOrDefault(h => h.Value is "总分班次" or "班次" or "班级名次" or "班排名").Key;
            var totalSchoolRankCol = headers.FirstOrDefault(h => h.Value is "总分校次" or "校次" or "年级名次" or "校排名").Key;

            for (var r = headerRow + 1; r < table.Rows.Count; r++)
            {
                var name = CellStr(table.Rows[r][nameCol]);
                if (!IsStudentName(name)) continue;

                if (detectedClass.Length == 0 && classCol > 0)
                    detectedClass = CellStr(table.Rows[r][classCol]);

                var scores = new Dictionary<string, SubjectScore>(StringComparer.Ordinal);
                foreach (var col in subjectColumns)
                {
                    scores[col.Value] = new SubjectScore
                    {
                        Score = CellDouble(table.Rows[r][col.Key])
                    };
                }

                double? totalScoreVal = totalCol > 0 ? CellDouble(table.Rows[r][totalCol]) : null;
                if (!totalScoreVal.HasValue)
                {
                    var scored = scores.Values.Where(s => s.Score.HasValue).Select(s => s.Score!.Value).ToList();
                    totalScoreVal = scored.Count == 0 ? null : Math.Round(scored.Sum(), 2, MidpointRounding.AwayFromZero);
                }

                students.Add(new StudentRecord
                {
                    No = noCol > 0 ? CellInt(table.Rows[r][noCol]) ?? students.Count + 1 : students.Count + 1,
                    Name = name,
                    ExamNo = examNoCol > 0 ? CellExamNumber(table.Rows[r][examNoCol]) : string.Empty,
                    Subjects = scores,
                    Total = new SubjectScore
                    {
                        Score = totalScoreVal,
                        ClassRank = totalClassRankCol > 0 ? CellInt(table.Rows[r][totalClassRankCol]) : null,
                        SchoolRank = totalSchoolRankCol > 0 ? CellInt(table.Rows[r][totalSchoolRankCol]) : null,
                    }
                });
            }

            if (students.Count == 0) return false;

            PopulateMissingClassRanks(students, subjectsList);

            var finalClass = detectedClass.Length > 0 ? detectedClass : ClassFromTitle(rawTitle);
            var examName = ExtractExamName(rawTitle);
            if (string.IsNullOrEmpty(examName)) examName = Path.GetFileNameWithoutExtension(path);

            examData = new ExamData
            {
                SourceTitle = rawTitle.Length > 0 ? rawTitle : table.TableName,
                ClassName = finalClass,
                ExamName = examName,
                Subjects = subjectsList,
                Students = students,
            };
            return true;
        }

        return false;
    }

    private static void TryEnrichTotalDeltasXls(DataSet dataSet, IReadOnlyList<StudentRecord> students)
    {
        var hasDeltas = students.Any(s => s.Total.ClassDelta.HasValue || s.Total.SchoolDelta.HasValue);
        if (hasDeltas) return;

        foreach (DataTable table in dataSet.Tables)
        {
            if (table.Rows.Count < 2 || table.Columns.Count < 2) continue;

            var nameCol = -1;
            var schoolDeltaCol = -1;
            var classDeltaCol = -1;
            var classRankCol = -1;
            var headerRow = -1;

            for (var r = 0; r < Math.Min(10, table.Rows.Count); r++)
            {
                nameCol = -1;
                schoolDeltaCol = -1;
                classDeltaCol = -1;
                classRankCol = -1;

                for (var c = 0; c < table.Columns.Count; c++)
                {
                    var val = CellStr(table.Rows[r][c]);
                    if (val == "姓名") nameCol = c;
                    else if (val is "校次进退步" or "校进退步" or "年级进退步") schoolDeltaCol = c;
                    else if (val is "班次进退步" or "班进退步" or "班级进退步") classDeltaCol = c;
                    else if (val is "总分班次" or "班次" or "班名次") classRankCol = c;
                }

                if (nameCol >= 0 && (schoolDeltaCol >= 0 || classDeltaCol >= 0))
                {
                    headerRow = r;
                    break;
                }
            }

            if (nameCol < 0 || (schoolDeltaCol < 0 && classDeltaCol < 0) || headerRow < 0)
                continue;

            var deltaMap = new Dictionary<string, (int? SchoolDelta, int? ClassDelta, int? ClassRank)>(StringComparer.Ordinal);
            for (var r = headerRow + 1; r < table.Rows.Count; r++)
            {
                var name = CellStr(table.Rows[r][nameCol]);
                if (!IsStudentName(name)) continue;

                var sd = schoolDeltaCol >= 0 ? CellInt(table.Rows[r][schoolDeltaCol]) : null;
                var cd = classDeltaCol >= 0 ? CellInt(table.Rows[r][classDeltaCol]) : null;
                var cr = classRankCol >= 0 ? CellInt(table.Rows[r][classRankCol]) : null;
                deltaMap[name] = (sd, cd, cr);
            }

            if (deltaMap.Count == 0) continue;

            foreach (var student in students)
            {
                if (deltaMap.TryGetValue(student.Name, out var d))
                {
                    student.Total = new SubjectScore
                    {
                        Score = student.Total.Score,
                        SchoolRank = student.Total.SchoolRank,
                        SchoolDelta = student.Total.SchoolDelta ?? d.SchoolDelta,
                        ClassRank = student.Total.ClassRank ?? d.ClassRank,
                        ClassDelta = student.Total.ClassDelta ?? d.ClassDelta,
                    };
                }
            }

            break;
        }
    }

}
