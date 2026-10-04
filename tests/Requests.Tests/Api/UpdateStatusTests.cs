using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task Get_returns_the_version_as_a_strong_etag()
    {
        var request = await SeedOne(RequestStatus.New);

        var (body, etag) = await _client.GetRequestAsync(request.Id);

        etag.Should().Be($"\"{body.RowVersion}\"");
    }

    [Fact]
    public async Task Valid_update_changes_status_version_and_timestamp_and_writes_audit()
    {
        var request = await SeedOne(RequestStatus.New);
        var (before, etag) = await _client.GetRequestAsync(request.Id);

        var response = await _client.PatchStatusAsync(request.Id, RequestStatus.InProgress, etag, "דנה לוי");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = (await response.Content.ReadFromJsonAsync<RequestDetailsDto>(Json))!;
        after.Status.Should().Be(RequestStatus.InProgress);
        after.RowVersion.Should().NotBe(before.RowVersion);
        after.UpdatedAt.Should().BeAfter(before.UpdatedAt);
        response.Headers.ETag!.ToString().Should().Be($"\"{after.RowVersion}\"");

        var history = await _client.GetFromJsonAsync<List<StatusHistoryDto>>($"/api/requests/{request.Id}/history", Json);
        history.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            PreviousStatus = RequestStatus.New,
            NewStatus = RequestStatus.InProgress,
            ChangedBy = "דנה לוי",
            ChangedAt = after.UpdatedAt
        });
    }

    [Fact]
    public async Task Stale_version_returns_409_with_current_state_and_who_changed_it_and_does_not_overwrite()
    {
        var request = await SeedOne(RequestStatus.New);
        var (_, staleETag) = await _client.GetRequestAsync(request.Id);
        (await _client.PatchStatusAsync(request.Id, RequestStatus.InProgress, staleETag, "יוסי כהן")).EnsureSuccessStatusCode();

        var response = await _client.PatchStatusAsync(request.Id, RequestStatus.Waiting, staleETag, "מיכל פרץ");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var current = problem.GetProperty("currentState");
        current.GetProperty("id").GetInt32().Should().Be(request.Id);
        // Enums are names in every response, including error bodies – the client compares them as strings.
        current.GetProperty("status").GetString().Should().Be("InProgress");
        var lastChange = problem.GetProperty("lastChange");
        lastChange.GetProperty("changedBy").GetString().Should().Be("יוסי כהן");
        lastChange.GetProperty("newStatus").GetString().Should().Be("InProgress");

        // Read straight from the database: the second user's change must not have been written.
        var stored = await factory.QueryDbAsync(db => db.Requests.AsNoTracking().SingleAsync(r => r.Id == request.Id));
        stored.Status.Should().Be(RequestStatus.InProgress);
        current.GetProperty("rowVersion").GetString().Should().Be(Convert.ToBase64String(stored.RowVersion));
    }

    [Fact]
    public async Task Retrying_with_the_version_returned_in_the_409_succeeds()
    {
        var request = await SeedOne(RequestStatus.New);
        var (_, staleETag) = await _client.GetRequestAsync(request.Id);
        (await _client.PatchStatusAsync(request.Id, RequestStatus.InProgress, staleETag, "יוסי כהן")).EnsureSuccessStatusCode();

        var conflict = await _client.PatchStatusAsync(request.Id, RequestStatus.Waiting, staleETag);
        var freshVersion = (await conflict.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("currentState").GetProperty("rowVersion").GetString();

        var retry = await _client.PatchStatusAsync(request.Id, RequestStatus.Waiting, $"\"{freshVersion}\"");

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Concurrent_updates_with_the_same_version_only_one_succeeds()
    {
        var request = await SeedOne(RequestStatus.New);
        var (_, etag) = await _client.GetRequestAsync(request.Id);

        // Five users read the same version and submit at the same time.
        var responses = await Task.WhenAll(Enumerable.Range(1, 5).Select(i =>
            _client.PatchStatusAsync(request.Id, i % 2 == 0 ? RequestStatus.Waiting : RequestStatus.InProgress, etag, $"user{i}")));

        responses.Should().ContainSingle(r => r.StatusCode == HttpStatusCode.OK);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(4);

        // No lost update: exactly one audit row, and it matches the stored status.
        var (stored, _) = await _client.GetRequestAsync(request.Id);
        var history = await factory.QueryDbAsync(db => db.StatusHistory.Where(h => h.RequestId == request.Id).ToListAsync());
        history.Should().ContainSingle().Which.NewStatus.Should().Be(stored.Status);
    }

    [Fact]
    public async Task Missing_if_match_returns_428()
    {
        var request = await SeedOne(RequestStatus.New);

        var response = await _client.PatchStatusAsync(request.Id, RequestStatus.InProgress, ifMatch: null);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"AAAAAAAAB9E=\"")]
    [InlineData("\"not-a-version\"")]
    [InlineData("AAAAAAAAB9E=")]
    [InlineData("\"AAAAAAAAB9E=\", \"AAAAAAAAB9I=\"")]
    public async Task Unusable_if_match_returns_400_and_changes_nothing(string ifMatch)
    {
        var request = await SeedOne(RequestStatus.New);

        var response = await _client.PatchStatusAsync(request.Id, RequestStatus.InProgress, ifMatch);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.GetRequestAsync(request.Id)).Request.Status.Should().Be(RequestStatus.New);
    }

    [Fact]
    public async Task Disallowed_transition_returns_422_with_allowed_statuses()
    {
        var request = await SeedOne(RequestStatus.New);
        var (_, etag) = await _client.GetRequestAsync(request.Id);

        var response = await _client.PatchStatusAsync(request.Id, RequestStatus.Completed, etag);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json);
        problem!.Title.Should().Be("Invalid status transition");
        (await _client.GetRequestAsync(request.Id)).Request.Status.Should().Be(RequestStatus.New);
    }

    [Fact]
    public async Task Unknown_request_returns_404()
    {
        await factory.ResetAndSeedAsync();

        var response = await _client.PatchStatusAsync(424242, RequestStatus.InProgress, "\"AAAAAAAAB9E=\"");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("""{"status":"Closed","changedBy":"x"}""")]
    [InlineData("""{"status":7,"changedBy":"x"}""")]
    [InlineData("""{"status":"InProgress","changedBy":""}""")]
    [InlineData("""{"changedBy":"x"}""")]
    public async Task Invalid_body_returns_400(string body)
    {
        var request = await SeedOne(RequestStatus.New);
        var (_, etag) = await _client.GetRequestAsync(request.Id);

        var message = new HttpRequestMessage(HttpMethod.Patch, $"/api/requests/{request.Id}/status")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        message.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await _client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Summary_is_invalidated_after_a_status_change()
    {
        var request = await SeedOne(RequestStatus.New);
        var before = await _client.GetFromJsonAsync<RequestsSummaryDto>("/api/requests/summary", Json);
        before!.ByStatus.Single(s => s.Key == RequestStatus.New).Count.Should().Be(1);

        var (_, etag) = await _client.GetRequestAsync(request.Id);
        (await _client.PatchStatusAsync(request.Id, RequestStatus.InProgress, etag)).EnsureSuccessStatusCode();

        var after = await _client.GetFromJsonAsync<RequestsSummaryDto>("/api/requests/summary", Json);
        after!.ByStatus.Single(s => s.Key == RequestStatus.New).Count.Should().Be(0);
        after.ByStatus.Single(s => s.Key == RequestStatus.InProgress).Count.Should().Be(1);
    }

    private async Task<ServiceRequest> SeedOne(RequestStatus status) =>
        (await factory.ResetAndSeedAsync(NewRequest(status: status)))[0];
}
