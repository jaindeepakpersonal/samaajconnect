namespace Sangam.IdentityTenant.Domain.Tenants;

/// <summary>
/// The modules a Samaaj can switch on, and the only values
/// <see cref="Tenant.EnabledModules"/> may contain.
/// </summary>
/// <remarks>
/// A closed list rather than free text, because of what an unrecognised value
/// does. The gateway gates a route by looking for its module key in this
/// collection and answers 404 when it is absent (`ModuleGateMiddleware`), so a
/// Samaaj created with "pathshaala" would have its Pathshala routes disappear
/// with no error anywhere - the typo and a deliberate switch-off are the same
/// request. Validating on the way in turns that into a 400 at the one moment
/// someone is in a position to fix it.
///
/// The keys are the platform's, not a Samaaj's: they name features the codebase
/// has, so they change when a service ships, not when an admin edits a form.
/// Routes with no module key at all - identity, audit, notifications - are
/// platform infrastructure and are never gated.
///
/// Adding a module means adding a key here, a route block in `gateway/` with
/// matching `Metadata.module`, and a row in the table below.
///
/// <c>scripts/module-keys.sh</c> checks that, and CI runs it. It reads all
/// three lists from the files that own them, so this class stays the source of
/// truth and the check fails whichever side moves. Until 2026-09-04 the rule
/// was stated here, and in <c>libs/shared</c>'s own copy of it, and enforced by
/// nothing — which is how member-portal's Home came to filter two tiles on keys
/// the catalogue had never heard of, leaving both invisible to every Samaaj
/// with nothing logged anywhere.
/// </remarks>
public static class ModuleCatalog
{
    /// <summary>Timeline posts, volunteer groups and Samaaj events.</summary>
    public const string Community = "community";

    /// <summary>Social issues raised by members, and their approval flow.</summary>
    public const string SocialIssues = "social-issues";

    /// <summary>Celebrities of Samaaj nominations and voting.</summary>
    public const string CelebrityVoting = "celebrity-voting";

    /// <summary>Jain Pathshala: classes, attendance and exams.</summary>
    public const string Pathshala = "pathshala";

    /// <summary>Auctions. Off by default - most Samaaj do not run one.</summary>
    public const string Boli = "boli";

    /// <summary>Matrimony biodata and proposals. Off by default - see Boli's own note.</summary>
    public const string Matrimony = "matrimony";

    /// <summary>Members' own businesses, products and orders between members. Off by default - see Boli's own note.</summary>
    public const string Businesses = "businesses";

    /// <summary>Samaaj competitions: registration, judged rounds and published results. Off by default - see Boli's own note.</summary>
    public const string Competitions = "competitions";

    /// <summary>The Samaaj Panch: the elected ten-member jury that judges social issues. Off by default - a Samaaj switches it on once it is ready to elect one.</summary>
    public const string Panch = "panch";

    /// <summary>Locality-wise Trust management: rosters, and later elections, donations, expenses and payment collection. Off by default - see Boli's own note.</summary>
    public const string Trust = "trust";

    public static IReadOnlyList<ModuleDescriptor> All { get; } =
    [
        new(Community, "Timeline & Volunteer Groups", DefaultOn: true),
        new(SocialIssues, "Social Issues", DefaultOn: true),
        new(CelebrityVoting, "Celebrities of Samaaj", DefaultOn: true),
        new(Pathshala, "Jain Pathshala", DefaultOn: true),

        // The admin wireframe leaves this one unticked. A Samaaj that does not
        // run auctions should not have to notice the feature to turn it off.
        new(Boli, "Auctions / Boli", DefaultOn: false),

        // Same reasoning as Boli: not every Samaaj wants a matrimony module
        // running, and opting in should be a visible admin decision.
        new(Matrimony, "Matrimony", DefaultOn: false),

        // Same reasoning again: a member marketplace is optional, not every
        // Samaaj wants to run one.
        new(Businesses, "Businesses", DefaultOn: false),

        // Same reasoning again: running a competition is optional, not every
        // Samaaj wants one.
        new(Competitions, "Competitions", DefaultOn: false),

        // Electing a Panch is a decision a Samaaj makes; until it does, social
        // issues are reviewed by its administrators as before.
        new(Panch, "Samaaj Panch", DefaultOn: false),

        // Same reasoning as Boli: not every Samaaj organises itself by
        // locality Trusts, and opting in should be a visible admin decision.
        new(Trust, "Trust Management", DefaultOn: false),
    ];

    public static IReadOnlyList<string> DefaultKeys { get; } =
        [.. All.Where(m => m.DefaultOn).Select(m => m.Key)];

    /// <summary>
    /// The canonical key matching <paramref name="key"/>, or null when nothing
    /// matches. Case-insensitive on the way in, canonical on the way out: the
    /// gateway compares module keys case-insensitively, so rejecting
    /// "Pathshala" would refuse a request that would have worked, while
    /// storing it verbatim would leave two spellings of one module in the
    /// database for the next comparison to disagree about.
    /// </summary>
    public static string? Canonical(string? key) =>
        key is null
            ? null
            : All.FirstOrDefault(m =>
                string.Equals(m.Key, key.Trim(), StringComparison.OrdinalIgnoreCase))?.Key;

    public static bool IsKnown(string? key) => Canonical(key) is not null;

    /// <summary>The keys in <paramref name="keys"/> this catalogue has never heard of.</summary>
    public static IReadOnlyList<string> Unknown(IEnumerable<string>? keys) =>
        keys is null ? [] : [.. keys.Where(k => !IsKnown(k)).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The canonical form of every key given, with unknown ones dropped.
    /// Callers validate first; this is the normalisation step after that.
    /// </summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? keys) =>
        keys is null
            ? []
            : [.. keys.Select(Canonical).OfType<string>().Distinct(StringComparer.Ordinal)];
}

/// <summary>
/// One module, with the label the admin portal shows. The label lives here
/// rather than in the portal so the two cannot drift into disagreeing about
/// what a key means.
/// </summary>
public sealed record ModuleDescriptor(string Key, string Label, bool DefaultOn);
