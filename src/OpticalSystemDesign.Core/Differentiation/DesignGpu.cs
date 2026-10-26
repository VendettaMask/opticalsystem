using System.Diagnostics;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using OpticalSystemDesign.Core.Raytrace;

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
        var runtime = RequireRuntime();
        return runtime.Differentiate(tapes, outputs, parameters, cancellationToken);
    }

    private static Runtime RequireRuntime()
    {
        if (SequentialTraceMeasurement.HasCpuBudget)
            throw new NotSupportedException("CUDA computation cannot execute inside a hard CPU trace quota.");
        return Instance.Value ?? throw new NotSupportedException("CUDA is unavailable: " + Status.UnavailableReason);
    }

    private sealed partial class Runtime
    {
        private readonly Context _context;
        private readonly Accelerator _accelerator;
        private readonly Action<Index1D, ArrayView<GradientTapeNode>, ArrayView<int>, ArrayView<double>, ArrayView<double>, int> _kernel;
        private readonly object _gate = new();
        private GradientTapeNode[] _hostGradientNodes = [];
        private int[] _hostGradientRanges = [];
        private MemoryBuffer1D<GradientTapeNode, Stride1D.Dense>? _gradientNodes;
        private MemoryBuffer1D<int, Stride1D.Dense>? _gradientRanges;
        private MemoryBuffer1D<double, Stride1D.Dense>? _gradientAdjoints, _gradientResults;
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
                AppDomain.CurrentDomain.ProcessExit += (_, _) => { lock (_gate) { DisposePropagationBuffers(); DisposeDifferentiationBuffers(); DisposeFixedInputBuffers(); _accelerator.Dispose(); _context.Dispose(); } };
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
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_hostGradientNodes.Length < Math.Max(1, nodeCount)) _hostGradientNodes = new GradientTapeNode[Math.Max(1, nodeCount)];
                if (_hostGradientRanges.Length < tapes.Count * 8) _hostGradientRanges = new int[tapes.Count * 8];
                var nodes = _hostGradientNodes;
                var ranges = _hostGradientRanges;
                var offset = 0;
                for (var ray = 0; ray < tapes.Count; ray++)
                {
                    if (outputs[ray].Length != 6) throw new ArgumentException("A ray has six differentiable outputs.");
                    ranges[ray * 8] = offset;
                    ranges[ray * 8 + 1] = tapes[ray].Nodes.Count;
                    for (var index = 0; index < tapes[ray].Nodes.Count; index++) nodes[offset + index] = tapes[ray].Nodes[index];
                    for (var component = 0; component < 6; component++) ranges[ray * 8 + 2 + component] = outputs[ray][component];
                    offset += tapes[ray].Nodes.Count;
                }
                cancellationToken.ThrowIfCancellationRequested();
                var timer = Stopwatch.StartNew();
                EnsureBuffer(ref _gradientNodes, Math.Max(1, nodeCount));
                EnsureBuffer(ref _gradientRanges, tapes.Count * 8);
                EnsureBuffer(ref _gradientAdjoints, Math.Max(1, nodeCount * 6));
                EnsureBuffer(ref _gradientResults, tapes.Count * 6 * parameters);
                var deviceNodes = _gradientNodes!.View.BaseView.SubView(0, Math.Max(1, nodeCount));
                var deviceRanges = _gradientRanges!.View.BaseView.SubView(0, tapes.Count * 8);
                var adjoints = _gradientAdjoints!.View.BaseView.SubView(0, Math.Max(1, nodeCount * 6));
                var gradients = _gradientResults!.View.BaseView.SubView(0, tapes.Count * 6 * parameters);
                ReadOnlySpan<GradientTapeNode> nodeSpan = nodes.AsSpan(0, Math.Max(1, nodeCount));
                ReadOnlySpan<int> rangeSpan = ranges.AsSpan(0, tapes.Count * 8);
                deviceNodes.CopyFromCPU(nodeSpan);
                deviceRanges.CopyFromCPU(rangeSpan);
                adjoints.MemSetToZero();
                gradients.MemSetToZero();
                _kernel(tapes.Count * 6, deviceNodes, deviceRanges, adjoints, gradients, parameters);
                _accelerator.Synchronize();
                var result = new double[tapes.Count * 6 * parameters];
                gradients.CopyToCPU(result);
                timer.Stop();
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Any(value => !double.IsFinite(value))) throw new GpuTapeRangeException("A CUDA tangent is outside the finite double range.");
                return (result, timer.Elapsed.TotalMilliseconds);
            }
        }

        private void DisposeDifferentiationBuffers()
        { _gradientNodes?.Dispose(); _gradientRanges?.Dispose(); _gradientAdjoints?.Dispose(); _gradientResults?.Dispose(); }

        private static void ReverseKernel(Index1D thread, ArrayView<GradientTapeNode> nodes, ArrayView<int> ranges,
            ArrayView<double> adjoints, ArrayView<double> gradients, int parameters)
        {
            var ray = thread / 6;
            var component = thread % 6;
            var start = ranges[ray * 8];
            var count = ranges[ray * 8 + 1];
            var output = ranges[ray * 8 + 2 + component];
            if (output < 0) return;
            var scratch = start * 6 + component * count;
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
