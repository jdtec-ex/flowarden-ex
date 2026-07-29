using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flowarden.Ui.Models;
using Flowarden.Ui.Services;
using Flowarden.Ui.State;

namespace Flowarden.Ui.ViewModels;

public sealed partial class InspectPageViewModel : ViewModelBase
{
    public enum InspectMode
    {
        Flows,
        TcpConnections,
    }

    private readonly ProjectionClient? _projectionClient;
    private readonly LiveProjectionState? _liveProjectionState;
    private readonly ProjectionSettingsState _projectionSettings;
    private readonly bool _isDesignTime;
    private IReadOnlyList<ConnectionRowDto> _allRows;
    private IReadOnlyList<TcpConnectionRowDto> _allTcpRows;
    private int _searchGeneration;
    private CancellationTokenSource? _coldCts;
    private CancellationTokenSource? _searchDebounceCts;
    private FilterChipSource _lastCommitSource = FilterChipSource.User;
    private IReadOnlyDictionary<string, string> _hostCountryByAddress =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public InspectPageViewModel()
        : this(
            projectionClient: null,
            liveProjectionState: null,
            projectionSettings: new ProjectionSettingsState(),
            isDesignTime: true
        )
    {
    }

    public InspectPageViewModel(
        ProjectionClient? projectionClient,
        LiveProjectionState? liveProjectionState,
        ProjectionSettingsState projectionSettings
    )
        : this(projectionClient, liveProjectionState, projectionSettings, isDesignTime: false)
    {
    }

    private InspectPageViewModel(
        ProjectionClient? projectionClient,
        LiveProjectionState? liveProjectionState,
        ProjectionSettingsState projectionSettings,
        bool isDesignTime
    )
    {
        _projectionClient = projectionClient;
        _liveProjectionState = liveProjectionState;
        _projectionSettings = projectionSettings;
        _isDesignTime = isDesignTime;
        _allRows = CreateSeedRows();
        _allTcpRows = CreateSeedTcpRows();
        Filter = new InspectFilterDto();
        Rows = new ObservableCollection<ConnectionRowDto>(_allRows);
        TcpRows = new ObservableCollection<TcpConnectionRowDto>(_allTcpRows);
        ActiveFilterChips = new ObservableCollection<FilterChipViewModel>();
        Summary = BuildSummary(_allRows.Count, _allRows);
        ActiveFilterSummary = "No active filters";
        ProjectionStateLabel = "Seed dataset";
        if (!isDesignTime && _liveProjectionState is not null)
        {
            _liveProjectionState.OverviewUpdated += OnLiveOverview;
        }
    }

    public ObservableCollection<ConnectionRowDto> Rows { get; }

    public ObservableCollection<TcpConnectionRowDto> TcpRows { get; }

    public ObservableCollection<FilterChipViewModel> ActiveFilterChips { get; }

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
    private string searchInput = string.Empty;

    [ObservableProperty]
    private string processInput = string.Empty;

    [ObservableProperty]
    private string sniInput = string.Empty;

    [ObservableProperty]
    private string countryInput = string.Empty;

    [ObservableProperty]
    private bool isMoreExpanded;

    public string ResultCountLabel => $"{Summary.VisibleRows} visible / {Summary.TotalRows} total";

    public string TotalBytesLabel => FormatBytes(Summary.TotalBytes);

    public string TotalPacketsLabel => $"{Summary.TotalPackets} packets";

    public string SortLabel => $"{Summary.SortBy} · {Summary.SortDirection}";

    public bool HasActiveFilters => ActiveFilterChips.Count > 0;

    public bool IsFlowsMode => CurrentMode == InspectMode.Flows;

    public bool IsTcpConnectionsMode => CurrentMode == InspectMode.TcpConnections;

    public bool HasTcpRows => TcpRows.Count > 0;

    public bool HasNoTcpRows => !HasTcpRows;

    public bool HasFlowRows => Rows.Count > 0;

    public bool HasNoFlowRows => !HasFlowRows;

    public string FlowModeLabel => "Flows";

    public string TcpConnectionsModeLabel => "TCP Connections";

    public bool IsFlowModeActive => IsFlowsMode;

    public bool IsTcpModeActive => IsTcpConnectionsMode;

    public string MoreToggleLabel => IsMoreExpanded ? "Less" : "More";

    public string TopNHint =>
        $"Searching top {_projectionSettings.TopN} projected flows (max {ProjectionSettingsState.MaxTopN})";

    public string TcpEmptyStateTitle =>
        HasActiveFilters
            ? "No TCP connections match the current filters"
            : "No TCP connections observed";

    public string TcpEmptyStateDetail =>
        _projectionClient is null || _isDesignTime
            ? "The design-time TCP dataset is empty after filtering."
            : "Start capture or wait for TCP packets, then adjust address, port, or state filters if needed.";

    public string FlowEmptyStateTitle =>
        HasActiveFilters
            ? "No flows match the current filters"
            : "No projected flows observed";

    public string FlowEmptyStateDetail =>
        HasActiveFilters
            ? $"{TopNHint}. Clear chips or broaden Search — a miss may mean the flow is outside the current Top N window."
            : "Start capture or wait for projection ticks.";

    public async Task LoadAsync()
    {
        if (_projectionClient is null || _isDesignTime)
        {
            return;
        }

        await ApplyCommittedFilterAsync();
    }

    /// <summary>
    /// Pivot into Inspect (KD15 clear-then-set; KD16 host → SearchText).
    /// </summary>
    public async Task ApplySignalPivotAsync(string pivotKind, string pivotValue) =>
        await ApplyPivotAsync(pivotKind, pivotValue);

    public async Task ApplyPivotAsync(string pivotKind, string pivotValue)
    {
        if (string.IsNullOrWhiteSpace(pivotValue))
        {
            return;
        }

        CurrentMode = InspectMode.Flows;
        ClearFilterInputsOnly();

        var kind = pivotKind.Trim().ToLowerInvariant();
        var value = pivotValue.Trim();

        switch (kind)
        {
            case "host":
            case "peer":
            case "connection":
                SearchInput = value;
                break;
            case "sni":
                SniInput = value;
                break;
            case "service":
                ServiceInput = value;
                break;
            case "process":
                ProcessInput = value;
                break;
            case "src":
            case "source":
                SourceAddressInput = value;
                break;
            case "dst":
            case "destination":
                DestinationAddressInput = value;
                break;
            case "country":
            case "region":
                CountryInput = value;
                break;
            default:
                SearchInput = value;
                break;
        }

        _lastCommitSource = FilterChipSource.Pivot;
        Filter = BuildDtoFromInputs();
        await ApplyCommittedFilterAsync();
    }

    [RelayCommand]
    private async Task ApplyFilters()
    {
        _lastCommitSource = FilterChipSource.User;
        Filter = BuildDtoFromInputs();
        await ApplyCommittedFilterAsync();
    }

    [RelayCommand]
    private async Task ApplyStructuredFilters()
    {
        _lastCommitSource = FilterChipSource.User;
        Filter = BuildDtoFromInputs();
        await ApplyCommittedFilterAsync();
    }

    [RelayCommand]
    private async Task ApplyDirection(string? direction)
    {
        DirectionInput = direction ?? string.Empty;
        _lastCommitSource = FilterChipSource.User;
        Filter = BuildDtoFromInputs();
        await ApplyCommittedFilterAsync();
    }

    [RelayCommand]
    private void ToggleMore()
    {
        IsMoreExpanded = !IsMoreExpanded;
        OnPropertyChanged(nameof(MoreToggleLabel));
    }

    [RelayCommand]
    private void ClearFilters()
    {
        CancelSearchDebounce();
        ClearFilterInputsOnly();
        _lastCommitSource = FilterChipSource.User;
        Filter = new InspectFilterDto();
        if (CurrentMode == InspectMode.Flows)
        {
            var source = SnapshotAllRows();
            ReplaceRows(source);
            Summary = BuildSummary(source.Count, source);
        }
        else
        {
            var source = SnapshotAllTcpRows();
            ReplaceTcpRows(source);
            Summary = BuildTcpSummary(source.Count, source);
        }

        ActiveFilterSummary = "No active filters";
        ProjectionStateLabel =
            _projectionClient is null || _isDesignTime ? "Seed dataset" : "Projection ready";
        RebuildChips();
        NotifyResultSummaryChanged();
    }

    [RelayCommand]
    private async Task SwitchToFlows()
    {
        if (CurrentMode == InspectMode.Flows)
        {
            return;
        }

        CurrentMode = InspectMode.Flows;
        await ApplyCommittedFilterAsync();
        NotifyModeChrome();
    }

    [RelayCommand]
    private async Task SwitchToTcpConnections()
    {
        if (CurrentMode == InspectMode.TcpConnections)
        {
            return;
        }

        CurrentMode = InspectMode.TcpConnections;
        await ApplyCommittedFilterAsync();
        NotifyModeChrome();
    }

    partial void OnSearchInputChanged(string value)
    {
        if (_isDesignTime)
        {
            return;
        }

        ScheduleSearchDebounce();
    }

    private void ScheduleSearchDebounce()
    {
        CancelSearchDebounce();
        var gen = ++_searchGeneration;
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        _ = DebounceSearchAsync(gen, cts.Token);
    }

    private async Task DebounceSearchAsync(int generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (generation != _searchGeneration || token.IsCancellationRequested)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (generation != _searchGeneration)
            {
                return;
            }

            _lastCommitSource = FilterChipSource.User;
            Filter = BuildDtoFromInputs();
            await ApplyCommittedFilterAsync();
        });
    }

    private void CancelSearchDebounce()
    {
        try
        {
            _searchDebounceCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        _searchDebounceCts?.Dispose();
        _searchDebounceCts = null;
    }

    private void OnLiveOverview(OverviewSnapshotDto snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyLiveOverviewToInspect(snapshot));
            return;
        }

        ApplyLiveOverviewToInspect(snapshot);
    }

    private void ApplyLiveOverviewToInspect(OverviewSnapshotDto snapshot)
    {
        // Unified path: always apply committed Filter (no exclusive pivot branch).
        if (CurrentMode == InspectMode.TcpConnections)
        {
            UpdateHostCountryIndex(snapshot);
            _allTcpRows = snapshot.TopTcpConnections.ToArray();
            var source = SnapshotAllTcpRows();
            var filtered = source.Where(r => InspectFilterMatcher.MatchesTcp(r, Filter)).ToArray();
            ReplaceTcpRows(filtered);
            Summary = BuildTcpSummary(source.Count, filtered);
            ProjectionStateLabel = ProjectionLabelForSnapshot(snapshot);
            ActiveFilterSummary = BuildFilterSummary();
            RebuildChips();
            NotifyResultSummaryChanged();
            return;
        }

        if (CurrentMode != InspectMode.Flows)
        {
            return;
        }

        UpdateHostCountryIndex(snapshot);
        _allRows = snapshot.TopConnections.ToArray();
        var all = SnapshotAllRows();
        var visible = all
            .Where(r => InspectFilterMatcher.Matches(r, Filter, _hostCountryByAddress))
            .ToArray();
        ReplaceRows(visible);
        Summary = BuildSummary(all.Count, visible);
        ProjectionStateLabel = ProjectionLabelForSnapshot(snapshot);
        ActiveFilterSummary = BuildFilterSummary();
        RebuildChips();
        NotifyResultSummaryChanged();
    }

    private void UpdateHostCountryIndex(OverviewSnapshotDto snapshot)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in snapshot.TopHosts)
        {
            if (string.IsNullOrWhiteSpace(host.Host))
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(host.CountryLabel)
                ? string.Empty
                : host.CountryLabel.Trim();
            if (string.IsNullOrEmpty(label))
            {
                continue;
            }

            // Keep both full label and short codes so country pivot tokens match.
            map[host.Host.Trim()] = label;
        }

        // Enrich with destination region codes when hosts only carry full labels.
        foreach (var dest in snapshot.TopDestinations)
        {
            var code = dest.CountryCode?.Trim() ?? string.Empty;
            var destLabel = string.IsNullOrWhiteSpace(dest.Label) ? dest.CountryLabel : dest.Label;
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(destLabel))
            {
                continue;
            }

            foreach (var host in snapshot.TopHosts)
            {
                if (string.IsNullOrWhiteSpace(host.Host) || string.IsNullOrWhiteSpace(host.CountryLabel))
                {
                    continue;
                }

                var hostLabel = host.CountryLabel;
                var related =
                    (!string.IsNullOrWhiteSpace(destLabel)
                        && hostLabel.Contains(destLabel, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(code)
                        && hostLabel.Contains(code, StringComparison.OrdinalIgnoreCase));
                if (!related)
                {
                    continue;
                }

                var key = host.Host.Trim();
                var existing = map.TryGetValue(key, out var prev) ? prev : hostLabel;
                if (!string.IsNullOrWhiteSpace(code) && !existing.Contains(code, StringComparison.OrdinalIgnoreCase))
                {
                    map[key] = $"{existing} {code}";
                }
            }
        }

        _hostCountryByAddress = map;
    }

    private async Task ApplyCommittedFilterAsync()
    {
        _coldCts?.Cancel();
        _coldCts?.Dispose();
        _coldCts = new CancellationTokenSource();
        var token = _coldCts.Token;

        if (CurrentMode == InspectMode.TcpConnections)
        {
            await ApplyTcpCommittedAsync(token);
            return;
        }

        // Prefer live snapshot — do not RPC on every Search keystroke.
        if (_liveProjectionState is not null && !_isDesignTime)
        {
            var snapshot = _liveProjectionState.CurrentOverview;
            if (snapshot.TopConnections.Count > 0
                || string.Equals(snapshot.Mode, "live", StringComparison.OrdinalIgnoreCase)
                || string.Equals(snapshot.Mode, "offline", StringComparison.OrdinalIgnoreCase))
            {
                UpdateHostCountryIndex(snapshot);
                _allRows = snapshot.TopConnections.ToArray();
                var all = SnapshotAllRows();
                var visible = all
                    .Where(r => InspectFilterMatcher.Matches(r, Filter, _hostCountryByAddress))
                    .ToArray();
                ReplaceRows(visible);
                Summary = BuildSummary(all.Count, visible);
                ProjectionStateLabel = ProjectionLabelForSnapshot(snapshot);
                ActiveFilterSummary = BuildFilterSummary();
                RebuildChips();
                NotifyResultSummaryChanged();
                return;
            }
        }

        if (_projectionClient is not null && !_isDesignTime)
        {
            try
            {
                var result = await _projectionClient.GetInspectPageAsync(
                    Filter,
                    _projectionSettings.TopN,
                    token
                );
                if (token.IsCancellationRequested)
                {
                    return;
                }

                // Structured filtered server-side; always re-apply Search/process/sni/country locally.
                _allRows = result.Rows;
                var all = SnapshotAllRows();
                var visible = all
                    .Where(r =>
                        InspectFilterMatcher.MatchesSearchAndLocalOnly(
                            r,
                            Filter,
                            _hostCountryByAddress
                        )
                    )
                    .ToArray();
                ReplaceRows(visible);
                Summary = BuildSummary(all.Count, visible);
                ProjectionStateLabel = string.Equals(
                    result.State,
                    "ready",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "Flow projection ready"
                    : $"Projection {result.State}";
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        else
        {
            var all = SnapshotAllRows();
            var visible = all
                .Where(r => InspectFilterMatcher.Matches(r, Filter, _hostCountryByAddress))
                .ToArray();
            ReplaceRows(visible);
            Summary = BuildSummary(all.Count, visible);
            ProjectionStateLabel = "Seed dataset";
        }

        ActiveFilterSummary = BuildFilterSummary();
        RebuildChips();
        NotifyResultSummaryChanged();
    }

    private async Task ApplyTcpCommittedAsync(CancellationToken token)
    {
        if (_liveProjectionState is not null && !_isDesignTime)
        {
            var snapshot = _liveProjectionState.CurrentOverview;
            _allTcpRows = snapshot.TopTcpConnections.ToArray();
            var all = SnapshotAllTcpRows();
            var visible = all.Where(r => InspectFilterMatcher.MatchesTcp(r, Filter)).ToArray();
            ReplaceTcpRows(visible);
            Summary = BuildTcpSummary(all.Count, visible);
            ProjectionStateLabel = ProjectionLabelForSnapshot(snapshot);
            ActiveFilterSummary = BuildFilterSummary();
            RebuildChips();
            NotifyResultSummaryChanged();
            return;
        }

        if (_projectionClient is not null && !_isDesignTime)
        {
            try
            {
                var result = await _projectionClient.GetTcpConnectionsPageAsync(
                    Filter,
                    _projectionSettings.TopN,
                    token
                );
                if (token.IsCancellationRequested)
                {
                    return;
                }

                _allTcpRows = result.TcpRows;
                var all = SnapshotAllTcpRows();
                var visible = all.Where(r => InspectFilterMatcher.MatchesTcp(r, Filter)).ToArray();
                ReplaceTcpRows(visible);
                Summary = BuildTcpSummary(all.Count, visible);
                ProjectionStateLabel = string.Equals(
                    result.State,
                    "ready",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "TCP connections ready"
                    : $"Projection {result.State}";
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        else
        {
            var all = SnapshotAllTcpRows();
            var visible = all.Where(r => InspectFilterMatcher.MatchesTcp(r, Filter)).ToArray();
            ReplaceTcpRows(visible);
            Summary = BuildTcpSummary(all.Count, visible);
            ProjectionStateLabel = "Seed TCP dataset";
        }

        ActiveFilterSummary = BuildFilterSummary();
        RebuildChips();
        NotifyResultSummaryChanged();
    }

    private void RemoveChip(FilterChipKind kind)
    {
        switch (kind)
        {
            case FilterChipKind.Search:
                SearchInput = string.Empty;
                break;
            case FilterChipKind.SourceAddress:
                SourceAddressInput = string.Empty;
                break;
            case FilterChipKind.DestinationAddress:
                DestinationAddressInput = string.Empty;
                break;
            case FilterChipKind.Protocol:
                ProtocolInput = string.Empty;
                break;
            case FilterChipKind.Service:
                ServiceInput = string.Empty;
                break;
            case FilterChipKind.Direction:
                DirectionInput = string.Empty;
                break;
            case FilterChipKind.Process:
                ProcessInput = string.Empty;
                break;
            case FilterChipKind.Sni:
                SniInput = string.Empty;
                break;
            case FilterChipKind.Country:
                CountryInput = string.Empty;
                break;
            case FilterChipKind.Address:
                AddressInput = string.Empty;
                break;
            case FilterChipKind.Port:
                PortInput = string.Empty;
                break;
            case FilterChipKind.State:
                StateInput = string.Empty;
                break;
        }

        _lastCommitSource = FilterChipSource.User;
        Filter = BuildDtoFromInputs();
        _ = ApplyCommittedFilterAsync();
    }

    private InspectFilterDto BuildDtoFromInputs()
    {
        if (CurrentMode == InspectMode.TcpConnections)
        {
            return new InspectFilterDto
            {
                SearchText = NullIfEmpty(SearchInput),
                Address = NullIfEmpty(AddressInput),
                Port = NullIfEmpty(PortInput),
                State = NullIfEmpty(StateInput),
            };
        }

        return new InspectFilterDto
        {
            SearchText = NullIfEmpty(SearchInput),
            SourceAddress = NullIfEmpty(SourceAddressInput),
            DestinationAddress = NullIfEmpty(DestinationAddressInput),
            ServiceName = NullIfEmpty(ServiceInput),
            Protocol = NullIfEmpty(ProtocolInput),
            Direction = NullIfEmpty(DirectionInput),
            ProcessName = NullIfEmpty(ProcessInput),
            Sni = NullIfEmpty(SniInput),
            Country = NullIfEmpty(CountryInput),
        };
    }

    private void ClearFilterInputsOnly()
    {
        SearchInput = string.Empty;
        SourceAddressInput = string.Empty;
        DestinationAddressInput = string.Empty;
        ServiceInput = string.Empty;
        ProtocolInput = string.Empty;
        DirectionInput = string.Empty;
        ProcessInput = string.Empty;
        SniInput = string.Empty;
        CountryInput = string.Empty;
        AddressInput = string.Empty;
        PortInput = string.Empty;
        StateInput = string.Empty;
    }

    private void RebuildChips()
    {
        ActiveFilterChips.Clear();
        void Add(FilterChipKind kind, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            ActiveFilterChips.Add(
                new FilterChipViewModel(kind, value.Trim(), _lastCommitSource, RemoveChip)
            );
        }

        Add(FilterChipKind.Search, Filter.SearchText);
        Add(FilterChipKind.SourceAddress, Filter.SourceAddress);
        Add(FilterChipKind.DestinationAddress, Filter.DestinationAddress);
        Add(FilterChipKind.Protocol, Filter.Protocol);
        Add(FilterChipKind.Service, Filter.ServiceName);
        if (!string.IsNullOrWhiteSpace(Filter.Direction))
        {
            Add(FilterChipKind.Direction, Filter.Direction);
        }

        Add(FilterChipKind.Process, Filter.ProcessName);
        Add(FilterChipKind.Sni, Filter.Sni);
        Add(FilterChipKind.Country, Filter.Country);
        Add(FilterChipKind.Address, Filter.Address);
        Add(FilterChipKind.Port, Filter.Port);
        Add(FilterChipKind.State, Filter.State);

        OnPropertyChanged(nameof(TcpEmptyStateTitle));
        OnPropertyChanged(nameof(TcpEmptyStateDetail));
        OnPropertyChanged(nameof(FlowEmptyStateTitle));
        OnPropertyChanged(nameof(FlowEmptyStateDetail));
    }

    private string BuildFilterSummary()
    {
        if (ActiveFilterChips.Count == 0 && !HasCommittedFilters())
        {
            return "No active filters";
        }

        var parts = new List<string>();
        void Append(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{label}: {value}");
            }
        }

        Append("search", Filter.SearchText);
        Append("src", Filter.SourceAddress);
        Append("dst", Filter.DestinationAddress);
        Append("service", Filter.ServiceName);
        Append("protocol", Filter.Protocol);
        Append("direction", Filter.Direction);
        Append("process", Filter.ProcessName);
        Append("sni", Filter.Sni);
        Append("country", Filter.Country);
        Append("address", Filter.Address);
        Append("port", Filter.Port);
        Append("state", Filter.State);
        return parts.Count == 0 ? "No active filters" : string.Join(" | ", parts);
    }

    private bool HasCommittedFilters()
    {
        return !string.IsNullOrWhiteSpace(Filter.SearchText)
            || !string.IsNullOrWhiteSpace(Filter.SourceAddress)
            || !string.IsNullOrWhiteSpace(Filter.DestinationAddress)
            || !string.IsNullOrWhiteSpace(Filter.ServiceName)
            || !string.IsNullOrWhiteSpace(Filter.Protocol)
            || !string.IsNullOrWhiteSpace(Filter.Direction)
            || !string.IsNullOrWhiteSpace(Filter.ProcessName)
            || !string.IsNullOrWhiteSpace(Filter.Sni)
            || !string.IsNullOrWhiteSpace(Filter.Country)
            || !string.IsNullOrWhiteSpace(Filter.Address)
            || !string.IsNullOrWhiteSpace(Filter.Port)
            || !string.IsNullOrWhiteSpace(Filter.State);
    }

    private IReadOnlyList<ConnectionRowDto> SnapshotAllRows() => _allRows.ToArray();

    private IReadOnlyList<TcpConnectionRowDto> SnapshotAllTcpRows() => _allTcpRows.ToArray();

    private void ReplaceRows(IEnumerable<ConnectionRowDto> rows)
    {
        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasFlowRows));
        OnPropertyChanged(nameof(HasNoFlowRows));
        OnPropertyChanged(nameof(FlowEmptyStateTitle));
        OnPropertyChanged(nameof(FlowEmptyStateDetail));
        _ = LoadProcessIconsAsync(Rows.ToArray());
    }

    private async Task LoadProcessIconsAsync(IReadOnlyList<ConnectionRowDto> rows)
    {
        var iconService = new ProcessIconService();
        foreach (var row in rows)
        {
            if (row.IconKey.IsEmpty)
            {
                continue;
            }

            try
            {
                var icon = await iconService.GetIconAsync(row.IconKey);
                if (icon is not null)
                {
                    row.ProcessIcon = icon;
                }
            }
            catch
            {
                // Keep monogram fallback.
            }
        }
    }

    private void ReplaceTcpRows(IEnumerable<TcpConnectionRowDto> rows)
    {
        TcpRows.Clear();
        foreach (var row in rows)
        {
            TcpRows.Add(row);
        }

        OnPropertyChanged(nameof(HasTcpRows));
        OnPropertyChanged(nameof(HasNoTcpRows));
        OnPropertyChanged(nameof(TcpEmptyStateTitle));
        OnPropertyChanged(nameof(TcpEmptyStateDetail));
        OnPropertyChanged(nameof(TcpConnectionsModeLabel));
    }

    private static string ProjectionLabelForSnapshot(OverviewSnapshotDto snapshot)
    {
        return string.Equals(snapshot.Mode, "offline", StringComparison.OrdinalIgnoreCase)
            ? "Offline projection"
            : "Live projection";
    }

    private void NotifyResultSummaryChanged()
    {
        OnPropertyChanged(nameof(ResultCountLabel));
        OnPropertyChanged(nameof(TotalBytesLabel));
        OnPropertyChanged(nameof(TotalPacketsLabel));
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(HasFlowRows));
        OnPropertyChanged(nameof(HasNoFlowRows));
    }

    private void NotifyModeChrome()
    {
        OnPropertyChanged(nameof(IsFlowsMode));
        OnPropertyChanged(nameof(IsTcpConnectionsMode));
        OnPropertyChanged(nameof(IsFlowModeActive));
        OnPropertyChanged(nameof(IsTcpModeActive));
    }

    partial void OnCurrentModeChanged(InspectMode value)
    {
        NotifyModeChrome();
        OnPropertyChanged(nameof(TcpEmptyStateTitle));
        OnPropertyChanged(nameof(TcpEmptyStateDetail));
        OnPropertyChanged(nameof(FlowEmptyStateTitle));
        OnPropertyChanged(nameof(FlowEmptyStateDetail));
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

    private static InspectResultSummaryDto BuildSummary(
        int totalPool,
        IEnumerable<ConnectionRowDto> visible
    )
    {
        var materialized = visible.ToArray();
        return new InspectResultSummaryDto
        {
            TotalRows = (ulong)totalPool,
            VisibleRows = (ulong)materialized.Length,
            TotalPackets = materialized.Aggregate(0UL, (acc, row) => acc + row.Packets),
            TotalBytes = materialized.Aggregate(0UL, (acc, row) => acc + row.Bytes),
            SortBy = "bytes",
            SortDirection = "desc",
        };
    }

    private static InspectResultSummaryDto BuildTcpSummary(
        int totalPool,
        IEnumerable<TcpConnectionRowDto> visible
    )
    {
        var materialized = visible.ToArray();
        return new InspectResultSummaryDto
        {
            TotalRows = (ulong)totalPool,
            VisibleRows = (ulong)materialized.Length,
            TotalPackets = materialized.Aggregate(0UL, (acc, row) => acc + row.Packets),
            TotalBytes = materialized.Aggregate(0UL, (acc, row) => acc + row.Bytes),
            SortBy = "bytes",
            SortDirection = "desc",
        };
    }

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
                ProcessName = "Chrome",
                Sni = "www.google.com",
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
                SourceAddress = "192.168.50.21",
                SourcePort = 53112,
                DestinationAddress = "151.101.1.140",
                DestinationPort = 80,
                Protocol = "tcp",
                ServiceName = "http",
                Direction = "outbound",
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
                EndpointAAddress = "192.168.50.21",
                EndpointAPort = 53112,
                EndpointBAddress = "151.101.1.140",
                EndpointBPort = 80,
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
