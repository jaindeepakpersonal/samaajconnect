using FluentValidation;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Domain.Competitions;
using Sangam.Competition.Domain.Entries;
using CompetitionAggregate = Sangam.Competition.Domain.Competitions.Competition;

namespace Sangam.Competition.Application.Competitions;

/// <summary>
/// The format fields creating and correcting a competition share. Every one is
/// optional on the wire, so a caller that predates them still works: a
/// create without them is <see cref="CompetitionFormat.Default"/>, a
/// correction without them leaves the format as it was.
/// </summary>
public interface IHasCompetitionFormat
{
    DateTimeOffset RegistrationStartAt { get; }
    int? RoundCount { get; }
    int? MaxMarks { get; }
    bool? AuditionRequired { get; }
    DateOnly? AuditionDate { get; }
    int? FinalistCount { get; }
}

public sealed class CompetitionFormatValidator : AbstractValidator<IHasCompetitionFormat>
{
    public CompetitionFormatValidator()
    {
        RuleFor(x => x.RoundCount)
            .InclusiveBetween(0, CompetitionFormat.MaxRounds)
            .When(x => x.RoundCount is not null)
            .WithMessage($"Rounds must be between 0 and {CompetitionFormat.MaxRounds}.");

        RuleFor(x => x.MaxMarks)
            .InclusiveBetween(1, CompetitionFormat.HighestMaxMarks)
            .When(x => x.MaxMarks is not null)
            .WithMessage($"Max marks must be between 1 and {CompetitionFormat.HighestMaxMarks}.");

        When(x => x.AuditionRequired == true, () =>
        {
            RuleFor(x => x.AuditionDate)
                .NotNull()
                .WithMessage("Give the audition date.");

            RuleFor(x => x.AuditionDate)
                .Must((x, date) => date >= DateOnly.FromDateTime(x.RegistrationStartAt.UtcDateTime))
                .When(x => x.AuditionDate is not null)
                .WithMessage("The audition cannot be before registration opens.");

            RuleFor(x => x.FinalistCount)
                .NotNull()
                .WithMessage("Say how many go through from the audition.")
                .InclusiveBetween(1, 500)
                .WithMessage("Between 1 and 500 go through from the audition.");
        });
    }
}

internal static class CompetitionFormatMapping
{
    /// <summary>The format a request asks for, filling anything it leaves out from <paramref name="current"/>.</summary>
    public static CompetitionFormat ToFormat(this IHasCompetitionFormat request, CompetitionFormat current) =>
        new(
            request.RoundCount ?? current.RoundCount,
            request.MaxMarks ?? current.MaxMarks,
            request.AuditionRequired ?? current.AuditionRequired,
            request.AuditionDate ?? current.AuditionDate,
            request.FinalistCount ?? current.FinalistCount);

    public static bool SaysAnythingAboutFormat(this IHasCompetitionFormat request) =>
        request.RoundCount is not null
        || request.MaxMarks is not null
        || request.AuditionRequired is not null
        || request.AuditionDate is not null
        || request.FinalistCount is not null;
}

/// <summary>Entering participants into rounds, and adding up who is winning.</summary>
internal static class CompetitionJudging
{
    /// <summary>
    /// Enters each member into each round, skipping anybody already in one -
    /// an organiser may have entered somebody by hand, and the unique index
    /// on (round, participant) would refuse the second.
    /// </summary>
    public static async Task<int> EnterAsync(
        IEntryRepository entries,
        Guid tenantId,
        IReadOnlyList<Round> rounds,
        IReadOnlyList<Guid> memberIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await entries.ListByRoundsAsync([.. rounds.Select(r => r.Id)], cancellationToken);
        var taken = existing.Select(e => (e.RoundId, e.ParticipantMemberId)).ToHashSet();
        var added = 0;

        foreach (var round in rounds)
        {
            foreach (var memberId in memberIds)
            {
                if (taken.Add((round.Id, memberId)))
                {
                    entries.Add(new Entry(tenantId, round.Id, memberId, now));
                    added++;
                }
            }
        }

        return added;
    }

    /// <summary>
    /// The standings now: each participant's total of their main-round
    /// averages, an unmarked entry counting nothing. Audition marks decide who
    /// takes part and never count towards winning.
    /// </summary>
    public static IReadOnlyList<Standing> Standings(
        CompetitionAggregate competition,
        IReadOnlyList<Entry> entries,
        IReadOnlyDictionary<Guid, double> averages)
    {
        var mainRoundIds = competition.MainRounds.Select(r => r.Id).ToHashSet();

        var totals = entries
            .Where(e => mainRoundIds.Contains(e.RoundId))
            .GroupBy(e => e.ParticipantMemberId)
            .Select(g => (
                MemberId: g.Key,
                Total: g.Sum(e => averages.GetValueOrDefault(e.Id)),
                Order: g.Min(e => e.CreatedAt).UtcTicks));

        return CompetitionScoring.Rank(totals);
    }
}
