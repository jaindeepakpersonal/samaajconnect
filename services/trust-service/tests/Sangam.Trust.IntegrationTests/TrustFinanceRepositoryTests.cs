using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.IntegrationEvents;
using Sangam.Trust.Domain.Collections;
using Sangam.Trust.Domain.Donations;
using Sangam.Trust.Infrastructure.Persistence;
using Xunit;

namespace Sangam.Trust.IntegrationTests;

/// <summary>
/// The donation, expense and collection repositories against a real Postgres -
/// the two guarantees that live in the database (one receipt number per
/// donation, one Collection per source) and the tenant filter.
/// </summary>
/// <remarks>
/// The receipt test takes each number on its own scope, connection and
/// transaction, the way concurrent requests do, so only the advisory lock in
/// <c>ReserveNextReceiptNumberAsync</c> can make it pass: without the lock two
/// transactions read the same MAX and the unique index refuses the second.
/// </remarks>
public sealed class TrustFinanceRepositoryTests(TrustApiFactory factory)
    : IClassFixture<TrustApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Concurrent_collections_each_get_their_own_receipt_number()
    {
        var trustId = Guid.NewGuid();
        var pledged = new List<Guid>();

        for (var i = 0; i < 10; i++)
        {
            pledged.Add(await PledgeAsync(TenantA, trustId));
        }

        // Ten requests marking ten donations collected at the same moment.
        var numbers = await Task.WhenAll(pledged.Select(CollectAsync));

        numbers.Should().BeEquivalentTo(Enumerable.Range(1, 10), "every receipt names exactly one donation");

        var stored = await factory.WithDbContextAsync(db => db.Donations
            .IgnoreQueryFilters()
            .Where(d => d.TrustId == trustId)
            .Select(d => d.ReceiptNumber)
            .ToListAsync());

        stored.Should().OnlyHaveUniqueItems().And.HaveCount(10);
    }

    [Fact]
    public async Task Each_Trust_numbers_its_own_receipts_from_one()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await CollectAsync(await PledgeAsync(TenantA, first));
        await CollectAsync(await PledgeAsync(TenantA, first));

        var number = await CollectAsync(await PledgeAsync(TenantA, second));

        number.Should().Be(1);
    }

    [Fact]
    public async Task The_database_refuses_a_second_donation_with_the_same_receipt_number()
    {
        // The backstop for a reservation made outside a transaction, where the
        // advisory lock is released the moment it is taken.
        var trustId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var first = Pledge(TenantA, trustId);
        first.Collect(1, Guid.NewGuid(), now);
        var second = Pledge(TenantA, trustId);
        second.Collect(1, Guid.NewGuid(), now);

        await InsertAsync(first);

        var duplicate = async () => await InsertAsync(second);

        await duplicate.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Opening_a_Collection_twice_for_one_source_is_a_no_op()
    {
        var trustId = Guid.NewGuid();
        var boliId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var scope = factory.Services.CreateAsyncScope();
        var collections = scope.ServiceProvider.GetRequiredService<ICollectionRepository>();

        var opened = await collections.TryOpenAsync(
            Collection.Open(TenantA, trustId, CollectionSourceType.BoliResult, boliId, Guid.NewGuid(), 1_500_000, now));
        var again = await collections.TryOpenAsync(
            Collection.Open(TenantA, trustId, CollectionSourceType.BoliResult, boliId, Guid.NewGuid(), 1_500_000, now));

        opened.Should().BeTrue();
        again.Should().BeFalse("a redelivered event must not open a second Collection");

        var rows = await factory.WithDbContextAsync(db =>
            db.Collections.IgnoreQueryFilters().CountAsync(c => c.SourceId == boliId));
        var dueEvents = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.CountAsync(m => m.Topic == "trust.collection.due.v1"));

        rows.Should().Be(1);
        dueEvents.Should().Be(1, "the refused insert's event is discarded with it");
    }

    [Fact]
    public async Task A_published_Boli_result_opens_one_Collection_for_its_locality_Trust_however_often_it_arrives()
    {
        var localityId = Guid.NewGuid();
        var trust = Domain.Trusts.Trust.Create(TenantA, localityId, "Hiran Magri Jain Trust", DateTimeOffset.UtcNow);

        await factory.WithDbContextAsync(async db =>
        {
            db.Trusts.Add(trust);
            return await db.SaveChangesAsync();
        });

        var boliId = Guid.NewGuid();
        var winner = Guid.NewGuid();
        var envelope = new IntegrationEventEnvelope(
            Guid.NewGuid(),
            TenantA,
            ConsumeIntegrationEventCommandHandler.BoliResultPublishedTopic,
            "BoliResultPublished",
            JsonSerializer.Serialize(new
            {
                boliId,
                winningMemberId = winner,
                amount = 2_100_000L,
                localityId,
            }),
            DateTimeOffset.UtcNow);

        var first = await ConsumeAsync(envelope);
        var redelivered = await ConsumeAsync(envelope);

        first.Should().Be(1);
        redelivered.Should().Be(0);

        var collection = await factory.WithDbContextAsync(db =>
            db.Collections.IgnoreQueryFilters().SingleAsync(c => c.SourceId == boliId));

        collection.TrustId.Should().Be(trust.Id);
        collection.TenantId.Should().Be(TenantA);
        collection.Amount.Should().Be(2_100_000L);
        collection.Status.Should().Be(CollectionStatus.Due);
        collection.PayerMemberId.Should().Be(winner, "the winner is who the Trust collects from");
    }

    [Fact]
    public async Task A_Samaaj_sees_only_its_own_donations_and_expenses()
    {
        var trustId = Guid.NewGuid();
        var ours = await PledgeAsync(TenantA, trustId);
        await PledgeAsync(TenantB, trustId);

        await InsertAsync(Domain.Expenses.Expense.Record(
            TenantB, trustId, "Maintenance", "Roof repair", 500_000, null, Guid.NewGuid(), DateTimeOffset.UtcNow));

        await using var scope = factory.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var request = new DefaultHttpContext();
        request.Request.Headers["X-Tenant-Id"] = TenantA.ToString();
        accessor.HttpContext = request;

        try
        {
            var donations = scope.ServiceProvider.GetRequiredService<IDonationRepository>();
            var expenses = scope.ServiceProvider.GetRequiredService<IExpenseRepository>();

            var listed = await donations.ListByTrustAsync(trustId, null);

            listed.Select(d => d.Id).Should().Equal(ours);
            (await expenses.ListByTrustAsync(trustId, null)).Should().BeEmpty();
            (await donations.ListByTrustAsync(trustId, DonationStatus.Collected)).Should().BeEmpty();
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    // ---- Helpers -------------------------------------------------------------

    private static Donation Pledge(Guid tenantId, Guid trustId) =>
        Donation.Record(
            tenantId, trustId, null, "Shri Mahendra Jain", null, 510_000, "Temple fund",
            DonationMode.Cash, Guid.NewGuid(), DateTimeOffset.UtcNow);

    private async Task<Guid> PledgeAsync(Guid tenantId, Guid trustId)
    {
        var donation = Pledge(tenantId, trustId);

        await InsertAsync(donation);

        return donation.Id;
    }

    private async Task InsertAsync(object entity)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrustDbContext>();

        db.Add(entity);

        await db.SaveChangesAsync();
    }

    /// <summary>What MarkDonationCollectedCommand does, inside the transaction TransactionBehavior opens.</summary>
    private async Task<int> CollectAsync(Guid donationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrustDbContext>();
        var donations = scope.ServiceProvider.GetRequiredService<IDonationRepository>();

        await using var transaction = await db.Database.BeginTransactionAsync();

        // No request, so no tenant: read past the filter the handler's
        // GetByIdAsync would apply for a real caller.
        var donation = await db.Donations.IgnoreQueryFilters().SingleAsync(d => d.Id == donationId);
        var number = await donations.ReserveNextReceiptNumberAsync(donation.TrustId);

        donation.Collect(number, Guid.NewGuid(), DateTimeOffset.UtcNow);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return number;
    }

    private async Task<int> ConsumeAsync(IntegrationEventEnvelope envelope)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.Send(new ConsumeIntegrationEventCommand(envelope));

        result.IsSuccess.Should().BeTrue();

        return result.Value;
    }
}
