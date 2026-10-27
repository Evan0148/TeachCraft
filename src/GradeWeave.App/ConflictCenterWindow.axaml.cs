using Avalonia.Controls;
using Avalonia.Interactivity;
using GradeWeave.Core.Models;
using TeachCraft.Shared;

namespace GradeWeave.App;

/// <summary>冲突聚合台：非模态窗口，定位时主窗口保持可操作。</summary>
public partial class ConflictCenterWindow : Window
{
    private readonly GradeWeaveViewModel? _owner;
    private readonly Action<WeaveConflict>? _locate;
    private readonly ConflictCenterViewModel? _viewModel;

    public ConflictCenterWindow()
    {
        InitializeComponent();
        Opened += (_, _) => WindowFit.Clamp(this);
    }

    public ConflictCenterWindow(GradeWeaveViewModel owner, Action<WeaveConflict> locate) : this()
    {
        _owner = owner;
        _locate = locate;
        _viewModel = new ConflictCenterViewModel(owner.Project, owner.ConflictModels);
        DataContext = _viewModel;
    }

    public void RefreshConflicts()
    {
        if (_viewModel is null || _owner is null) return;
        _viewModel.Refresh(_owner.ConflictModels);
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => RefreshConflicts();

    private void OnLocateConflict(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ConflictItemViewModel item })
            _locate?.Invoke(item.Conflict);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
