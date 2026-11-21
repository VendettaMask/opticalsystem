using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var product = Path.GetFullPath(args[0]);
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var dependencies = new[] { product,
            Path.Combine(packages, "avalonia.headless", "12.1.0", "lib", "net10.0"),
            Path.Combine(packages, "xunit.assert", "2.9.3", "lib", "net6.0"),
            Path.Combine(packages, "xunit.extensibility.core", "2.9.3", "lib", "netstandard1.1"),
            Path.Combine(packages, "xunit.abstractions", "2.0.3", "lib", "netstandard1.0") };
        AssemblyLoadContext.Default.Resolving += (_, name) => dependencies.Select(dir => Path.Combine(dir, name.Name + ".dll"))
            .Where(File.Exists).Select(AssemblyLoadContext.Default.LoadFromAssemblyPath).FirstOrDefault();
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
        {
            var path = Path.Combine(product, "runtimes", "win-x64", "native", name + (name.EndsWith(".dll") ? "" : ".dll"));
            return File.Exists(path) ? NativeLibrary.Load(path) : IntPtr.Zero;
        };
        await Run(args[1]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task Run(string resultPath)
    {
        var results = new List<object>();
        var failed = false;
        foreach (var type in new[] { typeof(OpticalSystemDesign.Tests.SurfaceColorTests),
                     typeof(OpticalSystemDesign.Tests.SurfacePropertiesServiceTests), typeof(OpticalSystemDesign.Tests.SurfacePropertiesPanelTests),
                     typeof(OpticalSystemDesign.Tests.FullFieldHeatmapTests), typeof(OpticalSystemDesign.Tests.FullFieldAberrationAnalysisTests) })
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var attributes = method.GetCustomAttributesData();
            if (!attributes.Any(a => a.AttributeType.FullName is "Xunit.FactAttribute" or "Xunit.TheoryAttribute")) continue;
            var filter = Environment.GetEnvironmentVariable("OPTICAL_FOCUSED_TEST_FILTER");
            if (!string.IsNullOrWhiteSpace(filter) && !filter.Split('|').Any(value =>
                    $"{type.Name}.{method.Name}".Contains(value, StringComparison.Ordinal))) continue;
            var rows = attributes.Where(a => a.AttributeType.FullName == "Xunit.InlineDataAttribute")
                .Select(a => ((IReadOnlyCollection<CustomAttributeTypedArgument>)a.ConstructorArguments[0].Value!).Select(v => v.Value).ToArray()).ToList();
            if (rows.Count == 0) rows.Add([]);
            foreach (var row in rows)
            {
                var parameters = method.GetParameters();
                for (var i = 0; i < row.Length; i++)
                    if (parameters[i].ParameterType.IsEnum) row[i] = Enum.ToObject(parameters[i].ParameterType, row[i]!);
                var name = $"{type.Name}.{method.Name}({string.Join(',', row)})";
                Console.WriteLine("START " + name);
                var watch = Stopwatch.StartNew();
                try
                {
                    if (method.Invoke(Activator.CreateInstance(type), row) is Task task) await task.WaitAsync(TimeSpan.FromSeconds(90));
                    results.Add(new { name, passed = true, seconds = watch.Elapsed.TotalSeconds });
                    Console.WriteLine("PASS " + name);
                }
                catch (Exception exception)
                {
                    failed = true;
                    var error = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
                    results.Add(new { name, passed = false, seconds = watch.Elapsed.TotalSeconds, error = error.ToString() });
                    Console.WriteLine("FAIL " + name + " " + error);
                }
                File.WriteAllText(resultPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        Console.WriteLine($"Completed {results.Count} direct test cases; failed={failed}");
        Environment.ExitCode = failed ? 1 : 0;
    }
}
