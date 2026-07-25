using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Platform.Storage;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Flowarden.Ui.Views;
using Flowarden.Ui.ViewModels;
using Flowarden.Ui.Services;
using Flowarden.Ui.Configuration;
using Grpc.Net.Client;

namespace Flowarden.Ui;

public partial class App : Application
{
    private static GrpcChannel? _coreChannel;
    private static CoreEndpointOptions? _coreEndpoint;
    private bool _allowShutdown;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            _coreEndpoint = CoreEndpointResolver.Resolve(Program.StartupArgs);
            _coreChannel = CreateCoreChannel(_coreEndpoint.GrpcAddress);
            var coreHealthService = new CoreHealthService(_coreChannel);
            var coreLauncherService = new CoreLauncherService();
            var discoveryClient = new DiscoveryClient(_coreChannel);
            var projectionClient = new ProjectionClient(_coreChannel);
            var controlClient = new ControlClient(_coreChannel);
            var coreConnectionCoordinator = new CoreConnectionCoordinator(
                coreHealthService,
                coreLauncherService
            );
            var shellViewModel = new AppShellViewModel(
                coreConnectionCoordinator,
                discoveryClient,
                projectionClient,
                controlClient,
                coreHealthService,
                _coreEndpoint.BindAddress,
                _coreEndpoint.Source,
                ReadInitialPageId()
            );
            var mainWindow = new MainWindow
            {
                DataContext = shellViewModel,
            };
            shellViewModel.SourcePage.OfflineFileRequested += async () =>
            {
                var files = await mainWindow.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = "Select offline pcap",
                        AllowMultiple = false,
                        FileTypeFilter =
                        [
                            new FilePickerFileType("Packet capture")
                            {
                                Patterns = ["*.pcap", "*.pcapng"],
                                MimeTypes = ["application/vnd.tcpdump.pcap"],
                            },
                        ],
                    }
                );

                return files.Count == 0 ? null : files[0].TryGetLocalPath();
            };
            desktop.ShutdownRequested += (_, eventArgs) =>
            {
                if (_allowShutdown)
                {
                    return;
                }

                eventArgs.Cancel = true;
                _ = HandleDesktopShutdownAsync(desktop, shellViewModel);
            };
            desktop.MainWindow = mainWindow;
            _ = InitializeCoreConnectionAsync(shellViewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static GrpcChannel CreateCoreChannel(string address)
    {
        return GrpcChannel.ForAddress(address);
    }

    private static string? ReadInitialPageId()
    {
        return Environment.GetEnvironmentVariable("FLOWARDEN_UI_INITIAL_PAGE");
    }

    private static async Task InitializeCoreConnectionAsync(AppShellViewModel shellViewModel)
    {
        if (_coreEndpoint is null)
        {
            return;
        }

        var workingDirectory = Directory.GetCurrentDirectory();
        var binaryPath = LocateCoreBinaryPath();

        await shellViewModel.InitializeCoreConnectionAsync(
            workingDirectory,
            binaryPath,
            _coreEndpoint.BindAddress
        );
    }

    private static async Task HandleDesktopShutdownAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        AppShellViewModel shellViewModel
    )
    {
        if (Current is not App app)
        {
            desktop.Shutdown();
            return;
        }

        try
        {
            await shellViewModel.HandleUiExitAsync();
        }
        finally
        {
            app._allowShutdown = true;
            desktop.Shutdown();
        }
    }

    private static string LocateCoreBinaryPath()
    {
        var executableName = OperatingSystem.IsWindows() ? "flowarden.exe" : "flowarden";
        foreach (var root in GetCoreBinarySearchRoots())
        {
            var directory = new DirectoryInfo(Path.GetFullPath(root));

            var bundledCandidate = Path.Combine(directory.FullName, executableName);
            if (File.Exists(bundledCandidate))
            {
                return bundledCandidate;
            }

            while (directory is not null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "flowarden",
                    "target",
                    "debug",
                    executableName
                );

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        return Path.Combine(
            Directory.GetCurrentDirectory(),
            "flowarden",
            "target",
            "debug",
            executableName
        );
    }

    private static string[] GetCoreBinarySearchRoots()
    {
        var roots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
        };

        if (!OperatingSystem.IsMacOS())
        {
            return roots;
        }

        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        if (baseDirectory.Parent?.Parent?.Parent is not { } appBundleRoot)
        {
            return roots;
        }

        var macOsDirectory = Path.Combine(appBundleRoot.FullName, "MacOS");
        var resourcesDirectory = Path.Combine(appBundleRoot.FullName, "Resources");
        return
        [
            AppContext.BaseDirectory,
            macOsDirectory,
            resourcesDirectory,
            Directory.GetCurrentDirectory(),
        ];
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
