using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.ZemaxComparison.Workbench;
using OptilandWorkbench.ZemaxComparison.Zemax;

namespace OptilandWorkbench.ZemaxComparison.Diagnostics;

/// <summary>Independent diagnostic capture; never a numerical comparison or physical acceptance result.</summary>
public sealed record RayAuditJob
{
    public required string Source { get; init; }
    public required string SourceSha256 { get; init; }
    public required string Output { get; init; }
    public string ZemaxVersion { get; init; } = "2026 R1";
    public string? ZosApiPath { get; init; }
    public int Configuration { get; init; } = 1;
    public int Wavelength { get; init; } = 1;
    public string Method { get; init; } = "GQ";
    public int RayDensity { get; init; } = 6;
    public bool RemoveVignettingFactors { get; init; } = true;
    public double[][] Fields { get; init; } = [[0, 0], [0, 1]];
    public int TimeoutSeconds { get; init; } = 180;

    public IReadOnlyList<PupilSample> CreateSamples()
    {
        if (Configuration < 1 || Wavelength < 1 || TimeoutSeconds is < 1 or > 3600
            || string.IsNullOrWhiteSpace(Source) || string.IsNullOrWhiteSpace(Output)
            || string.IsNullOrWhiteSpace(SourceSha256) || SourceSha256.Length != 64 || !SourceSha256.All(Uri.IsHexDigit)
            || Fields is null || Fields.Length is < 1 or > 16
            || Fields.Any(f => f is null || f.Length != 2 || f.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1)))
            throw new ArgumentException("Invalid ray audit source, fields or capture limits.");
        return Method switch
        {
            "GQ" when RayDensity is >= 1 and <= 20 => ApertureSampler.GenerateGaussianQuadrature(RayDensity, 2 * RayDensity),
            "RA" when RayDensity is 32 or 64 or 128 or 256 => ApertureSampler.GenerateRectangularArray(RayDensity),
            _ => throw new ArgumentException("Ray audit requires GQ order 1..20 or RA 32/64/128/256.")
        };
    }

    public void ValidateSurfaceBudget(int surfaceCount)
    {
        var pupils = CreateSamples();
        if (surfaceCount < 2 || (long)Fields.Length * pupils.Count * (surfaceCount - 1)
            > SequentialTraceLimits.MaximumRetainedSamples)
            throw new ArgumentException("Ray audit surface records exceed the shared retained-sample budget.");
    }
}

public static class RayAuditRunner
{
    public static async Task<int> Run(string path, CancellationToken cancellationToken)
    {
        var job = JsonSerializer.Deserialize<RayAuditJob>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonFiles.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new ArgumentException("Missing ray audit job.");
        var pupils = job.CreateSamples();
        var source = Path.GetFullPath(job.Source);
        var output = Path.GetFullPath(job.Output);
        if (!Path.GetExtension(source).Equals(".zmx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Ray audit source must be ZMX.");
        var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
        if (!JsonFiles.Hash(bytes).Equals(job.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Ray audit source SHA-256 differs from the job.");
        if (source.StartsWith(output.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Ray audit requires a fresh output directory outside the source.");
        Directory.CreateDirectory(output);
        using var outputLock = new FileStream(Path.Combine(output, ".run.lock"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None);
        var workingSource = Path.Combine(output, "source.zmx");
        await File.WriteAllBytesAsync(workingSource, bytes, cancellationToken);
        JsonFiles.Write(Path.Combine(output, "job.json"), job);
        var imported = await WorkbenchExecutor.Import(workingSource);
        if (job.Configuration > imported.Configurations.Count) throw new ArgumentException("Configuration exceeds the imported model.");
        var optic = imported.Configurations[job.Configuration - 1];
        job.ValidateSurfaceBudget(optic.SurfaceGroup.Items.Count);
        if (job.Wavelength > optic.Wavelengths.Count) throw new ArgumentException("Wavelength exceeds the imported model.");
        JsonFiles.Write(Path.Combine(output, "snapshot.json"), optic.ToSnapshot());
        var diagnostic = Optic.FromSnapshot(optic.ToSnapshot());
        if (job.RemoveVignettingFactors)
            foreach (var field in diagnostic.Fields) PupilVignetting.Clear(field);
        var unclipped = Optic.FromSnapshot(diagnostic.ToSnapshot());
        foreach (var surface in unclipped.SurfaceGroup.Items) surface.PhysicalAperture = null;
        var wave = optic.Wavelengths[job.Wavelength - 1];
        var inputs = job.Fields.SelectMany((f, fi) => pupils.Select((p, pi) => new
            { fieldIndex = fi, pupilIndex = pi, hx = f[0], hy = f[1], px = p.X, py = p.Y, weight = p.Weight })).ToArray();
        JsonFiles.Write(Path.Combine(output, "inputs.json"), inputs);
        var coreFields = job.Fields.Select(f => new
        {
            hx = f[0], hy = f[1],
            vignetting = diagnostic.SequentialRayTracer.RayGenerator.GetPupilVignetting(f[0], f[1]),
            physical = Trace(diagnostic, f),
            unclippedDiagnostic = Trace(unclipped, f),
            gaussianPupilIntegral = Trace(diagnostic, f, gaussian: true),
            physicalSpot = SpotMetricEvaluator.EvaluatePupilSamples(diagnostic, f[0], f[1], pupils,
                job.Wavelength, includeSurfaceTransmission: false, cancellationToken: cancellationToken),
            apertureDiagnostic = SampledApertureDiagnostics.Evaluate(diagnostic, f[0], f[1], pupils,
                job.Wavelength, cancellationToken)
        }).ToArray();
        JsonFiles.Write(Path.Combine(output, "core.json"), coreFields);
        var api = ZemaxExecutor.Locate(job.ZosApiPath);
        var native = await ZemaxExecutor.Build(api, output, cancellationToken);
        var result = await native.CaptureRayAudit(workingSource, Path.Combine(output, "native"), job,
            inputs, cancellationToken);
        var unchanged = JsonFiles.Hash(await File.ReadAllBytesAsync(source, cancellationToken)).Equals(job.SourceSha256, StringComparison.OrdinalIgnoreCase);
        JsonFiles.Write(Path.Combine(output, "manifest.json"), new
        {
            semantics = "Explicit pupil ray audit; unclipped continuation is diagnostic only. No comparison conclusion or RMS-analysis sampling equivalence is implied.",
            job, sourceSha256 = JsonFiles.Hash(bytes), originalFileUnchanged = unchanged,
            wavelengthMicrometers = wave.Micrometers, optic.RayAimingEnabled,
            surfaceCount = optic.SurfaceGroup.Items.Count, pupilCount = pupils.Count, inputCount = inputs.Length,
            snapshotSha256 = JsonFiles.Hash(File.ReadAllBytes(Path.Combine(output, "snapshot.json"))),
            inputsSha256 = JsonFiles.Hash(File.ReadAllBytes(Path.Combine(output, "inputs.json"))),
            coreAssemblySha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location)),
            toolAssemblySha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(RayAuditRunner).Assembly.Location)),
            nativeExitCode = result.ExitCode, result.TimedOut, completedUtc = DateTimeOffset.UtcNow
        });
        Console.WriteLine($"Ray audit: {output}; inputs={inputs.Length}, native exit={result.ExitCode}, unchanged={unchanged}");
        return result.TimedOut ? 4 : !unchanged ? 2 : result.ExitCode == 0 ? 0 : 3;

        object Trace(Optic model, double[] field, bool gaussian = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bundle = model.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(field[0], field[1],
                wave.Micrometers, pupils, aimAtStop: model.RayAimingEnabled);
            var histories = gaussian ? model.SequentialRayTracer.TraceGaussianPupil(bundle, cancellationToken)
                : model.SequentialRayTracer.Trace(bundle).RayHistories;
            return histories.Select((history, index) => new
            {
                pupilIndex = index, launch = bundle.Rays[index],
                samples = history.Select(s => new
                {
                    s.SurfaceNumber, s.Vignetted, s.Intensity, s.InteractionKind, s.SegmentLength,
                    localPosition = model.SurfaceGroup.Items[s.SurfaceNumber].CoordinateSystem.ToLocalPoint(s.Position),
                    localDirection = model.SurfaceGroup.Items[s.SurfaceNumber].CoordinateSystem.ToLocalDirection(s.Direction),
                    localIncidentDirection = s.IncidentDirection is { } incident
                        ? model.SurfaceGroup.Items[s.SurfaceNumber].CoordinateSystem.ToLocalDirection(incident) : (Vector3D?)null
                }).ToArray()
            }).ToArray();
        }
    }
}
