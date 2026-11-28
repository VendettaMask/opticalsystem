using System.Runtime.InteropServices;
using ILGPU;
using ILGPU.Runtime;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public struct GpuPsfInputs
{
    public ArrayView<double> Source, Kernels, Coefficients;
    public int Width, Height, KernelWidth, KernelHeight, Components, FirstPixel;

    public double Reconstruct(int pixel, int tap)
    {
        var value = Kernels[tap];
        var area = KernelWidth * KernelHeight;
        for (var component = 0; component < Components; component++)
            value += Coefficients[component * Width * Height + pixel] * Kernels[(component + 1) * area + tap];
        return value;
    }
}

internal sealed partial class AnalysisGpuDevice
{
    private Action<Index1D, GpuPsfInputs, ArrayView<double>, ArrayView<double>> _normalize = null!;
    private Action<Index1D, GpuPsfInputs, ArrayView<double>, ArrayView<double>> _gather = null!;

    private void LoadConvolutionKernels()
    {
        _normalize = _accelerator.LoadAutoGroupedStreamKernel<Index1D, GpuPsfInputs, ArrayView<double>, ArrayView<double>>(NormalizePsf);
        _gather = _accelerator.LoadAutoGroupedStreamKernel<Index1D, GpuPsfInputs, ArrayView<double>, ArrayView<double>>(GatherPsf);
    }

    private static void NormalizePsf(Index1D index, GpuPsfInputs input, ArrayView<double> positive, ArrayView<double> negative)
    {
        int pixel = input.FirstPixel + index;
        positive[pixel] = 0;
        negative[pixel] = 0;
        var power = input.Source[pixel];
        if (power == 0) return;
        if (!(power >= 0 && power <= double.MaxValue)) { negative[pixel] = -2; return; }
        double pos = 0, neg = 0;
        for (var tap = 0; tap < input.KernelWidth * input.KernelHeight; tap++)
        {
            var v = input.Reconstruct(pixel, tap);
            if (!(v >= -double.MaxValue && v <= double.MaxValue)) { negative[pixel] = -1; return; }
            if (v > 0) pos += v; else neg -= v;
        }
        positive[pixel] = pos;
        negative[pixel] = pos > 0 && pos <= double.MaxValue && neg <= double.MaxValue ? neg : -1;
    }

    // One thread owns one output pixel. Gather in scalar source order to avoid atomics
    // while retaining the exact finite-window loss and source-dependent normalization.
    private static void GatherPsf(Index1D index, GpuPsfInputs input, ArrayView<double> positive, ArrayView<double> output)
    {
        int pixel = input.FirstPixel + index;
        var centerX = pixel % input.Width + (input.KernelWidth - 1) / 2;
        var centerY = pixel / input.Width + (input.KernelHeight - 1) / 2;
        double sum = 0;
        for (var y = Math.Max(0, centerY - input.KernelHeight + 1); y <= Math.Min(input.Height - 1, centerY); y++)
            for (var x = Math.Max(0, centerX - input.KernelWidth + 1); x <= Math.Min(input.Width - 1, centerX); x++)
            {
                var source = y * input.Width + x;
                if (positive[source] == 0) continue;
                var v = input.Reconstruct(source, (centerY - y) * input.KernelWidth + centerX - x);
                if (v > 0) sum += input.Source[source] * (v / positive[source]);
            }
        output[pixel] = sum;
    }

    internal PsfIntensityConvolutionResult Convolve(double[,] source, IReadOnlyList<double[,]> eigen,
        double[,,] coefficients, double[,] mean)
    {
        var count = source.Length;
        using var pixels = _accelerator.Allocate1D<double>(count);
        using var kernels = _accelerator.Allocate1D<double>((1 + eigen.Count) * mean.Length);
        using var maps = _accelerator.Allocate1D<double>(Math.Max(1, coefficients.Length));
        using var positive = _accelerator.Allocate1D<double>(count);
        using var negative = _accelerator.Allocate1D<double>(count);
        using var output = _accelerator.Allocate1D<double>(count);
        pixels.View.BaseView.CopyFromCPU(MemoryMarshal.CreateReadOnlySpan(ref source[0, 0], count));
        if (coefficients.Length != 0)
            maps.View.BaseView.CopyFromCPU(MemoryMarshal.CreateReadOnlySpan(ref coefficients[0, 0, 0], coefficients.Length));
        for (var i = 0; i <= eigen.Count; i++)
        {
            var kernel = i == 0 ? mean : eigen[i - 1];
            kernels.View.BaseView.SubView(i * mean.Length, mean.Length)
                .CopyFromCPU(MemoryMarshal.CreateReadOnlySpan(ref kernel[0, 0], kernel.Length));
        }
        var input = new GpuPsfInputs
        {
            Source = pixels.View,
            Kernels = kernels.View,
            Coefficients = maps.View,
            Width = source.GetLength(1),
            Height = source.GetLength(0),
            KernelWidth = mean.GetLength(1),
            KernelHeight = mean.GetLength(0),
            Components = eigen.Count
        };
        // Limit work per dispatch on display adapters and allow cancellation between tiles.
        var tile = Math.Max(1, 8_000_000 / (mean.Length * (1 + eigen.Count)));
        void RunTiles(Action<Index1D, GpuPsfInputs, ArrayView<double>, ArrayView<double>> kernel, ArrayView<double> target)
        {
            for (var start = 0; start < count; start += tile)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                input.FirstPixel = start;
                kernel(Math.Min(tile, count - start), input, positive.View, target);
                _accelerator.Synchronize();
            }
        }
        RunTiles(_normalize, negative.View);
        var masses = negative.GetAsArray1D();
        var corrected = 0;
        double maximumNegative = 0;
        foreach (var mass in masses)
        {
            if (mass == -2) throw new ArgumentException("Source intensity must be finite and nonnegative.", nameof(source));
            if (mass == -1) throw ImageSimulationEngine.InvalidIntensityPsf();
            if (mass > 1e-12) corrected++;
            maximumNegative = Math.Max(maximumNegative, mass);
        }
        ComputationCancellation.ThrowIfCancellationRequested();
        RunTiles(_gather, output.View);
        var result = new double[input.Height, input.Width];
        output.View.BaseView.CopyToCPU(MemoryMarshal.CreateSpan(ref result[0, 0], count));
        ComputationCancellation.ThrowIfCancellationRequested();
        return new(result, corrected, maximumNegative);
    }
}
