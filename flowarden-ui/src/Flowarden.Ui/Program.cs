using Avalonia;
using System;
using Flowarden.Ui.Configuration;

namespace Flowarden.Ui;

sealed class Program
{
    internal static string[] StartupArgs { get; private set; } = [];

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        StartupArgs = args;
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(CoreEndpointResolver.RemoveCoreEndpointArgs(args));
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();

#if DEBUG
        builder = builder.LogToTrace();
#endif

        return builder;
    }
}
