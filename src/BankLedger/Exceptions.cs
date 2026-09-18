namespace BankLedger;

public class LedgerException : Exception
{
    public LedgerException(string message) : base(message) { }
}

public sealed class InsufficientFundsException : LedgerException
{
    public InsufficientFundsException(string accountNumber, Money requested, Money available)
        : base($"{accountNumber}: cannot withdraw {requested}, available {available}")
    {
        AccountNumber = accountNumber;
        Requested = requested;
        Available = available;
    }

    public string AccountNumber { get; }
    public Money Requested { get; }
    public Money Available { get; }
}

public sealed class AccountNotFoundException : LedgerException
{
    public AccountNotFoundException(string accountNumber)
        : base($"no account numbered {accountNumber}") { }
}

public sealed class AccountFrozenException : LedgerException
{
    public AccountFrozenException(string accountNumber)
        : base($"{accountNumber} is frozen; unfreeze it before posting") { }
}
