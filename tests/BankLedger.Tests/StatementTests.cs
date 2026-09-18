using Xunit;

namespace BankLedger.Tests;

public class StatementTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Opens_at_the_balance_carried_in_and_closes_at_the_last_entry()
    {
        var ledger = new Ledger("PKR", () => Start);
        var account = ledger.Open("Noor");

        ledger.Deposit(account.Number, 10000m, "December salary", Start);
        ledger.Withdraw(account.Number, 2000m, "Groceries", Start.AddDays(40));
        ledger.Deposit(account.Number, 500m, "Refund", Start.AddDays(45));

        var february = ledger.StatementFor(account.Number, Start.AddDays(31), Start.AddDays(58));

        Assert.Equal(new Money(10000m), february.Opening);
        Assert.Equal(new Money(8500m), february.Closing);
        Assert.Equal(2, february.Count);
        Assert.Equal(new Money(500m), february.TotalCredits);
        Assert.Equal(new Money(2000m), february.TotalDebits);
        Assert.True(february.Balances());
    }

    [Fact]
    public void A_period_with_no_entries_opens_and_closes_at_the_same_figure()
    {
        var ledger = new Ledger("PKR", () => Start);
        var account = ledger.Open("Noor");
        ledger.Deposit(account.Number, 7500m, "Opening deposit", Start);

        var quiet = ledger.StatementFor(account.Number, Start.AddDays(60), Start.AddDays(90));

        Assert.Empty(quiet.Entries);
        Assert.Equal(quiet.Opening, quiet.Closing);
        Assert.True(quiet.Balances());
    }

    [Fact]
    public void Refuses_a_period_that_ends_before_it_starts()
    {
        var ledger = new Ledger("PKR", () => Start);
        var account = ledger.Open("Noor");

        Assert.Throws<LedgerException>(() => ledger.StatementFor(account.Number, Start.AddDays(10), Start));
    }

    [Fact]
    public void Printed_statement_shows_every_entry_and_the_totals()
    {
        var ledger = new Ledger("PKR", () => Start);
        var account = ledger.Open("Noor");
        ledger.Deposit(account.Number, 1000m, "Salary", Start);
        ledger.Withdraw(account.Number, 400m, "Bill", Start.AddDays(1));

        var text = ledger.StatementFor(account.Number, Start, Start.AddDays(2)).ToText();

        Assert.Contains("Statement for ACC-0001 — Noor", text, StringComparison.Ordinal);
        Assert.Contains("Salary", text, StringComparison.Ordinal);
        Assert.Contains("+1,000.00", text, StringComparison.Ordinal);
        Assert.Contains("-400.00", text, StringComparison.Ordinal);
        Assert.Contains("Closing balance", text, StringComparison.Ordinal);
        Assert.Contains("600.00", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_has_a_header_and_one_row_per_entry_across_all_accounts()
    {
        var ledger = new Ledger("PKR", () => Start);
        var a = ledger.Open("Noor");
        var b = ledger.Open("Rizwan & Sons, Traders");
        ledger.Deposit(a.Number, 5000m, "Salary", Start);
        ledger.Transfer(a.Number, b.Number, 1500m, "Invoice 44", Start.AddDays(1));

        var lines = ledger.ToCsv().TrimEnd().Split('\n');

        Assert.StartsWith("date,account,holder,type,description", lines[0], StringComparison.Ordinal);
        Assert.Equal(4, lines.Length); // header + deposit + transfer out + transfer in
        Assert.Contains("\"Rizwan & Sons, Traders\"", ledger.ToCsv(), StringComparison.Ordinal);
        Assert.Contains("-1500.00", ledger.ToCsv(), StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_rows_are_oldest_first()
    {
        var ledger = new Ledger("PKR", () => Start);
        var account = ledger.Open("Noor");
        ledger.Deposit(account.Number, 100m, "Second", Start.AddDays(5));
        ledger.Deposit(account.Number, 100m, "First", Start.AddDays(1));

        var lines = ledger.ToCsv().TrimEnd().Split('\n');

        Assert.Contains("First", lines[1], StringComparison.Ordinal);
        Assert.Contains("Second", lines[2], StringComparison.Ordinal);
    }
}
