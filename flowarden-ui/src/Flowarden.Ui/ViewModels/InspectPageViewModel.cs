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
    public enum InspectMode
    {
        Flows,
        TcpConnections,
    }

    private readonly ProjectionClient? _projectionClient;
    private readonly bool _isDesignTime;
    private IReadOnlyList<ConnectionRowDto> _allRows;
    private IReadOnlyList<TcpConnectionRowDto> _allTcpRows;

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
        _allTcpRows = CreateSeedTcpRows();
        Filter = new InspectFilterDto();
        Rows = new ObservableCollection<ConnectionRowDto>(_allRows);
        TcpRows = new ObservableCollection<TcpConnectionRowDto>(_allTcpRows);
        ActiveFilterChips = new ObservableCollection<string>();
        Summary = BuildSummary(Rows);
        ActiveFilterSummary = "No active filters";
        ProjectionStateLabel = "Seed dataset";
    }

    public ObservableCollection<ConnectionRowDto> Rows { get; }

    public ObservableCollection<TcpConnectionRowDto> TcpRows { get; }

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
    private InspectMode currentMode = InspectMode.Flows;

    [ObservableProperty]
    private string addressInput = string.Empty;

    [ObservableProperty]
    private string portInput = string.Empty;

    [ObservableProperty]
    private string stateInput = string.Empty;

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

    public bool IsFlowsMode => CurrentMode == InspectMode.Flows;

    public bool IsTcpConnectionsMode => CurrentMode == InspectMode.TcpConnections;

    public string FlowModeLabel => "Flows";

    public string TcpConnectionsModeLabel => "TCP Connections";

    public async Task LoadAsync()
    {
        if (_projectionClient is null || _isDesignTime)
        {
            return;
        }

        await ReloadAsync();
    }

    [RelayCommand]
    private async Task ApplyFilters()
    {
        Filter = CurrentMode == InspectMode.Flows
            ? new InspectFilterDto
            {
                SourceAddress = NullIfEmpty(SourceAddressInput),
                DestinationAddress = NullIfEmpty(DestinationAddressInput),
                ServiceName = NullIfEmpty(ServiceInput),
                Protocol = NullIfEmpty(ProtocolInput),
                Direction = NullIfEmpty(DirectionInput),
                Bpf = NullIfEmpty(BpfInput),
            }
            : new InspectFilterDto
            {
                Address = NullIfEmpty(AddressInput),
                Port = NullIfEmpty(PortInput),
                State = NullIfEmpty(StateInput),
            };

        await ReloadAsync();
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
        AddressInput = string.Empty;
        PortInput = string.Empty;
        StateInput = string.Empty;
        Filter = new InspectFilterDto();
        if (CurrentMode == InspectMode.Flows)
        {
            ReplaceRows(_allRows);
            Summary = BuildSummary(_allRows);
        }
        else
        {
            ReplaceTcpRows(_allTcpRows);
            Summary = BuildTcpSummary(_allTcpRows);
        }
        ActiveFilterSummary = "No active filters";
        ProjectionStateLabel = _projectionClient is null || _isDesignTime ? "Seed dataset" : "Projection ready";
        ReplaceActiveFilterChips([]);
        OnPropertyChanged(nameof(ResultCountLabel));
        OnPropertyChanged(nameof(TotalBytesLabel));
        OnPropertyChanged(nameof(TotalPacketsLabel));
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    [RelayCommand]
    private async Task SwitchToFlows()
    {
        if (CurrentMode == InspectMode.Flows)
        {
            return;
        }

        CurrentMode = InspectMode.Flows;
        await ReloadAsync();
        OnPropertyChanged(nameof(IsFlowsMode));
        OnPropertyChanged(nameof(IsTcpConnectionsMode));
    }

    [RelayCommand]
    private async Task SwitchToTcpConnections()
    {
        if (CurrentMode == InspectMode.TcpConnections)
        {
            return;
        }

        CurrentMode = InspectMode.TcpConnections;
        await ReloadAsync();
        OnPropertyChanged(nameof(IsFlowsMode));
        OnPropertyChanged(nameof(IsTcpConnectionsMode));
    }

    private void ReplaceRows(IEnumerable<ConnectionRowDto> rows)
    {
        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }
    }

    private void ReplaceTcpRows(IEnumerable<TcpConnectionRowDto> rows)
    {
        TcpRows.Clear();
        foreach (var row in rows)
        {
            TcpRows.Add(row);
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

    private bool MatchesTcpFilter(TcpConnectionRowDto row)
    {
        return MatchesText(Filter.Address, row.ConnectionLabel)
            && MatchesText(Filter.Port, row.ConnectionLabel)
            && MatchesText(Filter.State, row.State);
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
        AppendFilter(parts, "address", Filter.Address);
        AppendFilter(parts, "port", Filter.Port);
        AppendFilter(parts, "state", Filter.State);

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
        AppendFilter(chips, "address", filter.Address);
        AppendFilter(chips, "port", filter.Port);
        AppendFilter(chips, "state", filter.State);

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

    private static InspectResultSummaryDto BuildTcpSummary(IEnumerable<TcpConnectionRowDto> rows)
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

    private async Task ReloadAsync()
    {
        if (_projectionClient is not null && !_isDesignTime)
        {
            if (CurrentMode == InspectMode.Flows)
            {
                var result = await _projectionClient.GetInspectPageAsync(Filter);
                _allRows = result.Rows;
                ReplaceRows(_allRows);
                Summary = result.Summary;
                ActiveFilterSummary = BuildFilterSummary();
                ProjectionStateLabel = string.Equals(result.State, "ready", StringComparison.OrdinalIgnoreCase)
                    ? "Flow projection ready"
                    : $"Projection {result.State}";
            }
            else
            {
                var result = await _projectionClient.GetTcpConnectionsPageAsync(Filter);
                _allTcpRows = result.TcpRows;
                ReplaceTcpRows(_allTcpRows);
                Summary = result.Summary;
                ActiveFilterSummary = BuildFilterSummary();
                ProjectionStateLabel = string.Equals(result.State, "ready", StringComparison.OrdinalIgnoreCase)
                    ? "TCP connections ready"
                    : $"Projection {result.State}";
            }

            ReplaceActiveFilterChips(BuildFilterChips(Filter));
            OnPropertyChanged(nameof(ResultCountLabel));
            OnPropertyChanged(nameof(TotalBytesLabel));
            OnPropertyChanged(nameof(TotalPacketsLabel));
            OnPropertyChanged(nameof(SortLabel));
            OnPropertyChanged(nameof(HasActiveFilters));
            return;
        }

        if (CurrentMode == InspectMode.Flows)
        {
            var filtered = _allRows.Where(MatchesFilter).ToArray();
            ReplaceRows(filtered);
            Summary = BuildSummary(filtered);
            ProjectionStateLabel = "Seed dataset";
        }
        else
        {
            var filtered = _allTcpRows.Where(MatchesTcpFilter).ToArray();
            ReplaceTcpRows(filtered);
            Summary = BuildTcpSummary(filtered);
            ProjectionStateLabel = "Seed TCP dataset";
        }

        ActiveFilterSummary = BuildFilterSummary();
        ReplaceActiveFilterChips(BuildFilterChips(Filter));
        OnPropertyChanged(nameof(ResultCountLabel));
        OnPropertyChanged(nameof(TotalBytesLabel));
        OnPropertyChanged(nameof(TotalPacketsLabel));
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(HasActiveFilters));
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

    private static IReadOnlyList<TcpConnectionRowDto> CreateSeedTcpRows()
    {
        return
        [
            new TcpConnectionRowDto
            {
                EndpointAAddress = "192.168.50.21",
                EndpointAPort = 52901,
                EndpointBAddress = "142.250.72.14",
                EndpointBPort = 443,
                State = "ESTABLISHED",
                SynCount = 2,
                FinCount = 0,
                RstCount = 0,
                Packets = 144,
                Bytes = 212_540,
                FirstSeen = new PacketTimestampDto { Seconds = 1714587202, Microseconds = 0 },
                LastSeen = new PacketTimestampDto { Seconds = 1714587212, Microseconds = 0 },
            },
            new TcpConnectionRowDto
            {
                EndpointAAddress = "127.0.0.1",
                EndpointAPort = 50100,
                EndpointBAddress = "127.0.0.1",
                EndpointBPort = 39091,
                State = "ESTABLISHED",
                SynCount = 0,
                FinCount = 0,
                RstCount = 0,
                Packets = 88,
                Bytes = 10_928,
                FirstSeen = new PacketTimestampDto { Seconds = 1714587202, Microseconds = 0 },
                LastSeen = new PacketTimestampDto { Seconds = 1714587208, Microseconds = 0 },
            },
        ];
    }
}
