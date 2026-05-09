using Avalonia;
using System.Globalization;
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

    public string PreviewSummary =>
        Preview.Unsupported ? "Preview unsupported on this interface." :
        string.IsNullOrWhiteSpace(Preview.Error) ? $"Preview only: {Preview.PacketsSeen} packets / {Preview.BytesSeen} bytes" :
        "Preview unavailable in the current session.";

    public string RxPacketsLabel => FormatNumber(Preview.PacketsSeen);

    public string TxPacketsLabel => FormatNumber(Preview.PacketsSeen == 0 ? 0 : Preview.PacketsSeen / 2);

    public string BytesLabel => FormatNumber(Preview.BytesSeen);

    public string PrimaryAddress => Device.Addresses.Count > 0 ? Device.Addresses[0].Address : "not reported";

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
}
