namespace BankLedger;

public enum AccountType
{
    /// <summary>Everyday account. May be allowed to go negative up to an overdraft limit.</summary>
    Checking,

    /// <summary>Earns interest, never goes below zero.</summary>
    Savings,
}

/// <summary>
/// One account and its entries.
/// </summary>
/// <remarks>
/// The entry list is append-only and the balance is not a stored field that can
/// drift out of step with it: <see cref="Balance"/> is whatever the last entry
/// says, and <see cref="Recompute"/> replays every entry to prove the two agree.
/// </remarks>
public sealed class Account
{
    private readonly List<Transaction> _entries = new();

    internal Account(string number, string holder, AccountType type, string currency, Money overdraftLimit, DateTimeOffset openedAt)
    {
        Number = number;
        Holder = holder;
        Type = type;
        Currency = currency;
        OverdraftLimit = overdraftLimit;
        OpenedAt = openedAt;
    }

    public string Number { get; }

    public string Holder { get; }

    public AccountType Type { get; }

    public string Currency { get; }

    /// <summary>How far below zero a checking account may go. Always zero for savings.</summary>
    public Money OverdraftLimit { get; }

    public DateTimeOffset OpenedAt { get; }

    public bool IsFrozen { get; private set; }

    public IReadOnlyList<Transaction> Entries => _entries;

    public Money Balance => _entries.Count == 0 ? Money.Zero(Currency) : _entries[^1].BalanceAfter;

    /// <summary>The most a withdrawal may take right now, balance plus any overdraft.</summary>
    public Money Available => Balance + OverdraftLimit;

    public void Freeze() => IsFrozen = true;

    public void Unfreeze() => IsFrozen = false;

    /// <summary>Replays every entry from zero. The result must equal <see cref="Balance"/>.</summary>
    public Money Recompute()
    {
        var running = Money.Zero(Currency);
        foreach (var entry in _entries)
        {
            running += entry.SignedAmount;
        }

        return running;
    }

    internal Transaction Post(TransactionType type, Money amount, string description, DateTimeOffset at, string? reference)
    {
        if (IsFrozen)
        {
            throw new AccountFrozenException(Number);
        }

        if (amount.Currency != Currency)
        {
            throw new LedgerException($"{Number} holds {Currency}, not {amount.Currency}");
        }

        if (amount.Amount <= 0m)
        {
            throw new LedgerException($"{type} must be a positive amount, got {amount}");
        }

        var rounded = amount.Rounded();
        var entry = new Transaction(Guid.NewGuid(), at, type, rounded, Money.Zero(Currency), description, reference);
        var next = Balance + entry.SignedAmount;

        if (next.IsNegative && next < (Money.Zero(Currency) - OverdraftLimit))
        {
            throw new InsufficientFundsException(Number, rounded, Available);
        }

        entry = entry with { BalanceAfter = next };
        _entries.Add(entry);
        return entry;
    }

    public override string ToString() => $"{Number} ({Type}) {Holder}: {Balance}";
}
