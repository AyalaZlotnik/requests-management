using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Requests.Application.Requests.Entities;
using Requests.Application.Common;
using Requests.Application.Requests.Contracts;
using Requests.Tests.Support;
using static Requests.Tests.Support.RequestsApiFactory;

namespace Requests.Tests.Api;

public class BulkUpdateStatusTests(RequestsApiFactory factory) : IClassFixture<RequestsApiFactory>
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

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<BulkUpdateStatusResponse>(Json))!;
        result.Should().BeEquivalentTo(new { Requested = 5, Succeeded = 2, Failed = 3 });
        result.Results.Select(r => r.Outcome).Should().Equal(
            BulkItemOutcome.Updated, BulkItemOutcome.Updated, BulkItemOutcome.Conflict, BulkItemOutcome.InvalidTransition, BulkItemOutcome.NotFound);
        result.Results.Where(r => r.Outcome == BulkItemOutcome.Updated).Should().OnlyContain(r => r.RowVersion != null);

        // Only the two valid items changed, and each has exactly one bulk audit row.
        var bulkAudit = await factory.QueryDbAsync(db => db.StatusHistory.Where(h => h.ChangedBy == "bulk-user").Select(h => h.RequestId).ToListAsync());
        bulkAudit.Should().BeEquivalentTo([seeded[0].Id, seeded[1].Id]);
        var stored = await factory.QueryDbAsync(db => db.Requests.ToDictionaryAsync(r => r.Id, r => r.Status));
        stored[stale].Should().Be(RequestStatus.Waiting);
        stored[seeded[3].Id].Should().Be(RequestStatus.InProgress);
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

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private Task<Dictionary<int, string>> CurrentVersions() =>
        factory.QueryDbAsync(db => db.Requests.ToDictionaryAsync(r => r.Id, r => Convert.ToBase64String(r.RowVersion)));
}
