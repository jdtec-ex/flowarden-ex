using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Flowarden.Ui.Models;
using Flowarden.Ui.Services;

namespace Flowarden.Ui.ViewModels;

public sealed partial class SettingsPageViewModel : ViewModelBase
{
    private readonly CoreHealthService? _coreHealthService;
    private readonly DiscoveryClient? _discoveryClient;
    private readonly bool _isDesignTime;

    public SettingsPageViewModel()
        : this(
            coreHealthService: null,
            discoveryClient: null,
            bindAddress: "127.0.0.1:39091",
            latestCoreError: null,
            isDesignTime: true
        )
    {
    }

    public SettingsPageViewModel(
        CoreHealthService? coreHealthService,
        DiscoveryClient? discoveryClient,
        string bindAddress,
        CoreErrorDto? latestCoreError
    )
        : this(coreHealthService, discoveryClient, bindAddress, latestCoreError, isDesignTime: false)
    {
    }

    private SettingsPageViewModel(
        CoreHealthService? coreHealthService,
        DiscoveryClient? discoveryClient,
        string bindAddress,
        CoreErrorDto? latestCoreError,
        bool isDesignTime
    )
    {
        _coreHealthService = coreHealthService;
        _discoveryClient = discoveryClient;
        _isDesignTime = isDesignTime;
        CoreEndpoint = bindAddress;
        UiVersion = "0.1.0-phase2";
        TickInterval = "1s";
        TopN = "20";
        RuntimeState = CreateSeedRuntimeState();
        CoreHealth = CreateSeedCoreHealth();
        Diagnostics = new ReadOnlyCollection<CoreErrorDto>(CreateDiagnostics(latestCoreError));
        ProcessState = CoreHealth.Status == "ok" ? "Running" : "Offline";
        CoreVersion = "unknown";
        ShutdownCoreWhenUiCloses = false;
    }

    public CaptureSessionStateDto RuntimeState { get; private set; }

    public CoreHealthDto CoreHealth { get; private set; }

    public IReadOnlyList<CoreErrorDto> Diagnostics { get; private set; }

    public string CoreEndpoint { get; }

    public string ProcessState { get; private set; }

    public string CoreVersion { get; private set; }

    public string UiVersion { get; }

    public string TickInterval { get; }

    public string TopN { get; }

    [ObservableProperty]
    private bool shutdownCoreWhenUiCloses;

    public string StartedAtLabel => DateTimeOffset.FromUnixTimeSeconds((long)CoreHealth.StartedAtUnixSeconds)
        .ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm:ss");

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
