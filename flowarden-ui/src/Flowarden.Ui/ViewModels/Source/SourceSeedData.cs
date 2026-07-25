using System.Collections.Generic;
using Flowarden.Ui.Models;
using Flowarden.Ui.ViewModels;

namespace Flowarden.Ui.ViewModels.Source;

internal static class SourceSeedData
{
    public static IReadOnlyList<SourceDeviceItemViewModel> CreateSeedDevices()
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
