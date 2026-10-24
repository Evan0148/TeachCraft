using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using ExcelDataReader;
using GradeStrip.Core.Model;

namespace GradeStrip.Core.Import;

/// <summary>
/// 读取智学网导出的「分析报表」（统计表 / 试卷分析 / 名单）。
/// 与成绩条通道相互独立：成绩总表（全科汇总、单科成绩报告、学生小题得分明细等）一律返回空列表。
/// </summary>
public static class OverviewReportImporter
{
    /// <summary>判断表头行的关键词：命中至少两个才认为是列标题行。</summary>
    private static readonly string[] HeaderKeywords =
    {
        "序号", "班级", "学科", "科目", "题号", "姓名", "准考证号", "类别", "人数", "得分",
        "分数段", "均分", "占比", "统计", "难度",
    };

    /// <summary>两级表头中「子表头」常见的短词，用于把上一行识别为分组表头。</summary>
    private static readonly string[] SubHeaderTokens =
    {
        "人数", "占比%", "占比％", "占比", "比例%", "比例％", "比例",
        "得分", "教师", "排名", "均分", "得分率%", "得分率％", "得分率",
        "校次", "班次", "校次进退步", "班次进退步", "班级", "年级",
        "前10名", "后10名", "前50名", "后50名",
    };

    /// <summary>工作表名正好是这些名称时，视为整张表只讲一个学科。</summary>
    private static readonly string[] SubjectSheetNames =
    {
        "语文", "数学", "英语", "物理", "化学", "生物学", "政治", "历史", "地理", "体育", "总分",
    };

    /// <summary>「800-850」这类分数段区间（含 [800,850] 写法），用于结构化兜底识别。</summary>
    private static readonly Regex SegmentRangeRegex = new(@"(?:\[\d+[,，]\d+\]|\b\d{1,3}-\d{1,3}\b)", RegexOptions.Compiled);

    static OverviewReportImporter()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    /// <summary>只读标题、工作表名与首表表头，用于入口分流。</summary>
    public static OverviewReportKind DetectKind(string path)
    {
        try
        {
            path = Normalize(path);
            if (!IsSupportedFile(path)) return OverviewReportKind.Generic;

            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelReaderFactory.CreateReader(stream);

            var sheetNames = new List<string>();
            var headTexts = new List<string>();
            var title = string.Empty;
            var firstHeaderHasName = false;
            var sheetIndex = 0;

            do
            {
                sheetNames.Add((reader.Name ?? string.Empty).Trim());

                if (sheetIndex == 0)
                {
                    var row = 0;
                    while (row < 10 && reader.Read())
                    {
                        if (row == 0)
                        {
                            for (var c = 0; c < reader.FieldCount; c++)
                            {
                                var value = CellStr(reader.GetValue(c));
                                if (value.Length > 0) { title = value; break; }
                            }
                        }

                        for (var c = 0; c < reader.FieldCount; c++)
                        {
                            if (CellStr(reader.GetValue(c)) == "姓名") firstHeaderHasName = true;
                        }

                        // 结构化兜底用：收集前 5 行的全部文本
                        if (row < 5)
                        {
                            for (var c = 0; c < reader.FieldCount; c++)
                            {
                                var value = CellStr(reader.GetValue(c));
                                if (value.Length > 0) headTexts.Add(value);
                            }
                        }

                        row++;
                    }
                }

                sheetIndex++;
            } while (reader.NextResult());

            var haystack = string.Join('\n', new[]
            {
                title,
                Path.GetFileNameWithoutExtension(path),
                string.Join('\n', sheetNames),
            });

            var kind = Classify(haystack, firstHeaderHasName);
            if (kind == OverviewReportKind.Generic)
            {
                // 关键词没命中时按表头结构兜底识别（如文件名被重命名过的报表）
                kind = ClassifyByStructure(string.Join('\n', headTexts));
            }

            return kind;
        }
        catch
        {
            return OverviewReportKind.Generic;
        }
    }

    /// <summary>在已读入的 DataSet 上判断文件级类别，避免为探测再打开一次文件。</summary>
    private static OverviewReportKind DetectKind(DataSet dataSet, string path)
    {
        var sheetNames = new List<string>();
        foreach (DataTable table in dataSet.Tables)
            sheetNames.Add((table.TableName ?? string.Empty).Trim());

        var title = string.Empty;
        var firstHeaderHasName = false;
        var headTexts = new List<string>();

        if (dataSet.Tables.Count > 0)
        {
            var first = dataSet.Tables[0];
            var rowCount = Math.Min(10, first.Rows.Count);
            for (var r = 0; r < rowCount; r++)
            {
                if (r == 0)
                {
                    for (var c = 0; c < first.Columns.Count; c++)
                    {
                        var value = CellStr(first.Rows[0][c]);
                        if (value.Length > 0) { title = value; break; }
                    }
                }

                for (var c = 0; c < first.Columns.Count; c++)
                {
                    if (CellStr(first.Rows[r][c]) == "姓名") firstHeaderHasName = true;
                }

                // 结构化兜底用：收集前 5 行的全部文本
                if (r < 5)
                {
                    for (var c = 0; c < first.Columns.Count; c++)
                    {
                        var value = CellStr(first.Rows[r][c]);
                        if (value.Length > 0) headTexts.Add(value);
                    }
                }

                if (firstHeaderHasName) break;
            }
        }

        var haystack = string.Join('\n', new[]
        {
            title,
            Path.GetFileNameWithoutExtension(path),
            string.Join('\n', sheetNames),
        });

        var kind = Classify(haystack, firstHeaderHasName);
        if (kind == OverviewReportKind.Generic)
        {
            // 关键词没命中时按表头结构兜底识别（如文件名被重命名过的报表）
            kind = ClassifyByStructure(string.Join('\n', headTexts));
        }

        return kind;
    }

    /// <summary>是否为可展示的分析报表；成绩总表与无法识别的文件返回 false。</summary>
    public static bool IsReportFile(string path) =>
        DetectKind(path) != OverviewReportKind.Generic;

    /// <summary>解析整份报表；任何失败都返回空列表，不抛异常。</summary>
    public static IReadOnlyList<OverviewReport> Load(string path) => Load(path, out _);

    /// <summary>解析整份报表并返回文件级类别；只打开工作簿一次。</summary>
    public static IReadOnlyList<OverviewReport> Load(string path, out OverviewReportKind kind)
    {
        kind = OverviewReportKind.Generic;
        try
        {
            path = Normalize(path);
            if (!IsSupportedFile(path)) return Array.Empty<OverviewReport>();

            DataSet dataSet;
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                dataSet = reader.AsDataSet();
            }

            if (dataSet.Tables.Count == 0) return Array.Empty<OverviewReport>();

            // 类别在已读入的 DataSet 上判断，不再二次打开文件。
            kind = DetectKind(dataSet, path);

            // 文件级类别明确时，全部工作表归入该类别；
            // 成绩总表（Generic）也可能附带报表页（如「成绩汇总 + 班级成绩对比」混合工作簿），
            // 此时逐表按自身标题 / 表名识别，有报表页就一并导出。
            var groups = new List<(OverviewReportKind Kind, List<OverviewSheet> Sheets)>();
            if (kind != OverviewReportKind.Generic)
            {
                var list = new List<OverviewSheet>();
                foreach (DataTable table in dataSet.Tables)
                {
                    try
                    {
                        // 混合工作簿：跳过夹在报表里的成绩数据页（姓名 + 多个学科列）
                        if (LooksLikeGradeDataSheet(table)) continue;

                        AddReportSheets(table, kind, list);
                    }
                    catch
                    {
                        // 单张工作表解析失败只跳过它，不影响整份报表。
                    }
                }
                if (list.Count > 0) groups.Add((kind, list));
            }
            else
            {
                foreach (DataTable table in dataSet.Tables)
                {
                    try
                    {
                        var sheetKind = Classify(
                            ReadTitle(table) + "\n" + table.TableName, firstHeaderHasName: false);
                        if (sheetKind == OverviewReportKind.Generic) continue;

                        var list = new List<OverviewSheet>();
                        AddReportSheets(table, sheetKind, list);
                        if (list.Count > 0) groups.Add((sheetKind, list));
                    }
                    catch
                    {
                        // 单张工作表解析失败只跳过它，不影响整份报表。
                    }
                }
            }

            if (groups.Count == 0) return Array.Empty<OverviewReport>();

            var title = ReadTitle(dataSet.Tables[0]);
            if (title.Length == 0) title = Path.GetFileNameWithoutExtension(path);

            return groups.Select(g => new OverviewReport
            {
                Kind = g.Kind,
                KindLabel = KindLabel(g.Kind),
                Title = title,
                SourceFile = Path.GetFileName(path),
                ExamName = ExtractExamName(title),
                Note = g.Sheets.Count > 1 ? $"共 {g.Sheets.Count} 张工作表" : null,
                Sheets = g.Sheets,
            }).ToList();
        }
        catch
        {
            kind = OverviewReportKind.Generic;
            return Array.Empty<OverviewReport>();
        }
    }

    /// <summary>按类别把一张工作表解析为报表页。</summary>
    private static void AddReportSheets(DataTable table, OverviewReportKind kind, List<OverviewSheet> sheets)
    {
        if (kind == OverviewReportKind.GradeLevels)
            AddGradeLevelSheets(table, sheets);
        else if (kind == OverviewReportKind.ChoiceDistribution)
            AddChoiceSheets(table, sheets);
        else
            AddGenericSheet(table, table.TableName, sheets);
    }

    /// <summary>
    /// 识别夹在报表工作簿里的成绩数据页：前 10 行内有一行同时含「姓名」与至少两个学科列（或「总分」）。
    /// 名单 / 等级明细（只有姓名 + 得分，没有学科列）不会被误判。
    /// </summary>
    private static bool LooksLikeGradeDataSheet(DataTable table)
    {
        var maxRow = Math.Min(10, table.Rows.Count - 1);
        for (var r = 0; r <= maxRow; r++)
        {
            var hasName = false;
            var subjects = 0;
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var value = CellStr(table.Rows[r][c]);
                if (value.Length == 0) continue;
                if (value == "姓名") hasName = true;
                else if (SubjectSheetNames.Contains(value, StringComparer.Ordinal) || value == "总分") subjects++;
            }

            if (hasName && subjects >= 2) return true;
        }

        return false;
    }

    // ---------------------------------------------------------------- 类别识别

    private static OverviewReportKind Classify(string haystack, bool firstHeaderHasName)
    {
        if (haystack.Contains("班级成绩对比", StringComparison.Ordinal)) return OverviewReportKind.ClassComparison;
        if (haystack.Contains("学科成绩对比", StringComparison.Ordinal)) return OverviewReportKind.SubjectMetrics;
        if (haystack.Contains("成绩分段对比", StringComparison.Ordinal) ||
            haystack.Contains("分段对比", StringComparison.Ordinal)) return OverviewReportKind.ScoreSegments;
        if (haystack.Contains("学业等级分布", StringComparison.Ordinal)) return OverviewReportKind.GradeLevels;
        if (haystack.Contains("优劣势学科", StringComparison.Ordinal)) return OverviewReportKind.SubjectAdvantage;
        if (haystack.Contains("平均分对比", StringComparison.Ordinal)) return OverviewReportKind.ClassAverages;

        if (haystack.Contains("优秀生学困生", StringComparison.Ordinal) ||
            haystack.Contains("优秀生与学困生", StringComparison.Ordinal))
        {
            return firstHeaderHasName
                ? OverviewReportKind.TopBottomRoster
                : OverviewReportKind.TopBottomStats;
        }

        if (haystack.Contains("临界生", StringComparison.Ordinal)) return OverviewReportKind.BorderlineRoster;
        if (haystack.Contains("缺考学生汇总", StringComparison.Ordinal) ||
            haystack.Contains("缺考名单", StringComparison.Ordinal)) return OverviewReportKind.AbsenceRoster;
        if (haystack.Contains("需关注学生", StringComparison.Ordinal)) return OverviewReportKind.AttentionRoster;
        if (haystack.Contains("小题分析", StringComparison.Ordinal) ||
            haystack.Contains("大题分析", StringComparison.Ordinal)) return OverviewReportKind.QuestionAnalysis;
        if (haystack.Contains("作答详情", StringComparison.Ordinal) ||
            haystack.Contains("答题明细", StringComparison.Ordinal) ||
            haystack.Contains("学生答题明细", StringComparison.Ordinal)) return OverviewReportKind.ChoiceDistribution;

        return OverviewReportKind.Generic;
    }

    /// <summary>
    /// 关键词没命中时的结构化兜底：按表头特征判断报表类别。
    /// 只在文件级 DetectKind 里使用，避免把成绩总表误判成报表。
    /// </summary>
    private static OverviewReportKind ClassifyByStructure(string headText)
    {
        var hasClass = headText.Contains("班级", StringComparison.Ordinal);
        var hasQuestionNo = headText.Contains("题号", StringComparison.Ordinal);

        if (hasClass && (headText.Contains("及格率", StringComparison.Ordinal) ||
                         headText.Contains("优秀率", StringComparison.Ordinal) ||
                         headText.Contains("平均分", StringComparison.Ordinal)))
        {
            return OverviewReportKind.ClassComparison;
        }

        if (headText.Contains("分数段", StringComparison.Ordinal) || SegmentRangeRegex.IsMatch(headText))
        {
            return OverviewReportKind.ScoreSegments;
        }

        if (hasQuestionNo && (headText.Contains("得分率", StringComparison.Ordinal) ||
                              headText.Contains("难度", StringComparison.Ordinal)))
        {
            return OverviewReportKind.QuestionAnalysis;
        }

        if (hasQuestionNo && (headText.Contains("选项", StringComparison.Ordinal) ||
                              headText.Contains("答对人数", StringComparison.Ordinal)))
        {
            return OverviewReportKind.ChoiceDistribution;
        }

        return OverviewReportKind.Generic;
    }

    private static string KindLabel(OverviewReportKind kind) => kind switch
    {
        OverviewReportKind.ClassComparison => "班级成绩对比",
        OverviewReportKind.SubjectMetrics => "学科成绩对比",
        OverviewReportKind.ScoreSegments => "成绩分段对比",
        OverviewReportKind.GradeLevels => "学业等级分布",
        OverviewReportKind.SubjectAdvantage => "优劣势学科",
        OverviewReportKind.ClassAverages => "班级平均分对比",
        OverviewReportKind.TopBottomStats => "优秀生学困生统计",
        OverviewReportKind.BorderlineRoster => "临界生名单",
        OverviewReportKind.TopBottomRoster => "优秀生学困生名单",
        OverviewReportKind.AbsenceRoster => "缺考学生名单",
        OverviewReportKind.AttentionRoster => "需关注学生",
        OverviewReportKind.QuestionAnalysis => "试题分析",
        OverviewReportKind.ChoiceDistribution => "作答详情",
        _ => "通用表格",
    };

    // ---------------------------------------------------------------- 工作表读取

    private static void AddGenericSheet(DataTable table, string name, List<OverviewSheet> sheets)
    {
        var lastRow = FindLastContentRow(table, 0);
        var sheet = BuildSheet(table, 0, lastRow, name, SubjectOf(name), null);
        if (sheet is not null) sheets.Add(sheet);
    }

    /// <summary>作答详情/学生答题明细：通用读取后，对「题号/题型/得分率/答对人数」做向下补齐。</summary>
    private static void AddChoiceSheets(DataTable table, List<OverviewSheet> sheets)
    {
        var lastRow = FindLastContentRow(table, 0);
        var sheet = BuildSheet(table, 0, lastRow, table.TableName, SubjectOf(table.TableName), null);
        if (sheet is null) return;

        if (sheet.Columns.Count == 7 && sheet.Columns[0].Header == "题号")
            sheet = FillChoiceGroups(sheet);

        sheets.Add(sheet);
    }

    /// <summary>同一道题的多行选项分布：下一条记录空出的题号/题型/得分率/答对人数沿用上一行。</summary>
    private static OverviewSheet FillChoiceGroups(OverviewSheet sheet)
    {
        var rows = new List<IReadOnlyList<string>>(sheet.Rows.Count);
        var previous = new string[5];
        foreach (var row in sheet.Rows)
        {
            var cells = row.ToArray();
            if (cells.Length >= 5)
            {
                if (cells[0].Length > 0)
                {
                    Array.Copy(cells, previous, 5);
                }
                else
                {
                    for (var i = 0; i < 5; i++) cells[i] = previous[i];
                }
            }
            rows.Add(cells);
        }

        return new OverviewSheet
        {
            Name = sheet.Name,
            Subject = sheet.Subject,
            Note = sheet.Note,
            Columns = sheet.Columns,
            Rows = rows,
        };
    }

    /// <summary>
    /// 学业等级分布：上半部分是 A–E 人数/占比，下半部分可能紧跟「学生等级分布详情」名单。
    /// 有名单时拆成「等级分布」与「学生等级明细」两张工作表。
    /// </summary>
    private static void AddGradeLevelSheets(DataTable table, List<OverviewSheet> sheets)
    {
        var lastRow = FindLastContentRow(table, 0);
        if (lastRow < 0) return;

        var markerRow = -1;
        for (var r = 0; r <= lastRow && markerRow < 0; r++)
        {
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var text = CellStr(table.Rows[r][c]);
                if (text.Contains("学生等级分布详情", StringComparison.Ordinal) ||
                    text.Contains("学生等级明细", StringComparison.Ordinal))
                {
                    markerRow = r;
                    break;
                }
            }
        }

        var subject = SubjectOf(table.TableName);
        if (markerRow < 0)
        {
            var single = BuildSheet(table, 0, lastRow, table.TableName, subject, null);
            if (single is not null) sheets.Add(single);
            return;
        }

        var upper = BuildSheet(table, 0, markerRow - 1, "等级分布", subject, null);
        if (upper is not null) sheets.Add(upper);

        var detailHeader = -1;
        for (var r = markerRow + 1; r <= Math.Min(markerRow + 6, lastRow) && detailHeader < 0; r++)
        {
            for (var c = 0; c < table.Columns.Count; c++)
            {
                if (CellStr(table.Rows[r][c]) == "姓名") { detailHeader = r; break; }
            }
        }

        if (detailHeader >= 0)
        {
            var detail = BuildSheet(table, markerRow, lastRow, "学生等级明细", subject, detailHeader);
            if (detail is not null) sheets.Add(detail);
        }
    }

    /// <summary>按给定行范围读取一张工作表；表头行可由调用方强制指定。</summary>
    private static OverviewSheet? BuildSheet(
        DataTable table, int startRow, int endRow, string name, string? subject, int? forcedHeader)
    {
        if (endRow < startRow) return null;

        var lastRow = Math.Min(endRow, table.Rows.Count - 1);
        if (lastRow < startRow) return null;

        var maxCol = FindMaxColumn(table, startRow, lastRow);
        if (maxCol < 0) return null;

        // 跳过完全空白的首行（部分导出会在表头上方留一行空行）
        while (startRow <= lastRow && CountNonEmpty(table, startRow, maxCol) == 0) startRow++;
        if (startRow > lastRow) return null;

        int headerRow;
        int groupRow;
        if (forcedHeader.HasValue)
        {
            headerRow = Math.Min(Math.Max(forcedHeader.Value, startRow), lastRow);
            groupRow = headerRow - 1 >= startRow && IsGroupHeaderRow(table, headerRow - 1, maxCol)
                ? headerRow - 1
                : -1;
        }
        else
        {
            (headerRow, groupRow) = FindHeaderLayout(table, startRow, lastRow, maxCol);
        }

        var headerTexts = BuildHeaderTexts(table, headerRow, groupRow, maxCol);

        var rows = new List<IReadOnlyList<string>>();
        for (var r = headerRow + 1; r <= lastRow; r++)
        {
            var firstCell = CellStr(table.Rows[r][0]);
            var secondCell = maxCol >= 1 ? CellStr(table.Rows[r][1]) : string.Empty;

            // 客观题填涂表在表头下会有一行「客观题答案」，它不是学生记录
            if (firstCell.Contains("答案", StringComparison.Ordinal) &&
                (secondCell.Length == 0 || secondCell.Contains("答案", StringComparison.Ordinal)))
                continue;

            var values = new string[maxCol + 1];
            var hasContent = false;
            for (var c = 0; c <= maxCol; c++)
            {
                var value = CellStr(table.Rows[r][c]);
                values[c] = value;
                if (value.Length > 0) hasContent = true;
            }

            if (hasContent) rows.Add(values);
        }

        if (rows.Count == 0) return null;

        // 学科/科目/类别常按组合并，只在组首行有值；按班级筛选后组首行可能被裁掉，
        // 先把这些分组标签向下补齐，保证筛选后的每一行仍能看出属于哪个学科。
        FillDownGroupLabels(rows, headerTexts);

        var columns = new List<OverviewColumn>(maxCol + 1);
        for (var c = 0; c <= maxCol; c++)
        {
            // 源表首列常因合并单元格留空，按该列数据推断一个可读表头，避免界面出现「列1」
            var header = headerTexts[c] == $"列{c + 1}"
                ? InferHeaderFromValues(rows, c)
                : headerTexts[c];
            columns.Add(new OverviewColumn { Header = header, IsNumeric = IsNumericColumn(rows, c) });
        }

        return new OverviewSheet
        {
            Name = string.IsNullOrWhiteSpace(name) ? "工作表" : name,
            Subject = subject,
            Columns = columns,
            Rows = rows,
        };
    }

    /// <summary>把「学科 / 科目 / 类别」这类分组标签向下补齐（空单元格沿用上一行）。</summary>
    private static void FillDownGroupLabels(List<IReadOnlyList<string>> rows, string[] headers)
    {
        var targets = new List<int>();
        for (var c = 0; c < headers.Length; c++)
        {
            var header = headers[c].Replace(" ", string.Empty);
            if (header is "学科" or "科目" or "类别") targets.Add(c);
        }
        if (targets.Count == 0) return;

        var last = new string[targets.Count];
        foreach (var row in rows)
        {
            if (row is not string[] cells) continue;
            for (var i = 0; i < targets.Count; i++)
            {
                var c = targets[i];
                if (c >= cells.Length) continue;
                if (cells[c].Length == 0)
                    cells[c] = last[i];
                else
                    last[i] = cells[c];
            }
        }
    }

    /// <summary>找表头行与分组表头行（分组表头在表头上一行，可能是合并单元格）。</summary>
    private static (int HeaderRow, int GroupRow) FindHeaderLayout(
        DataTable table, int startRow, int endRow, int maxCol)
    {
        var searchEnd = Math.Min(endRow, startRow + 7);

        // 0. 子表头法：一行全部由「得分 / 教师 / 人数 / 占比%」这类短词组成时，它就是列标题行，
        //    上一行是「数学 / [800,850] / 年级」这类分组表头（合并单元格只在首列有值）。
        //    这类表头往往命中不到两个关键词（如「得分/教师/排名」），必须先于关键词法判定。
        for (var r = startRow; r <= searchEnd; r++)
        {
            if (!IsSubHeaderRow(table, r, maxCol)) continue;

            var groupHint = r - 1 >= startRow && IsGroupHeaderRow(table, r - 1, maxCol) ? r - 1 : -1;
            return (r, groupHint);
        }

        // 1. 关键词法：非空单元格最多、且命中至少两个表头关键词的行
        var candidate = -1;
        var candidateCount = 1;
        for (var r = startRow; r <= searchEnd; r++)
        {
            if (CountNonEmpty(table, r, maxCol) < 2) continue;
            if (CountKeywords(table, r, maxCol) < 2) continue;

            var count = CountNonEmpty(table, r, maxCol);
            if (count > candidateCount)
            {
                candidate = r;
                candidateCount = count;
            }
        }

        if (candidate >= 0)
        {
            var header = candidate;
            var group = -1;
            if (header < endRow && IsSubHeaderRow(table, header + 1, maxCol))
            {
                // 选中的其实是分组表头，真正的列标题在下一行
                group = header;
                header++;
            }
            else if (header - 1 >= startRow && IsGroupHeaderRow(table, header - 1, maxCol))
            {
                group = header - 1;
            }
            return (header, group);
        }

        // 2. 数据行回推法：第一行明显是数据时，上一行即表头
        var dataRow = FindFirstDataRow(table, startRow, searchEnd, maxCol);
        if (dataRow > startRow)
        {
            var header = dataRow - 1;
            var group = header - 1 >= startRow && IsGroupHeaderRow(table, header - 1, maxCol)
                ? header - 1
                : -1;
            return (header, group);
        }

        // 3. 兜底：取前 8 行里非空最多的行，避免无关键词的名单/填涂表整张丢弃
        var best = -1;
        var bestCount = 1;
        for (var r = startRow; r <= searchEnd; r++)
        {
            var count = CountNonEmpty(table, r, maxCol);
            if (count > bestCount)
            {
                best = r;
                bestCount = count;
            }
        }

        if (best < 0) return (startRow, -1);

        var fallbackHeader = best;
        var fallbackGroup = -1;
        if (fallbackHeader < endRow && IsSubHeaderRow(table, fallbackHeader + 1, maxCol))
        {
            fallbackGroup = fallbackHeader;
            fallbackHeader++;
        }
        else if (fallbackHeader - 1 >= startRow && IsGroupHeaderRow(table, fallbackHeader - 1, maxCol))
        {
            fallbackGroup = fallbackHeader - 1;
        }

        return (fallbackHeader, fallbackGroup);
    }

    /// <summary>组装列名：上一行分组名 + 当前行子列名，如「数学·得分」。</summary>
    private static string[] BuildHeaderTexts(DataTable table, int headerRow, int groupRow, int maxCol)
    {
        var texts = new string[maxCol + 1];
        var groupCells = new string[maxCol + 1];

        if (groupRow >= 0)
        {
            // 合并单元格只在首列有值，按列向左填充
            var current = string.Empty;
            for (var c = 0; c <= maxCol; c++)
            {
                var raw = CellStr(table.Rows[groupRow][c]);
                if (raw.Length > 0) current = raw;
                groupCells[c] = current;
            }
        }

        for (var c = 0; c <= maxCol; c++)
        {
            var sub = CellStr(table.Rows[headerRow][c]);
            var group = groupRow >= 0 ? groupCells[c] : string.Empty;

            texts[c] = sub.Length == 0
                ? group.Length > 0 ? group : $"列{c + 1}"
                : group.Length > 0 && group != sub ? $"{group}·{sub}" : sub;
        }

        return texts;
    }

    private static bool IsSubHeaderRow(DataTable table, int row, int maxCol)
    {
        var count = 0;
        for (var c = 0; c <= maxCol; c++)
        {
            var value = CellStr(table.Rows[row][c]);
            if (value.Length == 0) continue;

            count++;
            var token = value.Replace(" ", string.Empty).Replace("　", string.Empty);
            if (!SubHeaderTokens.Contains(token, StringComparer.Ordinal)) return false;
        }

        return count >= 2;
    }

    private static bool IsGroupHeaderRow(DataTable table, int row, int maxCol) =>
        CountNonEmpty(table, row, maxCol) >= 2;

    private static int CountNonEmpty(DataTable table, int row, int maxCol)
    {
        var count = 0;
        for (var c = 0; c <= maxCol; c++)
            if (CellStr(table.Rows[row][c]).Length > 0) count++;
        return count;
    }

    private static int CountKeywords(DataTable table, int row, int maxCol)
    {
        var count = 0;
        for (var c = 0; c <= maxCol; c++)
        {
            var value = CellStr(table.Rows[row][c]);
            if (value.Length == 0) continue;
            if (HeaderKeywords.Any(k => value.Contains(k, StringComparison.Ordinal))) count++;
        }
        return count;
    }

    private static int FindFirstDataRow(DataTable table, int startRow, int endRow, int maxCol)
    {
        for (var r = startRow; r <= endRow; r++)
        {
            // 含「姓名 / 题号 / 准考证号」等锚点的行是表头，不是数据
            if (LooksLikeHeaderRow(table, r, maxCol)) continue;

            var nonEmpty = 0;
            var numeric = 0;
            for (var c = 0; c <= maxCol; c++)
            {
                var value = CellStr(table.Rows[r][c]);
                if (value.Length == 0) continue;
                nonEmpty++;
                if (IsNumericLike(value)) numeric++;
            }

            if (nonEmpty >= 2 && numeric >= 3 && numeric * 10 >= nonEmpty * 3) return r;
        }

        return -1;
    }

    private static readonly string[] HeaderAnchors =
    {
        "姓名", "题号", "准考证号", "班级", "学科", "科目", "类别", "序号", "题型",
    };

    private static bool LooksLikeHeaderRow(DataTable table, int row, int maxCol)
    {
        for (var c = 0; c <= maxCol; c++)
        {
            var value = CellStr(table.Rows[row][c]);
            if (value.Length == 0) continue;
            if (HeaderAnchors.Contains(value, StringComparer.Ordinal)) return true;
        }

        return false;
    }

    private static int FindMaxColumn(DataTable table, int startRow, int endRow)
    {
        var maxCol = -1;
        for (var r = startRow; r <= endRow; r++)
        {
            for (var c = 0; c < table.Columns.Count; c++)
            {
                if (c <= maxCol) continue;
                if (CellStr(table.Rows[r][c]).Length > 0) maxCol = c;
            }
        }
        return maxCol;
    }

    private static int FindLastContentRow(DataTable table, int startRow)
    {
        for (var r = table.Rows.Count - 1; r >= startRow; r--)
        {
            for (var c = 0; c < table.Columns.Count; c++)
                if (CellStr(table.Rows[r][c]).Length > 0) return r;
        }
        return startRow - 1;
    }

    private static bool IsNumericColumn(IReadOnlyList<IReadOnlyList<string>> rows, int column)
    {
        var values = new List<string>();
        foreach (var row in rows)
        {
            if (column >= row.Count) continue;
            var value = row[column];
            if (value.Length == 0 || IsPlaceholder(value)) continue;
            values.Add(value);
        }

        if (values.Count == 0) return false;

        var numeric = values.Count(IsNumericLike);
        return numeric * 10 >= values.Count * 6;
    }

    private static bool IsNumericLike(string value)
    {
        var text = value.Trim().TrimEnd('%', '％').Trim();
        if (text.Length == 0) return false;
        return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>空表头按列内容推断名称：班级 / 学科 / 姓名，无法判断时用「项目」。</summary>
    private static string InferHeaderFromValues(IReadOnlyList<IReadOnlyList<string>> rows, int column)
    {
        var values = new List<string>();
        foreach (var row in rows)
        {
            if (column >= row.Count) continue;
            var value = row[column];
            if (value.Length == 0 || IsPlaceholder(value)) continue;
            if (!values.Contains(value, StringComparer.Ordinal)) values.Add(value);
        }

        if (values.Count == 0) return "项目";

        if (values.Any(v =>
                v == "全部班级" ||
                v == "合计" ||
                v.Contains("年级", StringComparison.Ordinal) ||
                v.Contains("班", StringComparison.Ordinal)))
        {
            return "班级";
        }

        if (values.Any(v => v is "科目" or "学科")) return "科目";
        if (values.All(v => SubjectSheetNames.Contains(v, StringComparer.Ordinal))) return "学科";
        if (values.All(IsPersonName)) return "姓名";

        return "项目";
    }

    /// <summary>2–4 个纯汉字视为人名（排除常见表头词）。</summary>
    private static bool IsPersonName(string value)
    {
        if (value is "姓名" or "科目" or "学科" or "班级" or "题号" or "序号" or "题型" or "项目" or "备注" or "类别" or "得分" or "总分")
            return false;
        if (value.Length is < 2 or > 4) return false;
        foreach (var ch in value)
            if (ch < '\u4e00' || ch > '\u9fa5') return false;
        return true;
    }

    private static bool IsPlaceholder(string value) =>
        value is "-" or "—" or "–" or "/" or "／";

    private static string? SubjectOf(string sheetName)
    {
        var name = sheetName.Replace(" ", string.Empty).Replace("　", string.Empty).Trim();
        return SubjectSheetNames.Contains(name, StringComparer.Ordinal) ? name : null;
    }

    // ---------------------------------------------------------------- 标题与工具

    private static string ReadTitle(DataTable table)
    {
        if (table.Rows.Count == 0) return string.Empty;

        var first = CellStr(table.Rows[0][0]);
        if (first.Length == 0)
        {
            for (var c = 1; c < table.Columns.Count; c++)
            {
                first = CellStr(table.Rows[0][c]);
                if (first.Length > 0) break;
            }
        }

        return first.Trim().Trim('【', '】', ' ', '　');
    }

    private static string? ExtractExamName(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        // 「【…段考】所有班级…」先按「】」截断，避免把报表类型带进考试名
        var bracketEnd = title.IndexOf('】');
        var basis = bracketEnd > 0 ? title[..bracketEnd] : title;

        var match = Regex.Match(basis, @"\d{4}\s*[-－]\s*\d{4}\s*学年.*$");
        var examName = (match.Success ? match.Value : basis).Trim();
        return examName.Length == 0 ? null : examName;
    }

    private static string Normalize(string path) =>
        path.Normalize(System.Text.NormalizationForm.FormC);

    private static bool IsSupportedFile(string path)
    {
        if (!File.Exists(path)) return false;
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".xls", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".xlsm", StringComparison.OrdinalIgnoreCase);
    }

    private static string CellStr(object? cell)
    {
        if (cell == null || cell == DBNull.Value) return string.Empty;
        if (cell is double d) return d.ToString("0.##########", CultureInfo.InvariantCulture);
        if (cell is float f) return f.ToString("0.##########", CultureInfo.InvariantCulture);
        if (cell is decimal m) return m.ToString("0.##########", CultureInfo.InvariantCulture);
        if (cell is DateTime dt) return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return cell.ToString()?.Trim() ?? string.Empty;
    }
}
