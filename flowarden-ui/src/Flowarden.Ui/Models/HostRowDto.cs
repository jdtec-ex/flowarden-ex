namespace Flowarden.Ui.Models;

public sealed class HostRowDto
{
    public string Host { get; init; } = string.Empty;

    public ulong Packets { get; init; }

    public ulong Bytes { get; init; }
}
