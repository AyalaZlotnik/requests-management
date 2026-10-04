using System.ComponentModel.DataAnnotations;
using Requests.Application.Requests.Entities;

namespace Requests.Application.Requests.Contracts;

public enum RequestSortField
{
    CreatedAt,
    UpdatedAt,
    Priority,
    Status,
    Title,
    OrganizationName
}

public enum SortDirection
{
    Asc,
    Desc
}

/// <summary>
/// Filters shared by the list and the summary (GET /api/requests and /api/requests/summary).
/// All filters are combined with AND.
/// </summary>
public class RequestFilter : IValidatableObject
{
    /// <summary>Free text, matched against Title and OrganizationName (contains).</summary>
    [StringLength(100)]
    public string? Search { get; set; }

    /// <summary>One or more statuses (?status=New&amp;status=Waiting).</summary>
    public RequestStatus[]? Status { get; set; }

    public RequestPriority[]? Priority { get; set; }

    /// <summary>Prefix match on the organization name.</summary>
    [StringLength(RequestFieldLimits.OrganizationName)]
    public string? OrganizationName { get; set; }

    /// <summary>Part of the handler name (contains), e.g. a first or last name.</summary>
    [StringLength(RequestFieldLimits.AssignedTo)]
    public string? AssignedTo { get; set; }

    /// <summary>Inclusive lower bound on CreatedAt (UTC).</summary>
    public DateTime? CreatedFrom { get; set; }

    /// <summary>Inclusive upper bound on CreatedAt (UTC).</summary>
    public DateTime? CreatedTo { get; set; }

    /// <summary>True when no filter is set – the default view.</summary>
    public bool HasNoFilters() =>
        string.IsNullOrWhiteSpace(Search)
        && Status is not { Length: > 0 }
        && Priority is not { Length: > 0 }
        && string.IsNullOrWhiteSpace(OrganizationName)
        && string.IsNullOrWhiteSpace(AssignedTo)
        && CreatedFrom is null
        && CreatedTo is null;

    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Enum binding accepts numbers ("?status=99"), so check every value is actually defined.
        if (Status?.Any(s => !Enum.IsDefined(s)) == true)
            yield return new ValidationResult("Unknown status value.", [nameof(Status)]);

        if (Priority?.Any(p => !Enum.IsDefined(p)) == true)
            yield return new ValidationResult("Unknown priority value.", [nameof(Priority)]);

        if (CreatedFrom > CreatedTo)
            yield return new ValidationResult("CreatedFrom must be earlier than CreatedTo.", [nameof(CreatedFrom), nameof(CreatedTo)]);
    }
}

/// <summary>Query string parameters of GET /api/requests: the filters plus paging and sorting.</summary>
public class RequestSearchQuery : RequestFilter
{
    public const int MaxPageSize = 100;

    [Range(1, 100_000)]
    public int Page { get; set; } = 1;

    [Range(1, MaxPageSize)]
    public int PageSize { get; set; } = 20;

    public RequestSortField SortBy { get; set; } = RequestSortField.CreatedAt;

    public SortDirection SortDirection { get; set; } = SortDirection.Desc;

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        if (!Enum.IsDefined(SortBy))
            yield return new ValidationResult("Unknown sort field.", [nameof(SortBy)]);

        if (!Enum.IsDefined(SortDirection))
            yield return new ValidationResult("Sort direction must be Asc or Desc.", [nameof(SortDirection)]);
    }
}
