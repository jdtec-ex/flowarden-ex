using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Flowarden.Ui.ViewModels;

namespace Flowarden.Ui.ViewModels.Source;

internal static class SourceDeviceSelection
{
    public static SourceDeviceItemViewModel? SelectActiveDevice(
            IReadOnlyList<SourceDeviceItemViewModel> items
        )
        {
            if (items.Count == 0)
            {
                return null;
            }
    
            var nonLoopbackCandidates = items
                .Where(item => !IsLoopbackInterfaceName(item.DisplayName))
                .ToArray();
            var candidates = nonLoopbackCandidates.Length > 0 ? nonLoopbackCandidates : items;
    
            return candidates
                .OrderByDescending(HasPreviewTraffic)
                .ThenByDescending(item => item.Preview.BytesSeen)
                .ThenByDescending(item => item.Preview.PacketsSeen)
                .ThenByDescending(HasNonLoopbackIpv4)
                .ThenByDescending(HasUsableNonLoopbackAddress)
                .ThenByDescending(item => IsCommonPrimaryInterfaceName(item.DisplayName))
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

    public static bool HasPreviewTraffic(SourceDeviceItemViewModel item)
        {
            return item.Preview.PacketsSeen > 0 || item.Preview.BytesSeen > 0;
        }

    public static bool HasNonLoopbackIpv4(SourceDeviceItemViewModel item)
        {
            return item.Device.Addresses.Any(address => IsNonLoopbackIpv4(address.Address));
        }

    public static bool HasUsableNonLoopbackAddress(SourceDeviceItemViewModel item)
        {
            return item.Device.Addresses.Any(address => IsUsableNonLoopbackAddress(address.Address));
        }

    public static bool IsNonLoopbackIpv4(string value)
        {
            return IPAddress.TryParse(value, out var address)
                && address.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(address)
                && !IsLinkLocalIpv4(address);
        }

    public static bool IsUsableNonLoopbackAddress(string value)
        {
            if (!IPAddress.TryParse(value, out var address) || IPAddress.IsLoopback(address))
            {
                return false;
            }
    
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return !IsLinkLocalIpv4(address);
            }
    
            return address.AddressFamily == AddressFamily.InterNetworkV6 && !address.IsIPv6LinkLocal;
        }

    public static bool IsLinkLocalIpv4(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            return bytes.Length >= 2 && bytes[0] == 169 && bytes[1] == 254;
        }

    public static bool IsLoopbackInterfaceName(string name)
        {
            return string.Equals(name, "lo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "lo0", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("loopback", StringComparison.OrdinalIgnoreCase);
        }

    public static bool IsCommonPrimaryInterfaceName(string name)
        {
            return string.Equals(name, "en0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "eth0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "wlan0", StringComparison.OrdinalIgnoreCase);
        }

    public static bool IsCaptureActiveStatus(string? status)
        {
            return string.Equals(status, "starting", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "running", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "stopping", StringComparison.OrdinalIgnoreCase);
        }
}
