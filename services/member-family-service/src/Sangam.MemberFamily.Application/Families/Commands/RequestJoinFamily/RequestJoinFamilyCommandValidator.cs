using FluentValidation;
using Sangam.MemberFamily.Domain.Families;

namespace Sangam.MemberFamily.Application.Families.Commands.RequestJoinFamily;

public sealed class RequestJoinFamilyCommandValidator : AbstractValidator<RequestJoinFamilyCommand>
{
    public RequestJoinFamilyCommandValidator()
    {
        RuleFor(x => x.FamilyCode).NotEmpty().Length(8);

        RuleFor(x => x.Relationship)
            .NotEmpty()
            .Must(value => Enum.TryParse<Relationship>(value, ignoreCase: true, out _))
            .WithMessage($"Relationship must be one of: {string.Join(", ", Enum.GetNames<Relationship>())}.");
    }
}
