using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.Matrimony.Application.IntegrationEvents;
using Sangam.Matrimony.Application.IntegrationEvents.Commands.EraseMatrimonyData;
using Sangam.Matrimony.Application.Security;
using Xunit;

namespace Sangam.Matrimony.IntegrationTests;

/// <summary>
/// Matrimony biodata and proposals through their endpoints, against a real
/// database. The tenant query filter is applied by the DbContext rather than
/// by any handler, and the outbox guarantee is transactional - neither can be
/// shown against a substituted repository.
/// </summary>
public sealed class MatrimonyEndpointsTests(MatrimonyApiFactory factory)
    : IClassFixture<MatrimonyApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid Ravi = Guid.NewGuid();
    private static readonly Guid Meera = Guid.NewGuid();
    private static readonly Guid Neha = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Member(Guid userId, Guid? tenantId = null) =>
        factory.CreateClientAs(userId, tenantId ?? TenantA, [Roles.Member], [PermissionKeys.MembersRead]);

    private static object OptInBody(
        string fullName = "Ravi Shah",
        string gender = "Male",
        string dateOfBirth = "1995-04-02",
        bool consentGiven = true,
        string noticeVersion = "2026-09-12.1",
        string? gautraId = null) => new
    {
        fullName,
        gender,
        dateOfBirth,
        gautraId,
        professionId = (Guid?)null,
        qualificationId = (Guid?)null,
        cityId = (Guid?)null,
        localityId = (Guid?)null,
        heightCm = 175,
        maritalStatusId = (Guid?)null,
        diet = "JainVegetarian",
        familyType = "Nuclear",
        fatherOccupation = "Business",
        motherOccupation = "Homemaker",
        siblingsCount = 1,
        manglik = false,
        horoscopeNotes = (string?)null,
        aboutMe = "Loves cricket.",
        preferredAgeMin = 24,
        preferredAgeMax = 30,
        partnerPreferenceNotes = (string?)null,
        consentGiven,
        noticeVersion,
    };

    private async Task OptInAsync(Guid memberId, Guid? tenantId = null, string fullName = "Ravi Shah", string gender = "Male")
    {
        var response = await Member(memberId, tenantId)
            .PostAsJsonAsync("/v1/matrimony/biodata/me/opt-in", OptInBody(fullName, gender));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- Consent notice and opt-in ---------------------------------------

    [Fact]
    public async Task The_consent_notice_is_readable_before_opting_in()
    {
        var notice = await Member(Ravi).GetFromJsonAsync<JsonElement>("/v1/matrimony/consent-notice");

        notice.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        notice.GetProperty("summary").GetString().Should().Contain("mobile");
    }

    [Fact]
    public async Task Opting_in_without_consent_is_a_validation_problem()
    {
        var response = await Member(Ravi)
            .PostAsJsonAsync("/v1/matrimony/biodata/me/opt-in", OptInBody(consentGiven: false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Opting_in_round_trips_every_field()
    {
        await OptInAsync(Ravi);

        var mine = await Member(Ravi).GetFromJsonAsync<JsonElement>("/v1/matrimony/biodata/me");

        var biodata = mine.GetProperty("biodata");
        biodata.GetProperty("fullName").GetString().Should().Be("Ravi Shah");
        biodata.GetProperty("heightCm").GetInt32().Should().Be(175);
        biodata.GetProperty("diet").GetString().Should().Be("JainVegetarian");
        biodata.GetProperty("aboutMe").GetString().Should().Be("Loves cricket.");
        mine.GetProperty("isActive").GetBoolean().Should().BeTrue();

        // The owner's own view carries the real date of birth, unlike
        // BiodataResponse's derived Age - an edit form needs it back, or the
        // required field would have to be retyped on every edit.
        mine.GetProperty("dateOfBirth").GetString().Should().Be("1995-04-02");
    }

    [Fact]
    public async Task A_member_under_18_cannot_opt_in()
    {
        var response = await Member(Ravi)
            .PostAsJsonAsync("/v1/matrimony/biodata/me/opt-in", OptInBody(dateOfBirth: DateOnly.FromDateTime(
                DateTime.UtcNow.AddYears(-17)).ToString("yyyy-MM-dd")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A member whose base profile has never had a date of birth set (the
    /// common case for a fresh registration) sees member-portal's opt-in form
    /// prefill this field as an empty string - this is the request that
    /// actually goes over the wire in that case, not a contrived one. It must
    /// come back as a validation problem, not the unhandled 500 an empty
    /// string against a DateOnly-typed request field used to produce before
    /// DateOfBirth became a string here, parsed and validated like Gender and
    /// Diet already are.
    /// </summary>
    [Fact]
    public async Task An_empty_date_of_birth_is_a_validation_problem_not_a_crash()
    {
        var response = await Member(Ravi)
            .PostAsJsonAsync("/v1/matrimony/biodata/me/opt-in", OptInBody(dateOfBirth: ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unparseable_date_of_birth_is_a_validation_problem_not_a_crash()
    {
        var response = await Member(Ravi)
            .PostAsJsonAsync("/v1/matrimony/biodata/me/opt-in", OptInBody(dateOfBirth: "not-a-date"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The literal text "null" - what a `&lt;select&gt;` bound with `[value]`
    /// instead of `[ngValue]` sends for its "Not specified" option, since
    /// `[value]` always stringifies onto the DOM. A Guid?-typed request field
    /// crashed on this exactly the way DateOnly crashed on an empty string,
    /// before GautraId became a string here, parsed and validated the same
    /// way.
    /// </summary>
    [Fact]
    public async Task The_literal_text_null_for_a_lookup_id_is_a_validation_problem_not_a_crash()
    {
        var response = await Member(Ravi)
            .PostAsJsonAsync("/v1/matrimony/biodata/me/opt-in", OptInBody(gautraId: "null"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_real_lookup_id_can_be_cleared_back_to_not_specified()
    {
        await OptInAsync(Ravi);

        var response = await Member(Ravi)
            .PostAsJsonAsync("/v1/matrimony/biodata/me/opt-in", OptInBody(gautraId: null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var mine = await Member(Ravi).GetFromJsonAsync<JsonElement>("/v1/matrimony/biodata/me");
        mine.GetProperty("biodata").GetProperty("gautraId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Nobody_has_opted_in_yet_reads_as_204_not_an_error()
    {
        var response = await Member(Ravi).GetAsync("/v1/matrimony/biodata/me");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---- Search -----------------------------------------------------------

    [Fact]
    public async Task Search_finds_an_opted_in_candidate_and_excludes_the_caller()
    {
        await OptInAsync(Ravi, fullName: "Ravi Shah", gender: "Male");
        await OptInAsync(Meera, fullName: "Meera Shah", gender: "Female");

        var results = await Member(Ravi).GetFromJsonAsync<JsonElement>("/v1/matrimony/biodata/search");

        var names = results.EnumerateArray().Select(r => r.GetProperty("fullName").GetString()).ToList();

        names.Should().Contain("Meera Shah");
        names.Should().NotContain("Ravi Shah");
    }

    [Fact]
    public async Task Search_excludes_a_withdrawn_profile()
    {
        await OptInAsync(Ravi);
        await OptInAsync(Meera, fullName: "Meera Shah", gender: "Female");

        (await Member(Meera).DeleteAsync("/v1/matrimony/biodata/me/consent"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var results = await Member(Ravi).GetFromJsonAsync<JsonElement>("/v1/matrimony/biodata/search");

        results.EnumerateArray().Select(r => r.GetProperty("fullName").GetString())
            .Should().NotContain("Meera Shah");
    }

    [Fact]
    public async Task A_member_in_another_Samaaj_is_not_found()
    {
        await OptInAsync(Ravi, TenantB, "Ravi In Samaaj B", "Male");

        var response = await Member(Meera, TenantA).GetAsync($"/v1/matrimony/biodata/{Ravi}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Proposals ----------------------------------------------------------

    [Fact]
    public async Task Sending_a_proposal_to_someone_not_opted_in_is_not_found()
    {
        var response = await Member(Ravi).PostAsJsonAsync(
            "/v1/matrimony/proposals", new { toMemberId = Meera, message = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_sent_proposal_can_be_accepted()
    {
        await OptInAsync(Ravi, fullName: "Ravi Shah", gender: "Male");
        await OptInAsync(Meera, fullName: "Meera Shah", gender: "Female");

        var sendResponse = await Member(Ravi).PostAsJsonAsync(
            "/v1/matrimony/proposals", new { toMemberId = Meera, message = "Would love to connect." });
        sendResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var sent = await sendResponse.Content.ReadFromJsonAsync<JsonElement>();
        var proposalId = sent.GetProperty("id").GetGuid();

        var respondResponse = await Member(Meera).PostAsJsonAsync(
            $"/v1/matrimony/proposals/{proposalId}/respond", new { accept = true });
        respondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var responded = await respondResponse.Content.ReadFromJsonAsync<JsonElement>();
        responded.GetProperty("status").GetString().Should().Be("Accepted");

        var sentList = await Member(Ravi).GetFromJsonAsync<JsonElement>("/v1/matrimony/proposals/sent");
        sentList.EnumerateArray().Single().GetProperty("status").GetString().Should().Be("Accepted");

        var receivedList = await Member(Meera).GetFromJsonAsync<JsonElement>("/v1/matrimony/proposals/received");
        receivedList.EnumerateArray().Single().GetProperty("otherParty").GetProperty("fullName")
            .GetString().Should().Be("Ravi Shah");
    }

    [Fact]
    public async Task Only_the_recipient_may_respond()
    {
        await OptInAsync(Ravi, fullName: "Ravi Shah", gender: "Male");
        await OptInAsync(Meera, fullName: "Meera Shah", gender: "Female");

        var sendResponse = await Member(Ravi).PostAsJsonAsync(
            "/v1/matrimony/proposals", new { toMemberId = Meera, message = (string?)null });
        var proposalId = (await sendResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await Member(Ravi).PostAsJsonAsync(
            $"/v1/matrimony/proposals/{proposalId}/respond", new { accept = true });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_sender_can_withdraw_a_pending_proposal()
    {
        await OptInAsync(Ravi, fullName: "Ravi Shah", gender: "Male");
        await OptInAsync(Meera, fullName: "Meera Shah", gender: "Female");

        var sendResponse = await Member(Ravi).PostAsJsonAsync(
            "/v1/matrimony/proposals", new { toMemberId = Meera, message = (string?)null });
        var proposalId = (await sendResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var withdrawResponse = await Member(Ravi).PostAsync(
            $"/v1/matrimony/proposals/{proposalId}/withdraw", null);
        withdrawResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var sentList = await Member(Ravi).GetFromJsonAsync<JsonElement>("/v1/matrimony/proposals/sent");
        sentList.EnumerateArray().Single().GetProperty("status").GetString().Should().Be("Withdrawn");
    }

    [Fact]
    public async Task A_second_proposal_while_one_is_pending_is_refused()
    {
        await OptInAsync(Ravi, fullName: "Ravi Shah", gender: "Male");
        await OptInAsync(Meera, fullName: "Meera Shah", gender: "Female");

        (await Member(Ravi).PostAsJsonAsync(
            "/v1/matrimony/proposals", new { toMemberId = Meera, message = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await Member(Ravi).PostAsJsonAsync(
            "/v1/matrimony/proposals", new { toMemberId = Meera, message = (string?)null });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_declined_proposal_can_be_sent_again()
    {
        await OptInAsync(Ravi, fullName: "Ravi Shah", gender: "Male");
        await OptInAsync(Meera, fullName: "Meera Shah", gender: "Female");

        var first = await Member(Ravi).PostAsJsonAsync(
            "/v1/matrimony/proposals", new { toMemberId = Meera, message = (string?)null });
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await Member(Meera).PostAsJsonAsync($"/v1/matrimony/proposals/{firstId}/respond", new { accept = false });

        var second = await Member(Ravi).PostAsJsonAsync(
            "/v1/matrimony/proposals", new { toMemberId = Meera, message = (string?)null });

        second.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- Tenant isolation -----------------------------------------------

    [Fact]
    public async Task A_member_of_another_Samaaj_cannot_propose_to_a_candidate_here()
    {
        await OptInAsync(Ravi, TenantA, "Ravi Shah", "Male");

        var response = await Member(Neha, TenantB).PostAsJsonAsync(
            "/v1/matrimony/proposals", new { toMemberId = Ravi, message = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Erasure ------------------------------------------------------------

    [Fact]
    public async Task Account_erasure_removes_biodata_and_de_identifies_proposals()
    {
        await OptInAsync(Ravi, fullName: "Ravi Shah", gender: "Male");
        await OptInAsync(Meera, fullName: "Meera Shah", gender: "Female");

        var sendResponse = await Member(Ravi).PostAsJsonAsync(
            "/v1/matrimony/proposals", new { toMemberId = Meera, message = "A private note." });
        var proposalId = (await sendResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var envelope = new IntegrationEventEnvelope(
            Guid.NewGuid(), TenantA, "identity.user.erased.v1", "UserErasedDomainEvent",
            $$"""{"userId":"{{Ravi}}","tenantId":"{{TenantA}}"}""", DateTimeOffset.UtcNow);

        var result = await factory.SendAsync(new EraseMatrimonyDataCommand(envelope));

        result.IsSuccess.Should().BeTrue();

        var stillMine = await factory.WithDbContextAsync(db =>
            db.BiodataProfiles.IgnoreQueryFilters().SingleAsync(p => p.Id == Ravi));
        stillMine.FullName.Should().Be("Erased member");
        stillMine.IsActive.Should().BeFalse();

        var proposal = await factory.WithDbContextAsync(db =>
            db.Proposals.IgnoreQueryFilters().SingleAsync(p => p.Id == proposalId));
        proposal.Message.Should().BeNull();

        // Meera's own received-proposal history survives the other party's erasure.
        var received = await Member(Meera).GetFromJsonAsync<JsonElement>("/v1/matrimony/proposals/received");
        received.EnumerateArray().Should().ContainSingle();
    }
}
