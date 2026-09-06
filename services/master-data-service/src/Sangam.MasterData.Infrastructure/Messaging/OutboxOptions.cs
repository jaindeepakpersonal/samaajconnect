namespace Sangam.MasterData.Infrastructure.Messaging;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollIntervalSeconds { get; set; } = 5;

    public int BatchSize { get; set; } = 50;

    /// <summary>After this many failed publishes a row is left for an operator.</summary>
    public int MaxAttempts { get; set; } = 10;
}
