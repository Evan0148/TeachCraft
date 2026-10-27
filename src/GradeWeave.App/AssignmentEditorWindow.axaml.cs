using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GradeWeave.Core.Models;

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
    }

    public AssignmentEditorWindow(WeaveProject project)
    {
        InitializeComponent();
        _viewModel = new AssignmentEditorViewModel(project);
        DataContext = _viewModel;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
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
}
