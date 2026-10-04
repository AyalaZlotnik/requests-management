using Requests.Api.Domain;
using Requests.Tests.Infrastructure;

namespace Requests.Tests.Domain;

public class StatusTransitionTests
{
    [Theory]
    [InlineData(RequestStatus.New, RequestStatus.InProgress)]
    [InlineData(RequestStatus.New, RequestStatus.Waiting)]
    [InlineData(RequestStatus.InProgress, RequestStatus.Completed)]
    [InlineData(RequestStatus.Waiting, RequestStatus.InProgress)]
    [InlineData(RequestStatus.Completed, RequestStatus.InProgress)]
    public void Allowed_transitions_are_accepted(RequestStatus from, RequestStatus to) =>
        Assert.True(StatusTransitions.IsAllowed(from, to));

    [Theory]
    [InlineData(RequestStatus.New, RequestStatus.Completed)]
    [InlineData(RequestStatus.New, RequestStatus.New)]
    [InlineData(RequestStatus.Completed, RequestStatus.New)]
    [InlineData(RequestStatus.Completed, RequestStatus.Waiting)]
    public void Disallowed_transitions_are_rejected(RequestStatus from, RequestStatus to) =>
        Assert.False(StatusTransitions.IsAllowed(from, to));

    [Fact]
    public void ChangeStatus_updates_status_and_timestamp_and_returns_audit_record()
    {
        var request = RequestsApiFactory.NewRequest(status: RequestStatus.New);
        var now = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        var history = request.ChangeStatus(RequestStatus.InProgress, "dana", now);

        Assert.Equal(RequestStatus.InProgress, request.Status);
        Assert.Equal(now, request.UpdatedAt);
        Assert.Equal(RequestStatus.New, history.PreviousStatus);
        Assert.Equal(RequestStatus.InProgress, history.NewStatus);
        Assert.Equal("dana", history.ChangedBy);
        Assert.Equal(now, history.ChangedAt);
    }

    [Fact]
    public void ChangeStatus_with_disallowed_transition_throws_and_leaves_request_unchanged()
    {
        var request = RequestsApiFactory.NewRequest(status: RequestStatus.New);
        var updatedAt = request.UpdatedAt;

        var ex = Assert.Throws<InvalidStatusTransitionException>(() =>
            request.ChangeStatus(RequestStatus.Completed, "dana", DateTime.UtcNow));

        Assert.Equal(RequestStatus.New, request.Status);
        Assert.Equal(updatedAt, request.UpdatedAt);
        Assert.Equal([RequestStatus.InProgress, RequestStatus.Waiting], ex.AllowedStatuses);
    }
}
