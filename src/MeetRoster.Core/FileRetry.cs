using System.Threading;

namespace MeetRoster.Core;

/// <summary>
/// 文件操作重试：Windows 的实时扫描 / 索引服务会短暂锁定刚写完或刚改名的文件，
/// 备份与替换偶发 IOException（“文件正被另一个进程使用”）。
/// 按 100/200/300ms 退避重试，给扫描让路；重试耗尽后异常照常抛出。
/// </summary>
public static class FileRetry
{
    public static void Run(Action action, int attempts = 8)
    {
        ArgumentNullException.ThrowIfNull(action);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(150 * attempt);
            }
        }
    }
}
