# BankLedger

[![CI](https://github.com/umer-78/bank-ledger-csharp/actions/workflows/ci.yml/badge.svg)](https://github.com/umer-78/bank-ledger-csharp/actions/workflows/ci.yml)

**Live demo:** https://umer-78.github.io/bank-ledger-csharp/

An append-only account ledger in C# — deposits, withdrawals, transfers,
overdrafts, monthly interest, statements and CSV export, with a scriptable
console front end.

The point of the project is the part most toy banking examples get wrong:
**money is `decimal`, never `double`; entries are never edited; and the balance
is not a field that can drift** — it is whatever the last entry says, and
`audit` replays every entry from zero to prove the two still agree.

- .NET 8, no third-party runtime dependencies
- 56 tests, 89.6% line coverage / 85.2% branch coverage
- Warnings are errors, nullable reference types on

## Quick start

```bash
git clone https://github.com/umer-78/bank-ledger-csharp.git
cd bank-ledger-csharp
dotnet test                       # 56 tests
dotnet run --project src/BankLedger.Cli
```

Commands can also be passed as arguments, which makes the CLI scriptable:

```bash
dotnet run --project src/BankLedger.Cli -- demo "statement ACC-0001" audit
```

## A worked example

`demo` seeds three accounts and seven entries — a salary, a standing transfer to
savings, a cash withdrawal, a payment to a supplier, a card fee and one month of
interest:

```
$ dotnet run --project src/BankLedger.Cli -- demo
seeded 3 accounts and 7 entries
Account   Holder            Type       Entries         Balance
ACC-0001  Ayesha Khan       Checking         5  PKR 103,800.00
ACC-0002  Ayesha Khan       Savings          2   PKR 60,425.00
ACC-0003  Gulberg Hardware  Checking         1    PKR 8,450.00
                                                 total  PKR 172,675.00
```

The savings interest is exact, not approximate: 60,000 × 8.5% ÷ 12 = 425.00.

```
$ dotnet run --project src/BankLedger.Cli -- demo "statement ACC-0001"
Statement for ACC-0001 — Ayesha Khan
2026-03-01 to 2026-03-11
------------------------------------------------------------------------------
Date        Type          Description                       Amount     Balance
------------------------------------------------------------------------------
2026-03-01  Opening balance                                 0.00
2026-03-01  Deposit       March salary                +185,000.00  185,000.00
2026-03-02  TransferOut   Monthly saving               -60,000.00  125,000.00
2026-03-04  Withdrawal    Rent top-up                  -12,500.00  112,500.00
2026-03-07  TransferOut   Paint and fittings            -8,450.00  104,050.00
2026-03-11  Fee           Card annual fee                 -250.00  103,800.00
------------------------------------------------------------------------------
Credits                                   185,000.00
Debits                                     81,200.00
Closing balance                           103,800.00
```

An overdraft is a limit, not a suggestion — a checking account with a 5,000
overdraft and 500 in it stops at 5,500, and the refused withdrawal leaves no
entry behind:

```
$ dotnet run --project src/BankLedger.Cli -- "open Ayesha checking 5000" \
    "deposit ACC-0001 500" "withdraw ACC-0001 6000" list audit
opened ACC-0001 for Ayesha (checking)
Deposit PKR 500.00 — balance PKR 500.00
error: ACC-0001: cannot withdraw PKR 6,000.00, available PKR 5,500.00
Account   Holder            Type       Entries         Balance
ACC-0001  Ayesha            Checking         1      PKR 500.00
                                                 total      PKR 500.00
audit clean: 1 account(s), every balance matches its entries
```

## Commands

```
  open <holder> [checking|savings] [overdraft]   open an account
  deposit <acc> <amount> [description]           pay money in
  withdraw <acc> <amount> [description]          take money out
  transfer <from> <to> <amount>                  move money between accounts
  fee <acc> <amount> [description]               charge a fee
  interest <annual %>                            post a month of interest to savings
  freeze <acc> | unfreeze <acc>                  block or allow posting
  list                                           show every account
  statement <acc>                                print a full statement
  audit                                          replay every entry and report breaks
  csv [path]                                     export every entry as CSV
  demo                                           seed a worked example
  help | quit
```

## How it holds together

| Type | Job |
| --- | --- |
| `Money` | `decimal` amount + currency. Refuses to add rupees to dollars; rounds half **away from zero**, the convention statements use. |
| `Transaction` | One immutable entry: type, amount, running balance after it, description, optional transfer reference. |
| `Account` | The append-only entry list, the balance, the overdraft floor and the frozen flag. `Recompute()` replays the list from zero. |
| `Ledger` | Opens accounts, and is the only place a transfer, interest run or audit is expressed. |
| `Statement` | A date range of one account with opening/closing balances and totals, plus `Balances()` to check they reconcile. |
| `Shell` | Parses commands and writes to an injected `TextWriter`, so the tests drive the real front end. |

Three decisions worth calling out:

**Every posting goes through one method.** `Account.Post` is the only code that
appends an entry, so the overdraft rule, the frozen check, the currency check and
the positive-amount check each exist once. A new operation (a reversal, a standing
order) cannot accidentally skip them.

**A transfer checks both legs before moving anything.** The destination's frozen
state is tested before the source is debited, and if the credit still fails the
debit is reversed with its own compensating entry rather than being deleted. Money
is never in mid-air, and the history is never rewritten.

**The balance is derived, not stored.** `Balance` reads the last entry's
`BalanceAfter`. `audit` replays every account from zero and reports any account
whose entries no longer add up to its balance, whose savings balance has gone
negative, or that has slipped past its overdraft floor.

## Tests

```
$ dotnet test
Passed!  - Failed:     0, Passed:    56, Skipped:     0, Total:    56, Duration: 206 ms
```

Coverage, from `dotnet test --collect:"XPlat Code Coverage"`:

| Type | Line coverage |
| --- | --- |
| `Money` | 100.0% |
| `Transaction` | 100.0% |
| `Statement` | 100.0% |
| `Account` | 94.1% |
| `Shell` | 94.1% |
| `Ledger` | 90.6% |
| **Overall** | **89.6%** (85.2% branch) |

The uncovered remainder is `Program.cs`, the interactive read-line loop.

What the tests actually pin down, rather than just exercising:

- `0.1 + 0.2` is `0.3` — the test that fails the moment someone changes `Money`
  to `double`
- rounding half away from zero at `2.345`, `2.005` and `-2.345`
- one rupee past the overdraft limit is refused, and leaves the entry count unchanged
- a transfer into a frozen account takes nothing from the source
- a transfer neither creates nor destroys money: total held is identical before and after
- interest is exactly one twelfth of the annual rate, skips checking accounts,
  empty savings and frozen accounts
- a statement with no entries in the period opens and closes at the same figure
- CSV quotes a holder name containing a comma, and rows come out oldest first

## Layout

```
src/BankLedger/        the library — Money, Transaction, Account, Ledger, Statement
src/BankLedger.Cli/    the console front end
tests/BankLedger.Tests/ 56 xUnit tests
examples/              demo-ledger.csv, produced by `demo` + `csv`
```

## Licence

MIT — see [LICENSE](LICENSE).
