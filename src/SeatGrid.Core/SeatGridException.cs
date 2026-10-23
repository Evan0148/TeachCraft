using System;

namespace SeatGrid.Core;

/// <summary>面向老师的可读错误，UI 直接展示 Message。</summary>
public sealed class SeatGridException : Exception
{
    public SeatGridException(string message) : base(message)
    {
    }

    public SeatGridException(string message, Exception inner) : base(message, inner)
    {
    }
}
