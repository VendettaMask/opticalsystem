using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Workbench;

if (args.Length == 2 && args[0] == "--workbench-worker")
{
    try { WorkbenchExecutor.Execute(JsonFiles.Read<WorkbenchJob>(args[1])); return 0; }
    catch (Exception e) { Console.Error.WriteLine(e); return 3; }
}
try
{
    var options = ComparisonOptions.Parse(args);
    if (options.ListAnalyses)
    {
        foreach (var e in AnalysisComparisonRegistry.Entries)
            Console.WriteLine($"{e.CanonicalAnalysisKey}\t{e.ZemaxAnalysisType ?? "—"}\t{e.Support}\t{e.Reason}");
        return 0;
    }
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    return await new ComparisonRunner().Run(options, cancellation.Token);
}
catch (OperationCanceledException) { return 4; }
catch (Exception e) when (e is ArgumentException or IOException or System.Text.Json.JsonException)
{ Console.Error.WriteLine(e.Message); return 2; }
catch (Exception e) { Console.Error.WriteLine(e); return 3; }
