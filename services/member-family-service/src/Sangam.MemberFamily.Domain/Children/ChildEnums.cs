namespace Sangam.MemberFamily.Domain.Children;

public enum ChildStatus
{
    /// <summary>A child record managed by their family. No login of their own.</summary>
    Minor = 1,

    /// <summary>Conversion approved; a login now exists for this person.</summary>
    Converted = 2,
}

public enum ConversionStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}
