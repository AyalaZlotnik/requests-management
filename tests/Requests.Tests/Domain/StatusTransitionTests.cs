using FluentAssertions;
using Requests.Application.Requests.Entities;
using Requests.Tests.Support;

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
        StatusTransitions.IsAllowed(from, to).Should().BeTrue();

    [Theory]
    [InlineData(RequestStatus.New, RequestStatus.Completed)]
    [InlineData(RequestStatus.New, RequestStatus.New)]
    [InlineData(RequestStatus.Completed, RequestStatus.New)]
    [InlineData(RequestStatus.Completed, RequestStatus.Waiting)]
    public void Disallowed_transitions_are_rejected(RequestStatus from, RequestStatus to) =>
        StatusTransitions.IsAllowed(from, to).Should().BeFalse();

    [Fact]
    public void ChangeStatus_updates_status_and_timestamp_and_returns_audit_record()
    {
        var request = RequestsApiFactory.NewRequest(status: RequestStatus.New);
        var now = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        var history = request.ChangeStatus(RequestStatus.InProgress, "dana", now);

        request.Status.Should().Be(RequestStatus.InProgress);
        request.UpdatedAt.Should().Be(now);
        history.Should().BeEquivalentTo(new
        {
            PreviousStatus = RequestStatus.New,
            NewStatus = RequestStatus.InProgress,
            ChangedBy = "dana",
            ChangedAt = now
        });
    }

    [Fact]
    public void ChangeStatus_with_disallowed_transition_throws_and_leaves_request_unchanged()
    {
        var request = RequestsApiFactory.NewRequest(status: RequestStatus.New);
        var updatedAt = request.UpdatedAt;

        var act = () => request.ChangeStatus(RequestStatus.Completed, "dana", DateTime.UtcNow);

        act.Should().Throw<InvalidStatusTransitionException>()
            .Which.AllowedStatuses.Should().Equal(RequestStatus.InProgress, RequestStatus.Waiting);
        request.Status.Should().Be(RequestStatus.New);
        request.UpdatedAt.Should().Be(updatedAt);
    }
}
