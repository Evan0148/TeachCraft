using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using MeetRoster.Core.Import;
using TeachCraft.Shared;

namespace MeetRoster.App;

public partial class MeetRosterWindow : Window
{
    private static readonly FilePickerFileType ExcelFilter = new("Excel 报名表")
    {
        Patterns = ["*.xlsx"],
    };

    private static readonly FilePickerFileType RosterFilter = new("学生名单")
    {
        Patterns = ["*.xlsx"],
    };

    private bool _closeConfirmed;

    public MeetRosterWindow() : this(new MeetRosterViewModel())
    {
    }

    public MeetRosterWindow(MeetRosterViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Opened += (_, _) =>
        {
            WindowFit.Clamp(this);
            UpdateCompact();
        };
        SizeChanged += (_, _) => UpdateCompact();
        KeyDown += OnWindowKeyDown;
    }

    private MeetRosterViewModel ViewModel => (MeetRosterViewModel)DataContext!;

    /// <summary>窗口收窄到 1240 以下时隐藏右侧概览，避免报名表被挤成多行。</summary>
    private void UpdateCompact() => ViewModel.IsCompact = Bounds.Width < 1240;

    /// <summary>走查用：把每行的高度与行内控件位置写成文本，用于核对垂直居中。</summary>
    public void DumpRowMetrics(string path)
    {
        var lines = new List<string>();
        var rows = this.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("athlete-row"))
            .Take(6)
            .ToList();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowTop = row.TranslatePoint(new Point(0, 0), this)?.Y ?? 0;
            lines.Add($"row[{i}] top={rowTop:F1} h={row.Bounds.Height:F1}");

            foreach (var child in row.GetVisualDescendants().OfType<Control>())
            {
                var tag = child switch
                {
                    ComboBox => "combo",
                    TextBox => "textbox",
                    Button b when b.Classes.Contains("event-cell") => "chip",
                    Button b when b.Classes.Contains("mini") => "del",
                    TextBlock t when t.Classes.Contains("table-head") => "head",
                    _ => null,
                };
                if (tag is null || child.Bounds.Height <= 0) continue;
                var top = child.TranslatePoint(new Point(0, 0), this)?.Y;
                if (top is null) continue;
                var rel = top.Value - rowTop;
                lines.Add($"  {tag} y={rel:F1} h={child.Bounds.Height:F1} " +
                          $"gapTop={rel:F1} gapBottom={row.Bounds.Height - rel - child.Bounds.Height:F1}");
            }
        }

        File.WriteAllText(path, string.Join(Environment.NewLine, lines));
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // 有未保存的改动才拦：没有改动时关闭不会丢任何东西。
        if (!_closeConfirmed && ViewModel.IsDirty)
        {
            e.Cancel = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                if (await ConfirmDialog.ShowAsync(
                        this, "关闭竞名？",
                        $"当前有未保存的报名表修改（{ViewModel.AllClasses.Count} 个班），关闭后不会保留。可先按「保存」(Ctrl+S) 或「另存为」留档。",
                        "放弃并关闭", "继续编辑", danger: true))
                {
                    _closeConfirmed = true;
                    Close();
                }
            });
        }

        base.OnClosing(e);
    }

    /// <summary>设计走查用：把窗口渲染成 PNG；scale 用于放大取证明细。</summary>
    public void CaptureTo(string path, double scale = 1)
    {
        _closeConfirmed = true;
        var pixelSize = new PixelSize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scale, 96 * scale));
        bitmap.Render(this);
        using var stream = File.Create(path);
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }

    private async void OnImportRoster(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入班级报名表",
                AllowMultiple = true,
                FileTypeFilter = [ExcelFilter],
            });

            var paths = files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToList();

            if (paths.Count == 0) return;

            var existingAthletes = ViewModel.AllClasses.Sum(c => c.AthleteCount);
            if (ViewModel.IsDemo)
            {
                if (!await ConfirmDialog.ShowAsync(
                        this, "导入报名表？",
                        $"将导入 {paths.Count} 份报名表并替换当前示例数据，示例班级与排程不会保留。",
                        "导入并替换示例", "取消", danger: true))
                    return;
            }
            else
            {
                // 廉价预读各文件的"参赛单位"，把"新增 / 替换 / 保留"算给用户看；预读失败按新增对待。
                var peeked = paths
                    .Select(path => (Path: path, Name: RegistrationWorkbookImporter.PeekClassName(path)))
                    .ToList();
                var existingNames = ViewModel.AllClasses.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
                var replaceNames = peeked
                    .Where(p => p.Name is not null && existingNames.Contains(p.Name))
                    .Select(p => p.Name!)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();
                var newCount = peeked.Count(p => p.Name is null || !existingNames.Contains(p.Name));

                if (existingAthletes > 0 && !await ConfirmDialog.ShowAsync(
                        this, "导入报名表？",
                        $"本次导入 {paths.Count} 份：新增 {newCount} 个班{SummarizeNewClasses(peeked, existingNames)}；"
                        + $"替换 {replaceNames.Count} 个同名班{SummarizeClassNames(replaceNames)}；"
                        + $"其余 {Math.Max(ViewModel.AllClasses.Count - replaceNames.Count, 0)} 个现有班原样保留。"
                        + "同名班级将按新文件整体替换。",
                        "导入报名表", "取消", danger: true))
                    return;
            }

            await ViewModel.ImportRegistrationFilesAsync(paths);
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus(ErrorText.Describe(ex, "导入报名表"), true);
        }
    }

    private async void OnImportSchedule(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入赛程工作簿",
                AllowMultiple = false,
                FileTypeFilter = [ExcelFilter],
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;

            if (ViewModel.HasScheduleDays && !await ConfirmDialog.ShowAsync(
                    this, "替换当前赛程？",
                    "导入新赛程会替换当前赛程表与分组条目。",
                    "替换赛程", "取消", danger: true))
                return;

            await ViewModel.ImportScheduleFileAsync(path);
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus(ErrorText.Describe(ex, "导入赛程"), true);
        }
    }

    private async void OnExportRoster(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "导出报名表到文件夹",
                AllowMultiple = false,
            });

            var directory = folders.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(directory)) return;

            var targets = ViewModel.AllClasses
                .Where(c => !string.IsNullOrWhiteSpace(c.Source.SourcePath))
                .Select(c => Path.Combine(directory, c.Name + "运动会报名表.xlsx"))
                .ToList();
            var overwrite = targets.Count(File.Exists);

            if (overwrite > 0 && !await ConfirmDialog.ShowAsync(
                    this, "覆盖同名报名表？",
                    $"目标文件夹里已有 {overwrite} 份同名报名表，继续导出会直接覆盖它们。",
                    "覆盖导出", "取消", danger: true))
                return;

            await ViewModel.ExportRegistrationFilesAsync(directory);
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus(ErrorText.Describe(ex, "导出报名表"), true);
        }
    }

    private void OnClearHighlight(object? sender, RoutedEventArgs e) =>
        ViewModel.ClearScheduleHighlight();

    private void OnSelectRegistration(object? sender, RoutedEventArgs e) =>
        ViewModel.SelectSection("registration");

    private void OnSelectSchedule(object? sender, RoutedEventArgs e) =>
        ViewModel.SelectSection("schedule");

    private void OnSelectDay(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ScheduleDayItemViewModel day })
            ViewModel.SelectDay(day);
    }

    private void OnSelectCategory(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ScheduleCategoryItemViewModel category })
            ViewModel.SelectScheduleCategory(category);
    }

    public async Task LoadRegistrationForCaptureAsync(string path)
    {
        _closeConfirmed = true;
        await ViewModel.ImportRegistrationFilesAsync([path]);
    }

    public async Task LoadScheduleForCaptureAsync(string path)
    {
        _closeConfirmed = true;
        await ViewModel.ImportScheduleFileAsync(path);
    }

    /// <summary>走查用：删掉指定姓名的行，模拟“用户改完超限”后的界面。</summary>
    public void SimulateRemoveForCapture(params string[] names)
    {
        var target = new HashSet<string>(names, StringComparer.Ordinal);
        var rows = ViewModel.SelectedClass?.Athletes
            .Where(r => target.Contains(r.Name))
            .ToList() ?? [];
        foreach (var row in rows)
            ViewModel.RemoveAthlete(row);

        if (rows.Count > 0)
            ViewModel.SetStatus($"走查：已移除 {rows.Count} 行");
    }

    public void LoadDemoForCapture()
    {
        _closeConfirmed = true;
        ViewModel.LoadDemoData();
    }

    /// <summary>截图走查用：为当前班级第一行打开「选择项目」弹窗。</summary>
    public EventEditorWindow? OpenEventDialogForCapture(string slotKey = EventEditorViewModel.IndividualGroupKey)
    {
        var row = ViewModel.SelectedClass?.Athletes.FirstOrDefault();
        var meetClass = ViewModel.SelectedClass?.Source;
        if (row is null || meetClass is null) return null;

        return new EventEditorWindow(meetClass, row, slotKey);
    }

    public void ShowScheduleForCapture(int dayIndex = 1, string? categoryKey = null, string? highlightClass = null)
    {
        ViewModel.SelectSection("schedule");

        if (dayIndex >= 1 && dayIndex <= ViewModel.ScheduleDays.Count)
            ViewModel.SelectDay(ViewModel.ScheduleDays[dayIndex - 1]);

        if (!string.IsNullOrWhiteSpace(categoryKey))
        {
            var category = ViewModel.ScheduleCategories.FirstOrDefault(c => c.Key == categoryKey);
            if (category is not null) ViewModel.SelectScheduleCategory(category);
        }

        if (!string.IsNullOrWhiteSpace(highlightClass))
        {
            ViewModel.ScheduleHighlightClass = string.Equals(highlightClass, "first", StringComparison.OrdinalIgnoreCase)
                ? ViewModel.AllClasses.FirstOrDefault()
                : ViewModel.AllClasses.FirstOrDefault(c => c.Name == highlightClass)
                  ?? ViewModel.AllClasses.FirstOrDefault();
        }
    }

    /// <summary>导入学生名单：之后「添加运动员」改为从名单挑选。</summary>
    private async void OnImportRosterList(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入学生名单",
                AllowMultiple = false,
                FileTypeFilter = [RosterFilter],
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;

            await ViewModel.ImportRosterFileAsync(path);
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus(ErrorText.Describe(ex, "导入名单"), true);
        }
    }

    /// <summary>
    /// 添加运动员：已导入名单时弹出名单挑选窗（与「选择项目」同构，可多选）；
    /// 没有名单时维持旧行为，直接加一行空白。
    /// </summary>
    private async void OnAddAthlete(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedClass is null)
        {
            ViewModel.SetStatus("先导入或选择一个班级，再添加运动员");
            return;
        }

        if (!ViewModel.HasRoster)
        {
            ViewModel.AddAthlete();
            return;
        }

        var dialog = new RosterPickWindow(new RosterPickViewModel(
            ViewModel.BuildRosterCandidates(), ViewModel.SelectedClass.Name));
        await dialog.ShowDialog(this);

        if (dialog.Picked.Count == 0) return;
        ViewModel.AddAthletesFromRoster(dialog.Picked);
    }

    private async void OnRemoveAthlete(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AthleteRowViewModel row }) return;

        var hasContent = !string.IsNullOrWhiteSpace(row.Name) ||
                         AthleteRowViewModel.TeamSlots.Any(s => !string.IsNullOrWhiteSpace(row.RawEvent(s))) ||
                         AthleteRowViewModel.IndividualSlots.Any(s => !string.IsNullOrWhiteSpace(row.RawEvent(s)));

        if (hasContent && !await ConfirmDialog.ShowAsync(
                this, "删除这一行？",
                $"将删除「{(string.IsNullOrWhiteSpace(row.Name) ? "未命名运动员" : row.Name)}」及其已报项目，删除后需要重新填写。",
                "删除", "取消", danger: true))
            return;

        ViewModel.RemoveAthlete(row);
    }

    private void OnClearClassSearch(object? sender, RoutedEventArgs e) => ViewModel.ClearClassSearch();

    private void OnDismissImportNotices(object? sender, RoutedEventArgs e) =>
        ViewModel.DismissImportNotices();

    private async void OnSaveRoster(object? sender, RoutedEventArgs e) => await SaveAsync();

    /// <summary>Ctrl+S 保存到原文件。</summary>
    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.S || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        e.Handled = true;
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        if (ViewModel.IsBusy) return;
        if (!ViewModel.CanSave)
        {
            ViewModel.SetStatus(ViewModel.IsDirty
                ? ViewModel.DescribeUnsavableClasses()
                : "没有需要保存的改动");
            return;
        }

        try
        {
            await ViewModel.SaveToSourceAsync();
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus(ErrorText.Describe(ex, "保存报名表"), true);
        }
    }

    /// <summary>性别变了以后，把新性别里不存在的项目摘掉并说明原因。</summary>
    private void OnGenderAdjusted(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: AthleteRowViewModel row }) return;
        if (string.IsNullOrWhiteSpace(row.LastGenderAdjustment)) return;

        ViewModel.SetStatus($"{DisplayName(row)}：{row.LastGenderAdjustment}");
    }

    private void OnNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: AthleteRowViewModel row }) return;
        var name = row.Name;
        if (string.IsNullOrWhiteSpace(name) || name == name.Trim()) return;

        row.Name = name.Trim();
    }

    private async void OnEditEvent(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string slotKey } button)
            return;

        var row = button.Tag as AthleteRowViewModel
                  ?? button.DataContext as AthleteRowViewModel;
        var meetClass = ViewModel.SelectedClass?.Source;
        if (row is null || meetClass is null) return;

        var dialog = new EventEditorWindow(meetClass, row, slotKey);
        await dialog.ShowDialog(this);

        if (dialog.Result is not { } result) return;

        if (slotKey is EventEditorViewModel.IndividualGroupKey or EventEditorViewModel.TeamGroupKey)
        {
            var events = result.Events ?? [];
            if (slotKey == EventEditorViewModel.TeamGroupKey)
                row.SetTeamEvents(events);
            else
                row.SetIndividualEvents(events);

            ViewModel.SetStatus(events.Count == 0
                ? $"已清空 {DisplayName(row)} 的{(slotKey == EventEditorViewModel.TeamGroupKey ? "团体项目" : "比赛项目")}"
                : $"已为 {DisplayName(row)} 选择 {string.Join("、", events)}");
            return;
        }

        if (result.Clear)
        {
            row.SetEvent(result.SlotKey, string.Empty);
            ViewModel.SetStatus($"已清空 {DisplayName(row)} 的项目格");
            return;
        }

        if (string.IsNullOrWhiteSpace(result.EventName)) return;
        row.SetEvent(result.SlotKey, result.EventName);
        ViewModel.SetStatus($"已为 {DisplayName(row)} 选入「{result.EventName}」");
    }

    private static string DisplayName(AthleteRowViewModel row) =>
        string.IsNullOrWhiteSpace(row.Name) ? "未命名运动员" : row.Name;

    /// <summary>班级名列表的折叠展示：最多点名 5 个，其余合并为计数。</summary>
    private static string SummarizeClassNames(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return string.Empty;
        var shown = string.Join("、", names.Take(5));
        return $"（{shown}{(names.Count > 5 ? $" 等 {names.Count} 个班" : "")}）";
    }

    /// <summary>新增班的预读名单（预读失败的文件不计入点名，只算进数量）。</summary>
    private static string SummarizeNewClasses(
        IReadOnlyList<(string Path, string? Name)> peeked, IReadOnlySet<string> existingNames)
    {
        var names = peeked
            .Where(p => p.Name is not null && !existingNames.Contains(p.Name))
            .Select(p => p.Name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        return SummarizeClassNames(names);
    }
}
