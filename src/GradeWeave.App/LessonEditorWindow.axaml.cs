using Avalonia.Controls;
using Avalonia.Interactivity;
using GradeWeave.Core.Models;
using TeachCraft.Shared;

namespace GradeWeave.App;

/// <summary>手动放课的结果：学科 / 教师；Clear 为 true 时表示清空目标格。</summary>
public sealed record LessonEditorResult(
    string ClassId,
    int DayIndex,
    int Period,
    string SubjectId,
    string TeacherId,
    bool Clear = false);

public partial class LessonEditorWindow : Window
{
    private readonly LessonEditorViewModel _viewModel;

    public LessonEditorWindow()
        : this(new WeaveProject(), string.Empty, 1, 1)
    {
    }

    public LessonEditorWindow(WeaveProject project, string classId, int dayIndex, int period)
    {
        InitializeComponent();
        _viewModel = new LessonEditorViewModel(project, classId, dayIndex, period);
        DataContext = _viewModel;
        Opened += (_, _) => WindowFit.Clamp(this);
    }

    private void OnChoose(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LessonChoiceViewModel choice)
        {
            _viewModel.SetHint("没有选中学科，请再点一次。");
            return;
        }

        Close(new LessonEditorResult(
            _viewModel.ClassId,
            _viewModel.DayIndex,
            _viewModel.Period,
            choice.SubjectId,
            choice.TeacherId));
    }

    private void OnClear(object? sender, RoutedEventArgs e) =>
        Close(new LessonEditorResult(
            _viewModel.ClassId,
            _viewModel.DayIndex,
            _viewModel.Period,
            string.Empty,
            string.Empty,
            Clear: true));

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
