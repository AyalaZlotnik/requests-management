using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;
using Requests.Tests.Support;
using static Requests.Tests.Support.RequestsApiFactory;

namespace Requests.Tests.Api;

public class HistoryTests(RequestsApiFactory factory) : IClassFixture<RequestsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task History_lists_every_status_change_newest_first()
    {
        var request = (await factory.ResetAndSeedAsync(NewRequest(status: RequestStatus.New)))[0];

        await ChangeStatus(request.Id, RequestStatus.InProgress, "דנה לוי");
        await ChangeStatus(request.Id, RequestStatus.Waiting, "יוסי כהן");
        await ChangeStatus(request.Id, RequestStatus.Completed, "מיכל פרץ");

        var history = await _client.GetFromJsonAsync<List<StatusHistoryDto>>($"/api/requests/{request.Id}/history", Json);

        history!.Select(h => (h.PreviousStatus, h.NewStatus, h.ChangedBy)).Should().Equal(
            (RequestStatus.Waiting, RequestStatus.Completed, "מיכל פרץ"),
            (RequestStatus.InProgress, RequestStatus.Waiting, "יוסי כהן"),
            (RequestStatus.New, RequestStatus.InProgress, "דנה לוי"));
    }

    [Fact]
    public async Task Request_without_changes_has_an_empty_history()
    {
        var request = (await factory.ResetAndSeedAsync(NewRequest()))[0];

        var history = await _client.GetFromJsonAsync<List<StatusHistoryDto>>($"/api/requests/{request.Id}/history", Json);

        history.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/api/requests/987654")]
    [InlineData("/api/requests/987654/history")]
    public async Task Unknown_request_returns_404_problem_details(string url)
    {
        await factory.ResetAndSeedAsync();

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Status.Should().Be(404);
    }

    private async Task ChangeStatus(int id, RequestStatus status, string changedBy)
    {
        var (_, etag) = await _client.GetRequestAsync(id);
        (await _client.PatchStatusAsync(id, status, etag, changedBy)).EnsureSuccessStatusCode();
    }
}
