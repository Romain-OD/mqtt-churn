Options options;
try
{
    options = Options.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(Options.Usage);
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"run {options.RunId}: {options.Clients} clients -> {options.Broker}:{options.Port} at {options.Rate}/s");
var results = await Fleet.RunAsync(options, cts.Token);

Console.Write(Stats.Report(results));
if (options.Csv is { } path)
{
    await File.WriteAllTextAsync(path, Stats.Csv(results));
    Console.WriteLine($"csv -> {path}");
}
return results.Any(r => r.Error is not null) ? 1 : 0;
