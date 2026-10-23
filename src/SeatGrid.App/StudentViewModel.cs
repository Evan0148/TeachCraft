using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SeatGrid.Core.Models;

namespace SeatGrid.App;

public partial class StudentViewModel : ObservableObject
{
    private static readonly IBrush MaleBrush = new SolidColorBrush(Color.Parse("#5A7A4B"));
    private static readonly IBrush FemaleBrush = new SolidColorBrush(Color.Parse("#D97757"));
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.Parse("#C9C1AE"));

    public StudentViewModel(Student model)
    {
        Model = model;
    }

    public Student Model { get; }

    public Guid Id => Model.Id;

    public string Name => Model.Name;

    public string GenderLabel => Model.GenderLabel;

    public bool IsFemale => Model.Gender == Gender.Female;

    public IBrush GenderBrush => Model.Gender switch
    {
        Gender.Male => MaleBrush,
        Gender.Female => FemaleBrush,
        _ => UnknownBrush,
    };

    public string Meta
    {
        get
        {
            var parts = new List<string> { GenderLabel };
            if (!string.IsNullOrWhiteSpace(Model.Number)) parts.Add($"学号 {Model.Number}");
            if (Model.Height is not null) parts.Add($"{Model.Height:0.#}cm");
            if (Model.Vision is not null) parts.Add($"视力 {Model.Vision:0.0}");
            return string.Join(" · ", parts);
        }
    }

    public string SearchKey =>
        $"{Model.Name} {Model.Number} {Model.OriginalGroup} {Model.Notes} {GenderLabel}".ToLowerInvariant();

    [ObservableProperty] private string _seatLabel = "未排";
    [ObservableProperty] private bool _isAssigned;

    public void SetSeat(SeatPosition? position)
    {
        IsAssigned = position is not null;
        SeatLabel = position is null
            ? "未排"
            : $"{position.Value.Group + 1}组 · {position.Value.Row + 1}排";
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(GenderLabel));
        OnPropertyChanged(nameof(IsFemale));
        OnPropertyChanged(nameof(GenderBrush));
        OnPropertyChanged(nameof(Meta));
        OnPropertyChanged(nameof(SearchKey));
    }

    public void RefreshSeat()
    {
        OnPropertyChanged(nameof(SeatLabel));
        OnPropertyChanged(nameof(IsAssigned));
    }
}
