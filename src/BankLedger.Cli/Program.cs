using BankLedger;
using BankLedger.Cli;

var ledger = new Ledger("PKR");
var shell = new Shell(ledger, Console.Out);

if (args.Length > 0)
{
    // Non-interactive: each argument is one command, so the CLI is scriptable.
    foreach (var command in args)
    {
        if (!shell.Execute(command))
        {
            break;
        }
    }

    return 0;
}

Console.WriteLine(Shell.Banner);
while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (line is null || !shell.Execute(line))
    {
        break;
    }
}

Console.WriteLine("bye");
return 0;
