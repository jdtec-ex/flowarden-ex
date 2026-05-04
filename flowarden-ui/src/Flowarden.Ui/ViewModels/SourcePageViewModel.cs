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
    private readonly bool _isDesignTime;

    public SourcePageViewModel()
        : this(discoveryClient: null, isDesignTime: true)
    {
    }

    public SourcePageViewModel(DiscoveryClient? discoveryClient)
        : this(discoveryClient, isDesignTime: false)
    {
    }

    private SourcePageViewModel(DiscoveryClient? discoveryClient, bool isDesignTime)
    {
        _discoveryClient = discoveryClient;
        _isDesignTime = isDesignTime;
        DeviceItems = new ObservableCollection<SourceDeviceItemViewModel>();

        SelectedSourceMode = "Live source";
        LastPreviewLabel = "Preview window: 2s sample";
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

    public bool HasSelectedDevice => SelectedDevice is not null;

    public string FormalCaptureSummary =>
        SelectedDevice is null
            ? "Formal capture requires selecting exactly one source."
            : $"Formal capture target: {SelectedDevice.DisplayName}";

    public string OfflineImportSummary => "Offline import remains a single file source, separate from live device preview.";

    public async Task LoadAsync()
    {
        if (_discoveryClient is null || _isDesignTime)
        {
            return;
        }

        StatusMessage = "Loading devices from resident core...";
        try
        {
            var devices = await _discoveryClient.GetDevicesAsync();
            var previews = await _discoveryClient.GetDevicePreviewsAsync(2);
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
            LastPreviewLabel = $"Loaded {DeviceItems.Count} devices from core";
            StatusMessage = string.Empty;
            UpdateCurrentSessionFromSelectedDevice();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            LoadSeedDevices();
        }
    }

    partial void OnSelectedDeviceChanged(SourceDeviceItemViewModel? value)
    {
        UpdateCurrentSessionFromSelectedDevice();
        OnPropertyChanged(nameof(HasSelectedDevice));
        OnPropertyChanged(nameof(FormalCaptureSummary));
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
            return;
        }

        _ = LoadAsync();
    }

    [RelayCommand]
    private void ImportOffline()
    {
        SelectedSourceMode = "Offline file";
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
    private void StartFormalCapture()
    {
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
            Bpf = null,
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
        LastPreviewLabel = "Preview window: 2s sample";
        UpdateCurrentSessionFromSelectedDevice();
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
