using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;
using Requests.Tests.Support;
using static Requests.Tests.Support.RequestsApiFactory;

namespace Requests.Tests.Api;

/// <summary>
/// The hardest case, made deterministic with a <see cref="SaveGate"/>: the version was current when the
/// update read it, another user committed before it saved, and only SQL Server's
/// "UPDATE … WHERE RowVersion = @expected" can notice. These tests prove that check – not just the early
/// comparison in the service – prevents lost updates.
/// </summary>
public class DatabaseConflictTests(GatedApiFactory factory) : IClassFixture<GatedApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Single_update_whose_row_changes_between_read_and_save_gets_409_from_the_database()
    {
        var request = (await factory.ResetAndSeedAsync(NewRequest(status: RequestStatus.New)))[0];
        var (_, etag) = await _client.GetRequestAsync(request.Id);

        // A reads the current version and stops right before saving.
        factory.Gate.Arm();
        var first = _client.PatchStatusAsync(request.Id, RequestStatus.InProgress, etag, "first");
        await factory.Gate.Reached;

        // B, with the same (still current) version, commits.
        var second = await _client.PatchStatusAsync(request.Id, RequestStatus.Waiting, etag, "second");
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        // A saves now: its version was current when it read it, so only the database can reject it.
        factory.Gate.Release();
        (await first).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var stored = await factory.QueryDbAsync(db => db.Requests.AsNoTracking().SingleAsync(r => r.Id == request.Id));
        stored.Status.Should().Be(RequestStatus.Waiting);
        var audit = await factory.QueryDbAsync(db => db.StatusHistory.AsNoTracking().Where(h => h.RequestId == request.Id).ToListAsync());
        audit.Should().ContainSingle().Which.ChangedBy.Should().Be("second");
    }

    [Fact]
    public async Task Bulk_update_whose_rows_change_between_read_and_save_saves_only_the_unchanged_rows()
    {
        var seeded = await factory.ResetAndSeedAsync(Enumerable.Range(1, 10).Select(i => NewRequest($"פנייה {i}")).ToArray());
        var versions = await factory.QueryDbAsync(db => db.Requests.ToDictionaryAsync(r => r.Id, r => Convert.ToBase64String(r.RowVersion)));
        var changedMeanwhile = seeded.Take(5).Select(r => r.Id).ToHashSet();

        // The bulk reads all 10 (all versions current) and stops right before saving.
        factory.Gate.Arm();
        var bulkTask = _client.PostAsJsonAsync("/api/requests/bulk/status", new BulkUpdateStatusRequest
        {
            Status = RequestStatus.InProgress,
            ChangedBy = "bulk",
            Items = seeded.Select(r => new BulkStatusItem { Id = r.Id, RowVersion = versions[r.Id] }).ToList()
        }, Json);
        await factory.Gate.Reached;

        // Meanwhile 5 of them are changed by single updates and committed.
        foreach (var id in changedMeanwhile)
        {
            (await _client.PatchStatusAsync(id, RequestStatus.Waiting, $"\"{versions[id]}\"", "single")).EnsureSuccessStatusCode();
        }

        factory.Gate.Release();
        var bulk = (await (await bulkTask).Content.ReadFromJsonAsync<BulkUpdateStatusResponse>(Json))!;

        // The database rejected the batch because of the 5 changed rows; only those were dropped and the rest saved.
        bulk.Results.Where(r => changedMeanwhile.Contains(r.Id)).Should().OnlyContain(r => r.Outcome == BulkItemOutcome.Conflict);
        bulk.Results.Where(r => !changedMeanwhile.Contains(r.Id)).Should().OnlyContain(r => r.Outcome == BulkItemOutcome.Updated);

        var stored = await factory.QueryDbAsync(db => db.Requests.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Status));
        var audit = await factory.QueryDbAsync(db => db.StatusHistory.AsNoTracking().ToListAsync());
        foreach (var request in seeded)
        {
            var winner = changedMeanwhile.Contains(request.Id) ? "single" : "bulk";
            stored[request.Id].Should().Be(winner == "single" ? RequestStatus.Waiting : RequestStatus.InProgress);
            audit.Where(a => a.RequestId == request.Id).Should().ContainSingle().Which.ChangedBy.Should().Be(winner);
        }
    }
}
