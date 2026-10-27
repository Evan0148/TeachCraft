namespace GradeWeave.Core;

/// <summary>面向老师的可读错误；消息必须是中文且能直接指导下一步操作。</summary>
public sealed class GradeWeaveException : Exception
{
    public GradeWeaveException(string message) : base(message)
    {
    }

    public GradeWeaveException(string message, Exception inner) : base(message, inner)
    {
    }
}
