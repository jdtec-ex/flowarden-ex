using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flowarden.Ui.Models;

namespace Flowarden.Ui.ViewModels;

public sealed partial class SourcePageViewModel : ViewModelBase
{
    public SourcePageViewModel()
    {
        DeviceItems = new ObservableCollection<SourceDeviceItemViewModel>(
            CreateSeedDevices()
        );

        SelectedDevice = DeviceItems.FirstOrDefault();
        CurrentSession = new CaptureSessionStateDto
        {
            SourceKind = "live",
            SourceDisplayName = SelectedDevice?.DisplayName ?? "none",
            CaptureStatus = "idle",
            Mode = "live",
            Bpf = null,
        };
    }

    public ObservableCollection<SourceDeviceItemViewModel> DeviceItems { get; }

    [ObservableProperty]
    private SourceDeviceItemViewModel? selectedDevice;

    [ObservableProperty]
    private CaptureSessionStateDto currentSession;

    [ObservableProperty]
    private string selectedSourceMode = "Live source";

    [ObservableProperty]
    private string lastPreviewLabel = "Preview window: 2s sample";

    public bool HasSelectedDevice => SelectedDevice is not null;

    public string FormalCaptureSummary =>
        SelectedDevice is null
            ? "Formal capture requires selecting exactly one source."
            : $"Formal capture target: {SelectedDevice.DisplayName}";

    public string OfflineImportSummary => "Offline import remains a single file source, separate from live device preview.";

    partial void OnSelectedDeviceChanged(SourceDeviceItemViewModel? value)
    {
        CurrentSession = new CaptureSessionStateDto
        {
            SourceKind = value is null ? "none" : "live",
            SourceDisplayName = value?.DisplayName ?? "none",
            CaptureStatus = "idle",
            Mode = "live",
            Bpf = null,
        };
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
        LastPreviewLabel = $"Preview refreshed at {DateTime.Now:HH:mm:ss}";
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
