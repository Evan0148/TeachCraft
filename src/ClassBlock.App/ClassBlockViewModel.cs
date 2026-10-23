using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

    // ----------------------------- 预览与状态 -----------------------------

    [ObservableProperty] private Bitmap? _preview;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "导入总表后，软件会自动识别年级、班级与课表。";
    [ObservableProperty] private IBrush _statusBrush = SuccessBrush;

    public bool HasMaster => _master is not null;
    public bool HasSelection => SelectedClass is not null;
    public bool HasPreview => Preview is not null;
    public bool IsEmptyState => _master is null;
    public bool IsSelectPrompt => _master is not null && SelectedClass is null;
    public bool IsIdle => !IsBusy;
    public bool IsClassListEmpty => FilteredClasses.Count == 0;
    public string ClassEmptyText => _classes.Count == 0 ? "还没有总表" : "没有匹配的班级";
    public string ClassCountLabel => $"{_classes.Count} 个班级";

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
        _ = RefreshPreviewAsync();
        NotifyState();
    }

    partial void OnTitleChanged(string value)
    {
        if (_suppressTitle) return;
        _ = RefreshPreviewAsync();
    }

    partial void OnShowTeachersChanged(bool value) => _ = RefreshPreviewAsync();
    partial void OnShowEarlyReadingChanged(bool value) => _ = RefreshPreviewAsync();
    partial void OnShowLunchStudyChanged(bool value) => _ = RefreshPreviewAsync();
    partial void OnShowAfterSchoolChanged(bool value) => _ = RefreshPreviewAsync();
    partial void OnShowEveningStudyChanged(bool value) => _ = RefreshPreviewAsync();

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

        return TimetableBuilder.Build(_master, SelectedClass.Key, ToSettings());
    }

    public async Task RefreshPreviewAsync()
    {
        var master = _master;
        var item = SelectedClass;
        if (master is null || item is null)
        {
            Preview = null;
            NotifyState();
            return;
        }

        var version = ++_renderVersion;
        var settings = ToSettings();

        try
        {
            var (document, png) = await Task.Run(() =>
            {
                var doc = TimetableBuilder.Build(master, item.Key, settings);
                return (doc, TimetableRenderer.RenderPngBytes(doc, 2.0f));
            });

            if (version != _renderVersion) return;

            using var stream = new MemoryStream(png);
            Preview = new Bitmap(stream);
            Subtitle = document.Subtitle;
            SetStatus($"已生成 {document.ClassName}课程表 · 可直接打印或导出图片。");
            NotifyState();
        }
        catch (ClassBlockException ex)
        {
            SetStatus(ex.Message, true);
        }
        catch (Exception ex)
        {
            SetStatus($"生成课程表失败：{ex.Message}", true);
        }
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
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(IsSelectPrompt));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsClassListEmpty));
        OnPropertyChanged(nameof(ClassEmptyText));
        OnPropertyChanged(nameof(ClassCountLabel));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsIdle));
}
