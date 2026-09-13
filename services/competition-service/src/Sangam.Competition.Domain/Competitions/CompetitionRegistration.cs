namespace Sangam.Competition.Domain.Competitions;

/// <summary>One member's registration in one competition. Owned by <see cref="Competition"/>.</summary>
public sealed class CompetitionRegistration
{
    public Guid Id { get; private set; }
    public Guid CompetitionId { get; private set; }
    public Guid MemberId { get; private set; }
    public RegistrationStatus Status { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }

    private CompetitionRegistration() { }   // EF Core

    internal CompetitionRegistration(Guid competitionId, Guid memberId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CompetitionId = competitionId;
        MemberId = memberId;
        Status = RegistrationStatus.Registered;
        RegisteredAt = now;
    }

    internal void Withdraw(DateTimeOffset now)
    {
        Status = RegistrationStatus.Withdrawn;
        WithdrawnAt = now;
    }

    internal void Reactivate(DateTimeOffset now)
    {
        Status = RegistrationStatus.Registered;
        RegisteredAt = now;
        WithdrawnAt = null;
    }
}

public enum RegistrationStatus
{
    Registered = 1,
    Withdrawn = 2,
}
