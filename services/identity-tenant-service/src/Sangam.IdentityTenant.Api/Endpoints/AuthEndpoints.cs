using MediatR;
using Sangam.IdentityTenant.Api.Extensions;
using Sangam.IdentityTenant.Application.Users;
using Sangam.IdentityTenant.Application.Users.Commands.Login;
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

        group.MapPost("/login", async (
                LoginRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new LoginCommand(request.MobileOrEmail, request.Password), cancellationToken);

                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithName("Login")
            .WithSummary("Common login. Returns a tenant-scoped token and the Samaaj to redirect to.")
            .Produces<LoginResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

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

    public sealed record LoginRequest(string MobileOrEmail, string Password);
}
