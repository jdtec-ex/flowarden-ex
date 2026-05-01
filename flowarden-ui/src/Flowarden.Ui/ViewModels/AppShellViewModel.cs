using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flowarden.Ui.ViewModels;

public sealed partial class AppShellViewModel : ViewModelBase
{
    private readonly IReadOnlyDictionary<string, AppShellPageViewModel> _pages;

    public AppShellViewModel()
    {
        NavigationItems = new ReadOnlyCollection<AppNavigationItemViewModel>(
            [
                new AppNavigationItemViewModel { Id = "source", Label = "Source" },
                new AppNavigationItemViewModel { Id = "overview", Label = "Overview" },
                new AppNavigationItemViewModel { Id = "inspect", Label = "Inspect" },
                new AppNavigationItemViewModel { Id = "settings", Label = "Settings" },
            ]
        );

        _pages = new Dictionary<string, AppShellPageViewModel>
        {
            ["source"] = new AppShellPageViewModel
            {
                Id = "source",
                Title = "Source",
                Description = "Choose one formal capture source or review preview data before starting a session.",
            },
            ["overview"] = new AppShellPageViewModel
            {
                Id = "overview",
                Title = "Overview",
                Description = "Traffic timeline, summary cards, destination workbench and ranked detail panels.",
            },
            ["inspect"] = new AppShellPageViewModel
            {
                Id = "inspect",
                Title = "Inspect",
                Description = "Filter-first results table for connections, services and ranked summaries.",
            },
            ["settings"] = new AppShellPageViewModel
            {
                Id = "settings",
                Title = "Settings",
                Description = "Runtime-oriented endpoint, source and diagnostics settings for phase 2.",
            },
        };

        ActiveMode = "Live";
        CoreStatus = new StatusIndicatorViewModel
        {
            Label = "Core",
            Value = "Connected",
            Tone = "good",
        };
        CaptureStatus = new StatusIndicatorViewModel
        {
            Label = "Capture",
            Value = "Idle",
            Tone = "neutral",
        };
        CurrentPageId = "overview";
        CurrentPage = _pages[CurrentPageId];
    }

    public string Title { get; } = "Flowarden";

    public string Subtitle { get; } = "Cosmos Network System";

    public IReadOnlyList<AppNavigationItemViewModel> NavigationItems { get; }

    [ObservableProperty]
    private string activeMode;

    [ObservableProperty]
    private StatusIndicatorViewModel coreStatus;

    [ObservableProperty]
    private StatusIndicatorViewModel captureStatus;

    [ObservableProperty]
    private string currentPageId;

    [ObservableProperty]
    private AppShellPageViewModel currentPage;

    public bool IsSourcePageActive => CurrentPageId == "source";

    public bool IsOverviewPageActive => CurrentPageId == "overview";

    public bool IsInspectPageActive => CurrentPageId == "inspect";

    public bool IsSettingsPageActive => CurrentPageId == "settings";

    [RelayCommand]
    private void Navigate(string? pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId) || !_pages.TryGetValue(pageId, out var page))
        {
            return;
        }

        CurrentPageId = pageId;
        CurrentPage = page;
        OnPropertyChanged(nameof(IsSourcePageActive));
        OnPropertyChanged(nameof(IsOverviewPageActive));
        OnPropertyChanged(nameof(IsInspectPageActive));
        OnPropertyChanged(nameof(IsSettingsPageActive));
    }

    [RelayCommand]
    private void ToggleMode()
    {
        ActiveMode = ActiveMode == "Live" ? "Replay" : "Live";
    }

    [RelayCommand]
    private void StartCapture()
    {
        CaptureStatus = new StatusIndicatorViewModel
        {
            Label = "Capture",
            Value = CaptureStatus.Value == "Running" ? "Idle" : "Running",
            Tone = CaptureStatus.Value == "Running" ? "neutral" : "good",
        };
    }

    [RelayCommand]
    private void OpenTools()
    {
        CoreStatus = new StatusIndicatorViewModel
        {
            Label = "Core",
            Value = CoreStatus.Value == "Connected" ? "Diagnostics" : "Connected",
            Tone = CoreStatus.Value == "Connected" ? "neutral" : "good",
        };
    }
}
