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
// OverviewSnapshotDto / preferences live under Models + State.

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
    private readonly UserPreferencesStore _preferencesStore;
    private readonly UserPreferences _preferences;
    private readonly SignalFeedState _signalFeed;
    private bool _isRefreshingAfterStop;
    private bool _autoStartCaptureAttempted;
    private string _lastCaptureStatus = "idle";
    private System.Diagnostics.Process? _launchedCoreProcess;
    private bool _launchedCoreByUi;
    private CancellationTokenSource? _liveOverviewCts;
    private CancellationTokenSource? _healthWatchCts;
    private bool _projectionStale;
    private string? _coreWorkingDirectory;
    private string? _coreBinaryPath;
    private string? _coreBindAddress;
    private const int StreamReconnectMaxAttempts = 5;
    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromSeconds(3);

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
        _preferencesStore = new UserPreferencesStore();
        _preferences = _preferencesStore.Load();
        _projectionSettings.SetTopN(_preferences.TopN);
        _projectionSettings.TopNChanged += OnProjectionTopNChanged;
        _signalFeed = new SignalFeedState();
        _liveProjectionState.OverviewUpdated += OnLiveOverviewForSignals;
        NavigationItems = new ReadOnlyCollection<AppNavigationItemViewModel>(
            [
                new AppNavigationItemViewModel { Id = "source", Label = "Source", CompactLabel = "Src" },
                new AppNavigationItemViewModel { Id = "overview", Label = "Overview", CompactLabel = "Ovr" },
                new AppNavigationItemViewModel { Id = "inspect", Label = "Inspect", CompactLabel = "Insp" },
                new AppNavigationItemViewModel { Id = "signals", Label = "Signals", CompactLabel = "Sig" },
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
            ["signals"] = new AppShellPageViewModel
            {
                Id = "signals",
                Title = "Signals",
                Description = "Behavior signals for thresholds, watched hosts, and known-bad traffic.",
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
        OverviewPage.OpenInspectRequested += () =>
        {
            Navigate("inspect");
            ConnectionMessage = OverviewPage.HasForensicsFocus
                ? $"Inspect opened from forensics focus ({OverviewPage.ForensicsFocusLabel})"
                : "Inspect opened from Overview";
        };
        OverviewPage.RankingPivotRequested += OnOverviewRankingPivot;
        InspectPage = new InspectPageViewModel(
            _projectionClient,
            _liveProjectionState,
            _projectionSettings
        );
        SignalsPage = new SignalsPageViewModel(_signalFeed);
        SignalsPage.PivotRequested += OnSignalPivotRequested;
        _signalFeed.Changed += () =>
        {
            OnPropertyChanged(nameof(SignalUnreadCount));
            OnPropertyChanged(nameof(HasSignalUnread));
            OnPropertyChanged(nameof(SignalUnreadLabel));
        };
        SettingsPage = new SettingsPageViewModel(
            _coreHealthService,
            _discoveryClient,
            _projectionSettings,
            _bindAddress,
            _bindAddressSource,
            LatestCoreError,
            _preferences,
            SavePreferencesAsync
        );
        SettingsPage.ReconnectCoreHandler = ReconnectCoreAsync;
        SettingsPage.SignalSnapshotProvider = () => _signalFeed.Signals.ToArray();
        SettingsPage.UiDensityChanged += OnSettingsUiDensityChanged;
        IsCompactDensity = SettingsPage.IsCompactDensity;
        ThumbnailPage = new ThumbnailViewModel(_liveProjectionState, this);

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

    public string Subtitle { get; } = "Traffic Flow Warden · Public Beta";

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

    public SignalsPageViewModel SignalsPage { get; }

    public SettingsPageViewModel SettingsPage { get; }

    public int SignalUnreadCount => _signalFeed.UnreadCount;

    public bool HasSignalUnread => SignalUnreadCount > 0;

    public string SignalUnreadLabel =>
        SignalUnreadCount > 99 ? "99+" : SignalUnreadCount.ToString();

    public SignalFeedState SignalFeed => _signalFeed;

    [ObservableProperty]
    private string connectionMessage = "Connecting to flowarden core...";

    [ObservableProperty]
    private CoreErrorDto? latestCoreError;

    [ObservableProperty]
    private bool isRailCollapsed;

    /// <summary>Compact always-on-top monitoring chrome (shared live projection).</summary>
    [ObservableProperty]
    private bool isThumbnailMode;

    /// <summary>UI density: compact rows/padding when true.</summary>
    [ObservableProperty]
    private bool isCompactDensity;

    public bool IsComfortableDensity => !IsCompactDensity;

    /// <summary>Always created with the shell so thumbnail bindings never see a null DataContext.</summary>
    public ThumbnailViewModel ThumbnailPage { get; }

    public event Action? ThumbnailModeChanged;

    /// <summary>User-facing run state: loading / ready / running / paused / offline / stale / failed.</summary>
    [ObservableProperty]
    private string userRunState = "loading";

    public string HeaderSupportingText =>
        LatestCoreError?.Message
        ?? (_projectionStale
            ? "Projection may be stale — core connection is degraded."
            : CoreStatus.Value == "Connected"
                ? CurrentPage.Description
                : ConnectionMessage);

    public bool IsSourcePageActive => CurrentPageId == "source";

    public bool IsNotSourcePageActive => !IsSourcePageActive;

    public bool IsOverviewPageActive => CurrentPageId == "overview";

    public bool IsNotOverviewPageActive => !IsOverviewPageActive;

    public bool IsInspectPageActive => CurrentPageId == "inspect";

    public bool IsNotInspectPageActive => !IsInspectPageActive;

    public bool IsSignalsPageActive => CurrentPageId == "signals";

    public bool IsSettingsPageActive => CurrentPageId == "settings";

    public bool HasLatestCoreError => LatestCoreError is not null;

    public string RailToggleLabel => IsRailCollapsed ? "Expand" : "Collapse";

    public double RailWidth => IsRailCollapsed ? 96 : 180;

    public bool IsNotThumbnailMode => !IsThumbnailMode;

    public UserPreferences Preferences => _preferences;

    public UserPreferencesStore PreferencesStore => _preferencesStore;

    public LiveProjectionState LiveProjection => _liveProjectionState;

    partial void OnIsThumbnailModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotThumbnailMode));
        // Refresh compact metrics immediately when entering thumbnail chrome.
        if (value)
        {
            ThumbnailPage.RefreshFromCurrentProjection();
        }

        ThumbnailModeChanged?.Invoke();
    }

    [RelayCommand]
    private void EnterThumbnail()
    {
        if (!IsThumbnailMode)
        {
            IsThumbnailMode = true;
        }
    }

    [RelayCommand]
    private void ExitThumbnail()
    {
        if (IsThumbnailMode)
        {
            IsThumbnailMode = false;
        }
    }

    [RelayCommand]
    private void ToggleThumbnail()
    {
        IsThumbnailMode = !IsThumbnailMode;
    }

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
        OnPropertyChanged(nameof(IsNotInspectPageActive));
        OnPropertyChanged(nameof(IsSignalsPageActive));
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
            "signals" => "signals",
            "settings" => "settings",
            _ => "overview",
        };
    }

    private void OnLiveOverviewForSignals(OverviewSnapshotDto snapshot)
    {
        _signalFeed.ObserveOverview(snapshot, _preferences);
        SettingsPage?.UpdateCaptureDiagnostics(snapshot, UserRunState);
    }

    private void OnOverviewRankingPivot(string pivotKind, string pivotValue)
    {
        Navigate("inspect");
        _ = InspectPage.ApplyPivotAsync(pivotKind, pivotValue);
        ConnectionMessage = $"Inspect pivot: {pivotKind} = {pivotValue}";
    }

    private void OnSignalPivotRequested(SignalItemDto signal)
    {
        // Offline findings: Overview timeline marker + rankings, then Inspect filters.
        var isOfflineFinding = string.Equals(
            signal.Mode,
            "offline",
            StringComparison.OrdinalIgnoreCase
        );

        if (isOfflineFinding)
        {
            OverviewPage.ApplyForensicsFocus(
                signal.PivotKind,
                string.IsNullOrWhiteSpace(signal.PivotValue) ? signal.Subject : signal.PivotValue,
                signal.Timestamp
            );
            // Show timeline marker first so analysts see when the finding occurred.
            Navigate("overview");
            _ = InspectPage.ApplyPivotAsync(signal.PivotKind, signal.PivotValue);
            ConnectionMessage = signal.CanPivot
                ? $"Replay: timeline @ {signal.Timestamp:HH:mm:ss} · focus {signal.PivotKind}={signal.PivotValue} (Inspect filters ready)"
                : $"Replay finding on Overview timeline · {signal.Title}";
            return;
        }

        Navigate("inspect");
        _ = InspectPage.ApplyPivotAsync(signal.PivotKind, signal.PivotValue);
        if (signal.CanPivot)
        {
            ConnectionMessage = $"Inspect pivot: {signal.PivotKind} = {signal.PivotValue}";
        }
        else
        {
            ConnectionMessage = $"Opened Inspect from signal: {signal.Title}";
        }
    }

    private async Task<string> SavePreferencesAsync()
    {
        // SettingsPage may not be assigned yet if a property changed during its constructor.
        if (SettingsPage is null)
        {
            return "Settings page not ready.";
        }

        _preferences.TopN = _projectionSettings.TopN;
        _preferences.ShutdownCoreWhenUiCloses = SettingsPage.ShutdownCoreWhenUiCloses;
        _preferences.DataThresholdBytes = SettingsPage.DataThresholdBytes;
        _preferences.WatchedHosts = SettingsPageViewModel.ParseHostList(SettingsPage.WatchedHostsInput);
        _preferences.KnownBadHosts = SettingsPageViewModel.ParseHostList(SettingsPage.KnownBadHostsInput);
        _preferences.DesktopNotificationsEnabled = SettingsPage.DesktopNotificationsEnabled;
        _preferences.SignalSoundEnabled = SettingsPage.SignalSoundEnabled;
        _preferences.UiDensity = SettingsPage.IsCompactDensity ? "compact" : "comfortable";
        IsCompactDensity = SettingsPage.IsCompactDensity;
        _preferences.SyslogEnabled = SettingsPage.SyslogEnabled;
        _preferences.SyslogTarget = SettingsPage.SyslogTarget?.Trim() ?? string.Empty;
        _preferences.SyslogProto = string.IsNullOrWhiteSpace(SettingsPage.SyslogProto)
            ? "udp"
            : SettingsPage.SyslogProto.Trim();
        _preferences.SyslogEmitSignals = SettingsPage.SyslogEmitSignals;
        _preferences.SyslogEmitFlows = SettingsPage.SyslogEmitFlows;
        if (ulong.TryParse(SettingsPage.SyslogFlowMinBytesInput?.Trim(), out var minB))
        {
            _preferences.SyslogFlowMinBytes = minB;
        }

        if (ulong.TryParse(SettingsPage.SyslogFlowDeltaBytesInput?.Trim(), out var deltaB))
        {
            _preferences.SyslogFlowDeltaBytes = deltaB;
        }

        if (ulong.TryParse(SettingsPage.SyslogFlowIntervalSecsInput?.Trim(), out var intervalS))
        {
            _preferences.SyslogFlowIntervalSecs = intervalS;
        }

        try
        {
            _preferencesStore.Save(_preferences);
        }
        catch (Exception ex)
        {
            return $"Local save failed: {ex.Message}";
        }

        var policyResult = await PushSignalPolicyToCoreAsync();
        var syslogResult = await PushSyslogConfigToCoreAsync();
        var watched = _preferences.WatchedHosts.Count;
        var bad = _preferences.KnownBadHosts.Count;
        return
            $"Saved · threshold={_preferences.DataThresholdBytes:N0} · watched={watched} · known-bad={bad} · topN={_preferences.TopN} · policy: {policyResult} · syslog: {syslogResult}";
    }

    private async Task<string> PushSyslogConfigToCoreAsync()
    {
        if (_controlClient is null)
        {
            return "no control client";
        }

        try
        {
            var result = await _controlClient.SetSyslogConfigAsync(
                _preferences.SyslogEnabled,
                _preferences.SyslogTarget,
                _preferences.SyslogProto,
                _preferences.SyslogEmitSignals,
                _preferences.SyslogEmitFlows,
                _preferences.SyslogFlowMinBytes,
                _preferences.SyslogFlowDeltaBytes,
                _preferences.SyslogFlowIntervalSecs
            );
            return result.Accepted ? result.Message : $"declined: {result.Message}";
        }
        catch (Exception ex)
        {
            return $"error: {ex.Message}";
        }
    }

    private async Task<string> PushSignalPolicyToCoreAsync()
    {
        if (_controlClient is null)
        {
            return "core client not wired (local only)";
        }

        try
        {
            var result = await _controlClient.SetSignalPolicyAsync(
                _preferences.DataThresholdBytes,
                _preferences.WatchedHosts,
                _preferences.KnownBadHosts
            );
            if (!result.Accepted)
            {
                return string.IsNullOrWhiteSpace(result.Message)
                    ? "policy rejected"
                    : result.Message;
            }

            return string.IsNullOrWhiteSpace(result.Message) ? "policy applied" : result.Message;
        }
        catch (Exception ex)
        {
            return $"policy push failed ({ex.Message})";
        }
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

        _coreWorkingDirectory = workingDirectory;
        _coreBinaryPath = binaryPath;
        _coreBindAddress = bindAddress;
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
            var corePath = string.IsNullOrWhiteSpace(result.BinaryPath)
                ? binaryPath
                : result.BinaryPath;
            ConnectionMessage = result.LaunchedByUi
                ? $"flowarden core launched: {corePath}"
                : $"Connected to existing core (may be outdated). Preferred binary: {corePath}";
            UserRunState = "ready";
            _projectionStale = false;
            await PushSignalPolicyToCoreAsync();
            await LoadSourcePageAsync();
            await LoadOverviewPageAsync();
            await LoadInspectPageAsync();
            await LoadSettingsPageAsync();
            await StartCaptureOnLaunchAsync();
            ApplyInitialPageSelection();
            BeginHealthWatch();
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
        UserRunState = "offline";
        ConnectionMessage = result.Error?.Message ?? "Failed to connect to flowarden core.";
    }

    [RelayCommand]
    private Task ReconnectCore() => ReconnectCoreAsync();

    private async Task ReconnectCoreAsync()
    {
        if (_coreConnectionCoordinator is null
            || string.IsNullOrWhiteSpace(_coreWorkingDirectory)
            || string.IsNullOrWhiteSpace(_coreBinaryPath)
            || string.IsNullOrWhiteSpace(_coreBindAddress))
        {
            ConnectionMessage = "Cannot reconnect: core launch paths are not configured.";
            UserRunState = "failed";
            return;
        }

        UserRunState = "loading";
        ConnectionMessage = "Reconnecting to flowarden core...";
        // Safety default: reconnect core only — do not auto-restart capture.
        StopOverviewStreaming();
        StopHealthWatch();
        var result = await _coreConnectionCoordinator.EnsureConnectedAsync(
            _coreWorkingDirectory,
            _coreBinaryPath,
            _coreBindAddress
        );

        if (result.Connected && result.Health is not null)
        {
            if (result.LaunchedByUi)
            {
                _launchedCoreProcess = result.LaunchedProcess;
                _launchedCoreByUi = true;
                SettingsPage?.NoteCoreRelaunch();
            }

            LatestCoreError = null;
            _projectionStale = false;
            CoreStatus = new StatusIndicatorViewModel
            {
                Label = "Core",
                Value = "Connected",
                Tone = "good",
            };
            UserRunState = "ready";
            ConnectionMessage = result.LaunchedByUi
                ? "flowarden core relaunched. Start capture again when ready."
                : "Reconnected to flowarden core. Start capture again when ready.";
            OnPropertyChanged(nameof(HeaderSupportingText));
            BeginHealthWatch();
            return;
        }

        LatestCoreError = result.Error;
        UserRunState = "offline";
        CoreStatus = new StatusIndicatorViewModel
        {
            Label = "Core",
            Value = "Offline",
            Tone = "warning",
        };
        ConnectionMessage = result.Error?.Message ?? "Failed to reconnect to flowarden core.";
        OnPropertyChanged(nameof(HeaderSupportingText));
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
            "paused" => ("Paused", "warning"),
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
            // New capture session: clear prior signal feed so offline findings don't mix with live.
            _signalFeed.Clear();
            OverviewPage.ClearForensicsFocus();
            ResetProjectionForSession(session);
            return;
        }

        if (status is "running" or "paused")
        {
            // Keep the shared live projection stream while paused so Overview /
            // Inspect stay on the frozen last snapshot without re-subscribing.
            BeginOverviewStreaming();
            UserRunState = status;
        }

        if (status == "stopping")
        {
            StopOverviewStreaming();
            UserRunState = "stopping";
        }

        if (status == "idle" && CoreStatus.Value == "Connected")
        {
            UserRunState = "ready";
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
        var attempts = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await foreach (
                        var snapshot in _projectionClient.StreamOverviewAsync(
                            _projectionSettings.TopN,
                            cancellationToken
                        )
                    )
                    {
                        attempts = 0;
                        _projectionStale = false;
                        _liveProjectionState.UpdateOverview(snapshot);
                        ApplyCaptureStatusFromOverview(snapshot);
                    }

                    // Stream completed without cancel — try reconnect while capture is active.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // fall through to reconnect / degrade
                }

                attempts++;
                if (attempts > StreamReconnectMaxAttempts)
                {
                    _projectionStale = true;
                    UserRunState = "stale";
                    ConnectionMessage =
                        "Live projection stream lost after repeated reconnect attempts. Showing last snapshot.";
                    OnPropertyChanged(nameof(HeaderSupportingText));
                    await TryPollLatestOverviewAsync(cancellationToken);
                    break;
                }

                ConnectionMessage =
                    $"Live projection interrupted; reconnecting ({attempts}/{StreamReconnectMaxAttempts})...";
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(400 * attempts), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_liveOverviewCts, streamCts))
            {
                _liveOverviewCts = null;
            }

            streamCts.Dispose();
        }
    }

    private async Task TryPollLatestOverviewAsync(CancellationToken cancellationToken)
    {
        if (_projectionClient is null)
        {
            return;
        }

        try
        {
            var snapshot = await _projectionClient.GetLatestOverviewAsync(
                _projectionSettings.TopN,
                cancellationToken
            );
            _liveProjectionState.UpdateOverview(snapshot);
        }
        catch
        {
            // Keep last known projection.
        }
    }

    private void BeginHealthWatch()
    {
        if (_coreHealthService is null || _healthWatchCts is not null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _healthWatchCts = cts;
        _ = WatchCoreHealthAsync(cts.Token);
    }

    private void StopHealthWatch()
    {
        _healthWatchCts?.Cancel();
        _healthWatchCts = null;
    }

    private async Task WatchCoreHealthAsync(CancellationToken cancellationToken)
    {
        if (_coreHealthService is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(HealthPollInterval, cancellationToken);
                var health = await _coreHealthService.GetHealthAsync(cancellationToken);
                if (health is null)
                {
                    MarkCoreOffline("Resident core stopped responding to health checks.");
                    continue;
                }

                if (CoreStatus.Value != "Connected")
                {
                    CoreStatus = new StatusIndicatorViewModel
                    {
                        Label = "Core",
                        Value = "Connected",
                        Tone = "good",
                    };
                    if (_projectionStale)
                    {
                        ConnectionMessage = "Core is reachable again. Reconnect live projection if capture is active.";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                MarkCoreOffline("Lost connection to the resident flowarden core.");
            }
        }
    }

    private void MarkCoreOffline(string message)
    {
        CoreStatus = new StatusIndicatorViewModel
        {
            Label = "Core",
            Value = "Offline",
            Tone = "warning",
        };
        UserRunState = "offline";
        _projectionStale = true;
        ConnectionMessage = message;
        LatestCoreError = new CoreErrorDto
        {
            Source = "CoreHealth",
            Reason = "RuntimeDisconnect",
            Message = message,
        };
        StopOverviewStreaming();
        OnPropertyChanged(nameof(HeaderSupportingText));
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

    private void OnSettingsUiDensityChanged()
    {
        IsCompactDensity = SettingsPage.IsCompactDensity;
        OnPropertyChanged(nameof(IsComfortableDensity));
    }

    partial void OnIsCompactDensityChanged(bool value)
    {
        OnPropertyChanged(nameof(IsComfortableDensity));
    }

    private void OnProjectionTopNChanged(uint topN)
    {
        _preferences.TopN = topN;
        _preferencesStore.Save(_preferences);

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
