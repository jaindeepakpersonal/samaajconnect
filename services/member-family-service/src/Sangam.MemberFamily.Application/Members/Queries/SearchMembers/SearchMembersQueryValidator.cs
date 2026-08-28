using FluentValidation;

namespace Sangam.MemberFamily.Application.Members.Queries.SearchMembers;

public sealed class SearchMembersQueryValidator : AbstractValidator<SearchMembersQuery>
{
    public SearchMembersQueryValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
        RuleFor(x => x.Term).MaximumLength(120);
        RuleFor(x => x.Locality).MaximumLength(120);
    }
}
