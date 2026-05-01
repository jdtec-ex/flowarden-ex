using System.Threading;
using System.Threading.Tasks;
using Flowarden.Ui.Models;

namespace Flowarden.Ui.Services;

public sealed class ProjectionClient
{
    public Task<OverviewSnapshotDto> GetPlaceholderOverviewAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OverviewSnapshotDto());
    }

    public Task<InspectResultDto> GetPlaceholderInspectResultAsync(
        InspectFilterDto filter,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = filter;
        return Task.FromResult(new InspectResultDto());
    }
}
