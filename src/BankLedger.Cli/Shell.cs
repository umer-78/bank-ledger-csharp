using System.Globalization;

namespace BankLedger.Cli;

/// <summary>
/// Reads commands and runs them against one ledger.
/// </summary>
/// <remarks>
/// Writing to an injected <see cref="TextWriter"/> rather than Console directly
/// is what lets the tests drive the whole shell and read back exactly what a user
/// would see.
/// </remarks>
public sealed class Shell
{
    private readonly Ledger _ledger;
    private readonly TextWriter _out;

    public Shell(Ledger ledger, TextWriter output)
    {
        _ledger = ledger;
        _out = output;
    }

    public static string Banner =>
        "BankLedger — double-entry account ledger\nType 'help' for commands, 'demo' for a worked example, 'quit' to leave.";

    public static string Help => string.Join('\n',
        "  open <holder> [checking|savings] [overdraft]   open an account",
        "  deposit <acc> <amount> [description]           pay money in",
        "  withdraw <acc> <amount> [description]          take money out",
        "  transfer <from> <to> <amount>                  move money between accounts",
        "  fee <acc> <amount> [description]               charge a fee",
        "  interest <annual %>                            post a month of interest to savings",
        "  freeze <acc> | unfreeze <acc>                  block or allow posting",
        "  list                                           show every account",
        "  statement <acc>                                print a full statement",
        "  audit                                          replay every entry and report breaks",
        "  csv [path]                                     export every entry as CSV",
        "  demo                                           seed a worked example",
        "  help | quit");

    /// <summary>Runs one command. Returns false when the shell should stop.</summary>
    public bool Execute(string line)
    {
        var parts = Split(line);
        if (parts.Length == 0)
        {
            return true;
        }

        try
        {
            return Dispatch(parts);
        }
        catch (LedgerException error)
        {
            _out.WriteLine($"error: {error.Message}");
            return true;
        }
        catch (FormatException)
        {
            _out.WriteLine("error: that is not a number");
            return true;
        }
    }

    private bool Dispatch(string[] parts)
    {
        switch (parts[0].ToLowerInvariant())
        {
            case "quit":
            case "exit":
                return false;

            case "help":
                _out.WriteLine(Help);
                return true;

            case "open":
                Open(parts);
                return true;

            case "deposit":
                Show(_ledger.Deposit(Arg(parts, 1), Amount(parts, 2), Rest(parts, 3, "Deposit")));
                return true;

            case "withdraw":
                Show(_ledger.Withdraw(Arg(parts, 1), Amount(parts, 2), Rest(parts, 3, "Withdrawal")));
                return true;

            case "fee":
                Show(_ledger.Fee(Arg(parts, 1), Amount(parts, 2), Rest(parts, 3, "Service fee")));
                return true;

            case "transfer":
                Transfer(parts);
                return true;

            case "interest":
                Interest(parts);
                return true;

            case "freeze":
                _ledger.Get(Arg(parts, 1)).Freeze();
                _out.WriteLine($"{Arg(parts, 1)} frozen");
                return true;

            case "unfreeze":
                _ledger.Get(Arg(parts, 1)).Unfreeze();
                _out.WriteLine($"{Arg(parts, 1)} unfrozen");
                return true;

            case "list":
                List();
                return true;

            case "statement":
                PrintStatement(parts);
                return true;

            case "audit":
                Audit();
                return true;

            case "csv":
                Csv(parts);
                return true;

            case "demo":
                Demo();
                return true;

            default:
                _out.WriteLine($"unknown command '{parts[0]}' — try 'help'");
                return true;
        }
    }

    /// <summary>Prints the whole history, dated from the account's own first and last entry
    /// rather than from DateTimeOffset.MinValue, which would put year 0001 on a bank statement.</summary>
    private void PrintStatement(string[] parts)
    {
        var account = _ledger.Get(Arg(parts, 1));
        var from = account.Entries.Count == 0 ? account.OpenedAt : account.Entries[0].At;
        var to = account.Entries.Count == 0 ? account.OpenedAt : account.Entries[^1].At;
        _out.Write(_ledger.StatementFor(account.Number, from, to).ToText());
    }

    private void Open(string[] parts)
    {
        var holder = Arg(parts, 1);
        var type = parts.Length > 2 && parts[2].Equals("savings", StringComparison.OrdinalIgnoreCase)
            ? AccountType.Savings
            : AccountType.Checking;
        var overdraft = parts.Length > 3 ? decimal.Parse(parts[3], CultureInfo.InvariantCulture) : 0m;

        var account = _ledger.Open(holder, type, overdraft);
        _out.WriteLine($"opened {account.Number} for {account.Holder} ({type.ToString().ToLowerInvariant()})");
    }

    private void Transfer(string[] parts)
    {
        var (debit, credit) = _ledger.Transfer(Arg(parts, 1), Arg(parts, 2), Amount(parts, 3));
        _out.WriteLine($"moved {debit.Amount} — {Arg(parts, 1)} now {debit.BalanceAfter}, {Arg(parts, 2)} now {credit.BalanceAfter} (ref {debit.Reference})");
    }

    private void Interest(string[] parts)
    {
        var rate = parts.Length > 1 ? decimal.Parse(parts[1], CultureInfo.InvariantCulture) : 0m;
        var posted = _ledger.PostMonthlyInterest(rate);
        _out.WriteLine($"posted interest to {posted.Count} account(s)");
        foreach (var entry in posted)
        {
            _out.WriteLine($"  {entry.Amount} → balance {entry.BalanceAfter}");
        }
    }

    private void List()
    {
        if (_ledger.Count == 0)
        {
            _out.WriteLine("no accounts yet — try 'open Ayesha savings'");
            return;
        }

        _out.WriteLine($"{"Account",-10}{"Holder",-18}{"Type",-10}{"Entries",8}{"Balance",16}");
        foreach (var account in _ledger.Accounts.OrderBy(a => a.Number, StringComparer.Ordinal))
        {
            var flag = account.IsFrozen ? " [frozen]" : string.Empty;
            _out.WriteLine($"{account.Number,-10}{account.Holder,-18}{account.Type,-10}{account.Entries.Count,8}{account.Balance.ToString(),16}{flag}");
        }

        _out.WriteLine($"{"",-46}{"total",8}{_ledger.TotalHeld().ToString(),16}");
    }

    private void Audit()
    {
        var problems = _ledger.Audit();
        if (problems.Count == 0)
        {
            _out.WriteLine($"audit clean: {_ledger.Count} account(s), every balance matches its entries");
            return;
        }

        foreach (var problem in problems)
        {
            _out.WriteLine($"BREAK {problem}");
        }
    }

    private void Csv(string[] parts)
    {
        var csv = _ledger.ToCsv();
        if (parts.Length > 1)
        {
            File.WriteAllText(parts[1], csv);
            _out.WriteLine($"wrote {parts[1]}");
            return;
        }

        _out.Write(csv);
    }

    private void Demo()
    {
        var start = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var salary = _ledger.Open("Ayesha Khan", AccountType.Checking, 5000m);
        var savings = _ledger.Open("Ayesha Khan", AccountType.Savings);
        var shop = _ledger.Open("Gulberg Hardware");

        _ledger.Deposit(salary.Number, 185000m, "March salary", start);
        _ledger.Transfer(salary.Number, savings.Number, 60000m, "Monthly saving", start.AddDays(1));
        _ledger.Withdraw(salary.Number, 12500m, "Rent top-up", start.AddDays(3));
        _ledger.Transfer(salary.Number, shop.Number, 8450m, "Paint and fittings", start.AddDays(6));
        _ledger.Fee(salary.Number, 250m, "Card annual fee", start.AddDays(10));
        _ledger.PostMonthlyInterest(8.5m, start.AddDays(30));

        _out.WriteLine("seeded 3 accounts and 7 entries");
        List();
    }

    private void Show(Transaction entry) =>
        _out.WriteLine($"{entry.Type} {entry.Amount} — balance {entry.BalanceAfter}");

    private static string Arg(string[] parts, int index) =>
        index < parts.Length ? parts[index] : throw new LedgerException("missing argument — try 'help'");

    private static decimal Amount(string[] parts, int index) =>
        decimal.Parse(Arg(parts, index), CultureInfo.InvariantCulture);

    private static string Rest(string[] parts, int index, string fallback) =>
        parts.Length > index ? string.Join(' ', parts[index..]) : fallback;

    private static string[] Split(string line) =>
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
