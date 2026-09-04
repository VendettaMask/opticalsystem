using System.Buffers;
using System.Collections.Concurrent;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Raytrace;

public sealed partial class SequentialRayTracer
{
    private bool TryTraceSurfaceBatched(
        OpticalSurface surface,
        int surfaceIndex,
        int surfaceCount,
        TraceRequest request,
        int[] surfaceSlots,
        PooledRayStateBuffer states,
        IMaterial[] materials,
        double[] cumulativePaths,
        double[] cumulativeOpticalPaths,
        bool[] active,
        int rayCount,
        RayTraceSampleValue[] samples,
        bool[] hasSamples,
        double[]? finalOpticalPaths,
        bool[]? hasFinalOpticalPath)
    {
        if ((surfaceIndex == 0 && ObjectConjugate.IsInfinite(surface))
            || !request.UseBatchedBackend
            || surface.Geometry is not (PlaneGeometry or StandardGeometry)
            || surface.PhysicalAperture is not (null or CircularAperture)
            || surface.InteractionModel is not RefractiveReflectiveInteractionModel
            || surface.CoatingModel is not (NoneCoatingModel or SimpleCoatingModel)
            || surface.ScatteringModel is not null)
        {
            return false;
        }

        for (var index = 0; index < rayCount; index++)
        {
            if (active[index]
                && materials[index].PropagationModel is not HomogeneousPropagationModel)
            {
                return false;
            }
        }

        var backend = _optic.Backend.CurrentBatched;
        var options = new ParallelOptions
        {
            CancellationToken = ComputationCancellation.Current,
            MaxDegreeOfParallelism = request.MaxDegreeOfParallelism
        };
        var chunkSize = Math.Max(256, backend.PreferredBatchWidth * 128);

        void TraceRange(int start, int end)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var count = end - start;
            using var workspace = new SurfaceBatchWorkspace(count);
            for (var localIndex = 0; localIndex < count; localIndex++)
            {
                var rayIndex = start + localIndex;
                if (!active[rayIndex])
                {
                    workspace.DirectionZ[localIndex] = 1;
                    workspace.RefractiveIndexBefore[localIndex] = 1;
                    workspace.RefractiveIndexAfter[localIndex] = 1;
                    continue;
                }

                var state = states[rayIndex];
                var localOrigin = surface.CoordinateSystem.ToLocalPoint(state.Origin);
                var localDirection = surface.CoordinateSystem.ToLocalDirection(state.Direction);
                workspace.OriginX[localIndex] = localOrigin.X;
                workspace.OriginY[localIndex] = localOrigin.Y;
                workspace.OriginZ[localIndex] = localOrigin.Z;
                workspace.DirectionX[localIndex] = localDirection.X;
                workspace.DirectionY[localIndex] = localDirection.Y;
                workspace.DirectionZ[localIndex] = localDirection.Z;
                workspace.RefractiveIndexBefore[localIndex] =
                    materials[rayIndex].RefractiveIndex(state.WavelengthNanometers);
                workspace.RefractiveIndexAfter[localIndex] =
                    surface.MaterialAfter.RefractiveIndex(state.WavelengthNanometers);
            }

            if (surface.Geometry is StandardGeometry standard)
            {
                backend.IntersectStandard(
                    workspace.OriginX,
                    workspace.OriginY,
                    workspace.OriginZ,
                    workspace.DirectionX,
                    workspace.DirectionY,
                    workspace.DirectionZ,
                    standard.Radius,
                    standard.Conic,
                    workspace.Distance,
                    workspace.Intersects);
            }
            else
            {
                backend.IntersectPlane(
                    workspace.OriginZ,
                    workspace.DirectionZ,
                    0,
                    workspace.Distance,
                    workspace.Intersects);
            }

            for (var localIndex = 0; localIndex < count; localIndex++)
            {
                var rayIndex = start + localIndex;
                if (!active[rayIndex] || !workspace.Intersects[localIndex])
                {
                    workspace.Distance[localIndex] = 0;
                }
            }

            backend.Propagate(
                workspace.OriginX,
                workspace.OriginY,
                workspace.OriginZ,
                workspace.DirectionX,
                workspace.DirectionY,
                workspace.DirectionZ,
                workspace.Distance,
                workspace.HitX,
                workspace.HitY,
                workspace.HitZ);

            if (surface.PhysicalAperture is CircularAperture circular)
            {
                backend.ApplyCircularAperture(
                    workspace.HitX,
                    workspace.HitY,
                    circular.Radius,
                    workspace.Accepted);
            }
            else
            {
                workspace.Accepted.Fill(true);
            }

            for (var localIndex = 0; localIndex < count; localIndex++)
            {
                var rayIndex = start + localIndex;
                if (!active[rayIndex] || !workspace.Intersects[localIndex])
                {
                    workspace.NormalZ[localIndex] = 1;
                    workspace.Accepted[localIndex] = false;
                    continue;
                }

                var hit = new Vector3D(
                    workspace.HitX[localIndex],
                    workspace.HitY[localIndex],
                    workspace.HitZ[localIndex]);
                var normal = surface.Geometry.SurfaceNormal(hit);
                workspace.NormalX[localIndex] = normal.X;
                workspace.NormalY[localIndex] = normal.Y;
                workspace.NormalZ[localIndex] = normal.Z;
            }

            var interaction = (RefractiveReflectiveInteractionModel)surface.InteractionModel;
            backend.RefractOrReflect(
                workspace.DirectionX,
                workspace.DirectionY,
                workspace.DirectionZ,
                workspace.NormalX,
                workspace.NormalY,
                workspace.NormalZ,
                workspace.RefractiveIndexBefore,
                workspace.RefractiveIndexAfter,
                surface.IsReflective || interaction.IsReflective,
                workspace.OutgoingX,
                workspace.OutgoingY,
                workspace.OutgoingZ,
                workspace.InteractionKinds);

            var slot = surfaceSlots[surfaceIndex];
            for (var localIndex = 0; localIndex < count; localIndex++)
            {
                var rayIndex = start + localIndex;
                if (!active[rayIndex])
                {
                    continue;
                }

                var incoming = states[rayIndex];
                RayTraceSampleValue sample;
                if (!workspace.Intersects[localIndex])
                {
                    states[rayIndex] = incoming with { Intensity = 0 };
                    sample = new RayTraceSampleValue(
                        surface.Number,
                        surface.Label,
                        incoming.Origin,
                        incoming.Direction,
                        0,
                        true,
                        CumulativePathLength: cumulativePaths[rayIndex],
                        CumulativeOpticalPathLength: cumulativeOpticalPaths[rayIndex]);
                    active[rayIndex] = false;
                }
                else
                {
                    var segmentLength = Math.Max(0, workspace.Distance[localIndex]);
                    var refractiveIndexBefore = workspace.RefractiveIndexBefore[localIndex];
                    var segmentOpticalPathLength = Math.Abs(segmentLength * refractiveIndexBefore);
                    cumulativePaths[rayIndex] += segmentLength;
                    cumulativeOpticalPaths[rayIndex] += segmentOpticalPathLength;
                    var localHit = new Vector3D(
                        workspace.HitX[localIndex],
                        workspace.HitY[localIndex],
                        workspace.HitZ[localIndex]);
                    var globalHit = surface.CoordinateSystem.ToGlobalPoint(localHit);
                    var extinction = materials[rayIndex]
                        .ExtinctionCoefficient(incoming.WavelengthNanometers);
                    var wavelengthMicrometers = incoming.WavelengthNanometers / 1000.0;
                    var attenuation = extinction <= 0
                        ? 1.0
                        : Math.Exp(
                            (-4.0 * Math.PI * extinction * segmentLength * 1000.0)
                            / wavelengthMicrometers);
                    var propagatedIntensity = incoming.Intensity * attenuation;
                    var propagatedOpd = incoming.OpticalPathDifference + segmentOpticalPathLength;

                    if (!workspace.Accepted[localIndex])
                    {
                        states[rayIndex] = incoming with
                        {
                            Origin = globalHit,
                            Intensity = 0,
                            OpticalPathDifference = propagatedOpd
                        };
                        sample = new RayTraceSampleValue(
                            surface.Number,
                            surface.Label,
                            globalHit,
                            incoming.Direction,
                            0,
                            true,
                            segmentLength,
                            segmentOpticalPathLength,
                            cumulativePaths[rayIndex],
                            cumulativeOpticalPaths[rayIndex]);
                        active[rayIndex] = false;
                    }
                    else
                    {
                        var kind = workspace.InteractionKinds[localIndex];
                        var outgoingMaterial = kind == RayInteractionKind.Transmitted
                            ? surface.MaterialAfter
                            : materials[rayIndex];
                        var coatingFactor = surface.CoatingModel is SimpleCoatingModel simple
                            ? kind is RayInteractionKind.Reflected or RayInteractionKind.TotalInternalReflection
                                ? simple.Reflectance
                                : simple.Transmittance
                            : 1.0;
                        var outgoingLocal = new Vector3D(
                            workspace.OutgoingX[localIndex],
                            workspace.OutgoingY[localIndex],
                            workspace.OutgoingZ[localIndex]);
                        var outgoingGlobal =
                            surface.CoordinateSystem.ToGlobalDirection(outgoingLocal);
                        var outgoingIntensity = propagatedIntensity * coatingFactor;
                        states[rayIndex] = incoming with
                        {
                            Origin = globalHit,
                            Direction = outgoingGlobal,
                            Intensity = outgoingIntensity,
                            OpticalPathDifference = propagatedOpd,
                            IsNormalized = true
                        };
                        materials[rayIndex] = outgoingMaterial;
                        sample = new RayTraceSampleValue(
                            surface.Number,
                            surface.Label,
                            globalHit,
                            outgoingGlobal,
                            outgoingIntensity,
                            false,
                            segmentLength,
                            segmentOpticalPathLength,
                            cumulativePaths[rayIndex],
                            cumulativeOpticalPaths[rayIndex],
                            InteractionKind: kind);
                    }
                }

                if (slot >= 0)
                {
                    var offset = (slot * rayCount) + rayIndex;
                    samples[offset] = sample;
                    hasSamples[offset] = true;
                }

                if (request.NormalizeOpticalPathDifference
                    && surfaceIndex == surfaceCount - 1
                    && sample.Intensity > 0
                    && !sample.Vignetted)
                {
                    finalOpticalPaths![rayIndex] = sample.CumulativeOpticalPathLength;
                    hasFinalOpticalPath![rayIndex] = true;
                }
            }
        }

        if (rayCount >= Math.Max(1, request.ParallelThreshold)
            && request.MaxDegreeOfParallelism != 1)
        {
            Parallel.ForEach(
                Partitioner.Create(0, rayCount, chunkSize),
                options,
                range => TraceRange(range.Item1, range.Item2));
        }
        else
        {
            TraceRange(0, rayCount);
        }

        return true;
    }

    private sealed class SurfaceBatchWorkspace : IDisposable
    {
        private const int DoubleFieldCount = 18;
        private readonly int _length;
        private double[]? _values;
        private bool[]? _intersects;
        private bool[]? _accepted;
        private RayInteractionKind[]? _interactionKinds;

        public SurfaceBatchWorkspace(int length)
        {
            _length = length;
            _values = ArrayPool<double>.Shared.Rent(Math.Max(1, length * DoubleFieldCount));
            _intersects = ArrayPool<bool>.Shared.Rent(Math.Max(1, length));
            _accepted = ArrayPool<bool>.Shared.Rent(Math.Max(1, length));
            _interactionKinds =
                ArrayPool<RayInteractionKind>.Shared.Rent(Math.Max(1, length));
            Array.Clear(_intersects, 0, length);
            Array.Clear(_accepted, 0, length);
        }

        public Span<double> OriginX => Field(0);
        public Span<double> OriginY => Field(1);
        public Span<double> OriginZ => Field(2);
        public Span<double> DirectionX => Field(3);
        public Span<double> DirectionY => Field(4);
        public Span<double> DirectionZ => Field(5);
        public Span<double> Distance => Field(6);
        public Span<double> HitX => Field(7);
        public Span<double> HitY => Field(8);
        public Span<double> HitZ => Field(9);
        public Span<double> NormalX => Field(10);
        public Span<double> NormalY => Field(11);
        public Span<double> NormalZ => Field(12);
        public Span<double> RefractiveIndexBefore => Field(13);
        public Span<double> RefractiveIndexAfter => Field(14);
        public Span<double> OutgoingX => Field(15);
        public Span<double> OutgoingY => Field(16);
        public Span<double> OutgoingZ => Field(17);
        public Span<bool> Intersects => _intersects.AsSpan(0, _length);
        public Span<bool> Accepted => _accepted.AsSpan(0, _length);
        public Span<RayInteractionKind> InteractionKinds =>
            _interactionKinds.AsSpan(0, _length);

        public void Dispose()
        {
            var values = Interlocked.Exchange(ref _values, null);
            var intersects = Interlocked.Exchange(ref _intersects, null);
            var accepted = Interlocked.Exchange(ref _accepted, null);
            var interactionKinds = Interlocked.Exchange(ref _interactionKinds, null);
            if (values is not null)
            {
                ArrayPool<double>.Shared.Return(values, clearArray: true);
            }

            if (intersects is not null)
            {
                ArrayPool<bool>.Shared.Return(intersects, clearArray: true);
            }

            if (accepted is not null)
            {
                ArrayPool<bool>.Shared.Return(accepted, clearArray: true);
            }

            if (interactionKinds is not null)
            {
                ArrayPool<RayInteractionKind>.Shared.Return(
                    interactionKinds,
                    clearArray: true);
            }
        }

        private Span<double> Field(int index) =>
            _values.AsSpan(index * _length, _length);
    }
}
