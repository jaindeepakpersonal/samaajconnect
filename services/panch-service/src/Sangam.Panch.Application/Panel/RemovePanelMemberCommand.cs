using FluentValidation;
using MediatR;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Application.Common;
using Sangam.Panch.Application.Elections;
using Sangam.Panch.Application.Security;
using Sangam.Panch.Domain.Elections;

namespace Sangam.Panch.Application.Panel;

/// <summary>
/// Takes one member off the Panch. The seat stays empty until the next election:
/// filling it by hand would be an appointment nobody voted for.
/// </summary>
/// <remarks>
/// Removing somebody who is not on the panel is success with nothing changed, so
/// a repeated click is not an error and not a second audit entry.
/// </remarks>
[RequiresPermission(PermissionKeys.PanchManage)]
public sealed record RemovePanelMemberCommand(Guid MemberId) : ICommand<PanelResponse>;

public sealed class RemovePanelMemberCommandValidator : AbstractValidator<RemovePanelMemberCommand>
{
    public RemovePanelMemberCommandValidator() => RuleFor(x => x.MemberId).NotEmpty();
}

public sealed class RemovePanelMemberCommandHandler(
    IPanelRepository panel,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<RemovePanelMemberCommand, Result<PanelResponse>>
{
    public async Task<Result<PanelResponse>> Handle(
        RemovePanelMemberCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<PanelResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var members = await panel.ListAsync(cancellationToken);
        var leaving = members.FirstOrDefault(m => m.MemberId == command.MemberId);

        if (leaving is not null)
        {
            var remaining = members.Where(m => m.Id != leaving.Id).Select(m => m.MemberId).ToList();

            leaving.AnnounceRemoval(remaining, actorId, clock.UtcNow);
            panel.Remove(leaving);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            members = [.. members.Where(m => m.Id != leaving.Id)];
        }

        return Result.Success(new PanelResponse(
            [.. members.Select(m => new PanelMemberResponse(m.MemberId, m.ElectionId, m.SeatedAt))],
            PanchElection.SeatCount,
            members.Any(m => m.MemberId == actorId)));
    }
}
