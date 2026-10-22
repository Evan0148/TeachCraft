namespace TeachCraft.App.ViewModels;

/// <summary>多文件工具里的一行：序号 + 文件名，可上下移动或移除。</summary>
public sealed class FileEntryViewModel
{
    public FileEntryViewModel(int number, string path)
    {
        Number = number;
        Path = path;
    }

    public int Number { get; }
    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
}
