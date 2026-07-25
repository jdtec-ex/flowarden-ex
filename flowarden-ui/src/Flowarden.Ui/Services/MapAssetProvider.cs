using System;
using System.IO;
using Avalonia.Platform;

namespace Flowarden.Ui.Services;

public static class MapAssetProvider
{
    private const string WorldMapUri =
        "avares://Flowarden.Ui/Assets/world-map-110m-equal-earth.path";

    public static string WorldMapPathData { get; } = LoadWorldMapPathData();

    private static string LoadWorldMapPathData()
    {
        using var stream = AssetLoader.Open(new Uri(WorldMapUri));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
