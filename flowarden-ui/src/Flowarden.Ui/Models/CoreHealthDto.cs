namespace Flowarden.Ui.Models;

public sealed class CoreHealthDto
{
    public string Status { get; init; } = string.Empty;

    public ulong StartedAtUnixSeconds { get; init; }
}
