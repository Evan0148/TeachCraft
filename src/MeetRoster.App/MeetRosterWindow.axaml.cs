using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using TeachCraft.Shared;

namespace MeetRoster.App;

public partial class MeetRosterWindow : Window
{
    public MeetRosterWindow() : this(new MeetRosterViewModel())
    {
    }

    public MeetRosterWindow(MeetRosterViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Opened += (_, _) => WindowFit.Clamp(this);
    }

    private MeetRosterViewModel ViewModel => (MeetRosterViewModel)DataContext!;

    /// <summary>设计走查用：把窗口渲染成 PNG。</summary>
    public void CaptureTo(string path)
    {
        var pixelSize = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        bitmap.Render(this);
        using var stream = File.Create(path);
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }

    private void OnImportRoster(object? sender, RoutedEventArgs e) => ViewModel.ImportPlaceholder();

    private void OnExportRoster(object? sender, RoutedEventArgs e) => ViewModel.ExportPlaceholder();

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

    public void ShowScheduleForCapture(int dayIndex = 1, string? categoryKey = null)
    {
        ViewModel.SelectSection("schedule");

        if (dayIndex >= 1 && dayIndex <= ViewModel.ScheduleDays.Count)
            ViewModel.SelectDay(ViewModel.ScheduleDays[dayIndex - 1]);

        if (!string.IsNullOrWhiteSpace(categoryKey))
        {
            var category = ViewModel.ScheduleCategories.FirstOrDefault(c => c.Key == categoryKey);
            if (category is not null) ViewModel.SelectScheduleCategory(category);
        }
    }

    private void OnAddAthlete(object? sender, RoutedEventArgs e) => ViewModel.AddAthlete();

    private void OnRemoveAthlete(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AthleteRowViewModel row })
            ViewModel.RemoveAthlete(row);
    }
}
