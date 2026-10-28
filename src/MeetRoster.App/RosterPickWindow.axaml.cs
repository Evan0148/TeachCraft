using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MeetRoster.Core.Import;
using TeachCraft.Shared;

namespace MeetRoster.App;

/// <summary>名单挑选窗口：与「选择项目」同构（搜索 + 点选 + 完成），支持一次勾选多人。</summary>
public partial class RosterPickWindow : Window
{
    private readonly RosterPickViewModel _viewModel;

    /// <summary>用户点「完成」时选中的名单条目；取消时为空。</summary>
    public IReadOnlyList<RosterEntry> Picked { get; private set; } = [];

    public RosterPickWindow()
        : this(new RosterPickViewModel([], "示例班级"))
    {
    }

    public RosterPickWindow(RosterPickViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Opened += (_, _) =>
        {
            WindowFit.Clamp(this);
            SearchBox.Focus();
        };
    }

    private void OnPick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not RosterCandidateViewModel candidate) return;
        _viewModel.Toggle(candidate);
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e) => _viewModel.SelectAllVisible();

    private void OnClear(object? sender, RoutedEventArgs e) => _viewModel.ClearSelection();

    private void OnDone(object? sender, RoutedEventArgs e)
    {
        Picked = _viewModel.GetPicked();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
