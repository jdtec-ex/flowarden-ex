using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Flowarden.Ui.Views;
using Flowarden.Ui.ViewModels;
using Flowarden.Ui.Services;
using Grpc.Net.Client;

namespace Flowarden.Ui;

public partial class App : Application
{
    private static GrpcChannel? _coreChannel;

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
            _coreChannel = CreateCoreChannel("http://127.0.0.1:39091");
            var coreHealthService = new CoreHealthService(_coreChannel);
            var coreLauncherService = new CoreLauncherService();
            var discoveryClient = new DiscoveryClient(_coreChannel);
            var coreConnectionCoordinator = new CoreConnectionCoordinator(
                coreHealthService,
                coreLauncherService
            );
            var shellViewModel = new AppShellViewModel(coreConnectionCoordinator, discoveryClient);
            desktop.MainWindow = new MainWindow
            {
                DataContext = shellViewModel,
            };
            _ = InitializeCoreConnectionAsync(shellViewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static GrpcChannel CreateCoreChannel(string address)
    {
        return GrpcChannel.ForAddress(address);
    }

    private static async Task InitializeCoreConnectionAsync(AppShellViewModel shellViewModel)
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        var binaryPath = LocateCoreBinaryPath();

        await shellViewModel.InitializeCoreConnectionAsync(
            workingDirectory,
            binaryPath,
            "127.0.0.1:39091"
        );
    }

    private static string LocateCoreBinaryPath()
    {
        var executableName = OperatingSystem.IsWindows() ? "flowarden.exe" : "flowarden";
        var searchRoots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
        };

        foreach (var root in searchRoots)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(root));

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
