using System.Numerics;
using System.Runtime.InteropServices;
using ILGPU;
using ILGPU.Runtime;

namespace OpticalSystemDesign.Core.Analysis;

// Kernel argument types must be visible to ILGPU's generated launch assembly.
[StructLayout(LayoutKind.Sequential)]
public struct GpuFourierValue(double real, double imaginary)
{
    public double Real = real;
    public double Imaginary = imaginary;
}

internal sealed partial class AnalysisGpuDevice
{
    private Action<Index1D, ArrayView<GpuFourierValue>, ArrayView<GpuFourierValue>, int> _permute = null!;
    private Action<Index1D, ArrayView<GpuFourierValue>, ArrayView<GpuFourierValue>, int, int, int> _butterflies = null!;

    private void LoadFourierKernels()
    {
        _permute = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<GpuFourierValue>, ArrayView<GpuFourierValue>, int>(Permute);
        _butterflies = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<GpuFourierValue>, ArrayView<GpuFourierValue>, int, int, int>(Butterflies);
    }

    private static int ReverseBits(int value, int size)
    {
        var reversed = 0;
        for (var bit = 1; bit < size; bit <<= 1)
        {
            reversed = (reversed << 1) | (value & 1);
            value >>= 1;
        }
        return reversed;
    }

    private static void Permute(Index1D index, ArrayView<GpuFourierValue> source, ArrayView<GpuFourierValue> output, int size)
    {
        output[index] = source[ReverseBits(index / size, size) * size + ReverseBits(index % size, size)];
    }

    private static void Butterflies(Index1D index, ArrayView<GpuFourierValue> data, ArrayView<GpuFourierValue> roots,
        int size, int half, int stride)
    {
        int line = index / (size / 2), pair = index % (size / 2);
        var offset = pair % half;
        var element = (pair - offset) * 2 + offset;
        var first = stride == 1 ? line * size + element : element * size + line;
        var second = first + stride * half;
        var even = data[first];
        var odd = data[second];
        var root = roots[half + offset];
        var real = odd.Real * root.Real - odd.Imaginary * root.Imaginary;
        var imaginary = odd.Real * root.Imaginary + odd.Imaginary * root.Real;
        data[first] = new GpuFourierValue(even.Real + real, even.Imaginary + imaginary);
        data[second] = new GpuFourierValue(even.Real - real, even.Imaginary - imaginary);
    }

    internal void Fourier(Complex[,] data, CancellationToken token = default)
    {
        var size = data.GetLength(0);
        // Use the scalar FFT's per-stage twiddle recurrence and the same row/column order.
        var roots = new GpuFourierValue[size];
        for (var half = 1; half < size; half *= 2)
        {
            var root = Complex.FromPolarCoordinates(1, -Math.PI / half);
            var factor = Complex.One;
            for (var offset = 0; offset < half; offset++)
            {
                roots[half + offset] = new GpuFourierValue(factor.Real, factor.Imaginary);
                factor *= root;
            }
        }

        using var input = _accelerator.Allocate1D<GpuFourierValue>(data.Length);
        using var output = _accelerator.Allocate1D<GpuFourierValue>(data.Length);
        using var twiddles = _accelerator.Allocate1D(roots);
        var cells = MemoryMarshal.CreateSpan(ref data[0, 0], data.Length);
        input.View.BaseView.CopyFromCPU((ReadOnlySpan<GpuFourierValue>)MemoryMarshal.Cast<Complex, GpuFourierValue>(cells));
        _permute(data.Length, input.View, output.View, size);
        foreach (var stride in new[] { 1, size })
            for (var half = 1; half < size; half *= 2)
            {
                token.ThrowIfCancellationRequested();
                _butterflies(data.Length / 2, output.View, twiddles.View, size, half, stride);
            }

        // Commit only after successful readback; a driver failure can safely retry on CPU.
        var result = output.GetAsArray1D();
        token.ThrowIfCancellationRequested();
        MemoryMarshal.Cast<GpuFourierValue, Complex>(result.AsSpan()).CopyTo(cells);
    }
}
