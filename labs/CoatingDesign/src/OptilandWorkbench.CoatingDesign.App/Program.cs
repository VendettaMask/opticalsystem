using System.Text.Json;
using Avalonia;
using OptilandWorkbench.CoatingDesign.Engine;

namespace OptilandWorkbench.CoatingDesign.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--batch-examples")
        {
            RunExamplesAsync(args.Length > 1 ? args[1] : "coating-examples").GetAwaiter().GetResult();
            return 0;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    private static async Task RunExamplesAsync(string directory)
    {
        var service = new DesignService();
        var summaries = new List<object>();
        foreach (var kind in Enum.GetValues<DesignKind>())
        {
            var input = Examples.Create(kind);
            var destination = Path.Combine(directory, kind.ToString());
            Directory.CreateDirectory(destination);
            await ExperimentStore.SaveAsync(input, Path.Combine(destination, "input.coating.json"));
            var seed = service.Generate(input);
            await ExperimentStore.SaveAsync(seed, Path.Combine(destination, "initial.coating.json"));
            var output = service.Optimize(seed);
            await ExperimentStore.ExportAsync(output, destination);
            var reopened = await ExperimentStore.OpenAsync(Path.Combine(destination, "experiment.coating.json"));
            var same = reopened.Result!.Spectrum.SequenceEqual(output.Result!.Spectrum);
            summaries.Add(new { kind, output.Name, output.Result.Passed, output.Result.Metrics, output.Result.Checks,
                output.Result.SamplingConverged, output.Result.VerificationSamples, output.Result.Run, ReopenIdentical = same });
            Console.WriteLine($"{kind}: passed={output.Result.Passed}; layers={output.Layers.Length}; evaluations={output.Result.Run.Evaluations}; reopenIdentical={same}");
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "verification.json"), JsonSerializer.Serialize(summaries, Experiment.JsonOptions));
    }
}
