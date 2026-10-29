using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison.Zemax;

if (args.Length == 3 && args[0] == "--normal-formulas")
    return NormalFormulaDiagnostics.Run(args[1], args[2]);
if (args.Length == 3 && args[0] == "--pupil-map")
    return PupilMapDiagnostics.Run(args[1], args[2]);
if (args.Length == 3 && args[0] == "--grid-diagnose")
    return PupilGridDiagnostics.Run(args[1], args[2]);
if (args.Length == 3 && args[0] == "--pupil-lines")
    return PupilLineDiagnostics.Run(args[1], args[2]);
if (args.Length == 4 && args[0] == "--gaussian-grid")
    return GaussianGridDiagnostics.Run(args[1], args[2], args[3]);
if (args.Length == 4 && args[0] == "--gaussian-pairs")
    return GaussianGridDiagnostics.Run(args[1], args[2], args[3], pairedUniform: true);
if (args.Length == 4 && args[0] == "--compare")
    return CoreComparison.Run(args[1], args[2], args[3]);
if (args.Length == 4 && args[0] == "--assess")
    return NativeAssessment.Run(args[1], args[2], args[3]);
if (args.Length != 3) throw new ArgumentException("Expected repository, prepared directory, installed API directory");
var repository = Path.GetFullPath(args[0]);
var directory = Path.GetFullPath(args[1]);
var api = Path.GetFullPath(args[2]);
var json = new JsonSerializerOptions { WriteIndented = true };
var executable = Path.Combine(directory, "ZemaxHost.exe");
var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET/Framework64/v4.0.30319/csc.exe");
var compilation = new List<string> { "/nologo", "/target:exe", "/platform:x64", "/out:" + executable, "/r:System.Web.Extensions.dll" };
compilation.AddRange(new[] { "ZOSAPI.dll", "ZOSAPI_Interfaces.dll", "ZOSAPI_NetHelper.dll" }.Select(name => "/r:" + Path.Combine(api, name)));
compilation.AddRange(Directory.GetFiles(Path.Combine(repository, "tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost"), "*.cs").Order(StringComparer.Ordinal));
var build = await ProcessIsolation.Run(compiler, compilation, directory, 60, CancellationToken.None);
File.WriteAllText(Path.Combine(directory, "build.json"), JsonSerializer.Serialize(build, json));
if (build.ExitCode != 0) throw new InvalidOperationException(build.StandardOutput + build.StandardError);
File.WriteAllText(executable + ".config", "<configuration><startup useLegacyV2RuntimeActivationPolicy=\"true\"><supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.8\"/></startup></configuration>");
var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "jobs.json")));
if (manifest.RootElement.GetArrayLength() is < 1 or > 16) throw new InvalidDataException("One to sixteen bounded jobs required");
var results = new List<object>();
foreach (var job in manifest.RootElement.EnumerateArray())
{
    var id = job.GetProperty("id").GetString()!;
    var requestPath = Path.Combine(directory, job.GetProperty("request").GetString()!);
    using var request = JsonDocument.Parse(File.ReadAllText(requestPath));
    if (!request.RootElement.TryGetProperty("captureScreenshots", out var screenshotFlag) || screenshotFlag.GetBoolean())
        throw new InvalidDataException("This background-only runner requires captureScreenshots=false");
    var input = request.RootElement.GetProperty("input").GetString()!;
    string HashInput() => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(input)));
    if (HashInput() != job.GetProperty("inputSha256").GetString()) throw new InvalidDataException("Source hash mismatch");
    var output = Path.Combine(directory, id);
    if (Directory.Exists(output)) throw new IOException("Capture output already exists: " + output);
    Console.WriteLine("Starting " + id);
    var result = await ProcessIsolation.Run(executable, [requestPath, output], directory, 120, CancellationToken.None,
        new Dictionary<string, string> { ["PATH"] = api + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH") });
    var unchanged = HashInput() == job.GetProperty("inputSha256").GetString();
    results.Add(new { id, result, sourceUnchanged = unchanged });
    File.WriteAllText(Path.Combine(directory, "execution.json"), JsonSerializer.Serialize(results, json));
    Console.WriteLine($"Completed {id}: {result.ExitCode}");
    if (result.ExitCode != 0 || !unchanged) return 2;
}
return 0;
