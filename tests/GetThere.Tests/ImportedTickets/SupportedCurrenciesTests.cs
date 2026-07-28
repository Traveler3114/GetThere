using GetThereShared.Common;

namespace GetThere.Tests.ImportedTickets;

/// <summary>
/// Validation and the picker are driven from the same list. They disagreed before: the API
/// accepted HRK while the picker never offered it, so the only way to store one was to call the
/// API directly — and HRK stopped being legal tender in January 2023.
/// </summary>
public class SupportedCurrenciesTests
{
    [Fact]
    public void Everything_the_picker_offers_can_be_written()
    {
        foreach (var currency in SupportedCurrencies.Selectable)
            Assert.Contains(currency, SupportedCurrencies.All);
    }

    [Fact]
    public void Everything_writable_is_offered_by_the_picker()
    {
        foreach (var currency in SupportedCurrencies.All)
            Assert.Contains(currency, SupportedCurrencies.Selectable);
    }

    [Fact]
    public void The_default_is_writable()
    {
        Assert.Contains(SupportedCurrencies.Default, SupportedCurrencies.All);
    }

    [Fact]
    public void Retired_currencies_cannot_be_written()
    {
        Assert.DoesNotContain("HRK", SupportedCurrencies.All);
        Assert.DoesNotContain("HRK", SupportedCurrencies.Selectable);
    }

    /// <summary>
    /// Existing rows priced in HRK must still render. There is no stored conversion rate, so
    /// restating what someone recorded paying would be worse than showing it as paid.
    /// </summary>
    [Fact]
    public void Retired_currencies_remain_readable()
    {
        Assert.Contains("HRK", SupportedCurrencies.Legacy);
        Assert.True(SupportedCurrencies.IsKnown("HRK"));
        Assert.Equal("kn ", MoneyFormatter.SymbolFor("HRK"));
    }

    [Theory]
    [InlineData("EUR", true)]
    [InlineData("eur", true)]
    [InlineData("HRK", true)]
    [InlineData("XYZ", false)]
    [InlineData(null, false)]
    public void Knows_which_currencies_it_can_display(string? code, bool expected)
    {
        Assert.Equal(expected, SupportedCurrencies.IsKnown(code));
    }
}
