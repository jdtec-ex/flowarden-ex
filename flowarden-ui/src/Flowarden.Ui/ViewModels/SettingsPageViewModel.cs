using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flowarden.Ui.Models;
using Flowarden.Ui.Services;
using Flowarden.Ui.State;

namespace Flowarden.Ui.ViewModels;

public sealed partial class SettingsPageViewModel : ViewModelBase
{
    private readonly CoreHealthService? _coreHealthService;
    private readonly DiscoveryClient? _discoveryClient;
    private readonly ProjectionSettingsState _projectionSettings;
    private readonly Func<Task<string>>? _savePreferencesAsync;
    private readonly bool _isDesignTime;
    private bool _suppressPreferenceSave;

    /// <summary>Optional shell handler for full core reconnect / relaunch.</summary>
    public Func<Task>? ReconnectCoreHandler { get; set; }

    /// <summary>Optional save-file picker; returns absolute path or null if cancelled.</summary>
    public Func<string, string, Task<string?>>? SaveDiagnosticsFileHandler { get; set; }

    /// <summary>Optional provider for current signal feed snapshot (export).</summary>
    public Func<IReadOnlyList<SignalItemDto>>? SignalSnapshotProvider { get; set; }

    public SettingsPageViewModel()
        : this(
            coreHealthService: null,
            discoveryClient: null,
            projectionSettings: new ProjectionSettingsState(),
            bindAddress: "not configured",
            bindAddressSource: "design-time",
            latestCoreError: null,
            preferences: new UserPreferences(),
            savePreferencesAsync: null,
            isDesignTime: true
        )
    {
    }

    public SettingsPageViewModel(
        CoreHealthService? coreHealthService,
        DiscoveryClient? discoveryClient,
        ProjectionSettingsState projectionSettings,
        string bindAddress,
        string bindAddressSource,
        CoreErrorDto? latestCoreError,
        UserPreferences preferences,
        Func<Task<string>>? savePreferencesAsync
    )
        : this(
            coreHealthService,
            discoveryClient,
            projectionSettings,
            bindAddress,
            bindAddressSource,
            latestCoreError,
            preferences,
            savePreferencesAsync,
            isDesignTime: false
        )
    {
    }

    private SettingsPageViewModel(
        CoreHealthService? coreHealthService,
        DiscoveryClient? discoveryClient,
        ProjectionSettingsState projectionSettings,
        string bindAddress,
        string bindAddressSource,
        CoreErrorDto? latestCoreError,
        UserPreferences preferences,
        Func<Task<string>>? savePreferencesAsync,
        bool isDesignTime
    )
    {
        _coreHealthService = coreHealthService;
        _discoveryClient = discoveryClient;
        _projectionSettings = projectionSettings;
        _savePreferencesAsync = savePreferencesAsync;
        _isDesignTime = isDesignTime;
        CoreEndpoint = bindAddress;
        CoreEndpointSource = bindAddressSource;
        UiVersion = "0.1.0-phase2";
        TickInterval = "1s";
        TopNInput = _projectionSettings.TopN.ToString();
        TopNStatus = $"Applied Top N: {_projectionSettings.TopN}";
        DataThresholdInput = preferences.DataThresholdBytes.ToString();
        WatchedHostsInput = string.Join(", ", preferences.WatchedHosts);
        KnownBadHostsInput = string.Join(", ", preferences.KnownBadHosts);
        RuntimeState = CreateSeedRuntimeState();
        CoreHealth = CreateSeedCoreHealth();
        Diagnostics = new ReadOnlyCollection<CoreErrorDto>(CreateDiagnostics(latestCoreError));
        ProcessState = CoreHealth.Status == "ok" ? "Running" : "Offline";
        CoreVersion = "unknown";
        // Property setters may raise OnXxxChanged and invoke save; suppress until ctor finishes
        // so AppShell.SavePreferencesAsync does not run before SettingsPage is assigned.
        _suppressPreferenceSave = true;
        DesktopNotificationsEnabled = preferences.DesktopNotificationsEnabled;
        SignalSoundEnabled = preferences.SignalSoundEnabled;
        ShutdownCoreWhenUiCloses = preferences.ShutdownCoreWhenUiCloses;
        _suppressPreferenceSave = false;
    }

    public CaptureSessionStateDto RuntimeState { get; private set; }

    public CoreHealthDto CoreHealth { get; private set; }

    public IReadOnlyList<CoreErrorDto> Diagnostics { get; private set; }

    public string CoreEndpoint { get; }

    public string CoreEndpointSource { get; }

    public string ProcessState { get; private set; }

    public string CoreVersion { get; private set; }

    public string UiVersion { get; }

    public string TickInterval { get; }

    public uint TopN => _projectionSettings.TopN;

    [ObservableProperty]
    private string topNInput;

    [ObservableProperty]
    private string topNStatus;

    [ObservableProperty]
    private bool shutdownCoreWhenUiCloses;

    [ObservableProperty]
    private string dataThresholdInput = "50000000";

    [ObservableProperty]
    private string watchedHostsInput = string.Empty;

    [ObservableProperty]
    private string knownBadHostsInput = string.Empty;

    [ObservableProperty]
    private string listsStatus =
        "Edit lists / threshold, then Apply to save and push policy to core.";

    [ObservableProperty]
    private bool desktopNotificationsEnabled = true;

    [ObservableProperty]
    private bool signalSoundEnabled;

    [ObservableProperty]
    private bool isSavingPreferences;

    [ObservableProperty]
    private string droppedPacketsLabel = "—";

    [ObservableProperty]
    private string lastPacketAgeLabel = "—";

    [ObservableProperty]
    private string streamStateLabel = "idle";

    [ObservableProperty]
    private string coreUptimeLabel = "—";

    [ObservableProperty]
    private string coreRestartCountLabel = "0";

    [ObservableProperty]
    private string processLookupLabel = "—";

    [ObservableProperty]
    private string captureQualitySummary = "Waiting for capture metrics…";

    [ObservableProperty]
    private string diagnosticsExportStatus = "Export writes a JSON snapshot of capture quality metrics.";

    private int _coreRestartCount;
    private OverviewSnapshotDto? _lastDiagnosticsSnapshot;

    public ulong DataThresholdBytes =>
        ulong.TryParse(DataThresholdInput?.Trim() ?? string.Empty, out var value)
            ? value
            : 50_000_000UL;

    public static List<string> ParseHostList(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new List<string>();
        }

        return input
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [RelayCommand]
    private async Task ApplyTopN()
    {
        if (!uint.TryParse(TopNInput?.Trim() ?? string.Empty, out var parsed))
        {
            TopNInput = _projectionSettings.TopN.ToString();
            TopNStatus = "Top N must be a number from 1 to 100.";
            ListsStatus = TopNStatus;
            return;
        }

        var normalized = ProjectionSettingsState.NormalizeTopN(parsed);
        _projectionSettings.SetTopN(normalized);
        TopNInput = normalized.ToString();
        TopNStatus = normalized == parsed
            ? $"Applied Top N: {normalized}"
            : $"Applied Top N: {normalized} (clamped to 1–100)";
        OnPropertyChanged(nameof(TopN));
        ListsStatus = await PersistPreferencesAsync();
    }

    [RelayCommand]
    private async Task SaveWatchlists()
    {
        if (!ulong.TryParse(DataThresholdInput?.Trim() ?? string.Empty, out var threshold))
        {
            ListsStatus = "Data threshold must be a non-negative integer (bytes).";
            return;
        }

        // Also apply Top N if the field is a valid number, so one Apply covers the panel.
        if (uint.TryParse(TopNInput?.Trim() ?? string.Empty, out var topNParsed))
        {
            var normalized = ProjectionSettingsState.NormalizeTopN(topNParsed);
            _projectionSettings.SetTopN(normalized);
            TopNInput = normalized.ToString();
            TopNStatus = $"Applied Top N: {normalized}";
            OnPropertyChanged(nameof(TopN));
        }

        var watched = ParseHostList(WatchedHostsInput);
        var bad = ParseHostList(KnownBadHostsInput);
        ListsStatus = $"Applying… threshold={threshold:N0}, watched={watched.Count}, known-bad={bad.Count}";
        ListsStatus = await PersistPreferencesAsync();
    }

    private async Task<string> PersistPreferencesAsync()
    {
        if (_isDesignTime)
        {
            return "Design-time: preferences not persisted.";
        }

        if (_savePreferencesAsync is null)
        {
            return "Save handler not wired.";
        }

        if (IsSavingPreferences)
        {
            return ListsStatus;
        }

        try
        {
            IsSavingPreferences = true;
            return await _savePreferencesAsync();
        }
        catch (Exception ex)
        {
            return $"Save failed: {ex.Message}";
        }
        finally
        {
            IsSavingPreferences = false;
        }
    }

    partial void OnShutdownCoreWhenUiClosesChanged(bool value)
    {
        if (_suppressPreferenceSave || _isDesignTime)
        {
            return;
        }

        _ = PersistPreferencesAsync();
    }

    partial void OnDesktopNotificationsEnabledChanged(bool value)
    {
        if (_suppressPreferenceSave || _isDesignTime)
        {
            return;
        }

        _ = PersistLocalPreferencesOnlyAsync();
    }

    partial void OnSignalSoundEnabledChanged(bool value)
    {
        if (_suppressPreferenceSave || _isDesignTime)
        {
            return;
        }

        _ = PersistLocalPreferencesOnlyAsync();
    }

    private async Task PersistLocalPreferencesOnlyAsync()
    {
        // Toast/sound are UI-local; still persist file without requiring core policy.
        if (_savePreferencesAsync is null || _isDesignTime)
        {
            return;
        }

        try
        {
            ListsStatus = await _savePreferencesAsync();
        }
        catch
        {
            // Ignore background toggle save failures.
        }
    }

    [RelayCommand]
    private async Task ReconnectCore()
    {
        if (ReconnectCoreHandler is not null)
        {
            await ReconnectCoreHandler();
            await LoadAsync();
            return;
        }

        await RefreshCoreHealthAsync();
    }

    private async Task RefreshCoreHealthAsync()
    {
        if (_isDesignTime || _coreHealthService is null)
        {
            return;
        }

        try
        {
            var health = await _coreHealthService.GetHealthAsync();
            if (health is null)
            {
                ProcessState = "Offline";
                TopNStatus = "Core health check returned no response.";
            }
            else
            {
                CoreHealth = health;
                ProcessState = string.Equals(health.Status, "ok", StringComparison.OrdinalIgnoreCase)
                    ? "Running"
                    : "Degraded";
                CoreVersion = "0.1.0";
                OnPropertyChanged(nameof(CoreHealth));
                OnPropertyChanged(nameof(ProcessState));
                OnPropertyChanged(nameof(CoreVersion));
                OnPropertyChanged(nameof(StartedAtLabel));
                TopNStatus = "Core health refreshed.";
            }
        }
        catch (Exception ex)
        {
            ProcessState = "Offline";
            OnPropertyChanged(nameof(ProcessState));
            TopNStatus = "Core health check failed.";
            _ = ex;
        }
    }

    public string StartedAtLabel => DateTimeOffset.FromUnixTimeSeconds((long)CoreHealth.StartedAtUnixSeconds)
        .ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>Record a UI-driven core relaunch for diagnostics.</summary>
    public void NoteCoreRelaunch()
    {
        _coreRestartCount = checked(_coreRestartCount + 1);
        CoreRestartCountLabel = _coreRestartCount.ToString();
    }

    /// <summary>Update capture-quality diagnostics from live/offline overview + shell run state.</summary>
    public void UpdateCaptureDiagnostics(OverviewSnapshotDto? snapshot, string streamState)
    {
        _lastDiagnosticsSnapshot = snapshot;
        StreamStateLabel = string.IsNullOrWhiteSpace(streamState) ? "idle" : streamState;
        CoreRestartCountLabel = _coreRestartCount.ToString();

        if (CoreHealth.StartedAtUnixSeconds > 0)
        {
            var started = DateTimeOffset.FromUnixTimeSeconds((long)CoreHealth.StartedAtUnixSeconds);
            var uptime = DateTimeOffset.UtcNow - started.ToUniversalTime();
            CoreUptimeLabel = uptime.TotalHours >= 1
                ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m"
                : $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
        }
        else
        {
            CoreUptimeLabel = "—";
        }

        if (snapshot is null)
        {
            DroppedPacketsLabel = "—";
            LastPacketAgeLabel = "—";
            ProcessLookupLabel = "—";
            CaptureQualitySummary =
                $"Stream {StreamStateLabel} · restarts {CoreRestartCountLabel} · no overview yet";
            return;
        }

        DroppedPacketsLabel = snapshot.DroppedPackets.ToString("N0");
        ProcessLookupLabel =
            $"q={snapshot.ProcessLookupPending} · cache={snapshot.ProcessLookupCacheSize}";
        if (snapshot.LastPacketTimestamp is { Seconds: > 0 } last)
        {
            var lastAt = DateTimeOffset.FromUnixTimeSeconds(last.Seconds);
            var age = DateTimeOffset.UtcNow - lastAt.ToUniversalTime();
            LastPacketAgeLabel = age.TotalSeconds < 0
                ? "0s"
                : age.TotalMinutes >= 1
                    ? $"{(int)age.TotalMinutes}m {age.Seconds}s"
                    : $"{(int)age.TotalSeconds}s";
        }
        else
        {
            LastPacketAgeLabel = "n/a";
        }

        CaptureQualitySummary =
            $"{snapshot.Mode} · dropped {DroppedPacketsLabel} · last pkt {LastPacketAgeLabel} · stream {StreamStateLabel} · proc {ProcessLookupLabel} · restarts {CoreRestartCountLabel}";
    }

    [RelayCommand]
    private async Task ExportDiagnostics()
    {
        if (_isDesignTime)
        {
            DiagnosticsExportStatus = "Design-time: export disabled.";
            return;
        }

        var payload = BuildDiagnosticsExportJson();
        var suggestedName = $"flowarden-diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json";

        try
        {
            if (SaveDiagnosticsFileHandler is not null)
            {
                var path = await SaveDiagnosticsFileHandler(suggestedName, payload);
                DiagnosticsExportStatus = string.IsNullOrWhiteSpace(path)
                    ? "Export cancelled."
                    : $"Exported diagnostics to {path}";
                return;
            }

            // Fallback: write under Application Support/Flowarden/exports
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Flowarden",
                "exports"
            );
            Directory.CreateDirectory(root);
            var fullPath = Path.Combine(root, suggestedName);
            await File.WriteAllTextAsync(fullPath, payload, Encoding.UTF8);
            DiagnosticsExportStatus = $"Exported diagnostics to {fullPath}";
        }
        catch (Exception ex)
        {
            DiagnosticsExportStatus = $"Export failed: {ex.Message}";
        }
    }

    private string BuildDiagnosticsExportJson()
    {
        var snapshot = _lastDiagnosticsSnapshot;
        var document = new
        {
            exportedAt = DateTimeOffset.Now.ToString("O"),
            core = new
            {
                endpoint = CoreEndpoint,
                endpointSource = CoreEndpointSource,
                version = CoreVersion,
                processState = ProcessState,
                health = CoreHealth.Status,
                startedAt = StartedAtLabel,
                uptime = CoreUptimeLabel,
                restartCount = _coreRestartCount,
            },
            capture = new
            {
                streamState = StreamStateLabel,
                droppedPackets = DroppedPacketsLabel,
                lastPacketAge = LastPacketAgeLabel,
                processLookup = ProcessLookupLabel,
                summary = CaptureQualitySummary,
                mode = snapshot?.Mode,
                captureId = snapshot?.CaptureId,
                captureStatus = snapshot?.CaptureStatus,
                sourceLabel = snapshot?.SourceLabel,
                filterLabel = snapshot?.FilterLabel,
                totals = snapshot is null
                    ? null
                    : new
                    {
                        packets = snapshot.Totals.Packets,
                        bytes = snapshot.Totals.Bytes,
                        bytesIn = snapshot.Totals.BytesIn,
                        bytesOut = snapshot.Totals.BytesOut,
                    },
                processLookupPending = snapshot?.ProcessLookupPending,
                processLookupCacheSize = snapshot?.ProcessLookupCacheSize,
            },
            preferences = new
            {
                topN = TopN,
                dataThresholdBytes = DataThresholdBytes,
                watchedCount = ParseHostList(WatchedHostsInput).Count,
                knownBadCount = ParseHostList(KnownBadHostsInput).Count,
                desktopNotifications = DesktopNotificationsEnabled,
                signalSound = SignalSoundEnabled,
            },
            diagnostics = Diagnostics
                .Select(d => new { d.Source, d.Reason, d.Message })
                .ToArray(),
            runtime = new
            {
                mode = RuntimeState.Mode,
                source = RuntimeState.SourceDisplayName,
                status = RuntimeState.CaptureStatus,
                bpf = RuntimeState.Bpf,
            },
            signals = (SignalSnapshotProvider?.Invoke() ?? Array.Empty<SignalItemDto>())
                .Select(s => new
                {
                    s.Id,
                    s.Kind,
                    s.Title,
                    s.Detail,
                    s.Subject,
                    s.Severity,
                    s.Mode,
                    s.Status,
                    timestamp = s.Timestamp.ToString("O"),
                    s.PivotKind,
                    s.PivotValue,
                    s.IsUnread,
                })
                .ToArray(),
        };

        return JsonSerializer.Serialize(
            document,
            new JsonSerializerOptions { WriteIndented = true }
        );
    }

    public async Task LoadAsync(CoreErrorDto? latestCoreError = null)
    {
        if (_isDesignTime)
        {
            return;
        }

        if (_coreHealthService is not null)
        {
            var health = await _coreHealthService.GetHealthAsync();
            if (health is not null)
            {
                CoreHealth = health;
                ProcessState = string.Equals(health.Status, "ok", StringComparison.OrdinalIgnoreCase)
                    ? "Running"
                    : "Degraded";
                CoreVersion = "0.1.0";
            }
        }

        if (_discoveryClient is not null)
        {
            var devices = await _discoveryClient.GetDevicesAsync();
            var selected = devices.FirstOrDefault();
            RuntimeState = new CaptureSessionStateDto
            {
                SourceKind = selected is null ? "none" : "live",
                SourceDisplayName = selected?.Name ?? "none",
                CaptureStatus = "idle",
                Mode = "live",
                Bpf = null,
            };
        }

        Diagnostics = new ReadOnlyCollection<CoreErrorDto>(CreateDiagnostics(latestCoreError));

        OnPropertyChanged(nameof(RuntimeState));
        OnPropertyChanged(nameof(CoreHealth));
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(ProcessState));
        OnPropertyChanged(nameof(CoreVersion));
        OnPropertyChanged(nameof(StartedAtLabel));
    }

    private static CaptureSessionStateDto CreateSeedRuntimeState()
    {
        return new CaptureSessionStateDto
        {
            SourceKind = "live",
            SourceDisplayName = "en0",
            CaptureStatus = "idle",
            Mode = "live",
            Bpf = "tcp",
        };
    }

    private static CoreHealthDto CreateSeedCoreHealth()
    {
        return new CoreHealthDto
        {
            Status = "ok",
            StartedAtUnixSeconds = 1_714_587_200,
        };
    }

    private static List<CoreErrorDto> CreateDiagnostics(CoreErrorDto? latestCoreError)
    {
        var diagnostics = new List<CoreErrorDto>();

        if (latestCoreError is not null)
        {
            diagnostics.Add(latestCoreError);
        }

        diagnostics.Add(
            new CoreErrorDto
            {
                Source = "Capture",
                Reason = "PermissionHint",
                Message = "Live capture on restricted interfaces may require elevated permissions.",
            }
        );

        return diagnostics;
    }
}
