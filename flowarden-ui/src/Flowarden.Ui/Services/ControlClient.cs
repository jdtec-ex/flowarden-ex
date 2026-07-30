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

    public Task<ControlActionResult> SetLiveSourceAsync(
        string deviceName,
        CancellationToken cancellationToken = default
    )
    {
        return SetSourceAsync(
            new CaptureSourceSpec
            {
                Mode = CaptureSourceMode.Live,
                DeviceName = deviceName,
            },
            cancellationToken
        );
    }

    public Task<ControlActionResult> SetOfflineSourceAsync(
        string filePath,
        CancellationToken cancellationToken = default
    )
    {
        return SetSourceAsync(
            new CaptureSourceSpec
            {
                Mode = CaptureSourceMode.Offline,
                FilePath = filePath,
            },
            cancellationToken
        );
    }

    private async Task<ControlActionResult> SetSourceAsync(
        CaptureSourceSpec source,
        CancellationToken cancellationToken
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

    public async Task<ControlActionResult> PauseCaptureAsync(
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteAsync(
            async () =>
                await _client.PauseCaptureAsync(
                    new PauseCaptureRequest(),
                    cancellationToken: cancellationToken
                )
        );
    }

    public async Task<ControlActionResult> ResumeCaptureAsync(
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteAsync(
            async () =>
                await _client.ResumeCaptureAsync(
                    new ResumeCaptureRequest(),
                    cancellationToken: cancellationToken
                )
        );
    }

    public async Task<ControlActionResult> SetSignalPolicyAsync(
        ulong dataThresholdBytes,
        System.Collections.Generic.IEnumerable<string> watchedHosts,
        System.Collections.Generic.IEnumerable<string> knownBadHosts,
        CancellationToken cancellationToken = default
    )
    {
        var request = new SetSignalPolicyRequest { DataThresholdBytes = dataThresholdBytes };
        request.WatchedHosts.AddRange(watchedHosts);
        request.KnownBadHosts.AddRange(knownBadHosts);
        return await ExecuteAsync(
            async () =>
                await _client.SetSignalPolicyAsync(request, cancellationToken: cancellationToken)
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

    public async Task<ControlActionResult> SetSyslogConfigAsync(
        bool enabled,
        string target,
        string proto,
        bool emitSignals,
        bool emitFlows,
        ulong flowMinBytes,
        ulong flowDeltaBytes,
        ulong flowIntervalSecs,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteAsync(
            async () =>
                await _client.SetSyslogConfigAsync(
                    new SetSyslogConfigRequest
                    {
                        Enabled = enabled,
                        Target = target ?? string.Empty,
                        Proto = string.IsNullOrWhiteSpace(proto) ? "udp" : proto,
                        Facility = "local0",
                        Tag = "flowarden",
                        EmitSignals = emitSignals,
                        EmitFlows = emitFlows,
                        FlowMinBytes = flowMinBytes,
                        FlowDeltaBytes = flowDeltaBytes,
                        FlowIntervalSecs = flowIntervalSecs,
                    },
                    cancellationToken: cancellationToken
                )
        );
    }

    public async Task<SyslogConfigResponse?> GetSyslogConfigAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await _client.GetSyslogConfigAsync(
                new GetSyslogConfigRequest(),
                cancellationToken: cancellationToken
            );
        }
        catch (RpcException)
        {
            return null;
        }
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
