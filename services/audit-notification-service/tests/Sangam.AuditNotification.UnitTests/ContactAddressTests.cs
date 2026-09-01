using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sangam.AuditNotification.Application.Notifications.Delivery;
using Sangam.AuditNotification.Domain.Notifications;
using Sangam.AuditNotification.Infrastructure.Notifications;
using Xunit;

namespace Sangam.AuditNotification.UnitTests;

public sealed class ContactAddressTests
{
    [Theory]
    [InlineData("ravi@example.com")]
    [InlineData("ravi.shah+samaaj@mail.example.co.in")]
    [InlineData("  RAVI@EXAMPLE.COM  ")]
    public void An_email_address_is_reachable_by_email(string value) =>
        ContactAddress.ChannelFor(value).Should().Be(NotificationChannel.Email);

    [Theory]
    [InlineData("+919876543210")]
    [InlineData("9876543210")]
    [InlineData("+91 98765 43210")]
    [InlineData("(022) 2222-3333")]
    public void A_mobile_number_is_reachable_by_text(string value) =>
        ContactAddress.ChannelFor(value).Should().Be(NotificationChannel.Sms);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ravi-shah")]
    [InlineData("ravi@localhost")]      // no dot in the domain
    [InlineData("@example.com")]        // no local part
    [InlineData("ravi@")]               // no domain
    [InlineData("a@b@example.com")]     // two at signs
    [InlineData("ravi shah@example.com")]
    [InlineData("2026")]                // a year, not a number anyone answers
    [InlineData("1234567")]             // too few digits to be a phone number
    [InlineData("1234567890123456")]    // more digits than E.164 allows
    public void Anything_it_cannot_classify_is_refused_rather_than_guessed(string? value) =>
        ContactAddress.ChannelFor(value).Should().BeNull();
}

public sealed class ContactRedactionTests
{
    [Theory]
    [InlineData("ravi@example.com", "r***@example.com")]
    [InlineData("r@example.com", "***@example.com")]
    [InlineData("+919876543210", "***210")]
    [InlineData("98765 43210", "***210")]
    [InlineData(null, "(none)")]
    [InlineData("", "(none)")]
    public void An_address_is_shortened_to_something_a_log_can_hold(string? value, string expected) =>
        ContactRedaction.Redact(value).Should().Be(expected);

    [Fact]
    public void A_very_short_number_gives_up_its_digits_entirely()
    {
        // Keeping "the last three of four" would be keeping nearly all of it.
        ContactRedaction.Redact("123").Should().Be("***");
    }

    [Fact]
    public void The_local_part_of_an_email_never_survives_intact()
    {
        var redacted = ContactRedaction.Redact("deepak.jain@example.com");

        redacted.Should().NotContain("deepak");
        redacted.Should().NotContain("jain");
        redacted.Should().EndWith("@example.com");
    }
}

public sealed class LoggingNotificationChannelTests
{
    private static readonly OutboundMessage Message = new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        NotificationChannel.Email,
        "deepak.jain@example.com",
        "Welcome to your Samaaj",
        "Your membership is active, Deepak.");

    private static (LoggingNotificationChannel Channel, CapturingLogger Log) Build(bool revealContent)
    {
        var log = new CapturingLogger();

        var options = Options.Create(new NotificationDeliveryOptions
        {
            Logging = new LoggingChannelOptions { RevealContent = revealContent },
        });

        return (new LoggingNotificationChannel(NotificationChannel.Email, options, log), log);
    }

    [Fact]
    public async Task It_reports_delivered_so_features_can_be_built_on_it()
    {
        var (channel, _) = Build(revealContent: false);

        var result = await channel.DeliverAsync(Message);

        result.Status.Should().Be(DeliveryStatus.Delivered);
    }

    [Fact]
    public async Task It_says_in_the_log_that_nothing_was_actually_sent()
    {
        var (channel, log) = Build(revealContent: false);

        await channel.DeliverAsync(Message);

        log.Lines.Should().ContainSingle().Which.Should().Contain("NOT SENT");
    }

    [Fact]
    public async Task By_default_neither_the_address_nor_the_body_reaches_the_log()
    {
        // The point of the default. A log of both is a copy of personal data in
        // the one place erasure cannot reach.
        var (channel, log) = Build(revealContent: false);

        await channel.DeliverAsync(Message);

        var line = log.Lines.Single();

        line.Should().NotContain("deepak.jain@example.com");
        line.Should().NotContain("Your membership is active");
        line.Should().Contain("d***@example.com");

        // The title is not personal - it is the same for every recipient - and
        // an operator needs it to tell which message this was.
        line.Should().Contain("Welcome to your Samaaj");
    }

    [Fact]
    public async Task Revealing_content_is_what_makes_a_code_readable_off_a_local_console()
    {
        var (channel, log) = Build(revealContent: true);

        await channel.DeliverAsync(Message);

        var line = log.Lines.Single();

        line.Should().Contain("deepak.jain@example.com");
        line.Should().Contain("Your membership is active, Deepak.");
    }
}

/// <summary>Captures formatted log lines so a test can assert on what was written.</summary>
internal sealed class CapturingLogger : ILogger<LoggingNotificationChannel>
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Lines.Add(formatter(state, exception));
}
