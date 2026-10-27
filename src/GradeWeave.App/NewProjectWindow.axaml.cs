using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TeachCraft.Shared;

namespace GradeWeave.App;

public partial class NewProjectWindow : Window
{
    private readonly NewProjectViewModel _viewModel = new();
    private bool _closeReady;
    private bool _guardOpen;

    public NewProjectWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Opened += (_, _) => WindowFit.Clamp(this);
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        var config = _viewModel.TryBuild(out var error);
        if (config is null)
        {
            _viewModel.Error = error;
            return;
        }

        _closeReady = true;
        Close(config);
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
        var discard = await EditorCloseGuard.ShowAsync(this, "「全新制作」的设置尚未创建工程，放弃并关闭？");
        _guardOpen = false;
        if (discard)
        {
            _closeReady = true;
            Close();
        }
    }
}
