using FluentValidation;

namespace Sangam.MemberFamily.Application.Families.Commands.SetFamilyHead;

public sealed class SetFamilyHeadCommandValidator : AbstractValidator<SetFamilyHeadCommand>
{
    public SetFamilyHeadCommandValidator()
    {
        RuleFor(x => x.MemberProfileId)
            .Must(value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _))
            .WithMessage("MemberProfileId must be a valid id, or empty to clear the head.");
    }
}
