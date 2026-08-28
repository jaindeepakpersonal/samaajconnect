using Sangam.MemberFamily.Domain.Children;

namespace Sangam.MemberFamily.Application.Children;

internal static class ChildMappings
{
    public static ChildResponse ToResponse(
        this ChildProfile child, DateOnly today, bool hasPendingConversion) =>
        new(
            child.Id,
            child.FamilyId,
            child.FullName,
            child.DateOfBirth,
            child.AgeOn(today),
            child.Gender.ToString(),
            child.PhotoUrl,
            child.Status.ToString(),
            child.IsEligibleForConversion(today),
            hasPendingConversion,
            child.CreatedAt);

    public static ConversionRequestResponse ToResponse(
        this ChildConversionRequest request, string childFullName) =>
        new(
            request.Id,
            request.ChildProfileId,
            childFullName,
            request.MobileOrEmail,
            request.Status.ToString(),
            request.RequestedAt,
            request.DecidedBy,
            request.DecidedAt,
            request.DecisionNote);
}
