using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;
using Requests.Tests.Support;
using static Requests.Tests.Support.RequestsApiFactory;

namespace Requests.Tests.Api;

public class UpdateStatusTests(RequestsApiFactory factory) : IClassFixture<RequestsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Valid_update_changes_status_version_and_timestamp_and_writes_audit()
    {
        var request = await SeedOne(RequestStatus.New);
        var before = await Get(request.Id);

        var response = await Patch(request.Id, RequestStatus.InProgress, before.RowVersion, "dana");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = (await response.Content.ReadFromJsonAsync<RequestDetailsDto>(Json))!;
        after.Status.Should().Be(RequestStatus.InProgress);
        after.RowVersion.Should().NotBe(before.RowVersion);
        after.UpdatedAt.Should().BeAfter(before.UpdatedAt);

        var history = await _client.GetFromJsonAsync<List<StatusHistoryDto>>($"/api/requests/{request.Id}/history", Json);
        history.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            PreviousStatus = RequestStatus.New,
            NewStatus = RequestStatus.InProgress,
            ChangedBy = "dana",
            ChangedAt = after.UpdatedAt
        });
    }

    [Fact]
    public async Task Concurrent_updates_with_the_same_version_only_one_succeeds()
    {
        var request = await SeedOne(RequestStatus.New);
        var version = (await Get(request.Id)).RowVersion;

        // Five users read the same version and submit at the same time.
        var responses = await Task.WhenAll(Enumerable.Range(1, 5).Select(i =>
            Patch(request.Id, i % 2 == 0 ? RequestStatus.Waiting : RequestStatus.InProgress, version, $"user{i}")));

        responses.Should().ContainSingle(r => r.StatusCode == HttpStatusCode.OK);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(4);

        // No lost update: exactly one audit row, and it matches the stored status.
        var stored = await Get(request.Id);
        var history = await factory.QueryDbAsync(db => db.StatusHistory.Where(h => h.RequestId == request.Id).ToListAsync());
        history.Should().ContainSingle().Which.NewStatus.Should().Be(stored.Status);
    }

    [Fact]
    public async Task Update_with_stale_version_returns_409()
    {
        var request = await SeedOne(RequestStatus.New);
        var staleVersion = (await Get(request.Id)).RowVersion;
        (await Patch(request.Id, RequestStatus.InProgress, staleVersion, "first")).EnsureSuccessStatusCode();

        var response = await Patch(request.Id, RequestStatus.Waiting, staleVersion, "second");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Get(request.Id)).Status.Should().Be(RequestStatus.InProgress);
    }

    [Fact]
    public async Task Disallowed_transition_returns_422_with_allowed_statuses()
    {
        var request = await SeedOne(RequestStatus.New);
        var version = (await Get(request.Id)).RowVersion;

        var response = await Patch(request.Id, RequestStatus.Completed, version, "dana");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json);
        problem!.Title.Should().Be("Invalid status transition");
        (await Get(request.Id)).Status.Should().Be(RequestStatus.New);
    }

    [Fact]
    public async Task Unknown_request_returns_404()
    {
        await factory.ResetAndSeedAsync();

        var response = await Patch(424242, RequestStatus.InProgress, "AAAAAAAAB9E=", "dana");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Summary_is_invalidated_after_a_status_change()
    {
        var request = await SeedOne(RequestStatus.New);
        var before = await _client.GetFromJsonAsync<RequestsSummaryDto>("/api/requests/summary", Json);
        before!.ByStatus.Single(s => s.Key == RequestStatus.New).Count.Should().Be(1);

        var version = (await Get(request.Id)).RowVersion;
        (await Patch(request.Id, RequestStatus.InProgress, version, "dana")).EnsureSuccessStatusCode();

        var after = await _client.GetFromJsonAsync<RequestsSummaryDto>("/api/requests/summary", Json);
        after!.ByStatus.Single(s => s.Key == RequestStatus.New).Count.Should().Be(0);
        after.ByStatus.Single(s => s.Key == RequestStatus.InProgress).Count.Should().Be(1);
    }

    private async Task<ServiceRequest> SeedOne(RequestStatus status) =>
        (await factory.ResetAndSeedAsync(NewRequest(status: status)))[0];

    private async Task<RequestDetailsDto> Get(int id) =>
        (await _client.GetFromJsonAsync<RequestDetailsDto>($"/api/requests/{id}", Json))!;

    private Task<HttpResponseMessage> Patch(int id, RequestStatus status, string rowVersion, string changedBy) =>
        _client.PatchAsJsonAsync($"/api/requests/{id}/status",
            new UpdateStatusRequest { Status = status, RowVersion = rowVersion, ChangedBy = changedBy }, Json);
}
