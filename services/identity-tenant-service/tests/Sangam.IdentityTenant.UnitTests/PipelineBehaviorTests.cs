using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Behaviors;
using Sangam.IdentityTenant.Application.Common;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class ValidationBehaviorTests
{
    public sealed record SampleCommand(string Name) : ICommand<string>;

    private sealed class NameRequiredValidator : AbstractValidator<SampleCommand>
    {
        public NameRequiredValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    private sealed class NameLengthValidator : AbstractValidator<SampleCommand>
    {
        public NameLengthValidator() => RuleFor(x => x.Name).MinimumLength(3);
    }

    private static readonly RequestHandlerDelegate<Result<string>> Next =
        () => Task.FromResult(Result.Success("handled"));

    [Fact]
    public async Task Calls_the_handler_when_there_are_no_validators()
    {
        var behavior = new ValidationBehavior<SampleCommand, Result<string>>([]);

        var result = await behavior.Handle(new SampleCommand("ok"), Next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Collects_failures_from_every_validator_into_one_result()
    {
        var behavior = new ValidationBehavior<SampleCommand, Result<string>>(
            [new NameRequiredValidator(), new NameLengthValidator()]);

        var result = await behavior.Handle(new SampleCommand(""), Next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.FieldErrors.Should().ContainKey(nameof(SampleCommand.Name));
        result.Error.FieldErrors[nameof(SampleCommand.Name)].Should().HaveCount(2);
    }

    [Fact]
    public async Task Returns_a_failure_rather_than_throwing_a_ValidationException()
    {
        var behavior = new ValidationBehavior<SampleCommand, Result<string>>([new NameRequiredValidator()]);

        var act = async () => await behavior.Handle(new SampleCommand(""), Next, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}

public sealed class TransactionBehaviorTests
{
    public sealed record SampleCommand : ICommand<string>;

    public sealed record SampleQuery : IQuery<string>;

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IAppTransaction _transaction = Substitute.For<IAppTransaction>();

    public TransactionBehaviorTests() =>
        _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(_transaction);

    private TransactionBehavior<TRequest, Result<string>> BehaviorFor<TRequest>()
        where TRequest : notnull =>
        new(_unitOfWork, NullLogger<TransactionBehavior<TRequest, Result<string>>>.Instance);

    [Fact]
    public async Task Commits_when_a_command_succeeds()
    {
        await BehaviorFor<SampleCommand>().Handle(
            new SampleCommand(),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        await _transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rolls_back_when_a_command_returns_a_business_failure()
    {
        await BehaviorFor<SampleCommand>().Handle(
            new SampleCommand(),
            () => Task.FromResult(Result.Failure<string>(Error.Conflict("X", "already done"))),
            CancellationToken.None);

        await _transaction.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rolls_back_and_rethrows_when_a_command_throws()
    {
        var act = async () => await BehaviorFor<SampleCommand>().Handle(
            new SampleCommand(),
            () => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _transaction.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Never_opens_a_transaction_for_a_query()
    {
        await BehaviorFor<SampleQuery>().Handle(
            new SampleQuery(),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_nest_a_transaction_when_one_is_already_open()
    {
        _unitOfWork.HasActiveTransaction.Returns(true);

        await BehaviorFor<SampleCommand>().Handle(
            new SampleCommand(),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }
}

public sealed class UnhandledExceptionBehaviorTests
{
    public sealed record SampleCommand : ICommand<string>;

    private readonly UnhandledExceptionBehavior<SampleCommand, Result<string>> _behavior =
        new(NullLogger<UnhandledExceptionBehavior<SampleCommand, Result<string>>>.Instance);

    [Fact]
    public async Task Converts_an_unexpected_exception_into_a_generic_failure()
    {
        var result = await _behavior.Handle(
            new SampleCommand(),
            () => throw new InvalidOperationException("connection reset by peer"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Unexpected");

        // The caller must not learn what actually broke.
        result.Error.Description.Should().NotContain("connection reset");
    }

    [Fact]
    public async Task Lets_cancellation_propagate_rather_than_reporting_it_as_a_server_error()
    {
        var act = async () => await _behavior.Handle(
            new SampleCommand(),
            () => throw new OperationCanceledException(),
            CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
