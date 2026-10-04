using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;
using Requests.Infrastructure.Caching;
using Requests.Tests.Support;
using static Requests.Tests.Support.RequestsApiFactory;

namespace Requests.Tests.Api;

public class SummaryCacheTests(RequestsApiFactory factory) : IClassFixture<RequestsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Unfiltered_summary_is_served_from_the_cache_while_filtered_summaries_are_computed_each_time()
    {
        await factory.ResetAndSeedAsync(NewRequest(status: RequestStatus.New));
        (await GetSummary("")).Total.Should().Be(1);

        // A row added behind the API's back (no invalidation): the cached view cannot know about it.
        await factory.QueryDbAsync(async db =>
        {
            db.Requests.Add(NewRequest(status: RequestStatus.New));
            return await db.SaveChangesAsync();
        });

        (await GetSummary("")).Total.Should().Be(1, "the unfiltered view comes from the cache");
        (await GetSummary("status=New")).Total.Should().Be(2, "filtered views are never cached");
    }

    [Fact]
    public async Task Bulk_update_invalidates_the_cached_summary()
    {
        var seeded = await factory.ResetAndSeedAsync(NewRequest(status: RequestStatus.New), NewRequest(status: RequestStatus.New));
        (await GetSummary("")).ByStatus.Single(c => c.Key == RequestStatus.New).Count.Should().Be(2);

        var items = new List<BulkStatusItem>();
        foreach (var request in seeded)
        {
            var (body, _) = await _client.GetRequestAsync(request.Id);
            items.Add(new BulkStatusItem { Id = request.Id, RowVersion = body.RowVersion });
        }

        (await _client.PostAsJsonAsync("/api/requests/bulk/status",
            new BulkUpdateStatusRequest { Status = RequestStatus.InProgress, ChangedBy = "דנה לוי", Items = items }, Json))
            .EnsureSuccessStatusCode();

        var after = await GetSummary("");
        after.ByStatus.Single(c => c.Key == RequestStatus.New).Count.Should().Be(0);
        after.ByStatus.Single(c => c.Key == RequestStatus.InProgress).Count.Should().Be(2);
    }

    [Fact]
    public async Task Value_computed_while_an_update_invalidates_the_cache_is_not_kept()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        using var cache = new MemorySummaryCache(memory, Options.Create(new CacheOptions()), NullLogger<MemorySummaryCache>.Instance);
        var computations = 0;

        // The first computation is interrupted by an update that commits while it is still running.
        await cache.GetOrCreateAsync(_ =>
        {
            computations++;
            cache.Invalidate();
            return Task.FromResult(Summary(total: 1));
        }, CancellationToken.None);

        var second = await cache.GetOrCreateAsync(_ =>
        {
            computations++;
            return Task.FromResult(Summary(total: 2));
        }, CancellationToken.None);

        computations.Should().Be(2, "the stale value must not have been cached");
        second.Total.Should().Be(2);
    }

    private async Task<RequestsSummaryDto> GetSummary(string query) =>
        (await _client.GetFromJsonAsync<RequestsSummaryDto>($"/api/requests/summary?{query}", Json))!;

    private static RequestsSummaryDto Summary(int total) =>
        new(total, [], [], 0, null, [], DateTime.UtcNow);
}
