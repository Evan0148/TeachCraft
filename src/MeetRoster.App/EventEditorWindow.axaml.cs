using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using MeetRoster.Core.Models;
using TeachCraft.Shared;

namespace MeetRoster.App;

/// <summary>选择项目的结果；Clear 为 true 时清空，Events 为多选结果（团体或单项分组）。</summary>
public sealed record EventEditorResult(
    string SlotKey,
    string? EventName,
    bool Clear = false,
    IReadOnlyList<string>? Events = null);

public partial class EventEditorWindow : Window
{
    private readonly EventEditorViewModel _viewModel;

    public EventEditorResult? Result { get; private set; }

    public EventEditorWindow()
        : this(new MeetClass { Name = "示例班级" }, new AthleteRowViewModel(new Athlete()), AthleteRowViewModel.SlotIndividual1)
    {
    }

    public EventEditorWindow(MeetClass meetClass, AthleteRowViewModel row, string slotKey)
    {
        InitializeComponent();
        _viewModel = new EventEditorViewModel(meetClass, row, slotKey);
        DataContext = _viewModel;
        Opened += (_, _) =>
        {
            WindowFit.Clamp(this);
            // 打开即落焦搜索框：多数场景是先搜项目再点选。
            SearchBox.Focus();
        };
        // 单选模式下回车 = 选第一项（「完成」按钮只在多选模式存在，搜索后回车不应是死键）。
        KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter || _viewModel.IsGroupMode) return;

        var first = _viewModel.Choices.FirstOrDefault();
        if (first is null) return;

        e.Handled = true;
        Result = new EventEditorResult(_viewModel.SlotKey, first.EventName);
        Close();
    }

    /// <summary>设计走查用：把窗口渲染成 PNG。</summary>
    public void CaptureTo(string path)
    {
        var pixelSize = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        bitmap.Render(this);
        using var stream = File.Create(path);
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }

    private void OnChoose(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not EventChoiceViewModel choice)
            return;

        if (_viewModel.IsGroupMode)
        {
            _viewModel.Toggle(choice);
            return;
        }

        Result = new EventEditorResult(_viewModel.SlotKey, choice.EventName);
        Close();
    }

    private void OnDone(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsGroupMode) return;
        Result = new EventEditorResult(
            _viewModel.SlotKey,
            null,
            Events: _viewModel.GetSelection());
        Close();
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsGroupMode)
        {
            _viewModel.ClearSelection();
            return;
        }

        Result = new EventEditorResult(_viewModel.SlotKey, null, Clear: true);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
