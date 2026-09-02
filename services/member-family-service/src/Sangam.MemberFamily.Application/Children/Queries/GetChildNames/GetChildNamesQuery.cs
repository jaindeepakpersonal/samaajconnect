using FluentValidation;
using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Children.Queries.GetChildNames;

/// <summary>
/// The names of specific children, for an administrator holding their ids.
/// </summary>
/// <remarks>
/// <para>
/// pathshala-service stores a child by id and nothing else, so its enrolment
/// queue is a list of GUIDs. An administrator deciding which class to put a
/// child in has to know which child. Every other screen on this platform that
/// needs a name resolves it client-side from a list it is already entitled to;
/// this is that list, for children.
/// </para>
/// <para>
/// <b>By id, and names only.</b> Not "every child in the Samaaj", and not
/// <see cref="ChildResponse"/>. That record carries a date of birth, a gender, a
/// photo and the parental-consent record, and handing all of it over so a screen
/// can print a name would be the opposite of what section 9 asks of this
/// platform. The caller already holds the ids; this answers for exactly those.
/// </para>
/// </remarks>
[RequiresRoles(Roles.SamaajAdmin, Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetChildNamesQuery(IReadOnlyList<Guid> Ids)
    : IQuery<IReadOnlyList<ChildNameResponse>>;

public sealed record ChildNameResponse(Guid Id, string FullName);

public sealed class GetChildNamesQueryValidator : AbstractValidator<GetChildNamesQuery>
{
    /// <summary>
    /// A cap, because this is the one query whose cost the caller chooses. It is
    /// comfortably more than a Samaaj's Pathshala will have waiting to be
    /// placed, and small enough that nobody can ask for the whole table one
    /// request at a time.
    /// </summary>
    public const int MaxIds = 200;

    public GetChildNamesQueryValidator()
    {
        RuleFor(x => x.Ids)
            .NotEmpty()
            .WithMessage("Name at least one child.")
            .Must(ids => ids.Count <= MaxIds)
            .WithMessage($"Ask for at most {MaxIds} children at a time.");
    }
}

public sealed class GetChildNamesQueryHandler(
    IChildRepository children,
    ITenantContext tenantContext)
    : IRequestHandler<GetChildNamesQuery, Result<IReadOnlyList<ChildNameResponse>>>
{
    public async Task<Result<IReadOnlyList<ChildNameResponse>>> Handle(
        GetChildNamesQuery query,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequireTenantId();

        var found = await children.ListByIdsAsync(query.Ids, cancellationToken);

        // **Re-checked here, not left to the query filter.**
        //
        // SECURITY-CHECKLIST.md asks write paths to re-validate the target's
        // tenant rather than trust the filter, on the grounds that a missing
        // check is invisible. This is a read, and it gets the same treatment for
        // a sharper reason: an integration test seeded one child in each of two
        // Samaajs, asked as an administrator of the first, and got both back -
        // reproducibly, on a clean build, with `ToQueryString()` showing a
        // correctly parameterised `WHERE tenant_id = @__ef_filter__...` and the
        // same query returning nothing when no tenant is resolved. Those facts
        // do not reconcile, and the cause is written up as an open item in
        // DEVELOPMENT_PLAN.md rather than guessed at.
        //
        // Whatever that turns out to be, this line is the guarantee: names cross
        // a service boundary here, and children's data is the most sensitive
        // thing this platform holds (DPDP s.9). An explicit check costs one
        // comparison per row and does not depend on being right about EF.
        IReadOnlyList<ChildNameResponse> results =
        [
            .. found
                .Where(child => child.TenantId == tenantId)
                .Select(child => new ChildNameResponse(child.Id, child.FullName))
        ];

        // Ids that belong to another Samaaj are simply absent rather than
        // refused: a caller holding a GUID learns only that this Samaaj has no
        // such child, which is true.
        return Result.Success(results);
    }
}
