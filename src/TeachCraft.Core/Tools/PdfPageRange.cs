namespace TeachCraft.Core.Tools;

/// <summary>工具箱可预期的失败（文件损坏、格式不支持等），消息直接面向老师展示。</summary>
public sealed class ToolboxException : Exception
{
    public ToolboxException(string message) : base(message) { }
}

/// <summary>页码范围解析：支持 “1-3,5,8-10”。</summary>
public static class PdfPageRange
{
    public static IReadOnlyList<int> Parse(string? text, int pageCount)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Enumerable.Range(1, pageCount).ToList();

        var pages = new SortedSet<int>();
        foreach (var raw in text.Split(new[] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            if (part.Length == 0) continue;

            var dash = part.IndexOfAny(new[] { '-', '–', '—', '~' });
            if (dash < 0)
            {
                if (!int.TryParse(part, out var single) || single < 1 || single > pageCount)
                    throw new ToolboxException($"页码 “{part}” 超出范围（共 {pageCount} 页）。");
                pages.Add(single);
                continue;
            }

            var left = part[..dash].Trim();
            var right = part[(dash + 1)..].Trim();
            var start = string.IsNullOrEmpty(left) ? 1 : int.TryParse(left, out var s) ? s : -1;
            var end = string.IsNullOrEmpty(right) ? pageCount : int.TryParse(right, out var e) ? e : -1;

            if (start < 1 || end < 1 || start > pageCount || end > pageCount || start > end)
                throw new ToolboxException($"页码范围 “{part}” 无效（共 {pageCount} 页）。");

            for (var i = start; i <= end; i++) pages.Add(i);
        }

        if (pages.Count == 0)
            throw new ToolboxException("没有选中任何页码。");

        return pages.ToList();
    }

    /// <summary>把页码列表压缩成 “1-3,5” 这样的可读文本。</summary>
    public static string Describe(IReadOnlyList<int> pages)
    {
        if (pages.Count == 0) return "无";
        var ordered = pages.OrderBy(x => x).ToList();
        var parts = new List<string>();
        var start = ordered[0];
        var prev = start;
        foreach (var page in ordered.Skip(1))
        {
            if (page == prev + 1) { prev = page; continue; }
            parts.Add(start == prev ? $"{start}" : $"{start}-{prev}");
            start = prev = page;
        }
        parts.Add(start == prev ? $"{start}" : $"{start}-{prev}");
        return string.Join(",", parts);
    }
}
