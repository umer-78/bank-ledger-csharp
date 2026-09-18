using BankLedger.Cli;
using Xunit;

namespace BankLedger.Tests;

public class ShellTests
{
    private static (Shell Shell, StringWriter Output) NewShell()
    {
        var output = new StringWriter();
        var ledger = new Ledger("PKR", () => new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero));
        return (new Shell(ledger, output), output);
    }

    private static string Run(params string[] commands)
    {
        var (shell, output) = NewShell();
        foreach (var command in commands)
        {
            shell.Execute(command);
        }

        return output.ToString();
    }

    [Fact]
    public void Opens_an_account_and_lists_it()
    {
        var text = Run("open Ayesha savings", "deposit ACC-0001 2500", "list");

        Assert.Contains("opened ACC-0001 for Ayesha (savings)", text, StringComparison.Ordinal);
        Assert.Contains("PKR 2,500.00", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_an_overdrawn_withdrawal_instead_of_crashing()
    {
        var text = Run("open Ayesha", "deposit ACC-0001 100", "withdraw ACC-0001 500");

        Assert.Contains("error:", text, StringComparison.Ordinal);
        Assert.Contains("cannot withdraw", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_non_numeric_amount()
    {
        var text = Run("open Ayesha", "deposit ACC-0001 lots");

        Assert.Contains("that is not a number", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_an_unknown_command()
    {
        Assert.Contains("unknown command 'frobnicate'", Run("frobnicate"), StringComparison.Ordinal);
    }

    [Fact]
    public void Quit_stops_the_loop()
    {
        var (shell, _) = NewShell();

        Assert.False(shell.Execute("quit"));
        Assert.True(shell.Execute("help"));
        Assert.True(shell.Execute("   "));
    }

    [Fact]
    public void Demo_seeds_three_accounts_that_audit_clean()
    {
        var text = Run("demo", "audit");

        Assert.Contains("seeded 3 accounts and 7 entries", text, StringComparison.Ordinal);
        Assert.Contains("audit clean: 3 account(s)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BREAK", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Demo_statement_shows_the_worked_figures()
    {
        var text = Run("demo", "statement ACC-0001");

        Assert.Contains("March salary", text, StringComparison.Ordinal);
        Assert.Contains("103,800.00", text, StringComparison.Ordinal);
        Assert.Contains("2026-03-01 to 2026-03-11", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Freeze_blocks_posting_until_unfrozen()
    {
        var text = Run("open Ayesha", "deposit ACC-0001 100", "freeze ACC-0001", "deposit ACC-0001 50", "unfreeze ACC-0001", "deposit ACC-0001 50", "list");

        Assert.Contains("ACC-0001 frozen", text, StringComparison.Ordinal);
        Assert.Contains("is frozen", text, StringComparison.Ordinal);
        Assert.Contains("PKR 150.00", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Transfer_prints_both_new_balances()
    {
        var text = Run("open Ayesha", "open Hamza", "deposit ACC-0001 5000", "transfer ACC-0001 ACC-0002 2000");

        Assert.Contains("ACC-0001 now PKR 3,000.00", text, StringComparison.Ordinal);
        Assert.Contains("ACC-0002 now PKR 2,000.00", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Interest_reports_how_many_accounts_earned()
    {
        var text = Run("open Ayesha savings", "deposit ACC-0001 12000", "interest 9");

        Assert.Contains("posted interest to 1 account(s)", text, StringComparison.Ordinal);
        Assert.Contains("PKR 90.00", text, StringComparison.Ordinal); // 12000 * 9% / 12
    }

    [Fact]
    public void Csv_writes_to_a_file_when_given_a_path()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ledger-{Guid.NewGuid():N}.csv");
        try
        {
            var text = Run("demo", $"csv {path}");

            Assert.Contains($"wrote {path}", text, StringComparison.Ordinal);
            var csv = File.ReadAllLines(path);
            Assert.Equal(9, csv.Length); // header + 8 entries
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_arguments_are_reported_not_thrown()
    {
        var text = Run("deposit");

        Assert.Contains("missing argument", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_ledger_says_so()
    {
        Assert.Contains("no accounts yet", Run("list"), StringComparison.Ordinal);
    }

    [Fact]
    public void Help_lists_every_command()
    {
        var text = Run("help");

        foreach (var command in new[] { "open", "deposit", "withdraw", "transfer", "interest", "statement", "audit", "csv" })
        {
            Assert.Contains(command, text, StringComparison.Ordinal);
        }
    }
}
