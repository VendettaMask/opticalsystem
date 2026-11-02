using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    public Task<CameraSceneLinearBatchArtifact> RunSceneLinearBatchAsync(CameraSceneLinearBatchPlan plan, string path,
        IProgress<CameraSceneLinearBatchProgress>? progress = null, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        using var cancellation = ComputationCancellation.Push(cancellationToken);
        var validated = ValidateLinearPlan(plan); plan = validated.Plan;
        if (!plan.WithinBudget) throw new InvalidOperationException(plan.BudgetFailure);
        var request = plan.Request; var optic = validated.Optic; var firstZ = optic.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z;
        var settings = LinearBatchSettings(request);
        var scenes = plan.Tasks.Select(t => EmissiveCheckerboard.Create(request.Chart!, t.Nodes.Select(n =>
            new EmissiveCheckerboardBand(n.WavelengthNanometers, n.BrightRadiance, n.DarkRadiance)).ToArray(), firstZ)).ToArray();
        using var scratch = new LinearBatchScratch();
        var outputs = new List<SceneLinearTaskOutput>(); var files = new List<SceneLinearFile>();
        var area = Math.Pow(request.PixelPitchMicrometers * 1e-6, 2) * request.FillFactor;
        double[]? sum = null; var nextSource = 0; var nextRow = 0;
        var statistics = DirectCameraRenderer.RenderBatch(optic, scenes, settings, request.RowsPerStrip, strip =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (strip.SourceIndex != nextSource || strip.FirstRow != nextRow) throw new InvalidOperationException("Sensor strip sequence is incomplete or duplicated.");
            sum ??= new double[checked(request.Width * request.Height)];
            for (var band = 0; band < strip.Bands.Count; band++)
            {
                var name = NodeFile(strip.SourceIndex, band);
                scratch.AppendFloat64(name, strip.Bands[band].PixelBandPowerWatts, checked((long)strip.FirstRow * request.Width * 8));
                for (var i = 0; i < strip.Bands[band].PixelBandPowerWatts.Count; i++)
                    sum[strip.FirstRow * request.Width + i] += strip.Bands[band].PixelBandPowerWatts[i];
            }
            nextRow += strip.RowCount;
            if (nextRow != request.Height) return;
            var count = plan.Tasks[strip.SourceIndex].Nodes.Count;
            for (var band = 0; band < count; band++) files.Add(scratch.Describe(NodeFile(strip.SourceIndex, band), "node_power_W_per_pixel", strip.SourceIndex, band));
            var powerPath = count == 1 ? NodeFile(strip.SourceIndex, 0) : $"task-{strip.SourceIndex + 1:D3}-power.f64le";
            if (count > 1) { scratch.AppendFloat64(powerPath, sum, 0); files.Add(scratch.Describe(powerPath, "task_power_W_per_pixel", strip.SourceIndex)); }
            var irradiance = sum.Select(v => v / area).ToArray();
            if (irradiance.Any(v => !double.IsFinite(v) || v < 0)) throw new InvalidOperationException("Invalid integrated scene irradiance.");
            var irradiancePath = $"task-{strip.SourceIndex + 1:D3}-irradiance.f64le";
            scratch.AppendFloat64(irradiancePath, irradiance, 0);
            files.Add(scratch.Describe(irradiancePath, "task_irradiance_W_per_m2", strip.SourceIndex));
            outputs.Add(new(powerPath, irradiancePath, $"task-{strip.SourceIndex + 1:D3}-linear16.png", sum.Sum(), irradiance.Max(), new(0, 0, 0, 0)));
            sum = null; nextSource++; nextRow = 0;
        }, new InlineProgress<DirectCameraBatchProgress>(p => progress?.Report(new("Tracing", p.SourceIndex, p.CompletedRows, p.FullHeight, p.CompletedRays, p.TotalRays))));
        if (nextSource != plan.Tasks.Count || sum is not null) throw new InvalidOperationException("Full-sensor tasks did not finish.");
        var maximum = request.PngMaximumIrradiance ?? outputs.Max(o => o.MaximumIrradiance);
        if (maximum == 0) maximum = 1;
        if (outputs.Any(o => o.MaximumIrradiance > maximum)) throw new InvalidOperationException("指定的线性交换图标尺会裁切真实辐照度；目标文件未替换。");
        for (var task = 0; task < outputs.Count; task++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new("ExchangeImages", task, request.Height, request.Height, plan.Work.Rays, plan.Work.Rays));
            var irradiance = ReadFloat64(File.ReadAllBytes(scratch.PathFor(outputs[task].IrradiancePath)), request.Width * request.Height);
            scratch.WriteBytes(outputs[task].PngPath, LinearGray16Png.Encode(irradiance, request.Width, request.Height, maximum, cancellationToken));
            files.Add(scratch.Describe(outputs[task].PngPath, "linear_gray16_png", task));
            outputs[task] = outputs[task] with { Statistics = statistics.Sources[task] };
        }
        var manifest = new CameraSceneLinearBatchManifest(plan, DetectorRowOrder.PositiveLocalYFirst, area, firstZ,
            optic.SurfaceGroup.Items[^1].CoordinateSystem.Origin.Z, Array.AsReadOnly(outputs.ToArray()), Array.AsReadOnly(files.ToArray()),
            new(16, "Grayscale", "Linear", maximum, 0, maximum / 65535 / 2, "None: no gAMA, sRGB, iCCP, cICP or alpha", 0),
            statistics.Sources.Sum(s => s.LaunchedRays), statistics.Sources.Sum(s => s.SurfaceInteractions));
        var json = JsonSerializer.SerializeToUtf8Bytes(manifest, SceneSfrJson);
        if (json.Length > BoundedFile.MaximumSettingsBytes || files.Sum(f => f.Bytes) + json.Length > BoundedFile.MaximumExportBytes)
            throw new InvalidDataException("Full-sensor exchange archive exceeds the existing output budget.");
        progress?.Report(new("Packaging", outputs.Count - 1, request.Height, request.Height, plan.Work.Rays, plan.Work.Rays));
        await BoundedFile.WriteAtomicAsync(path, BoundedFile.MaximumExportBytes, "full-sensor linear experiment", async (destination, token) =>
        {
            using var zip = new ZipArchive(destination, ZipArchiveMode.Create, true);
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                await using var source = File.OpenRead(scratch.PathFor(file.Path));
                await using var entry = zip.CreateEntry(file.Path, CompressionLevel.Optimal).Open();
                await source.CopyToAsync(entry, token).ConfigureAwait(false);
            }
            Write("manifest.json", json);
            Write("manifest.sha256", Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(json))));
            Write("README.txt", Encoding.UTF8.GetBytes("Full sensor, row-major (+Y first, X increasing), float64 little endian, W/pixel and collecting-area average W/m2. Pixel centers: x=(column+0.5-width/2)*pitch_mm; y=(height/2-row-0.5)*pitch_mm. Power already includes pixel area and lens transmission. Independent source tasks share one frozen lens, chart, sensor and global budget. Source quadrature weights are applied once, before transport. PNGs use one common linear zero-offset scale; DN/65535*common_max restores irradiance up to declared quantization. No gamma, ICC, QE, exposure, noise, ADC or ISP. No original Ansys ROM/full-resolution parity, formal MTF agreement or sampling convergence claim.\n"));
            void Write(string name, byte[] bytes) { using var stream = zip.CreateEntry(name).Open(); stream.Write(bytes); }
        }, cancellationToken).ConfigureAwait(false);
        progress?.Report(new("Completed", outputs.Count - 1, request.Height, request.Height, plan.Work.Rays, plan.Work.Rays));
        return new CameraSceneLinearBatchArtifact(System.IO.Path.GetFullPath(path), manifest);
    }, cancellationToken);

    public Task<CameraSceneLinearBatchArtifact> OpenSceneLinearBatchAsync(string path, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var scope = ComputationCancellation.Push(cancellationToken);
        using var file = BoundedFile.OpenRead(path, BoundedFile.MaximumExportBytes, "full-sensor linear experiment");
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        if (zip.Entries.Count is < 4 or > 512 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count)
            throw new InvalidDataException("Invalid or duplicate batch archive entries.");
        long total = 0;
        foreach (var entry in zip.Entries) { total = checked(total + entry.Length); if (total > BoundedFile.MaximumExportBytes) throw new InvalidDataException("Expanded batch archive exceeds its budget."); }
        var bytes = ReadLinearEntry(zip, "manifest.json", BoundedFile.MaximumSettingsBytes, cancellationToken);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != Encoding.ASCII.GetString(ReadLinearEntry(zip, "manifest.sha256", 64, cancellationToken)))
            throw new InvalidDataException("Full-sensor manifest checksum differs.");
        using var parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 40 }); CheckDuplicateProperties(parsed.RootElement);
        var manifest = JsonSerializer.Deserialize<CameraSceneLinearBatchManifest>(bytes, SceneSfrJson) ?? throw new InvalidDataException("Missing batch manifest.");
        ValidateLinearManifest(zip, manifest, cancellationToken);
        return new CameraSceneLinearBatchArtifact(System.IO.Path.GetFullPath(path), manifest);
    }, cancellationToken);

    public async Task<CameraSceneLinearTaskImage> ReadSceneLinearTaskAsync(CameraSceneLinearBatchArtifact artifact, int taskIndex, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        // Re-read the file identity rather than trusting mutable caller-supplied manifest metadata.
        var verified = await OpenSceneLinearBatchAsync(artifact.ArchivePath, cancellationToken).ConfigureAwait(false);
        if (JsonSerializer.Serialize(verified.Manifest, SceneSfrJson) != JsonSerializer.Serialize(artifact.Manifest, SceneSfrJson))
            throw new InvalidDataException("批次文件已改变，请重新打开，不能混用原清单与新像元。");
        return await Task.Run(() =>
        {
            if ((uint)taskIndex >= verified.Manifest.Outputs.Count) throw new ArgumentOutOfRangeException(nameof(taskIndex));
            var m = verified.Manifest; var r = m.Plan.Request; var output = m.Outputs[taskIndex];
            using var zip = ZipFile.OpenRead(verified.ArchivePath);
            double[] ReadPlane(string name)
            {
                var bytes = ReadLinearEntry(zip, name, r.Width * r.Height * 8L, cancellationToken);
                if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != m.Files.Single(f => f.Path == name).Sha256)
                    throw new InvalidDataException("读取期间像元文件已改变，请重新打开实验。");
                return ReadFloat64(bytes, r.Width * r.Height);
            }
            var power = ReadPlane(output.PowerPath); var irradiance = ReadPlane(output.IrradiancePath);
            var rgb = irradiance.SelectMany(v => Enumerable.Repeat((byte)Math.Round(v / m.Png.CommonMaximumWattsPerSquareMeter * 255), 3)).ToArray();
            return new CameraSceneLinearTaskImage(taskIndex, m.Plan.Tasks[taskIndex].Source.Name, r.Width, r.Height, r.PixelPitchMicrometers,
                Array.AsReadOnly(power), Array.AsReadOnly(irradiance), new(r.Width, r.Height, Array.AsReadOnly(rgb)));
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateLinearManifest(ZipArchive zip, CameraSceneLinearBatchManifest manifest, CancellationToken token)
    {
        var validation = ValidateLinearPlan(manifest.Plan); var plan = validation.Plan; var r = plan.Request; var pixels = checked(r.Width * r.Height);
        var area = Math.Pow(r.PixelPitchMicrometers * 1e-6, 2) * r.FillFactor;
        if (!plan.WithinBudget || manifest.Outputs is null || manifest.Outputs.Count != plan.Tasks.Count || manifest.Files is null
            || manifest.RowOrder != DetectorRowOrder.PositiveLocalYFirst || manifest.CollectionAreaSquareMeters != area
            || manifest.FirstLensVertexZMillimeters != validation.Optic.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z
            || manifest.SensorPlaneZMillimeters != validation.Optic.SurfaceGroup.Items[^1].CoordinateSystem.Origin.Z
            || manifest.CompletedRays != plan.Work.Rays || manifest.SurfaceInteractions is < 0 || manifest.SurfaceInteractions > plan.Work.MaximumInteractions
            || manifest.Files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() != manifest.Files.Count
            || zip.Entries.Count != manifest.Files.Count + 3)
            throw new InvalidDataException("Full-sensor manifest disagrees with frozen optics, raster, tasks or work accounting.");
        var png = manifest.Png;
        if (png is null || png.BitDepth != 16 || png.ColorType != "Grayscale" || png.TransferFunction != "Linear"
            || png.OffsetWattsPerSquareMeter != 0 || png.ClippedPixels != 0 || !double.IsFinite(png.CommonMaximumWattsPerSquareMeter)
            || png.CommonMaximumWattsPerSquareMeter <= 0 || png.MaximumQuantizationError != png.CommonMaximumWattsPerSquareMeter / 65535 / 2
            || png.ColorMetadata != "None: no gAMA, sRGB, iCCP, cICP or alpha") throw new InvalidDataException("Invalid linear exchange encoding.");
        var expected = new Dictionary<string, (string Kind, int Task, int? Node)>(StringComparer.Ordinal);
        for (var task = 0; task < plan.Tasks.Count; task++)
        {
            token.ThrowIfCancellationRequested(); var count = plan.Tasks[task].Nodes.Count; var output = manifest.Outputs[task];
            for (var node = 0; node < count; node++) expected.Add(NodeFile(task, node), ("node_power_W_per_pixel", task, node));
            var powerPath = count == 1 ? NodeFile(task, 0) : $"task-{task + 1:D3}-power.f64le";
            if (count > 1) expected.Add(powerPath, ("task_power_W_per_pixel", task, null));
            expected.Add($"task-{task + 1:D3}-irradiance.f64le", ("task_irradiance_W_per_m2", task, null));
            expected.Add($"task-{task + 1:D3}-linear16.png", ("linear_gray16_png", task, null));
            if (output.PowerPath != powerPath || output.IrradiancePath != $"task-{task + 1:D3}-irradiance.f64le" || output.PngPath != $"task-{task + 1:D3}-linear16.png")
                throw new InvalidDataException("Task file mapping differs from the full-sensor layout.");
        }
        if (expected.Count != manifest.Files.Count) throw new InvalidDataException("Task files are incomplete.");
        if (zip.Entries.Any(e => e.FullName is not ("manifest.json" or "manifest.sha256" or "README.txt") && !expected.ContainsKey(e.FullName))
            || zip.GetEntry("README.txt") is null) throw new InvalidDataException("Unexpected batch archive entry.");
        foreach (var descriptor in manifest.Files)
        {
            token.ThrowIfCancellationRequested();
            if (!expected.TryGetValue(descriptor.Path, out var spec) || descriptor.Kind != spec.Kind || descriptor.TaskIndex != spec.Task || descriptor.NodeIndex != spec.Node
                || descriptor.Sha256 is null || descriptor.Sha256.Length != 64 || descriptor.Bytes <= 0
                || spec.Kind != "linear_gray16_png" && descriptor.Bytes != pixels * 8L)
                throw new InvalidDataException("Invalid task file descriptor.");
            var content = ReadLinearEntry(zip, descriptor.Path, descriptor.Bytes, token);
            if (content.Length != descriptor.Bytes || Convert.ToHexStringLower(SHA256.HashData(content)) != descriptor.Sha256) throw new InvalidDataException("Linear task file checksum differs.");
        }
        long interactions = 0;
        for (var task = 0; task < plan.Tasks.Count; task++)
        {
            var output = manifest.Outputs[task]; var s = output.Statistics;
            var expectedRays = checked((long)pixels * r.PixelSamplesPerAxis * r.PixelSamplesPerAxis * r.PupilRings * r.PupilArms * plan.Tasks[task].Nodes.Count);
            if (s.LaunchedRays != expectedRays || s.PassedLensRays <= 0 || s.PassedLensRays > s.LaunchedRays || s.SceneHitRays < 0 || s.SceneHitRays > s.PassedLensRays
                || s.SurfaceInteractions < 0 || s.SurfaceInteractions > expectedRays * (validation.Optic.SurfaceGroup.Items.Count - 2)) throw new InvalidDataException("Invalid per-task work accounting.");
            interactions += s.SurfaceInteractions;
            var sum = new double[pixels];
            for (var node = 0; node < plan.Tasks[task].Nodes.Count; node++)
            {
                var nodePower = ReadFloat64(ReadLinearEntry(zip, NodeFile(task, node), pixels * 8L, token), pixels);
                for (var i = 0; i < pixels; i++) sum[i] += nodePower[i];
            }
            var power = ReadFloat64(ReadLinearEntry(zip, output.PowerPath, pixels * 8L, token), pixels);
            var irradiance = ReadFloat64(ReadLinearEntry(zip, output.IrradiancePath, pixels * 8L, token), pixels);
            for (var i = 0; i < pixels; i++) if (power[i] != sum[i] || irradiance[i] != power[i] / area) throw new InvalidDataException("Node sum or collecting-area conversion differs from authoritative power.");
            if (output.TotalPowerWatts != power.Sum() || output.MaximumIrradiance != irradiance.Max() || output.MaximumIrradiance > png.CommonMaximumWattsPerSquareMeter)
                throw new InvalidDataException("Task radiometry differs from its linear pixels.");
            LinearGray16Png.Validate(ReadLinearEntry(zip, output.PngPath, BoundedFile.MaximumImageDataBytes, token), irradiance, r.Width, r.Height, png.CommonMaximumWattsPerSquareMeter, token);
        }
        if (interactions != manifest.SurfaceInteractions) throw new InvalidDataException("Global surface accounting differs from task totals.");
    }

    private static string NodeFile(int task, int node) => $"task-{task + 1:D3}-node-{node + 1:D3}-power.f64le";
    private static byte[] ReadLinearEntry(ZipArchive zip, string name, long maximum, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException("Missing linear task entry: " + name);
        if (entry.Length < 0 || entry.Length > maximum || entry.Length > BoundedFile.MaximumImageDataBytes) throw new InvalidDataException("Linear entry exceeds its byte budget.");
        using var stream = entry.Open(); var bytes = new byte[checked((int)entry.Length)]; stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Linear entry exceeds declared length.");
        token.ThrowIfCancellationRequested(); return bytes;
    }
    private static double[] ReadFloat64(byte[] bytes, int count)
    {
        if (bytes.Length != checked(count * 8)) throw new InvalidDataException("Wrong float64 plane length.");
        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(i * 8)));
            if (!double.IsFinite(values[i]) || values[i] < 0) throw new InvalidDataException("Invalid physical float64 value.");
        }
        return values;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }

    private sealed class LinearBatchScratch : IDisposable
    {
        private readonly string _root;
        private readonly HashSet<string> _owned = new(StringComparer.Ordinal);
        internal LinearBatchScratch()
        {
            _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opticalsystemdesign-scene-linear-" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(_root)) throw new IOException("Scratch collision."); Directory.CreateDirectory(_root);
        }
        internal string PathFor(string name)
        {
            if (name != System.IO.Path.GetFileName(name) || name.Contains(':')) throw new InvalidDataException("Invalid task file name.");
            return System.IO.Path.Combine(_root, name);
        }
        internal void AppendFloat64(string name, IReadOnlyList<double> values, long expectedOffset)
        {
            var path = PathFor(name); _owned.Add(path);
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            if (stream.Length != expectedOffset) throw new InvalidOperationException("A linear strip is missing, repeated or out of order.");
            stream.Position = stream.Length; var bytes = new byte[checked(values.Count * 8)];
            for (var i = 0; i < values.Count; i++)
            {
                if (!double.IsFinite(values[i]) || values[i] < 0) throw new InvalidDataException("Nonfinite or negative physical power.");
                BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(i * 8), BitConverter.DoubleToInt64Bits(values[i]));
            }
            stream.Write(bytes);
        }
        internal void WriteBytes(string name, byte[] bytes) { var path = PathFor(name); _owned.Add(path); using var stream = new FileStream(path, FileMode.CreateNew); stream.Write(bytes); }
        internal SceneLinearFile Describe(string name, string kind, int task, int? node = null)
        {
            using var stream = File.OpenRead(PathFor(name)); return new(name, kind, stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream)), task, node);
        }
        public void Dispose()
        {
            // Only explicitly owned files; no recursive deletion and no caller-supplied paths.
            foreach (var file in _owned) try { File.Delete(file); } catch (IOException) { }
            try { Directory.Delete(_root, false); } catch (IOException) { }
        }
    }
}
