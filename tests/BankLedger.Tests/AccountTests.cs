using Xunit;

namespace BankLedger.Tests;

public class AccountTests
{
    private static readonly DateTimeOffset Fixed = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

    private static Ledger NewLedger() => new("PKR", () => Fixed);

    [Fact]
    public void Starts_empty_at_zero()
    {
        var account = NewLedger().Open("Bilal");

        Assert.Empty(account.Entries);
        Assert.Equal(Money.Zero(), account.Balance);
        Assert.Equal(Fixed, account.OpenedAt);
        Assert.False(account.IsFrozen);
    }

    [Fact]
    public void Balance_is_always_the_last_entry()
    {
        var ledger = NewLedger();
        var account = ledger.Open("Bilal");

        ledger.Deposit(account.Number, 500m);
        ledger.Deposit(account.Number, 250m);
        ledger.Withdraw(account.Number, 100m);

        Assert.Equal(new Money(650m), account.Balance);
        Assert.Equal(account.Entries[^1].BalanceAfter, account.Balance);
    }

    [Fact]
    public void Replaying_every_entry_reproduces_the_balance()
    {
        var ledger = NewLedger();
        var account = ledger.Open("Bilal");

        ledger.Deposit(account.Number, 1000m);
        ledger.Withdraw(account.Number, 333.33m);
        ledger.Fee(account.Number, 12.67m);

        Assert.Equal(account.Balance, account.Recompute());
    }

    [Fact]
    public void Checking_may_go_negative_up_to_its_overdraft()
    {
        var ledger = NewLedger();
        var account = ledger.Open("Bilal", AccountType.Checking, overdraftLimit: 2000m);

        ledger.Deposit(account.Number, 500m);
        ledger.Withdraw(account.Number, 2500m);

        Assert.Equal(new Money(-2000m), account.Balance);
    }

    [Fact]
    public void One_rupee_past_the_overdraft_is_refused()
    {
        var ledger = NewLedger();
        var account = ledger.Open("Bilal", AccountType.Checking, overdraftLimit: 2000m);
        ledger.Deposit(account.Number, 500m);

        var error = Assert.Throws<InsufficientFundsException>(() => ledger.Withdraw(account.Number, 2500.01m));

        Assert.Equal(account.Number, error.AccountNumber);
        Assert.Equal(new Money(2500.01m), error.Requested);
        Assert.Equal(new Money(2500m), error.Available);
        Assert.Equal(new Money(500m), account.Balance);
        Assert.Single(account.Entries);
    }

    [Fact]
    public void Savings_cannot_go_negative_at_all()
    {
        var ledger = NewLedger();
        var account = ledger.Open("Bilal", AccountType.Savings);
        ledger.Deposit(account.Number, 100m);

        Assert.Throws<InsufficientFundsException>(() => ledger.Withdraw(account.Number, 100.01m));
    }

    [Fact]
    public void A_frozen_account_refuses_every_posting()
    {
        var ledger = NewLedger();
        var account = ledger.Open("Bilal");
        ledger.Deposit(account.Number, 100m);
        account.Freeze();

        Assert.Throws<AccountFrozenException>(() => ledger.Deposit(account.Number, 10m));
        Assert.Throws<AccountFrozenException>(() => ledger.Withdraw(account.Number, 10m));

        account.Unfreeze();
        ledger.Deposit(account.Number, 10m);
        Assert.Equal(new Money(110m), account.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Amounts_must_be_positive(decimal amount)
    {
        var ledger = NewLedger();
        var account = ledger.Open("Bilal");

        Assert.Throws<LedgerException>(() => ledger.Deposit(account.Number, amount));
    }

    [Fact]
    public void Available_is_balance_plus_overdraft()
    {
        var ledger = NewLedger();
        var account = ledger.Open("Bilal", AccountType.Checking, overdraftLimit: 1000m);
        ledger.Deposit(account.Number, 250m);

        Assert.Equal(new Money(1250m), account.Available);
    }

    [Fact]
    public void Entries_record_type_direction_and_running_balance()
    {
        var ledger = NewLedger();
        var account = ledger.Open("Bilal");

        var deposit = ledger.Deposit(account.Number, 400m, "Salary");
        var fee = ledger.Fee(account.Number, 25m);

        Assert.True(deposit.IsCredit);
        Assert.False(fee.IsCredit);
        Assert.Equal(new Money(-25m), fee.SignedAmount);
        Assert.Equal("Salary", deposit.Description);
        Assert.Equal(new Money(375m), fee.BalanceAfter);
        Assert.NotEqual(deposit.Id, fee.Id);
    }
}
