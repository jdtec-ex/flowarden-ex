namespace Flowarden.Ui.Models;

public sealed class CoreErrorDto
{
    public string Source { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}
