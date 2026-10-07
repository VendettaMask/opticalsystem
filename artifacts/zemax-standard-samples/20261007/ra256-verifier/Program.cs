using System.Security.Cryptography;
using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Diagnostics;
using OptilandWorkbench.ZemaxComparison.Workbench;

// Offline evidence audit only. All Workbench optics use the formal shared Core.
// Arithmetic over native accepted image points is an independent reference check;
// it never supplies runtime evaluation, acceptance or application display results.
if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--import-source") throw new ArgumentException("capture-directory [--import-source]");
var importSource = args.Length == 2;
var root = Path.GetFullPath(args[0]);
var output = Path.Combine(root, importSource ? "verification-import-source.json" : "verification.json");
if (File.Exists(output)) throw new IOException("Fresh verification output required");
string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
using var manifest = JsonDocument.Parse(File.OpenRead(Path.Combine(root, "manifest.json")));
using var environment = JsonDocument.Parse(File.OpenRead(Path.Combine(root, "native", "environment.json")));
var m = manifest.RootElement;
if (!m.GetProperty("originalFileUnchanged").GetBoolean() || m.GetProperty("nativeExitCode").GetInt32() != 0
    || m.GetProperty("timedOut").GetBoolean() || environment.RootElement.GetProperty("major").GetInt32() != 26
    || environment.RootElement.GetProperty("minor").GetInt32() != 1 || !environment.RootElement.GetProperty("validLicense").GetBoolean())
    throw new InvalidDataException("Invalid native capture");
var job = JsonFiles.Read<RayAuditJob>(Path.Combine(root, "job.json"));
if (Hash(job.Source) != job.SourceSha256 || Hash(Path.Combine(root, "source.zmx")) != job.SourceSha256
    || Hash(Path.Combine(root, "snapshot.json")) != m.GetProperty("snapshotSha256").GetString()
    || Hash(Path.Combine(root, "inputs.json")) != m.GetProperty("inputsSha256").GetString())
    throw new InvalidDataException("Source or input evidence changed");
var capturedSnapshot = JsonFiles.Read<OpticSnapshot>(Path.Combine(root, "snapshot.json"));
var optic = importSource ? (await WorkbenchExecutor.Import(Path.Combine(root, "source.zmx"))).Configurations[job.Configuration - 1]
    : Optic.FromSnapshot(capturedSnapshot);
var currentSnapshot = Path.Combine(root, "current-import-snapshot.json");
if (importSource) JsonFiles.Write(currentSnapshot, optic.ToSnapshot());
if (job.RemoveVignettingFactors) foreach (var f in optic.Fields) PupilVignetting.Clear(f);
var pupils = job.CreateSamples();
job.ValidateSurfaceBudget(optic.SurfaceGroup.Items.Count);
if (job.Fields.Length != 1 || pupils.Count != m.GetProperty("inputCount").GetInt32())
    throw new InvalidDataException("Expected one complete pupil at an explicit field");
var wave = optic.Wavelengths[job.Wavelength - 1].Micrometers;
var field = job.Fields[0];
using var native = JsonDocument.Parse(File.OpenRead(Path.Combine(root, "native", "ray-audit.json")));
var inputs = native.RootElement.GetProperty("inputs");
for (var i = 0; i < pupils.Count; i++)
    if (inputs[i].GetProperty("px").GetDouble() != pupils[i].X || inputs[i].GetProperty("py").GetDouble() != pupils[i].Y
        || inputs[i].GetProperty("weight").GetDouble() != pupils[i].Weight || inputs[i].GetProperty("hx").GetDouble() != field[0]
        || inputs[i].GetProperty("hy").GetDouble() != field[1]) throw new InvalidDataException("Current formal pupil input differs");
var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(field[0], field[1], wave,
    pupils, aimAtStop: optic.RayAimingEnabled);
var physical = optic.SequentialRayTracer.Trace(bundle).RayHistories;
var integral = optic.SequentialRayTracer.TraceGaussianPupil(bundle);
var surfaces = native.RootElement.GetProperty("surfaces");
var imageIndex = optic.SurfaceGroup.Items.Count - 1;
if (surfaces.GetArrayLength() != imageIndex) throw new InvalidDataException("Incomplete native surface range");
var image = surfaces[imageIndex - 1].GetProperty("rays");
var nativeAccepted = 0;
var coreAccepted = 0;
var nativeErrors = 0;
var acceptanceDifferences = 0;
var firstClipDifferences = 0;
var physicalMisses = 0;
var witnesses = new List<object>();
var clipWitnesses = new List<object>();
var nativePoints = new List<(double X, double Y)>();
for (var i = 0; i < pupils.Count; i++)
{
    var nr = image[i];
    var error = nr.GetProperty("error").GetInt32();
    var clip = nr.GetProperty("vignette").GetInt32();
    var accepted = error == 0 && clip == 0;
    var last = physical[i][^1];
    var wbAccepted = last.SurfaceNumber == imageIndex && !last.Vignetted && last.Intensity > 0;
    nativeErrors += error != 0 ? 1 : 0;
    nativeAccepted += accepted ? 1 : 0;
    coreAccepted += wbAccepted ? 1 : 0;
    physicalMisses += last.Vignetted && last.RefractiveIndexBefore is null ? 1 : 0;
    if (accepted) nativePoints.Add((nr.GetProperty("x").GetDouble(), nr.GetProperty("y").GetDouble()));
    if (accepted != wbAccepted)
    {
        acceptanceDifferences++;
        if (witnesses.Count < 20) witnesses.Add(new { index = i, pupils[i].X, pupils[i].Y, nativeError = error,
            nativeVignette = clip, coreSurface = last.SurfaceNumber, coreVignette = last.Vignetted,
            coreMiss = last.RefractiveIndexBefore is null });
    }
    if (error == 0 && clip > 0 && last.Vignetted && last.RefractiveIndexBefore.HasValue && clip != last.SurfaceNumber)
    {
        firstClipDifferences++;
        if (clipWitnesses.Count < 20) clipWitnesses.Add(new { index = i, pupils[i].X, pupils[i].Y,
            nativeFirstClip = clip, coreFirstClip = last.SurfaceNumber });
    }
}
var results = 0;
var virtualMissingOrNativeErrors = 0;
var physicalCompared = 0;
var maximumPhysicalCoordinate = 0d;
var maximumPhysicalDirection = 0d;
var maximumVirtualCoordinate = 0d;
var maximumVirtualDirection = 0d;
foreach (var surface in surfaces.EnumerateArray())
{
    var index = surface.GetProperty("surface").GetInt32();
    var rays = surface.GetProperty("rays");
    if (rays.GetArrayLength() != pupils.Count) throw new InvalidDataException("Incomplete native pupil at surface");
    var frame = optic.SurfaceGroup.Items[index].CoordinateSystem;
    for (var i = 0; i < pupils.Count; i++)
    {
        results++;
        var nr = rays[i];
        if (nr.GetProperty("number").GetInt32() != i + 1) throw new InvalidDataException("Native ray ordering differs");
        var vs = integral[i].FirstOrDefault(s => s.SurfaceNumber == index);
        if (nr.GetProperty("error").GetInt32() != 0 || vs is null || vs.Vignetted)
        {
            virtualMissingOrNativeErrors++;
            continue;
        }
        Difference(vs, ref maximumVirtualCoordinate, ref maximumVirtualDirection);
        var ps = physical[i].FirstOrDefault(s => s.SurfaceNumber == index);
        if (ps is { RefractiveIndexBefore: not null } && !ps.Vignetted && nr.GetProperty("vignette").GetInt32() == 0)
        {
            physicalCompared++;
            Difference(ps, ref maximumPhysicalCoordinate, ref maximumPhysicalDirection);
        }

        void Difference(RayTraceSample s, ref double coordinate, ref double direction)
        {
            var p = frame.ToLocalPoint(s.Position);
            var d = frame.ToLocalDirection(s.Direction);
            coordinate = Math.Max(coordinate, Math.Max(Math.Abs(p.X - nr.GetProperty("x").GetDouble()),
                Math.Max(Math.Abs(p.Y - nr.GetProperty("y").GetDouble()), Math.Abs(p.Z - nr.GetProperty("z").GetDouble()))));
            direction = Math.Max(direction, Math.Max(Math.Abs(d.X - nr.GetProperty("l").GetDouble()),
                Math.Max(Math.Abs(d.Y - nr.GetProperty("m").GetDouble()), Math.Abs(d.Z - nr.GetProperty("n").GetDouble()))));
        }
    }
}
var formalSpot = SpotMetricEvaluator.EvaluatePupilSamples(optic, field[0], field[1], pupils, job.Wavelength,
    includeSurfaceTransmission: false);
var meanX = nativePoints.Average(p => p.X);
var meanY = nativePoints.Average(p => p.Y);
var nativeRms = Math.Sqrt(nativePoints.Average(p => Math.Pow(p.X - meanX, 2) + Math.Pow(p.Y - meanY, 2)));
var files = new[] { "source.zmx", "snapshot.json", "job.json", "manifest.json", "inputs.json", "core.json",
    "native/environment.json", "native/model.json", "native/request.json", "native/ray-audit.json" }
    .Select(path => new { path, sha256 = Hash(Path.Combine(root, path)) }).ToArray();
var summary = new
{
    semantics = "Current formal Core per-ray diagnostics; acceptance, first clipping and coordinate residuals are separate. Native accepted-point RMS arithmetic is a reference check, not native analysis equivalence.",
    inputs = pupils.Count, surfaceRayResults = results, nativeAccepted, coreAccepted, nativeErrors,
    acceptanceDifferences, firstClipDifferences, physicalMisses, witnesses, clipWitnesses,
    physicalCompared, maximumPhysicalCoordinateMillimeters = maximumPhysicalCoordinate,
    maximumPhysicalDirectionComponent = maximumPhysicalDirection, virtualMissingOrNativeErrors,
    maximumVirtualCoordinateMillimeters = maximumVirtualCoordinate, maximumVirtualDirectionComponent = maximumVirtualDirection,
    nativeAcceptedPointRmsMicrometers = nativeRms * 1000,
    formalRmsMicrometers = formalSpot.Metrics!.RmsSpotRadius * 1000,
    capturedCoreAssemblySha256 = m.GetProperty("coreAssemblySha256").GetString(),
    currentCoreAssemblySha256 = Hash(typeof(Optic).Assembly.Location),
    currentToolAssemblySha256 = Hash(typeof(RayAuditRunner).Assembly.Location),
    sourceSha256 = job.SourceSha256, originalFileUnchanged = true, files
    , modelOrigin = importSource ? "Fresh import of unchanged original source" : "Unchanged captured snapshot",
    capturedSnapshotSha256 = Hash(Path.Combine(root, "snapshot.json")),
    currentImportSnapshotSha256 = importSource ? Hash(currentSnapshot) : null
};
JsonFiles.Write(output, summary);
Console.WriteLine(JsonSerializer.Serialize(summary, JsonFiles.Options));
