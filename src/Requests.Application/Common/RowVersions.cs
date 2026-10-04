using System.ComponentModel.DataAnnotations;

namespace Requests.Application.Common;

/// <summary>
/// The one rule for a client-supplied version: base64 of exactly 8 bytes (a SQL Server rowversion).
/// Used for the If-Match header of a single update and for every item of a bulk update.
/// </summary>
public static class RowVersions
{
    public const int Length = 8;

    public static bool TryParse(string? value, out byte[] rowVersion)
    {
        rowVersion = [];
        var buffer = new byte[Length];
        if (value is null || !Convert.TryFromBase64String(value, buffer, out var written) || written != Length)
        {
            return false;
        }

        rowVersion = buffer;
        return true;
    }

    /// <summary>For values that were already validated; a malformed value here is a programming error.</summary>
    public static byte[] Parse(string value) =>
        TryParse(value, out var rowVersion) ? rowVersion : throw new FormatException("Not a valid row version.");
}

/// <summary>Validates that a string is a version issued by this API (see <see cref="RowVersions"/>).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RowVersionAttribute : ValidationAttribute
{
    public RowVersionAttribute() : base("The {0} field is not a version issued by this API.") { }

    public override bool IsValid(object? value) => value is null || RowVersions.TryParse(value as string, out _);
}
