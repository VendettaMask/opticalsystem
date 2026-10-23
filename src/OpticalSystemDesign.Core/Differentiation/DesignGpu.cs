using System.Diagnostics;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

namespace OpticalSystemDesign.Core.Differentiation;

public enum DesignComputeMode { Cpu = 0, Automatic = 1, Gpu = 2 }
public sealed record DesignGpuStatus(bool IsAvailable, string DeviceName, string? UnavailableReason);
public sealed record DesignComputeExecution(string Backend, string DeviceName, int GpuBatches, long GpuRays, double GpuMilliseconds)
{
    public static DesignComputeExecution Cpu { get; } = new("cpu-forward-v1", "CPU", 0, 0, 0);
    public string? FallbackReason { get; init; }
}

/// <summary>C# CUDA chain-rule execution; optical roots and physical acceptance stay in shared Core.</summary>
public static partial class DesignGpu
{
    private static readonly Lazy<Runtime?> Instance = new(CreateRuntime, LazyThreadSafetyMode.ExecutionAndPublication);
    private static string? _unavailableReason;
    public static DesignGpuStatus Status => Instance.Value is { } runtime
        ? new(true, runtime.DeviceName, null) : new(false, "", _unavailableReason ?? "No compatible CUDA device.");

    private static Runtime? CreateRuntime()
    {
        try { return new Runtime(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _unavailableReason = error.GetBaseException().Message;
            return null;
        }
    }

    internal static (double[] Gradients, double Milliseconds) Differentiate(IReadOnlyList<DifferentialTape> tapes,
        IReadOnlyList<int[]> outputs, int parameters, CancellationToken cancellationToken)
    {
        var runtime = Instance.Value ?? throw new NotSupportedException("CUDA is unavailable: " + Status.UnavailableReason);
        return runtime.Differentiate(tapes, outputs, parameters, cancellationToken);
    }

    private sealed partial class Runtime
    {
        private readonly Context _context;
        private readonly Accelerator _accelerator;
        private readonly Action<Index1D, ArrayView<GradientTapeNode>, ArrayView<int>, ArrayView<double>, ArrayView<double>, int> _kernel;
        private readonly object _gate = new();
        public string DeviceName => _accelerator.Name;

        public Runtime()
        {
            _context = Context.Create(builder => builder.Cuda().Inlining(InliningMode.Conservative).EnableAlgorithms());
            Accelerator? initialized = null;
            try
            {
                CudaDevice? selected = null;
                foreach (var candidate in _context.GetCudaDevices())
                    if (selected is null || candidate.MemorySize > selected.MemorySize) selected = candidate;
                var device = selected ?? throw new NotSupportedException("No CUDA accelerator was found.");
                _accelerator = initialized = device.CreateAccelerator(_context);
                if (_accelerator.AcceleratorType != AcceleratorType.Cuda) throw new NotSupportedException("A hardware CUDA accelerator is required.");
                _kernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<GradientTapeNode>, ArrayView<int>, ArrayView<double>, ArrayView<double>, int>(ReverseKernel);
                AppDomain.CurrentDomain.ProcessExit += (_, _) => { lock (_gate) { DisposePropagationBuffers(); _accelerator.Dispose(); _context.Dispose(); } };
            }
            catch { initialized?.Dispose(); _context.Dispose(); throw; }
        }

        public (double[], double) Differentiate(IReadOnlyList<DifferentialTape> tapes, IReadOnlyList<int[]> outputs,
            int parameters, CancellationToken cancellationToken)
        {
            var nodeCount = tapes.Sum(tape => tape.Nodes.Count);
            if (nodeCount > 2_000_000) throw new GpuTapeRangeException("The CUDA batch exceeds its bounded graph capacity.");
            if (tapes.Count == 0 || outputs.Count != tapes.Count || parameters is < 1 or > 512)
                throw new ArgumentException("Invalid CUDA tangent batch.");
            var nodes = new GradientTapeNode[Math.Max(1, nodeCount)];
            var ranges = new int[tapes.Count * 7];
            var offset = 0;
            for (var ray = 0; ray < tapes.Count; ray++)
            {
                if (outputs[ray].Length != 5) throw new ArgumentException("A ray has five differentiable outputs.");
                ranges[ray * 7] = offset;
                ranges[ray * 7 + 1] = tapes[ray].Nodes.Count;
                for (var index = 0; index < tapes[ray].Nodes.Count; index++) nodes[offset + index] = tapes[ray].Nodes[index];
                for (var component = 0; component < 5; component++) ranges[ray * 7 + 2 + component] = outputs[ray][component];
                offset += tapes[ray].Nodes.Count;
            }
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var timer = Stopwatch.StartNew();
                using var deviceNodes = _accelerator.Allocate1D(nodes);
                using var deviceRanges = _accelerator.Allocate1D(ranges);
                using var adjoints = _accelerator.Allocate1D<double>(Math.Max(1, (long)nodeCount * 5));
                using var gradients = _accelerator.Allocate1D<double>((long)tapes.Count * 5 * parameters);
                adjoints.MemSetToZero();
                gradients.MemSetToZero();
                _kernel(tapes.Count * 5, deviceNodes.View, deviceRanges.View, adjoints.View, gradients.View, parameters);
                _accelerator.Synchronize();
                var result = gradients.GetAsArray1D();
                timer.Stop();
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Any(value => !double.IsFinite(value))) throw new GpuTapeRangeException("A CUDA tangent is outside the finite double range.");
                return (result, timer.Elapsed.TotalMilliseconds);
            }
        }

        private static void ReverseKernel(Index1D thread, ArrayView<GradientTapeNode> nodes, ArrayView<int> ranges,
            ArrayView<double> adjoints, ArrayView<double> gradients, int parameters)
        {
            var ray = thread / 5;
            var component = thread % 5;
            var start = ranges[ray * 7];
            var count = ranges[ray * 7 + 1];
            var output = ranges[ray * 7 + 2 + component];
            if (output < 0) return;
            var scratch = start * 5 + component * count;
            var gradient = thread * parameters;
            adjoints[scratch + output] = 1;
            for (var index = count - 1; index >= 0; index--)
            {
                var tangent = adjoints[scratch + index];
                if (tangent == 0) continue;
                var node = nodes[start + index];
                if (node.Parameter >= 0) gradients[gradient + node.Parameter] += tangent * node.LeftWeight;
                else
                {
                    if (node.Left >= 0) adjoints[scratch + node.Left] += tangent * node.LeftWeight;
                    if (node.Right >= 0) adjoints[scratch + node.Right] += tangent * node.RightWeight;
                }
            }
        }
    }
}
