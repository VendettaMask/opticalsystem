using System.Numerics;

namespace OpticalSystemDesign.Core.Analysis;

public enum FftPhaseSamplingStatus { ResolvedAtNodes, UndersampledPhase, UnverifiedNodeOrder, NoConnectedSamples }

public sealed class FftPupilSamplingException(string analysis, FftPupilSampling diagnostics)
    : InvalidOperationException($"{analysis}: 光瞳相位采样不足或无法验证，相邻节点最大相位差 {diagnostics.MaximumStepWaves:G6} 波（Nyquist 上限 0.5 波）。请增加实际光瞳采样，或选择适用的 Huygens 分析；当前 FFT 数据不可用。")
{
    public FftPupilSampling Diagnostics { get; } = diagnostics;
}

/// <summary>A necessary pupil-phase Nyquist check, not a convergence certificate.</summary>
public sealed record FftPupilSampling(FftPhaseSamplingStatus Status, int ConnectedEdges, double MaximumStepWaves)
{
    private static readonly AsyncLocal<int> RequiredDepth = new();
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
        if (Status != FftPhaseSamplingStatus.ResolvedAtNodes)
            throw new FftPupilSamplingException(analysis, this);
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
            :maximum>.5*(1+1e-12)?FftPhaseSamplingStatus.UndersampledPhase:FftPhaseSamplingStatus.ResolvedAtNodes,edges,maximum);
    }
}
