using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Flowarden.Ui.Configuration;

public static class CoreEndpointResolver
{
    public const string CommandLineOption = "--core-bind";
    public const string EnvironmentVariable = "FLOWARDEN_CORE_BIND";
    private const string SettingsDirectoryName = "flowarden";
    private const string SettingsFileName = "core-bind.txt";

    public static CoreEndpointOptions Resolve(string[] args)
    {
        var commandLineValue = ReadCommandLineValue(args);
        if (!string.IsNullOrWhiteSpace(commandLineValue))
        {
            return Create(commandLineValue, "command-line");
        }

        var environmentValue = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return Create(environmentValue, "environment");
        }

        var settingsValue = ReadSettingsValue();
        if (!string.IsNullOrWhiteSpace(settingsValue))
        {
            return Create(settingsValue, "settings");
        }

        var generated = Create(GenerateLoopbackBindAddress(), "generated-settings");
        PersistSettingsValue(generated.BindAddress);
        return generated;
    }

    public static string[] RemoveCoreEndpointArgs(string[] args)
    {
        var filtered = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith($"{CommandLineOption}=", StringComparison.Ordinal))
            {
                continue;
            }

            if (arg == CommandLineOption)
            {
                i++;
                continue;
            }

            filtered.Add(arg);
        }

        return filtered.ToArray();
    }

    private static CoreEndpointOptions Create(string value, string source)
    {
        var bindAddress = NormalizeLoopbackBindAddress(value);
        return new CoreEndpointOptions(bindAddress, source);
    }

    private static string? ReadCommandLineValue(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith($"{CommandLineOption}=", StringComparison.Ordinal))
            {
                return arg[(CommandLineOption.Length + 1)..];
            }

            if (arg == CommandLineOption && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string NormalizeLoopbackBindAddress(string value)
    {
        var trimmed = value.Trim();
        if (!IPEndPoint.TryParse(trimmed, out var endpoint))
        {
            throw new InvalidOperationException($"Invalid core bind address `{trimmed}`.");
        }

        if (!IPAddress.IsLoopback(endpoint.Address))
        {
            throw new InvalidOperationException("UI-managed flowarden core must bind to a loopback address.");
        }

        if (endpoint.Port == 0)
        {
            throw new InvalidOperationException("UI-managed flowarden core bind port must be explicit.");
        }

        var address = endpoint.Address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{endpoint.Address}]"
            : endpoint.Address.ToString();
        return $"{address}:{endpoint.Port}";
    }

    private static string GenerateLoopbackBindAddress()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            return $"{IPAddress.Loopback}:{endpoint.Port}";
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string? ReadSettingsValue()
    {
        var path = SettingsFilePath();
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private static void PersistSettingsValue(string bindAddress)
    {
        var path = SettingsFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, bindAddress);
    }

    private static string SettingsFilePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, SettingsDirectoryName, SettingsFileName);
    }
}
