using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

// Validation host only. Product assemblies are always read from the actual default App output.
// The retained test driver supplies scenarios, never its copied optical or application libraries.
var app = Path.GetFullPath(args[0]); var driver = Path.GetFullPath(args[1]); var audit = Path.GetFullPath(args[2]);
string[] products = ["OpticalSystemDesign.Core", "OpticalSystemDesign.InitialStructure.Contracts", "OpticalSystemDesign.InitialStructure.Engine",
    "OpticalSystemDesign.InitialStructure.Persistence", "OpticalSystemDesign.Application", "OpticalSystemDesign.CoatingDesign.Engine",
    "OpticalSystemDesign.Modules", "OpticalSystemDesign.App"];
var context = AssemblyLoadContext.Default;
context.Resolving += (_, name) =>
{
    var path = Path.Combine(app, name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};
foreach (var product in products) context.LoadFromAssemblyPath(Path.Combine(app, product + ".dll"));
var scenario = context.LoadFromAssemblyPath(driver);
try
{
    var entry = scenario.EntryPoint ?? throw new InvalidOperationException("Scenario driver has no entry point.");
    var returned = entry.Invoke(null, [args.Skip(3).ToArray()]);
    if (returned is Task task) await task;
    else if (returned is int code && code != 0) throw new InvalidOperationException("Scenario exit code: " + code);
}
finally
{
    var loaded = products.Select(name =>
    {
        var assembly = context.Assemblies.Single(a => a.GetName().Name == name);
        var path = Path.GetFullPath(assembly.Location); var expected = Path.Combine(app, name + ".dll");
        if (!StringComparer.OrdinalIgnoreCase.Equals(path, expected)) throw new InvalidOperationException("Wrong product runtime: " + path);
        return new { Name = name + ".dll", Path = path, Mvid = assembly.ManifestModule.ModuleVersionId,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) };
    }).ToArray();
    Directory.CreateDirectory(Path.GetDirectoryName(audit)!);
    await using var file = new FileStream(audit, FileMode.CreateNew, FileAccess.Write);
    await JsonSerializer.SerializeAsync(file, new { DefaultAppOutput = app, ScenarioDriver = driver, Arguments = args.Skip(3).ToArray(),
        ScenarioDriverSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(driver))), ProductAssemblies = loaded,
        ForegroundWindowStarted = false }, new JsonSerializerOptions { WriteIndented = true });
}
