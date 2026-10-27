using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ClassBlock.Core;
using ClassBlock.Core.Timetable;

namespace ClassBlock.App;

public partial class TimetableTemplateEditorWindow : Window
{
    /// <summary>保存成功时带回模板编号；关闭/删除时为 null。</summary>
    public string? SavedTemplateId { get; private set; }

    private bool _confirmDelete;

    public TimetableTemplateEditorWindow()
        : this(new TimetableTemplate(), startAsCopy: false)
    {
    }

    public TimetableTemplateEditorWindow(TimetableTemplate source, bool startAsCopy = false)
    {
        InitializeComponent();
        DataContext = new TimetableTemplateEditorViewModel(source, startAsCopy);
    }

    private TimetableTemplateEditorViewModel ViewModel => (TimetableTemplateEditorViewModel)DataContext!;

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanSave)
        {
            SetEditorStatus("请先填写模板名称并至少保留一行。");
            return;
        }

        try
        {
            var template = ViewModel.BuildTemplate();
            TimetableTemplateIO.SaveCustom(template);
            SavedTemplateId = template.Id;
            Close();
        }
        catch (ClassBlockException ex)
        {
            SetEditorStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetEditorStatus($"保存模板失败：{ex.Message}");
        }
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (!_confirmDelete)
        {
            _confirmDelete = true;
            DeleteButton.Content = "确认删除";
            return;
        }

        try
        {
            TimetableTemplateIO.DeleteCustom(ViewModel.TemplateId);
            Close();
        }
        catch (Exception ex)
        {
            SetEditorStatus($"删除模板失败：{ex.Message}");
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void SetEditorStatus(string message)
    {
        EditorStatus.Text = message;
        EditorStatus.Foreground = new SolidColorBrush(Color.Parse("#C6613F"));
    }
}
