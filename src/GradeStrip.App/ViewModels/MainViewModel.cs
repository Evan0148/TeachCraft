using GradeStrip.Core.Comparison;
using GradeStrip.App.Controls;
using GradeStrip.Core.Analysis;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>导入、导出、会话切换等耗时操作期间为 true，用于禁用入口并显示进度文案。</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>忙碌态文案；与状态栏共用同一段文字。</summary>
    [ObservableProperty]
    public partial string BusyText { get; set; } = string.Empty;

    private readonly List<ExamSession> _sessions = new();

    /// <summary>尚未导入成绩总表时独立查看的分析报表；一旦建立会话就并进去。</summary>
    private readonly List<OverviewReport> _standaloneReports = new();
    private bool _overviewDirty = true;
    private bool _stripsDirty = true;
    private bool _reportsDirty = true;
    public ObservableCollection<ExamSessionItem> ExamSessions { get; } = new();

    [ObservableProperty]
    public partial int ActiveExamIndex { get; set; } = 0;

    [ObservableProperty]
    public partial bool IsExamManagerOpen { get; set; } = false;

    /// <summary>删除考试前的二次确认弹窗是否可见。</summary>
    [ObservableProperty]
    public partial bool IsRemoveConfirmOpen { get; set; } = false;

    [ObservableProperty]
    public partial string RemoveConfirmText { get; set; } = string.Empty;

    /// <summary>待删除的考试序号；确认后据此调用 RemoveSession。</summary>
    private int _pendingRemoveOrder;

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
    public RangeObservableCollection<StudentRow> Students { get; } = new();
    public RangeObservableCollection<PreviewItem> StripItems { get; } = new();

    /// <summary>排行列表折叠阈值：超过 50 人时默认只显示前 50 名。</summary>
    public const int RankListCollapseThreshold = 50;

    /// <summary>学生排行实际展示的行（Students 的前 N 项，展开后为全部）。</summary>
    public RangeObservableCollection<StudentRow> VisibleStudents { get; } = new();

    public bool HasMoreStudents => Students.Count > RankListCollapseThreshold;

    [ObservableProperty]
    public partial bool StudentsExpanded { get; set; }

    [ObservableProperty]
    public partial string StudentsToggleText { get; set; } = string.Empty;

    // ---------------------------------------------------------------- 成绩条班级筛选
    /// <summary>成绩条页的班级下拉：首项固定「全年级」，其后为总表中的有效班级（跨班总表才有）。</summary>
    public ObservableCollection<string> StripClasses { get; } = new();

    private bool _syncingStripClasses;

    /// <summary>成绩条页的班级筛选：默认「全年级」，选中具体班级时预览与导出都只用该班学生。</summary>
    [ObservableProperty]
    public partial string? SelectedStripClass { get; set; } = OverviewReportFilter.AllClasses;

    /// <summary>
    /// 单班成绩表保持原样（不显示下拉）；跨班总表显示；
    /// 已选中具体班级时保持可见，方便切回「全年级」。
    /// </summary>
    public bool HasStripClasses =>
        StripClasses.Count > 2 ||
        (StripClasses.Count == 2 && SelectedStripClass != OverviewReportFilter.AllClasses);

    /// <summary>成绩条页当前的数据源：按班级切片后的考试数据（「全年级」时即原数据）。</summary>
    private ExamData? StripSourceData => _data is null ? null : _data.FilterByClass(SelectedStripClass);

    partial void OnSelectedStripClassChanged(string? value)
    {
        OnPropertyChanged(nameof(HasStripClasses));
        if (_syncingStripClasses || _data is null) return;
        if (Section == 1) BuildStrips(StripSourceData!);
        else _stripsDirty = true;
    }

    // ---------------------------------------------------------------- 分析报表
    public ObservableCollection<ReportCardItem> ReportCards { get; } = new();
    public ObservableCollection<ReportSheetItem> ReportSheetTabs { get; } = new();
    public ObservableCollection<ReportColumnItem> ReportColumns { get; } = new();
    public RangeObservableCollection<ReportRowItem> ReportRows { get; } = new();
    public ObservableCollection<string> ReportClasses { get; } = new();

    private int _activeReportIndex;
    private int _activeReportSheetIndex;
    private bool _syncingReportClasses;
    private string? _reportClassAutoKey;

    [ObservableProperty]
    public partial string ActiveReportTitle { get; set; } = string.Empty;

    /// <summary>当前报表的来源文件名与工作表数量，显示在 chip 行下方。</summary>
    [ObservableProperty]
    public partial string ActiveReportSourceText { get; set; } = string.Empty;

    /// <summary>班级筛选：默认「全年级」，仅在报表含多个班级时显示。</summary>
    [ObservableProperty]
    public partial string SelectedReportClass { get; set; } = OverviewReportFilter.AllClasses;

    public bool HasReportClasses => ReportClasses.Count > 1;

    partial void OnSelectedReportClassChanged(string value)
    {
        if (_syncingReportClasses) return;
        BuildReports();
    }

    /// <summary>
    /// 拖拽 / 多参数启动的统一分流：成绩表建立或新增考试会话，分析报表挂到当前会话。
    /// 混拖时先处理成绩表，保证报表有会话可挂。
    /// </summary>
    public async Task ImportDroppedFilesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || IsBusy) return;

        await RunBusyAsync("正在识别拖入的文件…", async () =>
        {
            var (gradeSheets, reports, zipCount) = await Task.Run(() => ClassifyDroppedFiles(paths));

            for (var i = 0; i < gradeSheets.Count; i++)
            {
                if (gradeSheets.Count > 1)
                    BusyText = $"正在导入成绩总表 {i + 1}/{gradeSheets.Count}…";
                await AddSessionCoreAsync(gradeSheets[i], isReportHint: false);
            }

            if (reports.Count > 0)
            {
                if (gradeSheets.Count > 0) BusyText = "正在解析分析报表…";
                // 有会话直接挂载；无会话时 ImportReports 会先尝试从混合工作簿提取成绩数据建立会话
                await ImportReportsCoreAsync(reports);
            }
            else if (zipCount > 0 && gradeSheets.Count == 0)
            {
                ImportNotice = "压缩包不能直接导入。请先解压后选择其中的 .xlsx / .xls 表格导入；若解压后文件名出现乱码，推荐使用 Windows 自带解压或 Bandizip。";
                Status = ImportNotice;
                StatusBrush = Palette.Clay;
            }
        });
    }

    /// <summary>拖入文件的分类在后台完成，避免入口分流时卡住界面。</summary>
    private static (List<string> GradeSheets, List<string> Reports, int ZipCount) ClassifyDroppedFiles(
        IReadOnlyList<string> paths)
    {
        var gradeSheets = new List<string>();
        var reports = new List<string>();
        var zipCount = 0;

        foreach (var path in paths)
        {
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                zipCount++;
                continue;
            }

            if (!path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (OverviewReportImporter.IsReportFile(path))
                reports.Add(path);
            else
                gradeSheets.Add(path);
        }

        return (gradeSheets, reports, zipCount);
    }

    /// <summary>当前显示会话挂载的报表集合；没有成绩会话时用独立报表。</summary>
    private IReadOnlyList<OverviewReport> CurrentReports =>
        _sessions.Count > 0 && ActiveExamIndex >= 0 && ActiveExamIndex < _sessions.Count
            ? _sessions[ActiveExamIndex].Reports
            : _standaloneReports;

    public bool HasReports => CurrentReports.Count > 0;
    public bool HasNoReports => !HasReports;
    public bool HasReportRows => ReportRows.Count > 0;
    public bool HasNoReportRows => !HasReportRows;
    public double ReportTableWidth => ReportColumns.Sum(c => c.Width);

    /// <summary>当前报表有多张工作表时才显示表名 chip。</summary>
    public bool HasMultipleReportSheets
    {
        get
        {
            var reports = CurrentReports;
            if (reports.Count == 0) return false;
            var index = Math.Clamp(_activeReportIndex, 0, reports.Count - 1);
            return reports[index].Sheets.Count > 1;
        }
    }

    [ObservableProperty]
    public partial StudentDetail? Detail { get; set; }

    [ObservableProperty]
    public partial SubjectDetail? SubjectDetail { get; set; }

    public bool IsEmptyVisible => !HasData && !HasReports;
    public bool IsOverviewVisible => (HasData || HasReports) && Section == 0;
    public bool IsStripsVisible => HasData && Section == 1;
    public bool IsDetailVisible => HasData && Section == 2 && Detail is not null;
    public bool IsSubjectDetailVisible => HasData && Section == 3 && SubjectDetail is not null;
    public bool IsOverviewNavActive => Section is 0 or 2 or 3;
    public bool CanExport => HasData && !IsBusy;
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

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanExport));

    partial void OnSectionChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewVisible));
        OnPropertyChanged(nameof(IsStripsVisible));
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(IsSubjectDetailVisible));
        OnPropertyChanged(nameof(IsOverviewNavActive));
        OnPropertyChanged(nameof(HeaderEyebrow));

        // 切走页签时释放其集合，切回时按需重建；不靠 IsVisible 隐藏整棵对象图。
        if (value == 0)
        {
            if (StripItems.Count > 0)
            {
                StripItems.ReplaceAll(Array.Empty<PreviewItem>());
                _stripsDirty = true;
            }
            EnsureOverviewBuilt();
        }
        else if (value == 1)
        {
            ReleaseOverviewCollections();
            EnsureStripsBuilt();
        }
        else
        {
            ReleaseOverviewCollections();
            if (StripItems.Count > 0)
            {
                StripItems.ReplaceAll(Array.Empty<PreviewItem>());
                _stripsDirty = true;
            }
        }
    }

    private void EnsureOverviewBuilt()
    {
        if (_data is not null && _overviewDirty) BuildOverview(_data);
        if (_reportsDirty) BuildReports();
    }

    private void EnsureStripsBuilt()
    {
        if (StripSourceData is not null && _stripsDirty) BuildStrips(StripSourceData);
    }

    private void ReleaseOverviewCollections()
    {
        if (Students.Count > 0)
        {
            Students.ReplaceAll(Array.Empty<StudentRow>());
            _overviewDirty = true;
        }
        if (VisibleStudents.Count > 0)
            VisibleStudents.ReplaceAll(Array.Empty<StudentRow>());
        if (ReportRows.Count > 0)
        {
            ReportRows.ReplaceAll(Array.Empty<ReportRowItem>());
            _reportsDirty = true;
        }
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

        var detail = BuildSubjectDetail(_data, subjectName, _gradeInfo);
        ApplySubjectStudentVisibility(detail);
        SubjectDetail = detail;
        Section = 3;
        Status = $"正在查看【{subjectName}】的学科表现与学生排名";
        StatusBrush = Palette.Muted;
    }

    /// <summary>按折叠状态同步「单科学生排名」的可见行与按钮文案。</summary>
    private void ApplySubjectStudentVisibility(SubjectDetail detail)
    {
        var total = detail.Students.Count;
        detail.HasMoreStudents = total > RankListCollapseThreshold;
        detail.ListToggleText = detail.HasMoreStudents
            ? (StudentsExpanded ? "收起列表" : $"展开全部 {total} 名学生")
            : string.Empty;
        var take = StudentsExpanded ? total : Math.Min(total, RankListCollapseThreshold);
        detail.VisibleStudents = detail.Students.Take(take).ToList();
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
        if (_data is null) return;
        if (Section == 0) BuildOverview(_data);
        else _overviewDirty = true;
    }

    private void RefreshStrips()
    {
        if (_data is null) return;
        if (Section == 1) BuildStrips(StripSourceData!);
        else _stripsDirty = true;
    }


    public void OpenExamManager() => IsExamManagerOpen = true;
    public void CloseExamManager() => IsExamManagerOpen = false;

    /// <summary>请求删除一场考试：先弹出二次确认，避免误删。</summary>
    public void RequestRemoveSession(int order)
    {
        var idx = _sessions.FindIndex(s => s.Order == order);
        if (idx < 0) return;

        _pendingRemoveOrder = order;
        var session = _sessions[idx];
        RemoveConfirmText = $"第 {session.Order} 次考试 · {session.DisplayName}（{session.Data.Students.Count} 人 · {session.Data.Subjects.Count} 科）将被移除，删除后无法恢复。";
        IsRemoveConfirmOpen = true;
    }

    /// <summary>确认删除：关闭弹窗并真正移除该场考试。</summary>
    public void ConfirmRemoveSession()
    {
        var order = _pendingRemoveOrder;
        IsRemoveConfirmOpen = false;
        RemoveSession(order);
    }

    /// <summary>取消删除：仅关闭确认弹窗，不改动任何数据。</summary>
    public void CancelRemoveSession() => IsRemoveConfirmOpen = false;

    /// <summary>
    /// 导入智学网分析报表，挂到当前考试会话下。
    /// 单份失败不阻断其余文件，最后统一汇报成功与失败数量。
    /// </summary>
    public async Task ImportReportsAsync(IReadOnlyList<string> paths)
    {
        if (IsBusy) return;
        await RunBusyAsync("正在解析分析报表…", () => ImportReportsCoreAsync(paths));
    }

    private async Task ImportReportsCoreAsync(IReadOnlyList<string> paths)
    {
        // 无会话时，允许从混合工作簿（成绩数据 + 报表页）中先提取成绩数据建立会话
        if (!HasData || _sessions.Count == 0)
        {
            string? seedPath = null;
            ExamData? seedData = null;

            foreach (var path in paths)
            {
                try
                {
                    var pathCopy = path;
                    seedData = await Task.Run(() => GradeSheetImporter.Load(pathCopy));
                    seedPath = path;
                    break;
                }
                catch
                {
                    // 该文件不含可识别的成绩数据，继续尝试下一份
                }
            }

            if (seedPath is not null && seedData is not null)
            {
                var created = CreateSession(seedPath, seedData);
                ApplyActiveExamAndComparison();
                ImportNotice = "已从文件内同时导入成绩总表与分析报表。";
            }
        }

        // 没有成绩会话也照样能看报表：挂到独立报表区，不要求先导入成绩总表
        var target = _sessions.Count > 0
            ? _sessions[Math.Clamp(ActiveExamIndex, 0, _sessions.Count - 1)].Reports
            : _standaloneReports;

        // 后台一次性解析全部报表，UI 线程只做集合应用与重建
        var loadedReports = await Task.Run(() =>
        {
            var list = new List<(OverviewReportKind Kind, IReadOnlyList<OverviewReport> Reports)>(paths.Count);
            foreach (var path in paths)
            {
                var reports = OverviewReportImporter.Load(path, out var kind);
                list.Add((kind, reports));
            }
            return list;
        });

        var imported = 0;
        var failed = 0;
        var sawGradeSheet = false;
        var lastIndex = -1;

        foreach (var (kind, reports) in loadedReports)
        {
            if (kind == OverviewReportKind.Generic)
            {
                failed++;
                sawGradeSheet = true;
                continue;
            }

            if (reports.Count == 0)
            {
                failed++;
                continue;
            }

            foreach (var report in reports)
            {
                // 以源文件名为去重键：同一份报表重复导入时用新数据替换旧数据
                var existing = target.FindIndex(r =>
                    string.Equals(r.SourceFile, report.SourceFile, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                {
                    target[existing] = report;
                    lastIndex = existing;
                }
                else
                {
                    target.Add(report);
                    lastIndex = target.Count - 1;
                }
                imported++;
            }
        }

        if (sawGradeSheet)
            ImportNotice = "该文件是成绩总表，请使用『选择成绩总表』导入";

        if (imported > 0)
        {
            _activeReportIndex = Math.Max(0, lastIndex);
            _activeReportSheetIndex = 0;
            if (!HasData) ImportNotice = string.Empty;
        }
        if (Section == 0) BuildReports();
        else _reportsDirty = true;

        if (!HasReports) _activeReportIndex = 0;

        Status = $"已导入 {imported} 份报表 · {failed} 份未识别";
        StatusBrush = failed > 0 ? Palette.Clay : Palette.Moss;
    }

    private static void MergeReports(ExamSession session, IReadOnlyList<OverviewReport> reports)
    {
        foreach (var report in reports)
        {
            var existing = session.Reports.FindIndex(r =>
                string.Equals(r.SourceFile, report.SourceFile, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) session.Reports[existing] = report;
            else session.Reports.Add(report);
        }
    }

    /// <summary>把一场考试加入会话列表并激活首场。</summary>
    private ExamSession CreateSession(string path, ExamData data)
    {
        var order = _sessions.Count + 1;
        var session = new ExamSession
        {
            Order = order,
            Label = $"第 {order} 次考试",
            SourcePath = path,
            SourceFileName = Path.GetFileName(path),
            Data = data
        };

        // 之前独立查看的报表并入新会话，切过去不会丢
        if (_standaloneReports.Count > 0)
        {
            MergeReports(session, _standaloneReports);
            _standaloneReports.Clear();
        }

        _sessions.Add(session);
        SyncExamSessions();
        if (_sessions.Count == 1) ActiveExamIndex = 0;
        return session;
    }

    /// <summary>统一的忙碌态包装：耗时操作全部在后台完成，UI 线程只更新状态与集合。</summary>
    private async Task RunBusyAsync(string text, Func<Task> action)
    {
        IsBusy = true;
        BusyText = text;
        Status = text;
        StatusBrush = Palette.Muted;

        // 先把忙碌遮罩绘制出来再开始干活：低优先级会让出一次调度，避免第一帧就被耗时任务占住。
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Status = FriendlyError(ex, "操作失败");
            StatusBrush = Palette.Seal;
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    public Task LoadAsync(string path) => AddSessionAsync(path);

    public async Task AddSessionAsync(string path)
    {
        if (IsBusy) return;
        await RunBusyAsync("正在导入成绩总表…", () => AddSessionCoreAsync(path));
    }

    private async Task AddSessionCoreAsync(string path, bool? isReportHint = null)
    {
        try
        {
            // 入口已分类时直接沿用结果，避免为同一文件再打开一次做探测。
            var isReport = isReportHint ??
                           await Task.Run(() => OverviewReportImporter.IsReportFile(path));

            if (isReport)
            {
                // 走错入口也不再拒绝：直接按分析报表导入，界面马上能看
                await ImportReportsCoreAsync(new[] { path });
                return;
            }

            var data = await Task.Run(() => GradeSheetImporter.Load(path));
            var session = CreateSession(path, data);
            ApplyActiveExamAndComparison();

            Status = $"已导入【第 {session.Order} 次考试】· 共 {_sessions.Count} 场考试";
            StatusBrush = Palette.Moss;
        }
        catch (Exception ex)
        {
            Status = FriendlyError(ex, "导入考试失败");
            StatusBrush = Palette.Seal;
        }
    }

    public async Task ReplaceSessionAsync(int order, string path)
    {
        if (IsBusy) return;
        await RunBusyAsync("正在替换考试数据…", () => ReplaceSessionCoreAsync(order, path));
    }

    private async Task ReplaceSessionCoreAsync(int order, string path)
    {
        var idx = _sessions.FindIndex(s => s.Order == order);
        if (idx < 0) return;

        try
        {
            var isReport = await Task.Run(() => OverviewReportImporter.IsReportFile(path));
            if (isReport)
            {
                // 替换入口选到报表时不覆盖原成绩，改为把报表挂上
                await ImportReportsCoreAsync(new[] { path });
                return;
            }

            var data = await Task.Run(() => GradeSheetImporter.Load(path));
            var session = new ExamSession
            {
                Order = order,
                Label = $"第 {order} 次考试",
                SourcePath = path,
                SourceFileName = Path.GetFileName(path),
                Data = data
            };
            session.Reports.AddRange(_sessions[idx].Reports);
            _sessions[idx] = session;
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
            var reordered = new ExamSession
            {
                Order = i + 1,
                Label = $"第 {i + 1} 次考试",
                SourcePath = s.SourcePath,
                SourceFileName = s.SourceFileName,
                Data = s.Data
            };
            reordered.Reports.AddRange(s.Reports);
            _sessions[i] = reordered;
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
            var reordered = new ExamSession
            {
                Order = i + 1,
                Label = $"第 {i + 1} 次考试",
                SourcePath = s.SourcePath,
                SourceFileName = s.SourceFileName,
                Data = s.Data
            };
            reordered.Reports.AddRange(s.Reports);
            _sessions[i] = reordered;
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
            ReleaseOverviewCollections();
            if (StripItems.Count > 0)
            {
                StripItems.ReplaceAll(Array.Empty<PreviewItem>());
                _stripsDirty = true;
            }
            BuildReports();
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

        // 多考对比会重建 StudentRecord，班级信息不会自动带过来；按序号补回，班级筛选才有数据
        if (baseline is not null)
        {
            var classByNo = new Dictionary<int, string>(activeSession.Data.Students.Count);
            foreach (var s in activeSession.Data.Students)
                classByNo[s.No] = s.ClassName;
            foreach (var s in displayData.Students)
                s.ClassName = classByNo.TryGetValue(s.No, out var studentClass) ? studentClass : string.Empty;
        }

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
        SyncStripClasses(displayData);

        // 只重建当前页签需要的集合，其余页签切过去时再按需构建。
        _overviewDirty = true;
        _stripsDirty = true;
        _reportsDirty = true;
        ReleaseOverviewCollections();
        if (StripItems.Count > 0)
            StripItems.ReplaceAll(Array.Empty<PreviewItem>());

        if (Section == 0) EnsureOverviewBuilt();
        else if (Section == 1) EnsureStripsBuilt();

        if (Detail is not null)
        {
            var student = displayData.Students.FirstOrDefault(s => s.No == Detail.No);
            if (student is not null)
            {
                Detail = BuildDetail(displayData, student, _gradeInfo, _sessions);
            }
        }
    }

    public async Task ExportAsync(string path)
    {
        if (_data is null || IsBusy) return;

        await RunBusyAsync("正在导出成绩条…", async () =>
        {
            try
            {
                string? compNote = null;
                if (ComparisonMode == ComparisonBaseline.PreviousExam && ActiveExamIndex > 0)
                    compNote = $"对比第 {ActiveExamIndex} 次考试";
                else if (ComparisonMode == ComparisonBaseline.FirstExam && ActiveExamIndex > 0)
                    compNote = "对比第 1 次基准考";
                else if (ComparisonMode == ComparisonBaseline.OriginalFile)
                    compNote = "表格自带";

                // 与预览共用同一份班级切片：界面看哪个班，就导出哪个班
                var data = _data.FilterByClass(SelectedStripClass);
                var options = new StripExportOptions
                {
                    FirstGroupSize = Math.Max(1, (int)FirstGroupSize),
                    GroupSize = Math.Max(1, (int)GroupSize),
                    IncludeLegend = IncludeLegend,
                    UseSchoolRank = UseSchoolRank,
                    ComparisonNote = compNote,
                    Title = string.IsNullOrWhiteSpace(TitleOverride) ? null : TitleOverride.Trim(),
                };

                var result = await Task.Run(() => ScoreStripExporter.Export(data, path, options));

                Status = $"已导出 {result.StudentCount} 人 · {result.Sheets.Count} 张表 → {Path.GetFileName(path)}";
                StatusBrush = Palette.Moss;
            }
            catch (Exception ex)
            {
                Status = FriendlyError(ex, "导出失败");
                StatusBrush = Palette.Seal;
            }
        });
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
        // 选中具体班级时文件名带上班级（如「九2班-…-成绩条.xlsx」）；「全年级」沿用原命名
        var scope = string.IsNullOrWhiteSpace(SelectedStripClass) || SelectedStripClass == OverviewReportFilter.AllClasses
            ? ClassName
            : SelectedStripClass;
        var name = $"{scope}-{ExamName}-成绩条".Replace("/", "-").Replace("\\", "-");
        return string.IsNullOrWhiteSpace(name) ? "成绩条.xlsx" : $"{name}.xlsx";
    }

    private void BuildOverview(ExamData data)
    {
        Kpis.Clear();
        SubjectStats.Clear();
        TierStats.Clear();
        TrendStats.Clear();

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
        // 方案 A：跨班考试时在学生姓名后展示班级角标；单班考试角标没有信息量，保持原样
        var crossClass = data.AvailableClasses.Count > 1;
        var studentRows = new List<StudentRow>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var student = ordered[i];
            var delta = student.Total.ClassDelta;
            var score = student.Total.Score ?? 0;
            var ratio = score / Math.Max(1, bestTotal);
            studentRows.Add(new StudentRow
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
                ClassBadgeText = crossClass ? student.ClassName : string.Empty,
            });
        }
        Students.ReplaceAll(studentRows);

        SheetSummary = data.HasImportNotice
            ? $"{data.Students.Count} 人 · {data.Subjects.Count} 科 · 简表"
            : $"{data.Students.Count} 人 · {data.Subjects.Count} 科";

        RefreshVisibleStudents();
        _overviewDirty = false;
    }

    /// <summary>按折叠状态同步「学生排行」的可见行与展开按钮文案。</summary>
    private void RefreshVisibleStudents()
    {
        var take = StudentsExpanded ? Students.Count : Math.Min(Students.Count, RankListCollapseThreshold);
        VisibleStudents.ReplaceAll(Students.Take(take));

        OnPropertyChanged(nameof(HasMoreStudents));
        StudentsToggleText = HasMoreStudents
            ? (StudentsExpanded ? "收起列表" : $"展开全部 {Students.Count} 名学生")
            : string.Empty;
    }

    /// <summary>展开 / 收起首页学生排行与单科学生排名。</summary>
    public void ToggleStudentsExpanded()
    {
        StudentsExpanded = !StudentsExpanded;
        RefreshVisibleStudents();

        if (SubjectDetail is not null) ApplySubjectStudentVisibility(SubjectDetail);
    }

    /// <summary>重建「分析报表」区块：切换 chip、表名 chip、表头与数据行。</summary>
    private void BuildReports()
    {
        var reports = CurrentReports;

        ReportCards.Clear();
        ReportSheetTabs.Clear();
        ReportColumns.Clear();
        ReportRows.ReplaceAll(Array.Empty<ReportRowItem>());
        OnPropertyChanged(nameof(ReportTableWidth));

        if (reports.Count == 0)
        {
            _activeReportIndex = 0;
            _activeReportSheetIndex = 0;
            ActiveReportTitle = string.Empty;
            ActiveReportSourceText = string.Empty;
            SyncReportClasses(null);
            NotifyReportState();
            _reportsDirty = false;
            return;
        }

        _activeReportIndex = Math.Clamp(_activeReportIndex, 0, reports.Count - 1);

        for (var i = 0; i < reports.Count; i++)
        {
            var index = i;
            var report = reports[i];
            ReportCards.Add(new ReportCardItem
            {
                Report = report,
                IsActive = i == _activeReportIndex,
                Activate = new RelayCommand(() => ActivateReport(index)),
            });
        }

        var active = reports[_activeReportIndex];
        SyncReportClasses(active);
        ActiveReportTitle = active.Title;
        ActiveReportSourceText = $"来源：{active.SourceFile} · {active.Sheets.Count} 张表";

        var view = OverviewReportFilter.Filter(active, SelectedReportClass);
        var sheets = view.Sheets;
        _activeReportSheetIndex = sheets.Count == 0
            ? 0
            : Math.Clamp(_activeReportSheetIndex, 0, sheets.Count - 1);

        for (var i = 0; i < sheets.Count; i++)
        {
            var index = i;
            ReportSheetTabs.Add(new ReportSheetItem
            {
                Name = sheets[i].Name,
                IsActive = i == _activeReportSheetIndex,
                Activate = new RelayCommand(() => ActivateReportSheet(index)),
            });
        }

        if (sheets.Count > 0)
        {
            var sheet = sheets[_activeReportSheetIndex];
            var widths = ReportColumnWidths(sheet);

            for (var c = 0; c < sheet.Columns.Count; c++)
            {
                var column = sheet.Columns[c];
                ReportColumns.Add(new ReportColumnItem
                {
                    Header = column.Header,
                    Width = widths[c],
                    IsNumeric = column.IsNumeric,
                    Alignment = column.IsNumeric ? TextAlignment.Right : TextAlignment.Left,
                });
            }
            OnPropertyChanged(nameof(ReportTableWidth));

            var rowItems = new List<ReportRowItem>(sheet.Rows.Count);
            foreach (var row in sheet.Rows)
            {
                var cells = new List<ReportCellItem>(sheet.Columns.Count);
                for (var c = 0; c < sheet.Columns.Count; c++)
                {
                    var column = sheet.Columns[c];
                    cells.Add(new ReportCellItem
                    {
                        Text = c < row.Count ? row[c] : string.Empty,
                        Width = widths[c],
                        IsNumeric = column.IsNumeric,
                        Alignment = column.IsNumeric ? TextAlignment.Right : TextAlignment.Left,
                    });
                }
                rowItems.Add(new ReportRowItem { Cells = cells });
            }
            ReportRows.ReplaceAll(rowItems);
        }

        NotifyReportState();
        _reportsDirty = false;
    }

    /// <summary>
    /// 列宽按内容推算：首列（题号 / 序号）可以很窄；长文本列（如答错学生名单）
    /// 往右延伸到放得下整段内容，超过上限才交给单元格换行，不再用省略号吞掉学生姓名。
    /// </summary>
    private static double[] ReportColumnWidths(OverviewSheet sheet)
    {
        var widths = new double[sheet.Columns.Count];
        for (var c = 0; c < sheet.Columns.Count; c++)
        {
            var column = sheet.Columns[c];

            // 数值列宽度固定，避免分组表头被挤成两行抬高整个表头
            if (c > 0 && column.IsNumeric)
            {
                widths[c] = ReportLayout.NumericColumnWidth;
                continue;
            }

            var units = DisplayUnits(column.Header);
            foreach (var row in sheet.Rows)
            {
                if (c < row.Count) units = Math.Max(units, DisplayUnits(row[c]));
            }

            var fitted = 22 + units * 7.2;
            widths[c] = c == 0
                ? Math.Clamp(fitted, 48, ReportLayout.FirstColumnWidth)
                : Math.Clamp(fitted, ReportLayout.ColumnWidth, ReportLayout.WideColumnWidth);
        }

        return widths;
    }

    /// <summary>按显示宽度折算：中文算两个字宽，西文与数字算一个；只量到列宽上限就够。</summary>
    private static int DisplayUnits(string text)
    {
        const int cap = 122;
        var units = 0;
        foreach (var ch in text)
        {
            units += ch > 0x2E80 ? 2 : 1;
            if (units >= cap) return cap;
        }
        return units;
    }

    /// <summary>同步班级下拉：始终包含「全年级」；当前选择在新报表中不存在时回到全年级。</summary>
    private void SyncReportClasses(OverviewReport? report)
    {
        var classes = report is null
            ? Array.Empty<string>()
            : OverviewReportFilter.GetClasses(report);

        _syncingReportClasses = true;
        try
        {
            var desired = new List<string> { OverviewReportFilter.AllClasses };
            desired.AddRange(classes);

            // 列表没变就不重建，避免 ComboBox 在清空/重填时把 SelectedItem 写成 null 触发递归刷新
            if (!ReportClasses.SequenceEqual(desired))
            {
                ReportClasses.Clear();
                foreach (var className in desired) ReportClasses.Add(className);
            }

            // 单班成绩表（如「九A1」）导入全年级报表时，默认定位到该班；
            // 每场考试只自动匹配一次，之后尊重用户手动选择。
            var autoKey = $"{_data?.SourceTitle}|{_data?.ClassName}";
            if (!HasReportClasses)
            {
                _reportClassAutoKey = null;
                SelectedReportClass = OverviewReportFilter.AllClasses;
            }
            else if (_reportClassAutoKey != autoKey)
            {
                _reportClassAutoKey = autoKey;
                SelectedReportClass = MatchExamClass(classes);
            }
            else if (!ReportClasses.Contains(SelectedReportClass))
            {
                SelectedReportClass = OverviewReportFilter.AllClasses;
            }
        }
        finally
        {
            _syncingReportClasses = false;
        }

        OnPropertyChanged(nameof(HasReportClasses));
    }

    /// <summary>在报表班级里找与当前成绩表班级同班的一项（忽略「年级」字样差异）。</summary>
    private string MatchExamClass(IReadOnlyList<string> classes)
    {
        var examClass = _data?.ClassName;
        if (string.IsNullOrWhiteSpace(examClass)) return OverviewReportFilter.AllClasses;

        var examKey = OverviewReportFilter.NormalizeClassName(examClass);
        foreach (var className in classes)
        {
            if (OverviewReportFilter.NormalizeClassName(className) == examKey)
                return className;
        }

        return OverviewReportFilter.AllClasses;
    }

    /// <summary>切换当前查看的报表。</summary>
    public void ActivateReport(int index)
    {
        var reports = CurrentReports;
        if (index < 0 || index >= reports.Count) return;

        _activeReportIndex = index;
        _activeReportSheetIndex = 0;
        BuildReports();
    }

    /// <summary>切换当前报表内的工作表。</summary>
    public void ActivateReportSheet(int index)
    {
        var reports = CurrentReports;
        if (reports.Count == 0) return;

        var sheets = reports[Math.Clamp(_activeReportIndex, 0, reports.Count - 1)].Sheets;
        if (index < 0 || index >= sheets.Count) return;

        _activeReportSheetIndex = index;
        BuildReports();
    }

    private void NotifyReportState()
    {
        OnPropertyChanged(nameof(HasReports));
        OnPropertyChanged(nameof(HasNoReports));
        OnPropertyChanged(nameof(HasMultipleReportSheets));
        OnPropertyChanged(nameof(HasReportRows));
        OnPropertyChanged(nameof(HasNoReportRows));

        // 只有报表、没有成绩数据时也要能把概览页（报表区块）显示出来
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(IsOverviewVisible));
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
            QuestionRows = BuildQuestionRows(data, student),
            MultiExamProfile = BuildMultiExamProfile(student.Name, sessions),
        };
    }

    /// <summary>构建得分明细行：以全班逐题最高分为推断满分，刻度为班均得分率。</summary>
    private static IReadOnlyList<QuestionDetailRow> BuildQuestionRows(ExamData data, StudentRecord student)
    {
        if (student.QuestionScores is not { Count: > 0 })
            return Array.Empty<QuestionDetailRow>();

        // 全班逐题统计：推断满分（全班最高分）与班均
        var maxByLabel = new Dictionary<string, double>(StringComparer.Ordinal);
        var sumByLabel = new Dictionary<string, double>(StringComparer.Ordinal);
        var countByLabel = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var other in data.Students)
        {
            if (other.QuestionScores is null) continue;
            foreach (var q in other.QuestionScores)
            {
                if (q.Score is not { } v) continue;
                maxByLabel[q.Label] = Math.Max(maxByLabel.GetValueOrDefault(q.Label), v);
                sumByLabel[q.Label] = sumByLabel.GetValueOrDefault(q.Label) + v;
                countByLabel[q.Label] = countByLabel.GetValueOrDefault(q.Label) + 1;
            }
        }

        var rows = new List<QuestionDetailRow>();
        foreach (var q in student.QuestionScores)
        {
            var full = maxByLabel.GetValueOrDefault(q.Label);
            var average = countByLabel.GetValueOrDefault(q.Label) > 0
                ? sumByLabel.GetValueOrDefault(q.Label) / countByLabel[q.Label]
                : 0;
            var rate = q.Score is { } v && full > 0 ? Math.Clamp(v / full, 0, 1) : 0;
            var averageRate = full > 0 ? Math.Clamp(average / full, 0, 1) : 0;
            var above = q.Score is { } sv && full > 0 && sv > average;

            rows.Add(new QuestionDetailRow
            {
                Label = q.Label,
                ScoreText = Fmt(q.Score),
                FullText = full > 0 ? $"/ {full:0.#}" : string.Empty,
                RateText = q.Score is null ? "—" : $"{rate * 100:0}%",
                FillWeight = rate,
                AverageWeight = averageRate,
                AboveAverage = above,
                FillBrush = above ? Palette.Ink : Palette.InkSoft,
            });
        }

        return rows;
    }

    /// <summary>
    /// 同步成绩条页的班级下拉：首项固定「全年级」，其后为总表中的有效班级。
    /// 会话加载或切换时重置回「全年级」。
    /// </summary>
    private void SyncStripClasses(ExamData data)
    {
        _syncingStripClasses = true;
        try
        {
            var classes = data.AvailableClasses;
            var desired = new List<string>(classes.Count + 1) { OverviewReportFilter.AllClasses };
            desired.AddRange(classes);

            // 列表没变就不重建，避免 ComboBox 在清空/重填时把 SelectedItem 写成 null 触发递归刷新
            if (!StripClasses.SequenceEqual(desired))
            {
                // 绑定初始化时列表为空，ComboBox 已把 SelectedItem 记成「全年级」但无选中容器；
                // 同值重设不会重建容器（显示空框），先清成 null 再回设，强制一次真实选中变更。
                SelectedStripClass = null;
                StripClasses.Clear();
                foreach (var className in desired) StripClasses.Add(className);
            }

            SelectedStripClass = OverviewReportFilter.AllClasses;
        }
        finally
        {
            _syncingStripClasses = false;
        }

        OnPropertyChanged(nameof(HasStripClasses));
    }

    private void BuildStrips(ExamData data)
    {
        var items = new List<PreviewItem>(data.Students.Count + 16);
        var groups = StripGrouping.Split(data.Students, Math.Max(1, (int)FirstGroupSize), Math.Max(1, (int)GroupSize));
        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];

            items.Add(new PreviewItem
            {
                IsHeader = true,
                HeaderText = $"第 {g + 1} 张表",
                HeaderMeta = $"序号 {group[0].No}–{group[^1].No} · {group.Count} 人" +
                             (g == 0 ? " · 含标题" : string.Empty) +
                             (g == 0 && IncludeLegend ? "与图例" : string.Empty),
            });

            if (g == 0)
            {
                items.Add(new PreviewItem
                {
                    IsTitleBlock = true,
                    TitleText = string.IsNullOrWhiteSpace(TitleOverride) ? data.DefaultTitle : TitleOverride.Trim(),
                    HasLegend = IncludeLegend,
                    UseSchoolRank = UseSchoolRank,
                });
            }

            foreach (var student in group)
                items.Add(new PreviewItem { Strip = StripRow.From(student, data.Subjects, UseSchoolRank) });
        }

        StripItems.ReplaceAll(items);
        _stripsDirty = false;
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

    private static IBrush Brush(string hex) => Palette.Brush(hex);


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
