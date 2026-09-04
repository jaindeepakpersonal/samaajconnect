using FluentValidation;
using Sangam.MemberFamily.Domain.Children;

namespace Sangam.MemberFamily.Application.Children.Commands.DecideChildConversion;

/// <summary>
/// The validator this command did without.
/// </summary>
/// <remarks>
/// <para>
/// Root <c>CLAUDE.md</c> §4.3 asks for one validator per command, and the
/// reason is exactly what happened here: <c>ValidationBehavior</c> runs the
/// validators that exist, so a command with none has no input validation at
/// all. This one carried free text and nothing checked it.
/// </para>
/// <para>
/// <b>The length is not arbitrary and must stay in step with the column.</b>
/// <c>ChildConfiguration</c> maps <c>DecisionNote</c> as
/// <c>HasMaxLength(1000)</c>, so Postgres refuses a longer value with SQLSTATE
/// 22001, <c>UnhandledExceptionBehavior</c> converts that to a generic failure,
/// and an administrator who wrote a long note received a 500 saying only that
/// something went wrong. Verified before this existed: a 1001-character note
/// answered 500, and there is a test that would fail again if this rule were
/// removed.
/// </para>
/// <para>
/// A rule stricter than the column would refuse a note the database would have
/// taken; a looser one would put the 500 back. The two numbers are one number,
/// and <see cref="ChildConversionRequest.MaxDecisionNoteLength"/> is where it
/// lives so neither side can move alone.
/// </para>
/// </remarks>
public sealed class DecideChildConversionCommandValidator
    : AbstractValidator<DecideChildConversionCommand>
{
    public DecideChildConversionCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();

        RuleFor(x => x.Note)
            .MaximumLength(ChildConversionRequest.MaxDecisionNoteLength)
            .WithMessage(
                "A decision note has to be "
                + $"{ChildConversionRequest.MaxDecisionNoteLength} characters or fewer.");
    }
}
