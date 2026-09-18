using System.Globalization;
using System.Text;

namespace BankLedger;

/// <summary>A period of one account's history, with the totals a reader expects.</summary>
public sealed record Statement(
    string AccountNumber,
    string Holder,
    DateTimeOffset From,
    DateTimeOffset To,
    Money Opening,
    Money Closing,
    IReadOnlyList<Transaction> Entries)
{
    public Money TotalCredits => Sum(Entries.Where(e => e.IsCredit));

    public Money TotalDebits => Sum(Entries.Where(e => !e.IsCredit));

    public int Count => Entries.Count;

    /// <summary>Opening + credits - debits must equal Closing, or the statement is wrong.</summary>
    public bool Balances() => (Opening + TotalCredits - TotalDebits) == Closing;

    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Statement for {AccountNumber} — {Holder}");
        text.AppendLine(CultureInfo.InvariantCulture, $"{From:yyyy-MM-dd} to {To:yyyy-MM-dd}");
        text.AppendLine(new string('-', 78));
        text.AppendLine(CultureInfo.InvariantCulture, $"{"Date",-12}{"Type",-14}{"Description",-28}{"Amount",12}{"Balance",12}");
        text.AppendLine(new string('-', 78));
        text.AppendLine(CultureInfo.InvariantCulture, $"{From:yyyy-MM-dd}  {"Opening balance",-40}{Opening.Amount,12:N2}");

        foreach (var entry in Entries)
        {
            var sign = entry.IsCredit ? "+" : "-";
            var description = entry.Description.Length > 27 ? entry.Description[..27] : entry.Description;
            text.AppendLine(CultureInfo.InvariantCulture,
                $"{entry.At:yyyy-MM-dd}  {entry.Type,-14}{description,-28}{sign + entry.Amount.Amount.ToString("N2", CultureInfo.InvariantCulture),11}{entry.BalanceAfter.Amount,12:N2}");
        }

        text.AppendLine(new string('-', 78));
        text.AppendLine(CultureInfo.InvariantCulture, $"{"Credits",-40}{TotalCredits.Amount,12:N2}");
        text.AppendLine(CultureInfo.InvariantCulture, $"{"Debits",-40}{TotalDebits.Amount,12:N2}");
        text.AppendLine(CultureInfo.InvariantCulture, $"{"Closing balance",-40}{Closing.Amount,12:N2}");
        return text.ToString();
    }

    private Money Sum(IEnumerable<Transaction> entries)
    {
        var total = Money.Zero(Closing.Currency);
        foreach (var entry in entries)
        {
            total += entry.Amount;
        }

        return total;
    }
}
