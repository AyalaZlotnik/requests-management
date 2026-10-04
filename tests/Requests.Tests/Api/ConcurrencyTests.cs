using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;
using Requests.Tests.Support;
using Xunit.Abstractions;
using static Requests.Tests.Support.RequestsApiFactory;

namespace Requests.Tests.Api;

/// <summary>
/// Real races against SQL Server: requests are sent at the same moment and the database decides.
/// Every scenario checks the invariant that matters – each request has exactly one winning change,
/// the stored status is the winner's, and there is exactly one audit row per change (no lost updates).
/// </summary>
public class ConcurrencyTests(RequestsApiFactory factory, ITestOutputHelper output) : IClassFixture<RequestsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Bulk_update_racing_single_updates_on_the_same_requests_has_exactly_one_winner_per_request()
    {
        var seeded = await factory.ResetAndSeedAsync(Enumerable.Range(1, 20).Select(i => NewRequest($"פנייה {i}")).ToArray());
        var versions = await CurrentVersions();

        // Everyone read the same versions; the bulk sets InProgress, each single update sets Waiting.
        var bulkTask = _client.PostAsJsonAsync("/api/requests/bulk/status", BulkCommand(RequestStatus.InProgress, "bulk", seeded.Select(r => r.Id), versions), Json);
        var singleTasks = seeded.Select(r => _client.PatchStatusAsync(r.Id, RequestStatus.Waiting, $"\"{versions[r.Id]}\"", $"single-{r.Id}")).ToList();
        await Task.WhenAll(singleTasks.Append(bulkTask));

        var bulk = (await (await bulkTask).Content.ReadFromJsonAsync<BulkUpdateStatusResponse>(Json))!;
        var singles = seeded.Zip(singleTasks, (r, t) => (r.Id, t.Result.StatusCode)).ToDictionary(x => x.Id, x => x.StatusCode);
        var stored = await StoredStatuses();
        var audit = await AuditRows();

        // How the race actually went (visible with: dotnet test --logger "console;verbosity=detailed").
        output.WriteLine($"bulk won {bulk.Succeeded}, single updates won {singles.Values.Count(s => s == HttpStatusCode.OK)} of {seeded.Count}");

        foreach (var request in seeded)
        {
            var bulkWon = bulk.Results.Single(x => x.Id == request.Id).Outcome == BulkItemOutcome.Updated;
            var singleWon = singles[request.Id] == HttpStatusCode.OK;

            (bulkWon ^ singleWon).Should().BeTrue($"request {request.Id} must have exactly one winner");
            if (!singleWon) singles[request.Id].Should().Be(HttpStatusCode.Conflict);
            stored[request.Id].Should().Be(bulkWon ? RequestStatus.InProgress : RequestStatus.Waiting);
            audit.Where(a => a.RequestId == request.Id).Should().ContainSingle()
                .Which.ChangedBy.Should().Be(bulkWon ? "bulk" : $"single-{request.Id}");
        }
    }

    [Fact]
    public async Task Two_overlapping_bulk_updates_change_each_request_exactly_once()
    {
        var seeded = await factory.ResetAndSeedAsync(Enumerable.Range(1, 30).Select(i => NewRequest($"פנייה {i}")).ToArray());
        var ids = seeded.Select(r => r.Id).ToList();
        var versions = await CurrentVersions();
        var onlyA = ids.Take(10).ToList();
        var both = ids.Skip(10).Take(10).ToList();
        var onlyB = ids.Skip(20).ToList();

        var taskA = _client.PostAsJsonAsync("/api/requests/bulk/status", BulkCommand(RequestStatus.InProgress, "bulk-A", onlyA.Concat(both), versions), Json);
        var taskB = _client.PostAsJsonAsync("/api/requests/bulk/status", BulkCommand(RequestStatus.Waiting, "bulk-B", both.Concat(onlyB), versions), Json);
        await Task.WhenAll(taskA, taskB);

        (await taskA).StatusCode.Should().Be(HttpStatusCode.OK);
        (await taskB).StatusCode.Should().Be(HttpStatusCode.OK);
        var a = (await (await taskA).Content.ReadFromJsonAsync<BulkUpdateStatusResponse>(Json))!.Results.ToDictionary(x => x.Id, x => x.Outcome);
        var b = (await (await taskB).Content.ReadFromJsonAsync<BulkUpdateStatusResponse>(Json))!.Results.ToDictionary(x => x.Id, x => x.Outcome);
        var stored = await StoredStatuses();
        var audit = await AuditRows();

        onlyA.Should().OnlyContain(id => a[id] == BulkItemOutcome.Updated && stored[id] == RequestStatus.InProgress);
        onlyB.Should().OnlyContain(id => b[id] == BulkItemOutcome.Updated && stored[id] == RequestStatus.Waiting);
        output.WriteLine($"overlapping requests: bulk A won {both.Count(id => a[id] == BulkItemOutcome.Updated)}, bulk B won {both.Count(id => b[id] == BulkItemOutcome.Updated)}");
        foreach (var id in both)
        {
            var aWon = a[id] == BulkItemOutcome.Updated;
            var bWon = b[id] == BulkItemOutcome.Updated;
            (aWon ^ bWon).Should().BeTrue($"request {id} must be updated by exactly one of the bulks");
            (aWon ? b[id] : a[id]).Should().Be(BulkItemOutcome.Conflict);
            stored[id].Should().Be(aWon ? RequestStatus.InProgress : RequestStatus.Waiting);
        }

        // One audit row per request – none lost, none doubled.
        audit.GroupBy(x => x.RequestId).Should().HaveCount(30).And.OnlyContain(g => g.Count() == 1);
    }

    [Fact]
    public async Task Parallel_updates_of_different_requests_never_conflict()
    {
        var seeded = await factory.ResetAndSeedAsync(Enumerable.Range(1, 50).Select(i => NewRequest($"פנייה {i}")).ToArray());
        var versions = await CurrentVersions();

        var responses = await Task.WhenAll(seeded.Select(r =>
            _client.PatchStatusAsync(r.Id, RequestStatus.InProgress, $"\"{versions[r.Id]}\"", $"user-{r.Id}")));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        (await StoredStatuses()).Values.Should().OnlyContain(s => s == RequestStatus.InProgress);
        (await AuditRows()).Should().HaveCount(50);
    }

    private static BulkUpdateStatusRequest BulkCommand(RequestStatus status, string changedBy, IEnumerable<int> ids, Dictionary<int, string> versions) => new()
    {
        Status = status,
        ChangedBy = changedBy,
        Items = ids.Select(id => new BulkStatusItem { Id = id, RowVersion = versions[id] }).ToList()
    };

    private Task<Dictionary<int, string>> CurrentVersions() =>
        factory.QueryDbAsync(db => db.Requests.ToDictionaryAsync(r => r.Id, r => Convert.ToBase64String(r.RowVersion)));

    private Task<Dictionary<int, RequestStatus>> StoredStatuses() =>
        factory.QueryDbAsync(db => db.Requests.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Status));

    private Task<List<RequestStatusHistory>> AuditRows() =>
        factory.QueryDbAsync(db => db.StatusHistory.AsNoTracking().ToListAsync());
}
