using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core.NonSequential;
using CoreSplittingMode = OptilandWorkbench.Core.NonSequential.NonSequentialSplittingMode;
using CoreDetectorRectangleParameters = OptilandWorkbench.Core.NonSequential.DetectorRectangleParameters;
using CoreObjectKind = OptilandWorkbench.Core.NonSequential.NonSequentialObjectKind;
using CoreSourceParameters = OptilandWorkbench.Core.NonSequential.SourceParameters;

namespace OptilandWorkbench.Application.Services;

internal sealed class NonSequentialAnalysisService : WorkbenchServiceBase, INonSequentialAnalysisService
{
    private readonly NonSequentialAnalysisSession _session;

    public NonSequentialAnalysisService(
        WorkspaceCoordinator workspace,
        NonSequentialAnalysisSession session) : base(workspace)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _session.Changed += (_, _) => SessionChanged?.Invoke(this, GetCurrentSession());
    }

    public event EventHandler<NonSequentialTraceSessionDto?>? SessionChanged;

    public NonSequentialTraceSessionDto? GetCurrentSession()
    {
        lock (Gate) return _session.Snapshot(Runtime.CurrentNonSequentialDocument, Workspace.Revision);
    }

    public async Task<NonSequentialTraceSessionDto> EnsureLayoutSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var current = GetCurrentSession();
        if (current is { IsStale: false }
            && current.State is NonSequentialTraceSessionState.Completed or NonSequentialTraceSessionState.Warning)
        {
            return current;
        }

        await TraceAsync(new NonSequentialTraceRunRequestDto(
            OutputMode: OptilandWorkbench.Application.Contracts.NonSequentialTraceOutputMode.RayDatabase,
            AnalysisRays: false,
            Command: NonSequentialTraceCommand.ClearAndTrace), cancellationToken).ConfigureAwait(false);
        return GetCurrentSession()
            ?? throw new InvalidOperationException("非序列布局追迹完成后未建立结果会话。");
    }

    public void ClearDetectors() => _session.Clear();

    public async Task<NonSequentialTraceRunResultDto> TraceAsync(
        NonSequentialTraceRunRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Command == NonSequentialTraceCommand.ClearOnly)
        {
            ClearDetectors();
            return new NonSequentialTraceRunResultDto(
                0, 0, 0, 0, 0, 0, 0, 0, 0, null, 0,
                SessionState: NonSequentialTraceSessionState.Empty,
                TracePassCount: 0);
        }

        NonSequentialDocument document;
        OptilandWorkbench.Core.Materials.MaterialRegistry materials;
        long revision;
        lock (Gate)
        {
            document = Runtime.CurrentNonSequentialDocument.Clone();
            materials = Runtime.CurrentOptic.Materials;
            revision = Workspace.Revision;
        }

        var previous = GetCurrentSession();
        var sceneHash = NonSequentialSceneHasher.Compute(document);
        var splitting = request.SplittingMode
            ?? ((request.SplitFresnelRays ?? document.TraceSettings.SplitFresnelRays)
                ? OptilandWorkbench.Application.Contracts.NonSequentialSplittingMode.FullFresnel
                : OptilandWorkbench.Application.Contracts.NonSequentialSplittingMode.None);
        var sourceIds = ResolveSourceIds(document, request);
        var baseRandomSeed = request.RandomSeed ?? document.TraceSettings.RandomSeed;
        var traceConfigurationFingerprint = CreateTraceConfigurationFingerprint(
            request, document, sourceIds, splitting, baseRandomSeed);
        if (request.Command == NonSequentialTraceCommand.TraceOnly)
        {
            if (previous is null || previous.IsStale || !previous.SceneHash.Equals(sceneHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("仅追迹只能累加到与当前场景一致的有效结果；请先执行“清空并追迹”。");
            }
            if (!string.Equals(
                    previous.TraceConfigurationFingerprint,
                    traceConfigurationFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("光源、射线数量、随机种子、分裂模式、追迹限制或保存筛选已经改变；请执行“清空并追迹”。");
            }
        }

        var requestedPath = string.IsNullOrWhiteSpace(request.RayDatabasePath)
            ? null
            : Path.GetFullPath(request.RayDatabasePath);
        var targetPath = requestedPath ?? CreateManagedDatabasePath();
        var ownsDatabase = requestedPath is null;
        var passCount = request.Command == NonSequentialTraceCommand.TraceOnly
            ? (previous?.TracePassCount ?? 0) + 1
            : 1;
        var randomSeed = checked(baseRandomSeed + passCount - 1);
        using var linkedCancellation = Workspace.LinkDocumentToken(cancellationToken);

        return await Task.Run(() =>
        {
            var timer = Stopwatch.StartNew();
            var directory = Path.GetDirectoryName(targetPath)
                ?? throw new InvalidOperationException("光线数据库路径没有父目录。");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                NonSequentialDocumentTraceResult result;
                NonSequentialTraceSessionDto session;
                NonSequentialTraceRunResultDto response;
                var priorBranches = previous?.BranchCount ?? 0;
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new NonSequentialRayDatabaseWriter(
                    stream,
                    NonSequentialRayDatabaseHeader.Create(document, revision, request.PathFilterExpression) with
                    {
                        RandomSeed = randomSeed,
                        SplittingMode = (CoreSplittingMode)(int)splitting,
                        TraceSettings = document.TraceSettings with
                        {
                            RandomSeed = randomSeed,
                            MaximumSegmentsPerRay = request.MaximumSegmentsPerRay ?? document.TraceSettings.MaximumSegmentsPerRay,
                            MaximumActiveBranches = request.MaximumActiveBranches ?? document.TraceSettings.MaximumActiveBranches,
                            MinimumRelativeIntensity = request.MinimumRelativeIntensity ?? document.TraceSettings.MinimumRelativeIntensity,
                            SplitFresnelRays = splitting == OptilandWorkbench.Application.Contracts.NonSequentialSplittingMode.FullFresnel
                        }
                    },
                    leaveOpen: true))
                {
                    if (request.Command == NonSequentialTraceCommand.TraceOnly && previous is not null)
                    {
                        CopyExistingBranches(previous.RayDatabasePath, writer, linkedCancellation.Token);
                    }

                    using var cancellationScope = OptilandWorkbench.Core.Services.ComputationCancellation.Push(linkedCancellation.Token);
                    INonSequentialTraceSink sink = priorBranches > 0
                        ? new OffsetTraceSink(writer, priorBranches)
                        : writer;
                    result = new NonSequentialDocumentTracer().Trace(
                        document,
                        materials,
                        new NonSequentialDocumentTraceRequest(
                            request.AnalysisRays ? NonSequentialTracePurpose.Analysis : NonSequentialTracePurpose.Layout,
                            request.SourceObjectId,
                            SplitFresnelRays: request.SplitFresnelRays,
                            OutputMode: OptilandWorkbench.Core.NonSequential.NonSequentialTraceOutputMode.RayDatabase,
                            MaximumRetainedBranches: request.MaximumRetainedBranches,
                            PathFilterExpression: request.PathFilterExpression,
                            SplittingMode: (CoreSplittingMode)(int)splitting,
                            RandomSeed: randomSeed,
                            MaximumSegmentsPerRay: request.MaximumSegmentsPerRay,
                            MaximumActiveBranches: request.MaximumActiveBranches,
                            MinimumRelativeIntensity: request.MinimumRelativeIntensity,
                            RayCountOverride: request.RayCountOverride,
                            SourceObjectIds: sourceIds),
                        sink);
                    linkedCancellation.Token.ThrowIfCancellationRequested();
                    writer.Complete();

                    timer.Stop();
                    var priorSource = previous?.SourcePowerWatts ?? 0;
                    var priorDetector = previous?.DetectorPowerWatts ?? 0;
                    var priorAbsorbed = previous?.AbsorbedPowerWatts ?? 0;
                    var priorEscaped = previous?.EscapedPowerWatts ?? 0;
                    var priorTruncated = previous?.TruncatedPowerWatts ?? 0;
                    var priorSegments = previous?.SegmentCount ?? 0;
                    var energy = result.EnergyBalance;
                    var warnings = new List<string>();
                    if (energy.TruncatedPowerWatts > Math.Max(1e-15, energy.SourcePowerWatts * 1e-9))
                    {
                        warnings.Add($"有 {energy.TruncatedPowerWatts:G6} W 因能量、段数或分支上限被截断。");
                    }
                    var state = warnings.Count == 0
                        ? NonSequentialTraceSessionState.Completed
                        : NonSequentialTraceSessionState.Warning;
                    session = new NonSequentialTraceSessionDto(
                        request.Command == NonSequentialTraceCommand.TraceOnly && previous is not null ? previous.Id : Guid.NewGuid(),
                        state,
                        sceneHash,
                        revision,
                        previous?.CreatedUtc ?? DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        passCount,
                        baseRandomSeed,
                        splitting,
                        sourceIds,
                        priorBranches + result.TotalBranchCount,
                        priorSegments + result.TotalSegmentCount,
                        priorSource + energy.SourcePowerWatts,
                        priorDetector + energy.DetectorPowerWatts,
                        priorAbsorbed + energy.AbsorbedPowerWatts,
                        priorEscaped + energy.EscapedPowerWatts,
                        priorTruncated + energy.TruncatedPowerWatts,
                        0,
                        0,
                        (previous?.Elapsed ?? TimeSpan.Zero) + timer.Elapsed,
                        targetPath,
                        ownsDatabase,
                        false,
                        request.PathFilterExpression,
                        warnings,
                        traceConfigurationFingerprint);

                    response = new NonSequentialTraceRunResultDto(
                        result.TotalBranchCount,
                        result.MatchedBranchCount,
                        result.Branches.Count,
                        result.TotalSegmentCount,
                        energy.SourcePowerWatts,
                        energy.DetectorPowerWatts,
                        energy.AbsorbedPowerWatts,
                        energy.EscapedPowerWatts,
                        energy.TruncatedPowerWatts,
                        targetPath,
                        0,
                        session.Id,
                        state,
                        timer.Elapsed,
                        passCount,
                        false,
                        warnings);
                    stream.Flush(true);
                }
                PublishFile(temporaryPath, targetPath);
                response = response with { RayDatabaseBytes = new FileInfo(targetPath).Length };
                _session.Publish(session, ownsDatabase);
                return response;
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }, linkedCancellation.Token).ConfigureAwait(false);
    }

    public NonSequentialRayDatabaseDto OpenRayDatabase(string path, string? pathFilterExpression = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        NonSequentialDocument document;
        lock (Gate) document = Runtime.CurrentNonSequentialDocument.Clone();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new NonSequentialRayDatabaseReader(stream);
        var filter = NonSequentialPathFilter.Parse(pathFilterExpression);
        var paths = NonSequentialPathAnalyzer.Analyze(document, reader.ReadBranches(filter))
            .Select(item => new NonSequentialPathSummaryDto(
                item.Path, item.FilterExpression, item.RayCount, item.TotalPowerWatts,
                item.PowerFraction, item.MinimumOpticalPathLength,
                item.AverageOpticalPathLength, item.MaximumOpticalPathLength,
                item.TerminationReason.ToString()))
            .ToArray();
        _session.Set(path, pathFilterExpression);
        return new NonSequentialRayDatabaseDto(
            Path.GetFullPath(path), reader.Header.SceneHash, reader.Header.SourceRevision,
            reader.Header.CreatedUtc, reader.BranchCount, reader.IsStale(document),
            reader.Header.PathFilterExpression, paths);
    }

    public NonSequentialRayDatabasePageDto GetRayDatabasePage(
        string? path = null,
        int pageIndex = 0,
        int pageSize = 100,
        string? pathFilterExpression = null)
    {
        if (pageIndex < 0) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (pageSize <= 0 || pageSize > 1_000) throw new ArgumentOutOfRangeException(nameof(pageSize));
        path = string.IsNullOrWhiteSpace(path) ? _session.SelectedPath : Path.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("当前没有可用的光线数据库。");
        NonSequentialDocument document;
        lock (Gate) document = Runtime.CurrentNonSequentialDocument.Clone();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new NonSequentialRayDatabaseReader(stream);
        var filter = NonSequentialPathFilter.Parse(pathFilterExpression ?? _session.SelectedFilter);
        var objectMap = reader.Header.Objects.ToDictionary(item => item.Id);
        var branches = reader.ReadBranches(filter)
            .Skip(checked(pageIndex * pageSize))
            .Take(pageSize)
            .Select(branch => MapBranch(branch, objectMap))
            .ToArray();
        return new NonSequentialRayDatabasePageDto(
            path, reader.BranchCount, pageIndex, pageSize, reader.IsStale(document), branches);
    }

    public NonSequentialDetectorViewDto GetDetectorView(NonSequentialDetectorViewRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        NonSequentialDocument document;
        long revision;
        lock (Gate)
        {
            document = Runtime.CurrentNonSequentialDocument.Clone();
            revision = Workspace.Revision;
        }
        var path = string.IsNullOrWhiteSpace(request.RayDatabasePath)
            ? _session.SelectedPath
            : Path.GetFullPath(request.RayDatabasePath);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException("没有可用的非序列追迹结果。请先运行追迹控制。");
        }
        var detectorObject = document.Objects.SingleOrDefault(item => item.Enabled
            && item.Id == request.DetectorId
            && item.Kind == CoreObjectKind.DetectorRectangle)
            ?? throw new InvalidOperationException("指定的矩形探测器不存在或已禁用。");
        var detector = (CoreDetectorRectangleParameters)detectorObject.Parameters;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new NonSequentialRayDatabaseReader(stream);
        var filter = NonSequentialPathFilter.Parse(request.PathFilterExpression ?? _session.SelectedFilter);
        var frame = NonSequentialDetectorReconstruction.Reconstruct(document, reader.ReadBranches(filter))
            .Single(item => item.DetectorId == request.DetectorId);
        var values = BuildDetectorValues(frame, detector, request);
        var xMin = request.Space == NonSequentialDetectorSpace.Position ? -detector.WidthMillimeters / 2 : -90;
        var xMax = request.Space == NonSequentialDetectorSpace.Position ? detector.WidthMillimeters / 2 : 90;
        var yMin = request.Space == NonSequentialDetectorSpace.Position ? -detector.HeightMillimeters / 2 : -90;
        var yMax = request.Space == NonSequentialDetectorSpace.Position ? detector.HeightMillimeters / 2 : 90;
        var statistics = Statistics(values, frame, request, xMin, xMax, yMin, yMax);
        var xProfile = Enumerable.Range(0, frame.PixelsX)
            .Select(x => Enumerable.Range(0, frame.PixelsY).Sum(y => values[y * frame.PixelsX + x])).ToArray();
        var yProfile = Enumerable.Range(0, frame.PixelsY)
            .Select(y => Enumerable.Range(0, frame.PixelsX).Sum(x => values[y * frame.PixelsX + x])).ToArray();
        var valueUnit = request.DataType switch
        {
            NonSequentialDetectorDataType.PixelPower => "W",
            NonSequentialDetectorDataType.IncoherentIrradiance => "W/mm²",
            NonSequentialDetectorDataType.HitCount => "count",
            _ => "W/sr"
        };
        var selectedSession = _session.Snapshot(document, revision);
        var selectedSessionIsStale = selectedSession is not null
            && Path.GetFullPath(path).Equals(selectedSession.RayDatabasePath, StringComparison.OrdinalIgnoreCase)
            && selectedSession.IsStale;
        return new NonSequentialDetectorViewDto(
            frame.DetectorId, frame.DetectorName, frame.PixelsX, frame.PixelsY,
            xMin, xMax, yMin, yMax,
            request.Space == NonSequentialDetectorSpace.Position ? "mm" : "deg",
            request.Space == NonSequentialDetectorSpace.Position ? "mm" : "deg",
            valueUnit, values, xProfile, yProfile, statistics,
            reader.IsStale(document) || selectedSessionIsStale,
            Path.GetFullPath(path));
    }

    private static string CreateTraceConfigurationFingerprint(
        NonSequentialTraceRunRequestDto request,
        NonSequentialDocument document,
        IReadOnlyList<Guid> sourceIds,
        OptilandWorkbench.Application.Contracts.NonSequentialSplittingMode splitting,
        int randomSeed)
    {
        var text = string.Join("|",
            request.AnalysisRays,
            request.RayCountOverride?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "object",
            randomSeed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            splitting,
            (request.MaximumSegmentsPerRay ?? document.TraceSettings.MaximumSegmentsPerRay)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            (request.MaximumActiveBranches ?? document.TraceSettings.MaximumActiveBranches)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            (request.MinimumRelativeIntensity ?? document.TraceSettings.MinimumRelativeIntensity)
                .ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            request.PathFilterExpression ?? string.Empty,
            string.Join(",", sourceIds.OrderBy(id => id).Select(id => id.ToString("N"))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static IReadOnlyList<Guid> ResolveSourceIds(
        NonSequentialDocument document,
        NonSequentialTraceRunRequestDto request)
    {
        var available = document.Objects.Where(item => item.Enabled && item.Parameters is CoreSourceParameters)
            .Select(item => item.Id).ToArray();
        if (request.SourceObjectIds is { Count: > 0 })
        {
            var unknown = request.SourceObjectIds.Where(id => !available.Contains(id)).ToArray();
            if (unknown.Length > 0) throw new InvalidOperationException("追迹请求包含不存在或未启用的光源对象。");
            return request.SourceObjectIds.Distinct().ToArray();
        }
        if (request.SourceObjectId is Guid sourceId)
        {
            if (!available.Contains(sourceId)) throw new InvalidOperationException("指定光源不存在或未启用。");
            return new[] { sourceId };
        }
        return available;
    }

    private static void CopyExistingBranches(
        string path,
        NonSequentialRayDatabaseWriter writer,
        CancellationToken cancellationToken)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new NonSequentialRayDatabaseReader(input);
        foreach (var branch in reader.ReadBranches())
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.OnBranch(branch);
        }
    }

    private static string CreateManagedDatabasePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OptilandWorkbench", "NonSequential");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"trace-{Guid.NewGuid():N}.starrdb");
    }

    private static void PublishFile(string temporaryPath, string targetPath)
    {
        File.Move(temporaryPath, targetPath, overwrite: true);
    }

    private static NonSequentialRayBranchDto MapBranch(
        NonSequentialRayBranch branch,
        IReadOnlyDictionary<Guid, NonSequentialRayDatabaseObject> objects)
    {
        return new NonSequentialRayBranchDto(
            branch.Id, branch.ParentId, branch.Level, branch.SourceObjectId,
            branch.TerminationReason.ToString(), branch.FinalIntensity, branch.WavelengthNanometers,
            branch.Segments.Select(segment =>
            {
                var item = segment.ObjectId is Guid id && objects.TryGetValue(id, out var value) ? value : null;
                return new NonSequentialRaySegmentDto(
                    branch.Id, segment.ObjectId, item?.ObjectNumber ?? 0, item?.Name ?? "-",
                    segment.FaceNumber, segment.InteractionKind?.ToString() ?? "-",
                    segment.End.X, segment.End.Y, segment.End.Z,
                    segment.OutgoingDirection.X, segment.OutgoingDirection.Y, segment.OutgoingDirection.Z,
                    segment.Intensity, segment.WavelengthNanometers,
                    segment.CumulativePathLength, segment.CumulativeOpticalPathLength);
            }).ToArray());
    }

    private static double[] BuildDetectorValues(
        NonSequentialDetectorFrame frame,
        CoreDetectorRectangleParameters detector,
        NonSequentialDetectorViewRequestDto request)
    {
        var length = frame.PixelsX * frame.PixelsY;
        var selectedWavelengths = request.WavelengthNumber > 0
            ? new[] { request.WavelengthNumber }
            : frame.PowerByWavelength.Keys.ToArray();
        var values = new double[length];
        foreach (var wavelength in selectedWavelengths)
        {
            if (request.DataType == NonSequentialDetectorDataType.HitCount)
            {
                var hitMap = request.Space == NonSequentialDetectorSpace.Angle
                    ? frame.AngularHitCountByWavelength
                    : frame.HitCountByWavelength;
                if (hitMap is null
                    || !hitMap.TryGetValue(wavelength, out var hits)) continue;
                for (var index = 0; index < length; index++) values[index] += hits[index];
                continue;
            }
            var source = request.Space == NonSequentialDetectorSpace.Angle
                ? frame.AngularPowerByWavelength
                : frame.PowerByWavelength;
            if (source is null || !source.TryGetValue(wavelength, out var pixels)) continue;
            for (var index = 0; index < length; index++) values[index] += pixels[index];
        }
        if (request.DataType == NonSequentialDetectorDataType.IncoherentIrradiance)
        {
            var area = detector.WidthMillimeters / frame.PixelsX * detector.HeightMillimeters / frame.PixelsY;
            for (var index = 0; index < length; index++) values[index] /= area;
        }
        else if (request.DataType == NonSequentialDetectorDataType.RadiantIntensity)
        {
            var solidAngle = Math.PI / frame.PixelsX * Math.PI / frame.PixelsY;
            for (var index = 0; index < length; index++) values[index] /= solidAngle;
        }
        return values;
    }

    private static NonSequentialDetectorStatisticsDto Statistics(
        IReadOnlyList<double> values,
        NonSequentialDetectorFrame frame,
        NonSequentialDetectorViewRequestDto request,
        double xMin,
        double xMax,
        double yMin,
        double yMax)
    {
        var total = values.Sum();
        var centroidX = 0.0;
        var centroidY = 0.0;
        if (total > 0)
        {
            for (var y = 0; y < frame.PixelsY; y++)
                for (var x = 0; x < frame.PixelsX; x++)
                {
                    var weight = values[y * frame.PixelsX + x];
                    centroidX += (xMin + (x + 0.5) * (xMax - xMin) / frame.PixelsX) * weight;
                    centroidY += (yMin + (y + 0.5) * (yMax - yMin) / frame.PixelsY) * weight;
                }
            centroidX /= total;
            centroidY /= total;
        }
        var varianceX = 0.0;
        var varianceY = 0.0;
        if (total > 0)
        {
            for (var y = 0; y < frame.PixelsY; y++)
                for (var x = 0; x < frame.PixelsX; x++)
                {
                    var weight = values[y * frame.PixelsX + x];
                    var px = xMin + (x + 0.5) * (xMax - xMin) / frame.PixelsX;
                    var py = yMin + (y + 0.5) * (yMax - yMin) / frame.PixelsY;
                    varianceX += (px - centroidX) * (px - centroidX) * weight;
                    varianceY += (py - centroidY) * (py - centroidY) * weight;
                }
        }
        var maximum = values.Count == 0 ? 0 : values.Max();
        var minimum = values.Count == 0 ? 0 : values.Min();
        var uniformity = maximum + minimum <= 0 ? 0 : 1 - (maximum - minimum) / (maximum + minimum);
        var selectedWavelengths = request.WavelengthNumber > 0
            ? new[] { request.WavelengthNumber }
            : frame.PowerByWavelength.Keys;
        var totalPower = selectedWavelengths.Sum(wavelength =>
            frame.PowerByWavelength.TryGetValue(wavelength, out var pixels) ? pixels.Sum() : 0);
        var hitMap = request.Space == NonSequentialDetectorSpace.Angle
            ? frame.AngularHitCountByWavelength
            : frame.HitCountByWavelength;
        var hits = selectedWavelengths.Sum(wavelength =>
            hitMap is not null && hitMap.TryGetValue(wavelength, out var items) ? items.Sum() : 0);
        return new NonSequentialDetectorStatisticsDto(
            totalPower, hits, maximum, centroidX, centroidY,
            total <= 0 ? 0 : Math.Sqrt(varianceX / total),
            total <= 0 ? 0 : Math.Sqrt(varianceY / total),
            uniformity);
    }

    private sealed class OffsetTraceSink : INonSequentialTraceSink
    {
        private readonly INonSequentialTraceSink _inner;
        private readonly long _offset;

        public OffsetTraceSink(INonSequentialTraceSink inner, long offset)
        {
            _inner = inner;
            _offset = offset;
        }

        public void OnBranch(NonSequentialRayBranch branch)
        {
            var id = checked(branch.Id + _offset);
            _inner.OnBranch(branch with
            {
                Id = id,
                ParentId = branch.ParentId is long parentId ? checked(parentId + _offset) : null,
                Segments = branch.Segments.Select(segment => segment with { BranchId = id }).ToArray()
            });
        }
    }
}
