using System.Collections.Generic;
using System.Linq;

namespace SeatGrid.Core.Models;

/// <summary>物理座位坐标：第几组、第几排（0 表示靠近讲台）、第几列。</summary>
public readonly record struct SeatPosition(int Group, int Row, int Column)
{
    public override string ToString() => $"{Group + 1} 组 · {Row + 1} 排 · {Column + 1} 列";
}

public sealed record SeatChartLayout(int Groups, int Rows, int Columns)
{
    public int SeatCount => Groups * Rows * Columns;

    public bool Contains(SeatPosition position) =>
        position.Group >= 0 && position.Group < Groups &&
        position.Row >= 0 && position.Row < Rows &&
        position.Column >= 0 && position.Column < Columns;

    /// <summary>
    /// 排座时的填充顺序：先填靠近讲台的一排，再从左到右跨组，最后才往后退。
    /// 这样按身高 / 视力排序时，前排优先的语义才成立。
    /// </summary>
    public IEnumerable<SeatPosition> FillOrder()
    {
        for (var row = 0; row < Rows; row++)
        {
            for (var group = 0; group < Groups; group++)
            {
                for (var column = 0; column < Columns; column++)
                {
                    yield return new SeatPosition(group, row, column);
                }
            }
        }
    }

    public IEnumerable<SeatPosition> AllSeats()
    {
        for (var group = 0; group < Groups; group++)
        {
            for (var row = 0; row < Rows; row++)
            {
                for (var column = 0; column < Columns; column++)
                {
                    yield return new SeatPosition(group, row, column);
                }
            }
        }
    }

    public static SeatChartLayout FromSettings(SeatChartSettings settings) =>
        new(settings.Groups, settings.Rows, settings.Columns);
}
