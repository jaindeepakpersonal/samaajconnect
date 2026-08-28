using FluentValidation;
using Sangam.MemberFamily.Domain.Common;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Members.Commands.UpdateProfile;

public sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    private static readonly string[] Levels = Enum.GetNames<PrivacyLevel>();

    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhotoUrl)
            .Must(ImageUrl.IsAcceptable)
            .WithMessage(
                "A photo link must be a full http:// or https:// web address. "
                + "Scripted and inline links are not accepted.");
        RuleFor(x => x.Mobile).MaximumLength(20);
        RuleFor(x => x.Email).MaximumLength(320);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Locality).MaximumLength(120);
        RuleFor(x => x.Profession).MaximumLength(120);

        RuleFor(x => x.Gender)
            .Must(value => Enum.TryParse<Gender>(value, ignoreCase: true, out _))
            .WithMessage($"Gender must be one of: {string.Join(", ", Enum.GetNames<Gender>())}.")
            .When(x => !string.IsNullOrWhiteSpace(x.Gender));

        RuleFor(x => x.DateOfBirth)
            .Must(value => value!.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth cannot be in the future.")
            .When(x => x.DateOfBirth.HasValue);

        RuleFor(x => x.Privacy).NotNull();

        RuleFor(x => x.Privacy.Mobile).Must(BeALevel).WithMessage(LevelMessage);
        RuleFor(x => x.Privacy.Email).Must(BeALevel).WithMessage(LevelMessage);
        RuleFor(x => x.Privacy.Address).Must(BeALevel).WithMessage(LevelMessage);
        RuleFor(x => x.Privacy.Profession).Must(BeALevel).WithMessage(LevelMessage);
        RuleFor(x => x.Privacy.DateOfBirth).Must(BeALevel).WithMessage(LevelMessage);
    }

    private static bool BeALevel(string value) => Enum.TryParse<PrivacyLevel>(value, ignoreCase: true, out _);

    private static string LevelMessage => $"Privacy must be one of: {string.Join(", ", Levels)}.";
}
