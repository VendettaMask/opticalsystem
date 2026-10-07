using System.Buffers.Binary;
using System.Numerics;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.NonSequential;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class CommercialReliabilityTests
{
    [Theory]
    [InlineData(3, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(9, false)]
    [InlineData(5, true)]
    [InlineData(8, false)]
    [InlineData(8, true)]
    public void PsfMtfPreservesRequestedGridAndMatchesIndependentAxisSums(int size, bool padded)
    {
        var values = new double[size, size];
        for (var row = 0; row < size; row++)
            for (var column = 0; column < size; column++)
                values[row, column] = 1 + row * row + 3 * column + row * column;
        var psf = new PsfResult(values, 4, size, 5, 2);
        var result = DiffractionEngine.ComputePsfMtf(psf, padded);
        var transformSize = padded ? 2 * size : size;
        var dc = values.Cast<double>().Sum();
        Assert.Equal(transformSize / 2, result.Frequency.Count);
        for (var index = 0; index < result.Frequency.Count; index++)
        {
            var tangential = Complex.Zero;
            var sagittal = Complex.Zero;
            for (var row = 0; row < size; row++)
                for (var column = 0; column < size; column++)
                {
                    tangential += values[row, column] * Complex.FromPolarCoordinates(1, -2 * Math.PI * index * row / transformSize);
                    sagittal += values[row, column] * Complex.FromPolarCoordinates(1, -2 * Math.PI * index * column / transformSize);
                }
            Assert.Equal(index * 1000.0 / (transformSize * 2), result.Frequency[index], 10);
            Assert.Equal(tangential.Magnitude / dc, result.Tangential[index], 10);
            Assert.Equal(sagittal.Magnitude / dc, result.Sagittal[index], 10);
        }
    }

    [Theory]
    [InlineData(369, false)]
    [InlineData(185, true)]
    [InlineData(2048, true)]
    public void ExcessiveTransformsAreRejectedBeforeComputing(int size, bool padded)
    {
        var psf = new PsfResult(new double[size, size], 4, size, 5, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.ComputePsfMtf(psf, padded));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2049)]
    [InlineData(int.MaxValue)]
    public void InvalidPsfMetadataIsRejectedWithoutAllocatingItsDeclaredGrid(int size)
    {
        var psf = new PsfResult(new double[1, 1], 4, size, 5, 2);
        Assert.Throws<ArgumentException>(() => DiffractionEngine.ComputePsfMtf(psf));
    }

    [Fact]
    public void DirectFrequencyBudgetIsCheckedBeforeReadingTheFrequencyList()
    {
        var psf = new PsfResult(new double[128, 128], 4, 128, 5, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiffractionEngine.ComputePsfMtfAtFrequencies(psf, new UnreadableFrequencyList(4000)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void DiffractionEntryPointsObserveAnAlreadyCancelledRequest(int entryPoint)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        var optic = Optic.CreateCookeTriplet();
        var wave = optic.Wavelengths[0];
        var psf = new PsfResult(new double[8, 8], 4, 8, 5, 2);
        Assert.Throws<OperationCanceledException>(() =>
        {
            switch (entryPoint)
            {
                case 0: DiffractionEngine.ComputePsfMtf(psf); break;
                case 1: DiffractionEngine.ComputePsfMtfAtFrequencies(psf, [1]); break;
                case 2: DiffractionEngine.ComputeFftMtf(psf, optic, wave); break;
                case 3: DiffractionEngine.ComputeMmdftPsf(optic, (0, 0), wave, 4, 8); break;
                default: DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wave, 4, 8, .001); break;
            }
        });
    }

    [Fact]
    public async Task NonPowerOfTwoTransformObservesCancellationDuringComputation()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var psf = new PsfResult(new double[367, 367], 4, 367, 5, 2);
        var pending = Task.Factory.StartNew(() =>
        {
            using var scope = ComputationCancellation.Push(cancellation.Token);
            started.SetResult();
            return DiffractionEngine.ComputePsfMtf(psf);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(30);
            Assert.False(pending.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
        }
        finally { cancellation.Cancel(); }
    }

    [Fact]
    public void DatabaseRejectsOversizedCompressedHeaderBeforeReadingItsPayload()
    {
        var bytes = CreateDatabase();
        var compressedLength = NonSequentialRayDatabaseWriter.MaximumHeaderCompressedBytes + 1;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), compressedLength);
        using var stream = new GuardedSparseStream(52L + compressedLength + 28, [(0L, bytes[..52])]);
        Assert.Throws<InvalidDataException>(() => new NonSequentialRayDatabaseReader(stream));
        Assert.Equal(52, stream.BytesRead);
    }

    [Fact]
    public void DatabaseRejectsOversizedCompressedChunkAtIndexValidation()
    {
        var bytes = CreateDatabase();
        var originalIndex = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(bytes.Length - 16, 8));
        var chunkOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan((int)originalIndex + 8, 8));
        var suffix = bytes[(int)originalIndex..];
        var compressedLength = NonSequentialRayDatabaseWriter.MaximumChunkCompressedBytes + 1;
        BinaryPrimitives.WriteInt32LittleEndian(suffix.AsSpan(24, 4), compressedLength);
        var indexOffset = chunkOffset + 48 + compressedLength;
        BinaryPrimitives.WriteInt64LittleEndian(suffix.AsSpan(suffix.Length - 16, 8), indexOffset);
        using var stream = new GuardedSparseStream(indexOffset + suffix.Length,
            [(0L, bytes[..(int)chunkOffset]), (indexOffset, suffix)]);
        Assert.Throws<InvalidDataException>(() => new NonSequentialRayDatabaseReader(stream));
        Assert.InRange(stream.BytesRead, 52, 4096);
    }

    [Fact]
    public void DatabaseRangeDoesNotAllocateTheRequestedCapacityForASmallFile()
    {
        using var stream = new MemoryStream(CreateDatabase());
        using var reader = new NonSequentialRayDatabaseReader(stream);
        Assert.Single(reader.ReadRange(0, int.MaxValue));
    }

    private static byte[] CreateDatabase()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay));
        using var stream = new MemoryStream();
        using (var writer = new NonSequentialRayDatabaseWriter(stream, NonSequentialRayDatabaseHeader.Create(document), leaveOpen: true))
        {
            writer.OnBranch(Assert.Single(new NonSequentialDocumentTracer().Trace(document, optic.Materials).Branches));
            writer.Complete();
        }
        return stream.ToArray();
    }

    [Theory]
    [InlineData("Image Simulation")]
    [InlineData("Partially Coherent Image Analysis")]
    [InlineData("Extended Diffraction Image Analysis")]
    public void StarterImageSimulationReportsFoldedPupilWithSettingsAndNoFabricatedRaster(string name)
    {
        var optic = Optic.CreateDemo();
        BaseAnalysis analysis = name switch
        {
            "Partially Coherent Image Analysis" => new PartiallyCoherentImageAnalysis(optic, imageSize: 16),
            "Extended Diffraction Image Analysis" => new ExtendedDiffractionImageAnalysis(optic, imageSize: 16, fieldGrid: 2),
            _ => new ImageSimulationAnalysis(optic, new ImageSimulationConfig
            {
                SourceImage = new RgbImage(new double[1, 16, 16]),
                WavelengthsMicrometers = [.55],
                PsfSize = 8,
                NumRays = 16
            })
        };
        var result = analysis.GenerateData();
        Assert.Equal(name, result.Name);
        Assert.Equal(AnalysisOutcome.Unavailable, result.Outcome);
        Assert.Contains("folds", result.OutcomeReason, StringComparison.Ordinal);
        Assert.Contains("normalized field", result.OutcomeReason, StringComparison.Ordinal);
        Assert.Contains("wavelength=", result.OutcomeReason, StringComparison.Ordinal);
        Assert.Contains("reduce the image field height", result.OutcomeReason, StringComparison.Ordinal);
        Assert.Empty(result.PlotSeries);
        Assert.Null(result.PlotPanes);
    }

    [Fact]
    public void DirectImageSimulationKeepsTheFoldedMappingException()
    {
        var exception = Assert.Throws<AnalysisDataUnavailableException>(() => ImageSimulationEngine.Simulate(
            Optic.CreateDemo(), new RgbImage(new double[1, 16, 16]), new ImageSimulationConfig
            {
                WavelengthsMicrometers = [.55],
                PsfSize = 8,
                NumRays = 16
            }));
        Assert.Equal("Relative Illumination", exception.AnalysisName);
        Assert.Contains("folds", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void UnavailableImageSimulationPreservesTheOutputFileAndItsDiagnostic()
    {
        var path = Path.Combine(Path.GetTempPath(), $"unavailable-image-{Guid.NewGuid():N}.png");
        var original = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(path, original);
        try
        {
            var data = new WorkbenchRuntime(Optic.CreateDemo()).BuildAnalysisData("Image Simulation",
                new Dictionary<string, string>
                {
                    ["ImageWidth"] = "16",
                    ["ImageHeight"] = "16",
                    ["PsfSize"] = "8",
                    ["NumRays"] = "16",
                    ["AberrationMode"] = "衍射",
                    ["OutputFile"] = path
                });
            Assert.Equal(AnalysisOutcome.Unavailable, data.Outcome);
            Assert.Contains("folds", data.OutcomeReason, StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void StarterImageSimulationProducesValidDataWhenAnExplicitSmallFieldIsSelected()
    {
        var pixels = new double[1, 16, 16];
        for (var row = 0; row < 16; row++)
            for (var column = 0; column < 16; column++) pixels[0, row, column] = 1;
        var analysis = new ImageSimulationAnalysis(Optic.CreateDemo(), new ImageSimulationConfig
        {
            SourceImage = new RgbImage(pixels),
            WavelengthsMicrometers = [.55],
            FieldHeight = 1,
            PsfSize = 8,
            NumRays = 5
        });
        var result = analysis.GenerateData();
        Assert.Equal(AnalysisOutcome.Success, result.Outcome);
        Assert.All(result.PlotPanes!, pane => Assert.Equal(AnalysisSeriesKind.Raster, Assert.Single(pane.Series).Kind));
        Assert.True(Assert.IsType<double>(result.Values["MaximumOutputValue"]) > 0);
    }

    private sealed class UnreadableFrequencyList(int count) : IReadOnlyList<double>
    {
        public int Count => count;
        public double this[int index] => throw new InvalidOperationException("Budget must be checked before reading frequencies.");
        public IEnumerator<double> GetEnumerator() => throw new InvalidOperationException("Budget must be checked before reading frequencies.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // Virtual gaps prove that file length is not an allocation budget, without
    // creating a huge fixture or permitting a huge payload read in the test.
    private sealed class GuardedSparseStream(long length, (long Offset, byte[] Bytes)[] segments) : Stream
    {
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (buffer.Length > 4096) throw new InvalidOperationException("Oversized payload read attempted.");
            var count = (int)Math.Min(buffer.Length, Math.Max(0, Length - Position));
            buffer[..count].Clear();
            foreach (var segment in segments)
            {
                var start = Math.Max(Position, segment.Offset);
                var end = Math.Min(Position + count, segment.Offset + segment.Bytes.Length);
                if (end > start)
                    segment.Bytes.AsSpan((int)(start - segment.Offset), (int)(end - start))
                        .CopyTo(buffer[(int)(start - Position)..]);
            }
            Position += count;
            BytesRead += count;
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
