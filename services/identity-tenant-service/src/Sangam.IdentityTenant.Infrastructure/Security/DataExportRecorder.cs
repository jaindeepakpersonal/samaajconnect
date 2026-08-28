using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Infrastructure.Persistence;

namespace Sangam.IdentityTenant.Infrastructure.Security;

/// <summary>
/// Writes the export event straight to the outbox, on its own scope. See
/// <see cref="IDataExportRecorder"/> for why it cannot ride on the request's
/// unit of work.
/// </summary>
public sealed class DataExportRecorder(
    IServiceScopeFactory scopeFactory,
    IDateTimeProvider clock,
    ILogger<DataExportRecorder> logger)
    : IDataExportRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(
        Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityTenantDbContext>();

            var occurredAt = clock.UtcNow;

            dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Topic = "identity.member-data.exported.v1",
                Type = "Sangam.IdentityTenant.MemberDataExported",

                // Ids and a timestamp. The event says an export happened, never
                // what was in it - the audit log stores payloads verbatim, and
                // putting the export's contents there would make the record of
                // the copy a second copy.
                Payload = JsonSerializer.Serialize(
                    new { userId, tenantId, occurredAt }, JsonOptions),
                OccurredAt = occurredAt,
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Never fails the export. A member's right to a copy of their data
            // does not depend on our bookkeeping working.
            logger.LogError(
                exception, "Could not record the data export for {UserId}", userId);
        }
    }
}
