namespace Flowarden.Ui.Models;

public sealed class InspectFilterDto
{
    public string? Address { get; init; }

    public string? Port { get; init; }

    public string? State { get; init; }

    public string? SourceAddress { get; init; }

    public string? DestinationAddress { get; init; }

    public string? ServiceName { get; init; }

    public string? Protocol { get; init; }

    public string? Direction { get; init; }

    public string? Bpf { get; init; }
}
