using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;

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
            var hue = Math.Abs(StringComparer.Ordinal.GetHashCode(Name)) % 360;
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
/// Resolves process icons asynchronously. Bitmap extraction is best-effort;
/// monogram metadata is always available via <see cref="ProcessIconKey"/>.
/// </summary>
public interface IProcessIconService
{
    /// <summary>Returns a bitmap when OS extraction succeeds; otherwise null (use monogram).</summary>
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
                // Platform bitmap extraction is optional. Path presence is recorded for future
                // native loaders; monogram UI covers macOS/Windows/Linux consistently for v1.
                IImage? image = null;
                if (!string.IsNullOrWhiteSpace(key.Path) && File.Exists(key.Path))
                {
                    image = null; // Reserved for OS-specific extractors without stream pollution.
                }

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
}
