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
    // TFC token hex for brush bindings (Theme.axaml).
    private const string TfcText = "#E6E0E9";
    private const string TfcMuted = "#CBC4D2";
    private const string TfcDim = "#948E9C";
    private const string TfcPrimary = "#CFBCFF";
    private const string TfcRaised = "#1D1B20";
    private const string TfcRaisedHigh = "#2B292F";
    private const string TfcBorder = "#494551";
    private const string TfcInbound = "#00CFEA";
    private const string TfcOutbound = "#A970FF";
    private const string TfcDataAccent = "#D9A84E";
    private const string TfcGood = "#10B981";
    private const string TfcWarning = "#F59E0B";
    private const string TfcError = "#EF4444";
    private const string TfcPanelLow = "#141218";
    private const string TfcShell = "#0F0D13";

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
        Preview.Unsupported ? TfcRaised :
        string.IsNullOrWhiteSpace(Preview.Error) ? TfcShell :
        TfcRaised;

    public string StatusForeground =>
        Preview.Unsupported ? TfcDim :
        string.IsNullOrWhiteSpace(Preview.Error) ? IsSelected ? TfcText : TfcDataAccent :
        TfcError;

    public string StatusDotBrush =>
        Preview.Unsupported ? TfcDim :
        string.IsNullOrWhiteSpace(Preview.Error) ? IsSelected ? TfcPrimary : TfcGood :
        TfcWarning;

    public string ShortStatusLabel =>
        Preview.Unsupported ? "IDLE" :
        string.IsNullOrWhiteSpace(Preview.Error) ? IsSelected ? "PRIMARY / READY" : "READY" :
        "READY";

    public string CardBackground => IsSelected ? TfcRaisedHigh : TfcRaised;

    public string CardBorderBrush => IsSelected ? TfcPrimary : TfcBorder;

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

        // Interface accents stay on the TFC semantic palette (no ad-hoc non-theme colors).
        if (StartsWithAny(normalized, "wlan", "wifi", "wi", "wl", "llw", "awdl"))
        {
            return new InterfaceVisual("Wi-Fi", TfcInbound, TfcShell, "W");
        }

        if (StartsWithAny(normalized, "ap"))
        {
            return new InterfaceVisual("Access Point", TfcDataAccent, TfcShell, "A");
        }

        if (StartsWithAny(normalized, "lo", "loopback"))
        {
            return new InterfaceVisual("Loopback", TfcPrimary, TfcShell, "L");
        }

        if (StartsWithAny(normalized, "utun", "tun", "tap", "ppp", "ipsec"))
        {
            return new InterfaceVisual("Tunnel", TfcOutbound, TfcShell, "T");
        }

        if (StartsWithAny(normalized, "bridge", "br"))
        {
            return new InterfaceVisual("Bridge", TfcError, TfcShell, "B");
        }

        if (StartsWithAny(normalized, "veth", "docker", "vmnet", "vbox", "virtual"))
        {
            return new InterfaceVisual("Virtual", TfcInbound, TfcPanelLow, "V");
        }

        if (StartsWithAny(normalized, "en", "eth"))
        {
            return new InterfaceVisual("Ethernet", TfcGood, TfcShell, "E");
        }

        return new InterfaceVisual("Interface", TfcMuted, TfcShell, "N");
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
