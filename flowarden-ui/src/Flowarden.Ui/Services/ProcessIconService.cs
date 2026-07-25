using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Flowarden.Ui.Services;

public readonly record struct ProcessIconKey(string Path, string BundleId, string Name, uint Pid)
{
    public static ProcessIconKey FromConnection(
        string processPath,
        string processBundleId,
        string processName,
        uint processPid
    ) =>
        new(
            processPath?.Trim() ?? string.Empty,
            processBundleId?.Trim() ?? string.Empty,
            processName?.Trim() ?? string.Empty,
            processPid
        );

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Path)
        && string.IsNullOrWhiteSpace(BundleId)
        && string.IsNullOrWhiteSpace(Name);

    public string Monogram =>
        string.IsNullOrWhiteSpace(Name)
            ? "?"
            : char.ToUpperInvariant(Name.Trim()[0]).ToString();

    public IBrush MonogramBrush
    {
        get
        {
            var seed = string.IsNullOrWhiteSpace(Name) ? "?" : Name;
            var hue = Math.Abs(StringComparer.Ordinal.GetHashCode(seed)) % 360;
            return new SolidColorBrush(HsvToColor(hue / 360.0, 0.42, 0.70));
        }
    }

    private static Color HsvToColor(double h, double s, double v)
    {
        h = (h % 1.0 + 1.0) % 1.0;
        var i = (int)(h * 6.0);
        var f = h * 6.0 - i;
        var p = v * (1.0 - s);
        var q = v * (1.0 - f * s);
        var t = v * (1.0 - (1.0 - f) * s);
        var (r, g, b) = (i % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}

/// <summary>
/// Resolves process icons asynchronously with LRU cache.
/// Returns OS bitmaps when available; UI should monogram when null.
/// </summary>
public interface IProcessIconService
{
    Task<IImage?> GetIconAsync(ProcessIconKey key, CancellationToken cancellationToken = default);
}

public sealed class ProcessIconService : IProcessIconService
{
    private const int MaxCacheEntries = 256;
    private readonly object _gate = new();
    private readonly LinkedList<string> _lru = new();
    private readonly Dictionary<string, LinkedListNode<string>> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IImage?> _cache = new(StringComparer.Ordinal);

    public Task<IImage?> GetIconAsync(
        ProcessIconKey key,
        CancellationToken cancellationToken = default
    )
    {
        if (key.IsEmpty)
        {
            return Task.FromResult<IImage?>(null);
        }

        var cacheKey = BuildCacheKey(key);
        lock (_gate)
        {
            if (_cache.TryGetValue(cacheKey, out var hit))
            {
                Touch(cacheKey);
                return Task.FromResult(hit);
            }
        }

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var image = TryLoadPlatformIcon(key);
                lock (_gate)
                {
                    if (_cache.TryGetValue(cacheKey, out var existing))
                    {
                        return existing;
                    }

                    _cache[cacheKey] = image;
                    var node = _lru.AddFirst(cacheKey);
                    _nodes[cacheKey] = node;
                    while (_cache.Count > MaxCacheEntries && _lru.Last is not null)
                    {
                        var evict = _lru.Last.Value;
                        _lru.RemoveLast();
                        _nodes.Remove(evict);
                        _cache.Remove(evict);
                    }
                }

                return image;
            },
            cancellationToken
        );
    }

    private void Touch(string cacheKey)
    {
        if (_nodes.TryGetValue(cacheKey, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
        }
    }

    private static string BuildCacheKey(ProcessIconKey key)
    {
        if (!string.IsNullOrWhiteSpace(key.Path))
        {
            return "path:" + key.Path;
        }

        if (!string.IsNullOrWhiteSpace(key.BundleId))
        {
            return "bundle:" + key.BundleId;
        }

        return $"name:{key.Name}|{key.Pid}";
    }

    private static IImage? TryLoadPlatformIcon(ProcessIconKey key)
    {
        var path = ResolveIconPath(key);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) && !Directory.Exists(path))
        {
            return null;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return TryLoadWindowsIcon(path);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return TryLoadMacIcon(path);
            }
        }
        catch
        {
            // Fall back to monogram in UI.
        }

        return null;
    }

    private static string ResolveIconPath(ProcessIconKey key)
    {
        if (!string.IsNullOrWhiteSpace(key.Path))
        {
            // Prefer enclosing .app bundle on macOS for better icons.
            var path = key.Path;
            var appIdx = path.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
            if (appIdx > 0)
            {
                return path[..(appIdx + 4)];
            }

            return path;
        }

        return string.Empty;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IImage? TryLoadWindowsIcon(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static IImage? TryLoadMacIcon(string path)
    {
        // Use Quick Look thumbnail generation (built into macOS). Cached by path key.
        var tempDir = Path.Combine(Path.GetTempPath(), "flowarden-icons");
        Directory.CreateDirectory(tempDir);
        var safeName = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(path)
            )
        )[..16];
        var expectedPng = Path.Combine(tempDir, safeName + ".png");
        if (File.Exists(expectedPng))
        {
            return LoadBitmap(expectedPng);
        }

        // qlmanage names output after the input basename, so work in a private folder.
        var workDir = Path.Combine(tempDir, safeName);
        Directory.CreateDirectory(workDir);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/qlmanage",
                ArgumentList = { "-t", "-s", "64", "-o", workDir, path },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            if (!process.WaitForExit(2500))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore
                }

                return null;
            }

            var produced = Directory.GetFiles(workDir, "*.png");
            if (produced.Length == 0)
            {
                return null;
            }

            File.Copy(produced[0], expectedPng, overwrite: true);
            return LoadBitmap(expectedPng);
        }
        catch
        {
            return null;
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }

    private static IImage? LoadBitmap(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

}
