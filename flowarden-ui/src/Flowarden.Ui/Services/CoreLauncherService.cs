using System.Diagnostics;

namespace Flowarden.Ui.Services;

public sealed class CoreLauncherService
{
    public Process? Start(string workingDirectory, string binaryPath, string bindAddress)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };

        processStartInfo.ArgumentList.Add("service");
        processStartInfo.ArgumentList.Add("--bind");
        processStartInfo.ArgumentList.Add(bindAddress);

        return Process.Start(processStartInfo);
    }
}
