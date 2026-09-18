namespace BankLedger;

/// <summary>
/// An amount in a single currency.
/// </summary>
/// <remarks>
/// Money is stored as <see cref="decimal"/>, never <c>double</c>: 0.1 + 0.2 in
/// binary floating point is not 0.3, and a ledger that cannot add up is not a
/// ledger. Every amount carries its currency, so adding rupees to dollars is a
/// compile-time-shaped error rather than a silent wrong number.
/// </remarks>
public readonly record struct Money(decimal Amount, string Currency = "PKR") : IComparable<Money>
{
    public static Money Zero(string currency = "PKR") => new(0m, currency);

    public static Money operator +(Money left, Money right)
    {
        Require(left, right);
        return left with { Amount = left.Amount + right.Amount };
    }

    public static Money operator -(Money left, Money right)
    {
        Require(left, right);
        return left with { Amount = left.Amount - right.Amount };
    }

    public static Money operator *(Money value, decimal factor) => value with { Amount = value.Amount * factor };

    public static bool operator >(Money left, Money right) { Require(left, right); return left.Amount > right.Amount; }
    public static bool operator <(Money left, Money right) { Require(left, right); return left.Amount < right.Amount; }
    public static bool operator >=(Money left, Money right) { Require(left, right); return left.Amount >= right.Amount; }
    public static bool operator <=(Money left, Money right) { Require(left, right); return left.Amount <= right.Amount; }

    public bool IsNegative => Amount < 0;

    /// <summary>Rounds half away from zero, the convention used on statements.</summary>
    public Money Rounded(int decimals = 2) =>
        this with { Amount = Math.Round(Amount, decimals, MidpointRounding.AwayFromZero) };

    public int CompareTo(Money other)
    {
        Require(this, other);
        return Amount.CompareTo(other.Amount);
    }

    public override string ToString() => $"{Currency} {Amount:N2}";

    private static void Require(Money left, Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"cannot mix {left.Currency} and {right.Currency}");
        }
    }
}
