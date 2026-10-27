using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GradeWeave.Core.Models;

namespace GradeWeave.App;

/// <summary>活动编辑窗口返回：保存的活动，或请求删除。</summary>
public sealed record ActivityEditorResult(ActivityBlock? Activity, bool Delete);

public partial class ActivityEditorWindow : Window
{
    private readonly ActivityEditorViewModel? _viewModel;
    private bool _closeReady;
    private bool _guardOpen;

    public ActivityEditorWindow()
    {
        InitializeComponent();
    }

    public ActivityEditorWindow(WeaveProject project, ActivityBlock? existing) : this()
    {
        _viewModel = new ActivityEditorViewModel(project, existing);
        DataContext = _viewModel;
        Opened += (_, _) => ScreenFit.Clamp(this);
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        var activity = _viewModel.TryBuild(out var error);
        if (activity is null)
        {
            _viewModel.Error = error;
            return;
        }

        _closeReady = true;
        Close(new ActivityEditorResult(activity, false));
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.IsEditing == true)
        {
            _closeReady = true;
            Close(new ActivityEditorResult(null, true));
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
        if (_viewModel is null || _closeReady || _guardOpen || !_viewModel.IsDirty) return;

        e.Cancel = true;
        _guardOpen = true;
        _ = ConfirmDiscardAsync();
    }

    private async Task ConfirmDiscardAsync()
    {
        var discard = await EditorCloseGuard.ShowAsync(this, "活动改动尚未保存，放弃并关闭？");
        _guardOpen = false;
        if (discard)
        {
            _closeReady = true;
            Close();
        }
    }
}
