namespace Sangam.AuditNotification.Application.Abstractions;

/// <summary>
/// Correlation id for one inbound request, threaded through logs so a single
/// user action can be followed across the gateway and every service it fans
/// out to.
/// </summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
}
