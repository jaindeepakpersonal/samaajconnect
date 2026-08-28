using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.IntegrationEvents;
using Sangam.AuditNotification.Application.IntegrationEvents.Commands.ErasePersonalData;
using Sangam.AuditNotification.Domain.AuditLogs;
using Xunit;

namespace Sangam.AuditNotification.UnitTests;

public sealed class ErasePersonalDataCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly IErasureRepository _erasure = Substitute.For<IErasureRepository>();
    private readonly IAuditLogRepository _auditLogs = Substitute.For<IAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ErasePersonalDataCommandHandler _handler;

    public ErasePersonalDataCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _erasure.DeleteNotificationsForAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(3);
        _erasure.DeIdentifyAuditRowsForAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(7);

        _handler = new ErasePersonalDataCommandHandler(
            _erasure,
            _auditLogs,
            _unitOfWork,
            _clock,
            NullLogger<ErasePersonalDataCommandHandler>.Instance);
    }

    private static IntegrationEventEnvelope Envelope(string? payload = null, Guid? messageId = null) =>
        new(
            messageId ?? Guid.NewGuid(),
            TenantId,
            "identity.user.erased.v1",
            "Sangam.IdentityTenant.Domain.Users.UserErasedDomainEvent",
            payload ?? $$"""{"userId":"{{UserId}}","tenantId":"{{TenantId}}"}""",
            Now.AddMinutes(-1));

    private Task<Application.Common.Result<ErasePersonalDataResult>> Handle(
        IntegrationEventEnvelope envelope) =>
        _handler.Handle(new ErasePersonalDataCommand(envelope), CancellationToken.None);

    [Fact]
    public async Task Deletes_notifications_and_de_identifies_audit_rows()
    {
        var result = await Handle(Envelope());

        result.IsSuccess.Should().BeTrue();
        result.Value.NotificationsDeleted.Should().Be(3);
        result.Value.AuditRowsDeIdentified.Should().Be(7);

        await _erasure.Received(1).DeleteNotificationsForAsync(UserId, Arg.Any<CancellationToken>());
        await _erasure.Received(1).DeIdentifyAuditRowsForAsync(UserId, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Records_the_erasure_itself_with_no_actor()
    {
        AuditLog? recorded = null;
        _auditLogs.When(x => x.Add(Arg.Any<AuditLog>())).Do(call => recorded = call.Arg<AuditLog>());

        await Handle(Envelope());

        recorded.Should().NotBeNull();
        recorded!.Action.Should().Be("Erased");
        recorded.EntityName.Should().Be("User");
        recorded.EntityId.Should().Be(UserId.ToString());

        // The row proves the request was honoured. It must not re-introduce the
        // person it is about as an actor.
        recorded.ActorUserId.Should().BeNull();
    }

    [Fact]
    public async Task Does_nothing_when_the_message_has_already_been_handled()
    {
        _auditLogs
            .AlreadyRecordedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await Handle(Envelope());

        result.Value.AlreadyHandled.Should().BeTrue();

        _auditLogs.DidNotReceive().Add(Arg.Any<AuditLog>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"tenantId":"not-a-guid"}""")]
    [InlineData("""{"userId":"00000000-0000-0000-0000-000000000000"}""")]
    public async Task Erases_nothing_when_the_payload_names_no_one(string payload)
    {
        // Better to leave the data and raise an error than to guess who was
        // meant. There is no undo.
        var result = await Handle(Envelope(payload));

        result.IsSuccess.Should().BeTrue();

        await _erasure.DidNotReceive()
            .DeleteNotificationsForAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _erasure.DidNotReceive()
            .DeIdentifyAuditRowsForAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
