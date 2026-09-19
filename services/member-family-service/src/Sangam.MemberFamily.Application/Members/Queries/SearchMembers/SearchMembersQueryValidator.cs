using FluentValidation;

namespace Sangam.MemberFamily.Application.Members.Queries.SearchMembers;

public sealed class SearchMembersQueryValidator : AbstractValidator<SearchMembersQuery>
{
    public SearchMembersQueryValidator()
    {
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 100);
        RuleFor(x => x.Term).MaximumLength(120);
    }
}
