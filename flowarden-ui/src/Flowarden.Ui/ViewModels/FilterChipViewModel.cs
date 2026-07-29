using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flowarden.Ui.ViewModels;

public enum FilterChipKind
{
    Search,
    SourceAddress,
    DestinationAddress,
    Protocol,
    Service,
    Direction,
    Process,
    Sni,
    Country,
    Address,
    Port,
    State,
}

public enum FilterChipSource
{
    User,
    Pivot,
}

public sealed partial class FilterChipViewModel : ObservableObject
{
    public FilterChipViewModel(
        FilterChipKind kind,
        string value,
        FilterChipSource source,
        Action<FilterChipKind> onRemove
    )
    {
        Kind = kind;
        Value = value;
        Source = source;
        _onRemove = onRemove;
    }

    private readonly Action<FilterChipKind> _onRemove;

    public FilterChipKind Kind { get; }

    public string Value { get; }

    public FilterChipSource Source { get; }

    public string DisplayLabel => $"{KindLabel}:{Value}";

    public string KindLabel =>
        Kind switch
        {
            FilterChipKind.Search => "search",
            FilterChipKind.SourceAddress => "src",
            FilterChipKind.DestinationAddress => "dst",
            FilterChipKind.Protocol => "protocol",
            FilterChipKind.Service => "service",
            FilterChipKind.Direction => "direction",
            FilterChipKind.Process => "process",
            FilterChipKind.Sni => "sni",
            FilterChipKind.Country => "country",
            FilterChipKind.Address => "address",
            FilterChipKind.Port => "port",
            FilterChipKind.State => "state",
            _ => Kind.ToString().ToLowerInvariant(),
        };

    public bool IsPivot => Source == FilterChipSource.Pivot;

    [RelayCommand]
    private void Remove()
    {
        _onRemove(Kind);
    }
}
