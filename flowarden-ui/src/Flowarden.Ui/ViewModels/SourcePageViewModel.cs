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

public sealed partial class SourcePageViewModel : ViewModelBase
{
    private readonly DiscoveryClient? _discoveryClient;
    private readonly ControlClient? _controlClient;
    private readonly bool _isDesignTime;
    private const ulong PreviewWindowSeconds = 2;

    public event Action<CaptureSessionStateDto?, string?>? SessionStateChanged;

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

    public bool CanStartFormalCapture =>
        HasSelectedDevice
        && !IsControlBusy
        && !string.Equals(CurrentSession.CaptureStatus, "running", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(CurrentSession.CaptureStatus, "starting", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(CurrentSession.CaptureStatus, "stopping", StringComparison.OrdinalIgnoreCase);

    public bool CanStopFormalCapture =>
        string.Equals(CurrentSession.CaptureStatus, "running", StringComparison.OrdinalIgnoreCase)
        && !IsControlBusy;

    public string FormalCaptureSummary =>
        SelectedDevice is null
            ? "Formal capture requires selecting exactly one source."
            : CurrentSession.CaptureStatus.ToLowerInvariant() switch
            {
                "starting" => $"Starting resident capture for {SelectedDevice.DisplayName}.",
                "running" => $"Resident capture is running on {CurrentSession.SourceDisplayName}.",
                "stopping" => $"Resident capture is stopping on {CurrentSession.SourceDisplayName}.",
                "armed" => $"Formal capture is armed for {SelectedDevice.DisplayName}.",
                _ => $"Formal capture target: {SelectedDevice.DisplayName}",
            };

    public string CaptureStateLabel =>
        CurrentSession.CaptureStatus.ToLowerInvariant() switch
        {
            "starting" => "Starting",
            "running" => "Running",
            "stopping" => "Stopping",
            "armed" => "Armed",
            _ => "Idle",
        };

    public string OfflineImportSummary => "Offline import remains a single file source, separate from live device preview.";

    public async Task LoadAsync()
    {
        if (_discoveryClient is null || _isDesignTime)
        {
            return;
        }

        SetPreviewState("Preview loading", "Loading devices and preview samples from resident core.", isWarning: false, isError: false);
        StatusMessage = "Loading source inventory...";
        try
        {
            var devices = await _discoveryClient.GetDevicesAsync();
            var previews = await _discoveryClient.GetDevicePreviewsAsync(PreviewWindowSeconds);
            var previewByName = previews.ToDictionary(preview => preview.Name, StringComparer.OrdinalIgnoreCase);

            var items = devices
                .Select(device => new SourceDeviceItemViewModel
                {
                    Device = device,
                    Preview = previewByName.TryGetValue(device.Name, out var preview)
                        ? preview
                        : new DevicePreviewDto
                        {
                            Name = device.Name,
                            PacketsSeen = 0,
                            BytesSeen = 0,
                            Unsupported = true,
                            Error = "Preview unavailable",
                        },
                })
                .ToArray();

            DeviceItems.Clear();
            foreach (var item in items)
            {
                DeviceItems.Add(item);
            }

            SelectedDevice = DeviceItems.FirstOrDefault();
            SelectedSourceMode = "Live source";
            LastPreviewLabel = $"Preview refreshed at {DateTime.Now:HH:mm:ss} from {DeviceItems.Count} device(s)";
            StatusMessage = "Review one source, then continue into formal capture.";
            UpdateCurrentSessionFromSelectedDevice();
        }
        catch (Exception)
        {
            StatusMessage = "Preview unavailable";
            SetPreviewState(
                "Preview unavailable",
                "Preview sampling is not currently wired or failed to return from the resident core. You can still review devices and continue with explicit source selection.",
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

        UpdateCurrentSessionFromSelectedDevice();
        UpdatePreviewStateFromSelectedDevice();
        OnPropertyChanged(nameof(HasSelectedDevice));
        OnPropertyChanged(nameof(FormalCaptureSummary));
        OnPropertyChanged(nameof(CanStartFormalCapture));
    }

    partial void OnCurrentSessionChanged(CaptureSessionStateDto value)
    {
        OnPropertyChanged(nameof(FormalCaptureSummary));
        OnPropertyChanged(nameof(CaptureStateLabel));
        OnPropertyChanged(nameof(CanStartFormalCapture));
        OnPropertyChanged(nameof(CanStopFormalCapture));
        SessionStateChanged?.Invoke(value, StatusMessage);
    }

    partial void OnIsControlBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartFormalCapture));
        OnPropertyChanged(nameof(CanStopFormalCapture));
    }

    partial void OnStatusMessageChanged(string? value)
    {
        SessionStateChanged?.Invoke(CurrentSession, value);
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

        _ = LoadAsync();
    }

    [RelayCommand]
    private void ImportOffline()
    {
        SelectedSourceMode = "Offline file";
        SetPreviewState(
            "Offline import selected",
            "Preview sampling applies only to live devices. Offline capture remains a single file source.",
            isWarning: false,
            isError: false
        );
        CurrentSession = new CaptureSessionStateDto
        {
            SourceKind = "offline",
            SourceDisplayName = "offline sample",
            CaptureStatus = "idle",
            Mode = "offline",
            Bpf = null,
        };
        OnPropertyChanged(nameof(FormalCaptureSummary));
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

            var sourceResult = await _controlClient.SetSourceAsync(SelectedDevice.Device.Name);
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
        catch (Exception)
        {
            CurrentSession = new CaptureSessionStateDto
            {
                SourceKind = "live",
                SourceDisplayName = SelectedDevice.DisplayName,
                CaptureStatus = "idle",
                Mode = "live",
                Bpf = CurrentSession.Bpf,
            };
            StatusMessage = "Resident core rejected the control-plane action or became unavailable.";
            SetPreviewState(
                "Capture failed",
                "Resident core rejected the control-plane action or became unavailable.",
                isWarning: false,
                isError: true
            );
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
                "Capture stop requested",
                stopResult.Message,
                isWarning: false,
                isError: false
            );
            CurrentSession = new CaptureSessionStateDto
            {
                SourceKind = CurrentSession.SourceKind,
                SourceDisplayName = CurrentSession.SourceDisplayName,
                CaptureStatus = "idle",
                Mode = CurrentSession.Mode,
                Bpf = CurrentSession.Bpf,
            };
            StatusMessage = stopResult.Message;
        }
        catch (Exception)
        {
            CurrentSession = new CaptureSessionStateDto
            {
                SourceKind = CurrentSession.SourceKind,
                SourceDisplayName = CurrentSession.SourceDisplayName,
                CaptureStatus = "running",
                Mode = CurrentSession.Mode,
                Bpf = CurrentSession.Bpf,
            };
            StatusMessage = "Resident core did not accept the stop request.";
            SetPreviewState(
                "Stop failed",
                "Resident core did not accept the stop request.",
                isWarning: false,
                isError: true
            );
        }
        finally
        {
            IsControlBusy = false;
        }
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
        foreach (var item in CreateSeedDevices())
        {
            DeviceItems.Add(item);
        }

        SelectedDevice = DeviceItems.FirstOrDefault();
        SelectedSourceMode = "Live source";
        LastPreviewLabel = $"Preview window: {PreviewWindowSeconds}s sample";
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

    private static IReadOnlyList<SourceDeviceItemViewModel> CreateSeedDevices()
    {
        return
        [
            new SourceDeviceItemViewModel
            {
                Device = new DeviceSummaryDto
                {
                    Name = "en0",
                    Description = "Wi-Fi adapter",
                    Addresses =
                    [
                        new DeviceAddressDto { Address = "192.168.50.21" },
                    ],
                },
                Preview = new DevicePreviewDto
                {
                    Name = "en0",
                    PacketsSeen = 318,
                    BytesSeen = 142_880,
                    Unsupported = false,
                    Error = null,
                },
            },
            new SourceDeviceItemViewModel
            {
                Device = new DeviceSummaryDto
                {
                    Name = "lo0",
                    Description = "Loopback",
                    Addresses =
                    [
                        new DeviceAddressDto { Address = "127.0.0.1" },
                    ],
                },
                Preview = new DevicePreviewDto
                {
                    Name = "lo0",
                    PacketsSeen = 103,
                    BytesSeen = 18_468,
                    Unsupported = false,
                    Error = null,
                },
            },
            new SourceDeviceItemViewModel
            {
                Device = new DeviceSummaryDto
                {
                    Name = "utun5",
                    Description = "Tunnel interface",
                },
                Preview = new DevicePreviewDto
                {
                    Name = "utun5",
                    PacketsSeen = 0,
                    BytesSeen = 0,
                    Unsupported = true,
                    Error = "Link type: unsupported",
                },
            },
        ];
    }
}
