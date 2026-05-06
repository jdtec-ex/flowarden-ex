using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flowarden.Ui.Models;
using Flowarden.Ui.Services;

namespace Flowarden.Ui.ViewModels;

public sealed partial class InspectPageViewModel : ViewModelBase
{
    private readonly ProjectionClient? _projectionClient;
    private readonly bool _isDesignTime;
    private IReadOnlyList<ConnectionRowDto> _allRows;

    public InspectPageViewModel()
        : this(projectionClient: null, isDesignTime: true)
    {
    }

    public InspectPageViewModel(ProjectionClient? projectionClient)
        : this(projectionClient, isDesignTime: false)
    {
    }

    private InspectPageViewModel(ProjectionClient? projectionClient, bool isDesignTime)
    {
        _projectionClient = projectionClient;
        _isDesignTime = isDesignTime;
        _allRows = CreateSeedRows();
        Filter = new InspectFilterDto();
        Rows = new ObservableCollection<ConnectionRowDto>(_allRows);
        ActiveFilterChips = new ObservableCollection<string>();
        Summary = BuildSummary(Rows);
        ActiveFilterSummary = "No active filters";
        ProjectionStateLabel = "Seed dataset";
    }

    public ObservableCollection<ConnectionRowDto> Rows { get; }

    public ObservableCollection<string> ActiveFilterChips { get; }

    [ObservableProperty]
    private InspectFilterDto filter;

    [ObservableProperty]
    private InspectResultSummaryDto summary;

    [ObservableProperty]
    private string activeFilterSummary;

    [ObservableProperty]
    private string projectionStateLabel;

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

    public string TotalBytesLabel => FormatBytes(Summary.TotalBytes);

    public string TotalPacketsLabel => $"{Summary.TotalPackets} packets";

    public string SortLabel =>
        $"{Summary.SortBy} · {Summary.SortDirection}";

    public bool HasActiveFilters => ActiveFilterChips.Count > 0;

    public async Task LoadAsync()
    {
        if (_projectionClient is null || _isDesignTime)
        {
            return;
        }

        var result = await _projectionClient.GetInspectPageAsync(Filter);
        _allRows = result.Rows;
        ReplaceRows(_allRows);
        Summary = result.Summary;
        ActiveFilterSummary = string.Equals(result.State, "ready", StringComparison.OrdinalIgnoreCase)
            ? "Backend projection ready"
            : $"Projection state: {result.State}";
        ProjectionStateLabel = string.Equals(result.State, "ready", StringComparison.OrdinalIgnoreCase)
            ? "Projection ready"
            : $"Projection {result.State}";
        ReplaceActiveFilterChips(BuildFilterChips(Filter));
        OnPropertyChanged(nameof(ResultCountLabel));
        OnPropertyChanged(nameof(TotalBytesLabel));
        OnPropertyChanged(nameof(TotalPacketsLabel));
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    [RelayCommand]
    private async Task ApplyFilters()
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

        if (_projectionClient is not null && !_isDesignTime)
        {
            var result = await _projectionClient.GetInspectPageAsync(Filter);
            _allRows = result.Rows;
            ReplaceRows(_allRows);
            Summary = result.Summary;
            ActiveFilterSummary = BuildFilterSummary();
            ProjectionStateLabel = string.Equals(result.State, "ready", StringComparison.OrdinalIgnoreCase)
                ? "Projection ready"
                : $"Projection {result.State}";
            ReplaceActiveFilterChips(BuildFilterChips(Filter));
            OnPropertyChanged(nameof(ResultCountLabel));
            OnPropertyChanged(nameof(TotalBytesLabel));
            OnPropertyChanged(nameof(TotalPacketsLabel));
            OnPropertyChanged(nameof(SortLabel));
            OnPropertyChanged(nameof(HasActiveFilters));
            return;
        }

        var filtered = _allRows.Where(MatchesFilter).ToArray();
        ReplaceRows(filtered);
        Summary = BuildSummary(filtered);
        ActiveFilterSummary = BuildFilterSummary();
        ProjectionStateLabel = "Seed dataset";
        ReplaceActiveFilterChips(BuildFilterChips(Filter));
        OnPropertyChanged(nameof(ResultCountLabel));
        OnPropertyChanged(nameof(TotalBytesLabel));
        OnPropertyChanged(nameof(TotalPacketsLabel));
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(HasActiveFilters));
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
        ReplaceRows(_allRows);
        Summary = BuildSummary(_allRows);
        ActiveFilterSummary = "No active filters";
        ProjectionStateLabel = _projectionClient is null || _isDesignTime ? "Seed dataset" : "Projection ready";
        ReplaceActiveFilterChips([]);
        OnPropertyChanged(nameof(ResultCountLabel));
        OnPropertyChanged(nameof(TotalBytesLabel));
        OnPropertyChanged(nameof(TotalPacketsLabel));
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    private void ReplaceRows(IEnumerable<ConnectionRowDto> rows)
    {
        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }
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

    private static IReadOnlyList<string> BuildFilterChips(InspectFilterDto filter)
    {
        var chips = new List<string>();

        AppendFilter(chips, "src", filter.SourceAddress);
        AppendFilter(chips, "dst", filter.DestinationAddress);
        AppendFilter(chips, "service", filter.ServiceName);
        AppendFilter(chips, "protocol", filter.Protocol);
        AppendFilter(chips, "direction", filter.Direction);
        AppendFilter(chips, "bpf", filter.Bpf);

        return chips;
    }

    private void ReplaceActiveFilterChips(IEnumerable<string> chips)
    {
        ActiveFilterChips.Clear();
        foreach (var chip in chips)
        {
            ActiveFilterChips.Add(chip);
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

    private static string FormatBytes(ulong bytes)
    {
        if (bytes >= 1_048_576)
        {
            return $"{bytes / 1_048_576.0:0.##} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024.0:0.##} KB";
        }

        return $"{bytes} B";
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
