using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

/// <summary>Hardware evidence belongs to one analysis request. CPU callers do not initialize a driver.</summary>
public sealed class AnalysisGpuSession : IDisposable
{
    private static readonly AsyncLocal<AnalysisGpuSession?> Active = new();
    private static readonly Lazy<AnalysisGpuDevice?> Hardware = new(AnalysisGpuDevice.Detect);
    private readonly AnalysisGpuSession? _previous = Active.Value;
    private string? _usedDevice;
    private int _reportedMemoryLimit;
    public string? UsedDevice => Volatile.Read(ref _usedDevice);
    public static string? AvailableDevice => Hardware.Value?.AvailableName;
    internal static AnalysisGpuSession? Current => Active.Value;

    public AnalysisGpuSession(bool automatic) => Active.Value = automatic ? this : null;
    public void Dispose() => Active.Value = _previous;

    internal bool TryExecute(long bytes, Action<AnalysisGpuDevice> calculate)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        if (Hardware.Value is not { } device) return false;
        if (bytes > device.MaximumWorkBytes)
        {
            if (Interlocked.Exchange(ref _reportedMemoryLimit, 1) == 0)
                System.Diagnostics.Trace.TraceInformation($"GPU memory limit on {device.Name}: requested {bytes} bytes, budget {device.MaximumWorkBytes}. Using CPU.");
            return false;
        }
        if (!device.TryExecute(calculate)) return false;
        Volatile.Write(ref _usedDevice, device.Name);
        return true;
    }
}

internal sealed partial class AnalysisGpuDevice
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    private readonly SemaphoreSlim _gate = new(1);
    private volatile bool _failed;
    public string Name => $"{_accelerator.Name} ({(_accelerator.AcceleratorType == AcceleratorType.Cuda ? "CUDA" : "OpenCL")})";
    public string? AvailableName => _failed ? null : Name;

    private AnalysisGpuDevice(Context context, Accelerator accelerator)
    {
        _context = context;
        _accelerator = accelerator;
        LoadFourierKernels();
        LoadConvolutionKernels();
    }

    internal static AnalysisGpuDevice? Detect()
    {
        // Separate contexts allow a missing CUDA driver and a usable OpenCL ICD to coexist.
        foreach (var cuda in new[] { true, false })
        {
            Context? context = null;
            try
            {
                context = Context.Create(builder =>
                {
                    if (cuda) builder.Cuda(); else builder.OpenCL();
                });
                var backendContext = context;
                foreach (var candidate in backendContext.Devices.OrderByDescending(device => device.MemorySize))
                {
                    if (candidate is not CudaDevice && candidate is not CLDevice
                        { DeviceType: CLDeviceType.CL_DEVICE_TYPE_GPU, Capabilities.Float64: true }) continue;
                    Accelerator? accelerator = null;
                    try
                    {
                        accelerator = candidate.CreateAccelerator(backendContext);
                        var device = new AnalysisGpuDevice(backendContext, accelerator);
                        // Confirm allocation, kernel execution and readback on this physical device.
                        var impulse = new System.Numerics.Complex[2, 2];
                        impulse[0, 0] = 1;
                        device.Fourier(impulse);
                        if (impulse.Cast<System.Numerics.Complex>().Any(value => value != System.Numerics.Complex.One))
                        {
                            System.Diagnostics.Trace.TraceWarning($"GPU self-test failed: {candidate.Name}");
                            continue;
                        }
                        AppDomain.CurrentDomain.ProcessExit += (_, _) => device.Shutdown();
                        accelerator = null;
                        context = null;
                        System.Diagnostics.Trace.TraceInformation($"GPU ready: {device.Name}");
                        return device;
                    }
                    catch (Exception error) when (DriverInitializationError(error))
                    {
                        System.Diagnostics.Trace.WriteLine($"GPU initialization failed on {candidate.Name}: {error}");
                    }
                    finally { accelerator?.Dispose(); }
                }
            }
            catch (Exception error) when (DriverInitializationError(error))
            {
                System.Diagnostics.Trace.WriteLine($"GPU backend discovery failed: {error}");
            }
            finally { context?.Dispose(); }
        }
        System.Diagnostics.Trace.TraceInformation("GPU detection: no compatible compute device. Using CPU.");
        return null;
    }

    private static bool DriverInitializationError(Exception error) =>
        error is not (OperationCanceledException or AccessViolationException);

    private void Shutdown()
    {
        // The OS releases an in-flight device on exit. Do not block process termination.
        if (!_gate.Wait(0)) return;
        try { _failed = true; _accelerator.Dispose(); _context.Dispose(); }
        finally { _gate.Release(); }
    }

    internal long MaximumWorkBytes => Math.Min(256L << 20, _accelerator.MemorySize / 4);

    internal bool TryExecute(Action<AnalysisGpuDevice> calculate)
    {
        if (_failed) return false;
        _gate.Wait(ComputationCancellation.Current);
        try
        {
            if (_failed) return false;
            calculate(this);
            return true;
        }
        catch (OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceWarning($"GPU allocation failed on {Name}. Retrying on CPU.");
            return false;
        }
        catch (Exception error) when (error is CudaException or CLException)
        {
            _failed = true;
            System.Diagnostics.Trace.TraceWarning($"GPU driver failure on {Name}. Using CPU. {error}");
            return false;
        }
        finally { _gate.Release(); }
    }
}
