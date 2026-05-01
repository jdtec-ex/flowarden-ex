using System.Threading;
using System.Threading.Tasks;
using Flowarden.Health.V1;
using Flowarden.Ui.Models;
using Grpc.Net.Client;

namespace Flowarden.Ui.Services;

public sealed class CoreHealthService
{
    private readonly HealthService.HealthServiceClient _client;

    public CoreHealthService(GrpcChannel channel)
    {
        _client = new HealthService.HealthServiceClient(channel);
    }

    public async Task<CoreHealthDto?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.GetHealthAsync(new GetHealthRequest(), cancellationToken: cancellationToken);
        return new CoreHealthDto
        {
            Status = response.Status,
            StartedAtUnixSeconds = response.StartedAtUnixSeconds,
        };
    }
}
