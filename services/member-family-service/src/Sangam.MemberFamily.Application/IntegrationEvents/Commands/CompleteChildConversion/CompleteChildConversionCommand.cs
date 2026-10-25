using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.IntegrationEvents.Commands.CompleteChildConversion;

/// <summary>
/// Closes the conversion loop. identity-tenant-service has created and
/// activated the account, so the child record can finally be marked Converted
/// and linked to it - and the new adult gets the member profile every other
/// member has, and a place in the household they grew up in.
/// </summary>
[InternalRequest]
public sealed record CompleteChildConversionCommand(IntegrationEventEnvelope Event)
    : ICommand<CompleteChildConversionResult>;

public sealed record CompleteChildConversionResult(
    bool Completed,
    bool ProfileCreated,
    Guid? ChildProfileId);
