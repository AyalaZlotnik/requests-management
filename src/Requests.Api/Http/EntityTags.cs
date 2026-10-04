using Microsoft.Net.Http.Headers;

namespace Requests.Api.Http;

/// <summary>
/// Converts between the database rowversion and an HTTP entity tag (ETag), and validates If-Match.
/// The ETag is the strong, quoted base64 of the rowversion: "AAAAAAABXHs=".
/// </summary>
public static class EntityTags
{
    private const int RowVersionLength = 8;

    public enum IfMatchError
    {
        None,
        Missing,
        Invalid
    }

    public static string FromRowVersion(string base64RowVersion) => $"\"{base64RowVersion}\"";

    /// <summary>
    /// Reads the version from If-Match. Rejected: a missing header (the client must say which version it read),
    /// "*" (would mean "overwrite whatever is there"), weak tags, several tags, and values that are not a rowversion.
    /// </summary>
    public static IfMatchError TryReadIfMatch(string? header, out byte[] rowVersion, out string detail)
    {
        rowVersion = [];
        detail = string.Empty;

        if (string.IsNullOrWhiteSpace(header))
        {
            detail = "Send the ETag you received from GET /api/requests/{id} in an If-Match header.";
            return IfMatchError.Missing;
        }

        if (!EntityTagHeaderValue.TryParseStrictList([header], out var tags) || tags.Count != 1)
        {
            detail = "If-Match must contain exactly one entity tag.";
            return IfMatchError.Invalid;
        }

        var tag = tags[0];
        if (tag.Equals(EntityTagHeaderValue.Any))
        {
            detail = "If-Match: * is not accepted – it would overwrite changes made by others. Send the specific ETag you read.";
            return IfMatchError.Invalid;
        }

        if (tag.IsWeak)
        {
            detail = "A weak entity tag cannot be used for an update. Send the strong ETag returned by GET.";
            return IfMatchError.Invalid;
        }

        var buffer = new byte[RowVersionLength];
        if (!Convert.TryFromBase64String(tag.Tag.ToString().Trim('"'), buffer, out var written) || written != RowVersionLength)
        {
            detail = "The If-Match value is not an ETag issued by this API.";
            return IfMatchError.Invalid;
        }

        rowVersion = buffer;
        return IfMatchError.None;
    }
}
