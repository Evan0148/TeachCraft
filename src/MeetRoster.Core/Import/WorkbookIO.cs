using ClosedXML.Excel;

namespace MeetRoster.Core.Import;

/// <summary>
/// ClosedXML 工作簿打开的统一入口（Plan 4.1 / 4.4）：
/// stream 构造读完即释放句柄（文件被 Excel 占用时也能读），打开失败包装为中文可操作提示。
/// </summary>
internal static class WorkbookIO
{
    /// <summary>以共享读方式打开文件。</summary>
    public static FileStream OpenReadShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    /// <summary>从共享读流构造工作簿，失败时抛出中文 InvalidDataException。</summary>
    public static XLWorkbook OpenWorkbook(string path, Stream stream)
    {
        try
        {
            return new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException(
                $"无法读取这份工作簿（{Path.GetFileName(path)}）：文件可能已损坏、被密码保护，或不是有效的 .xlsx 文件。"
                + "请用 Excel 打开后另存为 .xlsx 再试。", ex);
        }
    }
}
