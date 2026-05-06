using System.Threading;
using System.Threading.Tasks;
using Flowarden.Control.V1;
using Grpc.Net.Client;

namespace Flowarden.Ui.Services;

public sealed class ControlActionResult
{
    public bool Accepted { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed class ControlClient
{
    private readonly ControlService.ControlServiceClient _client;

    public ControlClient(GrpcChannel channel)
    {
        _client = new ControlService.ControlServiceClient(channel);
    }

    public async Task<ControlActionResult> SetSourceAsync(
        string source,
        CancellationToken cancellationToken = default
    )
    {
        var response = await _client.SetSourceAsync(
            new SetSourceRequest { Source = source },
            cancellationToken: cancellationToken
        );

        return MapResponse(response);
    }

    public async Task<ControlActionResult> ApplyFilterAsync(
        string bpf,
        CancellationToken cancellationToken = default
    )
    {
        var response = await _client.ApplyFilterAsync(
            new ApplyFilterRequest { Bpf = bpf },
            cancellationToken: cancellationToken
        );

        return MapResponse(response);
    }

    public async Task<ControlActionResult> StartCaptureAsync(
        CancellationToken cancellationToken = default
    )
    {
        var response = await _client.StartCaptureAsync(
            new StartCaptureRequest(),
            cancellationToken: cancellationToken
        );

        return MapResponse(response);
    }

    public async Task<ControlActionResult> StopCaptureAsync(
        CancellationToken cancellationToken = default
    )
    {
        var response = await _client.StopCaptureAsync(
            new StopCaptureRequest(),
            cancellationToken: cancellationToken
        );

        return MapResponse(response);
    }

    private static ControlActionResult MapResponse(ControlResponse response)
    {
        return new ControlActionResult
        {
            Accepted = response.Accepted,
            Message = response.Message,
        };
    }
}
