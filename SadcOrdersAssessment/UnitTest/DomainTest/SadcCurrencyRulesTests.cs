using Domain.Common;

namespace UnitTest.DomainTests;

public class SadcCurrencyRulesTests
{
    [Theory]
    [InlineData("ZA", "ZAR")]
    [InlineData("BW", "BWP")]
    [InlineData("AO", "AOA")]
    [InlineData("KM", "KMF")]
    [InlineData("CD", "CDF")]
    [InlineData("SZ", "SZL")]
    [InlineData("LS", "LSL")]
    [InlineData("MG", "MGA")]
    [InlineData("MW", "MWK")]
    [InlineData("MU", "MUR")]
    [InlineData("MZ", "MZN")]
    [InlineData("NA", "NAD")]
    [InlineData("SC", "SCR")]
    [InlineData("TZ", "TZS")]
    [InlineData("ZM", "ZMW")]
    [InlineData("ZW", "ZWL")]
    [InlineData("ZW", "USD")]
    [InlineData("za", "zar")] // case-insensitive input
    public void Allows_the_reference_currency_for_every_member_state(string country, string currency)
    {
        SadcCurrencyRules.IsCurrencyAllowed(country, currency, allowRandInCommonMonetaryArea: false).Should().BeTrue();
    }

    [Theory]
    [InlineData("ZA", "USD")]
    [InlineData("BW", "ZAR")]
    [InlineData("ZA", "BWP")]
    [InlineData("NA", "ZAR")] // CMA flag off: matches the brief's table exactly
    [InlineData("KE", "KES")] // Kenya is not in SADC
    [InlineData("ZA", "")]
    public void Rejects_currencies_not_permitted_for_the_country(string country, string currency)
    {
        SadcCurrencyRules.IsCurrencyAllowed(country, currency, allowRandInCommonMonetaryArea: false).Should().BeFalse();
    }

    [Theory]
    [InlineData("NA")]
    [InlineData("LS")]
    [InlineData("SZ")]
    public void Accepts_rand_in_CMA_countries_only_when_enabled(string country)
    {
        SadcCurrencyRules.IsCurrencyAllowed(country, "ZAR", allowRandInCommonMonetaryArea: true).Should().BeTrue();
        SadcCurrencyRules.AllowedCurrencies(country, allowRandInCommonMonetaryArea: true).Should().HaveCount(2);
    }

    [Fact]
    public void CMA_flag_does_not_widen_non_CMA_countries()
    {
        SadcCurrencyRules.IsCurrencyAllowed("BW", "ZAR", allowRandInCommonMonetaryArea: true).Should().BeFalse();
        SadcCurrencyRules.AllowedCurrencies("ZA", allowRandInCommonMonetaryArea: true).Should().Equal("ZAR");
    }

    [Fact]
    public void Lists_all_16_member_states()
    {
        SadcCurrencyRules.GetCountries(false).Should().HaveCount(16);
        SadcCurrencyRules.IsSadcCountry("KE").Should().BeFalse();
        SadcCurrencyRules.IsSadcCountry(null).Should().BeFalse();
    }
}
