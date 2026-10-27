using System;
using System.Runtime.InteropServices;

namespace TeachCraft.Shared;

/// <summary>
/// 系统减弱动效偏好：Windows 读取「动画效果」（SPI_GETCLIENTAREAANIMATION），
/// macOS 读取 NSWorkspace.accessibilityDisplayShouldReduceMotion（未实机验证，
/// 按 Apple 文档实现并优雅降级），Linux 暂无统一探测入口，返回 false（未实机验证）。
/// 结果在进程内缓存，仅用于关闭装饰性动效（抽屉横移、忙碌旋转、入场缩放），
/// 不影响功能性反馈（进度条、状态文字）。
/// </summary>
internal static class MotionPreference
{
    private const uint SpiGetClientAreaAnimation = 0x1042;

    private static bool? _reduceMotion;

    public static bool ReduceMotion => _reduceMotion ??= Detect();

    private static bool Detect()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // 「动画效果」关闭 → 客户区动画被系统禁用，等价于用户要求减弱动效。
                return SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var clientAnimation, 0)
                       && !clientAnimation;
            }

            if (OperatingSystem.IsMacOS())
            {
                return IsMacReduceMotionEnabled();
            }
        }
        catch
        {
            // 探测失败按「未开启减弱动效」处理，绝不影响启动。
        }

        return false;
    }

    private static bool IsMacReduceMotionEnabled()
    {
        var workspaceClass = objc_getClass("NSWorkspace");
        if (workspaceClass == IntPtr.Zero) return false;

        var shared = objc_msgSend_IntPtr(workspaceClass, sel_registerName("sharedWorkspace"));
        if (shared == IntPtr.Zero) return false;

        return objc_msgSend_Bool(shared, sel_registerName("accessibilityDisplayShouldReduceMotion")) != 0;
    }

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out bool pvParam, uint fWinIni);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern byte objc_msgSend_Bool(IntPtr receiver, IntPtr selector);
}
