using Avalonia;
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
        Preview.Unsupported ? "#F8CA6A" :
        string.IsNullOrWhiteSpace(Preview.Error) ? "#76E3B1" :
        "#FFB4AB";

    public string CardBackground => IsSelected ? "#303446" : "#272935";

    public string CardBorderBrush => IsSelected ? "#75D4E8" : "#2F3242";

    public Thickness CardBorderThickness => IsSelected ? new Thickness(2) : new Thickness(1);

    public string SelectionLabel => IsSelected ? "Selected source" : "Available source";

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CardBackground));
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(CardBorderThickness));
        OnPropertyChanged(nameof(SelectionLabel));
    }
}
