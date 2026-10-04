using Requests.Application.Requests.Contracts;

namespace Requests.Application.Requests.Abstractions;

/// <summary>Cache for the summary aggregations, implemented in Infrastructure.</summary>
public interface ISummaryCache
{
    Task<RequestsSummaryDto> GetOrCreateAsync(Func<CancellationToken, Task<RequestsSummaryDto>> factory, CancellationToken ct);

    /// <summary>Called after every committed status change so the next read recomputes the summary.</summary>
    void Invalidate();
}
