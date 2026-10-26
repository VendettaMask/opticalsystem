using System.Diagnostics;
using ILGPU;
using ILGPU.Runtime;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Core.Differentiation;

[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public struct SequentialGpuSurface
{
    public RotationalSurfaceEquation Equation;
    public Vector3D Origin;
    public double IndexAfter, ExtinctionAfter, ApertureBoundary, Transmission, Reflection;
    public int Backtrack;
}

[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public struct SequentialGpuRay
{
    public Vector3D Origin, Direction;
    public double Intensity, Opd, WavelengthNanometers, TargetX, TargetY, PupilX, PupilY, EntranceRadius, AimTolerance;
    public int Valid, AimTraces;
}

[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public struct SequentialGpuSample
{
    public Vector3D Point, Direction, Incident;
    public double Intensity, Segment, OpticalSegment, Path, OpticalPath, Opd, Before, After;
    public int Present, Blocked, Kind, Miss;
}

public static partial class DesignGpu
{
    internal static (SequentialGpuSample[] Samples, double Milliseconds) Propagate(SequentialGpuSurface[] surfaces,
        SequentialGpuRay[] rays, bool ignoreApertures, int last, CancellationToken token, bool ignoreAbsorption = false) =>
        (Instance.Value ?? throw new NotSupportedException("CUDA is unavailable.")).Propagate(surfaces, rays, ignoreApertures, last, token, ignoreAbsorption);

    internal static (SequentialGpuRay[] Rays, double Milliseconds) Aim(SequentialGpuSurface[] surfaces,
        SequentialGpuRay[] rays, int stop, CancellationToken token) =>
        (Instance.Value ?? throw new NotSupportedException("CUDA is unavailable.")).Aim(surfaces, rays, stop, token);

    private sealed partial class Runtime
    {
        private Action<Index1D, ArrayView<SequentialGpuSurface>, ArrayView<SequentialGpuRay>, ArrayView<SequentialGpuSample>, int, int>? _traceKernel;
        private Action<Index1D, ArrayView<SequentialGpuSurface>, ArrayView<SequentialGpuRay>, int>? _aimKernel;
        private MemoryBuffer1D<SequentialGpuSurface, Stride1D.Dense>? _propagationSurfaces;
        private MemoryBuffer1D<SequentialGpuRay, Stride1D.Dense>? _propagationRays;
        private MemoryBuffer1D<SequentialGpuSample, Stride1D.Dense>? _propagationSamples;
        private SequentialGpuSurface[]? _uploadedSurfaces;

        private void EnsureBuffer<T>(ref MemoryBuffer1D<T, Stride1D.Dense>? buffer, int count) where T : unmanaged
        {
            if (buffer is not null && buffer.Length >= count) return;
            buffer?.Dispose(); buffer = _accelerator.Allocate1D<T>(count);
        }

        private ArrayView<SequentialGpuSurface> UploadSurfaces(SequentialGpuSurface[] surfaces)
        {
            EnsureBuffer(ref _propagationSurfaces, surfaces.Length);
            var view = _propagationSurfaces!.View.BaseView.SubView(0, surfaces.Length);
            if (_uploadedSurfaces is null || !System.Runtime.InteropServices.MemoryMarshal.AsBytes(surfaces.AsSpan())
                .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(_uploadedSurfaces.AsSpan())))
            {
                view.CopyFromCPU(surfaces); _uploadedSurfaces = surfaces.ToArray();
            }
            return view;
        }

        private void DisposePropagationBuffers()
        { _propagationSamples?.Dispose(); _propagationRays?.Dispose(); _propagationSurfaces?.Dispose(); }

        public (SequentialGpuSample[], double) Propagate(SequentialGpuSurface[] surfaces, SequentialGpuRay[] rays,
            bool ignoreApertures, int last, CancellationToken token, bool ignoreAbsorption)
        {
            if (rays.Length is < 1 or > 4096 || surfaces.Length is < 2 or > 128 || last < 1 || last >= surfaces.Length)
                throw new ArgumentException("CUDA propagation batch exceeds its bounds.");
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                _traceKernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<SequentialGpuSurface>, ArrayView<SequentialGpuRay>, ArrayView<SequentialGpuSample>, int, int>(TraceKernel);
                var timer = Stopwatch.StartNew();
                var deviceSurfaces = UploadSurfaces(surfaces);
                EnsureBuffer(ref _propagationRays, rays.Length);
                var deviceRays = _propagationRays!.View.BaseView.SubView(0, rays.Length);
                deviceRays.CopyFromCPU(rays);
                EnsureBuffer(ref _propagationSamples, rays.Length * surfaces.Length);
                var samples = _propagationSamples!.View.BaseView.SubView(0, rays.Length * surfaces.Length);
                samples.MemSetToZero();
                _traceKernel(rays.Length, deviceSurfaces, deviceRays, samples, last, (ignoreApertures ? 1 : 0) | (ignoreAbsorption ? 2 : 0));
                var result = new SequentialGpuSample[rays.Length * surfaces.Length]; samples.CopyToCPU(result); timer.Stop();
                token.ThrowIfCancellationRequested(); return (result, timer.Elapsed.TotalMilliseconds);
            }
        }

        public (SequentialGpuRay[], double) Aim(SequentialGpuSurface[] surfaces, SequentialGpuRay[] rays, int stop, CancellationToken token)
        {
            if (rays.Length is < 1 or > 4096 || surfaces.Length is < 2 or > 128 || stop < 1 || stop >= surfaces.Length)
                throw new ArgumentException("CUDA aiming batch exceeds its bounds.");
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                _aimKernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<SequentialGpuSurface>, ArrayView<SequentialGpuRay>, int>(AimKernel);
                var timer = Stopwatch.StartNew();
                var deviceSurfaces = UploadSurfaces(surfaces);
                EnsureBuffer(ref _propagationRays, rays.Length);
                var deviceRays = _propagationRays!.View.BaseView.SubView(0, rays.Length);
                deviceRays.CopyFromCPU(rays);
                _aimKernel(rays.Length, deviceSurfaces, deviceRays, stop);
                var result = new SequentialGpuRay[rays.Length]; deviceRays.CopyToCPU(result); timer.Stop();
                token.ThrowIfCancellationRequested(); return (result, timer.Elapsed.TotalMilliseconds);
            }
        }

        private static void TraceKernel(Index1D ray, ArrayView<SequentialGpuSurface> surfaces,
            ArrayView<SequentialGpuRay> rays, ArrayView<SequentialGpuSample> samples, int last, int ignoreApertures)
        {
            var state = rays[ray]; var n = surfaces[0].IndexAfter; var extinction = surfaces[0].ExtinctionAfter; var path = 0d; var optical = 0d;
            var slot = ray * surfaces.Length;
            samples[slot] = new()
            {
                Present = 1,
                Point = state.Origin,
                Direction = state.Direction,
                Incident = state.Direction,
                Intensity = state.Intensity,
                Opd = state.Opd,
                Kind = -1
            };
            if (state.Valid == 0 || state.Intensity <= 0) return;
            for (var i = 1; i <= last; i++)
            {
                var sample = Step(surfaces[i], ref state, ref n, ref extinction, ref path, ref optical, ignoreApertures);
                samples[slot + i] = sample;
                if (sample.Blocked != 0 || sample.Miss != 0) break;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static SequentialGpuSample Step(SequentialGpuSurface surface, ref SequentialGpuRay ray,
            ref double n, ref double extinction, ref double path, ref double optical, int ignoreApertures)
        {
            var origin = ray.Origin - surface.Origin; var direction = ray.Direction;
            var hit = surface.Equation.Intersect(origin, direction); var distance = hit.Distance;
            if (!hit.IsHit && surface.Backtrack != 0)
            { hit = surface.Equation.Intersect(origin, -direction); distance = -hit.Distance; }
            if (!hit.IsHit) return new() { Present = 1, Miss = 1, Blocked = 1, Point = ray.Origin, Direction = direction, Path = path, OpticalPath = optical, Kind = -1 };
            var segment = surface.Backtrack != 0 ? distance : Math.Max(0, distance);
            var opd = surface.Backtrack != 0 ? segment * n : Math.Abs(segment * n);
            path += segment; optical += opd; ray.Opd += opd;
            if ((ignoreApertures & 2) == 0 && extinction > 0)
                ray.Intensity *= Math.Exp((-4.0 * Math.PI * extinction * Math.Abs(segment) * 1000.0) / (ray.WavelengthNanometers / 1000.0));
            var sample = new SequentialGpuSample
            {
                Present = 1,
                Segment = segment,
                OpticalSegment = opd,
                Path = path,
                OpticalPath = optical,
                Before = n,
                After = surface.IndexAfter,
                Kind = -1,
                Direction = direction,
                Incident = direction,
                Opd = ray.Opd
            };
            if ((ignoreApertures & 1) == 0 && hit.Point.X * hit.Point.X + hit.Point.Y * hit.Point.Y > surface.ApertureBoundary * surface.ApertureBoundary)
            {
                sample.Point = ray.Origin + direction * segment; sample.Blocked = 1;
                ray.Intensity = 0; return sample;
            }
            var normal = surface.Equation.Normal(hit.Point);
            if (normal.X * direction.X + normal.Y * direction.Y + normal.Z * direction.Z > 0) normal = -normal;
            var interaction = RefractiveReflectiveInteractionModel.RefractShared(direction, normal, n, surface.IndexAfter, hit.IncidenceCosineSquared);
            ray.Origin = hit.Point + surface.Origin; ray.Direction = interaction.Direction;
            ray.Intensity *= interaction.Kind == RayInteractionKind.Transmitted ? surface.Transmission : surface.Reflection;
            if (interaction.Kind == RayInteractionKind.Transmitted) { n = surface.IndexAfter; extinction = surface.ExtinctionAfter; }
            sample.Point = ray.Origin; sample.Direction = ray.Direction; sample.Intensity = ray.Intensity; sample.Kind = (int)interaction.Kind;
            return sample;
        }

        private struct GpuStopMap : IStopMap
        {
            public ArrayView<SequentialGpuSurface> Surfaces;
            public SequentialGpuRay Source;
            public int Stop, Traces;
            public bool Sample(double x, double y, out Vector3D point)
            {
                Traces++; point = default;
                if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
                var ray = Source; ray.Origin = new(x, y, Source.Origin.Z);
                // TraceToSurface starts by normalizing the source RealRay.
                ray.Direction /= ray.Direction.Length;
                var n = Surfaces[0].IndexAfter; var extinction = Surfaces[0].ExtinctionAfter; var path = 0d; var optical = 0d;
                for (var i = 1; i <= Stop; i++)
                {
                    var hit = Step(Surfaces[i], ref ray, ref n, ref extinction, ref path, ref optical, 3);
                    if (hit.Miss != 0 || hit.Blocked != 0) return false;
                    if (i == Stop) point = hit.Point - Surfaces[i].Origin;
                }
                return double.IsFinite(point.X) && double.IsFinite(point.Y);
            }
        }

        private static void AimKernel(Index1D index, ArrayView<SequentialGpuSurface> surfaces, ArrayView<SequentialGpuRay> rays, int stop)
        {
            var ray = rays[index]; var x = ray.Origin.X; var y = ray.Origin.Y;
            var map = new GpuStopMap { Surfaces = surfaces, Source = ray, Stop = stop };
            var solved = StopAimingSolver.Solve(ref map, ray.TargetX, ray.TargetY, ray.AimTolerance, ref x, ref y, out _);
            if (!solved)
            {
                // The same chief-to-marginal continuation as the normal ray generator.
                x = ray.Origin.X - ray.PupilX * ray.EntranceRadius; y = ray.Origin.Y - ray.PupilY * ray.EntranceRadius;
                solved = StopAimingSolver.Solve(ref map, 0, 0, ray.AimTolerance, ref x, ref y, out _);
                var fraction = 0d; var increment = 1d / 16;
                for (var step = 1; solved && fraction < 1 && step <= 64; step++)
                {
                    var next = Math.Min(1, fraction + increment); var nx = x; var ny = y;
                    if (!StopAimingSolver.Solve(ref map, ray.TargetX * next, ray.TargetY * next, ray.AimTolerance, ref nx, ref ny, out _))
                    { increment *= .5; if (increment < 1e-6) solved = false; continue; }
                    x = nx; y = ny; fraction = next; increment = Math.Min(1d / 16, increment * 1.5);
                }
                solved &= fraction >= 1;
            }
            ray.Valid = solved ? 1 : 0; ray.AimTraces = map.Traces;
            if (solved) ray.Origin = new(x, y, ray.Origin.Z);
            rays[index] = ray;
        }
    }
}
