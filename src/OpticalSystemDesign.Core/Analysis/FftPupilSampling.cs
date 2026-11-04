using System.Numerics;

namespace OpticalSystemDesign.Core.Analysis;

public enum FftPhaseSamplingStatus { ResolvedAtNodes, UndersampledPhase, UnverifiedNodeOrder, NoConnectedSamples }

public sealed class FftPupilSamplingException(string analysis, FftPupilSampling diagnostics)
    : InvalidOperationException(MessageFor(analysis, diagnostics))
{
    public FftPupilSampling Diagnostics { get; } = diagnostics;

    private static string MessageFor(string analysis, FftPupilSampling diagnostics)
    {
        var reason = diagnostics.Status switch
        {
            FftPhaseSamplingStatus.UndersampledPhase =>
                $"光瞳相位存在混叠风险，相邻实际受光节点最大相位差 {diagnostics.MaximumStepWaves:G6} 波，超过本程序局部相位检查的 {FftPupilSampling.MaximumAdjacentStepWaves:G6} 波界限",
            FftPhaseSamplingStatus.UnverifiedNodeOrder => "无法验证光瞳节点的物理相邻关系",
            FftPhaseSamplingStatus.NoConnectedSamples => "没有可供二维相位检查的相邻受光节点",
            _ => "未通过光瞳采样检查"
        };
        return $"{analysis}: {reason}。当前 FFT 数据不可用；需要独立加密验证，或使用适用的 Huygens 分析。此检查不代表 Zemax 的内部报错判据，也不构成收敛认证。";
    }
}

/// <summary>
/// Conservative local alias-risk diagnostic on the actual illuminated Fourier grid.
/// For P=A*exp(i*2*pi*W), a locally linear carrier reaches the discrete Nyquist
/// frequency at |delta W|=1/2 cycle per node. This does not establish the bandwidth
/// of a general pupil, detect variation between nodes, or certify output convergence.
/// OpticStudio documents grid/image scaling and aliasing risks, but does not publish
/// its internal rejection threshold. This check must not be described as that rule.
/// </summary>
public sealed record FftPupilSampling(FftPhaseSamplingStatus Status, int ConnectedEdges, double MaximumStepWaves)
{
    public const double MaximumAdjacentStepWaves = 0.5;
    public string Criterion => "LocalUnwrappedPupilPhaseStep/v1";
    public bool CertifiesConvergence => false;
    private static readonly AsyncLocal<int> RequiredDepth = new();
    private static readonly AsyncLocal<List<FftPupilSampling>?> Observations = new();
    public static bool IsRequired => RequiredDepth.Value > 0;

    /// <summary>Validate user-facing results in this asynchronous request; finite-sum APIs remain explicit.</summary>
    public static IDisposable RequireResolvedResults() => new RequiredScope();
    private sealed class RequiredScope : IDisposable
    {
        private readonly int previous = RequiredDepth.Value;
        private bool disposed;
        public RequiredScope() { RequiredDepth.Value = previous + 1; }
        public void Dispose() { if (!disposed) { RequiredDepth.Value = previous; disposed = true; } }
    }

    internal void EnforceForCurrentRequest(string analysis)
    {
        if (IsRequired) RequireResolved(analysis);
    }

    public void RequireResolved(string analysis)
    {
        if (Observations.Value is { } observations)
        {
            lock (observations) observations.Add(this);
            return;
        }
        if (Status != FftPhaseSamplingStatus.ResolvedAtNodes)
            throw new FftPupilSamplingException(analysis, this);
    }

    // Only the bounded result validator can defer rejection. It must validate
    // two resolved references before publishing any finite-sum candidate.
    internal static IDisposable ObserveForValidation(List<FftPupilSampling> observations)
        => new ObservationScope(observations);

    private sealed class ObservationScope(List<FftPupilSampling> observations) : IDisposable
    {
        private readonly List<FftPupilSampling>? previous = Set(observations);
        private bool disposed;
        public void Dispose() { if (!disposed) { Observations.Value = previous; disposed = true; } }
        private static List<FftPupilSampling>? Set(List<FftPupilSampling> value)
        { var old = Observations.Value; Observations.Value = value; return old; }
    }

    internal static FftPupilSampling Assess(WavefrontResult wavefront, PupilGridSpecification grid,
        Complex[,] pupil, Func<WavefrontSample, double> phaseWaves)
    {
        // Two one-row buffers avoid another dense allocation for raster grids.
        var previousPhase = new double[grid.SampleCount];
        var previousRow = Enumerable.Repeat(-2, grid.SampleCount).ToArray();
        var lastRow=-1;var lastColumn=-1;var leftColumn=-2;var leftPhase=0d;
        var edges=0;var maximum=0d;
        foreach(var sample in wavefront.Samples)
        {
            var row=grid.RowIndex(sample.NormalizedPupilY);var column=grid.ColumnIndex(sample.NormalizedPupilX);
            if(row<lastRow || row==lastRow && column<=lastColumn)
                return new(FftPhaseSamplingStatus.UnverifiedNodeOrder,edges,maximum);
            if(row!=lastRow)leftColumn=-2;
            lastRow=row;lastColumn=column;
            if(sample.Intensity<=0 || pupil[row,column]==Complex.Zero){leftColumn=-2;continue;}
            var phase=phaseWaves(sample);
            if(!double.IsFinite(phase))return new(FftPhaseSamplingStatus.UndersampledPhase,edges,double.PositiveInfinity);
            if(leftColumn==column-1){maximum=Math.Max(maximum,Math.Abs(phase-leftPhase));edges++;}
            if(previousRow[column]==row-1){maximum=Math.Max(maximum,Math.Abs(phase-previousPhase[column]));edges++;}
            leftColumn=column;leftPhase=phase;previousRow[column]=row;previousPhase[column]=phase;
        }
        return new(edges==0?FftPhaseSamplingStatus.NoConnectedSamples
            :maximum>MaximumAdjacentStepWaves*(1+1e-12)?FftPhaseSamplingStatus.UndersampledPhase:FftPhaseSamplingStatus.ResolvedAtNodes,edges,maximum);
    }
}
