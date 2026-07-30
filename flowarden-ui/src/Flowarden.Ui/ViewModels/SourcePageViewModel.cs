using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flowarden.Ui.Models;
using Flowarden.Ui.Services;

using Flowarden.Ui.ViewModels.Source;

namespace Flowarden.Ui.ViewModels;

public sealed partial class SourcePageViewModel : ViewModelBase
{
    private readonly DiscoveryClient? _discoveryClient;
    private readonly ControlClient? _controlClient;
    private readonly bool _isDesignTime;
    private bool _needsInitialActiveSelection = true;
    private const ulong PreviewWindowSeconds = 2;

    public event Action<CaptureSessionStateDto?, string?>? SessionStateChanged;
    public event Func<Task<string?>>? OfflineFileRequested;

    public SourcePageViewModel()
        : this(discoveryClient: null, controlClient: null, isDesignTime: true)
    {
    }

    public SourcePageViewModel(DiscoveryClient? discoveryClient, ControlClient? controlClient = null)
        : this(discoveryClient, controlClient, isDesignTime: false)
    {
    }

    private SourcePageViewModel(
        DiscoveryClient? discoveryClient,
        ControlClient? controlClient,
        bool isDesignTime
    )
    {
        _discoveryClient = discoveryClient;
        _controlClient = controlClient;
        _isDesignTime = isDesignTime;
        DeviceItems = new ObservableCollection<SourceDeviceItemViewModel>();

        SelectedSourceMode = "Live source";
        LastPreviewLabel = $"Preview window: {PreviewWindowSeconds}s sample";
        PreviewStatusLabel = "Preview not started";
        PreviewStatusDetail = "Select one device to review multi-device sampling before formal capture.";
        CurrentSession = new CaptureSessionStateDto
        {
            SourceKind = "none",
            SourceDisplayName = "none",
            CaptureStatus = "idle",
            Mode = "live",
            Bpf = null,
        };

        if (_isDesignTime || _discoveryClient is null)
        {
            LoadSeedDevices();
        }
    }

    public ObservableCollection<SourceDeviceItemViewModel> DeviceItems { get; }

    [ObservableProperty]
    private SourceDeviceItemViewModel? selectedDevice;

    [ObservableProperty]
    private CaptureSessionStateDto currentSession;

    [ObservableProperty]
    private string selectedSourceMode;

    [ObservableProperty]
    private string lastPreviewLabel;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string previewStatusLabel;

    [ObservableProperty]
    private string previewStatusDetail;

    [ObservableProperty]
    private bool previewStateIsWarning;

    [ObservableProperty]
    private bool previewStateIsError;

    [ObservableProperty]
    private bool isControlBusy;

    public bool HasSelectedDevice => SelectedDevice is not null;

    public bool HasDevices => DeviceItems.Count > 0;

    public bool HasNoDevices => !HasDevices;

    public bool CanStartFormalCapture =>
        HasSelectedDevice
        && !IsControlBusy
        && string.Equals(CurrentSession.Mode, "live", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(CurrentSession.CaptureStatus, "running", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(CurrentSession.CaptureStatus, "paused", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(CurrentSession.CaptureStatus, "starting", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(CurrentSession.CaptureStatus, "stopping", StringComparison.OrdinalIgnoreCase);

    public bool CanStopFormalCapture =>
        (
            string.Equals(CurrentSession.CaptureStatus, "running", StringComparison.OrdinalIgnoreCase)
            || string.Equals(CurrentSession.CaptureStatus, "paused", StringComparison.OrdinalIgnoreCase)
        )
        && !IsControlBusy;

    public bool ShowPauseCaptureAction =>
        string.Equals(CurrentSession.CaptureStatus, "running", StringComparison.OrdinalIgnoreCase);

    public bool ShowResumeCaptureAction =>
        string.Equals(CurrentSession.CaptureStatus, "paused", StringComparison.OrdinalIgnoreCase);

    public bool CanPauseFormalCapture => ShowPauseCaptureAction && !IsControlBusy;

    public bool CanResumeFormalCapture => ShowResumeCaptureAction && !IsControlBusy;

    public string FormalCaptureSummary =>
        string.Equals(CurrentSession.Mode, "offline", StringComparison.OrdinalIgnoreCase)
            ? CurrentSession.CaptureStatus.ToLowerInvariant() switch
            {
                "starting" => $"Starting offline replay for {SourceFormatting.FormatOfflineDisplayName(CurrentSession.SourceDisplayName)}.",
                "running" => $"Offline replay is running from {SourceFormatting.FormatOfflineDisplayName(CurrentSession.SourceDisplayName)}.",
                "paused" => $"Offline replay is paused for {SourceFormatting.FormatOfflineDisplayName(CurrentSession.SourceDisplayName)}.",
                "stopping" => $"Offline replay is stopping for {SourceFormatting.FormatOfflineDisplayName(CurrentSession.SourceDisplayName)}.",
                "armed" => $"Offline replay is armed for {SourceFormatting.FormatOfflineDisplayName(CurrentSession.SourceDisplayName)}.",
                _ => CurrentSession.SourceKind == "offline"
                    ? $"Offline replay target: {SourceFormatting.FormatOfflineDisplayName(CurrentSession.SourceDisplayName)}"
                    : "Formal capture requires selecting exactly one source.",
            }
            : SelectedDevice is null
            ? "Formal capture requires selecting exactly one source."
            : CurrentSession.CaptureStatus.ToLowerInvariant() switch
            {
                "starting" => $"Starting resident capture for {SelectedDevice.DisplayName}.",
                "running" => $"Resident capture is running on {CurrentSession.SourceDisplayName}.",
                "paused" => $"Resident capture is paused on {CurrentSession.SourceDisplayName}.",
                "stopping" => $"Resident capture is stopping on {CurrentSession.SourceDisplayName}.",
                "armed" => $"Formal capture is armed for {SelectedDevice.DisplayName}.",
                _ => $"Formal capture target: {SelectedDevice.DisplayName}",
            };

    public string CaptureStateLabel =>
        CurrentSession.CaptureStatus.ToLowerInvariant() switch
        {
            "starting" => "Starting",
            "running" => "Running",
            "paused" => "Paused",
            "stopping" => "Stopping",
            "armed" => "Armed",
            _ => "Idle",
        };

    public string SelectedAddressLabel => SelectedDevice?.PrimaryAddress ?? "not reported";

    public string SelectedIpv6AddressLabel => SelectedDevice?.PrimaryIpv6Address ?? "not reported";

    public string SelectedMacLabel => "not reported";

    public string SelectedMtuLabel => "not reported";

    public string SelectedSpeedLabel => "not reported";

    public string SelectedConfigurationTitle =>
        SelectedDevice is null ? "No interface selected" : $"{SelectedDevice.DisplayName} Configuration";

    public string SelectedDeviceDescriptionLabel =>
        SelectedDevice?.Description ?? "Choose an interface to review capture readiness.";

    public string SelectedPacketsCapturedLabel => SourceFormatting.FormatNumber(SelectedDevice?.Preview.PacketsSeen ?? 0);

    public string SelectedBytesTransferredLabel => SourceFormatting.FormatBytes(SelectedDevice?.Preview.BytesSeen ?? 0);

    public string SelectedAverageRateLabel => SourceFormatting.FormatBitRate(SelectedDevice?.Preview.BytesSeen ?? 0, PreviewWindowSeconds);

    public string SelectedErrorCountLabel => "0";

    public string HeaderRefreshLabel
    {
        get
        {
            const string refreshedPrefix = "Preview refreshed at ";
            if (LastPreviewLabel.StartsWith(refreshedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var refreshedAt = LastPreviewLabel[refreshedPrefix.Length..];
                var deviceSuffixIndex = refreshedAt.IndexOf(" from ", StringComparison.OrdinalIgnoreCase);
                if (deviceSuffixIndex >= 0)
                {
                    refreshedAt = refreshedAt[..deviceSuffixIndex];
                }

                return $"LAST REFRESHED: {refreshedAt}";
            }

            return "LAST REFRESHED: NOT STARTED";
        }
    }

    public string CapturePreferenceModeLabel => "Default";

    public string SnapshotLengthLabel => "65535";

    public string BufferSizeLabel => "2 MB";

    public string BpfFilterLabel => CurrentSession.Bpf ?? string.Empty;

    /// <summary>Editable capture BPF (L1). Effective only on next Start (KD12).</summary>
    [ObservableProperty]
    private string captureBpfInput = string.Empty;

    public const int CaptureBpfMaxLength = 1024;

    public string CaptureBpfHint =>
        IsCaptureRunningOrPaused
            ? "Capture BPF applies on next Start (not while running)."
            : "Optional libpcap BPF. Applied when you Start capture.";

    public string CaptureBpfStatusLabel
    {
        get
        {
            var draft = CaptureBpfInput?.Trim() ?? string.Empty;
            var active = CurrentSession.Bpf?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(draft) && string.IsNullOrEmpty(active))
            {
                return "Capture Filter · none";
            }

            if (IsCaptureRunningOrPaused)
            {
                if (!string.Equals(draft, active, StringComparison.Ordinal))
                {
                    return string.IsNullOrEmpty(draft)
                        ? "Capture Filter · pending clear on next Start"
                        : $"Capture Filter · pending Start · {TruncateBpf(draft)}";
                }

                return string.IsNullOrEmpty(active)
                    ? "Capture Filter · none (active)"
                    : $"Capture Filter · active · {TruncateBpf(active)}";
            }

            return string.IsNullOrEmpty(draft)
                ? "Capture Filter · none"
                : $"Capture Filter · ready · {TruncateBpf(draft)}";
        }
    }

    private bool IsCaptureRunningOrPaused
    {
        get
        {
            var status = CurrentSession.CaptureStatus ?? string.Empty;
            return status.Equals("running", StringComparison.OrdinalIgnoreCase)
                || status.Equals("active", StringComparison.OrdinalIgnoreCase)
                || status.Equals("paused", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string OfflineImportSummary => "Offline import replays one pcap file through the same overview and inspect projections.";

    private static string TruncateBpf(string value) =>
        value.Length <= 48 ? value : value[..45] + "…";

    partial void OnCaptureBpfInputChanged(string value)
    {
        if (value is { Length: > CaptureBpfMaxLength })
        {
            CaptureBpfInput = value[..CaptureBpfMaxLength];
            return;
        }

        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (!string.Equals(CurrentSession.Bpf, trimmed, StringComparison.Ordinal))
        {
            CurrentSession = new CaptureSessionStateDto
            {
                SourceKind = CurrentSession.SourceKind,
                SourceDisplayName = CurrentSession.SourceDisplayName,
                CaptureStatus = CurrentSession.CaptureStatus,
                Mode = CurrentSession.Mode,
                Bpf = trimmed,
            };
        }

        OnPropertyChanged(nameof(BpfFilterLabel));
        OnPropertyChanged(nameof(CaptureBpfStatusLabel));
        OnPropertyChanged(nameof(CaptureBpfHint));
        OnPropertyChanged(nameof(FormalCaptureSummary));
    }

    public Task StartFormalCaptureAsync() => StartFormalCapture();

    public async Task LoadAsync(bool refreshPreview = true)
    {
        if (_discoveryClient is null || _isDesignTime)
        {
            return;
        }

        SetPreviewState("Loading interfaces", "Loading capture interfaces from resident core.", isWarning: false, isError: false);
        StatusMessage = "Loading source inventory...";
        try
        {
            var previousSelection = SelectedDevice?.Device.Name;
            var devices = await _discoveryClient.GetDevicesAsync();
            if (devices.Count == 0)
            {
                DeviceItems.Clear();
                NotifyDeviceInventoryChanged();
                SelectedDevice = null;
                SelectedSourceMode = "Live source";
                LastPreviewLabel = $"Device inventory loaded at {DateTime.Now:HH:mm:ss} from 0 device(s)";
                StatusMessage = "No capture interfaces were returned by the resident core.";
                SetPreviewState(
                    "No interfaces discovered",
                    "The resident core returned an empty capture interface list.",
                    isWarning: true,
                    isError: false
                );
                UpdateCurrentSessionFromSelectedDevice();
                return;
            }

            var preferActiveSelection = _needsInitialActiveSelection || string.IsNullOrWhiteSpace(previousSelection);
            ApplyDeviceInventory(
                devices,
                previousSelection,
                previewByName: null,
                preferActiveSelection
            );
            SelectedSourceMode = "Live source";
            LastPreviewLabel = $"Device inventory loaded at {DateTime.Now:HH:mm:ss} from {DeviceItems.Count} device(s)";
            StatusMessage = refreshPreview
                ? "Device inventory ready; preview samples loading."
                : "Device inventory ready.";
            if (!SourceDeviceSelection.IsCaptureActiveStatus(CurrentSession.CaptureStatus))
            {
                UpdateCurrentSessionFromSelectedDevice();
            }

            if (refreshPreview)
            {
                await RefreshDevicePreviewsAsync(preferActiveSelection);
            }
            _needsInitialActiveSelection = false;
        }
        catch (Exception)
        {
            StatusMessage = "Source inventory unavailable";
            SetPreviewState(
                "Source inventory unavailable",
                "Device inventory failed to return from the resident core. Seed devices are shown so the Source page remains inspectable.",
                isWarning: true,
                isError: false
            );
            LastPreviewLabel = $"Preview window: {PreviewWindowSeconds}s sample";
            LoadSeedDevices();
        }
    }

    partial void OnSelectedDeviceChanged(SourceDeviceItemViewModel? value)
    {
        foreach (var item in DeviceItems)
        {
            item.IsSelected = ReferenceEquals(item, value);
        }

        if (!SourceDeviceSelection.IsCaptureActiveStatus(CurrentSession.CaptureStatus))
        {
            UpdateCurrentSessionFromSelectedDevice();
            UpdatePreviewStateFromSelectedDevice();
        }

        OnPropertyChanged(nameof(HasSelectedDevice));
        OnPropertyChanged(nameof(FormalCaptureSummary));
        OnPropertyChanged(nameof(CanStartFormalCapture));
        OnPropertyChanged(nameof(SelectedAddressLabel));
        OnPropertyChanged(nameof(SelectedIpv6AddressLabel));
        OnPropertyChanged(nameof(SelectedMacLabel));
        OnPropertyChanged(nameof(SelectedMtuLabel));
        OnPropertyChanged(nameof(SelectedSpeedLabel));
        OnPropertyChanged(nameof(SelectedConfigurationTitle));
        OnPropertyChanged(nameof(SelectedDeviceDescriptionLabel));
        OnPropertyChanged(nameof(SelectedPacketsCapturedLabel));
        OnPropertyChanged(nameof(SelectedBytesTransferredLabel));
        OnPropertyChanged(nameof(SelectedAverageRateLabel));
        OnPropertyChanged(nameof(SelectedErrorCountLabel));
        OnPropertyChanged(nameof(BpfFilterLabel));
    }

    partial void OnLastPreviewLabelChanged(string value)
    {
        OnPropertyChanged(nameof(HeaderRefreshLabel));
    }

    partial void OnCurrentSessionChanged(CaptureSessionStateDto value)
    {
        var bpf = value.Bpf ?? string.Empty;
        if (!string.Equals(CaptureBpfInput, bpf, StringComparison.Ordinal))
        {
            CaptureBpfInput = bpf;
        }

        OnPropertyChanged(nameof(FormalCaptureSummary));
        OnPropertyChanged(nameof(CaptureStateLabel));
        OnPropertyChanged(nameof(CanStartFormalCapture));
        OnPropertyChanged(nameof(CanStopFormalCapture));
        OnPropertyChanged(nameof(ShowPauseCaptureAction));
        OnPropertyChanged(nameof(ShowResumeCaptureAction));
        OnPropertyChanged(nameof(CanPauseFormalCapture));
        OnPropertyChanged(nameof(CanResumeFormalCapture));
        OnPropertyChanged(nameof(BpfFilterLabel));
        OnPropertyChanged(nameof(CaptureBpfStatusLabel));
        OnPropertyChanged(nameof(CaptureBpfHint));
        SessionStateChanged?.Invoke(value, StatusMessage);
    }

    partial void OnIsControlBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartFormalCapture));
        OnPropertyChanged(nameof(CanStopFormalCapture));
        OnPropertyChanged(nameof(CanPauseFormalCapture));
        OnPropertyChanged(nameof(CanResumeFormalCapture));
    }

    partial void OnStatusMessageChanged(string? value)
    {
        SessionStateChanged?.Invoke(CurrentSession, value);
    }

    public void SetProjectionCaptureStatus(string captureStatus, string statusMessage)
    {
        CurrentSession = new CaptureSessionStateDto
        {
            SourceKind = CurrentSession.SourceKind,
            SourceDisplayName = CurrentSession.SourceDisplayName,
            CaptureStatus = captureStatus,
            Mode = CurrentSession.Mode,
            Bpf = CurrentSession.Bpf,
        };
        StatusMessage = statusMessage;
        SetPreviewState(
            string.Equals(captureStatus, "error", StringComparison.OrdinalIgnoreCase)
                ? "Offline replay failed"
                : "Offline replay completed",
            statusMessage,
            isWarning: false,
            isError: string.Equals(captureStatus, "error", StringComparison.OrdinalIgnoreCase)
        );
    }

    [RelayCommand]
    private void SelectDevice(SourceDeviceItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedDevice = item;
    }

    [RelayCommand]
    private void RefreshPreview()
    {
        if (_discoveryClient is null || _isDesignTime)
        {
            LastPreviewLabel = $"Preview refreshed at {DateTime.Now:HH:mm:ss}";
            SetPreviewState(
                "Preview ready",
                "Design-time source preview refreshed. Formal capture still requires explicit source selection.",
                isWarning: false,
                isError: false
            );
            return;
        }

        _ = LoadAsync(refreshPreview: true);
    }

    [RelayCommand]
    private async Task ImportOffline()
    {
        var offlinePath = _isDesignTime
            ? "/tmp/sample.pcap"
            : OfflineFileRequested is null
                ? null
                : await OfflineFileRequested.Invoke();

        if (string.IsNullOrWhiteSpace(offlinePath))
        {
            StatusMessage = "Offline import cancelled.";
            SetPreviewState(
                "Offline import cancelled",
                "No pcap file was selected.",
                isWarning: true,
                isError: false
            );
            return;
        }

        if (!_isDesignTime && !File.Exists(offlinePath))
        {
            StatusMessage = $"Offline file not found: {offlinePath}";
            SetPreviewState(
                "Offline file unavailable",
                "The selected pcap file could not be found on disk.",
                isWarning: false,
                isError: true
            );
            return;
        }

        SelectedSourceMode = "Offline file";
        var bpf = CurrentSession.Bpf ?? string.Empty;

        if (_controlClient is null || _isDesignTime)
        {
            SetPreviewState(
                "Offline replay armed",
                "Offline replay is armed for the selected pcap file.",
                isWarning: false,
                isError: false
            );
            CurrentSession = new CaptureSessionStateDto
            {
                SourceKind = "offline",
                SourceDisplayName = offlinePath,
                CaptureStatus = "armed",
                Mode = "offline",
                Bpf = string.IsNullOrWhiteSpace(bpf) ? null : bpf,
            };
            StatusMessage = $"Offline file armed: {offlinePath}";
            return;
        }

        IsControlBusy = true;
        try
        {
            CurrentSession = new CaptureSessionStateDto
            {
                SourceKind = "offline",
                SourceDisplayName = offlinePath,
                CaptureStatus = "starting",
                Mode = "offline",
                Bpf = string.IsNullOrWhiteSpace(bpf) ? null : bpf,
            };
            StatusMessage = "Starting offline replay...";
            SetPreviewState(
                "Starting offline replay",
                "Submitting the pcap file, filter and replay command to the resident core.",
                isWarning: false,
                isError: false
            );

            var sourceResult = await _controlClient.SetOfflineSourceAsync(offlinePath);
            if (!sourceResult.Accepted)
            {
                CurrentSession = new CaptureSessionStateDto
                {
                    SourceKind = "offline",
                    SourceDisplayName = offlinePath,
                    CaptureStatus = "idle",
                    Mode = "offline",
                    Bpf = string.IsNullOrWhiteSpace(bpf) ? null : bpf,
                };
                StatusMessage = sourceResult.Message;
                SetPreviewState(
                    "Offline source rejected",
                    sourceResult.Message,
                    isWarning: false,
                    isError: true
                );
                return;
            }

            var filterResult = await _controlClient.ApplyFilterAsync(bpf);
            if (!filterResult.Accepted)
            {
                CurrentSession = new CaptureSessionStateDto
                {
                    SourceKind = "offline",
                    SourceDisplayName = offlinePath,
                    CaptureStatus = "idle",
                    Mode = "offline",
                    Bpf = string.IsNullOrWhiteSpace(bpf) ? null : bpf,
                };
                StatusMessage = filterResult.Message;
                SetPreviewState(
                    "Filter rejected",
                    filterResult.Message,
                    isWarning: false,
                    isError: true
                );
                return;
            }

            var startResult = await _controlClient.StartCaptureAsync();
            if (!startResult.Accepted)
            {
                CurrentSession = new CaptureSessionStateDto
                {
                    SourceKind = "offline",
                    SourceDisplayName = offlinePath,
                    CaptureStatus = "idle",
                    Mode = "offline",
                    Bpf = string.IsNullOrWhiteSpace(bpf) ? null : bpf,
                };
                StatusMessage = startResult.Message;
                SetPreviewState(
                    "Offline replay rejected",
                    startResult.Message,
                    isWarning: false,
                    isError: true
                );
                return;
            }

            SetPreviewState(
                "Offline replay running",
                startResult.Message,
                isWarning: false,
                isError: false
            );
            CurrentSession = new CaptureSessionStateDto
            {
                SourceKind = "offline",
                SourceDisplayName = offlinePath,
                CaptureStatus = "running",
                Mode = "offline",
                Bpf = string.IsNullOrWhiteSpace(bpf) ? null : bpf,
            };
            StatusMessage = startResult.Message;
        }
        finally
        {
            IsControlBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartFormalCapture()
    {
        if (SelectedDevice is null)
        {
            SetPreviewState(
                "Capture requires source",
                "Formal capture requires selecting exactly one source.",
                isWarning: true,
                isError: false
            );
            return;
        }

        if (_controlClient is null || _isDesignTime)
        {
            SetPreviewState(
                "Capture armed",
                $"Formal capture is armed for {SelectedDevice.DisplayName}. Actual start remains a separate control-plane action.",
                isWarning: false,
                isError: false
            );
            CurrentSession = new CaptureSessionStateDto
            {
                SourceKind = CurrentSession.SourceKind,
                SourceDisplayName = CurrentSession.SourceDisplayName,
                CaptureStatus = "armed",
                Mode = CurrentSession.Mode,
                Bpf = CurrentSession.Bpf,
            };
            return;
        }

        IsControlBusy = true;
        try
        {
            CurrentSession = new CaptureSessionStateDto
            {
                SourceKind = "live",
                SourceDisplayName = SelectedDevice.DisplayName,
                CaptureStatus = "starting",
                Mode = "live",
                Bpf = CurrentSession.Bpf,
            };
            StatusMessage = "Sending resident core start request...";
            SetPreviewState(
                "Starting capture",
                "Submitting source, filter and start commands to the resident core.",
                isWarning: false,
                isError: false
            );

            var sourceResult = await _controlClient.SetLiveSourceAsync(SelectedDevice.Device.Name);
            if (!sourceResult.Accepted)
            {
                CurrentSession = new CaptureSessionStateDto
                {
                    SourceKind = "live",
                    SourceDisplayName = SelectedDevice.DisplayName,
                    CaptureStatus = "idle",
                    Mode = "live",
                    Bpf = CurrentSession.Bpf,
                };
                StatusMessage = sourceResult.Message;
                SetPreviewState("Source rejected", sourceResult.Message, isWarning: false, isError: true);
                return;
            }

            var bpf = CurrentSession.Bpf ?? string.Empty;
            var filterResult = await _controlClient.ApplyFilterAsync(bpf);
            if (!filterResult.Accepted)
            {
                CurrentSession = new CaptureSessionStateDto
                {
                    SourceKind = "live",
                    SourceDisplayName = SelectedDevice.DisplayName,
                    CaptureStatus = "idle",
                    Mode = "live",
                    Bpf = CurrentSession.Bpf,
                };
                StatusMessage = filterResult.Message;
                SetPreviewState("Filter rejected", filterResult.Message, isWarning: false, isError: true);
                return;
            }

            var startResult = await _controlClient.StartCaptureAsync();
            if (!startResult.Accepted)
            {
                CurrentSession = new CaptureSessionStateDto
                {
                    SourceKind = "live",
                    SourceDisplayName = SelectedDevice.DisplayName,
                    CaptureStatus = "idle",
                    Mode = "live",
                    Bpf = string.IsNullOrWhiteSpace(bpf) ? null : bpf,
                };
                StatusMessage = startResult.Message;
                SetPreviewState("Capture rejected", startResult.Message, isWarning: false, isError: true);
                return;
            }

            SetPreviewState(
                "Capture running",
                startResult.Message,
                isWarning: false,
                isError: false
            );
            CurrentSession = new CaptureSessionStateDto
            {
                SourceKind = "live",
                SourceDisplayName = SelectedDevice.DisplayName,
                CaptureStatus = "running",
                Mode = "live",
                Bpf = string.IsNullOrWhiteSpace(bpf) ? null : bpf,
            };
            StatusMessage = startResult.Message;
        }
        finally
        {
            IsControlBusy = false;
        }
    }

    [RelayCommand]
    private async Task StopFormalCapture()
    {
        if (_controlClient is null || _isDesignTime)
        {
            CurrentSession = new CaptureSessionStateDto
            {
                SourceKind = CurrentSession.SourceKind,
                SourceDisplayName = CurrentSession.SourceDisplayName,
                CaptureStatus = "idle",
                Mode = CurrentSession.Mode,
                Bpf = CurrentSession.Bpf,
            };
            return;
        }

        IsControlBusy = true;
        try
        {
            CurrentSession = new CaptureSessionStateDto
            {
                SourceKind = CurrentSession.SourceKind,
                SourceDisplayName = CurrentSession.SourceDisplayName,
                CaptureStatus = "stopping",
                Mode = CurrentSession.Mode,
                Bpf = CurrentSession.Bpf,
            };
            StatusMessage = "Sending resident core stop request...";
            var stopResult = await _controlClient.StopCaptureAsync();
            SetPreviewState(
                stopResult.Accepted ? "Capture stopped" : "Capture stop declined",
                stopResult.Message,
                isWarning: !stopResult.Accepted,
                isError: false
            );
            CurrentSession = CloneSessionWithStatus(stopResult.Accepted ? "idle" : "running");
            StatusMessage = stopResult.Message;
        }
        finally
        {
            IsControlBusy = false;
        }
    }

    [RelayCommand]
    private async Task PauseFormalCapture()
    {
        if (_controlClient is null || _isDesignTime)
        {
            CurrentSession = CloneSessionWithStatus("paused");
            return;
        }

        IsControlBusy = true;
        try
        {
            StatusMessage = "Sending resident core pause request...";
            var result = await _controlClient.PauseCaptureAsync();
            SetPreviewState(
                result.Accepted ? "Capture paused" : "Capture pause declined",
                result.Message,
                isWarning: !result.Accepted,
                isError: false
            );
            if (result.Accepted)
            {
                CurrentSession = CloneSessionWithStatus("paused");
            }

            StatusMessage = result.Message;
        }
        finally
        {
            IsControlBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResumeFormalCapture()
    {
        if (_controlClient is null || _isDesignTime)
        {
            CurrentSession = CloneSessionWithStatus("running");
            return;
        }

        IsControlBusy = true;
        try
        {
            StatusMessage = "Sending resident core resume request...";
            var result = await _controlClient.ResumeCaptureAsync();
            SetPreviewState(
                result.Accepted ? "Capture resumed" : "Capture resume declined",
                result.Message,
                isWarning: !result.Accepted,
                isError: false
            );
            if (result.Accepted)
            {
                CurrentSession = CloneSessionWithStatus("running");
            }

            StatusMessage = result.Message;
        }
        finally
        {
            IsControlBusy = false;
        }
    }

    private CaptureSessionStateDto CloneSessionWithStatus(string captureStatus)
    {
        return new CaptureSessionStateDto
        {
            SourceKind = CurrentSession.SourceKind,
            SourceDisplayName = CurrentSession.SourceDisplayName,
            CaptureStatus = captureStatus,
            Mode = CurrentSession.Mode,
            Bpf = CurrentSession.Bpf,
        };
    }

    [RelayCommand]
    private void ArmFormalCapture()
    {
        SetPreviewState(
            "Capture armed",
            SelectedDevice is null
                ? "Formal capture requires selecting one source."
                : $"Formal capture is armed for {SelectedDevice.DisplayName}. Actual start remains a separate control-plane action.",
            isWarning: SelectedDevice is null,
            isError: false
        );
        CurrentSession = new CaptureSessionStateDto
        {
            SourceKind = CurrentSession.SourceKind,
            SourceDisplayName = CurrentSession.SourceDisplayName,
            CaptureStatus = "armed",
            Mode = CurrentSession.Mode,
            Bpf = CurrentSession.Bpf,
        };
    }

    private async Task RefreshDevicePreviewsAsync(bool preferActiveSelection = false)
    {
        if (_discoveryClient is null || _isDesignTime || DeviceItems.Count == 0)
        {
            return;
        }

        var selectedName = SelectedDevice?.Device.Name;
        var devices = DeviceItems.Select(item => item.Device).ToArray();
        try
        {
            SetPreviewState(
                "Preview loading",
                "Loading device preview samples from resident core.",
                isWarning: false,
                isError: false
            );
            var previews = await _discoveryClient.GetDevicePreviewsAsync(PreviewWindowSeconds);
            var previewByName = previews.ToDictionary(preview => preview.Name, StringComparer.OrdinalIgnoreCase);
            ApplyDeviceInventory(devices, selectedName, previewByName, preferActiveSelection);
            LastPreviewLabel = $"Preview refreshed at {DateTime.Now:HH:mm:ss} from {DeviceItems.Count} device(s)";
            if (!SourceDeviceSelection.IsCaptureActiveStatus(CurrentSession.CaptureStatus))
            {
                StatusMessage = preferActiveSelection && SelectedDevice is not null
                    ? $"Active interface selected: {SelectedDevice.DisplayName}."
                    : "Review one source, then continue into formal capture.";
                UpdatePreviewStateFromSelectedDevice();
            }
        }
        catch (Exception)
        {
            LastPreviewLabel = $"Preview window: {PreviewWindowSeconds}s sample";
            if (!SourceDeviceSelection.IsCaptureActiveStatus(CurrentSession.CaptureStatus))
            {
                StatusMessage = "Preview unavailable";
                SetPreviewState(
                    "Preview unavailable",
                    "Preview sampling failed, but the capture interface inventory remains available.",
                    isWarning: true,
                    isError: false
                );
            }
        }
    }

    private void ApplyDeviceInventory(
        IReadOnlyList<DeviceSummaryDto> devices,
        string? selectedName,
        IReadOnlyDictionary<string, DevicePreviewDto>? previewByName,
        bool preferActiveSelection
    )
    {
        var items = SourceDeviceSelection.OrderForDisplay(
            devices.Select(device => new SourceDeviceItemViewModel
            {
                Device = device,
                Preview = previewByName is not null && previewByName.TryGetValue(device.Name, out var preview)
                    ? preview
                    : new DevicePreviewDto
                    {
                        Name = device.Name,
                        PacketsSeen = 0,
                        BytesSeen = 0,
                        Unsupported = false,
                        Error = null,
                    },
            })
        );

        DeviceItems.Clear();
        foreach (var item in items)
        {
            DeviceItems.Add(item);
        }

        NotifyDeviceInventoryChanged();
        var preservedSelection = preferActiveSelection
            ? null
            : DeviceItems.FirstOrDefault(item =>
                string.Equals(item.Device.Name, selectedName, StringComparison.OrdinalIgnoreCase)
            );
        SelectedDevice = preservedSelection ?? SourceDeviceSelection.SelectActiveDevice(DeviceItems);
    }

    private void NotifyDeviceInventoryChanged()
    {
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(HasNoDevices));
    }

    private void UpdateCurrentSessionFromSelectedDevice()
    {
        CurrentSession = new CaptureSessionStateDto
        {
            SourceKind = SelectedDevice is null ? "none" : "live",
            SourceDisplayName = SelectedDevice?.DisplayName ?? "none",
            CaptureStatus = "idle",
            Mode = "live",
            Bpf = CurrentSession?.Bpf,
        };
    }

    private void LoadSeedDevices()
    {
        DeviceItems.Clear();
        foreach (var item in SourceDeviceSelection.OrderForDisplay(SourceSeedData.CreateSeedDevices()))
        {
            DeviceItems.Add(item);
        }

        SelectedDevice = SourceDeviceSelection.SelectActiveDevice(DeviceItems) ?? DeviceItems.FirstOrDefault();
        SelectedSourceMode = "Live source";
        LastPreviewLabel = $"Preview window: {PreviewWindowSeconds}s sample";
        NotifyDeviceInventoryChanged();
        UpdateCurrentSessionFromSelectedDevice();
        UpdatePreviewStateFromSelectedDevice();
    }

    private void UpdatePreviewStateFromSelectedDevice()
    {
        if (SelectedDevice is null)
        {
            SetPreviewState(
                "Select a source",
                "Choose one device to review preview sampling and formal capture readiness.",
                isWarning: false,
                isError: false
            );
            return;
        }

        if (SelectedDevice.Preview.Unsupported)
        {
            SetPreviewState(
                "Preview unsupported",
                "This interface is still selectable, but preview sampling is not supported on it.",
                isWarning: true,
                isError: false
            );
            return;
        }

        if (!string.IsNullOrWhiteSpace(SelectedDevice.Preview.Error))
        {
            SetPreviewState(
                "Preview unavailable",
                "Preview could not be sampled for the selected device. Formal capture still requires explicit selection and may need permission review.",
                isWarning: false,
                isError: true
            );
            return;
        }

        SetPreviewState(
            "Preview ready",
            "Multi-device preview is healthy. Continue with explicit source selection for formal capture.",
            isWarning: false,
            isError: false
        );
    }

    private void SetPreviewState(string label, string detail, bool isWarning, bool isError)
    {
        PreviewStatusLabel = label;
        PreviewStatusDetail = detail;
        PreviewStateIsWarning = isWarning;
        PreviewStateIsError = isError;
    }

}
