using System.Text.RegularExpressions;
using FluentValidation;

namespace Sangam.MemberFamily.Application.Children.Commands.RequestChildConversion;

public sealed partial class RequestChildConversionCommandValidator
    : AbstractValidator<RequestChildConversionCommand>
{
    /// <summary>Mirrors the identifier rule in identity-tenant-service's registration.</summary>
    [GeneratedRegex(@"^([^@\s]+@[^@\s]+\.[^@\s]+|(\+91)?[6-9]\d{9})$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    public RequestChildConversionCommandValidator()
    {
        RuleFor(x => x.ChildProfileId).NotEmpty();

        RuleFor(x => x.MobileOrEmail)
            .NotEmpty()
            .MaximumLength(320)
            .Must(value => IdentifierPattern().IsMatch(value.Trim()))
            .WithMessage("Enter a valid email address or 10-digit mobile number.");
    }
}
