using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flowarden.Ui.Models;

namespace Flowarden.Ui.ViewModels;

public sealed partial class InspectPageViewModel : ViewModelBase
{
    private readonly IReadOnlyList<ConnectionRowDto> _allRows;

    public InspectPageViewModel()
    {
        _allRows = CreateSeedRows();
        Filter = new InspectFilterDto();
        Rows = new ObservableCollection<ConnectionRowDto>(_allRows);
        Summary = BuildSummary(Rows);
        ActiveFilterSummary = "No active filters";
    }

    public ObservableCollection<ConnectionRowDto> Rows { get; }

    [ObservableProperty]
    private InspectFilterDto filter;

    [ObservableProperty]
    private InspectResultSummaryDto summary;

    [ObservableProperty]
    private string activeFilterSummary;

    [ObservableProperty]
    private string sourceAddressInput = string.Empty;

    [ObservableProperty]
    private string destinationAddressInput = string.Empty;

    [ObservableProperty]
    private string serviceInput = string.Empty;

    [ObservableProperty]
    private string protocolInput = string.Empty;

    [ObservableProperty]
    private string directionInput = string.Empty;

    [ObservableProperty]
    private string bpfInput = string.Empty;

    public string ResultCountLabel => $"{Summary.VisibleRows} visible / {Summary.TotalRows} total";

    [RelayCommand]
    private void ApplyFilters()
    {
        Filter = new InspectFilterDto
        {
            SourceAddress = NullIfEmpty(SourceAddressInput),
            DestinationAddress = NullIfEmpty(DestinationAddressInput),
            ServiceName = NullIfEmpty(ServiceInput),
            Protocol = NullIfEmpty(ProtocolInput),
            Direction = NullIfEmpty(DirectionInput),
            Bpf = NullIfEmpty(BpfInput),
        };

        var filtered = _allRows.Where(MatchesFilter).ToArray();

        Rows.Clear();
        foreach (var row in filtered)
        {
            Rows.Add(row);
        }

        Summary = BuildSummary(filtered);
        ActiveFilterSummary = BuildFilterSummary();
        OnPropertyChanged(nameof(ResultCountLabel));
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SourceAddressInput = string.Empty;
        DestinationAddressInput = string.Empty;
        ServiceInput = string.Empty;
        ProtocolInput = string.Empty;
        DirectionInput = string.Empty;
        BpfInput = string.Empty;
        Filter = new InspectFilterDto();

        Rows.Clear();
        foreach (var row in _allRows)
        {
            Rows.Add(row);
        }

        Summary = BuildSummary(_allRows);
        ActiveFilterSummary = "No active filters";
        OnPropertyChanged(nameof(ResultCountLabel));
    }

    private bool MatchesFilter(ConnectionRowDto row)
    {
        return MatchesText(Filter.SourceAddress, row.SourceAddress)
            && MatchesText(Filter.DestinationAddress, row.DestinationAddress)
            && MatchesText(Filter.ServiceName, row.ServiceName)
            && MatchesText(Filter.Protocol, row.Protocol)
            && MatchesText(Filter.Direction, row.Direction);
    }

    private string BuildFilterSummary()
    {
        var parts = new List<string>();

        AppendFilter(parts, "src", Filter.SourceAddress);
        AppendFilter(parts, "dst", Filter.DestinationAddress);
        AppendFilter(parts, "service", Filter.ServiceName);
        AppendFilter(parts, "protocol", Filter.Protocol);
        AppendFilter(parts, "direction", Filter.Direction);
        AppendFilter(parts, "bpf", Filter.Bpf);

        return parts.Count == 0 ? "No active filters" : string.Join(" | ", parts);
    }

    private static void AppendFilter(ICollection<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label}: {value}");
        }
    }

    private static bool MatchesText(string? filter, string value)
    {
        return string.IsNullOrWhiteSpace(filter)
            || value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static InspectResultSummaryDto BuildSummary(IEnumerable<ConnectionRowDto> rows)
    {
        var materialized = rows.ToArray();
        return new InspectResultSummaryDto
        {
            TotalRows = (ulong)materialized.Length,
            VisibleRows = (ulong)materialized.Length,
            TotalPackets = materialized.Aggregate(0UL, (acc, row) => acc + row.Packets),
            TotalBytes = materialized.Aggregate(0UL, (acc, row) => acc + row.Bytes),
            SortBy = "bytes",
            SortDirection = "desc",
        };
    }

    private static IReadOnlyList<ConnectionRowDto> CreateSeedRows()
    {
        return
        [
            new ConnectionRowDto
            {
                SourceAddress = "192.168.50.21",
                SourcePort = 52901,
                DestinationAddress = "142.250.72.14",
                DestinationPort = 443,
                Protocol = "tcp",
                ServiceName = "https",
                Direction = "outbound",
                Packets = 144,
                Bytes = 212_540,
            },
            new ConnectionRowDto
            {
                SourceAddress = "192.168.50.21",
                SourcePort = 51724,
                DestinationAddress = "1.1.1.1",
                DestinationPort = 53,
                Protocol = "udp",
                ServiceName = "dns",
                Direction = "outbound",
                Packets = 74,
                Bytes = 22_800,
            },
            new ConnectionRowDto
            {
                SourceAddress = "127.0.0.1",
                SourcePort = 50100,
                DestinationAddress = "127.0.0.1",
                DestinationPort = 39091,
                Protocol = "tcp",
                ServiceName = "grpc",
                Direction = "loopback",
                Packets = 88,
                Bytes = 10_928,
            },
        ];
    }
}
