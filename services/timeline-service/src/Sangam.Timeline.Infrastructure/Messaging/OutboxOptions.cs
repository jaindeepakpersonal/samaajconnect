namespace Sangam.Timeline.Infrastructure.Messaging;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollIntervalSeconds { get; set; } = 5;

    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// After this many failed publishes a row stops being retried on the normal
    /// cycle and is left for an operator. Retrying a poison message forever
    /// blocks every row behind it.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;
}
