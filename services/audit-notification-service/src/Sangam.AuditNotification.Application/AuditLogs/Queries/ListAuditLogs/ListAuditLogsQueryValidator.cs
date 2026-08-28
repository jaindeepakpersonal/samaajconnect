using FluentValidation;

namespace Sangam.AuditNotification.Application.AuditLogs.Queries.ListAuditLogs;

public sealed class ListAuditLogsQueryValidator : AbstractValidator<ListAuditLogsQuery>
{
    public ListAuditLogsQueryValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 200);
        RuleFor(x => x.Action).MaximumLength(100);
        RuleFor(x => x.EntityName).MaximumLength(100);
    }
}
