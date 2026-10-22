using GradeStrip.Core.Comparison;
using GradeStrip.App.Controls;
using GradeStrip.Core.Analysis;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GradeStrip.Core.Export;
using GradeStrip.Core.Import;
using GradeStrip.Core.Layout;
using GradeStrip.Core.Model;

namespace GradeStrip.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private ExamData? _data;
    private GradeInfo _gradeInfo = GradeDetector.Detect();

    [ObservableProperty]
    public partial string SourceFileName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SourcePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClassName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ExamName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int StudentCount { get; set; }

    [ObservableProperty]
    public partial int SubjectCount { get; set; }

    [ObservableProperty]
    public partial bool HasData { get; set; }

    [ObservableProperty]
    public partial int Section { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "等待导入成绩总表";

    [ObservableProperty]
    public partial IBrush StatusBrush { get; set; } = Palette.Muted;
    private readonly List<ExamSession> _sessions = new();
    public ObservableCollection<ExamSessionItem> ExamSessions { get; } = new();

    [ObservableProperty]
    public partial int ActiveExamIndex { get; set; } = 0;

    [ObservableProperty]
    public partial bool IsExamManagerOpen { get; set; } = false;

    [ObservableProperty]
    public partial ComparisonBaseline ComparisonMode { get; set; } = ComparisonBaseline.PreviousExam;

    [ObservableProperty]
    public partial int StudentTrendChartMode { get; set; } = 0; // 0: 总分, 1: 班排, 2: 校排

    public int ExamSessionCount => _sessions.Count;
    public string ExamSessionCountText => $"{_sessions.Count} 场";
    public bool HasMultipleExams => _sessions.Count >= 2;
    public bool HasThreeOrMoreExams => _sessions.Count >= 3;
    public bool HasActiveExamPrevious => ActiveExamIndex > 0;

    public bool IsComparisonPrevious => ComparisonMode == ComparisonBaseline.PreviousExam;
    public bool IsComparisonFirst => ComparisonMode == ComparisonBaseline.FirstExam;
    public bool IsComparisonOriginal => ComparisonMode == ComparisonBaseline.OriginalFile;

    public bool IsTrendScoreActive => StudentTrendChartMode == 0;
    public bool IsTrendClassRankActive => StudentTrendChartMode == 1;
    public bool IsTrendSchoolRankActive => StudentTrendChartMode == 2;

    public IReadOnlyList<ChartPoint>? ActiveStudentTrendPoints => Detail?.MultiExamProfile switch
    {
        null => null,
        var prof => StudentTrendChartMode switch
        {
            1 => prof.ClassRankPoints,
            2 => prof.SchoolRankPoints,
            _ => prof.ScorePoints
        }
    };

    public TrendChartMode ActiveStudentChartMode => StudentTrendChartMode switch
    {
        1 => TrendChartMode.ClassRank,
        2 => TrendChartMode.SchoolRank,
        _ => TrendChartMode.Score
    };


    [ObservableProperty]
    public partial decimal FirstGroupSize { get; set; } = 7;

    [ObservableProperty]
    public partial decimal GroupSize { get; set; } = 8;

    [ObservableProperty]
    public partial string TitleOverride { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IncludeLegend { get; set; } = true;

    /// <summary>是否导出各科与总分的校排（默认 false 导出班排）。</summary>
    [ObservableProperty]
    public partial bool UseSchoolRank { get; set; } = false;

    /// <summary>总分统计是否剔除缺考学生。点击「总分均分」卡片切换。</summary>
    [ObservableProperty]
    public partial bool ExcludeAbsent { get; set; } = true;

    [ObservableProperty]
    public partial string SheetSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ImportNotice { get; set; }

    public bool HasImportNotice => !string.IsNullOrWhiteSpace(ImportNotice);

    partial void OnImportNoticeChanged(string? value)
    {
        OnPropertyChanged(nameof(HasImportNotice));
    }

    public void DismissNotice()
    {
        ImportNotice = null;
    }

    public ObservableCollection<KpiCard> Kpis { get; } = new();
    public ObservableCollection<SubjectStat> SubjectStats { get; } = new();
    public ObservableCollection<TierStat> TierStats { get; } = new();
    public ObservableCollection<TrendStat> TrendStats { get; } = new();
    public ObservableCollection<StudentRow> Students { get; } = new();
    public ObservableCollection<PreviewItem> StripItems { get; } = new();

    [ObservableProperty]
    public partial StudentDetail? Detail { get; set; }

    [ObservableProperty]
    public partial SubjectDetail? SubjectDetail { get; set; }

    public bool IsEmptyVisible => !HasData;
    public bool IsOverviewVisible => HasData && Section == 0;
    public bool IsStripsVisible => HasData && Section == 1;
    public bool IsDetailVisible => HasData && Section == 2 && Detail is not null;
    public bool IsSubjectDetailVisible => HasData && Section == 3 && SubjectDetail is not null;
    public bool IsOverviewNavActive => Section is 0 or 2 or 3;
    public bool CanExport => HasData;
    public bool HasDataMeta => HasData;
    public string HeaderTitle => HasData ? ExamName : "分晓 · 成绩条生成器";

    public string HeaderEyebrow => Section switch
    {
        1 => "STRIPS / 成绩条",
        2 => "STUDENT / 学生画像",
        3 => "SUBJECT / 学科分析",
        _ => "OVERVIEW / 数据概览",
    };

    partial void OnHasDataChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(IsOverviewVisible));
        OnPropertyChanged(nameof(IsStripsVisible));
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(HasDataMeta));
        OnPropertyChanged(nameof(HeaderTitle));
    }

    partial void OnSectionChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewVisible));
        OnPropertyChanged(nameof(IsStripsVisible));
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(IsSubjectDetailVisible));
        OnPropertyChanged(nameof(IsOverviewNavActive));
        OnPropertyChanged(nameof(HeaderEyebrow));
    }

    partial void OnDetailChanged(StudentDetail? value)
    {
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(ActiveStudentTrendPoints));
        OnPropertyChanged(nameof(ActiveStudentChartMode));
    }
    partial void OnSubjectDetailChanged(SubjectDetail? value) => OnPropertyChanged(nameof(IsSubjectDetailVisible));

    /// <summary>打开某个单科的画像分析页。</summary>
    public void OpenSubject(string subjectName)
    {
        if (_data is null || !_data.Subjects.Contains(subjectName)) return;

        SubjectDetail = BuildSubjectDetail(_data, subjectName, _gradeInfo);
        Section = 3;
        Status = $"正在查看【{subjectName}】的学科表现与学生排名";
        StatusBrush = Palette.Muted;
    }

    /// <summary>根据学生姓名打开其画像页（用于单科排名向学生画像穿透跳转）。</summary>
    public void OpenStudentByName(string studentName)
    {
        if (_data is null) return;
        var student = _data.Students.FirstOrDefault(s => s.Name == studentName);
        if (student is null) return;

        Detail = BuildDetail(_data, student, _gradeInfo, _sessions);
        Section = 2;
        Status = $"正在查看 {student.Name} 的成绩画像";
        StatusBrush = Palette.Muted;
    }

    public void CloseSubjectDetail()
    {
        SubjectDetail = null;
        Section = 0;
        Status = $"已读取 {StudentCount} 名学生 · {SubjectCount} 个科目";
        StatusBrush = Palette.Moss;
    }

    /// <summary>打开某个学生的画像页。</summary>
    public void OpenStudent(StudentRow row)
    {
        if (_data is null) return;

        var student = _data.Students.FirstOrDefault(s => s.No == row.No);
        if (student is null) return;

        Detail = BuildDetail(_data, student, _gradeInfo, _sessions);
        Section = 2;
        Status = $"正在查看 {student.Name} 的成绩画像";
        StatusBrush = Palette.Muted;
    }

    public void CloseDetail()
    {
        Detail = null;
        Section = 0;
        Status = $"已读取 {StudentCount} 名学生 · {SubjectCount} 个科目";
        StatusBrush = Palette.Moss;
    }

    partial void OnExamNameChanged(string value) => OnPropertyChanged(nameof(HeaderTitle));

    partial void OnFirstGroupSizeChanged(decimal value) => RefreshStrips();

    partial void OnGroupSizeChanged(decimal value) => RefreshStrips();

    partial void OnIncludeLegendChanged(bool value) => RefreshStrips();

    partial void OnUseSchoolRankChanged(bool value) => RefreshStrips();
    partial void OnComparisonModeChanged(ComparisonBaseline value)
    {
        OnPropertyChanged(nameof(IsComparisonPrevious));
        OnPropertyChanged(nameof(IsComparisonFirst));
        OnPropertyChanged(nameof(IsComparisonOriginal));
        ApplyActiveExamAndComparison();
    }

    partial void OnStudentTrendChartModeChanged(int value)
    {
        OnPropertyChanged(nameof(ActiveStudentTrendPoints));
        OnPropertyChanged(nameof(ActiveStudentChartMode));
        OnPropertyChanged(nameof(IsTrendScoreActive));
        OnPropertyChanged(nameof(IsTrendClassRankActive));
        OnPropertyChanged(nameof(IsTrendSchoolRankActive));
    }


    partial void OnExcludeAbsentChanged(bool value) => RefreshOverview();

    private void RefreshOverview()
    {
        if (_data is not null) BuildOverview(_data);
    }

    private void RefreshStrips()
    {
        if (_data is not null) BuildStrips(_data);
    }


    public void OpenExamManager() => IsExamManagerOpen = true;
    public void CloseExamManager() => IsExamManagerOpen = false;

    public void AddSession(string path)
    {
        try
        {
            var data = GradeSheetImporter.Load(path);
            var order = _sessions.Count + 1;
            var session = new ExamSession
            {
                Order = order,
                Label = $"第 {order} 次考试",
                SourcePath = path,
                SourceFileName = Path.GetFileName(path),
                Data = data
            };
            _sessions.Add(session);
            SyncExamSessions();

            if (_sessions.Count == 1)
            {
                ActiveExamIndex = 0;
            }
            ApplyActiveExamAndComparison();

            Status = $"已导入【第 {order} 次考试】· 共 {_sessions.Count} 场考试";
            StatusBrush = Palette.Moss;
        }
        catch (Exception ex)
        {
            Status = FriendlyError(ex, "导入考试失败");
            StatusBrush = Palette.Seal;
        }
    }

    public void ReplaceSession(int order, string path)
    {
        var idx = _sessions.FindIndex(s => s.Order == order);
        if (idx < 0) return;

        try
        {
            var data = GradeSheetImporter.Load(path);
            _sessions[idx] = new ExamSession
            {
                Order = order,
                Label = $"第 {order} 次考试",
                SourcePath = path,
                SourceFileName = Path.GetFileName(path),
                Data = data
            };
            SyncExamSessions();
            ApplyActiveExamAndComparison();

            Status = $"已替换【第 {order} 次考试】数据";
            StatusBrush = Palette.Moss;
        }
        catch (Exception ex)
        {
            Status = FriendlyError(ex, "替换考试失败");
            StatusBrush = Palette.Seal;
        }
    }

    public void RemoveSession(int order)
    {
        var idx = _sessions.FindIndex(s => s.Order == order);
        if (idx < 0) return;

        _sessions.RemoveAt(idx);
        for (var i = 0; i < _sessions.Count; i++)
        {
            var s = _sessions[i];
            _sessions[i] = new ExamSession
            {
                Order = i + 1,
                Label = $"第 {i + 1} 次考试",
                SourcePath = s.SourcePath,
                SourceFileName = s.SourceFileName,
                Data = s.Data
            };
        }

        if (ActiveExamIndex >= _sessions.Count)
        {
            ActiveExamIndex = Math.Max(0, _sessions.Count - 1);
        }

        SyncExamSessions();
        ApplyActiveExamAndComparison();

        Status = $"已移除考试 · 剩余 {_sessions.Count} 场考试";
        StatusBrush = Palette.Muted;
    }

    public void MoveSessionUp(int order)
    {
        var idx = _sessions.FindIndex(s => s.Order == order);
        if (idx <= 0) return;

        var temp = _sessions[idx];
        _sessions[idx] = _sessions[idx - 1];
        _sessions[idx - 1] = temp;

        ReorderSessions();
    }

    public void MoveSessionDown(int order)
    {
        var idx = _sessions.FindIndex(s => s.Order == order);
        if (idx < 0 || idx >= _sessions.Count - 1) return;

        var temp = _sessions[idx];
        _sessions[idx] = _sessions[idx + 1];
        _sessions[idx + 1] = temp;

        ReorderSessions();
    }

    private void ReorderSessions()
    {
        for (var i = 0; i < _sessions.Count; i++)
        {
            var s = _sessions[i];
            _sessions[i] = new ExamSession
            {
                Order = i + 1,
                Label = $"第 {i + 1} 次考试",
                SourcePath = s.SourcePath,
                SourceFileName = s.SourceFileName,
                Data = s.Data
            };
        }
        SyncExamSessions();
        ApplyActiveExamAndComparison();
    }

    public void SetActiveSession(int order)
    {
        var idx = _sessions.FindIndex(s => s.Order == order);
        if (idx >= 0 && idx < _sessions.Count)
        {
            ActiveExamIndex = idx;
            SyncExamSessions();
            ApplyActiveExamAndComparison();
        }
    }

    private void SyncExamSessions()
    {
        ExamSessions.Clear();
        for (var i = 0; i < _sessions.Count; i++)
        {
            var s = _sessions[i];
            ExamSessions.Add(new ExamSessionItem
            {
                Order = s.Order,
                ExamName = s.DisplayName,
                FileName = s.SourceFileName,
                FilePath = s.SourcePath,
                SummaryText = $"{s.Data.Students.Count} 人 · {s.Data.Subjects.Count} 科",
                IsActive = i == ActiveExamIndex,
                CanMoveUp = i > 0,
                CanMoveDown = i < _sessions.Count - 1
            });
        }

        OnPropertyChanged(nameof(ExamSessionCount));
        OnPropertyChanged(nameof(ExamSessionCountText));
        OnPropertyChanged(nameof(HasMultipleExams));
        OnPropertyChanged(nameof(HasThreeOrMoreExams));
        OnPropertyChanged(nameof(HasActiveExamPrevious));
    }

    private void ApplyActiveExamAndComparison()
    {
        if (_sessions.Count == 0)
        {
            _data = null;
            HasData = false;
            return;
        }

        if (ActiveExamIndex < 0 || ActiveExamIndex >= _sessions.Count)
        {
            ActiveExamIndex = 0;
        }

        var activeSession = _sessions[ActiveExamIndex];
        ExamData? baseline = null;

        if (ComparisonMode == ComparisonBaseline.PreviousExam && ActiveExamIndex > 0)
        {
            baseline = _sessions[ActiveExamIndex - 1].Data;
        }
        else if (ComparisonMode == ComparisonBaseline.FirstExam && ActiveExamIndex > 0)
        {
            baseline = _sessions[0].Data;
        }

        var displayData = ExamComparisonEngine.CreateComparedExam(activeSession.Data, baseline);
        _data = displayData;
        _gradeInfo = GradeDetector.Detect(displayData.ExamName, displayData.ClassName, displayData.SourceTitle, activeSession.SourceFileName);

        SourcePath = activeSession.SourcePath;
        SourceFileName = activeSession.SourceFileName;
        ClassName = string.IsNullOrWhiteSpace(displayData.ClassName) ? "未识别班级" : displayData.ClassName;
        ExamName = string.IsNullOrWhiteSpace(displayData.ExamName) ? activeSession.DisplayName : displayData.ExamName;
        StudentCount = displayData.Students.Count;
        SubjectCount = displayData.Subjects.Count;
        TitleOverride = displayData.DefaultTitle;
        ImportNotice = displayData.ImportNotice;
        if (!string.IsNullOrWhiteSpace(displayData.ImportNotice))
        {
            Status = displayData.ImportNotice;
            StatusBrush = Palette.Clay;
        }

        HasData = true;
        BuildOverview(displayData);
        BuildStrips(displayData);

        if (Detail is not null)
        {
            var student = displayData.Students.FirstOrDefault(s => s.No == Detail.No);
            if (student is not null)
            {
                Detail = BuildDetail(displayData, student, _gradeInfo, _sessions);
            }
        }
    }

    public void Load(string path)
    {
        AddSession(path);
    }

    public void Export(string path)
    {
        if (_data is null) return;

        try
        {
            string? compNote = null;
            if (ComparisonMode == ComparisonBaseline.PreviousExam && ActiveExamIndex > 0)
                compNote = $"对比第 {ActiveExamIndex} 次考试";
            else if (ComparisonMode == ComparisonBaseline.FirstExam && ActiveExamIndex > 0)
                compNote = "对比第 1 次基准考";
            else if (ComparisonMode == ComparisonBaseline.OriginalFile)
                compNote = "表格自带";

            var result = ScoreStripExporter.Export(_data, path, new StripExportOptions
            {
                FirstGroupSize = Math.Max(1, (int)FirstGroupSize),
                GroupSize = Math.Max(1, (int)GroupSize),
                IncludeLegend = IncludeLegend,
                UseSchoolRank = UseSchoolRank,
                ComparisonNote = compNote,
                Title = string.IsNullOrWhiteSpace(TitleOverride) ? null : TitleOverride.Trim(),
            });

            Status = $"已导出 {result.StudentCount} 人 · {result.Sheets.Count} 张表 → {Path.GetFileName(path)}";
            StatusBrush = Palette.Moss;
        }
        catch (Exception ex)
        {
            Status = FriendlyError(ex, "导出失败");
            StatusBrush = Palette.Seal;
        }
    }

    /// <summary>把底层异常翻译成中文提示，避免状态栏出现英文堆栈信息。</summary>
    private static string FriendlyError(Exception ex, string prefix)
    {
        if (ex.InnerException is not null && ex is not IOException and not UnauthorizedAccessException
            and not InvalidDataException and not InvalidOperationException)
            return FriendlyError(ex.InnerException, prefix);

        var text = ex switch
        {
            FileNotFoundException or DirectoryNotFoundException => "找不到文件，可能已被移动或删除",
            UnauthorizedAccessException => "没有权限访问该文件，请检查文件属性或换一个位置",
            IOException => $"{prefix}：文件正被其他程序占用（通常是已在 Excel 中打开），请关闭后重试",
            InvalidDataException or InvalidOperationException => ex.Message,
            _ => $"{prefix}：{ex.Message}",
        };

        // 底层库（如解压失败）会抛出英文提示，统一兜底成中文
        return text.Any(ch => ch >= '\u4e00' && ch <= '\u9fff')
            ? text
            : $"{prefix}：无法处理该文件，请确认它是有效的 Excel 成绩表";
    }

    public string SuggestedFileName()
    {
        var name = $"{ClassName}-{ExamName}-成绩条".Replace("/", "-").Replace("\\", "-");
        return string.IsNullOrWhiteSpace(name) ? "成绩条.xlsx" : $"{name}.xlsx";
    }

    private void BuildOverview(ExamData data)
    {
        Kpis.Clear();
        SubjectStats.Clear();
        TierStats.Clear();
        TrendStats.Clear();
        Students.Clear();

        // 缺考口径：剔除时只统计有总分的学生，含入时把缺考按 0 分计
        var totals = ExcludeAbsent
            ? data.Students.Where(s => s.Total.Score.HasValue).Select(s => s.Total.Score!.Value).ToList()
            : data.Students.Select(s => s.Total.Score ?? 0).ToList();
        var average = totals.Count > 0 ? totals.Average() : 0;
        var absentCount = data.Students.Count(s => !s.Total.Score.HasValue);

        Kpis.Add(new KpiCard
        {
            Label = "学生人数",
            Value = data.Students.Count.ToString(CultureInfo.InvariantCulture),
            Unit = "人",
            Note = $"{data.Subjects.Count} 个科目",
            Accent = Palette.Ink,
        });
        Kpis.Add(new KpiCard
        {
            Label = "总分均分",
            Value = Fmt(average),
            Unit = "分",
            Note = ExcludeAbsent
                ? (absentCount > 0 ? $"剔除 {absentCount} 名缺考 · 点击切换" : "全部学生均有总分")
                : "缺考按 0 分计 · 点击切换",
            Accent = Palette.Ink,
        });
        Kpis.Add(new KpiCard
        {
            Label = "总分最高",
            Value = totals.Count > 0 ? Fmt(totals.Max()) : "—",
            Unit = "分",
            Note = TopStudentName(data, true),
            Accent = Palette.Moss,
        });
        Kpis.Add(new KpiCard
        {
            Label = "总分最低",
            Value = totals.Count > 0 ? Fmt(totals.Min()) : "—",
            Unit = "分",
            Note = !ExcludeAbsent && absentCount > 0
                ? $"{absentCount} 名缺考按 0 分计"
                : TopStudentName(data, false),
            Accent = Palette.Seal,
        });

        // 各科满分不同，直接比较平均分没有意义：
        // 先算「满分 − 班级均分」的差距，再按差距从小到大（即得分率从高到低）排序，
        // 条形长度表示得分率，右侧数字仍显示班级平均分。
        var subjectAverages = new List<(string Name, double Avg, double Max, double Min, int Full)>();
        foreach (var subject in data.Subjects)
        {
            var scores = data.Students
                .Select(s => s[subject].Score)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            if (scores.Count == 0) continue;
            var full = SubjectMarks.FullMark(subject, scores.Max(), _gradeInfo);
            subjectAverages.Add((subject, scores.Average(), scores.Max(), scores.Min(), full));
        }

        foreach (var s in subjectAverages
                     .OrderBy(s => s.Full - s.Avg)
                     .ThenByDescending(s => s.Avg / s.Full))
        {
            var rate = Math.Clamp(s.Avg / s.Full, 0, 1);
            var gap = s.Full - s.Avg;
            SubjectStats.Add(new SubjectStat
            {
                Name = s.Name,
                AverageText = Fmt(s.Avg),
                FullMarkText = $"/ {s.Full}",
                RangeText = $"满分 {s.Full} · 均分差距 {Fmt(gap)} 分 · 得分率 {rate * 100:0.0}% · " +
                            $"最高 {Fmt(s.Max)} / 最低 {Fmt(s.Min)}",
                FillWeight = Math.Max(0.03, rate),
                RestWeight = Math.Max(0, 1 - rate),
                Accent = Palette.Ink,
            });
        }

        var tiers = new (string Label, string Fill, int Min, int Max, IBrush Fg)[]
        {
            ("1-3 名", Strips.TierTop, 1, 3, Brushes.White),
            ("4-10 名", Strips.TierGood, 4, 10, Palette.Ink),
            ("11-20 名", Strips.TierUpper, 11, 20, Palette.Ink),
            ("21-35 名", Strips.TierMiddle, 21, 35, Palette.Ink),
            ("36 名以后", Strips.TierLow, 36, int.MaxValue, Palette.Ink),
        };

        var ranked = data.Students.Count(s => s.Total.ClassRank is > 0);
        foreach (var tier in tiers)
        {
            var count = data.Students.Count(s => s.Total.ClassRank is { } r && r >= tier.Min && r <= tier.Max);
            var ratio = ranked > 0 ? (double)count / ranked : 0;
            TierStats.Add(new TierStat
            {
                Label = tier.Label,
                CountText = count.ToString(CultureInfo.InvariantCulture),
                PercentText = ranked > 0 ? $"{ratio * 100:0}%" : "—",
                Weight = ratio,
                Fill = Brush(tier.Fill),
                Foreground = tier.Fg,
            });
        }

        var rise = data.Students.Count(s => s.Total.ClassDelta is > 0);
        var fall = data.Students.Count(s => s.Total.ClassDelta is < 0);
        var flat = data.Students.Count(s => s.Total.ClassDelta is 0);
        var unknown = data.Students.Count - rise - fall - flat;

        TrendStats.Add(new TrendStat
        {
            Label = "进步",
            CountText = rise.ToString(CultureInfo.InvariantCulture),
            Note = "名次上升",
            Accent = Brush(Strips.RiseText),
        });
        TrendStats.Add(new TrendStat
        {
            Label = "退步",
            CountText = fall.ToString(CultureInfo.InvariantCulture),
            Note = "名次下降",
            Accent = Brush(Strips.FallText),
        });
        TrendStats.Add(new TrendStat
        {
            Label = "持平 / 无对比",
            CountText = (flat + unknown).ToString(CultureInfo.InvariantCulture),
            Note = "含首次统考",
            Accent = Brush(Strips.FlatText),
        });

        var ordered = data.Students
            .OrderByDescending(s => s.Total.Score ?? double.MinValue)
            .ThenBy(s => s.Total.ClassRank ?? int.MaxValue)
            .ToList();

        var bestTotal = ordered.FirstOrDefault()?.Total.Score ?? 1;
        for (var i = 0; i < ordered.Count; i++)
        {
            var student = ordered[i];
            var delta = student.Total.ClassDelta;
            var score = student.Total.Score ?? 0;
            var ratio = score / Math.Max(1, bestTotal);
            Students.Add(new StudentRow
            {
                Index = (i + 1).ToString(CultureInfo.InvariantCulture),
                Name = student.Name,
                TotalText = Fmt(student.Total.Score),
                SchoolRankText = student.Total.SchoolRank?.ToString(CultureInfo.InvariantCulture) ?? "—",
                ClassRankText = student.Total.ClassRank?.ToString(CultureInfo.InvariantCulture) ?? "—",
                TrendText = Strips.DeltaText(delta),
                TrendBrush = Brush(Strips.DeltaColor(delta)),
                BarWeight = Math.Max(0.02, ratio),
                RestWeight = Math.Max(0, 1 - ratio),
                BarBrush = Brush(Strips.TierFill(student.Total.ClassRank)),
                No = student.No,
            });
        }

        SheetSummary = data.HasImportNotice
            ? $"{data.Students.Count} 人 · {data.Subjects.Count} 科 · 简表"
            : $"{data.Students.Count} 人 · {data.Subjects.Count} 科";
    }

    /// <summary>构建学生画像：个人得分率与班级均分刻度逐科对照。</summary>
    private static StudentDetail BuildDetail(ExamData data, StudentRecord student, GradeInfo? grade = null, IReadOnlyList<ExamSession>? sessions = null)
    {
        var rows = new List<DetailSubjectRow>();
        var above = 0;
        var below = 0;
        var totalFull = 0;
        var hasAnyScore = false;

        foreach (var subject in data.Subjects)
        {
            var scores = data.Students
                .Select(s => s[subject].Score)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            var full = SubjectMarks.FullMark(subject, scores.Count > 0 ? scores.Max() : null, grade);
            totalFull += full;

            var average = scores.Count > 0 ? scores.Average() : 0;
            var mine = student[subject];

            var rate = mine.Score is { } value ? Math.Clamp(value / full, 0, 1) : 0;
            var averageRate = Math.Clamp(average / full, 0, 1);
            var isAbove = mine.Score is { } sv && sv > average;
            if (mine.Score.HasValue)
            {
                hasAnyScore = true;
                if (isAbove) above++;
                else if (mine.Score.Value < average) below++;
            }

            rows.Add(new DetailSubjectRow
            {
                Name = subject,
                ScoreText = Fmt(mine.Score),
                FullText = $"/ {full}",
                RateText = mine.Score is null ? "—" : $"{rate * 100:0}%",
                FillWeight = rate,
                AverageWeight = averageRate,
                ClassRankText = mine.ClassRank?.ToString(CultureInfo.InvariantCulture) ?? "—",
                SchoolRankText = mine.SchoolRank?.ToString(CultureInfo.InvariantCulture) ?? "—",
                DeltaText = Strips.DeltaText(mine.ClassDelta),
                DeltaBrush = Brush(Strips.DeltaColor(mine.ClassDelta)),
                AboveAverage = isAbove,
                FillBrush = isAbove ? Palette.Ink : Palette.InkSoft,
            });
        }

        var totalRate = totalFull > 0 && student.Total.Score is { } total ? total / totalFull : 0;
        var rank = student.Total.ClassRank;
        var hasRank = rank is > 0;
        var position = hasRank
            ? Math.Clamp((double)(rank!.Value - 1) / Math.Max(1, data.Students.Count - 1), 0, 1)
            : 0;
        var positionNote = hasRank ? $"第 {rank!.Value} 名" : (hasAnyScore ? "暂无班级名次" : "缺考 · 暂无班级名次");

        var scored = rows.Where(r => r.ScoreText != "—").ToList();
        var strongest = scored.OrderByDescending(r => r.FillWeight).FirstOrDefault();
        var weakest = scored.OrderBy(r => r.FillWeight).FirstOrDefault();

        var delta = student.Total.ClassDelta;
        return new StudentDetail
        {
            No = student.No,
            NoText = student.No.ToString(CultureInfo.InvariantCulture),
            Name = student.Name,
            ExamNo = student.ExamNo,
            TotalText = Fmt(student.Total.Score),
            TotalRateText = hasAnyScore ? $"得分率 {totalRate * 100:0.0}%" : "无成绩",
            ClassRankText = rank?.ToString(CultureInfo.InvariantCulture) ?? "—",
            SchoolRankText = student.Total.SchoolRank?.ToString(CultureInfo.InvariantCulture) ?? "—",
            ClassRankNote = hasRank ? $"全班 {data.Students.Count} 人" : "无排名",
            SchoolRankNote = student.Total.SchoolRank is > 0 ? "年级排名" : "无排名",
            HasRank = hasRank,
            PositionNote = positionNote,
            PositionWeight = position,
            PositionRest = Math.Max(0, 1 - position),
            AboveText = $"{above} 科",
            BelowText = $"{below} 科",
            BelowBrush = below > 0 ? Palette.AccentDeep : Palette.Muted,
            StrongestText = strongest is null ? "—" : $"{strongest.Name} · {strongest.RateText}",
            WeakestText = weakest is null ? "—" : $"{weakest.Name} · {weakest.RateText}",
            TrendSummary = delta switch
            {
                null => "首次统考，无对比",
                > 0 => $"班级名次 ↑ {delta}",
                < 0 => $"班级名次 ↓ {-delta}",
                _ => "班级名次持平",
            },
            TrendBrush = Brush(Strips.DeltaColor(delta)),
            Subjects = rows,
            MultiExamProfile = BuildMultiExamProfile(student.Name, sessions),
        };
    }

    private void BuildStrips(ExamData data)
    {
        StripItems.Clear();

        var groups = StripGrouping.Split(data.Students, Math.Max(1, (int)FirstGroupSize), Math.Max(1, (int)GroupSize));
        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];

            StripItems.Add(new PreviewItem
            {
                IsHeader = true,
                HeaderText = $"第 {g + 1} 张表",
                HeaderMeta = $"序号 {group[0].No}–{group[^1].No} · {group.Count} 人" +
                             (g == 0 ? " · 含标题" : string.Empty) +
                             (g == 0 && IncludeLegend ? "与图例" : string.Empty),
            });

            if (g == 0)
            {
                StripItems.Add(new PreviewItem
                {
                    IsTitleBlock = true,
                    TitleText = string.IsNullOrWhiteSpace(TitleOverride) ? data.DefaultTitle : TitleOverride.Trim(),
                    HasLegend = IncludeLegend,
                    UseSchoolRank = UseSchoolRank,
                });
            }

            foreach (var student in group)
                StripItems.Add(new PreviewItem { Strip = StripRow.From(student, data.Subjects, UseSchoolRank) });
        }
    }

    private static string TopStudentName(ExamData data, bool highest)
    {
        var student = highest
            ? data.Students.OrderByDescending(s => s.Total.Score ?? double.MinValue).FirstOrDefault()
            : data.Students.Where(s => s.Total.Score.HasValue).OrderBy(s => s.Total.Score).FirstOrDefault();
        return student is null ? "—" : $"{student.Name} · 班次 {student.Total.ClassRank?.ToString() ?? "—"}";
    }

    private static string Fmt(double? value) =>
        value is null ? "—" : value.Value.ToString("0.##", CultureInfo.InvariantCulture);

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse("#" + hex));


    private static MultiExamProfile? BuildMultiExamProfile(string studentName, IReadOnlyList<ExamSession>? sessions)
    {
        if (sessions is null || sessions.Count < 3)
            return null;

        var traj = ExamComparisonEngine.BuildStudentTrajectory(studentName, sessions);
        var scorePoints = new List<ChartPoint>();
        var classRankPoints = new List<ChartPoint>();
        var schoolRankPoints = new List<ChartPoint>();

        for (var i = 0; i < traj.Points.Count; i++)
        {
            var p = traj.Points[i];
            var orderText = $"第 {p.ExamOrder} 次";
            var examName = p.ExamName;

            // 总分走势
            string scoreDeltaText = "—";
            IBrush? scoreDeltaBrush = Palette.Muted;
            if (i > 0 && p.TotalScore.HasValue && traj.Points[i - 1].TotalScore.HasValue)
            {
                var d = p.TotalScore.Value - traj.Points[i - 1].TotalScore!.Value;
                scoreDeltaText = d >= 0 ? $"+{d:0.#}" : $"{d:0.#}";
                scoreDeltaBrush = d >= 0 ? Palette.Moss : Palette.AccentDeep;
            }

            scorePoints.Add(new ChartPoint
            {
                ExamOrderText = orderText,
                ExamName = examName,
                Value = p.TotalScore ?? 0,
                ValueText = p.TotalScore is not null ? $"{p.TotalScore.Value:0.#}分" : "缺考",
                DeltaText = scoreDeltaText,
                DeltaBrush = scoreDeltaBrush,
                IsAbsent = p.IsAbsent || !p.TotalScore.HasValue
            });

            // 班排走势
            string crDeltaText = "—";
            IBrush? crDeltaBrush = Palette.Muted;
            if (i > 0 && p.ClassRank.HasValue && traj.Points[i - 1].ClassRank.HasValue)
            {
                var d = traj.Points[i - 1].ClassRank!.Value - p.ClassRank.Value;
                crDeltaText = d > 0 ? $"▲{d}" : d < 0 ? $"▼{-d}" : "持平";
                crDeltaBrush = d > 0 ? Palette.Moss : d < 0 ? Palette.AccentDeep : Palette.Muted;
            }

            classRankPoints.Add(new ChartPoint
            {
                ExamOrderText = orderText,
                ExamName = examName,
                Value = p.ClassRank ?? 0,
                ValueText = p.ClassRank is not null ? $"第 {p.ClassRank.Value} 名" : "无名次",
                DeltaText = crDeltaText,
                DeltaBrush = crDeltaBrush,
                IsAbsent = p.IsAbsent || !p.ClassRank.HasValue
            });

            // 校排走势
            string srDeltaText = "—";
            IBrush? srDeltaBrush = Palette.Muted;
            if (i > 0 && p.SchoolRank.HasValue && traj.Points[i - 1].SchoolRank.HasValue)
            {
                var d = traj.Points[i - 1].SchoolRank!.Value - p.SchoolRank.Value;
                srDeltaText = d > 0 ? $"▲{d}" : d < 0 ? $"▼{-d}" : "持平";
                srDeltaBrush = d > 0 ? Palette.Moss : d < 0 ? Palette.AccentDeep : Palette.Muted;
            }

            schoolRankPoints.Add(new ChartPoint
            {
                ExamOrderText = orderText,
                ExamName = examName,
                Value = p.SchoolRank ?? 0,
                ValueText = p.SchoolRank is not null ? $"校 {p.SchoolRank.Value} 名" : "无名次",
                DeltaText = srDeltaText,
                DeltaBrush = srDeltaBrush,
                IsAbsent = p.IsAbsent || !p.SchoolRank.HasValue
            });
        }

        var subRows = new List<SubjectTrajectoryRow>();
        foreach (var sub in traj.Subjects)
        {
            var scoresText = sub.Scores.Select(s => s is not null ? s.Value.ToString("0.#", CultureInfo.InvariantCulture) : "—").ToList();
            var ranksText = sub.ClassRanks.Select(r => r is not null ? r.Value.ToString() : "—").ToList();

            string netText = "—";
            IBrush netBrush = Palette.Muted;
            if (sub.NetClassRankDelta.HasValue)
            {
                var nd = sub.NetClassRankDelta.Value;
                netText = nd > 0 ? $"▲{nd}名" : nd < 0 ? $"▼{-nd}名" : "持平";
                netBrush = nd > 0 ? Palette.Moss : nd < 0 ? Palette.AccentDeep : Palette.Muted;
            }

            var (trendTag, trendBrush) = sub.Trend switch
            {
                TrajectoryTrendType.Rising => ("攀升", Palette.Moss),
                TrajectoryTrendType.StableHigh => ("稳定", Palette.InkSoft),
                TrajectoryTrendType.Fluctuating => ("起伏", Palette.Accent),
                TrajectoryTrendType.Declining => ("下滑", Palette.AccentDeep),
                _ => ("平稳", Palette.Muted)
            };

            subRows.Add(new SubjectTrajectoryRow
            {
                SubjectName = sub.SubjectName,
                ExamScoresText = scoresText,
                ExamRanksText = ranksText,
                NetDeltaText = netText,
                NetDeltaBrush = netBrush,
                TrendTag = trendTag,
                TrendTagBrush = trendBrush
            });
        }

        var (overallTag, overallBrush) = traj.OverallTrend switch
        {
            TrajectoryTrendType.Rising => ("🌟 稳步攀升", Palette.Moss),
            TrajectoryTrendType.StableHigh => ("📈 高位稳定", Palette.InkSoft),
            TrajectoryTrendType.Fluctuating => ("🔄 起伏波动", Palette.Accent),
            TrajectoryTrendType.Declining => ("⚠️ 出现下滑", Palette.AccentDeep),
            _ => ("📊 数据积累中", Palette.Muted)
        };

        return new MultiExamProfile
        {
            IsAvailable = true,
            ExamCount = traj.Points.Count,
            OverallTrendTag = overallTag,
            OverallTrendBrush = overallBrush,
            OverallTrendDescription = traj.OverallTrendDescription,
            HighestTotalText = traj.HighestTotal is not null ? $"{traj.HighestTotal.Value:0.#} 分" : "—",
            HighestTotalExam = traj.HighestTotalExam,
            LowestTotalText = traj.LowestTotal is not null ? $"{traj.LowestTotal.Value:0.#} 分" : "—",
            LowestTotalExam = traj.LowestTotalExam,
            TotalRangeText = traj.TotalRange is not null ? $"{traj.TotalRange.Value:0.#} 分" : "—",
            BestClassRankText = traj.BestClassRank is not null ? $"第 {traj.BestClassRank.Value} 名" : "—",
            BestClassRankExam = traj.BestClassRankExam,
            LowestClassRankText = traj.LowestClassRank is not null ? $"第 {traj.LowestClassRank.Value} 名" : "—",
            LowestClassRankExam = traj.LowestClassRankExam,
            ClassRankRangeText = traj.ClassRankRange is not null ? $"{traj.ClassRankRange.Value} 名" : "—",
            ScorePoints = scorePoints,
            ClassRankPoints = classRankPoints,
            SchoolRankPoints = schoolRankPoints,
            SubjectRows = subRows
        };
    }

    /// <summary>构建单科画像：全科参照、单科分数分布、及格率与单科学生排名。</summary>
    private static SubjectDetail BuildSubjectDetail(ExamData data, string subjectName, GradeInfo grade)
    {
        var scoredStudents = data.Students
            .Where(s => s[subjectName].Score.HasValue)
            .OrderByDescending(s => s[subjectName].Score!.Value)
            .ToList();

        var maxObserved = scoredStudents.Count > 0 ? scoredStudents.Max(s => s[subjectName].Score) : null;
        var full = SubjectMarks.FullMark(subjectName, maxObserved, grade);

        var avg = scoredStudents.Count > 0 ? scoredStudents.Average(s => s[subjectName].Score!.Value) : 0;
        var avgRate = full > 0 ? avg / full : 0;

        var highest = scoredStudents.Count > 0 ? scoredStudents[0][subjectName].Score!.Value : 0;
        var topStudents = scoredStudents.Where(s => s[subjectName].Score == highest).Select(s => s.Name).Take(3).ToList();
        var topStudentText = topStudents.Count > 0 ? string.Join("、", topStudents) : "—";

        var lowest = scoredStudents.Count > 0 ? scoredStudents[^1][subjectName].Score!.Value : 0;

        var passLine = full * 0.6;
        var passCount = scoredStudents.Count(s => s[subjectName].Score >= passLine);
        var passRate = scoredStudents.Count > 0 ? (double)passCount / scoredStudents.Count : 0;

        var excLine = full * 0.85;
        var excCount = scoredStudents.Count(s => s[subjectName].Score >= excLine);
        var excRate = scoredStudents.Count > 0 ? (double)excCount / scoredStudents.Count : 0;

        var subjectRankings = data.Subjects
            .Select(sub =>
            {
                var scores = data.Students.Select(s => s[sub].Score).Where(v => v.HasValue).Select(v => v!.Value).ToList();
                var subFull = SubjectMarks.FullMark(sub, scores.Count > 0 ? scores.Max() : null, grade);
                var subAvg = scores.Count > 0 ? scores.Average() : 0;
                var subRate = subFull > 0 ? subAvg / subFull : 0;
                return (Name: sub, Rate: subRate);
            })
            .OrderByDescending(x => x.Rate)
            .ToList();

        var subjectRankIndex = subjectRankings.FindIndex(x => x.Name == subjectName) + 1;
        var rankAmongText = subjectRankIndex > 0
            ? $"全科均分得分率第 {subjectRankIndex} / {data.Subjects.Count} 名" + (subjectRankIndex <= Math.Max(1, data.Subjects.Count / 3) ? " · 优势学科" : subjectRankIndex > data.Subjects.Count * 2 / 3 ? " · 薄弱学科" : " · 表现中位")
            : "学科参照";

        var tierDefs = new (string Label, string Fill, double MinRate, double MaxRate)[]
        {
            ("优秀 (≥85%)", Strips.TierTop, 0.85, 2.0),
            ("良好 (70-85%)", Strips.TierGood, 0.70, 0.85),
            ("及格 (60-70%)", Strips.TierUpper, 0.60, 0.70),
            ("待提高 (<60%)", Strips.TierLow, 0.0, 0.60),
        };

        var tierList = new List<TierStat>();
        foreach (var def in tierDefs)
        {
            var count = scoredStudents.Count(s =>
            {
                var r = full > 0 ? s[subjectName].Score!.Value / full : 0;
                return r >= def.MinRate && (def.MaxRate >= 2.0 ? r <= def.MaxRate : r < def.MaxRate);
            });
            var ratio = scoredStudents.Count > 0 ? (double)count / scoredStudents.Count : 0;
            tierList.Add(new TierStat
            {
                Label = def.Label,
                CountText = $"{count} 人",
                PercentText = scoredStudents.Count > 0 ? $"{ratio * 100:0.0}%" : "—",
                Weight = ratio,
                Fill = Brush(def.Fill),
                Foreground = Palette.Ink,
            });
        }

        var studentRows = new List<SubjectStudentRow>();
        for (var i = 0; i < scoredStudents.Count; i++)
        {
            var student = scoredStudents[i];
            var score = student[subjectName].Score!.Value;
            var delta = score - avg;
            var rate = full > 0 ? Math.Clamp(score / full, 0, 1) : 0;

            studentRows.Add(new SubjectStudentRow
            {
                Rank = i + 1,
                RankText = (i + 1).ToString("D2"),
                StudentName = student.Name,
                ExamNo = string.IsNullOrEmpty(student.ExamNo) ? $"学号 {student.No}" : student.ExamNo,
                ScoreText = Fmt(score),
                RateText = $"{rate * 100:0.0}%",
                DeltaText = delta >= 0 ? $"+{delta:0.0}" : $"{delta:0.0}",
                DeltaBrush = delta >= 0 ? Palette.Moss : Palette.AccentDeep,
                FillWeight = rate,
                AverageWeight = Math.Clamp(avgRate, 0, 1),
            });
        }

        var diagnosis = SubjectDiagnosisEngine.Analyze(data, subjectName, grade);

        return new SubjectDetail
        {
            SubjectName = subjectName,
            FullMarkText = $"{full} 分",
            AverageText = Fmt(avg),
            AverageRateText = $"({avgRate * 100:0.0}%)",
            HighestText = Fmt(highest),
            HighestStudentText = $"最高分: {topStudentText}",
            LowestText = Fmt(lowest),
            RangeText = $"{highest - lowest:0.#} 分 (最高 {Fmt(highest)} / 最低 {Fmt(lowest)})",
            PassRateText = $"{passRate * 100:0.0}%",
            PassCountText = $"{passCount} / {scoredStudents.Count} 人及格",
            ExcellentRateText = $"{excRate * 100:0.0}% ({excCount}人)",
            RankAmongSubjectsText = rankAmongText,
            GradeBadge = diagnosis.GradeBadge,
            MacroAssessment = diagnosis.MacroAssessment,
            PolarizationAssessment = diagnosis.PolarizationAssessment,
            BorderlineAssessment = diagnosis.BorderlineAssessment,
            ActionStrategies = diagnosis.ActionStrategies,
            SummaryInsight = diagnosis.ComprehensiveSummary,
            Tiers = tierList,
            Students = studentRows,
        };
    }

}
