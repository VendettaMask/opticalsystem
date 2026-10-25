using OpticalSystemDesign.Core.Differentiation;

namespace OpticalSystemDesign.Core.Raytrace;

public sealed record SequentialGpuExecution(string DeviceName,long AimRays,long AimTraces,long PropagatedRays,long Batches,
    double Milliseconds,long CpuRecoveryRays,string? FallbackReason)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public long CpuAimingRays { get; init; }
    public static SequentialGpuExecution Merge(SequentialGpuExecution? previous, SequentialGpuExecution current) => previous is null ? current : current with
    {
        AimRays = checked(previous.AimRays + current.AimRays), AimTraces = checked(previous.AimTraces + current.AimTraces),
        PropagatedRays = checked(previous.PropagatedRays + current.PropagatedRays), Batches = checked(previous.Batches + current.Batches),
        Milliseconds = previous.Milliseconds + current.Milliseconds, CpuRecoveryRays = checked(previous.CpuRecoveryRays + current.CpuRecoveryRays),
        CpuAimingRays = checked(previous.CpuAimingRays + current.CpuAimingRays), FallbackReason = current.FallbackReason ?? previous.FallbackReason
    };
}

/// <summary>A bounded, operation-local backend selection; never changes the document or global analysis defaults.</summary>
public sealed class SequentialGpuSession : IDisposable
{
    private static readonly AsyncLocal<SequentialGpuSession?> Active = new();
    public static SequentialGpuSession? Current => Active.Value;
    private readonly SequentialGpuSession? _previous;
    private readonly object _gate=new();
    private long _aimRays,_aimTraces,_propagatedRays,_batches,_recoveries;
    private long _cpuAimingRays;
    private double _milliseconds;
    private string? _fallback;
    private string _deviceName="CPU";
    internal bool Enabled { get; }
    public DesignComputeMode Mode { get; }
    internal CancellationToken CancellationToken { get; }

    public SequentialGpuSession(DesignComputeMode mode,CancellationToken cancellationToken=default)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        cancellationToken.ThrowIfCancellationRequested();
        Mode=mode; CancellationToken=cancellationToken; _previous=Active.Value;
        if (mode!=DesignComputeMode.Cpu)
        {
            var status=DesignGpu.Status;
            Enabled=status.IsAvailable;
            if (Enabled) _deviceName=status.DeviceName;
            if (!Enabled)
            {
                _fallback=status.UnavailableReason;
                if (mode==DesignComputeMode.Gpu) throw new NotSupportedException("CUDA is unavailable: "+_fallback);
            }
        }
        Active.Value=this;
    }

    public SequentialGpuExecution Execution
    { get { lock (_gate) return new(_deviceName,_aimRays,_aimTraces,_propagatedRays,_batches,_milliseconds,_recoveries,_fallback) { CpuAimingRays = _cpuAimingRays }; } }

    internal void Record(long aims,long traces,long rays,double milliseconds)
    { lock (_gate) { _aimRays+=aims; _aimTraces+=traces; _propagatedRays+=rays; _batches++; _milliseconds+=milliseconds; } }
    internal void Recover(long rays,string reason)
    { lock (_gate) { _recoveries+=rays; _fallback=reason; } }
    internal void RecordCpuAiming(long rays, string reason)
    { lock (_gate) { _cpuAimingRays += rays; _fallback = reason; } }
    internal bool Fallback(Exception error)
    {
        if (error is not (NotSupportedException or ILGPU.Runtime.Cuda.CudaException)) return false;
        if (Mode==DesignComputeMode.Gpu) return false;
        Recover(0,error.Message); return true;
    }

    public void Dispose()
    {
        if (!ReferenceEquals(Active.Value,this)) throw new InvalidOperationException("Sequential GPU scopes must be disposed in order.");
        Active.Value=_previous;
    }
}
