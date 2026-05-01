namespace Flowarden.Ui.Models;

public sealed class CaptureSessionStateDto
{
    public string SourceKind { get; init; } = "none";

    public string SourceDisplayName { get; init; } = string.Empty;

    public string CaptureStatus { get; init; } = "idle";

    public string Mode { get; init; } = "live";

    public string? Bpf { get; init; }
}
