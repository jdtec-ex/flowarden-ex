using System;
using System.Threading;
using System.Threading.Tasks;
using Flowarden.Control.V1;
using Grpc.Core;
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
        return await ExecuteAsync(
            async () =>
                await _client.SetSourceAsync(
                    new SetSourceRequest { Source = source },
                    cancellationToken: cancellationToken
                )
        );
    }

    public async Task<ControlActionResult> ApplyFilterAsync(
        string bpf,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteAsync(
            async () =>
                await _client.ApplyFilterAsync(
                    new ApplyFilterRequest { Bpf = bpf },
                    cancellationToken: cancellationToken
                )
        );
    }

    public async Task<ControlActionResult> StartCaptureAsync(
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteAsync(
            async () =>
                await _client.StartCaptureAsync(
                    new StartCaptureRequest(),
                    cancellationToken: cancellationToken
                )
        );
    }

    public async Task<ControlActionResult> StopCaptureAsync(
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteAsync(
            async () =>
                await _client.StopCaptureAsync(
                    new StopCaptureRequest(),
                    cancellationToken: cancellationToken
                )
        );
    }

    public async Task<ControlActionResult> ShutdownCoreAsync(
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteAsync(
            async () =>
                await _client.ShutdownCoreAsync(
                    new ShutdownCoreRequest(),
                    cancellationToken: cancellationToken
                )
        );
    }

    private static ControlActionResult MapResponse(ControlResponse response)
    {
        return new ControlActionResult
        {
            Accepted = response.Accepted,
            Message = response.Message,
        };
    }

    private static async Task<ControlActionResult> ExecuteAsync(
        Func<Task<ControlResponse>> action
    )
    {
        try
        {
            return MapResponse(await action());
        }
        catch (RpcException ex)
        {
            return new ControlActionResult
            {
                Accepted = false,
                Message = MapRpcException(ex),
            };
        }
    }

    private static string MapRpcException(RpcException exception)
    {
        return exception.StatusCode switch
        {
            StatusCode.Unimplemented =>
                "The running flowarden core does not support this control action yet. Restart the resident core after upgrading.",
            StatusCode.Unavailable => "The resident flowarden core is unavailable.",
            StatusCode.InvalidArgument => exception.Status.Detail,
            StatusCode.FailedPrecondition => exception.Status.Detail,
            StatusCode.PermissionDenied => exception.Status.Detail,
            StatusCode.Internal => exception.Status.Detail,
            _ => string.IsNullOrWhiteSpace(exception.Status.Detail)
                ? $"Resident core RPC failed: {exception.StatusCode}"
                : exception.Status.Detail,
        };
    }
}
