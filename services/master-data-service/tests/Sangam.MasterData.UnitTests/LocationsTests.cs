using FluentAssertions;
using Sangam.MasterData.Domain.Locations;
using Xunit;

namespace Sangam.MasterData.UnitTests;

public sealed class LocationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_city_remembers_the_country_it_was_created_under()
    {
        var countryId = Guid.NewGuid();

        var city = City.Create(countryId, "Udaipur", Now);

        city.CountryId.Should().Be(countryId);
        city.IsActive.Should().BeTrue();
    }

    [Fact]
    public void A_locality_remembers_the_city_it_was_created_under()
    {
        var cityId = Guid.NewGuid();

        var locality = Locality.Create(cityId, "Hiran Magri", Now);

        locality.CityId.Should().Be(cityId);
        locality.IsActive.Should().BeTrue();
    }
}
