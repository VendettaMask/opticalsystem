using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison.Zemax;

if (args.Length == 3 && args[0] == "--afocal-ri")
    return RelativeIlluminationDiagnostics.Run(args[1], args[2]);
if (args.Length == 3 && args[0] == "--centroid-energy")
    return CentroidEnergyDiagnostics.Run(args[1], args[2]);
if (args.Length == 3 && args[0] == "--energy-reference-controls")
    return EnergyReferenceControlDiagnostics.Run(args[1], args[2]);
if (args.Length == 4 && args[0] == "--launch-study")
    return LaunchRuleDiagnostics.Run(args[1], args[2], args[3]);
if (args.Length == 4 && args[0] == "--parabasal-rule-study")
    return ParabasalRuleStudy.Run(args[1], args[2], args[3]);
if (args.Length == 3 && args[0] == "--automatic-grids")
    return AutomaticGridDiagnostics.Run(args[1], args[2]);
if (args.Length == 3 && args[0] == "--working-pupils")
    return WorkingPupilDiagnostics.Run(args[1], args[2]);
if (args.Length == 4 && args[0] == "--native-pupil-check")
    return NativePupilCheck.Run(args[1], args[2], args[3]);
if (args.Length == 4 && args[0] == "--tagged-facets")
    return TaggedFacetDiagnostics.Run(args[1], args[2], args[3]);
if (args.Length == 3 && args[0] == "--score-tags")
    return TaggedFacetRuleScoring.Run(args[1], args[2]);
if (args.Length == 3 && args[0] == "--pupil-projection")
    return PupilProjectionDiagnostics.Run(args[1], args[2]);
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
    var budgetSeconds = job.TryGetProperty("budgetSeconds", out var budget) ? budget.GetInt32() : 120;
    if (budgetSeconds is < 120 or > 600) throw new InvalidDataException("Native capture budget must be 120 to 600 seconds");
    var host = executable;
    if (job.TryGetProperty("frozenHost", out var frozenHost))
    {
        host = Path.GetFullPath(Path.Combine(repository, frozenHost.GetString()!));
        if (!host.StartsWith(repository.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(host) != "ZemaxHost.exe"
            || Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(host))) != job.GetProperty("frozenHostSha256").GetString())
            throw new InvalidDataException("Frozen host must be hash-verified inside this repository");
    }
    var hostSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(host)));
    var queuedUtc = DateTime.UtcNow;
    Console.WriteLine("Waiting for background native-capture lease: " + id);
    using var captureLease = await AcquireCaptureLease(repository);
    var queueSeconds = (DateTime.UtcNow - queuedUtc).TotalSeconds;
    Console.WriteLine("Starting " + id);
    var startedUtc = DateTime.UtcNow;
    var result = await ProcessIsolation.Run(host, [requestPath, output], directory, budgetSeconds, CancellationToken.None,
        new Dictionary<string, string> { ["PATH"] = api + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH") });
    var unchanged = HashInput() == job.GetProperty("inputSha256").GetString();
    results.Add(new { id, result, sourceUnchanged = unchanged, budgetSeconds, host, hostSha256, queuedUtc, queueSeconds, startedUtc, elapsedSeconds = (DateTime.UtcNow - startedUtc).TotalSeconds });
    File.WriteAllText(Path.Combine(directory, "execution.json"), JsonSerializer.Serialize(results, json));
    Console.WriteLine($"Completed {id}: {result.ExitCode}");
    if (result.ExitCode != 0 || !unchanged) return 2;
}
return 0;

static async Task<FileStream> AcquireCaptureLease(string repository)
{
    // Only this diagnostic's background captures participate; existing user applications are untouched.
    // An OS file handle releases the lease even when a caller is killed, and is safe across async continuations.
    var folder = Path.Combine(repository, "artifacts/validation"); Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, ".background-native-capture.lock");
    var deadline = DateTime.UtcNow.AddSeconds(120);
    while (true)
    {
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(250); }
    }
}
