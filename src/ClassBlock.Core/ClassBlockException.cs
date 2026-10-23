using System;

namespace ClassBlock.Core;

/// <summary>面向老师的可读错误，UI 直接展示 Message。</summary>
public sealed class ClassBlockException : Exception
{
    public ClassBlockException(string message) : base(message)
    {
    }

    public ClassBlockException(string message, Exception inner) : base(message, inner)
    {
    }
}
