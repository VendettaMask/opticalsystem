using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis.Assembly;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Application.Services;

internal sealed class OpticalAssemblyService(WorkspaceCoordinator workspace, IWorkbenchModeService modes)
    : WorkbenchServiceBase(workspace), IOpticalAssemblyService
{
    public async Task<OpticalAssemblyResult> CalculateAsync(OpticalAssemblyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (optic, revision, linked) = Snapshot(cancellationToken);
        using (linked)
        {
            return await Task.Run(() =>
            {
                using var scope = ComputationCancellation.Push(linked.Token);
                var rows = new AssemblyConjugates(optic, request.WavelengthNanometers, request.FromRear).Calculate();
                linked.Token.ThrowIfCancellationRequested();
                return new OpticalAssemblyResult(optic.Name, revision, request,
                    rows.Select(row => new OpticalAssemblyRow(row.SurfaceNumber, row.SurfaceLabel,
                        (AssemblyConjugateKind)row.Kind, (AssemblyConjugateStatus)row.Status,
                        row.PositionMillimeters, row.Message)).ToArray());
            }, linked.Token).ConfigureAwait(false);
        }
    }

    public async Task<AssemblyReticleResult> SimulateAsync(AssemblyReticleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Optics);
        var (optic, revision, linked) = Snapshot(cancellationToken, request.ExpectedRevision);
        using (linked)
        {
            return await Task.Run(() =>
            {
                using var scope = ComputationCancellation.Push(linked.Token);
                var image = new AssemblyConjugates(optic, request.Optics.WavelengthNanometers, request.Optics.FromRear)
                    .Simulate(request.SurfaceNumber, (AssemblyImageKind)request.Kind, new AssemblyProbe(
                        request.HeadFocalLength, request.CollimatorFocalLength, request.NumericalAperture,
                        request.ReticleLength, request.ReticleWidth, request.FocusOffset, request.SensorWidth,
                        (AssemblyProbeMode)request.Mode, request.PupilDiameter, request.InstrumentDistance));
                linked.Token.ThrowIfCancellationRequested();
                return new AssemblyReticleResult(revision, image.Size, image.SensorWidth, image.Pixels,
                    image.LaunchedRays, image.ReturnedRays, image.RecordedRays, image.Magnification,
                    image.ImageLength, image.HeadPosition, image.Message,
                    (AssemblyMeasurementMode)image.Mode, image.CollimatorPosition, image.MatchesSelectedConjugate);
            }, linked.Token).ConfigureAwait(false);
        }
    }

    private (Optic Optic, long Revision, CancellationTokenSource Token) Snapshot(CancellationToken token, long? expected = null)
    {
        token.ThrowIfCancellationRequested();
        lock (Gate)
        {
            if (modes.CurrentMode != OpticalWorkbenchMode.Sequential)
                throw new InvalidOperationException("光学装调实验室需要打开顺序镜头设计。");
            if (expected is { } revision && revision != Workspace.Revision)
                throw new InvalidOperationException("镜头已经改变，请重新计算找像位置。");
            // Independent, uncached snapshot: never change the working prescription.
            return (Optic.FromSnapshot(Runtime.CurrentOptic.ToSnapshot()), Workspace.Revision,
                Workspace.LinkDocumentToken(token));
        }
    }
}
