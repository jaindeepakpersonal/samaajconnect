using FluentValidation;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Children.Commands.CreateChildProfile;

public sealed class CreateChildProfileCommandValidator : AbstractValidator<CreateChildProfileCommand>
{
    public CreateChildProfileCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhotoUrl).MaximumLength(2048);

        RuleFor(x => x.DateOfBirth)
            .Must(value => value <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth cannot be in the future.")
            .Must(value => value >= DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-120))
            .WithMessage("Date of birth is not plausible.");

        RuleFor(x => x.Gender)
            .Must(value => Enum.TryParse<Gender>(value, ignoreCase: true, out _))
            .WithMessage($"Gender must be one of: {string.Join(", ", Enum.GetNames<Gender>())}.")
            .When(x => !string.IsNullOrWhiteSpace(x.Gender));

        // DPDP section 9. Refused here rather than in the handler so the
        // message reaches the form that asked for it.
        RuleFor(x => x.ParentalConsentGiven)
            .Equal(true)
            .WithMessage(
                "A child's record can only be created with the consent of their parent "
                + "or lawful guardian.");

        RuleFor(x => x.NoticeVersion)
            .NotEmpty()
            .WithMessage("Child data notice version is required.");
    }
}
