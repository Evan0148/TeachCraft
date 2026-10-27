using GradeWeave.Core.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GradeWeave.Core.Export;

/// <summary>打印 PDF：高分辨率 PNG 嵌入 A4，避免中文字体内嵌导致体积膨胀。</summary>
public sealed class WeavePdfExporter
{
    public void ExportClassPdf(WeaveProject project, string classId, string path)
    {
        try
        {
            WeaveExportSupport.Prepare(project, path);
            var png = new WeaveImageRenderer().RenderClassPng(project, classId, 3);
            WritePdf(png, path);
        }
        catch (GradeWeaveException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new GradeWeaveException("导出班级课表 PDF 失败，请检查保存位置后重试。", ex);
        }
    }

    public void ExportTeacherPdf(WeaveProject project, string teacherId, string path)
    {
        try
        {
            WeaveExportSupport.Prepare(project, path);
            var png = new WeaveImageRenderer().RenderTeacherPng(project, teacherId, 3);
            WritePdf(png, path);
        }
        catch (GradeWeaveException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new GradeWeaveException("导出教师课表 PDF 失败，请检查保存位置后重试。", ex);
        }
    }

    private static void WritePdf(byte[] png, string path)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(18, Unit.Millimetre);
                page.PageColor(Colors.White);
                page.Content().Image(png).FitArea();
            });
        }).GeneratePdf(path);
    }
}
