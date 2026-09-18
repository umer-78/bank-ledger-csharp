using Xunit;

namespace BankLedger.Tests;

public class LedgerTests
{
    private static readonly DateTimeOffset March = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static Ledger NewLedger() => new("PKR", () => March);

    [Fact]
    public void Numbers_accounts_in_order()
    {
        var ledger = NewLedger();

        Assert.Equal("ACC-0001", ledger.Open("One").Number);
        Assert.Equal("ACC-0002", ledger.Open("Two").Number);
        Assert.Equal(2, ledger.Count);
    }

    [Fact]
    public void Trims_the_holder_name_and_rejects_a_blank_one()
    {
        var ledger = NewLedger();

        Assert.Equal("Sana Iqbal", ledger.Open("  Sana Iqbal  ").Holder);
        Assert.Throws<LedgerException>(() => ledger.Open("   "));
    }

    [Fact]
    public void Savings_may_not_have_an_overdraft()
    {
        var ledger = NewLedger();

        Assert.Throws<LedgerException>(() => ledger.Open("Sana", AccountType.Savings, 500m));
        Assert.Throws<LedgerException>(() => ledger.Open("Sana", AccountType.Checking, -1m));
    }

    [Fact]
    public void Unknown_account_numbers_are_reported_by_name()
    {
        var ledger = NewLedger();

        var error = Assert.Throws<AccountNotFoundException>(() => ledger.Get("ACC-9999"));
        Assert.Contains("ACC-9999", error.Message, StringComparison.Ordinal);
        Assert.False(ledger.Has("ACC-9999"));
    }

    [Fact]
    public void Transfer_writes_both_legs_with_one_reference()
    {
        var ledger = NewLedger();
        var from = ledger.Open("Sana");
        var to = ledger.Open("Hamza");
        ledger.Deposit(from.Number, 5000m);

        var (debit, credit) = ledger.Transfer(from.Number, to.Number, 1200m);

        Assert.Equal(TransactionType.TransferOut, debit.Type);
        Assert.Equal(TransactionType.TransferIn, credit.Type);
        Assert.Equal(debit.Reference, credit.Reference);
        Assert.Equal(new Money(3800m), from.Balance);
        Assert.Equal(new Money(1200m), to.Balance);
    }

    [Fact]
    public void Transfer_that_overdraws_leaves_both_accounts_untouched()
    {
        var ledger = NewLedger();
        var from = ledger.Open("Sana");
        var to = ledger.Open("Hamza");
        ledger.Deposit(from.Number, 100m);

        Assert.Throws<InsufficientFundsException>(() => ledger.Transfer(from.Number, to.Number, 500m));

        Assert.Equal(new Money(100m), from.Balance);
        Assert.Equal(Money.Zero(), to.Balance);
        Assert.Empty(to.Entries);
    }

    [Fact]
    public void Transfer_to_a_frozen_account_takes_nothing_from_the_source()
    {
        var ledger = NewLedger();
        var from = ledger.Open("Sana");
        var to = ledger.Open("Hamza");
        ledger.Deposit(from.Number, 5000m);
        to.Freeze();

        Assert.Throws<AccountFrozenException>(() => ledger.Transfer(from.Number, to.Number, 1000m));

        Assert.Equal(new Money(5000m), from.Balance);
        Assert.Single(from.Entries);
    }

    [Fact]
    public void An_account_cannot_transfer_to_itself()
    {
        var ledger = NewLedger();
        var account = ledger.Open("Sana");
        ledger.Deposit(account.Number, 100m);

        Assert.Throws<LedgerException>(() => ledger.Transfer(account.Number, account.Number, 10m));
    }

    [Fact]
    public void Interest_is_one_twelfth_of_the_annual_rate()
    {
        var ledger = NewLedger();
        var savings = ledger.Open("Sana", AccountType.Savings);
        ledger.Deposit(savings.Number, 60000m);

        var posted = ledger.PostMonthlyInterest(8.5m);

        // 60000 * 8.5% / 12 = 425.00 exactly.
        Assert.Single(posted);
        Assert.Equal(new Money(425m), posted[0].Amount);
        Assert.Equal(new Money(60425m), savings.Balance);
        Assert.Equal(TransactionType.Interest, posted[0].Type);
    }

    [Fact]
    public void Interest_skips_checking_accounts_and_empty_savings()
    {
        var ledger = NewLedger();
        var checking = ledger.Open("Sana");
        var empty = ledger.Open("Hamza", AccountType.Savings);
        ledger.Deposit(checking.Number, 100000m);

        Assert.Empty(ledger.PostMonthlyInterest(10m));
        Assert.Empty(empty.Entries);
        Assert.Single(checking.Entries);
    }

    [Fact]
    public void Interest_skips_a_frozen_account_and_rejects_a_negative_rate()
    {
        var ledger = NewLedger();
        var savings = ledger.Open("Sana", AccountType.Savings);
        ledger.Deposit(savings.Number, 10000m);
        savings.Freeze();

        Assert.Empty(ledger.PostMonthlyInterest(12m));
        Assert.Throws<LedgerException>(() => ledger.PostMonthlyInterest(-1m));
    }

    [Fact]
    public void Total_held_is_the_sum_of_every_balance()
    {
        var ledger = NewLedger();
        var a = ledger.Open("Sana");
        var b = ledger.Open("Hamza");
        ledger.Deposit(a.Number, 1000m);
        ledger.Deposit(b.Number, 250.50m);

        Assert.Equal(new Money(1250.50m), ledger.TotalHeld());
    }

    [Fact]
    public void A_transfer_moves_money_without_creating_or_destroying_any()
    {
        var ledger = NewLedger();
        var a = ledger.Open("Sana");
        var b = ledger.Open("Hamza");
        ledger.Deposit(a.Number, 9000m);
        var before = ledger.TotalHeld();

        ledger.Transfer(a.Number, b.Number, 3333.33m);

        Assert.Equal(before, ledger.TotalHeld());
    }

    [Fact]
    public void Audit_is_clean_on_a_healthy_ledger()
    {
        var ledger = NewLedger();
        var a = ledger.Open("Sana", AccountType.Checking, 1000m);
        var b = ledger.Open("Hamza", AccountType.Savings);
        ledger.Deposit(a.Number, 500m);
        ledger.Transfer(a.Number, b.Number, 1200m);
        ledger.PostMonthlyInterest(6m);

        Assert.Empty(ledger.Audit());
    }
}
