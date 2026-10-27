using System;
using System.IO;
using System.Text.Json;

namespace TeachCraft.Shared;

/// <summary>
/// 异常 → 教师能读懂的一句话。
/// 原则：业务异常（ToolboxException / ClassBlockException 等）由抛出方给出人话，原样透传；
/// 未预期异常只给「做什么失败了 + 下一步怎么办」，不把 CLR / IO 原文抛到界面上。
/// </summary>
internal static class ErrorText
{
    /// <summary>把未预期异常翻译成一句可执行的提示。action 用动词短语，如「处理」「生成课程表」。</summary>
    public static string Describe(Exception ex, string action)
    {
        var reason = ex switch
        {
            UnauthorizedAccessException => "没有权限访问这个文件或文件夹，换一个位置再试。",
            DirectoryNotFoundException => "找不到目标文件夹，它可能已被移动或删除。",
            FileNotFoundException => "找不到文件，它可能已被移动或删除。",
            IOException io when IsFileLocked(io) => "文件正被其他程序占用，关闭后重试。",
            IOException => "读写文件时出错，请确认磁盘空间充足、文件未被占用。",
            JsonException => "工程文件内容无法识别，可能已损坏。",
            InvalidDataException => "文件格式不受支持，或内容不完整。",
            OutOfMemoryException => "内存不足，请关闭其他程序后重试。",
            _ => null,
        };

        return reason is not null
            ? $"{action}失败：{reason}"
            : $"{action}失败：请检查文件后重试；若反复出现，请把这份文件和操作步骤反馈给我们。";
    }

    private static bool IsFileLocked(IOException ex)
    {
        var text = ex.Message;
        return text.Contains("being used", StringComparison.OrdinalIgnoreCase)
               || text.Contains("used by another process", StringComparison.OrdinalIgnoreCase)
               || text.Contains("占用", StringComparison.Ordinal);
    }
}
