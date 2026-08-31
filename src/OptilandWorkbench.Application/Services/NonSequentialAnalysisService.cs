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

internal sealed class NonSequentialAnalysisService : WorkbenchServiceBase, INonSequentialAnalysisService, IDisposable
{
    private const int MaximumCachedFilterIndices = 1_000_000;
    private readonly NonSequentialAnalysisSession _analysisSession;
    private readonly NonSequentialLayoutSession _layoutSession;
    private readonly SemaphoreSlim _analysisTraceGate = new(1, 1);
    private readonly SemaphoreSlim _layoutTraceGate = new(1, 1);
    private readonly object _cacheGate = new();
    private long _cacheGeneration;
    private FilterIndexCacheEntry? _filterIndexCache;
    private DetectorFrameCacheEntry? _detectorFrameCache;
    private bool _disposed;

    public NonSequentialAnalysisService(
        WorkspaceCoordinator workspace,
        NonSequentialAnalysisSession analysisSession,
        NonSequentialLayoutSession layoutSession) : base(workspace)
    {
        _analysisSession = analysisSession ?? throw new ArgumentNullException(nameof(analysisSession));
        _layoutSession = layoutSession ?? throw new ArgumentNullException(nameof(layoutSession));
        _analysisSession.Changed += OnAnalysisSessionChanged;
        _layoutSession.Changed += OnLayoutSessionChanged;
    }

    public event EventHandler<NonSequentialTraceSessionDto?>? SessionChanged;

    public event EventHandler<NonSequentialTraceSessionDto?>? LayoutSessionChanged;

    private void OnAnalysisSessionChanged(object? sender, EventArgs args)
    {
        lock (_cacheGate)
        {
            _cacheGeneration++;
            _filterIndexCache = null;
            _detectorFrameCache = null;
        }
        NotifySessionObservers(SessionChanged, GetCurrentSession(), nameof(SessionChanged));
    }

    private void OnLayoutSessionChanged(object? sender, EventArgs args) =>
        NotifySessionObservers(LayoutSessionChanged, GetCurrentLayoutSession(), nameof(LayoutSessionChanged));

    public NonSequentialTraceSessionDto? GetCurrentSession()
    {
        lock (Gate) return _analysisSession.Snapshot(Runtime.CurrentNonSequentialDocument, Workspace.Revision);
    }

    public NonSequentialTraceSessionDto? GetCurrentLayoutSession()
    {
        lock (Gate) return _layoutSession.Snapshot(Runtime.CurrentNonSequentialDocument, Workspace.Revision);
    }

    public async Task<NonSequentialTraceSessionDto> PrepareLayoutSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var current = GetCurrentLayoutSession();
        if (current is { IsStale: false }
            && current.State is NonSequentialTraceSessionState.Completed or NonSequentialTraceSessionState.Warning)
        {
            return current;
        }

        await TraceSerializedAsync(
            CreateLayoutTraceRequest(),
            _layoutSession,
            _layoutTraceGate,
            cancellationToken).ConfigureAwait(false);
        return GetCurrentLayoutSession()
            ?? throw new InvalidOperationException("非序列布局追迹完成后未建立结果会话。");
    }

    public async Task<NonSequentialTraceSessionDto> RefreshLayoutSessionAsync(
        CancellationToken cancellationToken = default)
    {
        await TraceSerializedAsync(
            CreateLayoutTraceRequest(),
            _layoutSession,
            _layoutTraceGate,
            cancellationToken).ConfigureAwait(false);
        return GetCurrentLayoutSession()
            ?? throw new InvalidOperationException("非序列布局追迹完成后未建立结果会话。");
    }

    [Obsolete("Compatibility alias. Layout tracing must be an explicit user action; use PrepareLayoutSessionAsync.")]
    public Task<NonSequentialTraceSessionDto> EnsureLayoutSessionAsync(
        CancellationToken cancellationToken = default) =>
        PrepareLayoutSessionAsync(cancellationToken);

    public async Task ClearDetectorsAsync(CancellationToken cancellationToken = default)
    {
        await _analysisTraceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _analysisSession.Clear();
        }
        finally
        {
            _analysisTraceGate.Release();
        }
    }

    public async Task<NonSequentialTraceRunResultDto> TraceAsync(
        NonSequentialTraceRunRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return await TraceSerializedAsync(
            request,
            _analysisSession,
            _analysisTraceGate,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<NonSequentialTraceRunResultDto> TraceSerializedAsync(
        NonSequentialTraceRunRequestDto request,
        NonSequentialResultSession targetSession,
        SemaphoreSlim traceGate,
        CancellationToken cancellationToken)
    {
        await traceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TraceCoreAsync(request, targetSession, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            traceGate.Release();
        }
    }

    private static NonSequentialTraceRunRequestDto CreateLayoutTraceRequest() => new(
        OutputMode: OptilandWorkbench.Application.Contracts.NonSequentialTraceOutputMode.RayDatabase,
        AnalysisRays: false,
        Command: NonSequentialTraceCommand.ClearAndTrace);

    private async Task<NonSequentialTraceRunResultDto> TraceCoreAsync(
        NonSequentialTraceRunRequestDto request,
        NonSequentialResultSession targetSession,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Command == NonSequentialTraceCommand.ClearOnly)
        {
            targetSession.Clear();
            return new NonSequentialTraceRunResultDto(
                0, 0, 0, 0, 0, 0, 0, 0, 0, null, 0,
                SessionState: NonSequentialTraceSessionState.Empty,
                TracePassCount: 0);
        }

        NonSequentialDocument document;
        OptilandWorkbench.Core.Materials.MaterialRegistry materials;
        long revision;
        long documentGeneration;
        long publicationGeneration;
        NonSequentialTraceSessionDto? previous;
        CancellationTokenSource linkedCancellation;
        lock (Gate)
        {
            document = Runtime.CurrentNonSequentialDocument.Clone();
            materials = Runtime.CurrentOptic.Materials.CreateSnapshot();
            revision = Workspace.Revision;
            documentGeneration = Workspace.DocumentGeneration;
            publicationGeneration = targetSession.PublicationGeneration;
            previous = targetSession.Snapshot(Runtime.CurrentNonSequentialDocument, Workspace.Revision);
            linkedCancellation = Workspace.LinkDocumentToken(cancellationToken);
        }
        using var linkedCancellationScope = linkedCancellation;
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
                long branchIdOffset = 0;
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
                        branchIdOffset = CopyExistingBranches(
                            previous.RayDatabasePath,
                            writer,
                            linkedCancellation.Token);
                    }

                    using var cancellationScope = OptilandWorkbench.Core.Services.ComputationCancellation.Push(linkedCancellation.Token);
                    INonSequentialTraceSink sink = branchIdOffset > 0
                        ? new OffsetTraceSink(writer, branchIdOffset)
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
                        writer.BranchCount,
                        writer.SegmentCount,
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
                var published = false;
                lock (Gate)
                {
                    linkedCancellation.Token.ThrowIfCancellationRequested();
                    if (Workspace.DocumentGeneration != documentGeneration)
                    {
                        throw new OperationCanceledException("文档已切换，旧非序列追迹结果不会发布。", linkedCancellation.Token);
                    }
                    published = targetSession.TryPublish(
                        session,
                        ownsDatabase,
                        publicationGeneration,
                        () =>
                        {
                            linkedCancellation.Token.ThrowIfCancellationRequested();
                            PublishFile(temporaryPath, targetPath);
                        },
                        notifyChanged: false);
                }
                if (!published)
                {
                    throw new OperationCanceledException("非序列结果会话已更新，旧追迹结果不会发布。", linkedCancellation.Token);
                }
                targetSession.NotifyChanged();
                response = response with { RayDatabaseBytes = new FileInfo(targetPath).Length };
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
        var result = InspectRayDatabase(path, pathFilterExpression);
        SelectRayDatabase(path, pathFilterExpression);
        return result;
    }

    public NonSequentialRayDatabaseDto InspectRayDatabase(
        string path,
        string? pathFilterExpression = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        NonSequentialDocument document;
        lock (Gate) document = Runtime.CurrentNonSequentialDocument.Clone();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new NonSequentialRayDatabaseReader(stream);
        long cacheGeneration;
        lock (_cacheGate) cacheGeneration = _cacheGeneration;
        var filter = NonSequentialPathFilter.Parse(pathFilterExpression);
        List<long>? matchedIndices = new();
        IEnumerable<NonSequentialRayBranch> IndexedBranches()
        {
            foreach (var (index, branch) in reader.ReadBranchesWithIndices(filter))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (matchedIndices is { } indices)
                {
                    if (indices.Count < MaximumCachedFilterIndices)
                    {
                        indices.Add(index);
                    }
                    else
                    {
                        matchedIndices = null;
                    }
                }
                yield return branch;
            }
        }
        var paths = NonSequentialPathAnalyzer.Analyze(reader.CreateHeaderDocument(), IndexedBranches())
            .Select(item => new NonSequentialPathSummaryDto(
                item.Path, item.FilterExpression, item.RayCount, item.TotalPowerWatts,
                item.PowerFraction, item.MinimumOpticalPathLength,
                item.AverageOpticalPathLength, item.MaximumOpticalPathLength,
                item.TerminationReason.ToString()))
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var cacheKey = DatabaseCacheKey.Create(
            path,
            pathFilterExpression,
            NonSequentialSceneHasher.Compute(document),
            stream.Length,
            reader.Header,
            reader.BranchCount);
        lock (_cacheGate)
        {
            if (_cacheGeneration == cacheGeneration)
            {
                _filterIndexCache = matchedIndices is null
                    ? null
                    : new FilterIndexCacheEntry(cacheKey, matchedIndices.ToArray());
            }
        }
        return new NonSequentialRayDatabaseDto(
            Path.GetFullPath(path), reader.Header.SceneHash, reader.Header.SourceRevision,
            reader.Header.CreatedUtc, reader.BranchCount, reader.IsStale(document),
            reader.Header.PathFilterExpression, paths);
    }

    public void SelectRayDatabase(string path, string? pathFilterExpression = null) =>
        _analysisSession.Set(path, pathFilterExpression);

    public NonSequentialRayDatabasePageDto GetRayDatabasePage(
        string? path = null,
        int pageIndex = 0,
        int pageSize = 100,
        string? pathFilterExpression = null,
        CancellationToken cancellationToken = default)
    {
        if (pageIndex < 0) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (pageSize <= 0 || pageSize > 1_000) throw new ArgumentOutOfRangeException(nameof(pageSize));
        path = string.IsNullOrWhiteSpace(path) ? _analysisSession.SelectedPath : Path.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("当前没有可用的光线数据库。");
        NonSequentialDocument document;
        lock (Gate) document = Runtime.CurrentNonSequentialDocument.Clone();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new NonSequentialRayDatabaseReader(stream);
        cancellationToken.ThrowIfCancellationRequested();
        var filter = NonSequentialPathFilter.Parse(pathFilterExpression ?? _analysisSession.SelectedFilter);
        var objectMap = reader.Header.Objects.ToDictionary(item => item.Id);
        var offset = checked((long)pageIndex * pageSize);
        var selectedFilter = pathFilterExpression ?? _analysisSession.SelectedFilter;
        IReadOnlyList<NonSequentialRayBranch> storedBranches;
        if (string.IsNullOrWhiteSpace(selectedFilter))
        {
            storedBranches = reader.ReadRange(offset, pageSize);
        }
        else
        {
            var cacheKey = DatabaseCacheKey.Create(
                path,
                selectedFilter,
                NonSequentialSceneHasher.Compute(document),
                stream.Length,
                reader.Header,
                reader.BranchCount);
            long[]? matchedIndices;
            lock (_cacheGate)
            {
                matchedIndices = _filterIndexCache is { } cached && cached.Key == cacheKey
                    ? cached.Indices
                    : null;
            }
            if (matchedIndices is not null)
            {
                storedBranches = offset >= matchedIndices.LongLength
                    ? Array.Empty<NonSequentialRayBranch>()
                    : reader.ReadIndices(matchedIndices
                        .Skip(checked((int)offset))
                        .Take(pageSize)
                        .ToArray());
            }
            else
            {
                storedBranches = offset >= reader.BranchCount
                    ? Array.Empty<NonSequentialRayBranch>()
                    : SkipLong(WithCancellation(reader.ReadBranches(filter), cancellationToken), offset)
                        .Take(pageSize)
                        .ToArray();
            }
        }
        var branches = storedBranches
            .Select(branch => MapBranch(branch, objectMap))
            .ToArray();
        return new NonSequentialRayDatabasePageDto(
            path, reader.BranchCount, pageIndex, pageSize, reader.IsStale(document), branches);

        static IEnumerable<NonSequentialRayBranch> WithCancellation(
            IEnumerable<NonSequentialRayBranch> branches,
            CancellationToken cancellationToken)
        {
            foreach (var branch in branches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return branch;
            }
        }

        static IEnumerable<NonSequentialRayBranch> SkipLong(
            IEnumerable<NonSequentialRayBranch> branches,
            long count)
        {
            using var enumerator = branches.GetEnumerator();
            while (count > 0 && enumerator.MoveNext())
            {
                count--;
            }

            while (enumerator.MoveNext())
            {
                yield return enumerator.Current;
            }
        }
    }

    public NonSequentialDetectorViewDto GetDetectorView(
        NonSequentialDetectorViewRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Space == NonSequentialDetectorSpace.Position
            && request.DataType == NonSequentialDetectorDataType.RadiantIntensity)
        {
            throw new ArgumentException("位置空间不能显示辐射强度；请选择像素功率、辐照度或命中数。", nameof(request));
        }
        if (request.Space == NonSequentialDetectorSpace.Angle
            && request.DataType == NonSequentialDetectorDataType.IncoherentIrradiance)
        {
            throw new ArgumentException("角度空间不能显示辐照度；请选择像素功率、辐射强度或命中数。", nameof(request));
        }
        NonSequentialDocument document;
        long revision;
        lock (Gate)
        {
            document = Runtime.CurrentNonSequentialDocument.Clone();
            revision = Workspace.Revision;
        }
        var path = string.IsNullOrWhiteSpace(request.RayDatabasePath)
            ? _analysisSession.SelectedPath
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
        cancellationToken.ThrowIfCancellationRequested();
        long cacheGeneration;
        lock (_cacheGate) cacheGeneration = _cacheGeneration;
        var filter = NonSequentialPathFilter.Parse(request.PathFilterExpression ?? _analysisSession.SelectedFilter);
        var detectorCacheKey = DatabaseCacheKey.Create(
            path,
            request.PathFilterExpression ?? _analysisSession.SelectedFilter,
            NonSequentialSceneHasher.Compute(document),
            stream.Length,
            reader.Header,
            reader.BranchCount);
        IReadOnlyList<NonSequentialDetectorFrame>? cachedFrames;
        lock (_cacheGate)
        {
            cachedFrames = _detectorFrameCache is { } cached && cached.Key == detectorCacheKey
                ? cached.Frames
                : null;
        }
        var frames = cachedFrames ?? NonSequentialDetectorReconstruction.Reconstruct(
            document,
            reader.ReadBranches(filter),
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (cachedFrames is null)
        {
            lock (_cacheGate)
            {
                if (_cacheGeneration == cacheGeneration)
                {
                    _detectorFrameCache = new DetectorFrameCacheEntry(detectorCacheKey, frames);
                }
            }
        }
        var frame = frames
            .Single(item => item.DetectorId == request.DetectorId);
        var values = BuildDetectorValues(frame, detector, request, cancellationToken);
        var xMin = request.Space == NonSequentialDetectorSpace.Position ? -detector.WidthMillimeters / 2 : -90;
        var xMax = request.Space == NonSequentialDetectorSpace.Position ? detector.WidthMillimeters / 2 : 90;
        var yMin = request.Space == NonSequentialDetectorSpace.Position ? -detector.HeightMillimeters / 2 : -90;
        var yMax = request.Space == NonSequentialDetectorSpace.Position ? detector.HeightMillimeters / 2 : 90;
        var statistics = Statistics(values, frame, request, xMin, xMax, yMin, yMax, cancellationToken);
        var xProfile = new double[frame.PixelsX];
        var yProfile = new double[frame.PixelsY];
        for (var y = 0; y < frame.PixelsY; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < frame.PixelsX; x++)
            {
                var value = values[y * frame.PixelsX + x];
                xProfile[x] += value;
                yProfile[y] += value;
            }
        }
        var valueUnit = request.DataType switch
        {
            NonSequentialDetectorDataType.PixelPower => "W",
            NonSequentialDetectorDataType.IncoherentIrradiance => "W/mm²",
            NonSequentialDetectorDataType.HitCount => "count",
            _ => "W/sr"
        };
        var selectedSession = _analysisSession.Snapshot(document, revision);
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

    private static long CopyExistingBranches(
        string path,
        NonSequentialRayDatabaseWriter writer,
        CancellationToken cancellationToken)
    {
        long maximumBranchId = 0;
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new NonSequentialRayDatabaseReader(input);
        foreach (var branch in reader.ReadBranches())
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.OnBranch(branch);
            maximumBranchId = Math.Max(maximumBranchId, branch.Id);
        }
        return maximumBranchId;
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _analysisSession.Changed -= OnAnalysisSessionChanged;
        _layoutSession.Changed -= OnLayoutSessionChanged;
        _analysisTraceGate.Dispose();
        _layoutTraceGate.Dispose();
    }

    private void NotifySessionObservers(
        EventHandler<NonSequentialTraceSessionDto?>? observers,
        NonSequentialTraceSessionDto? session,
        string eventName)
    {
        if (observers is null)
        {
            return;
        }

        foreach (EventHandler<NonSequentialTraceSessionDto?> observer in observers.GetInvocationList())
        {
            try
            {
                observer(this, session);
            }
            catch (Exception exception)
            {
                Trace.TraceError($"Non-sequential {eventName} observer failed: {exception}");
            }
        }
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
                    segment.BranchId, segment.ObjectId, item?.ObjectNumber ?? 0, item?.Name ?? "-",
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
        NonSequentialDetectorViewRequestDto request,
        CancellationToken cancellationToken)
    {
        var length = frame.PixelsX * frame.PixelsY;
        var selectedWavelengths = request.WavelengthNumber > 0
            ? new[] { request.WavelengthNumber }
            : frame.PowerByWavelength.Keys.ToArray();
        var values = new double[length];
        foreach (var wavelength in selectedWavelengths)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            for (var y = 0; y < frame.PixelsY; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < frame.PixelsX; x++)
                {
                    var solidAngle = AngularPixelSolidAngle(x, y, frame.PixelsX, frame.PixelsY);
                    values[y * frame.PixelsX + x] /= solidAngle;
                }
            }
        }
        return values;
    }

    private static double AngularPixelSolidAngle(int x, int y, int pixelsX, int pixelsY)
    {
        var alpha0 = -Math.PI / 2 + x * Math.PI / pixelsX;
        var alpha1 = -Math.PI / 2 + (x + 1) * Math.PI / pixelsX;
        var beta0 = -Math.PI / 2 + y * Math.PI / pixelsY;
        var beta1 = -Math.PI / 2 + (y + 1) * Math.PI / pixelsY;
        var u0 = Math.Tan(alpha0);
        var u1 = Math.Tan(alpha1);
        var v0 = Math.Tan(beta0);
        var v1 = Math.Tan(beta1);
        var value = Corner(u1, v1) - Corner(u0, v1) - Corner(u1, v0) + Corner(u0, v0);
        return Math.Max(1e-15, Math.Abs(value));

        static double Corner(double u, double v) =>
            Math.Atan2(u * v, Math.Sqrt(1 + u * u + v * v));
    }

    private static NonSequentialDetectorStatisticsDto Statistics(
        IReadOnlyList<double> values,
        NonSequentialDetectorFrame frame,
        NonSequentialDetectorViewRequestDto request,
        double xMin,
        double xMax,
        double yMin,
        double yMax,
        CancellationToken cancellationToken)
    {
        var total = values.Sum();
        var centroidX = 0.0;
        var centroidY = 0.0;
        if (total > 0)
        {
            for (var y = 0; y < frame.PixelsY; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < frame.PixelsX; x++)
                {
                    var weight = values[y * frame.PixelsX + x];
                    centroidX += (xMin + (x + 0.5) * (xMax - xMin) / frame.PixelsX) * weight;
                    centroidY += (yMin + (y + 0.5) * (yMax - yMin) / frame.PixelsY) * weight;
                }
            }
            centroidX /= total;
            centroidY /= total;
        }
        var varianceX = 0.0;
        var varianceY = 0.0;
        if (total > 0)
        {
            for (var y = 0; y < frame.PixelsY; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < frame.PixelsX; x++)
                {
                    var weight = values[y * frame.PixelsX + x];
                    var px = xMin + (x + 0.5) * (xMax - xMin) / frame.PixelsX;
                    var py = yMin + (y + 0.5) * (yMax - yMin) / frame.PixelsY;
                    varianceX += (px - centroidX) * (px - centroidX) * weight;
                    varianceY += (py - centroidY) * (py - centroidY) * weight;
                }
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
                Segments = branch.Segments.Select(segment => segment with
                {
                    BranchId = checked(segment.BranchId + _offset)
                }).ToArray()
            });
        }
    }

    private sealed record FilterIndexCacheEntry(DatabaseCacheKey Key, long[] Indices);

    private sealed record DetectorFrameCacheEntry(
        DatabaseCacheKey Key,
        IReadOnlyList<NonSequentialDetectorFrame> Frames);

    private readonly record struct DatabaseCacheKey(
        string Path,
        long Length,
        string DatabaseSceneHash,
        long DatabaseCreatedUtcTicks,
        long BranchCount,
        long SourceRevision,
        string Filter,
        string SceneHash)
    {
        public static DatabaseCacheKey Create(
            string path,
            string? filter,
            string sceneHash,
            long length,
            NonSequentialRayDatabaseHeader header,
            long branchCount)
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            return new DatabaseCacheKey(
                fullPath,
                length,
                header.SceneHash,
                header.CreatedUtc.UtcTicks,
                branchCount,
                header.SourceRevision,
                filter?.Trim() ?? string.Empty,
                sceneHash);
        }
    }
}
