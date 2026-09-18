namespace BankLedger;

public enum TransactionType
{
    Deposit,
    Withdrawal,
    TransferIn,
    TransferOut,
    Interest,
    Fee,
}

/// <summary>One entry in an account's history. Entries are never edited: a
/// mistake is corrected by posting the opposite entry, so the history stays true.</summary>
public sealed record Transaction(
    Guid Id,
    DateTimeOffset At,
    TransactionType Type,
    Money Amount,
    Money BalanceAfter,
    string Description,
    string? Reference = null)
{
    public bool IsCredit => Type is TransactionType.Deposit or TransactionType.TransferIn or TransactionType.Interest;

    public Money SignedAmount => IsCredit ? Amount : Amount * -1m;
}
