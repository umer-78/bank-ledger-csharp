using System.Globalization;
using System.Text;

namespace BankLedger;

/// <summary>
/// A set of accounts and the operations that move money between them.
/// </summary>
/// <remarks>
/// Every balance-changing call goes through <see cref="Account.Post"/>, so there
/// is exactly one place where an entry is written and one place where the
/// overdraft rule is enforced. A transfer checks both legs before touching
/// either, which is what keeps it from leaving money in mid-air.
/// </remarks>
public sealed class Ledger
{
    private readonly Dictionary<string, Account> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTimeOffset> _clock;
    private int _nextNumber = 1;

    /// <param name="currency">Currency every account in this ledger is held in.</param>
    /// <param name="clock">Source of "now". Tests pass a fixed clock so results are repeatable.</param>
    public Ledger(string currency = "PKR", Func<DateTimeOffset>? clock = null)
    {
        Currency = currency;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string Currency { get; }

    public IReadOnlyCollection<Account> Accounts => _accounts.Values;

    public int Count => _accounts.Count;

    /// <summary>Sum of every balance. A ledger's books only add up if this matches the replayed total.</summary>
    public Money TotalHeld()
    {
        var total = Money.Zero(Currency);
        foreach (var account in _accounts.Values)
        {
            total += account.Balance;
        }

        return total;
    }

    public Account Open(string holder, AccountType type = AccountType.Checking, decimal overdraftLimit = 0m)
    {
        if (string.IsNullOrWhiteSpace(holder))
        {
            throw new LedgerException("an account needs a holder name");
        }

        if (overdraftLimit < 0m)
        {
            throw new LedgerException("overdraft limit cannot be negative");
        }

        if (type == AccountType.Savings && overdraftLimit > 0m)
        {
            throw new LedgerException("a savings account cannot have an overdraft");
        }

        var number = string.Create(CultureInfo.InvariantCulture, $"ACC-{_nextNumber:D4}");
        _nextNumber++;

        var account = new Account(number, holder.Trim(), type, Currency, new Money(overdraftLimit, Currency), _clock());
        _accounts[number] = account;
        return account;
    }

    public Account Get(string number) =>
        _accounts.TryGetValue(number, out var account) ? account : throw new AccountNotFoundException(number);

    public bool Has(string number) => _accounts.ContainsKey(number);

    public Transaction Deposit(string number, decimal amount, string description = "Deposit", DateTimeOffset? at = null) =>
        Get(number).Post(TransactionType.Deposit, new Money(amount, Currency), description, at ?? _clock(), null);

    public Transaction Withdraw(string number, decimal amount, string description = "Withdrawal", DateTimeOffset? at = null) =>
        Get(number).Post(TransactionType.Withdrawal, new Money(amount, Currency), description, at ?? _clock(), null);

    public Transaction Fee(string number, decimal amount, string description = "Service fee", DateTimeOffset? at = null) =>
        Get(number).Post(TransactionType.Fee, new Money(amount, Currency), description, at ?? _clock(), null);

    /// <summary>
    /// Moves money between two accounts as one operation.
    /// </summary>
    /// <remarks>
    /// The credit leg is checked before the debit leg runs: a frozen destination
    /// throws before any money leaves the source, so a failed transfer leaves both
    /// accounts exactly as they were.
    /// </remarks>
    public (Transaction Out, Transaction In) Transfer(string fromNumber, string toNumber, decimal amount, string? description = null, DateTimeOffset? at = null)
    {
        if (string.Equals(fromNumber, toNumber, StringComparison.OrdinalIgnoreCase))
        {
            throw new LedgerException("cannot transfer an account to itself");
        }

        var source = Get(fromNumber);
        var destination = Get(toNumber);

        if (destination.IsFrozen)
        {
            throw new AccountFrozenException(destination.Number);
        }

        var when = at ?? _clock();
        var reference = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var label = description ?? $"Transfer {source.Number} -> {destination.Number}";

        var debit = source.Post(TransactionType.TransferOut, new Money(amount, Currency), label, when, reference);
        try
        {
            var credit = destination.Post(TransactionType.TransferIn, new Money(amount, Currency), label, when, reference);
            return (debit, credit);
        }
        catch (LedgerException)
        {
            // The debit already landed, so put it back rather than leaving the
            // money nowhere. The reversal is its own entry: history is never edited.
            source.Post(TransactionType.TransferIn, debit.Amount, $"Reversal of {reference}", when, reference);
            throw;
        }
    }

    /// <summary>
    /// Posts one month of interest to every savings account with a positive balance.
    /// </summary>
    /// <param name="annualRatePercent">Yearly rate, e.g. 8.5 for 8.5%.</param>
    /// <param name="at">When to date the entries. Defaults to the ledger clock.</param>
    /// <returns>The entries written, one per account that earned anything.</returns>
    public IReadOnlyList<Transaction> PostMonthlyInterest(decimal annualRatePercent, DateTimeOffset? at = null)
    {
        if (annualRatePercent < 0m)
        {
            throw new LedgerException("interest rate cannot be negative");
        }

        var when = at ?? _clock();
        var monthly = annualRatePercent / 12m / 100m;
        var posted = new List<Transaction>();

        foreach (var account in _accounts.Values.Where(a => a.Type == AccountType.Savings && !a.IsFrozen))
        {
            var interest = (account.Balance * monthly).Rounded();
            if (interest.Amount <= 0m)
            {
                continue;
            }

            posted.Add(account.Post(
                TransactionType.Interest,
                interest,
                string.Create(CultureInfo.InvariantCulture, $"Interest at {annualRatePercent:0.##}% p.a."),
                when,
                null));
        }

        return posted;
    }

    public Statement StatementFor(string number, DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            throw new LedgerException("statement end date is before its start date");
        }

        var account = Get(number);
        var before = account.Entries.Where(e => e.At < from).ToList();
        var inPeriod = account.Entries.Where(e => e.At >= from && e.At <= to).ToList();

        var opening = before.Count == 0 ? Money.Zero(Currency) : before[^1].BalanceAfter;
        var closing = inPeriod.Count == 0 ? opening : inPeriod[^1].BalanceAfter;

        return new Statement(account.Number, account.Holder, from, to, opening, closing, inPeriod);
    }

    /// <summary>Every entry in the ledger as CSV, oldest first, for import into a spreadsheet.</summary>
    public string ToCsv()
    {
        var csv = new StringBuilder();
        csv.AppendLine("date,account,holder,type,description,reference,amount,signed_amount,balance_after,currency");

        var rows = _accounts.Values
            .SelectMany(account => account.Entries.Select(entry => (account, entry)))
            .OrderBy(row => row.entry.At)
            .ThenBy(row => row.account.Number, StringComparer.Ordinal);

        foreach (var (account, entry) in rows)
        {
            csv.Append(CultureInfo.InvariantCulture, $"{entry.At:yyyy-MM-dd HH:mm:ss},");
            csv.Append(CultureInfo.InvariantCulture, $"{account.Number},");
            csv.Append(CultureInfo.InvariantCulture, $"{Quote(account.Holder)},");
            csv.Append(CultureInfo.InvariantCulture, $"{entry.Type},");
            csv.Append(CultureInfo.InvariantCulture, $"{Quote(entry.Description)},");
            csv.Append(CultureInfo.InvariantCulture, $"{entry.Reference},");
            csv.Append(CultureInfo.InvariantCulture, $"{entry.Amount.Amount:0.00},");
            csv.Append(CultureInfo.InvariantCulture, $"{entry.SignedAmount.Amount:0.00},");
            csv.Append(CultureInfo.InvariantCulture, $"{entry.BalanceAfter.Amount:0.00},");
            csv.AppendLine(entry.Amount.Currency);
        }

        return csv.ToString();
    }

    /// <summary>Replays every account and reports any whose stored balance disagrees with its entries.</summary>
    public IReadOnlyList<string> Audit()
    {
        var problems = new List<string>();
        foreach (var account in _accounts.Values)
        {
            var replayed = account.Recompute();
            if (replayed != account.Balance)
            {
                problems.Add($"{account.Number}: entries add to {replayed} but balance says {account.Balance}");
            }

            if (account.Type == AccountType.Savings && account.Balance.IsNegative)
            {
                problems.Add($"{account.Number}: savings account is negative ({account.Balance})");
            }

            var floor = Money.Zero(Currency) - account.OverdraftLimit;
            if (account.Balance < floor)
            {
                problems.Add($"{account.Number}: balance {account.Balance} is past the overdraft floor {floor}");
            }
        }

        return problems;
    }

    private static string Quote(string value) =>
        value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
}
