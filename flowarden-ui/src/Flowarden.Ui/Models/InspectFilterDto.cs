namespace Flowarden.Ui.Models;

/// <summary>
/// Committed Inspect result filter (L2). Capture BPF is session-level, not this DTO.
/// </summary>
public sealed class InspectFilterDto
{
    /// <summary>Multi-column OR free-text (UI-local only).</summary>
    public string? SearchText { get; init; }

    public string? Address { get; init; }

    public string? Port { get; init; }

    public string? State { get; init; }

    public string? SourceAddress { get; init; }

    public string? DestinationAddress { get; init; }

    public string? ServiceName { get; init; }

    public string? Protocol { get; init; }

    public string? Direction { get; init; }

    public string? ProcessName { get; init; }

    public string? Sni { get; init; }

    /// <summary>Experimental; only meaningful when country is known on the row path.</summary>
    public string? Country { get; init; }

    /// <summary>Deprecated Inspect bag — always leave null. Capture BPF is separate.</summary>
    public string? Bpf { get; init; }
}
