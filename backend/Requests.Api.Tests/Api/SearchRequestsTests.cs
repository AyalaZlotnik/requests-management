using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Requests.Api.Domain;
using Requests.Api.Features.Requests.Dtos;
using Requests.Api.Tests.Infrastructure;
using static Requests.Api.Tests.Infrastructure.RequestsApiFactory;

namespace Requests.Api.Tests.Api;

[Collection(ApiCollection.Name)]
public class SearchRequestsTests(RequestsApiFactory factory)
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

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(["Annual report", "Boiler inspection B"], page1.Items.Select(i => i.Title));
        Assert.Equal(["Boiler inspection A"], page2.Items.Select(i => i.Title));
        Assert.All(page1.Items, i => Assert.False(string.IsNullOrEmpty(i.RowVersion)));
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

        Assert.Equal(["A", "B"], result.Items.Select(i => i.Title));
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

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json);
        Assert.Contains(field, problem!.Errors.Keys);
    }

    private async Task<PagedResult<RequestListItemDto>> GetPage(string query)
    {
        var response = await _client.GetAsync($"/api/requests?{query}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PagedResult<RequestListItemDto>>(Json))!;
    }
}
