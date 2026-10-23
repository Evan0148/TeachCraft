using System;

namespace SeatGrid.Core.Models;

public enum Gender
{
    Unknown,
    Male,
    Female,
}

/// <summary>名单中的一名学生。座位分配不写在这里，按 SeatPosition 单独保存。</summary>
public sealed class Student
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public Gender Gender { get; set; }

    public string Number { get; set; } = string.Empty;

    public double? Height { get; set; }

    public double? Vision { get; set; }

    public double? Score { get; set; }

    public string Notes { get; set; } = string.Empty;

    /// <summary>名单里原有的小组 / 组别，仅用于展示与导出，不参与自动排座。</summary>
    public string OriginalGroup { get; set; } = string.Empty;

    public string GenderLabel => Gender switch
    {
        Gender.Male => "男",
        Gender.Female => "女",
        _ => "未知",
    };

    public Student Copy() => new()
    {
        Name = Name,
        Gender = Gender,
        Number = Number,
        Height = Height,
        Vision = Vision,
        Score = Score,
        Notes = Notes,
        OriginalGroup = OriginalGroup,
    };
}
