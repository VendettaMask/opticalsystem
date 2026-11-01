using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Services;
using CoreDetector = OpticalSystemDesign.Core.NonSequential.DetectorRectangleParameters;
using CoreKind = OpticalSystemDesign.Core.NonSequential.NonSequentialObjectKind;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class NonSequentialAnalysisService
{
    private static Contracts.NonSequentialBsdfStatistics? ToBsdfStatistics(Core.NonSequential.NonSequentialBsdfStatistics? s) =>
        s is null ? null : new(s.SampleCount, s.IncomingPowerWatts, s.ReflectedPowerWatts, s.AbsorbedPowerWatts,
            s.CosineSum, s.CosineSquaredSum, s.MinimumPdf, s.MaximumPdf, s.TransmittedPowerWatts, s.ReflectionSampleCount, s.TransmissionSampleCount,
            s.MaterialAxisSampleCount, s.MaterialDirectionXSum, s.MaterialDirectionYSum);

    private static Contracts.NonSequentialBsdfStatistics? MergeBsdfStatistics(Contracts.NonSequentialBsdfStatistics? a,
        Contracts.NonSequentialBsdfStatistics? b) => a is null ? b : b is null ? a : new(
            checked(a.SampleCount + b.SampleCount), a.IncomingPowerWatts + b.IncomingPowerWatts,
            a.ReflectedPowerWatts + b.ReflectedPowerWatts, a.AbsorbedPowerWatts + b.AbsorbedPowerWatts,
            a.CosineSum + b.CosineSum, a.CosineSquaredSum + b.CosineSquaredSum,
            Math.Min(a.MinimumPdf, b.MinimumPdf), Math.Max(a.MaximumPdf, b.MaximumPdf), a.TransmittedPowerWatts + b.TransmittedPowerWatts,
            checked(a.ReflectionSampleCount + b.ReflectionSampleCount), checked(a.TransmissionSampleCount + b.TransmissionSampleCount),
            checked(a.MaterialAxisSampleCount + b.MaterialAxisSampleCount), a.MaterialDirectionXSum + b.MaterialDirectionXSum, a.MaterialDirectionYSum + b.MaterialDirectionYSum);

    internal async Task<CameraNscDetectorInput> CaptureCameraDetectorAsync(Guid detectorId, string? pathFilter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NonSequentialDocument document; NonSequentialTraceSessionDto session; FileStream stream;
        CancellationTokenSource linked; NonSequentialRayDatabaseHeader expectedHeader;
        lock (Gate)
        {
            session = GetCurrentSession() ?? throw new InvalidOperationException("请先完成非序列分析追迹，再读取探测器。");
            if (session.IsStale || session.State is not (NonSequentialTraceSessionState.Completed or NonSequentialTraceSessionState.Warning))
                throw new InvalidOperationException("非序列分析结果已过期或未完成，请重新追迹。");
            document = Runtime.CurrentNonSequentialDocument.Clone();
            expectedHeader = _analysisSession.SelectedDatabaseHeader ?? throw new InvalidOperationException("请先完成非序列分析追迹，再读取探测器。");
            // Hold the opened file, rather than rereading a path which a newer session can replace/delete.
            stream = new FileStream(session.RayDatabasePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }
        using (stream) using (linked)
            return await Task.Run(() =>
            {
                using var cancellation = ComputationCancellation.Push(linked.Token);
                using var reader = new NonSequentialRayDatabaseReader(stream);
                if (reader.Header.SceneHash != session.SceneHash || reader.Header.SourceRevision != session.SourceRevision
                    || reader.Header.CreatedUtc != expectedHeader.CreatedUtc || reader.Header.RandomSeed != expectedHeader.RandomSeed
                    || reader.Header.SplittingMode != expectedHeader.SplittingMode || reader.BranchCount != session.BranchCount)
                    throw new InvalidDataException("非序列数据库与已选分析会话身份不一致。");
                var item = document.Objects.SingleOrDefault(o => o.Id == detectorId && o.Enabled && o.Kind == CoreKind.DetectorRectangle)
                    ?? throw new InvalidOperationException("请选择已启用的矩形探测器。");
                var p = (CoreDetector)item.Parameters;
                CameraSensorPipeline.ValidateDimensions(p.PixelsX, p.PixelsY);
                var filterText = pathFilter ?? session.FilterExpression ?? "";
                var filter = NonSequentialPathFilter.Parse(filterText);
                var frame = NonSequentialDetectorReconstruction.Reconstruct(document, reader.ReadBranches(filter),
                    cancellationToken: linked.Token, detectorId: detectorId).Single();
                _ = NonSequentialCameraSensor.Capture(frame, item, reader.Header.Wavelengths);
                var traceSettings = reader.Header.TraceSettings;
                static NonSequentialVector3 Vector(OpticalSystemDesign.Core.Backend.Vector3D v) => new(v.X, v.Y, v.Z);
                var definition = new CameraNscDetectorDefinition(item.Id, item.Name, p.WidthMillimeters, p.HeightMillimeters,
                    p.PixelsX, p.PixelsY, p.FrontOnly, p.Absorb, Vector(document.ToWorldPoint(item.Id, new(0, 0, 0))),
                    Vector(document.ToWorldDirection(item.Id, new(1, 0, 0))), Vector(document.ToWorldDirection(item.Id, new(0, 1, 0))),
                    Vector(document.ToWorldDirection(item.Id, new(0, 0, 1))));
                var result = new CameraNscDetectorInput(CameraNscDetectorInput.FormatIdentifier, 1, session.Id, session.SceneHash,
                    session.SourceRevision, reader.Header.CreatedUtc, session.BranchCount, session.TraceConfigurationFingerprint,
                    reader.Header.PathFilterExpression, filterText, reader.Header.RandomSeed, session.SplittingMode, definition,
                    Array.AsReadOnly(reader.Header.Wavelengths.Select((w, i) => new NonSequentialWavelengthDto(i + 1, w.Label,
                        w.Nanometers, w.Weight, w.IsPrimary)).ToArray()),
                    Array.AsReadOnly(frame.PowerByWavelength.OrderBy(b => b.Key).Select(b => new CameraNscPowerBand(b.Key,
                        reader.Header.Wavelengths[b.Key - 1].Nanometers, Array.AsReadOnly(b.Value.ToArray()))).ToArray()),
                    new(traceSettings.LayoutRaysPerSource, traceSettings.AnalysisRaysPerSource, traceSettings.MaximumTotalSourceRays,
                        traceSettings.MaximumSegmentsPerRay, traceSettings.MaximumActiveBranches, traceSettings.MinimumRelativeIntensity,
                        traceSettings.RandomSeed, traceSettings.SplitFresnelRays),
                    session.TracePassCount, Array.AsReadOnly(session.SourceObjectIds.ToArray()),
                    Array.AsReadOnly(session.Warnings.ToArray()),
                    document.Objects.Any(o => o.Enabled && o.SurfaceBsdf is not null)
                        ? Array.AsReadOnly(document.Objects.Select((o, i) => (o, i)).Where(t => t.o.Enabled && t.o.SurfaceBsdf is not null)
                            .Select(t => CaptureBsdf(document, t.o, t.i + 1)).ToArray()) : null,
                    reader.Header.SurfaceBsdfProtocol, session.BsdfStatistics);
                linked.Token.ThrowIfCancellationRequested();
                lock (Gate)
                {
                    if (GetCurrentSession() is not { IsStale: false } current || current.Id != session.Id
                        || current.FilterExpression != session.FilterExpression || current.UpdatedUtc != session.UpdatedUtc
                        || current.BranchCount != session.BranchCount || current.RayDatabasePath != session.RayDatabasePath)
                        throw new OperationCanceledException("分析会话已改变，未发布旧探测器输入。", linked.Token);
                }
                return result;
            }, linked.Token).ConfigureAwait(false);
    }

    private static CameraNscSurfaceBsdf CaptureBsdf(NonSequentialDocument document, NonSequentialObjectDefinition o, int number)
    {
        var model = NonSequentialBsdfMapper.ToDto(o.SurfaceBsdf)!;
        if (o.SurfaceBsdf!.MeasuredData?.Coordinates != Core.Scattering.BsdfCoordinates.FixedMaterialAxes) return new(o.Id, number, o.Name, model);
        var axes = o.SurfaceBsdf.WorldAxes(document, o.Id);
        return new(o.Id, number, o.Name, model, new(axes.X.X, axes.X.Y, axes.X.Z), new(axes.Y.X, axes.Y.Y, axes.Y.Z));
    }
}
