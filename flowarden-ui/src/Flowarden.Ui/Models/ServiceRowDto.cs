namespace Flowarden.Ui.Models;

public sealed class ServiceRowDto
{
    public string Name { get; init; } = string.Empty;

    public string Transport { get; init; } = string.Empty;

    public string Confidence { get; init; } = string.Empty;

    public ulong Packets { get; init; }

    public ulong Bytes { get; init; }
}
