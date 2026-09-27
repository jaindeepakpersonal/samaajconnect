using MediatR;
using Sangam.IdentityTenant.Api.Extensions;
using Sangam.IdentityTenant.Application.Users;
using Sangam.IdentityTenant.Application.Users.Commands.ChangePassword;
using Sangam.IdentityTenant.Application.Users.Commands.IssueCaptcha;
using Sangam.IdentityTenant.Application.Users.Commands.Login;
using Sangam.IdentityTenant.Application.Users.Commands.RedeemPasswordReset;
using Sangam.IdentityTenant.Application.Users.Commands.RefreshSession;
using Sangam.IdentityTenant.Application.Users.Commands.RequestPasswordReset;
using Sangam.IdentityTenant.Application.Users.Commands.ResendSignInCode;
using Sangam.IdentityTenant.Application.Users.Commands.SignOut;
using Sangam.IdentityTenant.Application.Users.Commands.VerifySignInCode;
using Sangam.IdentityTenant.Application.Users.Commands.RegisterMember;
using Sangam.IdentityTenant.Application.Users.Queries.GetCurrentUser;

namespace Sangam.IdentityTenant.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/identity").WithTags("Auth");

        group.MapPost("/register", async (
                RegisterRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new RegisterMemberCommand(
                    request.TenantSlug,
                    request.FullName,
                    request.MobileOrEmail,
                    request.Password,
                    request.ConsentedPurposes ?? [],
                    request.NoticeVersion ?? string.Empty);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(response => Results.Created("/v1/identity/me", response));
            })
            .AllowAnonymous()
            .WithName("RegisterMember")
            .WithSummary("Register as a member of one Samaaj.")
            .Produces<RegisterMemberResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/captcha", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new IssueCaptchaCommand(), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("IssueCaptcha")
            .WithSummary("A fresh CAPTCHA for the sign-in form: an id and a base64 PNG. Single use, five minutes.")
            .Produces<CaptchaResponse>();

        group.MapPost("/login", async (
                LoginRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new LoginCommand(
                        request.MobileOrEmail,
                        request.Password,
                        request.CaptchaId,
                        request.CaptchaAnswer,
                        request.DeviceToken),
                    cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("Login")
            .WithSummary(
                "Common login, step one: CAPTCHA and password. Signs in on a trusted device; "
                + "anywhere else sends a code and answers with a challenge.")
            .Produces<SignInResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/login/verify", async (
                VerifySignInCodeRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new VerifySignInCodeCommand(request.ChallengeToken, request.Code, request.TrustDevice),
                    cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("VerifySignInCode")
            .WithSummary("Step two: the code, with the challenge the password earned. Optionally trusts the device for 30 days.")
            .Produces<SignInResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/login/resend", async (
                ResendSignInCodeRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ResendSignInCodeCommand(request.ChallengeToken), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("ResendSignInCode")
            .WithSummary("Sends a fresh code for a waiting sign-in, at most every 30 seconds. Answers the same either way.")
            .Produces<ResendSignInCodeResponse>()
            .ProducesValidationProblem();

        group.MapPost("/token/refresh", async (
                RefreshRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new RefreshSessionCommand(request.RefreshToken), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("RefreshSession")
            .WithSummary("Exchange a refresh token for a new access token and the next refresh token.")
            .Produces<RefreshSessionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (
                SignOutRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new SignOutCommand(request.RefreshToken, request.Everywhere), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("SignOut")
            .WithSummary("End this session, or every session for the account.")
            .Produces<SignOutResponse>()
            .ProducesValidationProblem();

        group.MapGet("/me", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetCurrentUserQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetCurrentUser")
            .WithSummary("The signed-in account with its roles and permissions.")
            .Produces<CurrentUserResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/me/password", async (
                ChangePasswordRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ChangePasswordCommand(request.CurrentPassword, request.NewPassword),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ChangePassword")
            .WithSummary("Set a new password, given the current one. Ends every other session.")
            .Produces<ChangePasswordResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/password-reset/request", async (
                PasswordResetRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new RequestPasswordResetCommand(request.MobileOrEmail), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("RequestPasswordReset")
            .WithSummary(
                "Sends a password reset code. Answers the same way whether or not the account exists.")
            .Produces<RequestPasswordResetResponse>()
            .ProducesValidationProblem();

        group.MapPost("/password-reset/redeem", async (
                PasswordResetRedeemRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new RedeemPasswordResetCommand(request.MobileOrEmail, request.Code, request.NewPassword),
                    cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("RedeemPasswordReset")
            .WithSummary("Redeem a password reset code and set a new password. No token: sign in normally next.")
            .Produces<RedeemPasswordResetResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    /// <summary>
    /// <paramref name="NoticeVersion"/> is which version of the consent notice
    /// the visitor was shown. Required, because DPDP section 6(7) makes a
    /// consent record that cannot say what was shown worth very little.
    /// </summary>
    public sealed record RegisterRequest(
        string TenantSlug,
        string FullName,
        string MobileOrEmail,
        string Password,
        IReadOnlyCollection<string>? ConsentedPurposes,
        string? NoticeVersion);

    public sealed record LoginRequest(
        string MobileOrEmail,
        string Password,
        Guid? CaptchaId = null,
        string? CaptchaAnswer = null,
        string? DeviceToken = null);

    public sealed record VerifySignInCodeRequest(string ChallengeToken, string Code, bool TrustDevice = false);

    public sealed record ResendSignInCodeRequest(string ChallengeToken);

    public sealed record RefreshRequest(string RefreshToken);

    /// <summary>
    /// <paramref name="Everywhere"/> ends every session for the account rather
    /// than only this one - "sign out on all my devices".
    /// </summary>
    public sealed record SignOutRequest(string RefreshToken, bool Everywhere = false);

    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    public sealed record PasswordResetRequest(string MobileOrEmail);

    public sealed record PasswordResetRedeemRequest(string MobileOrEmail, string Code, string NewPassword);
}
