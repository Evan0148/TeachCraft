using Avalonia.Controls;
using Avalonia.Interactivity;
using GradeWeave.Core.Models;
using TeachCraft.Shared;

namespace GradeWeave.App;

public partial class ExportOptionsWindow : Window
{
    private readonly ExportOptionsViewModel _viewModel;

    public ExportOptionsWindow()
        : this(new WeaveProject(), null, null, false)
    {
    }

    public ExportOptionsWindow(
        WeaveProject project,
        SchoolClass? selectedClass,
        Teacher? selectedTeacher,
        bool teacherView)
    {
        InitializeComponent();
        _viewModel = new ExportOptionsViewModel(project, selectedClass, selectedTeacher, teacherView);
        DataContext = _viewModel;
        Opened += (_, _) => WindowFit.Clamp(this);
    }

    private void OnExport(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.TryBuild(out var request, out var error))
            Close(request);
        else
            _viewModel.Error = error;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
