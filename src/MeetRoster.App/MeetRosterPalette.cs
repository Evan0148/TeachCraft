using Avalonia.Media;

namespace MeetRoster.App;

/// <summary>
/// 竞名代码侧用色的唯一定义处（Plan U-4）：ViewModel 里的 Brush 一律引用这里，
/// 不再散落 Color.Parse；语义与 App.axaml 的 InkBrush/MutedBrush 等令牌一致。
/// </summary>
public static class MeetRosterPalette
{
    // 状态栏三色
    public static readonly IBrush Success = New("#5A7A4B");   // MossBrush
    public static readonly IBrush Neutral = New("#6F6A5E");   // MutedBrush
    public static readonly IBrush Error = New("#A34A36");     // AccentDeepBrush

    // 单项人数统计
    public static readonly IBrush Muted = New("#8A8476");
    public static readonly IBrush Accent = New("#A34A36");
    public static readonly IBrush Moss = New("#5A7A4B");

    // 报名体检
    public static readonly IBrush Warning = New("#A07A3C");

    // 赛程分类（径赛沿用印章红；Tint 为对应色的低透明度版本）
    public static readonly IBrush Track = New("#A34A36");
    public static readonly IBrush Field = New("#5A7A4B");
    public static readonly IBrush Team = New("#8A6A3C");
    public static readonly IBrush Fun = New("#4F6D7A");
    public static readonly IBrush Ceremony = New("#7C7668");
    public static readonly IBrush TrackTint = New("#14A34A36");
    public static readonly IBrush FieldTint = New("#145A7A4B");
    public static readonly IBrush TeamTint = New("#148A6A3C");
    public static readonly IBrush FunTint = New("#144F6D7A");
    public static readonly IBrush CeremonyTint = New("#127C7668");

    private static IBrush New(string color) => new SolidColorBrush(Color.Parse(color));
}
