using System.Diagnostics;
using ILGPU;
using ILGPU.Runtime;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Core.Differentiation;

public static partial class DesignGpu
{
    internal static FixedInputOpticalResult? EvaluateFixedInput(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters,
        FixedInputRaySet inputs, double bendGate, CancellationToken token)
    {
        // Relative-thickness and historical implicit-aim derivatives retain the recorded path.
        if (parameters.Any(p => p.Kind == OpticalDesignParameterKind.Thickness)) return null;
        var runtime = RequireRuntime();
        var bindings = new int[optic.SurfaceGroup.Items.Count * 16];
        Array.Fill(bindings, -1);
        var scales = parameters.Select(p => p.Scale).ToArray();
        for (var slot = 0; slot < parameters.Count; slot++)
        {
            var p = parameters[slot];
            var component = p.Kind switch
            {
                OpticalDesignParameterKind.AxialPosition => 0,
                OpticalDesignParameterKind.Curvature => 1,
                OpticalDesignParameterKind.Conic => 2,
                OpticalDesignParameterKind.MaterialIndex => 3,
                OpticalDesignParameterKind.MaterialAbbe => 4,
                OpticalDesignParameterKind.EvenAsphereCoefficient => 5 + p.CoefficientIndex,
                _ => -1
            };
            if (component < 0 || component >= 15) return null;
            bindings[p.SurfaceIndex * 16 + component] = slot;
            if (p.LinkedSurfaceIndex is { } linked) bindings[linked * 16 + component] = slot;
        }
        var materialCoordinates = new double[optic.SurfaceGroup.Items.Count * 2];
        for (var index = 0; index < optic.SurfaceGroup.Items.Count; index++)
            if (bindings[index * 16 + 3] >= 0 || bindings[index * 16 + 4] >= 0)
            {
                var coordinates = OpticalDesignParameters.RelaxedGlassCoordinates(optic.SurfaceGroup.Items[index].MaterialAfter);
                materialCoordinates[index * 2] = coordinates.Nd; materialCoordinates[index * 2 + 1] = coordinates.Vd;
            }
        var fields = new List<FixedInputSpotField>();
        double milliseconds = 0; long rays = 0; var batches = 0;
        foreach (var field in inputs.Fields)
        {
            token.ThrowIfCancellationRequested();
            var nominal = optic.SequentialRayTracer.TraceGpuNominal(field.Rays, token, ignoreApertures: false);
            if (nominal is null) return null;
            var surfaces = optic.SequentialRayTracer.GpuSurfaces(field.Rays[0].WavelengthNanometers);
            var data = runtime.FixedInput(surfaces, bindings, materialCoordinates, scales, nominal,
                field.Rays.Count, field.Rays[0].WavelengthNanometers, bendGate, token);
            var output = new FixedInputSpotRay[field.Rays.Count];
            for (var ray = 0; ray < output.Length; ray++)
            {
                var invalid = false;
                for (var parameter = 0; parameter < parameters.Count; parameter++)
                {
                    var error = data.Errors[ray * parameters.Count + parameter];
                    if (error == 2) throw new ArithmeticException("A CUDA fixed-input intersection/refraction derivative is singular or disagrees with formal Core.");
                    if (error == 3) throw new GpuTapeRangeException("A CUDA fixed-input tangent requires extended-range CPU evaluation.");
                    invalid |= error == 1;
                }
                if (invalid) { output[ray] = new(0, 0, 0, 0, false); continue; }
                output[ray] = new(Component(0), Component(1), Component(2), Component(3), true);
                DifferentialScalar Component(int component)
                {
                    var gradient = new double[parameters.Count];
                    Array.Copy(data.Gradients, (ray * 4 + component) * parameters.Count, gradient, 0, parameters.Count);
                    return DifferentialScalar.FromGradient(data.Values[ray * 4 + component], gradient);
                }
            }
            fields.Add(new(field, output)); milliseconds += data.Milliseconds; rays += output.Count(ray => ray.Valid); batches++;
        }
        return new(fields, new("cuda-fixed-input-forward-v1", runtime.DeviceName, batches, rays, milliseconds));
    }

    private sealed partial class Runtime
    {
        private Action<Index1D, ArrayView<SequentialGpuSurface>, ArrayView<int>, ArrayView<double>, ArrayView<double>,
            ArrayView<SequentialGpuSample>, ArrayView<double>, ArrayView<double>, ArrayView<int>, int, int, double, double>? _fixedKernel;
        private MemoryBuffer1D<int, Stride1D.Dense>? _fixedBindings, _fixedErrors;
        private MemoryBuffer1D<double, Stride1D.Dense>? _fixedCoordinates, _fixedScales, _fixedValues, _fixedGradients;
        private MemoryBuffer1D<SequentialGpuSample, Stride1D.Dense>? _fixedNominal;

        public readonly record struct FixedBatch(double[] Values, double[] Gradients, int[] Errors, double Milliseconds);
        public FixedBatch FixedInput(SequentialGpuSurface[] surfaces, int[] bindings, double[] coordinates, double[] scales,
            SequentialGpuSample[] nominal, int rays, double wavelength, double gate, CancellationToken token)
        {
            if (rays is < 1 or > 4096 || surfaces.Length is < 2 or > 128 || scales.Length is < 1 or > 512)
                throw new ArgumentException("The CUDA fixed-input batch exceeds its bounded capacity.");
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                _fixedKernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<SequentialGpuSurface>, ArrayView<int>, ArrayView<double>, ArrayView<double>,
                    ArrayView<SequentialGpuSample>, ArrayView<double>, ArrayView<double>, ArrayView<int>, int, int, double, double>(FixedInputKernel);
                var timer = Stopwatch.StartNew();
                var surfaceView = UploadSurfaces(surfaces);
                EnsureBuffer(ref _fixedBindings, bindings.Length); EnsureBuffer(ref _fixedCoordinates, coordinates.Length);
                EnsureBuffer(ref _fixedScales, scales.Length); EnsureBuffer(ref _fixedNominal, nominal.Length);
                EnsureBuffer(ref _fixedValues, rays * 4); EnsureBuffer(ref _fixedGradients, rays * 4 * scales.Length);
                EnsureBuffer(ref _fixedErrors, rays * scales.Length);
                var bindingView = _fixedBindings!.View.BaseView.SubView(0, bindings.Length);
                var coordinateView = _fixedCoordinates!.View.BaseView.SubView(0, coordinates.Length);
                var scaleView = _fixedScales!.View.BaseView.SubView(0, scales.Length);
                var nominalView = _fixedNominal!.View.BaseView.SubView(0, nominal.Length);
                var values = _fixedValues!.View.BaseView.SubView(0, rays * 4);
                var gradients = _fixedGradients!.View.BaseView.SubView(0, rays * 4 * scales.Length);
                var errors = _fixedErrors!.View.BaseView.SubView(0, rays * scales.Length);
                bindingView.CopyFromCPU(bindings); coordinateView.CopyFromCPU(coordinates); scaleView.CopyFromCPU(scales); nominalView.CopyFromCPU(nominal);
                values.MemSetToZero(); gradients.MemSetToZero(); errors.MemSetToZero();
                _fixedKernel(rays * scales.Length, surfaceView, bindingView, coordinateView, scaleView, nominalView, values, gradients, errors,
                    surfaces.Length, scales.Length, wavelength, gate);
                var outputValues = new double[rays * 4]; var outputGradients = new double[rays * 4 * scales.Length]; var outputErrors = new int[rays * scales.Length];
                values.CopyToCPU(outputValues); gradients.CopyToCPU(outputGradients); errors.CopyToCPU(outputErrors);
                token.ThrowIfCancellationRequested();
                return new(outputValues, outputGradients, outputErrors, timer.Elapsed.TotalMilliseconds);
            }
        }

        private void DisposeFixedInputBuffers()
        {
            _fixedBindings?.Dispose(); _fixedErrors?.Dispose(); _fixedCoordinates?.Dispose(); _fixedScales?.Dispose();
            _fixedValues?.Dispose(); _fixedGradients?.Dispose(); _fixedNominal?.Dispose();
        }

        private static void FixedInputKernel(Index1D thread, ArrayView<SequentialGpuSurface> surfaces, ArrayView<int> bindings,
            ArrayView<double> coordinates, ArrayView<double> scales, ArrayView<SequentialGpuSample> nominal,
            ArrayView<double> values, ArrayView<double> gradients, ArrayView<int> errors,
            int surfaceCount, int parameterCount, double wavelength, double gate)
        {
            var ray = thread / parameterCount; var parameter = thread % parameterCount;
            var first = nominal[ray * surfaceCount];
            DeviceTangent ox = first.Point.X, oy = first.Point.Y, oz = first.Point.Z;
            DeviceTangent dx = first.Direction.X, dy = first.Direction.Y, dz = first.Direction.Z, bendPenalty = 0;
            for (var index = 1; index < surfaceCount; index++)
            {
                var sample = nominal[ray * surfaceCount + index];
                if (sample.Present == 0 || sample.Miss != 0 || sample.Blocked != 0 || sample.Kind == (int)RayInteractionKind.TotalInternalReflection)
                { errors[thread] = 1; return; }
                var surface = surfaces[index];
                var center = Seed(surface.Origin.Z, bindings[index * 16], parameter, scales);
                var t = sample.Segment;
                var qx = ox - surface.Origin.X + dx * t; var qy = oy - surface.Origin.Y + dy * t; var qz = oz - center + dz * t;
                var profileError = Profile(surface.Equation, index, qx, qy, bindings, scales, parameter, out var sag, out var slope);
                if (profileError != 0) { errors[thread] = profileError; return; }
                var denominator = dz.Value - 2 * slope.Value * (qx.Value * dx.Value + qy.Value * dy.Value);
                if (!double.IsFinite(denominator) || Math.Abs(denominator) < 1e-9) { errors[thread] = 2; return; }
                var distance = t - (qz - sag).WithValue(0) / denominator;
                qx = ox - surface.Origin.X + dx * distance; qy = oy - surface.Origin.Y + dy * distance; qz = oz - center + dz * distance;
                if (!qx.IsFinite || !qy.IsFinite || !qz.IsFinite) { errors[thread] = 3; return; }
                if (!Agrees(qx, sample.Point.X - surface.Origin.X) || !Agrees(qy, sample.Point.Y - surface.Origin.Y)
                    || !Agrees(qz, sample.Point.Z - surface.Origin.Z)) { errors[thread] = 2; return; }
                qx = qx.WithValue(sample.Point.X - surface.Origin.X); qy = qy.WithValue(sample.Point.Y - surface.Origin.Y);
                qz = qz.WithValue(sample.Point.Z - surface.Origin.Z);
                profileError = Profile(surface.Equation, index, qx, qy, bindings, scales, parameter, out sag, out slope);
                if (profileError != 0) { errors[thread] = profileError; return; }
                var nx = -2 * qx * slope; var ny = -2 * qy * slope;
                var normalLength = DeviceTangent.Sqrt(nx * nx + ny * ny + 1);
                nx /= normalLength; ny /= normalLength; var nz = 1 / normalLength;
                if ((nx * dx + ny * dy + nz * dz).Value > 0) { nx = -nx; ny = -ny; nz = -nz; }
                var before = MaterialIndex(index - 1, wavelength, parameter, surfaces, bindings, coordinates, scales);
                var after = MaterialIndex(index, wavelength, parameter, surfaces, bindings, coordinates, scales);
                var eta = before / after;
                var cosine = -(nx * dx + ny * dy + nz * dz);
                var root = 1 - eta * eta * (1 - cosine * cosine);
                if (root.Value <= 1e-12) { errors[thread] = 2; return; }
                var normalScale = eta * cosine - DeviceTangent.Sqrt(root);
                var nextX = eta * dx + normalScale * nx; var nextY = eta * dy + normalScale * ny; var nextZ = eta * dz + normalScale * nz;
                var length = DeviceTangent.Sqrt(nextX * nextX + nextY * nextY + nextZ * nextZ);
                nextX /= length; nextY /= length; nextZ /= length;
                if (!nextX.IsFinite || !nextY.IsFinite || !nextZ.IsFinite) { errors[thread] = 3; return; }
                if (!Agrees(nextX, sample.Direction.X) || !Agrees(nextY, sample.Direction.Y) || !Agrees(nextZ, sample.Direction.Z))
                { errors[thread] = 2; return; }
                var bend = dx * nextX + dy * nextY + dz * nextZ;
                bendPenalty += DeviceTangent.PositivePart((gate - bend) / Math.Max(1 - gate, 1e-12));
                dx = nextX.WithValue(sample.Direction.X); dy = nextY.WithValue(sample.Direction.Y); dz = nextZ.WithValue(sample.Direction.Z);
                ox = (qx + surface.Origin.X).WithValue(sample.Point.X); oy = (qy + surface.Origin.Y).WithValue(sample.Point.Y);
                oz = (qz + center).WithValue(sample.Point.Z);
                if (!ox.IsFinite || !oy.IsFinite || !oz.IsFinite || !dx.IsFinite || !dy.IsFinite || !dz.IsFinite || !bendPenalty.IsFinite)
                { errors[thread] = 3; return; }
            }
            gradients[(ray * 4) * parameterCount + parameter] = ox.Derivative;
            gradients[(ray * 4 + 1) * parameterCount + parameter] = oy.Derivative;
            gradients[(ray * 4 + 2) * parameterCount + parameter] = dz.Derivative;
            gradients[(ray * 4 + 3) * parameterCount + parameter] = bendPenalty.Derivative;
            if (parameter == 0) { values[ray * 4] = ox.Value; values[ray * 4 + 1] = oy.Value; values[ray * 4 + 2] = dz.Value; values[ray * 4 + 3] = bendPenalty.Value; }
        }

        private static bool Agrees(DeviceTangent value, double formal) => value.IsFinite && double.IsFinite(formal)
            && Math.Abs(value.Value - formal) <= 1e-7 * Math.Max(1, Math.Abs(formal));

        private static DeviceTangent Seed(double value, int binding, int parameter, ArrayView<double> scales)
        {
            if (binding < 0) return value;
            var scale = scales[binding];
            return new(value, binding == parameter ? scale : 0, Math.Abs(scale));
        }

        private static DeviceTangent MaterialIndex(int surface, double wavelength, int parameter, ArrayView<SequentialGpuSurface> surfaces,
            ArrayView<int> bindings, ArrayView<double> coordinates, ArrayView<double> scales)
        {
            var nBinding = bindings[surface * 16 + 3]; var vBinding = bindings[surface * 16 + 4];
            if (nBinding < 0 && vBinding < 0) return surfaces[surface].IndexAfter;
            var nd = Seed(coordinates[surface * 2], nBinding, parameter, scales);
            var vd = Seed(coordinates[surface * 2 + 1], vBinding, parameter, scales);
            var b = (nd - 1) / vd / (1 / (.486 * .486) - 1 / (.656 * .656));
            var wave = wavelength / 1000;
            return nd + b * (1 / (wave * wave) - 1 / (.587 * .587));
        }

        private static int Profile(RotationalSurfaceEquation equation, int surface, DeviceTangent x, DeviceTangent y,
            ArrayView<int> bindings, ArrayView<double> scales, int parameter, out DeviceTangent sag, out DeviceTangent slope)
        {
            var curvature = Math.Abs(equation.Radius) < 1e-12 || double.IsInfinity(equation.Radius) ? 0 : 1 / equation.Radius;
            var c = Seed(curvature, bindings[surface * 16 + 1], parameter, scales);
            var k = Seed(equation.Conic, bindings[surface * 16 + 2], parameter, scales);
            var u = x * x + y * y; var argument = 1 - (1 + k) * c * c * u;
            sag = 0; slope = 0;
            if (argument.Value <= 1e-14) return 2;
            var root = DeviceTangent.Sqrt(argument);
            sag = c * u / (1 + root); slope = c / (2 * root);
            DeviceTangent power = 1;
            for (var index = 0; index < equation.Count; index++)
            {
                var coefficient = Seed(equation.Coefficient(index), bindings[surface * 16 + 5 + index], parameter, scales);
                slope += (index + 1) * coefficient * power; power *= u; sag += coefficient * power;
            }
            return sag.IsFinite && slope.IsFinite ? 0 : 3;
        }
    }
}
