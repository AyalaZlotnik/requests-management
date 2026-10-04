using Requests.Application.Common;
using Requests.Application.Requests.Abstractions;
using Requests.Application.Requests.Contracts;

namespace Requests.Application.Requests.Services;

public interface IRequestQueryService
{
    Task<PagedResult<RequestListItemDto>> SearchAsync(RequestSearchQuery query, CancellationToken ct);
    Task<RequestDetailsDto> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<StatusHistoryDto>> GetHistoryAsync(int id, CancellationToken ct);
}

/// <summary>Read side: delegates the queries to the repository and turns "not found" into an error.</summary>
public class RequestQueryService(IRequestRepository repository) : IRequestQueryService
{
    public Task<PagedResult<RequestListItemDto>> SearchAsync(RequestSearchQuery query, CancellationToken ct) =>
        repository.SearchAsync(query, ct);

    public async Task<RequestDetailsDto> GetByIdAsync(int id, CancellationToken ct)
    {
        var request = await repository.FindAsync(id, ct) ?? throw new NotFoundException("Request", id);
        return request.ToDetails();
    }

    public async Task<IReadOnlyList<StatusHistoryDto>> GetHistoryAsync(int id, CancellationToken ct)
    {
        if (!await repository.ExistsAsync(id, ct))
        {
            throw new NotFoundException("Request", id);
        }

        return await repository.GetHistoryAsync(id, ct);
    }
}
