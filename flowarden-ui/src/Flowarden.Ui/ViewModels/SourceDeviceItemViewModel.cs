using System;
using Avalonia;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using Flowarden.Ui.Models;

namespace Flowarden.Ui.ViewModels;

public sealed partial class SourceDeviceItemViewModel : ObservableObject
{
    public required DeviceSummaryDto Device { get; init; }

    public required DevicePreviewDto Preview { get; init; }

    [ObservableProperty]
    private bool isSelected;

    public string DisplayName => Device.Name;

    public string Description => string.IsNullOrWhiteSpace(Device.Description) ? "No description" : Device.Description;

    public string InterfaceIconText => BuildInterfaceIconText(Device.Name);

    public string InterfaceTypeLabel => ClassifyInterface(Device.Name).Label;

    public string InterfaceAccentBrush => ClassifyInterface(Device.Name).Accent;

    public string InterfaceIconBackground => ClassifyInterface(Device.Name).Background;

    public string PreviewSummary =>
        Preview.Unsupported ? "Preview unsupported on this interface." :
        string.IsNullOrWhiteSpace(Preview.Error) ? $"Preview only: {Preview.PacketsSeen} packets / {Preview.BytesSeen} bytes" :
        "Preview unavailable in the current session.";

    public string RxPacketsLabel => FormatNumber(Preview.PacketsSeen);

    public string TxPacketsLabel => FormatNumber(Preview.PacketsSeen == 0 ? 0 : Preview.PacketsSeen / 2);

    public string BytesLabel => FormatNumber(Preview.BytesSeen);

    public string PrimaryAddress => SelectIpv4Address();

    public string PrimaryIpv6Address => SelectIpv6Address();

    public string PreviewStatusLabel =>
        Preview.Unsupported ? "Preview unsupported" :
        string.IsNullOrWhiteSpace(Preview.Error) ? "Ready for formal capture" :
        "Preview unavailable";

    public string PreviewStatusDetail =>
        Preview.Unsupported ? "This interface does not currently support preview sampling." :
        string.IsNullOrWhiteSpace(Preview.Error) ? "Preview is healthy. You can select this device for formal capture." :
        "Preview could not be sampled. Review capture permissions or retry later.";

    public string ReadinessLabel =>
        Preview.Unsupported ? "Selection allowed, but preview is unavailable on this interface." :
        string.IsNullOrWhiteSpace(Preview.Error) ? "Ready for formal capture after explicit selection." :
        "Selection allowed, but preview is currently unavailable.";

    public string StatusBackground =>
        Preview.Unsupported ? "#4A3521" :
        string.IsNullOrWhiteSpace(Preview.Error) ? "#20372F" :
        "#4A2A31";

    public string StatusForeground =>
        Preview.Unsupported ? "#CBC4D2" :
        string.IsNullOrWhiteSpace(Preview.Error) ? IsSelected ? "#E6E0E9" : "#E7C365" :
        "#FFB4AB";

    public string StatusDotBrush =>
        Preview.Unsupported ? "#948E9C" :
        string.IsNullOrWhiteSpace(Preview.Error) ? IsSelected ? "#CFBCFF" : "#E7C365" :
        "#F59E0B";

    public string ShortStatusLabel =>
        Preview.Unsupported ? "IDLE" :
        string.IsNullOrWhiteSpace(Preview.Error) ? IsSelected ? "PRIMARY / READY" : "READY" :
        "READY";

    public string CardBackground => IsSelected ? "#2B292F" : "#1D1B20";

    public string CardBorderBrush => IsSelected ? "#CFBCFF" : "#494551";

    public Thickness CardBorderThickness => IsSelected ? new Thickness(3, 1, 1, 1) : new Thickness(0, 0, 0, 1);

    public string SelectionLabel => IsSelected ? "Selected source" : "Available source";

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CardBackground));
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(CardBorderThickness));
        OnPropertyChanged(nameof(SelectionLabel));
        OnPropertyChanged(nameof(StatusDotBrush));
        OnPropertyChanged(nameof(StatusForeground));
        OnPropertyChanged(nameof(ShortStatusLabel));
    }

    private static string FormatNumber(ulong value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static InterfaceVisual ClassifyInterface(string name)
    {
        var normalized = NormalizeName(name);

        if (StartsWithAny(normalized, "wlan", "wifi", "wi", "wl", "llw", "awdl"))
        {
            return new InterfaceVisual("Wi-Fi", "#7DD3FC", "#132A34", "W");
        }

        if (StartsWithAny(normalized, "ap"))
        {
            return new InterfaceVisual("Access Point", "#FDE68A", "#342B14", "A");
        }

        if (StartsWithAny(normalized, "lo", "loopback"))
        {
            return new InterfaceVisual("Loopback", "#C4B5FD", "#28213A", "L");
        }

        if (StartsWithAny(normalized, "utun", "tun", "tap", "ppp", "ipsec"))
        {
            return new InterfaceVisual("Tunnel", "#F9A8D4", "#36202E", "T");
        }

        if (StartsWithAny(normalized, "bridge", "br"))
        {
            return new InterfaceVisual("Bridge", "#FDA4AF", "#351F25", "B");
        }

        if (StartsWithAny(normalized, "veth", "docker", "vmnet", "vbox", "virtual"))
        {
            return new InterfaceVisual("Virtual", "#93C5FD", "#1C2638", "V");
        }

        if (StartsWithAny(normalized, "en", "eth"))
        {
            return new InterfaceVisual("Ethernet", "#A7F3D0", "#173126", "E");
        }

        return new InterfaceVisual("Interface", "#CBD5E1", "#242A32", "N");
    }

    private static string BuildInterfaceIconText(string name)
    {
        var visual = ClassifyInterface(name);
        var suffix = NormalizeName(name).LastOrDefault(char.IsDigit);
        if (suffix != default)
        {
            return $"{visual.Mark}{suffix}";
        }

        var letters = NormalizeName(name)
            .Where(char.IsLetterOrDigit)
            .Take(2)
            .ToArray();

        return letters.Length == 0
            ? visual.Mark
            : new string(letters).ToUpperInvariant();
    }

    private static string NormalizeName(string name)
    {
        return (name ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static bool StartsWithAny(string value, params string[] prefixes)
    {
        return prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private string SelectIpv4Address()
    {
        var addresses = Device.Addresses
            .Select(address => address.Address)
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .ToArray();

        return addresses.FirstOrDefault(IsNonLoopbackIpv4) ?? "not reported";
    }

    private string SelectIpv6Address()
    {
        var addresses = Device.Addresses
            .Select(address => address.Address)
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .ToArray();

        return addresses.FirstOrDefault(IsGlobalIpv6)
            ?? addresses.FirstOrDefault(IsUsableIpv6)
            ?? "not reported";
    }

    private static bool IsNonLoopbackIpv4(string value)
    {
        return IPAddress.TryParse(value, out var address)
            && address.AddressFamily == AddressFamily.InterNetwork
            && !IPAddress.IsLoopback(address)
            && !IsLinkLocalIpv4(address);
    }

    private static bool IsGlobalIpv6(string value)
    {
        return IPAddress.TryParse(value, out var address)
            && address.AddressFamily == AddressFamily.InterNetworkV6
            && !IPAddress.IsLoopback(address)
            && !address.IsIPv6LinkLocal
            && !address.IsIPv6SiteLocal
            && !address.IsIPv6Multicast;
    }

    private static bool IsUsableIpv6(string value)
    {
        return IPAddress.TryParse(value, out var address)
            && address.AddressFamily == AddressFamily.InterNetworkV6
            && !IPAddress.IsLoopback(address)
            && !address.IsIPv6Multicast;
    }

    private static bool IsLinkLocalIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length >= 2 && bytes[0] == 169 && bytes[1] == 254;
    }

    private readonly record struct InterfaceVisual(
        string Label,
        string Accent,
        string Background,
        string Mark
    );
}
