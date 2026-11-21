using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using OpticalSystemDesign.Tests;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var product = Path.GetFullPath(args[0]);
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var dependencies = new[]
        {
            product, Path.Combine(packages, "avalonia.headless", "12.1.0", "lib", "net10.0"),
            Path.Combine(packages, "xunit.assert", "2.9.3", "lib", "net6.0"),
            Path.Combine(packages, "xunit.extensibility.core", "2.9.3", "lib", "netstandard1.1"),
            Path.Combine(packages, "xunit.abstractions", "2.0.3", "lib", "netstandard1.0")
        };
        AssemblyLoadContext.Default.Resolving += (_, name) => dependencies.Select(dir => Path.Combine(dir, name.Name + ".dll"))
            .Where(File.Exists).Select(AssemblyLoadContext.Default.LoadFromAssemblyPath).FirstOrDefault();
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
        {
            var path = Path.Combine(product, "runtimes", "win-x64", "native", name + (name.EndsWith(".dll") ? "" : ".dll"));
            return File.Exists(path) ? NativeLibrary.Load(path) : IntPtr.Zero;
        };
        await Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task Run()
    {
        var results = new List<object>();
        async Task Check(string name, Func<Task> action)
        {
            try { await action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
            catch (Exception error) { results.Add(new { name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + name + " " + error); }
        }
        Task Sync(Action action) { action(); return Task.CompletedTask; }
        var tests = new AthermalGlassMapTests();
        foreach (var (resonance, temperature) in new[] { (.17, 20d), (-.17, 25d), (0d, 35d) })
            await Check($"RelativeDerivative {resonance}/{temperature}", () => Sync(() => tests.RelativeDerivativeAgreesWithIndependentFiniteDifference(resonance, temperature)));
        await Check("Missing, nonfinite and singular TD", () => Sync(tests.ThermalModelRejectsMissingNonfiniteAndSingularDataWithoutGuessingUnits));
        await Check("Project wavelength, scale, range and retained outliers", () => Sync(tests.MapUsesDefinedWavelengthsTypedScaleAndHonestDefaultRange));
        await Check("Pressure applicability", () => Sync(tests.NonstandardEnvironmentPressureIsExplicitlyUnavailable));
        foreach (var (theme, width, scale) in new[] { ("Light", 1500, 1d), ("Dark", 1100, 1.5), ("Isekai", 900, 2d) })
            await Check("Native panel " + theme, () => tests.NativePanelKeepsFiniteOutliersAndAllowsDefaultAllRangeRoundTrip(theme, width, scale));
        var existing = new MaterialAnalysisTests();
        foreach (var kind in new[] { OpticalSystemDesign.Application.Contracts.MaterialAnalysisKind.GlassMap, OpticalSystemDesign.Application.Contracts.MaterialAnalysisKind.AthermalGlassMap })
            await Check("Existing finite catalog map " + kind, () => Sync(() => existing.CatalogMapsExposeFiniteGlassPoints(kind)));
        await Check("Existing reversed glass map", () => Sync(existing.GlassMapUsesConventionalReversedAbbeAxisAndLabels));
        var resultPath = Environment.GetEnvironmentVariable("ATHERMAL_RESULT_PATH") ?? Path.Combine(AppContext.BaseDirectory, "results.json");
        File.WriteAllText(resultPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        using (var app = OpticalSystemDesign.Application.Services.WorkbenchApplication.Create("cooke"))
        {
            var view = app.Materials.Analyze(new(OpticalSystemDesign.Application.Contracts.MaterialAnalysisKind.AthermalGlassMap));
            var summary = new
            {
                Protocol = "catalog-reference-temperature, 1 atm, project min/primary/max wavelengths; x in 1e-3, y in 1e-6/K",
                view.Rows, view.ReportText,
                Selected = view.Series.Single(s => s.Name.StartsWith("所选：", StringComparison.Ordinal)).Points,
                ProductAssemblies = new[] { typeof(OpticalSystemDesign.App.App).Assembly, typeof(OpticalSystemDesign.Application.Services.WorkbenchApplication).Assembly,
                    typeof(OpticalSystemDesign.Core.Materials.CatalogThermalOptics).Assembly }.Select(assembly => new
                    { assembly.FullName, Path = assembly.Location, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly.Location))) })
            };
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(resultPath)!, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        }
        if (results.Any(item => JsonSerializer.Serialize(item).Contains("\"passed\":false"))) Environment.ExitCode = 1;
    }
}
