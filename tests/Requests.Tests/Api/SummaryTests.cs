using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Requests.Application.Common;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;
using Requests.Tests.Support;
using static Requests.Tests.Support.RequestsApiFactory;

namespace Requests.Tests.Api;

public class SummaryTests(RequestsApiFactory factory) : IClassFixture<RequestsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Status_breakdown_ignores_the_status_filter_but_honours_the_others()
    {
        await factory.ResetAndSeedAsync(
            NewRequest(status: RequestStatus.New, priority: RequestPriority.High),
            NewRequest(status: RequestStatus.New, priority: RequestPriority.High),
            NewRequest(status: RequestStatus.InProgress, priority: RequestPriority.High),
            NewRequest(status: RequestStatus.Waiting, priority: RequestPriority.Low));

        var summary = await GetSummary("status=New&priority=High");

        // Status facet: all statuses within priority High.
        Count(summary.ByStatus, RequestStatus.New).Should().Be(2);
        Count(summary.ByStatus, RequestStatus.InProgress).Should().Be(1);
        Count(summary.ByStatus, RequestStatus.Waiting).Should().Be(0);
        // Priority facet: all priorities within status New.
        Count(summary.ByPriority, RequestPriority.High).Should().Be(2);
        Count(summary.ByPriority, RequestPriority.Low).Should().Be(0);
        // Total honours both filters.
        summary.Total.Should().Be(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("status=New&status=Waiting")]
    [InlineData("priority=High&search=%D7%94%D7%99%D7%AA%D7%A8")]
    [InlineData("assignedTo=%D7%93%D7%A0%D7%94%20%D7%9C%D7%95%D7%99&createdFrom=2026-01-01&createdTo=2026-01-31")]
    public async Task Summary_total_matches_the_list_total_for_the_same_filters(string query)
    {
        await factory.ResetAndSeedAsync(
            NewRequest("חידוש היתר", status: RequestStatus.New, priority: RequestPriority.High),
            NewRequest("דיווח פליטות", status: RequestStatus.Waiting, priority: RequestPriority.High),
            NewRequest("חידוש היתר", status: RequestStatus.Completed, priority: RequestPriority.Low, assignedTo: "יוסי כהן"),
            NewRequest("תלונה", status: RequestStatus.InProgress, priority: RequestPriority.Medium,
                createdAt: new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc)));

        var summary = await GetSummary(query);
        var list = await _client.GetFromJsonAsync<PagedResult<RequestListItemDto>>($"/api/requests?{query}", Json);

        summary.Total.Should().Be(list!.TotalCount);
    }

    [Fact]
    public async Task Open_older_than_7_days_and_last_update_are_computed_for_the_filter()
    {
        var now = DateTime.UtcNow;
        await factory.ResetAndSeedAsync(
            NewRequest(status: RequestStatus.New, createdAt: now.AddDays(-30)),
            NewRequest(status: RequestStatus.Waiting, createdAt: now.AddDays(-10)),
            NewRequest(status: RequestStatus.Completed, createdAt: now.AddDays(-30)),
            NewRequest(status: RequestStatus.New, createdAt: now.AddDays(-1)));

        var summary = await GetSummary("");

        summary.OpenOlderThan7Days.Should().Be(2);
        summary.LastUpdatedAt.Should().BeCloseTo(now.AddDays(-1), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Top_assignees_count_only_open_requests_matching_the_filter()
    {
        await factory.ResetAndSeedAsync(
            NewRequest(assignedTo: "דנה לוי", priority: RequestPriority.High),
            NewRequest(assignedTo: "דנה לוי", priority: RequestPriority.High),
            NewRequest(assignedTo: "דנה לוי", priority: RequestPriority.Low),
            NewRequest(assignedTo: "יוסי כהן", priority: RequestPriority.High),
            NewRequest(assignedTo: "יוסי כהן", priority: RequestPriority.High, status: RequestStatus.Completed),
            NewRequest(assignedTo: null, priority: RequestPriority.High));

        var summary = await GetSummary("priority=High");

        summary.TopAssignees.Should().Equal(
            new CountByKey<string>("דנה לוי", 2),
            new CountByKey<string>("יוסי כהן", 1));
    }

    [Fact]
    public async Task When_nothing_matches_every_category_is_reported_as_zero()
    {
        await factory.ResetAndSeedAsync(NewRequest());

        var summary = await GetSummary("search=nothing-matches-this");

        summary.Total.Should().Be(0);
        summary.ByStatus.Should().HaveCount(4).And.OnlyContain(c => c.Count == 0);
        summary.ByPriority.Should().HaveCount(3).And.OnlyContain(c => c.Count == 0);
        summary.LastUpdatedAt.Should().BeNull();
        summary.TopAssignees.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_filter_returns_400()
    {
        var response = await _client.GetAsync("/api/requests/summary?status=99");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<RequestsSummaryDto> GetSummary(string query) =>
        (await _client.GetFromJsonAsync<RequestsSummaryDto>($"/api/requests/summary?{query}", Json))!;

    private static int Count<T>(IEnumerable<CountByKey<T>> counts, T key) =>
        counts.Single(c => EqualityComparer<T>.Default.Equals(c.Key, key)).Count;
}
