using Xunit;

namespace BankLedger.Tests;

public class MoneyTests
{
    [Fact]
    public void Adds_and_subtracts_within_one_currency()
    {
        var a = new Money(1200.50m);
        var b = new Money(300.25m);

        Assert.Equal(new Money(1500.75m), a + b);
        Assert.Equal(new Money(900.25m), a - b);
    }

    [Fact]
    public void Refuses_to_mix_currencies()
    {
        var rupees = new Money(100m, "PKR");
        var dollars = new Money(100m, "USD");

        var error = Assert.Throws<InvalidOperationException>(() => rupees + dollars);
        Assert.Contains("PKR", error.Message, StringComparison.Ordinal);
        Assert.Contains("USD", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_exact_decimals_where_double_would_not()
    {
        // 0.1 + 0.2 != 0.3 in binary floating point. It must here.
        var total = new Money(0.1m) + new Money(0.2m);
        Assert.Equal(0.3m, total.Amount);
    }

    [Theory]
    [InlineData(2.345, 2.35)]
    [InlineData(2.344, 2.34)]
    [InlineData(-2.345, -2.35)]
    [InlineData(2.005, 2.01)]
    public void Rounds_half_away_from_zero(double raw, double expected)
    {
        Assert.Equal((decimal)expected, new Money((decimal)raw).Rounded().Amount);
    }

    [Fact]
    public void Compares_by_amount()
    {
        Assert.True(new Money(10m) > new Money(9.99m));
        Assert.True(new Money(-1m) < Money.Zero());
        Assert.True(new Money(5m) >= new Money(5m));
        Assert.True(new Money(5m) <= new Money(5m));
        Assert.Equal(0, new Money(5m).CompareTo(new Money(5m)));
    }

    [Fact]
    public void Knows_when_it_is_negative()
    {
        Assert.True(new Money(-0.01m).IsNegative);
        Assert.False(Money.Zero().IsNegative);
    }

    [Fact]
    public void Prints_currency_and_thousands()
    {
        Assert.Equal("PKR 1,234.50", new Money(1234.5m).ToString());
        Assert.Equal("USD 0.00", Money.Zero("USD").ToString());
    }

    [Fact]
    public void Multiplies_by_a_plain_factor()
    {
        Assert.Equal(new Money(250m), new Money(1000m) * 0.25m);
    }
}
