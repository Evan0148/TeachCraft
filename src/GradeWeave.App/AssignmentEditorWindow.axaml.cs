using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GradeWeave.Core.Models;
using TeachCraft.Shared;

namespace GradeWeave.App;

public partial class AssignmentEditorWindow : Window
{
    private readonly AssignmentEditorViewModel _viewModel;
    private bool _closeReady;
    private bool _guardOpen;

    public AssignmentEditorWindow()
    {
        InitializeComponent();
        _viewModel = new AssignmentEditorViewModel(new WeaveProject());
        DataContext = _viewModel;
        Opened += (_, _) => WindowFit.Clamp(this);
    }

    public AssignmentEditorWindow(WeaveProject project)
    {
        InitializeComponent();
        _viewModel = new AssignmentEditorViewModel(project);
        DataContext = _viewModel;
        Opened += (_, _) => WindowFit.Clamp(this);
    }

    /// <summary>设计走查：切到指定视角（0 名单 / 1 班级 / 2 教师 / 3 学科 / 4 体检），可顺便选中教师。</summary>
    public void SetViewForCapture(int mode, string? teacherName = null)
    {
        _viewModel.ViewMode = Math.Clamp(mode, 0, 4);
        if (!string.IsNullOrWhiteSpace(teacherName)) _viewModel.ShowTeacher(teacherName);
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var pending = _viewModel.CountPendingClassRemovals();
        if (pending.Classes > 0 && pending.Lessons > 0)
        {
            if (!await ConfirmDialog.ShowAsync(
                    this, "删除班级并保存？",
                    $"将删除 {pending.Classes} 个班级，并连同其已有 {pending.Lessons} 节课一起移除。此操作无法撤销。",
                    "删除并保存", "返回修改", danger: true))
                return;
        }

        if (_viewModel.Apply())
        {
            _closeReady = true;
            Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _closeReady = true;
        Close(null);
    }

    private void OnAssignTeacher(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AssignmentTeacherDraftViewModel teacher })
            Dispatcher.UIThread.Post(() => _viewModel.AssignTeacher(teacher));
    }

    private void OnRemoveTeacherFromRoster(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AssignmentTeacherDraftViewModel teacher })
            Dispatcher.UIThread.Post(() => _viewModel.RemoveTeacher(teacher));
    }

    private void OnTeacherCellClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: AssignmentTeacherCellViewModel cell })
            Dispatcher.UIThread.Post(() => _viewModel.ToggleTeacherAssignment(cell));
    }

    private void OnGoToSubject(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AssignmentGapItemViewModel gap })
            Dispatcher.UIThread.Post(() => _viewModel.ShowSubject(gap.SubjectId));
    }

    private void OnGoToTeacher(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AssignmentTeacherLoadItemViewModel load })
            Dispatcher.UIThread.Post(() => _viewModel.ShowTeacher(load.Name));
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeReady || _guardOpen || !_viewModel.IsDirty) return;

        e.Cancel = true;
        _guardOpen = true;
        _ = ConfirmDiscardAsync();
    }

    private async Task ConfirmDiscardAsync()
    {
        var discard = await EditorCloseGuard.ShowAsync(this, "分工改动尚未保存，放弃并关闭？");
        _guardOpen = false;
        if (discard)
        {
            _closeReady = true;
            Close();
        }
    }

    // 表头与行分别放在两个 ScrollViewer 里：行横向滚动时把偏移同步给表头，列始终对齐。
    private void OnRowsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta.X == 0 || sender is not ScrollViewer rows) return;
        HeaderScroll.Offset = HeaderScroll.Offset.WithX(rows.Offset.X);
    }

    private void OnTeacherRowsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta.X == 0 || sender is not ScrollViewer rows) return;
        TeacherHeaderScroll.Offset = TeacherHeaderScroll.Offset.WithX(rows.Offset.X);
    }
}
