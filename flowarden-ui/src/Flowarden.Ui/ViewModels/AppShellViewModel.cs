using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flowarden.Ui.Models;
using Flowarden.Ui.Services;

namespace Flowarden.Ui.ViewModels;

public sealed partial class AppShellViewModel : ViewModelBase
{
    private readonly IReadOnlyDictionary<string, AppShellPageViewModel> _pages;
    private readonly CoreConnectionCoordinator? _coreConnectionCoordinator;

    public AppShellViewModel()
        : this(null)
    {
    }

    public AppShellViewModel(CoreConnectionCoordinator? coreConnectionCoordinator)
    {
        _coreConnectionCoordinator = coreConnectionCoordinator;
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
            Value = "Checking",
            Tone = "warning",
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

    [ObservableProperty]
    private string connectionMessage = "Connecting to flowarden core...";

    [ObservableProperty]
    private CoreErrorDto? latestCoreError;

    public bool IsSourcePageActive => CurrentPageId == "source";

    public bool IsOverviewPageActive => CurrentPageId == "overview";

    public bool IsInspectPageActive => CurrentPageId == "inspect";

    public bool IsSettingsPageActive => CurrentPageId == "settings";

    public bool HasLatestCoreError => LatestCoreError is not null;

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
        Navigate("source");
    }

    [RelayCommand]
    private void OpenTools()
    {
        // Tools entry is reserved for later wiring.
    }

    public async Task InitializeCoreConnectionAsync(
        string workingDirectory,
        string binaryPath,
        string bindAddress
    )
    {
        if (_coreConnectionCoordinator is null)
        {
            CoreStatus = new StatusIndicatorViewModel
            {
                Label = "Core",
                Value = "Not wired",
                Tone = "warning",
            };
            ConnectionMessage = "Core connection services are not configured in this app session.";
            return;
        }

        ConnectionMessage = "Checking for resident flowarden core...";
        var result = await _coreConnectionCoordinator.EnsureConnectedAsync(
            workingDirectory,
            binaryPath,
            bindAddress
        );

        if (result.Connected && result.Health is not null)
        {
            LatestCoreError = null;
            CoreStatus = new StatusIndicatorViewModel
            {
                Label = "Core",
                Value = "Connected",
                Tone = "good",
            };
            ConnectionMessage = result.LaunchedByUi
                ? "flowarden core launched and connected."
                : "Connected to an already running flowarden core.";
            OnPropertyChanged(nameof(HasLatestCoreError));
            return;
        }

        LatestCoreError = result.Error;
        CoreStatus = new StatusIndicatorViewModel
        {
            Label = "Core",
            Value = "Offline",
            Tone = "warning",
        };
        ConnectionMessage = result.Error?.Message ?? "Failed to connect to flowarden core.";
        OnPropertyChanged(nameof(HasLatestCoreError));
    }
}
