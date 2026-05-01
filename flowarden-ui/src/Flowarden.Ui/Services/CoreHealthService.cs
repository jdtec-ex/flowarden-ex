using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Flowarden.Ui.Models;

namespace Flowarden.Ui.Services;

public sealed class CoreHealthService
{
    private readonly HttpClient _httpClient;

    public CoreHealthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CoreHealthDto?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<CoreHealthDto>("/health", cancellationToken);
    }
}
