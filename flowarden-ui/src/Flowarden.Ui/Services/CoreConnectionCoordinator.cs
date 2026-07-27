using System;
using System.Diagnostics;
using System.IO;
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
        var hasLocalBinary = !string.IsNullOrWhiteSpace(binaryPath) && File.Exists(binaryPath);

        // Prefer launching the binary the UI resolved. Attaching to an already-running
        // core is a common source of "stale enrichment" (old process without process/rDNS).
        if (hasLocalBinary)
        {
            await TryStopExistingCoreOnBindAsync(bindAddress, cancellationToken);
            return await LaunchAndWaitAsync(
                workingDirectory,
                binaryPath,
                bindAddress,
                cancellationToken
            );
        }

        var existing = await TryGetHealthAsync(cancellationToken);
        if (existing is not null)
        {
            return CoreConnectionResult.CreateConnected(
                existing,
                launchedProcess: null,
                launchedByUi: false,
                binaryPath: binaryPath
            );
        }

        return CoreConnectionResult.Failed(
            new CoreErrorDto
            {
                Source = "CoreLauncher",
                Reason = "BinaryMissing",
                Message =
                    $"Flowarden core binary was not found at '{binaryPath}'. Build flowarden (cargo build -p flowarden) and restart the UI.",
            }
        );
    }

    private async Task<CoreConnectionResult> LaunchAndWaitAsync(
        string workingDirectory,
        string binaryPath,
        string bindAddress,
        CancellationToken cancellationToken
    )
    {
        Process? process;
        try
        {
            process = _coreLauncherService.Start(workingDirectory, binaryPath, bindAddress);
        }
        catch (Exception ex)
        {
            return CoreConnectionResult.Failed(
                new CoreErrorDto
                {
                    Source = "CoreLauncher",
                    Reason = "LaunchFailed",
                    Message = $"Failed to start flowarden core process: {ex.Message}",
                }
            );
        }

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
                        Message =
                            $"Flowarden core exited before becoming healthy. Exit code: {process.ExitCode}. Binary: {binaryPath}",
                    }
                );
            }

            var health = await TryGetHealthAsync(cancellationToken);
            if (health is not null)
            {
                return CoreConnectionResult.CreateConnected(
                    health,
                    process,
                    launchedByUi: true,
                    binaryPath: binaryPath
                );
            }

            await Task.Delay(200, cancellationToken);
        }

        return CoreConnectionResult.Failed(
            new CoreErrorDto
            {
                Source = "CoreHealth",
                Reason = "NotReachable",
                Message =
                    $"Flowarden core did not become healthy within the startup window. Binary: {binaryPath}",
            }
        );
    }

    private async Task TryStopExistingCoreOnBindAsync(
        string bindAddress,
        CancellationToken cancellationToken
    )
    {
        var existing = await TryGetHealthAsync(cancellationToken);
        if (existing is null)
        {
            TryKillFlowardenListenersOnBind(bindAddress);
            return;
        }

        // Free the port so a fresh binary can bind.
        TryKillFlowardenListenersOnBind(bindAddress);

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (await TryGetHealthAsync(cancellationToken) is null)
            {
                return;
            }

            await Task.Delay(150, cancellationToken);
        }
    }

    private static void TryKillFlowardenListenersOnBind(string bindAddress)
    {
        if (!TryParsePort(bindAddress, out var port) || port <= 0)
        {
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/sbin/lsof",
                ArgumentList = { "-nP", $"-iTCP:{port}", "-sTCP:LISTEN", "-t" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var lsof = Process.Start(psi);
            if (lsof is null)
            {
                return;
            }

            var output = lsof.StandardOutput.ReadToEnd();
            lsof.WaitForExit(2000);
            foreach (
                var line in output.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            {
                if (!int.TryParse(line, out var pid) || pid <= 0)
                {
                    continue;
                }

                try
                {
                    using var process = Process.GetProcessById(pid);
                    // Only kill flowarden cores, never random listeners.
                    if (
                        !string.Equals(
                            process.ProcessName,
                            "flowarden",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && !process.ProcessName.Contains(
                            "flowarden",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        continue;
                    }

                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
                catch
                {
                    // ignore races
                }
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static bool TryParsePort(string bindAddress, out int port)
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(bindAddress))
        {
            return false;
        }

        var parts = bindAddress.Trim().Split(':');
        return parts.Length >= 2 && int.TryParse(parts[^1], out port);
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
        CoreErrorDto? error,
        string? binaryPath
    )
    {
        Connected = connected;
        Health = health;
        LaunchedProcess = launchedProcess;
        LaunchedByUi = launchedByUi;
        Error = error;
        BinaryPath = binaryPath;
    }

    public bool Connected { get; }

    public CoreHealthDto? Health { get; }

    public Process? LaunchedProcess { get; }

    public bool LaunchedByUi { get; }

    public CoreErrorDto? Error { get; }

    public string? BinaryPath { get; }

    public static CoreConnectionResult CreateConnected(
        CoreHealthDto health,
        Process? launchedProcess,
        bool launchedByUi,
        string? binaryPath = null
    ) => new(true, health, launchedProcess, launchedByUi, error: null, binaryPath);

    public static CoreConnectionResult Failed(CoreErrorDto error) =>
        new(
            false,
            health: null,
            launchedProcess: null,
            launchedByUi: false,
            error,
            binaryPath: null
        );
}
