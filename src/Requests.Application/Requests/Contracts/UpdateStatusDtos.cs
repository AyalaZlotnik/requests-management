using System.ComponentModel.DataAnnotations;
using Requests.Application.Common;
using Requests.Application.Requests.Entities;

namespace Requests.Application.Requests.Contracts;

/// <summary>Body of a single status update. The version the client read travels in the If-Match header.</summary>
public class UpdateStatusRequest
{
    [Required]
    public RequestStatus? Status { get; set; }

    /// <summary>Who made the change (stored in the audit). In a real system this comes from the authenticated user.</summary>
    [Required, StringLength(RequestFieldLimits.ChangedBy, MinimumLength = 1)]
    public string ChangedBy { get; set; } = string.Empty;
}

public class BulkStatusItem
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    /// <summary>The version of this request the client read (base64 of 8 bytes).</summary>
    [Required, RowVersion]
    public string RowVersion { get; set; } = string.Empty;
}

public class BulkUpdateStatusRequest : IValidatableObject
{
    public const int MaxItems = 100;

    [Required]
    public RequestStatus? Status { get; set; }

    [Required, MinLength(1), MaxLength(MaxItems)]
    public List<BulkStatusItem> Items { get; set; } = [];

    [Required, StringLength(RequestFieldLimits.ChangedBy, MinimumLength = 1)]
    public string ChangedBy { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // JSON like "items": [null] binds to a list with a null element – reject it instead of failing below.
        if (Items.Any(i => i is null))
        {
            yield return new ValidationResult("Items must not contain empty entries.", [nameof(Items)]);
            yield break;
        }

        if (Items.GroupBy(i => i.Id).Any(g => g.Count() > 1))
            yield return new ValidationResult("Each request id may appear only once.", [nameof(Items)]);
    }
}

public enum BulkItemOutcome
{
    Updated,
    NotFound,
    Conflict,
    InvalidTransition
}

public record BulkItemResult(int Id, BulkItemOutcome Outcome, string? Error, string? RowVersion);

public record BulkUpdateStatusResponse(int Requested, int Succeeded, int Failed, IReadOnlyList<BulkItemResult> Results);
