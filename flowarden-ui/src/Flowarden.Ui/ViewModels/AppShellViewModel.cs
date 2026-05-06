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
    private readonly string _bindAddress;
    private readonly IReadOnlyDictionary<string, AppShellPageViewModel> _pages;
    private readonly CoreConnectionCoordinator? _coreConnectionCoordinator;
    private readonly DiscoveryClient? _discoveryClient;
    private readonly ProjectionClient? _projectionClient;
    private readonly ControlClient? _controlClient;
    private readonly CoreHealthService? _coreHealthService;

    public AppShellViewModel()
        : this(null)
    {
    }

    public AppShellViewModel(
        CoreConnectionCoordinator? coreConnectionCoordinator,
        DiscoveryClient? discoveryClient = null,
        ProjectionClient? projectionClient = null,
        ControlClient? controlClient = null,
        CoreHealthService? coreHealthService = null,
        string bindAddress = "127.0.0.1:39091"
    )
    {
        _bindAddress = bindAddress;
        _coreConnectionCoordinator = coreConnectionCoordinator;
        _discoveryClient = discoveryClient;
        _projectionClient = projectionClient;
        _controlClient = controlClient;
        _coreHealthService = coreHealthService;
        NavigationItems = new ReadOnlyCollection<AppNavigationItemViewModel>(
            [
                new AppNavigationItemViewModel { Id = "source", Label = "Source", CompactLabel = "Src" },
                new AppNavigationItemViewModel { Id = "overview", Label = "Overview", CompactLabel = "Ovr" },
                new AppNavigationItemViewModel { Id = "inspect", Label = "Inspect", CompactLabel = "Insp" },
                new AppNavigationItemViewModel { Id = "settings", Label = "Settings", CompactLabel = "Set" },
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

        SourcePage = new SourcePageViewModel(_discoveryClient, _controlClient);
        SourcePage.SessionStateChanged += OnSourceSessionStateChanged;
        OverviewPage = new OverviewPageViewModel(_projectionClient);
        InspectPage = new InspectPageViewModel(_projectionClient);
        SettingsPage = new SettingsPageViewModel(
            _coreHealthService,
            _discoveryClient,
            _bindAddress,
            LatestCoreError
        );

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

    public string Subtitle { get; } = "Traffic Flow Warden";

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

    public SourcePageViewModel SourcePage { get; }

    public OverviewPageViewModel OverviewPage { get; }

    public InspectPageViewModel InspectPage { get; }

    public SettingsPageViewModel SettingsPage { get; }

    [ObservableProperty]
    private string connectionMessage = "Connecting to flowarden core...";

    [ObservableProperty]
    private CoreErrorDto? latestCoreError;

    [ObservableProperty]
    private bool isRailCollapsed;

    public string HeaderSupportingText =>
        LatestCoreError?.Message
        ?? (CoreStatus.Value == "Connected" ? CurrentPage.Description : ConnectionMessage);

    public bool IsSourcePageActive => CurrentPageId == "source";

    public bool IsOverviewPageActive => CurrentPageId == "overview";

    public bool IsInspectPageActive => CurrentPageId == "inspect";

    public bool IsSettingsPageActive => CurrentPageId == "settings";

    public bool HasLatestCoreError => LatestCoreError is not null;

    public string RailToggleLabel => IsRailCollapsed ? "Expand" : "Collapse";

    public double RailWidth => IsRailCollapsed ? 96 : 180;

    public string StartCaptureLabel => IsRailCollapsed ? "Start" : "Start Capture";

    partial void OnCoreStatusChanged(StatusIndicatorViewModel value)
    {
        OnPropertyChanged(nameof(HeaderSupportingText));
    }

    partial void OnCurrentPageChanged(AppShellPageViewModel value)
    {
        OnPropertyChanged(nameof(HeaderSupportingText));
    }

    partial void OnConnectionMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HeaderSupportingText));
    }

    partial void OnLatestCoreErrorChanged(CoreErrorDto? value)
    {
        OnPropertyChanged(nameof(HeaderSupportingText));
        OnPropertyChanged(nameof(HasLatestCoreError));
    }

    partial void OnIsRailCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(RailToggleLabel));
        OnPropertyChanged(nameof(RailWidth));
        OnPropertyChanged(nameof(StartCaptureLabel));
    }

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
        OverviewPage.SetMode(ActiveMode);
    }

    [RelayCommand]
    private void StartCapture()
    {
        Navigate("source");
    }

    [RelayCommand]
    private void ToggleRail()
    {
        IsRailCollapsed = !IsRailCollapsed;
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
            await LoadSourcePageAsync();
            await LoadOverviewPageAsync();
            await LoadInspectPageAsync();
            await LoadSettingsPageAsync();
            return;
        }

        LatestCoreError = result.Error;
        await LoadSettingsPageAsync();
        CoreStatus = new StatusIndicatorViewModel
        {
            Label = "Core",
            Value = "Offline",
            Tone = "warning",
        };
        ConnectionMessage = result.Error?.Message ?? "Failed to connect to flowarden core.";
    }

    private async Task LoadSourcePageAsync()
    {
        if (SourcePage is null)
        {
            return;
        }

        await SourcePage.LoadAsync();
        OnSourceSessionStateChanged(SourcePage.CurrentSession, SourcePage.StatusMessage);
    }

    private async Task LoadOverviewPageAsync()
    {
        if (OverviewPage is null)
        {
            return;
        }

        await OverviewPage.LoadAsync();
    }

    private async Task LoadInspectPageAsync()
    {
        if (InspectPage is null)
        {
            return;
        }

        await InspectPage.LoadAsync();
    }

    private async Task LoadSettingsPageAsync()
    {
        if (SettingsPage is null)
        {
            return;
        }

        await SettingsPage.LoadAsync(LatestCoreError);
    }

    private void OnSourceSessionStateChanged(CaptureSessionStateDto? session, string? statusMessage)
    {
        var status = session?.CaptureStatus?.ToLowerInvariant() ?? "idle";
        var (value, tone) = status switch
        {
            "starting" => ("Starting", "warning"),
            "running" => ("Running", "good"),
            "stopping" => ("Stopping", "warning"),
            "armed" => ("Armed", "warning"),
            _ => ("Idle", "neutral"),
        };

        CaptureStatus = new StatusIndicatorViewModel
        {
            Label = "Capture",
            Value = value,
            Tone = tone,
        };

        if (!string.IsNullOrWhiteSpace(statusMessage) && CoreStatus.Value == "Connected")
        {
            ConnectionMessage = statusMessage;
        }
    }
}
