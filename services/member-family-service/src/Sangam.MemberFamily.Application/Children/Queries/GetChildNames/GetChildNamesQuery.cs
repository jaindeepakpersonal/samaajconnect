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

        // Belt and braces, and honestly labelled as such.
        //
        // The global query filter already does this correctly - verified four
        // ways after an earlier claim in this file that it did not: against the
        // deployed stack with this line removed, against a three-tenant probe in
        // the test host with this line removed, and by the tests below passing
        // with it removed. There is no filter bug. The claim that there was one
        // came from reading results off a build that still had a deliberately
        // broken repository from a fault-injection experiment.
        //
        // The line stays because this read is shaped like the IDOR case
        // SECURITY-CHECKLIST.md is about: the ids come from another service
        // rather than from anything this one issued, and the answer is
        // children's data, which is the most sensitive thing the platform holds
        // (DPDP s.9). One comparison per row is a fair price there. It is not
        // compensating for anything.
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
