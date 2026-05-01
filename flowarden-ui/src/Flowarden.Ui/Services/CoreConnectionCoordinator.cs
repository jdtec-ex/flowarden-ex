using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Flowarden.Ui.Models;

namespace Flowarden.Ui.Services;

public sealed class CoreConnectionCoordinator
{
    private readonly CoreHealthService _coreHealthService;
    private readonly CoreLauncherService _coreLauncherService;

    public CoreConnectionCoordinator(
        CoreHealthService coreHealthService,
        CoreLauncherService coreLauncherService
    )
    {
        _coreHealthService = coreHealthService;
        _coreLauncherService = coreLauncherService;
    }

    public async Task<CoreConnectionResult> EnsureConnectedAsync(
        string workingDirectory,
        string binaryPath,
        string bindAddress,
        CancellationToken cancellationToken = default
    )
    {
        var existing = await TryGetHealthAsync(cancellationToken);
        if (existing is not null)
        {
            return CoreConnectionResult.CreateConnected(
                existing,
                launchedProcess: null,
                launchedByUi: false
            );
        }

        var process = _coreLauncherService.Start(workingDirectory, binaryPath, bindAddress);
        if (process is null)
        {
            return CoreConnectionResult.Failed(
                new CoreErrorDto
                {
                    Source = "CoreLauncher",
                    Reason = "LaunchFailed",
                    Message = "Failed to start flowarden core process.",
                }
            );
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                return CoreConnectionResult.Failed(
                    new CoreErrorDto
                    {
                        Source = "CoreLauncher",
                        Reason = "ExitedEarly",
                        Message = $"Flowarden core exited before becoming healthy. Exit code: {process.ExitCode}.",
                    }
                );
            }

            var health = await TryGetHealthAsync(cancellationToken);
            if (health is not null)
            {
                return CoreConnectionResult.CreateConnected(health, process, launchedByUi: true);
            }

            await Task.Delay(200, cancellationToken);
        }

        return CoreConnectionResult.Failed(
            new CoreErrorDto
            {
                Source = "CoreHealth",
                Reason = "NotReachable",
                Message = "Flowarden core did not become healthy within the startup window.",
            }
        );
    }

    private async Task<CoreHealthDto?> TryGetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _coreHealthService.GetHealthAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class CoreConnectionResult
{
    private CoreConnectionResult(
        bool connected,
        CoreHealthDto? health,
        Process? launchedProcess,
        bool launchedByUi,
        CoreErrorDto? error
    )
    {
        Connected = connected;
        Health = health;
        LaunchedProcess = launchedProcess;
        LaunchedByUi = launchedByUi;
        Error = error;
    }

    public bool Connected { get; }

    public CoreHealthDto? Health { get; }

    public Process? LaunchedProcess { get; }

    public bool LaunchedByUi { get; }

    public CoreErrorDto? Error { get; }

    public static CoreConnectionResult CreateConnected(
        CoreHealthDto health,
        Process? launchedProcess,
        bool launchedByUi
    ) => new(true, health, launchedProcess, launchedByUi, error: null);

    public static CoreConnectionResult Failed(CoreErrorDto error) =>
        new(false, health: null, launchedProcess: null, launchedByUi: false, error);
}
