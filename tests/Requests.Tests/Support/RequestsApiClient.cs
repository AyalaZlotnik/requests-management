using System.Net.Http.Json;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;
using static Requests.Tests.Support.RequestsApiFactory;

namespace Requests.Tests.Support;

/// <summary>Small helpers so tests read as scenarios rather than HTTP plumbing.</summary>
public static class RequestsApiClient
{
    /// <summary>GET /api/requests/{id}; returns the body and the ETag header.</summary>
    public static async Task<(RequestDetailsDto Request, string ETag)> GetRequestAsync(this HttpClient client, int id)
    {
        var response = await client.GetAsync($"/api/requests/{id}");
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<RequestDetailsDto>(Json))!;
        return (body, response.Headers.ETag!.ToString());
    }

    /// <summary>PATCH /api/requests/{id}/status with an optional raw If-Match value.</summary>
    public static Task<HttpResponseMessage> PatchStatusAsync(
        this HttpClient client, int id, RequestStatus status, string? ifMatch, string changedBy = "דנה לוי")
    {
        var message = new HttpRequestMessage(HttpMethod.Patch, $"/api/requests/{id}/status")
        {
            Content = JsonContent.Create(new UpdateStatusRequest { Status = status, ChangedBy = changedBy }, options: Json)
        };
        if (ifMatch is not null)
        {
            message.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(message);
    }
}
