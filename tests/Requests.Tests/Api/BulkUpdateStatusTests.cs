using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Requests.Api.Domain;
using Requests.Api.Features.Requests.Dtos;
using Requests.Tests.Infrastructure;
using static Requests.Tests.Infrastructure.RequestsApiFactory;

namespace Requests.Tests.Api;

[Collection(ApiTestsDefinition.Name)]
public class BulkUpdateStatusTests(RequestsApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Bulk_update_is_partial_success_with_an_outcome_per_item()
    {
        var seeded = await factory.ResetAndSeedAsync(
            NewRequest("valid 1", status: RequestStatus.New),
            NewRequest("valid 2", status: RequestStatus.Waiting),
            NewRequest("stale", status: RequestStatus.New),
            NewRequest("invalid transition", status: RequestStatus.InProgress));
        var versions = await CurrentVersions();

        // Another user changes "stale" after we read it.
        var stale = seeded[2].Id;
        (await _client.PatchAsJsonAsync($"/api/requests/{stale}/status",
            new UpdateStatusRequest { Status = RequestStatus.Waiting, RowVersion = versions[stale], ChangedBy = "other" }, Json))
            .EnsureSuccessStatusCode();

        var command = new BulkUpdateStatusRequest
        {
            Status = RequestStatus.InProgress,
            ChangedBy = "bulk-user",
            Items =
            [
                .. seeded.Select(r => new BulkStatusItem { Id = r.Id, RowVersion = versions[r.Id] }),
                new BulkStatusItem { Id = 999_999, RowVersion = "AAAAAAAAB9E=" }
            ]
        };

        var response = await _client.PostAsJsonAsync("/api/requests/bulk/status", command, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BulkUpdateStatusResponse>(Json))!;
        Assert.Equal(5, result.Requested);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(3, result.Failed);
        Assert.Equal(
            [BulkItemOutcome.Updated, BulkItemOutcome.Updated, BulkItemOutcome.Conflict, BulkItemOutcome.InvalidTransition, BulkItemOutcome.NotFound],
            result.Results.Select(r => r.Outcome));
        Assert.All(result.Results.Where(r => r.Outcome == BulkItemOutcome.Updated), r => Assert.NotNull(r.RowVersion));

        // Only the two valid items changed, and each has exactly one bulk audit row.
        var bulkAudit = await factory.QueryDbAsync(db => db.StatusHistory.Where(h => h.ChangedBy == "bulk-user").Select(h => h.RequestId).ToListAsync());
        Assert.Equal([seeded[0].Id, seeded[1].Id], bulkAudit.Order());
        var stored = await factory.QueryDbAsync(db => db.Requests.ToDictionaryAsync(r => r.Id, r => r.Status));
        Assert.Equal(RequestStatus.Waiting, stored[stale]);
        Assert.Equal(RequestStatus.InProgress, stored[seeded[3].Id]);
    }

    [Fact]
    public async Task Bulk_update_with_more_than_100_items_returns_400()
    {
        var command = new BulkUpdateStatusRequest
        {
            Status = RequestStatus.InProgress,
            ChangedBy = "bulk-user",
            Items = Enumerable.Range(1, 101).Select(i => new BulkStatusItem { Id = i, RowVersion = "AAAAAAAAB9E=" }).ToList()
        };

        var response = await _client.PostAsJsonAsync("/api/requests/bulk/status", command, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_update_with_duplicate_ids_returns_400()
    {
        var command = new BulkUpdateStatusRequest
        {
            Status = RequestStatus.InProgress,
            ChangedBy = "bulk-user",
            Items = [new() { Id = 1, RowVersion = "AAAAAAAAB9E=" }, new() { Id = 1, RowVersion = "AAAAAAAAB9E=" }]
        };

        var response = await _client.PostAsJsonAsync("/api/requests/bulk/status", command, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<Dictionary<int, string>> CurrentVersions() =>
        factory.QueryDbAsync(db => db.Requests.ToDictionaryAsync(r => r.Id, r => Convert.ToBase64String(r.RowVersion)));
}
