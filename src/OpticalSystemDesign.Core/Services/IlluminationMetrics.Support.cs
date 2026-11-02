using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.Services;

public static partial class IlluminationMetrics
{
    private sealed partial class Integrator
    {
        private RayTraceSample PrepareFrame()
        {
            var source = _generate(0, 0);
            var chief = _optic.SequentialRayTracer.DiagnoseApertures(source, _cancellation, stopAtIncidentImage: true,
                ignoreScalarTransmission: !_includeScalarTransmission).UnclippedImage
                ?? throw Unavailable("the geometric chief ray does not reach the image surface");
            var localHit = _image.CoordinateSystem.ToLocalPoint(chief.Position);
            var normal = Normalize(_image.Geometry.SurfaceNormal(localHit));
            var reference = Math.Abs(normal.X) < 0.95 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
            _tangentX = Normalize(reference - normal * Dot(reference, normal));
            _tangentY = Normalize(Cross(normal, _tangentX));
            return chief;
        }

        // Existence of an open transmitting angular pupil only. This is never
        // returned as an absolute projected area, F-number or irradiance.
        internal IlluminationPupilSupport InspectSupport(int density)
        {
            PrepareFrame();
            Node? first = null; Node? second = null; var positive = false;
            var pupils = ApertureSampler.GenerateGaussianQuadrature(Math.Clamp(density, 2, 32), Math.Clamp(6 * density, 12, 72));
            foreach (var pupil in pupils)
            {
                var node = Sample(pupil.X, pupil.Y);
                if (!node.Inside || node.Weight <= 0) continue;
                if (first is not { } a) first = node;
                else if (second is not { } b)
                { if (Math.Abs(node.L - a.L) + Math.Abs(node.M - a.M) > 1e-14) second = node; }
                else if (Math.Abs(SignedArea(a, b, node)) > 1e-22) positive = true;
            }
            return new(positive, _validRays, _nodes.Count);
        }
    }
}
