using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Requests.Application.Requests.Entities;
using Requests.Application.Common;
using Requests.Application.Requests.Contracts;
using Requests.Tests.Support;
using static Requests.Tests.Support.RequestsApiFactory;

namespace Requests.Tests.Api;

public class SearchRequestsTests(RequestsApiFactory factory) : IClassFixture<RequestsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Search_combines_filters_sorts_and_pages_on_the_server()
    {
        var day = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        await factory.ResetAndSeedAsync(
            // Matching: New + High + "boiler" in title or organization, created in March.
            NewRequest("Boiler inspection A", status: RequestStatus.New, priority: RequestPriority.High, createdAt: day.AddDays(1)),
            NewRequest("Boiler inspection B", status: RequestStatus.New, priority: RequestPriority.High, createdAt: day.AddDays(2)),
            NewRequest("Annual report", organization: "Boiler Works Ltd", status: RequestStatus.New, priority: RequestPriority.High, createdAt: day.AddDays(3)),
            // Not matching – each fails exactly one condition.
            NewRequest("Boiler inspection C", status: RequestStatus.Waiting, priority: RequestPriority.High, createdAt: day.AddDays(4)),
            NewRequest("Boiler inspection D", status: RequestStatus.New, priority: RequestPriority.Low, createdAt: day.AddDays(5)),
            NewRequest("Payment issue", status: RequestStatus.New, priority: RequestPriority.High, createdAt: day.AddDays(6)),
            NewRequest("Boiler inspection E", status: RequestStatus.New, priority: RequestPriority.High, createdAt: day.AddMonths(2)));

        var page1 = await GetPage("search=boiler&status=New&priority=High&createdFrom=2026-03-01&createdTo=2026-03-31&sortBy=createdAt&sortDirection=desc&pageSize=2&page=1");
        var page2 = await GetPage("search=boiler&status=New&priority=High&createdFrom=2026-03-01&createdTo=2026-03-31&sortBy=createdAt&sortDirection=desc&pageSize=2&page=2");

        page1.TotalCount.Should().Be(3);
        page1.TotalPages.Should().Be(2);
        page1.Items.Select(i => i.Title).Should().Equal("Annual report", "Boiler inspection B");
        page2.Items.Select(i => i.Title).Should().Equal("Boiler inspection A");
        page1.Items.Should().OnlyContain(i => !string.IsNullOrEmpty(i.RowVersion));
    }

    [Fact]
    public async Task Search_filters_by_assignee_and_organization_prefix()
    {
        await factory.ResetAndSeedAsync(
            NewRequest("A", organization: "Negev Water Co", assignedTo: "agent07"),
            NewRequest("B", organization: "Negev Foods Ltd", assignedTo: "agent07"),
            NewRequest("C", organization: "Negev Water Co", assignedTo: "agent08"),
            NewRequest("D", organization: "Central Negev Ltd", assignedTo: "agent07"));

        var result = await GetPage("organizationName=Negev&assignedTo=agent07&sortBy=title&sortDirection=asc");

        result.Items.Select(i => i.Title).Should().Equal("A", "B");
    }

    [Fact]
    public async Task Paging_over_equal_sort_values_returns_every_row_exactly_once()
    {
        // 25 requests with the same CreatedAt: without a tie-breaker the order between pages is undefined.
        var sameTime = new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc);
        var seeded = await factory.ResetAndSeedAsync(
            Enumerable.Range(1, 25).Select(i => NewRequest($"פנייה {i}", createdAt: sameTime)).ToArray());

        var seen = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            seen.AddRange((await GetPage($"sortBy=createdAt&sortDirection=desc&pageSize=10&page={page}")).Items.Select(i => i.Id));
        }

        seen.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(seeded.Select(r => r.Id));
    }

    [Fact]
    public async Task Sorting_is_done_on_the_whole_result_not_only_within_the_page()
    {
        await factory.ResetAndSeedAsync(
            NewRequest("ג", createdAt: new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)),
            NewRequest("א", createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            NewRequest("ב", createdAt: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));

        var firstPage = await GetPage("sortBy=title&sortDirection=asc&pageSize=2&page=1");

        firstPage.Items.Select(i => i.Title).Should().Equal("א", "ב");
    }

    [Fact]
    public async Task Page_past_the_end_returns_an_empty_page_with_the_real_total()
    {
        await factory.ResetAndSeedAsync(NewRequest(), NewRequest());

        var result = await GetPage("pageSize=10&page=50");

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(2);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("[")]
    public async Task Search_treats_like_wildcards_as_plain_characters(string term)
    {
        // Each special character appears in exactly one title; unescaped, %, _ and [ would match more (or fail).
        await factory.ResetAndSeedAsync(
            NewRequest("הנחה של 50% בתשלום"), NewRequest("שם_קובץ"), NewRequest("סעיף [3]"), NewRequest("רגיל"));

        var result = await GetPage($"search={Uri.EscapeDataString(term)}");

        result.Items.Should().ContainSingle().Which.Title.Should().Contain(term);
    }

    [Theory]
    [InlineData("page=2147483647", "Page")]
    [InlineData("pageSize=0", "PageSize")]
    [InlineData("pageSize=101", "PageSize")]
    [InlineData("page=0", "Page")]
    [InlineData("status=99", "Status")]
    [InlineData("status=Closed", "Status")]
    [InlineData("sortBy=Password", "SortBy")]
    [InlineData("createdFrom=2026-02-01&createdTo=2026-01-01", "CreatedFrom")]
    public async Task Search_with_invalid_parameters_returns_400_with_field_error(string query, string field)
    {
        var response = await _client.GetAsync($"/api/requests?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json);
        problem!.Errors.Should().ContainKey(field);
    }

    private async Task<PagedResult<RequestListItemDto>> GetPage(string query)
    {
        var response = await _client.GetAsync($"/api/requests?{query}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PagedResult<RequestListItemDto>>(Json))!;
    }
}
