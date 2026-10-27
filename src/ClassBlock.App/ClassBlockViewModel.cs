using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClassBlock.Core;
using ClassBlock.Core.Models;
using ClassBlock.Core.Rendering;
using ClassBlock.Core.Timetable;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassBlock.App;

public partial class ClassBlockViewModel : ObservableObject
{
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#5A7A4B"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#C6613F"));

    private readonly List<ClassItemViewModel> _classes = new();
    private MasterSchedule? _master;
    private int _renderVersion;
    private bool _suppressTitle;
    private CancellationTokenSource? _previewCts;
    private bool _isClosed;

    public ObservableCollection<ClassItemViewModel> FilteredClasses { get; } = new();

    // ----------------------------- 总表信息 -----------------------------

    [ObservableProperty] private string _sourceName = "尚未导入总表";
    [ObservableProperty] private string _masterSummary = string.Empty;
    [ObservableProperty] private string _gradeText = string.Empty;
    [ObservableProperty] private string _termText = string.Empty;
    [ObservableProperty] private string _monthText = string.Empty;
    [ObservableProperty] private string _sheetText = string.Empty;
    [ObservableProperty] private string _warningText = string.Empty;
    [ObservableProperty] private bool _hasWarnings;

    // ----------------------------- 班级 -----------------------------

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ClassItemViewModel? _selectedClass;

    // ----------------------------- 课表显示设置 -----------------------------

    [ObservableProperty] private string _title = "课程表";
    [ObservableProperty] private string _subtitle = "导入总表后选择班级，即可生成课程表。";
    [ObservableProperty] private bool _showTeachers;
    [ObservableProperty] private bool _showEarlyReading = true;
    [ObservableProperty] private bool _showLunchStudy = true;
    [ObservableProperty] private bool _showAfterSchool = true;
    [ObservableProperty] private bool _showEveningStudy = true;

    // ----------------------------- 作息模板 -----------------------------

    public ObservableCollection<TimetableTemplate> Templates { get; } = new();
    [ObservableProperty] private TimetableTemplate? _selectedTemplate;

    // ----------------------------- 预览与状态 -----------------------------

    [ObservableProperty] private Bitmap? _preview;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "导入总表后，软件会自动识别年级、班级与课表。";
    [ObservableProperty] private IBrush _statusBrush = SuccessBrush;

    public ClassBlockViewModel()
    {
        LoadTemplates();
    }

    public bool HasMaster => _master is not null;
    public bool HasSelection => SelectedClass is not null;
    public bool CanExport => HasSelection && !IsBusy;
    public bool HasPreview => Preview is not null;
    public bool IsEmptyState => _master is null;
    public bool IsSelectPrompt => _master is not null && SelectedClass is null;
    public bool IsIdle => !IsBusy;
    public bool IsClassListEmpty => FilteredClasses.Count == 0;
    public string ClassEmptyText => _classes.Count == 0 ? "还没有总表" : "没有匹配的班级";
    public string ClassCountLabel => $"{_classes.Count} 个班级";

    // ----------------------------- 作息模板 -----------------------------

    public void LoadTemplates()
    {
        var previousId = SelectedTemplate?.Id;
        var (customs, warnings) = TimetableTemplateIO.LoadCustomTemplates();

        Templates.Clear();
        foreach (var template in TimetableTemplates.Builtins) Templates.Add(template);
        foreach (var template in customs) Templates.Add(template);

        SelectedTemplate = TimetableTemplates.Find(Templates, previousId) ?? PrefsOrDefault();

        if (warnings.Count > 0) SetStatus(warnings[0], true);
    }

    public void AdoptSavedTemplate(string templateId)
    {
        LoadTemplates();
        SelectedTemplate = TimetableTemplates.Find(Templates, templateId) ?? SelectedTemplate;
    }

    private TimetableTemplate PrefsOrDefault()
    {
        if (_master is null) return TimetableTemplates.DefaultForGrade(null);

        var grade = string.IsNullOrWhiteSpace(_master.Info.Grade) ? "未识别" : _master.Info.Grade;
        var prefs = ClassBlockPrefsStore.Load();
        if (prefs.LastTemplateByGrade.TryGetValue(grade, out var id))
        {
            var found = TimetableTemplates.Find(Templates, id);
            if (found is not null) return found;
        }

        return TimetableTemplates.DefaultForGrade(_master.Info.Grade);
    }

    partial void OnSelectedTemplateChanged(TimetableTemplate? value)
    {
        if (value is not null && _master is not null)
        {
            var grade = string.IsNullOrWhiteSpace(_master.Info.Grade) ? "未识别" : _master.Info.Grade;
            var prefs = ClassBlockPrefsStore.Load();
            prefs.LastTemplateByGrade[grade] = value.Id;
            ClassBlockPrefsStore.Save(prefs);
        }

        _ = RefreshPreviewAsync(debounce: true);
    }

    // ----------------------------- 载入 -----------------------------

    public void LoadMaster(MasterSchedule master, string sourcePath)
    {
        _master = master;
        _classes.Clear();

        foreach (var cls in master.Classes)
        {
            var headTeacher = master.HeadTeachers.TryGetValue(cls.Key, out var found) ? found : string.Empty;
            _classes.Add(new ClassItemViewModel(cls, headTeacher));
        }

        SourceName = Path.GetFileName(sourcePath);
        MasterSummary = master.Info.Summary;
        GradeText = string.IsNullOrWhiteSpace(master.Info.Grade) ? "未识别" : master.Info.Grade;
        TermText = string.IsNullOrWhiteSpace(master.Info.Term) ? "未识别" : master.Info.Term;
        MonthText = string.IsNullOrWhiteSpace(master.Info.Month) ? "未识别" : master.Info.Month;
        SheetText = string.IsNullOrWhiteSpace(master.Info.ScheduleSheet) ? "未识别" : master.Info.ScheduleSheet;
        WarningText = string.Join("\n", master.Warnings);
        HasWarnings = master.Warnings.Count > 0;

        SelectedTemplate = PrefsOrDefault();
        SearchText = string.Empty;
        ApplyFilter();

        if (FilteredClasses.Count > 0)
            SelectedClass = FilteredClasses[0];
        else
            SelectedClass = null;

        SetStatus($"已识别 {master.Classes.Count} 个班级 · {master.Info.Grade} · {master.Info.Term}，选择左侧班级即可生成。");
        NotifyState();
    }

    // ----------------------------- 过滤 -----------------------------

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var keyword = (SearchText ?? string.Empty).Trim();
        var matches = keyword.Length == 0
            ? _classes
            : _classes.Where(c =>
                c.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                c.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                c.HeadTeacher.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

        FilteredClasses.Clear();
        foreach (var item in matches) FilteredClasses.Add(item);

        if (SelectedClass is not null && !FilteredClasses.Contains(SelectedClass))
            SelectedClass = FilteredClasses.FirstOrDefault();

        NotifyState();
    }

    // ----------------------------- 生成 -----------------------------

    partial void OnSelectedClassChanged(ClassItemViewModel? value)
    {
        if (value is null || _master is null)
        {
            Preview = null;
            NotifyState();
            return;
        }

        _suppressTitle = true;
        Title = $"{value.Name}课程表";
        _suppressTitle = false;

        SetStatus($"已选择 {value.Name}，正在生成课程表。");
        _ = RefreshPreviewAsync(debounce: true);
        NotifyState();
    }

    partial void OnTitleChanged(string value)
    {
        if (_suppressTitle) return;
        _ = RefreshPreviewAsync(debounce: true);
    }

    partial void OnShowTeachersChanged(bool value) => _ = RefreshPreviewAsync(debounce: true);
    partial void OnShowEarlyReadingChanged(bool value) => _ = RefreshPreviewAsync(debounce: true);
    partial void OnShowLunchStudyChanged(bool value) => _ = RefreshPreviewAsync(debounce: true);
    partial void OnShowAfterSchoolChanged(bool value) => _ = RefreshPreviewAsync(debounce: true);
    partial void OnShowEveningStudyChanged(bool value) => _ = RefreshPreviewAsync(debounce: true);

    public TimetableSettings ToSettings() => new()
    {
        Title = Title,
        ShowTeachers = ShowTeachers,
        ShowEarlyReading = ShowEarlyReading,
        ShowLunchStudy = ShowLunchStudy,
        ShowAfterSchool = ShowAfterSchool,
        ShowEveningStudy = ShowEveningStudy,
    };

    public ClassTimetable BuildDocument()
    {
        if (_master is null || SelectedClass is null)
            throw new ClassBlockException("请先导入总表并选择一个班级。");

        var template = SelectedTemplate ?? TimetableTemplates.DefaultForGrade(_master.Info.Grade);
        return TimetableBuilder.Build(_master, SelectedClass.Key, template, ToSettings());
    }

    /// <summary>
    /// 重建预览。输入类改动走 250ms 防抖，旧的渲染任务会被取消；
    /// PNG 解码放到后台线程，避免 2000×3800 位图在 UI 线程解码造成卡顿。
    /// </summary>
    public async Task RefreshPreviewAsync(bool debounce = false)
    {
        var master = _master;
        var item = SelectedClass;
        if (master is null || item is null)
        {
            Preview = null;
            NotifyState();
            return;
        }

        if (_isClosed) return;

        _previewCts?.Cancel();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        var token = cts.Token;
        var version = ++_renderVersion;
        var settings = ToSettings();
        var template = SelectedTemplate ?? TimetableTemplates.DefaultForGrade(master.Info.Grade);

        try
        {
            if (debounce) await Task.Delay(250, token);

            var (document, png) = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var doc = TimetableBuilder.Build(master, item.Key, template, settings);
                token.ThrowIfCancellationRequested();
                return (doc, TimetableRenderer.RenderPngBytes(doc, 2.0f, token));
            }, token);

            if (version != _renderVersion || token.IsCancellationRequested || _isClosed) return;

            var bitmap = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(png);
                return new Bitmap(stream);
            }, token);

            if (version != _renderVersion || token.IsCancellationRequested || _isClosed)
            {
                bitmap.Dispose();
                return;
            }

            Preview = bitmap;
            Subtitle = document.Subtitle;
            SetStatus($"已生成 {document.ClassName}课程表 · 可直接打印或导出图片。");
            NotifyState();
        }
        catch (OperationCanceledException)
        {
            // 新的输入已接管，旧渲染直接丢弃。
        }
        catch (ClassBlockException ex)
        {
            if (!token.IsCancellationRequested && !_isClosed) SetStatus(ex.Message, true);
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !_isClosed)
                SetStatus($"生成课程表失败：{ex.Message}", true);
        }
        finally
        {
            if (ReferenceEquals(_previewCts, cts)) _previewCts = null;
            cts.Dispose();
        }
    }

    /// <summary>关窗时取消仍在跑的预览渲染，避免回写一张无人引用的位图。</summary>
    public void CancelPendingPreview()
    {
        _isClosed = true;
        _previewCts?.Cancel();
        _renderVersion++;
    }

    // ----------------------------- 状态 -----------------------------

    public void SetStatus(string message, bool isError = false)
    {
        StatusText = message;
        StatusBrush = isError ? ErrorBrush : SuccessBrush;
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasMaster));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(IsSelectPrompt));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsClassListEmpty));
        OnPropertyChanged(nameof(ClassEmptyText));
        OnPropertyChanged(nameof(ClassCountLabel));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanExport));
    }

    /// <summary>
    /// 预览位图是 2000×3800 量级的非托管像素缓冲（约 30MB）。切换班级或勾选设置都会重建，
    /// 旧图必须立即释放，否则连续几次预览就会把托管堆之外的内存推高到数百 MB。
    /// </summary>
    partial void OnPreviewChanged(Bitmap? oldValue, Bitmap? newValue) => oldValue?.Dispose();
}
