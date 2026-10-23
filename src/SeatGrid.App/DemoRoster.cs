using System;
using System.Collections.Generic;
using SeatGrid.Core.Models;
using SeatGrid.Core.Roster;

namespace SeatGrid.App;

/// <summary>仅供设计走查与空状态演示，不参与真实业务。</summary>
public static class DemoRoster
{
    private static readonly string[] Names =
    {
        "陈思远", "李雨桐", "王浩然", "张欣怡", "刘子涵", "赵梓萱", "周俊杰", "吴梦琪",
        "徐一鸣", "孙悦", "马晨曦", "朱嘉怡", "胡博文", "郭诗涵", "何宇航", "高晓雅",
        "林书言", "罗文静", "郑凯", "梁若曦", "谢明轩", "宋佳宁", "唐子睿", "许婉清",
        "韩泽宇", "冯可欣", "刘念安", "曹语嫣", "彭嘉树", "曾梦瑶", "萧然", "田雨欣",
        "董子墨", "袁心怡", "潘思源", "蒋依依", "蔡文博", "余佳琪", "杜明远", "沈静姝",
        "钟子墨", "汪雨泽", "任思彤", "姚俊熙", "卢欣妍", "姜浩然", "崔诗雨", "谭子轩",
    };

    public static RosterImportResult Create()
    {
        var students = new List<Student>();
        for (var i = 0; i < Names.Length; i++)
        {
            students.Add(new Student
            {
                Name = Names[i],
                Gender = i % 2 == 0 ? Gender.Male : Gender.Female,
                Number = (2024001 + i).ToString(),
                Height = 150 + (i % 12) * 2.5,
                Vision = Math.Round(4.5 + (i % 5) * 0.1, 1),
                Score = 500 + (i * 7) % 80,
                OriginalGroup = $"第{i % 4 + 1}组",
                Notes = i % 9 == 0 ? "近视" : string.Empty,
            });
        }

        return new RosterImportResult("演示名单.xlsx", "九年级六班名单.xlsx", students, Array.Empty<string>());
    }
}
