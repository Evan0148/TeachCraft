using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GradeWeave.Core.Models;

namespace GradeWeave.App;

public enum WeaveExportFormat
{
    Excel,
    Png,
    Pdf,
}

public sealed record ExportFormatOption(WeaveExportFormat Format, string Label, string Hint);

/// <summary>Excel 样式项；参考校样式的显示名称可在本机修改。</summary>
public partial class ExportStyleOption : ObservableObject
{
    public ExportStyleOption(WeaveExportStyle style, string label, string hint)
    {
        Style = style;
        _label = label;
        _hint = hint;
    }

    public WeaveExportStyle Style { get; }

    [ObservableProperty] private string _label;
    [ObservableProperty] private string _hint;
}

/// <summary>导出弹窗返回的请求：格式、工作簿内容 / 样式，以及 PNG / PDF 的目标。</summary>
public sealed record WeaveExportRequest(
    WeaveExportFormat Format,
    ExportOptions Options,
    string? ClassId,
    string? TeacherId,
    string TargetLabel);

/// <summary>04 步统一导出设置：格式、Excel 样式、分页内容；PNG / PDF 导出当前选中的班级或教师。</summary>
public partial class ExportOptionsViewModel : ObservableObject
{
    private readonly SchoolClass? _selectedClass;
    private readonly Teacher? _selectedTeacher;
    private readonly bool _teacherView;

    public ExportOptionsViewModel(
        WeaveProject project,
        SchoolClass? selectedClass,
        Teacher? selectedTeacher,
        bool teacherView)
    {
        ArgumentNullException.ThrowIfNull(project);
        _selectedClass = selectedClass;
        _selectedTeacher = selectedTeacher;
        _teacherView = teacherView;

        ProjectTitle = string.IsNullOrWhiteSpace(project.SchoolName)
            ? project.GradeName
            : $"{project.SchoolName} · {project.GradeName}";
        Summary = $"{project.Classes.Count} 个班 · {project.Teachers.Count} 位教师 · {project.Subjects.Count} 个学科";

        Formats.Add(new ExportFormatOption(
            WeaveExportFormat.Excel, "Excel 工作簿", "多分页，可勾选总表、分工表、职位表、班级与教师课表"));
        Formats.Add(new ExportFormatOption(
            WeaveExportFormat.Png, "PNG 图片", "导出当前班级或教师的课表图片"));
        Formats.Add(new ExportFormatOption(
            WeaveExportFormat.Pdf, "PDF 打印", "导出当前班级或教师的 A4 打印课表"));

        Styles.Add(new ExportStyleOption(
            WeaveExportStyle.Software, "软件自带样式", "纸墨色板、雅黑字体、浅色细边"));
        Styles.Add(new ExportStyleOption(
            WeaveExportStyle.Reference, ReferenceStyleName, "宋体 / 楷体、黑色细框、横向课程总表"));

        _selectedFormat = Formats[0];
        _selectedStyle = Styles[0];
    }

    public string ProjectTitle { get; }
    public string Summary { get; }

    public ObservableCollection<ExportFormatOption> Formats { get; } = new();
    public ObservableCollection<ExportStyleOption> Styles { get; } = new();

    [ObservableProperty] private ExportFormatOption _selectedFormat;
    [ObservableProperty] private ExportStyleOption _selectedStyle;
    [ObservableProperty] private string _referenceStyleName = ExportStylePreferences.LoadName();
    [ObservableProperty] private bool _includeMaster = true;
    [ObservableProperty] private bool _includeAssignment = true;
    [ObservableProperty] private bool _includeTeacherPosition = true;
    [ObservableProperty] private bool _includeClassSheets = true;
    [ObservableProperty] private bool _includeTeacherSheets = true;
    [ObservableProperty] private bool _includeConflict = true;
    [ObservableProperty] private string _error = string.Empty;

    public bool HasError => Error.Length > 0;

    public bool IsExcel => SelectedFormat.Format == WeaveExportFormat.Excel;
    public bool IsImageOrPdf => !IsExcel;
    public bool IsReferenceStyle => SelectedStyle.Style == WeaveExportStyle.Reference;

    public string TargetLabel => ResolveTarget().Label;

    public string TargetText
    {
        get
        {
            var target = ResolveTarget();
            if (target.Label == "未选择") return "未选择班级或教师";
            return target.TeacherId is not null ? $"教师 · {target.Label}" : $"班级 · {target.Label}";
        }
    }

    public string TargetHint => "PNG / PDF 导出当前选中的班级或教师课表；如需切换，请先在主窗口左侧选择。";

    private (string? ClassId, string? TeacherId, string Label) ResolveTarget()
    {
        if (_teacherView && _selectedTeacher is not null)
            return (null, _selectedTeacher.Id, _selectedTeacher.Name);
        if (_selectedClass is not null)
            return (_selectedClass.Id, null, _selectedClass.Name);
        if (_selectedTeacher is not null)
            return (null, _selectedTeacher.Id, _selectedTeacher.Name);
        return (null, null, "未选择");
    }

    partial void OnSelectedFormatChanged(ExportFormatOption value)
    {
        OnPropertyChanged(nameof(IsExcel));
        OnPropertyChanged(nameof(IsImageOrPdf));
        Error = string.Empty;
    }

    partial void OnSelectedStyleChanged(ExportStyleOption value) =>
        OnPropertyChanged(nameof(IsReferenceStyle));

    partial void OnReferenceStyleNameChanged(string value)
    {
        _styleNameEdited = true;
        if (Styles.Count < 2) return;
        var name = string.IsNullOrWhiteSpace(value) ? ExportStylePreferences.DefaultName : value.Trim();
        Styles[1].Label = name;
    }

    /// <summary>只在用户真的改过名称时才落本机配置，避免每次导出都覆盖已有名称（也避免测试污染）。</summary>
    private bool _styleNameEdited;

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    public bool TryBuild(out WeaveExportRequest? request, out string error)
    {
        request = null;
        error = string.Empty;
        Error = string.Empty;

        var format = SelectedFormat.Format;
        if (format == WeaveExportFormat.Excel)
        {
            if (!IncludeMaster && !IncludeAssignment && !IncludeTeacherPosition &&
                !IncludeClassSheets && !IncludeTeacherSheets && !IncludeConflict)
            {
                error = "请至少勾选一个分页。";
                Error = error;
                return false;
            }

            var options = new ExportOptions
            {
                IncludeMasterSheet = IncludeMaster,
                IncludeAssignmentSheet = IncludeAssignment,
                IncludeTeacherPositionSheet = IncludeTeacherPosition,
                IncludeClassSheets = IncludeClassSheets,
                IncludeTeacherSheets = IncludeTeacherSheets,
                IncludeConflictSheet = IncludeConflict,
                Style = SelectedStyle.Style,
            };
            if (options.Style == WeaveExportStyle.Reference && _styleNameEdited)
                ExportStylePreferences.SaveName(ReferenceStyleName);
            request = new WeaveExportRequest(format, options, null, null, "全年级");
            return true;
        }

        var target = ResolveTarget();
        if (target.ClassId is null && target.TeacherId is null)
        {
            error = "请先在主窗口左侧选择一个班级或教师。";
            Error = error;
            return false;
        }

        request = new WeaveExportRequest(
            format, new ExportOptions { Style = WeaveExportStyle.Software },
            target.ClassId, target.TeacherId, target.Label);
        return true;
    }
}
