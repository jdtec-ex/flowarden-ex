using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flowarden.Ui.Models;
using Flowarden.Ui.Services;
using Flowarden.Ui.State;

namespace Flowarden.Ui.ViewModels;

public sealed partial class AppShellViewModel : ViewModelBase
{
    private readonly string _bindAddress;
    private readonly string _bindAddressSource;
    private readonly IReadOnlyDictionary<string, AppShellPageViewModel> _pages;
    private readonly CoreConnectionCoordinator? _coreConnectionCoordinator;
    private readonly DiscoveryClient? _discoveryClient;
    private readonly ProjectionClient? _projectionClient;
    private readonly ControlClient? _controlClient;
    private readonly CoreHealthService? _coreHealthService;
    private readonly string _initialPageId;
    private readonly bool _shouldApplyInitialPageAfterLoad;
    private readonly LiveProjectionState _liveProjectionState;
    private readonly ProjectionSettingsState _projectionSettings;
    private bool _isRefreshingAfterStop;
    private bool _autoStartCaptureAttempted;
    private string _lastCaptureStatus = "idle";
    private System.Diagnostics.Process? _launchedCoreProcess;
    private bool _launchedCoreByUi;
    private CancellationTokenSource? _liveOverviewCts;

    public AppShellViewModel()
        : this(null)
    {
    }

    public AppShellViewModel(
        CoreConnectionCoordinator? coreConnectionCoordinator,
        DiscoveryClient? discoveryClient = null,
        ProjectionClient? projectionClient = null,
        ControlClient? controlClient = null,
        CoreHealthService? coreHealthService = null,
        string bindAddress = "not configured",
        string bindAddressSource = "design-time",
        string? initialPageId = null
    )
    {
        _bindAddress = bindAddress;
        _bindAddressSource = bindAddressSource;
        _coreConnectionCoordinator = coreConnectionCoordinator;
        _discoveryClient = discoveryClient;
        _projectionClient = projectionClient;
        _controlClient = controlClient;
        _coreHealthService = coreHealthService;
        _initialPageId = NormalizeInitialPageId(initialPageId);
        _shouldApplyInitialPageAfterLoad = !string.IsNullOrWhiteSpace(initialPageId);
        _liveProjectionState = new LiveProjectionState();
        _projectionSettings = new ProjectionSettingsState();
        _projectionSettings.TopNChanged += OnProjectionTopNChanged;
        NavigationItems = new ReadOnlyCollection<AppNavigationItemViewModel>(
            [
                new AppNavigationItemViewModel { Id = "source", Label = "Source", CompactLabel = "Src" },
                new AppNavigationItemViewModel { Id = "overview", Label = "Overview", CompactLabel = "Ovr" },
                new AppNavigationItemViewModel { Id = "inspect", Label = "Inspect", CompactLabel = "Insp" },
                new AppNavigationItemViewModel { Id = "settings", Label = "Settings", CompactLabel = "Set" },
            ]
        );

        _pages = new Dictionary<string, AppShellPageViewModel>
        {
            ["source"] = new AppShellPageViewModel
            {
                Id = "source",
                Title = "Source Selection",
                Description = "Choose one formal capture source or review preview data before starting a session.",
            },
            ["overview"] = new AppShellPageViewModel
            {
                Id = "overview",
                Title = "Overview",
                Description = "Traffic timeline, summary cards, destination workbench and ranked detail panels.",
            },
            ["inspect"] = new AppShellPageViewModel
            {
                Id = "inspect",
                Title = "Inspect",
                Description = "Filter-first results table for connections, services and ranked summaries.",
            },
            ["settings"] = new AppShellPageViewModel
            {
                Id = "settings",
                Title = "Settings",
                Description = "Runtime-oriented endpoint, source and diagnostics settings for phase 2.",
            },
        };

        SourcePage = new SourcePageViewModel(_discoveryClient, _controlClient);
        SourcePage.SessionStateChanged += OnSourceSessionStateChanged;
        OverviewPage = new OverviewPageViewModel(
            _projectionClient,
            _liveProjectionState,
            _projectionSettings
        );
        InspectPage = new InspectPageViewModel(
            _projectionClient,
            _liveProjectionState,
            _projectionSettings
        );
        SettingsPage = new SettingsPageViewModel(
            _coreHealthService,
            _discoveryClient,
            _projectionSettings,
            _bindAddress,
            _bindAddressSource,
            LatestCoreError
        );

        CoreStatus = new StatusIndicatorViewModel
        {
            Label = "Core",
            Value = "Checking",
            Tone = "warning",
        };
        CaptureStatus = new StatusIndicatorViewModel
        {
            Label = "Capture",
            Value = "Idle",
            Tone = "neutral",
        };
        CurrentPageId = _initialPageId;
        CurrentPage = _pages[CurrentPageId];
    }

    public string Title { get; } = "Flowarden";

    public string Subtitle { get; } = "Traffic Flow Warden";

    public IReadOnlyList<AppNavigationItemViewModel> NavigationItems { get; }

    [ObservableProperty]
    private StatusIndicatorViewModel coreStatus;

    [ObservableProperty]
    private StatusIndicatorViewModel captureStatus;

    [ObservableProperty]
    private string currentPageId;

    [ObservableProperty]
    private AppShellPageViewModel currentPage;

    public SourcePageViewModel SourcePage { get; }

    public OverviewPageViewModel OverviewPage { get; }

    public InspectPageViewModel InspectPage { get; }

    public SettingsPageViewModel SettingsPage { get; }

    [ObservableProperty]
    private string connectionMessage = "Connecting to flowarden core...";

    [ObservableProperty]
    private CoreErrorDto? latestCoreError;

    [ObservableProperty]
    private bool isRailCollapsed;

    public string HeaderSupportingText =>
        LatestCoreError?.Message
        ?? (CoreStatus.Value == "Connected" ? CurrentPage.Description : ConnectionMessage);

    public bool IsSourcePageActive => CurrentPageId == "source";

    public bool IsNotSourcePageActive => !IsSourcePageActive;

    public bool IsOverviewPageActive => CurrentPageId == "overview";

    public bool IsNotOverviewPageActive => !IsOverviewPageActive;

    public bool IsInspectPageActive => CurrentPageId == "inspect";

    public bool IsSettingsPageActive => CurrentPageId == "settings";

    public bool HasLatestCoreError => LatestCoreError is not null;

    public string RailToggleLabel => IsRailCollapsed ? "Expand" : "Collapse";

    public double RailWidth => IsRailCollapsed ? 96 : 180;

    partial void OnCoreStatusChanged(StatusIndicatorViewModel value)
    {
        OnPropertyChanged(nameof(HeaderSupportingText));
    }

    partial void OnCurrentPageChanged(AppShellPageViewModel value)
    {
        OnPropertyChanged(nameof(HeaderSupportingText));
    }

    partial void OnConnectionMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HeaderSupportingText));
    }

    partial void OnLatestCoreErrorChanged(CoreErrorDto? value)
    {
        OnPropertyChanged(nameof(HeaderSupportingText));
        OnPropertyChanged(nameof(HasLatestCoreError));
    }

    partial void OnIsRailCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(RailToggleLabel));
        OnPropertyChanged(nameof(RailWidth));
    }

    [RelayCommand]
    private void Navigate(string? pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId) || !_pages.TryGetValue(pageId, out var page))
        {
            return;
        }

        CurrentPageId = pageId;
        CurrentPage = page;
        OnPropertyChanged(nameof(IsSourcePageActive));
        OnPropertyChanged(nameof(IsNotSourcePageActive));
        OnPropertyChanged(nameof(IsOverviewPageActive));
        OnPropertyChanged(nameof(IsNotOverviewPageActive));
        OnPropertyChanged(nameof(IsInspectPageActive));
        OnPropertyChanged(nameof(IsSettingsPageActive));
    }

    private void ApplyInitialPageSelection()
    {
        if (!_shouldApplyInitialPageAfterLoad)
        {
            return;
        }

        Navigate(_initialPageId);
    }

    private static string NormalizeInitialPageId(string? pageId)
    {
        return pageId?.Trim().ToLowerInvariant() switch
        {
            "source" => "source",
            "inspect" => "inspect",
            "settings" => "settings",
            _ => "overview",
        };
    }

    [RelayCommand]
    private void ToggleRail()
    {
        IsRailCollapsed = !IsRailCollapsed;
    }

    public async Task InitializeCoreConnectionAsync(
        string workingDirectory,
        string binaryPath,
        string bindAddress
    )
    {
        if (_coreConnectionCoordinator is null)
        {
            CoreStatus = new StatusIndicatorViewModel
            {
                Label = "Core",
                Value = "Not wired",
                Tone = "warning",
            };
            ConnectionMessage = "Core connection services are not configured in this app session.";
            return;
        }

        ConnectionMessage = "Checking for resident flowarden core...";
        var result = await _coreConnectionCoordinator.EnsureConnectedAsync(
            workingDirectory,
            binaryPath,
            bindAddress
        );

        if (result.Connected && result.Health is not null)
        {
            _launchedCoreProcess = result.LaunchedProcess;
            _launchedCoreByUi = result.LaunchedByUi;
            LatestCoreError = null;
            CoreStatus = new StatusIndicatorViewModel
            {
                Label = "Core",
                Value = "Connected",
                Tone = "good",
            };
            ConnectionMessage = result.LaunchedByUi
                ? "flowarden core launched and connected."
                : "Connected to an already running flowarden core.";
            await LoadSourcePageAsync();
            await LoadOverviewPageAsync();
            await LoadInspectPageAsync();
            await LoadSettingsPageAsync();
            await StartCaptureOnLaunchAsync();
            ApplyInitialPageSelection();
            return;
        }

        LatestCoreError = result.Error;
        await LoadSettingsPageAsync();
        ApplyInitialPageSelection();
        CoreStatus = new StatusIndicatorViewModel
        {
            Label = "Core",
            Value = "Offline",
            Tone = "warning",
        };
        ConnectionMessage = result.Error?.Message ?? "Failed to connect to flowarden core.";
    }

    public async Task HandleUiExitAsync(CancellationToken cancellationToken = default)
    {
        if (!_launchedCoreByUi || _launchedCoreProcess is null || SettingsPage is null)
        {
            return;
        }

        if (!SettingsPage.ShutdownCoreWhenUiCloses)
        {
            return;
        }

        if (_controlClient is not null)
        {
            try
            {
                using var shutdownTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                shutdownTimeout.CancelAfter(5_000);
                var result = await _controlClient.ShutdownCoreAsync(shutdownTimeout.Token);
                if (result.Accepted)
                {
                    await WaitForLaunchedCoreExitAsync(_launchedCoreProcess, shutdownTimeout.Token);
                    if (_launchedCoreProcess.HasExited)
                    {
                        return;
                    }
                }
            }
            catch { }
        }

        try
        {
            if (!_launchedCoreProcess.HasExited)
            {
                _launchedCoreProcess.Kill(entireProcessTree: true);
                await _launchedCoreProcess.WaitForExitAsync(cancellationToken);
            }
        }
        catch { }
    }

    private async Task LoadSourcePageAsync()
    {
        if (SourcePage is null)
        {
            return;
        }

        await SourcePage.LoadAsync(refreshPreview: true);
        OnSourceSessionStateChanged(SourcePage.CurrentSession, SourcePage.StatusMessage);
    }

    private async Task LoadOverviewPageAsync()
    {
        if (OverviewPage is null)
        {
            return;
        }

        await OverviewPage.LoadAsync();
    }

    private async Task LoadInspectPageAsync()
    {
        if (InspectPage is null)
        {
            return;
        }

        await InspectPage.LoadAsync();
    }

    private async Task LoadSettingsPageAsync()
    {
        if (SettingsPage is null)
        {
            return;
        }

        await SettingsPage.LoadAsync(LatestCoreError);
    }

    private async Task StartCaptureOnLaunchAsync()
    {
        if (_autoStartCaptureAttempted)
        {
            return;
        }

        _autoStartCaptureAttempted = true;
        if (!SourcePage.CanStartFormalCapture)
        {
            ConnectionMessage = SourcePage.HasSelectedDevice
                ? "Automatic capture start skipped because the selected source is not ready."
                : "Automatic capture start skipped because no live source was available.";
            return;
        }

        ConnectionMessage = $"Starting capture on launch for {SourcePage.SelectedDevice?.DisplayName ?? "selected source"}...";
        await SourcePage.StartFormalCaptureAsync();
    }

    private void OnSourceSessionStateChanged(CaptureSessionStateDto? session, string? statusMessage)
    {
        var status = session?.CaptureStatus?.ToLowerInvariant() ?? "idle";
        var previousStatus = _lastCaptureStatus;
        _lastCaptureStatus = status;
        var (value, tone) = status switch
        {
            "starting" => ("Starting", "warning"),
            "running" => ("Running", "good"),
            "stopping" => ("Stopping", "warning"),
            "armed" => ("Armed", "warning"),
            _ => ("Idle", "neutral"),
        };

        CaptureStatus = new StatusIndicatorViewModel
        {
            Label = "Capture",
            Value = value,
            Tone = tone,
        };

        if (!string.IsNullOrWhiteSpace(statusMessage) && CoreStatus.Value == "Connected")
        {
            ConnectionMessage = statusMessage;
        }

        if (
            status == "idle"
            && (previousStatus == "running" || previousStatus == "stopping")
            && !_isRefreshingAfterStop
        )
        {
            StopOverviewStreaming();
            _ = RefreshProjectionAfterStopAsync();
        }

        if (status == "starting")
        {
            StopOverviewStreaming();
            ResetProjectionForSession(session);
            return;
        }

        if (status == "running")
        {
            BeginOverviewStreaming();
        }

        if (status == "stopping")
        {
            StopOverviewStreaming();
        }
    }

    private async Task RefreshProjectionAfterStopAsync()
    {
        if (_projectionClient is null)
        {
            return;
        }

        _isRefreshingAfterStop = true;
        try
        {
            await LoadOverviewPageAsync();
            await LoadInspectPageAsync();
        }
        finally
        {
            _isRefreshingAfterStop = false;
        }
    }

    private static async Task WaitForLaunchedCoreExitAsync(
        System.Diagnostics.Process process,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (!process.HasExited)
            {
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        catch { }
    }

    private void BeginOverviewStreaming()
    {
        if (_projectionClient is null || _liveOverviewCts is not null)
        {
            return;
        }

        var session = SourcePage.CurrentSession;
        ResetProjectionForSession(session);

        var streamCts = new CancellationTokenSource();
        _liveOverviewCts = streamCts;
        _ = ConsumeOverviewStreamAsync(streamCts);
    }

    private void ResetProjectionForSession(CaptureSessionStateDto? session)
    {
        if (session is null)
        {
            return;
        }

        var mode = string.Equals(session.Mode, "offline", StringComparison.OrdinalIgnoreCase)
            ? "offline"
            : "live";
        var sourceLabelPrefix = mode == "offline" ? "Offline source" : "Live source";
        _liveProjectionState.ResetOverview(
            new OverviewSnapshotDto
            {
                CaptureId = $"{mode}:running",
                Mode = mode,
                SourceLabel = session.SourceDisplayName == "none"
                    ? $"{sourceLabelPrefix} · not started"
                    : $"{sourceLabelPrefix} · {session.SourceDisplayName}",
                FilterLabel = string.IsNullOrWhiteSpace(session.Bpf)
                    ? "Filter · none"
                    : $"Filter · {session.Bpf}",
                MetricMode = "bytes",
                CaptureStatus = session.CaptureStatus,
            }
        );
    }

    private void StopOverviewStreaming()
    {
        _liveOverviewCts?.Cancel();
        _liveOverviewCts = null;
    }

    private async Task ConsumeOverviewStreamAsync(CancellationTokenSource streamCts)
    {
        if (_projectionClient is null)
        {
            return;
        }

        var cancellationToken = streamCts.Token;
        try
        {
            await foreach (
                var snapshot in _projectionClient.StreamOverviewAsync(
                    _projectionSettings.TopN,
                    cancellationToken
                )
            )
            {
                _liveProjectionState.UpdateOverview(snapshot);
                ApplyCaptureStatusFromOverview(snapshot);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_liveOverviewCts, streamCts))
            {
                _liveOverviewCts = null;
            }

            streamCts.Dispose();
        }
    }

    private void ApplyCaptureStatusFromOverview(OverviewSnapshotDto snapshot)
    {
        if (!string.Equals(snapshot.Mode, "offline", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var status = snapshot.CaptureStatus.Trim().ToLowerInvariant();
        if (status is not ("idle" or "error"))
        {
            return;
        }

        var current = SourcePage.CurrentSession;
        if (!string.Equals(current.Mode, "offline", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(current.CaptureStatus, "running", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SourcePage.SetProjectionCaptureStatus(
            status,
            status == "error" ? "Offline replay failed." : "Offline replay completed."
        );
        StopOverviewStreaming();
    }

    private void OnProjectionTopNChanged(uint topN)
    {
        if (_liveOverviewCts is not null)
        {
            StopOverviewStreaming();
            BeginOverviewStreaming();
            return;
        }

        _ = RefreshProjectionAfterTopNChangeAsync();
    }

    private async Task RefreshProjectionAfterTopNChangeAsync()
    {
        await LoadOverviewPageAsync();
        await LoadInspectPageAsync();
    }
}
