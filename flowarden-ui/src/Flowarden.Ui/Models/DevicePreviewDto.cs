namespace Flowarden.Ui.Models;

public sealed class DevicePreviewDto
{
    public string Name { get; init; } = string.Empty;

    public ulong PacketsSeen { get; init; }

    public ulong BytesSeen { get; init; }

    public bool Unsupported { get; init; }

    public string? Error { get; init; }
}
