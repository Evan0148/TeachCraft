using System;
using Avalonia;

namespace TeachCraft.Shared;

/// <summary>
/// 拖拽手感共享工具：速度采样、Apple 式指数衰减投影与 iOS 橡皮筋钳制。
/// 供归位座位卡（SeatGridWindow）与织序课程格（GradeWeaveWindow）两处拖拽复用。
/// 本文件由 Directory.Build.props 以链接方式编入各 App 项目，internal 避免跨程序集类型冲突。
/// </summary>
internal static class DragPhysics
{
    /// <summary>Apple UIScrollView 的 normal decelerationRate：慢放落在近处，快甩滑得更远。</summary>
    public const double Decay = 0.998;

    private const double RubberbandConstant = 0.55;

    /// <summary>速度投影：把松手瞬间的速度（px/s）换算成惯性滑行距离（px）。</summary>
    public static double ProjectDistance(double speedPxPerSecond) =>
        speedPxPerSecond / 1000 * Decay / (1 - Decay);

    /// <summary>投影落点：当前点沿速度方向滑行 project(v, d) 的距离；零速原样返回。</summary>
    public static Point ProjectedPoint(Point current, Vector velocity)
    {
        var speed = velocity.Length;
        if (speed < 1e-6) return current;
        return current + velocity * (ProjectDistance(speed) / speed);
    }

    /// <summary>iOS 橡皮筋：越界偏移 o 被压回 dim 内，越界越深阻力越大，不产生硬截断。</summary>
    public static double Rubberband(double offset, double dimension)
    {
        if (dimension <= 0) return 0;
        return offset * dimension * RubberbandConstant / (dimension + RubberbandConstant * Math.Abs(offset));
    }

    /// <summary>越出 bounds 的点按橡皮筋规律压回边界附近；界内点原样返回。</summary>
    public static Point RubberbandPoint(Point point, Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return point;

        var x = point.X;
        var y = point.Y;
        if (x < bounds.Left) x = bounds.Left - Rubberband(bounds.Left - x, bounds.Width);
        else if (x > bounds.Right) x = bounds.Right + Rubberband(x - bounds.Right, bounds.Width);
        if (y < bounds.Top) y = bounds.Top - Rubberband(bounds.Top - y, bounds.Height);
        else if (y > bounds.Bottom) y = bounds.Bottom + Rubberband(y - bounds.Bottom, bounds.Height);
        return new Point(x, y);
    }
}

/// <summary>
/// 最近 N 帧指针采样环形缓冲，松手时估算释放速度。
/// 停顿超过 0.1s 视为速度归零（用户停在原地后再松手，不该有惯性）。
/// </summary>
internal sealed class VelocitySampler
{
    private const int Capacity = 5;
    private const double StaleAfterSeconds = 0.1;

    private readonly (Point Point, double Time)[] _samples = new (Point, double)[Capacity];
    private int _count;
    private int _head;

    /// <summary>清空采样。nowSeconds 用 Stopwatch.GetTimestamp() 换算的秒。</summary>
    public void Begin()
    {
        _count = 0;
        _head = 0;
    }

    public void Add(Point point, double nowSeconds)
    {
        _samples[_head] = (point, nowSeconds);
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    /// <summary>速度（px/s）。样本不足、时间跨度为 0 或已停顿过久时返回零向量。</summary>
    public Vector Velocity(double nowSeconds)
    {
        if (_count < 2 || nowSeconds - _samples[(_head + Capacity - 1) % Capacity].Time > StaleAfterSeconds)
            return default;

        var oldest = _count < Capacity ? _samples[0] : _samples[_head];
        var newest = _samples[(_head + Capacity - 1) % Capacity];
        var dt = newest.Time - oldest.Time;
        if (dt <= 0) return default;

        // Avalonia 的 Point - Point 走分量重载，速度必须显式按 Vector 构造。
        return new Vector(newest.Point.X - oldest.Point.X, newest.Point.Y - oldest.Point.Y) / dt;
    }
}
