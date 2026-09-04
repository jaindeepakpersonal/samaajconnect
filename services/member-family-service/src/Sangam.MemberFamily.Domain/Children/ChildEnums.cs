namespace Sangam.MemberFamily.Domain.Children;

public enum ChildStatus
{
    /// <summary>A child record managed by their family. No login of their own.</summary>
    Minor = 1,

    /// <summary>Conversion approved; a login now exists for this person.</summary>
    Converted = 2,

    /// <summary>
    /// The parental consent this record existed on has been withdrawn, or the
    /// person who gave it erased their account. The row is de-identified and
    /// kept only because other services hold the id.
    /// </summary>
    /// <remarks>
    /// This is a status rather than a deleted row for the same reason erasure
    /// keeps a household standing: a Pathshala enrolment, a register mark and an
    /// exam result all reference this id, and deleting the row would leave those
    /// pointing at nothing. It is a status rather than nothing at all because
    /// without it a de-identified child stays on their family's screen forever,
    /// listed as "Erased child" - which was true before this existed.
    /// </remarks>
    Withdrawn = 3,
}

public enum ConversionStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}
