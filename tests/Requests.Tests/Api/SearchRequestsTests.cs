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

    [Theory]
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
