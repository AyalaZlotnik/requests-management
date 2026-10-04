using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Requests.Api.Domain;
using Requests.Api.Features.Requests.Dtos;
using Requests.Api.Tests.Infrastructure;
using static Requests.Api.Tests.Infrastructure.RequestsApiFactory;

namespace Requests.Api.Tests.Api;

[Collection(ApiCollection.Name)]
public class UpdateStatusTests(RequestsApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Valid_update_changes_status_version_and_timestamp_and_writes_audit()
    {
        var request = await SeedOne(RequestStatus.New);
        var before = await Get(request.Id);

        var response = await Patch(request.Id, RequestStatus.InProgress, before.RowVersion, "dana");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = (await response.Content.ReadFromJsonAsync<RequestDetailsDto>(Json))!;
        Assert.Equal(RequestStatus.InProgress, after.Status);
        Assert.NotEqual(before.RowVersion, after.RowVersion);
        Assert.True(after.UpdatedAt > before.UpdatedAt);

        var history = await _client.GetFromJsonAsync<List<StatusHistoryDto>>($"/api/requests/{request.Id}/history", Json);
        var entry = Assert.Single(history!);
        Assert.Equal(RequestStatus.New, entry.PreviousStatus);
        Assert.Equal(RequestStatus.InProgress, entry.NewStatus);
        Assert.Equal("dana", entry.ChangedBy);
        Assert.Equal(after.UpdatedAt, entry.ChangedAt);
    }

    [Fact]
    public async Task Concurrent_updates_with_the_same_version_only_one_succeeds()
    {
        var request = await SeedOne(RequestStatus.New);
        var version = (await Get(request.Id)).RowVersion;

        // Five users read the same version and submit at the same time.
        var responses = await Task.WhenAll(Enumerable.Range(1, 5).Select(i =>
            Patch(request.Id, i % 2 == 0 ? RequestStatus.Waiting : RequestStatus.InProgress, version, $"user{i}")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        // No lost update: exactly one audit row, and it matches the stored status.
        var stored = await Get(request.Id);
        var history = await factory.QueryDbAsync(db => db.StatusHistory.Where(h => h.RequestId == request.Id).ToListAsync());
        Assert.Equal(stored.Status, Assert.Single(history).NewStatus);
    }

    [Fact]
    public async Task Update_with_stale_version_returns_409()
    {
        var request = await SeedOne(RequestStatus.New);
        var staleVersion = (await Get(request.Id)).RowVersion;
        (await Patch(request.Id, RequestStatus.InProgress, staleVersion, "first")).EnsureSuccessStatusCode();

        var response = await Patch(request.Id, RequestStatus.Waiting, staleVersion, "second");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RequestStatus.InProgress, (await Get(request.Id)).Status);
    }

    [Fact]
    public async Task Disallowed_transition_returns_422_with_allowed_statuses()
    {
        var request = await SeedOne(RequestStatus.New);
        var version = (await Get(request.Id)).RowVersion;

        var response = await Patch(request.Id, RequestStatus.Completed, version, "dana");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json);
        Assert.Equal("Invalid status transition", problem!.Title);
        Assert.Equal(RequestStatus.New, (await Get(request.Id)).Status);
    }

    [Fact]
    public async Task Unknown_request_returns_404()
    {
        await factory.ResetAndSeedAsync();

        var response = await Patch(424242, RequestStatus.InProgress, "AAAAAAAAB9E=", "dana");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"status":"Closed","rowVersion":"AAAAAAAAB9E=","changedBy":"x"}""")]
    [InlineData("""{"status":"InProgress","rowVersion":"not-base64!","changedBy":"x"}""")]
    [InlineData("""{"status":"InProgress","rowVersion":"AAAAAAAAB9E=","changedBy":""}""")]
    [InlineData("""{"rowVersion":"AAAAAAAAB9E=","changedBy":"x"}""")]
    public async Task Invalid_body_returns_400(string body)
    {
        var request = await SeedOne(RequestStatus.New);

        var response = await _client.PatchAsync($"/api/requests/{request.Id}/status",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Summary_is_invalidated_after_a_status_change()
    {
        var request = await SeedOne(RequestStatus.New);
        var before = await _client.GetFromJsonAsync<RequestsSummaryDto>("/api/requests/summary", Json);
        Assert.Equal(1, before!.ByStatus.Single(s => s.Key == RequestStatus.New).Count);

        var version = (await Get(request.Id)).RowVersion;
        (await Patch(request.Id, RequestStatus.InProgress, version, "dana")).EnsureSuccessStatusCode();

        var after = await _client.GetFromJsonAsync<RequestsSummaryDto>("/api/requests/summary", Json);
        Assert.Equal(0, after!.ByStatus.Single(s => s.Key == RequestStatus.New).Count);
        Assert.Equal(1, after.ByStatus.Single(s => s.Key == RequestStatus.InProgress).Count);
    }

    private async Task<ServiceRequest> SeedOne(RequestStatus status) =>
        (await factory.ResetAndSeedAsync(NewRequest(status: status)))[0];

    private async Task<RequestDetailsDto> Get(int id) =>
        (await _client.GetFromJsonAsync<RequestDetailsDto>($"/api/requests/{id}", Json))!;

    private Task<HttpResponseMessage> Patch(int id, RequestStatus status, string rowVersion, string changedBy) =>
        _client.PatchAsJsonAsync($"/api/requests/{id}/status",
            new UpdateStatusRequest { Status = status, RowVersion = rowVersion, ChangedBy = changedBy }, Json);
}
