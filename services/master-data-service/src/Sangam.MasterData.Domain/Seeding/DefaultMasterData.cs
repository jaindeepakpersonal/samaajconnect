using Sangam.MasterData.Domain.Lookups;

namespace Sangam.MasterData.Domain.Seeding;

/// <summary>
/// The platform's default reference data, seeded by migration rather than by
/// a Samaaj Admin's first visit to the Master Data screen.
/// </summary>
/// <remarks>
/// Mirrors identity-tenant-service's own <c>AuthorizationCatalog</c>: hand-
/// assigned, stable ids so re-running a migration never produces a second
/// copy, and the rows are attached via <c>HasData()</c> in
/// <c>LookupItemConfiguration</c>/<c>StateConfiguration</c>/
/// <c>CityConfiguration</c> rather than through <c>LookupItem.Create()</c>/
/// <c>State.Create()</c>/<c>City.Create()</c> - a migration's own generated
/// <c>InsertData</c> calls write these rows directly, so none of them raises
/// a domain event or writes an Outbox row. That is correct, not an oversight:
/// identity-tenant-service's seeded roles and permissions are silent the
/// same way, because reference data landing on a fresh database is not an
/// event anybody needs told about.
///
/// <c>Gautra</c> is deliberately not seeded here - it is the one tenant-
/// scoped entity this service owns, a Samaaj's own list, and there is no
/// sensible platform-wide default for it. <c>Locality</c> is left out too:
/// too fine-grained for a default list to get right. Both stay exactly as
/// they are today, populated per Samaaj through the Master Data screen.
/// </remarks>
public static class DefaultMasterData
{
    /// <summary>
    /// The <c>LookupItem</c> ids the <c>States</c>/<c>Cities</c> lists below
    /// need to reference. Only India's id is needed by name - the five
    /// diaspora countries have no state/city rows seeded under them.
    /// </summary>
    public static class CountryIds
    {
        public static readonly Guid India = new("c0000000-0000-0000-0000-000000000001");
        public static readonly Guid UnitedStates = new("c0000000-0000-0000-0000-000000000002");
        public static readonly Guid UnitedKingdom = new("c0000000-0000-0000-0000-000000000003");
        public static readonly Guid Canada = new("c0000000-0000-0000-0000-000000000004");
        public static readonly Guid Australia = new("c0000000-0000-0000-0000-000000000005");
        public static readonly Guid UnitedArabEmirates = new("c0000000-0000-0000-0000-000000000006");
    }

    /// <summary>India's states and union territories, referenced by <c>Cities</c> below.</summary>
    public static class StateIds
    {
        public static readonly Guid AndhraPradesh = new("d0000000-0000-0000-0000-000000000001");
        public static readonly Guid ArunachalPradesh = new("d0000000-0000-0000-0000-000000000002");
        public static readonly Guid Assam = new("d0000000-0000-0000-0000-000000000003");
        public static readonly Guid Bihar = new("d0000000-0000-0000-0000-000000000004");
        public static readonly Guid Chhattisgarh = new("d0000000-0000-0000-0000-000000000005");
        public static readonly Guid Goa = new("d0000000-0000-0000-0000-000000000006");
        public static readonly Guid Gujarat = new("d0000000-0000-0000-0000-000000000007");
        public static readonly Guid Haryana = new("d0000000-0000-0000-0000-000000000008");
        public static readonly Guid HimachalPradesh = new("d0000000-0000-0000-0000-000000000009");
        public static readonly Guid Jharkhand = new("d0000000-0000-0000-0000-000000000010");
        public static readonly Guid Karnataka = new("d0000000-0000-0000-0000-000000000011");
        public static readonly Guid Kerala = new("d0000000-0000-0000-0000-000000000012");
        public static readonly Guid MadhyaPradesh = new("d0000000-0000-0000-0000-000000000013");
        public static readonly Guid Maharashtra = new("d0000000-0000-0000-0000-000000000014");
        public static readonly Guid Manipur = new("d0000000-0000-0000-0000-000000000015");
        public static readonly Guid Meghalaya = new("d0000000-0000-0000-0000-000000000016");
        public static readonly Guid Mizoram = new("d0000000-0000-0000-0000-000000000017");
        public static readonly Guid Nagaland = new("d0000000-0000-0000-0000-000000000018");
        public static readonly Guid Odisha = new("d0000000-0000-0000-0000-000000000019");
        public static readonly Guid Punjab = new("d0000000-0000-0000-0000-000000000020");
        public static readonly Guid Rajasthan = new("d0000000-0000-0000-0000-000000000021");
        public static readonly Guid Sikkim = new("d0000000-0000-0000-0000-000000000022");
        public static readonly Guid TamilNadu = new("d0000000-0000-0000-0000-000000000023");
        public static readonly Guid Telangana = new("d0000000-0000-0000-0000-000000000024");
        public static readonly Guid Tripura = new("d0000000-0000-0000-0000-000000000025");
        public static readonly Guid UttarPradesh = new("d0000000-0000-0000-0000-000000000026");
        public static readonly Guid Uttarakhand = new("d0000000-0000-0000-0000-000000000027");
        public static readonly Guid WestBengal = new("d0000000-0000-0000-0000-000000000028");

        // Union territories.
        public static readonly Guid AndamanAndNicobarIslands = new("d0000000-0000-0000-0000-000000000029");
        public static readonly Guid Chandigarh = new("d0000000-0000-0000-0000-000000000030");
        public static readonly Guid DadraAndNagarHaveliAndDamanAndDiu = new("d0000000-0000-0000-0000-000000000031");
        public static readonly Guid Delhi = new("d0000000-0000-0000-0000-000000000032");
        public static readonly Guid JammuAndKashmir = new("d0000000-0000-0000-0000-000000000033");
        public static readonly Guid Ladakh = new("d0000000-0000-0000-0000-000000000034");
        public static readonly Guid Lakshadweep = new("d0000000-0000-0000-0000-000000000035");
        public static readonly Guid Puducherry = new("d0000000-0000-0000-0000-000000000036");
    }

    private static object LookupItem(
        Guid id, LookupCategory category, string code, string label, int sortOrder) => new
    {
        Id = id,
        Category = category,
        Code = code,
        Label = label,
        SortOrder = sortOrder,
        IsActive = true,
    };

    private static object State(Guid id, Guid countryId, string name) => new
    {
        Id = id,
        CountryId = countryId,
        Name = name,
        IsActive = true,
    };

    private static object City(Guid id, Guid countryId, Guid? stateId, string name) => new
    {
        Id = id,
        CountryId = countryId,
        StateId = stateId,
        Name = name,
        IsActive = true,
    };

    /// <summary>
    /// One default row per platform-wide category. Country is India plus
    /// the diaspora countries with the largest Jain communities outside it;
    /// the other seven categories are short, ordinary starter lists, in the
    /// same spirit as the example values this service's own
    /// <see cref="LookupCategory"/> doc comments already name.
    /// </summary>
    public static readonly IReadOnlyList<object> LookupItems =
    [
        // Country
        LookupItem(CountryIds.India, LookupCategory.Country, "IN", "India", 1),
        LookupItem(CountryIds.UnitedStates, LookupCategory.Country, "US", "United States", 2),
        LookupItem(CountryIds.UnitedKingdom, LookupCategory.Country, "GB", "United Kingdom", 3),
        LookupItem(CountryIds.Canada, LookupCategory.Country, "CA", "Canada", 4),
        LookupItem(CountryIds.Australia, LookupCategory.Country, "AU", "Australia", 5),
        LookupItem(CountryIds.UnitedArabEmirates, LookupCategory.Country, "AE", "United Arab Emirates", 6),

        // Gender
        LookupItem(new("c0000001-0000-0000-0000-000000000001"), LookupCategory.Gender, "M", "Male", 1),
        LookupItem(new("c0000001-0000-0000-0000-000000000002"), LookupCategory.Gender, "F", "Female", 2),
        LookupItem(new("c0000001-0000-0000-0000-000000000003"), LookupCategory.Gender, "O", "Other", 3),

        // MaritalStatus
        LookupItem(new("c0000002-0000-0000-0000-000000000001"), LookupCategory.MaritalStatus, "S", "Single", 1),
        LookupItem(new("c0000002-0000-0000-0000-000000000002"), LookupCategory.MaritalStatus, "M", "Married", 2),
        LookupItem(new("c0000002-0000-0000-0000-000000000003"), LookupCategory.MaritalStatus, "W", "Widowed", 3),
        LookupItem(new("c0000002-0000-0000-0000-000000000004"), LookupCategory.MaritalStatus, "D", "Divorced", 4),

        // Qualification
        LookupItem(new("c0000003-0000-0000-0000-000000000001"), LookupCategory.Qualification, "B10", "Below 10th", 1),
        LookupItem(new("c0000003-0000-0000-0000-000000000002"), LookupCategory.Qualification, "10TH", "10th Pass", 2),
        LookupItem(new("c0000003-0000-0000-0000-000000000003"), LookupCategory.Qualification, "12TH", "12th Pass", 3),
        LookupItem(new("c0000003-0000-0000-0000-000000000004"), LookupCategory.Qualification, "DIP", "Diploma", 4),
        LookupItem(new("c0000003-0000-0000-0000-000000000005"), LookupCategory.Qualification, "GRAD", "Graduate", 5),
        LookupItem(new("c0000003-0000-0000-0000-000000000006"), LookupCategory.Qualification, "PG", "Post Graduate", 6),
        LookupItem(new("c0000003-0000-0000-0000-000000000007"), LookupCategory.Qualification, "PHD", "Doctorate", 7),

        // ProfessionType
        LookupItem(new("c0000004-0000-0000-0000-000000000001"), LookupCategory.ProfessionType, "BUS", "Business", 1),
        LookupItem(new("c0000004-0000-0000-0000-000000000002"), LookupCategory.ProfessionType, "SVC", "Service / Job", 2),
        LookupItem(new("c0000004-0000-0000-0000-000000000003"), LookupCategory.ProfessionType, "PROF", "Professional", 3),
        LookupItem(new("c0000004-0000-0000-0000-000000000004"), LookupCategory.ProfessionType, "STU", "Student", 4),
        LookupItem(new("c0000004-0000-0000-0000-000000000005"), LookupCategory.ProfessionType, "HOME", "Homemaker", 5),
        LookupItem(new("c0000004-0000-0000-0000-000000000006"), LookupCategory.ProfessionType, "RET", "Retired", 6),
        LookupItem(new("c0000004-0000-0000-0000-000000000007"), LookupCategory.ProfessionType, "OTH", "Other", 7),

        // BusinessCategory
        LookupItem(new("c0000005-0000-0000-0000-000000000001"), LookupCategory.BusinessCategory, "GRO", "Grocery", 1),
        LookupItem(new("c0000005-0000-0000-0000-000000000002"), LookupCategory.BusinessCategory, "TEX", "Textiles", 2),
        LookupItem(new("c0000005-0000-0000-0000-000000000003"), LookupCategory.BusinessCategory, "JWL", "Jewellery", 3),
        LookupItem(new("c0000005-0000-0000-0000-000000000004"), LookupCategory.BusinessCategory, "ELEC", "Electronics", 4),
        LookupItem(new("c0000005-0000-0000-0000-000000000005"), LookupCategory.BusinessCategory, "CAT", "Catering", 5),
        LookupItem(new("c0000005-0000-0000-0000-000000000006"), LookupCategory.BusinessCategory, "HOSP", "Hospitality", 6),
        LookupItem(new("c0000005-0000-0000-0000-000000000007"), LookupCategory.BusinessCategory, "PSVC", "Professional Services", 7),
        LookupItem(new("c0000005-0000-0000-0000-000000000008"), LookupCategory.BusinessCategory, "OTH", "Other", 8),

        // EventType
        LookupItem(new("c0000006-0000-0000-0000-000000000001"), LookupCategory.EventType, "REL", "Religious", 1),
        LookupItem(new("c0000006-0000-0000-0000-000000000002"), LookupCategory.EventType, "CUL", "Cultural", 2),
        LookupItem(new("c0000006-0000-0000-0000-000000000003"), LookupCategory.EventType, "SOC", "Social", 3),
        LookupItem(new("c0000006-0000-0000-0000-000000000004"), LookupCategory.EventType, "EDU", "Educational", 4),
        LookupItem(new("c0000006-0000-0000-0000-000000000005"), LookupCategory.EventType, "CHAR", "Charitable", 5),
        LookupItem(new("c0000006-0000-0000-0000-000000000006"), LookupCategory.EventType, "SPRT", "Sports", 6),

        // CompetitionType
        LookupItem(new("c0000007-0000-0000-0000-000000000001"), LookupCategory.CompetitionType, "RANG", "Rangoli", 1),
        LookupItem(new("c0000007-0000-0000-0000-000000000002"), LookupCategory.CompetitionType, "ELOC", "Elocution", 2),
        LookupItem(new("c0000007-0000-0000-0000-000000000003"), LookupCategory.CompetitionType, "QUIZ", "Quiz", 3),
        LookupItem(new("c0000007-0000-0000-0000-000000000004"), LookupCategory.CompetitionType, "SING", "Singing", 4),
        LookupItem(new("c0000007-0000-0000-0000-000000000005"), LookupCategory.CompetitionType, "DANCE", "Dance", 5),
        LookupItem(new("c0000007-0000-0000-0000-000000000006"), LookupCategory.CompetitionType, "ESSAY", "Essay Writing", 6),
        LookupItem(new("c0000007-0000-0000-0000-000000000007"), LookupCategory.CompetitionType, "ART", "Drawing / Art", 7),
    ];

    /// <summary>India's 28 states and 8 union territories.</summary>
    public static readonly IReadOnlyList<object> States =
    [
        State(StateIds.AndhraPradesh, CountryIds.India, "Andhra Pradesh"),
        State(StateIds.ArunachalPradesh, CountryIds.India, "Arunachal Pradesh"),
        State(StateIds.Assam, CountryIds.India, "Assam"),
        State(StateIds.Bihar, CountryIds.India, "Bihar"),
        State(StateIds.Chhattisgarh, CountryIds.India, "Chhattisgarh"),
        State(StateIds.Goa, CountryIds.India, "Goa"),
        State(StateIds.Gujarat, CountryIds.India, "Gujarat"),
        State(StateIds.Haryana, CountryIds.India, "Haryana"),
        State(StateIds.HimachalPradesh, CountryIds.India, "Himachal Pradesh"),
        State(StateIds.Jharkhand, CountryIds.India, "Jharkhand"),
        State(StateIds.Karnataka, CountryIds.India, "Karnataka"),
        State(StateIds.Kerala, CountryIds.India, "Kerala"),
        State(StateIds.MadhyaPradesh, CountryIds.India, "Madhya Pradesh"),
        State(StateIds.Maharashtra, CountryIds.India, "Maharashtra"),
        State(StateIds.Manipur, CountryIds.India, "Manipur"),
        State(StateIds.Meghalaya, CountryIds.India, "Meghalaya"),
        State(StateIds.Mizoram, CountryIds.India, "Mizoram"),
        State(StateIds.Nagaland, CountryIds.India, "Nagaland"),
        State(StateIds.Odisha, CountryIds.India, "Odisha"),
        State(StateIds.Punjab, CountryIds.India, "Punjab"),
        State(StateIds.Rajasthan, CountryIds.India, "Rajasthan"),
        State(StateIds.Sikkim, CountryIds.India, "Sikkim"),
        State(StateIds.TamilNadu, CountryIds.India, "Tamil Nadu"),
        State(StateIds.Telangana, CountryIds.India, "Telangana"),
        State(StateIds.Tripura, CountryIds.India, "Tripura"),
        State(StateIds.UttarPradesh, CountryIds.India, "Uttar Pradesh"),
        State(StateIds.Uttarakhand, CountryIds.India, "Uttarakhand"),
        State(StateIds.WestBengal, CountryIds.India, "West Bengal"),

        State(StateIds.AndamanAndNicobarIslands, CountryIds.India, "Andaman and Nicobar Islands"),
        State(StateIds.Chandigarh, CountryIds.India, "Chandigarh"),
        State(StateIds.DadraAndNagarHaveliAndDamanAndDiu, CountryIds.India, "Dadra and Nagar Haveli and Daman and Diu"),
        State(StateIds.Delhi, CountryIds.India, "Delhi"),
        State(StateIds.JammuAndKashmir, CountryIds.India, "Jammu and Kashmir"),
        State(StateIds.Ladakh, CountryIds.India, "Ladakh"),
        State(StateIds.Lakshadweep, CountryIds.India, "Lakshadweep"),
        State(StateIds.Puducherry, CountryIds.India, "Puducherry"),
    ];

    /// <summary>
    /// A modest, curated starter list - not an exhaustive gazetteer - weighted
    /// toward the states with the platform's largest expected Jain Samaaj
    /// communities (Rajasthan, Gujarat, Maharashtra, Madhya Pradesh, Delhi/NCR,
    /// Karnataka), with a handful of other major cities besides.
    /// </summary>
    public static readonly IReadOnlyList<object> Cities =
    [
        // Rajasthan
        City(new("e0000000-0000-0000-0000-000000000001"), CountryIds.India, StateIds.Rajasthan, "Jaipur"),
        City(new("e0000000-0000-0000-0000-000000000002"), CountryIds.India, StateIds.Rajasthan, "Udaipur"),
        City(new("e0000000-0000-0000-0000-000000000003"), CountryIds.India, StateIds.Rajasthan, "Jodhpur"),
        City(new("e0000000-0000-0000-0000-000000000004"), CountryIds.India, StateIds.Rajasthan, "Ajmer"),
        City(new("e0000000-0000-0000-0000-000000000005"), CountryIds.India, StateIds.Rajasthan, "Kota"),
        City(new("e0000000-0000-0000-0000-000000000006"), CountryIds.India, StateIds.Rajasthan, "Bikaner"),

        // Gujarat
        City(new("e0000000-0000-0000-0000-000000000007"), CountryIds.India, StateIds.Gujarat, "Ahmedabad"),
        City(new("e0000000-0000-0000-0000-000000000008"), CountryIds.India, StateIds.Gujarat, "Surat"),
        City(new("e0000000-0000-0000-0000-000000000009"), CountryIds.India, StateIds.Gujarat, "Vadodara"),
        City(new("e0000000-0000-0000-0000-000000000010"), CountryIds.India, StateIds.Gujarat, "Rajkot"),
        City(new("e0000000-0000-0000-0000-000000000011"), CountryIds.India, StateIds.Gujarat, "Bhavnagar"),

        // Maharashtra
        City(new("e0000000-0000-0000-0000-000000000012"), CountryIds.India, StateIds.Maharashtra, "Mumbai"),
        City(new("e0000000-0000-0000-0000-000000000013"), CountryIds.India, StateIds.Maharashtra, "Pune"),
        City(new("e0000000-0000-0000-0000-000000000014"), CountryIds.India, StateIds.Maharashtra, "Nagpur"),
        City(new("e0000000-0000-0000-0000-000000000015"), CountryIds.India, StateIds.Maharashtra, "Nashik"),

        // Madhya Pradesh
        City(new("e0000000-0000-0000-0000-000000000016"), CountryIds.India, StateIds.MadhyaPradesh, "Indore"),
        City(new("e0000000-0000-0000-0000-000000000017"), CountryIds.India, StateIds.MadhyaPradesh, "Bhopal"),
        City(new("e0000000-0000-0000-0000-000000000018"), CountryIds.India, StateIds.MadhyaPradesh, "Gwalior"),
        City(new("e0000000-0000-0000-0000-000000000019"), CountryIds.India, StateIds.MadhyaPradesh, "Ujjain"),

        // Delhi/NCR
        City(new("e0000000-0000-0000-0000-000000000020"), CountryIds.India, StateIds.Delhi, "New Delhi"),
        City(new("e0000000-0000-0000-0000-000000000021"), CountryIds.India, StateIds.Haryana, "Gurugram"),
        City(new("e0000000-0000-0000-0000-000000000022"), CountryIds.India, StateIds.Haryana, "Faridabad"),

        // Karnataka
        City(new("e0000000-0000-0000-0000-000000000023"), CountryIds.India, StateIds.Karnataka, "Bengaluru"),
        City(new("e0000000-0000-0000-0000-000000000024"), CountryIds.India, StateIds.Karnataka, "Mysuru"),

        // Other major cities
        City(new("e0000000-0000-0000-0000-000000000025"), CountryIds.India, StateIds.TamilNadu, "Chennai"),
        City(new("e0000000-0000-0000-0000-000000000026"), CountryIds.India, StateIds.TamilNadu, "Coimbatore"),
        City(new("e0000000-0000-0000-0000-000000000027"), CountryIds.India, StateIds.WestBengal, "Kolkata"),
        City(new("e0000000-0000-0000-0000-000000000028"), CountryIds.India, StateIds.UttarPradesh, "Lucknow"),
        City(new("e0000000-0000-0000-0000-000000000029"), CountryIds.India, StateIds.UttarPradesh, "Kanpur"),
        City(new("e0000000-0000-0000-0000-000000000030"), CountryIds.India, StateIds.UttarPradesh, "Agra"),
        City(new("e0000000-0000-0000-0000-000000000031"), CountryIds.India, StateIds.UttarPradesh, "Varanasi"),
        City(new("e0000000-0000-0000-0000-000000000032"), CountryIds.India, StateIds.Telangana, "Hyderabad"),
        City(new("e0000000-0000-0000-0000-000000000033"), CountryIds.India, StateIds.Punjab, "Ludhiana"),
        City(new("e0000000-0000-0000-0000-000000000034"), CountryIds.India, StateIds.Punjab, "Amritsar"),
        City(new("e0000000-0000-0000-0000-000000000035"), CountryIds.India, StateIds.Chandigarh, "Chandigarh"),
        City(new("e0000000-0000-0000-0000-000000000036"), CountryIds.India, StateIds.Bihar, "Patna"),
        City(new("e0000000-0000-0000-0000-000000000037"), CountryIds.India, StateIds.Kerala, "Kochi"),
        City(new("e0000000-0000-0000-0000-000000000038"), CountryIds.India, StateIds.Kerala, "Thiruvananthapuram"),
        City(new("e0000000-0000-0000-0000-000000000039"), CountryIds.India, StateIds.AndhraPradesh, "Vijayawada"),
        City(new("e0000000-0000-0000-0000-000000000040"), CountryIds.India, StateIds.AndhraPradesh, "Visakhapatnam"),
    ];
}
