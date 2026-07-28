using System.Numerics;
using OptilandWorkbench.Core.Interactions;

namespace OptilandWorkbench.Core.Backend;

public sealed partial class ManagedCpuBackend
{
    private void RefractOrReflectVectorized(
        ReadOnlySpan<double> directionX,
        ReadOnlySpan<double> directionY,
        ReadOnlySpan<double> directionZ,
        ReadOnlySpan<double> normalX,
        ReadOnlySpan<double> normalY,
        ReadOnlySpan<double> normalZ,
        ReadOnlySpan<double> refractiveIndexBefore,
        ReadOnlySpan<double> refractiveIndexAfter,
        bool forceReflection,
        Span<double> resultX,
        Span<double> resultY,
        Span<double> resultZ,
        Span<RayInteractionKind> interactionKinds)
    {
        BatchValidation.EqualLengths(
            directionX.Length,
            directionY.Length,
            directionZ.Length,
            normalX.Length,
            normalY.Length,
            normalZ.Length,
            refractiveIndexBefore.Length,
            refractiveIndexAfter.Length,
            resultX.Length,
            resultY.Length,
            resultZ.Length,
            interactionKinds.Length);

        var width = Vector<double>.Count;
        var index = 0;
        var zero = Vector<double>.Zero;
        var one = Vector<double>.One;
        var two = new Vector<double>(2);
        var epsilon = new Vector<double>(Epsilon);
        var epsilonSquared = new Vector<double>(Epsilon * Epsilon);
        for (; index <= directionX.Length - width; index += width)
        {
            var dx = new Vector<double>(directionX.Slice(index, width));
            var dy = new Vector<double>(directionY.Slice(index, width));
            var dz = new Vector<double>(directionZ.Slice(index, width));
            NormalizeVector(ref dx, ref dy, ref dz, epsilonSquared, zero, one);

            var nx = new Vector<double>(normalX.Slice(index, width));
            var ny = new Vector<double>(normalY.Slice(index, width));
            var nz = new Vector<double>(normalZ.Slice(index, width));
            NormalizeVector(ref nx, ref ny, ref nz, epsilonSquared, zero, one);

            var dot = (dx * nx) + (dy * ny) + (dz * nz);
            var flip = Vector.GreaterThan(dot, zero);
            nx = Vector.ConditionalSelect(flip, -nx, nx);
            ny = Vector.ConditionalSelect(flip, -ny, ny);
            nz = Vector.ConditionalSelect(flip, -nz, nz);
            dot = (dx * nx) + (dy * ny) + (dz * nz);

            var reflectedX = dx - (two * dot * nx);
            var reflectedY = dy - (two * dot * ny);
            var reflectedZ = dz - (two * dot * nz);
            Vector<double> outgoingX;
            Vector<double> outgoingY;
            Vector<double> outgoingZ;
            Vector<double> sinTransmittedSquared;
            if (forceReflection)
            {
                outgoingX = reflectedX;
                outgoingY = reflectedY;
                outgoingZ = reflectedZ;
                sinTransmittedSquared = new Vector<double>(double.PositiveInfinity);
            }
            else
            {
                var n1 = new Vector<double>(refractiveIndexBefore.Slice(index, width));
                var n2 = Vector.Max(
                    new Vector<double>(refractiveIndexAfter.Slice(index, width)),
                    epsilon);
                var eta = n1 / n2;
                var cosIncident = -dot;
                sinTransmittedSquared =
                    eta * eta * (one - (cosIncident * cosIncident));
                var totalInternalReflection =
                    Vector.GreaterThan(sinTransmittedSquared, one);
                var cosTransmitted =
                    Vector.SquareRoot(Vector.Max(zero, one - sinTransmittedSquared));
                var normalFactor = (eta * cosIncident) - cosTransmitted;
                var transmittedX = (eta * dx) + (normalFactor * nx);
                var transmittedY = (eta * dy) + (normalFactor * ny);
                var transmittedZ = (eta * dz) + (normalFactor * nz);
                outgoingX = Vector.ConditionalSelect(
                    totalInternalReflection,
                    reflectedX,
                    transmittedX);
                outgoingY = Vector.ConditionalSelect(
                    totalInternalReflection,
                    reflectedY,
                    transmittedY);
                outgoingZ = Vector.ConditionalSelect(
                    totalInternalReflection,
                    reflectedZ,
                    transmittedZ);
            }

            NormalizeVector(
                ref outgoingX,
                ref outgoingY,
                ref outgoingZ,
                epsilonSquared,
                zero,
                one);
            outgoingX.CopyTo(resultX.Slice(index, width));
            outgoingY.CopyTo(resultY.Slice(index, width));
            outgoingZ.CopyTo(resultZ.Slice(index, width));
            for (var lane = 0; lane < width; lane++)
            {
                interactionKinds[index + lane] = forceReflection
                    ? RayInteractionKind.Reflected
                    : sinTransmittedSquared[lane] > 1
                        ? RayInteractionKind.TotalInternalReflection
                        : RayInteractionKind.Transmitted;
            }
        }

        if (index < directionX.Length)
        {
            _scalarBatchAdapter.RefractOrReflect(
                directionX[index..],
                directionY[index..],
                directionZ[index..],
                normalX[index..],
                normalY[index..],
                normalZ[index..],
                refractiveIndexBefore[index..],
                refractiveIndexAfter[index..],
                forceReflection,
                resultX[index..],
                resultY[index..],
                resultZ[index..],
                interactionKinds[index..]);
        }
    }

    private static void NormalizeVector(
        ref Vector<double> x,
        ref Vector<double> y,
        ref Vector<double> z,
        Vector<double> epsilonSquared,
        Vector<double> zero,
        Vector<double> one)
    {
        var lengthSquared = (x * x) + (y * y) + (z * z);
        var valid = Vector.GreaterThan(lengthSquared, epsilonSquared);
        var inverseLength = one / Vector.SquareRoot(Vector.Max(lengthSquared, epsilonSquared));
        x = Vector.ConditionalSelect(valid, x * inverseLength, zero);
        y = Vector.ConditionalSelect(valid, y * inverseLength, zero);
        z = Vector.ConditionalSelect(valid, z * inverseLength, one);
    }
}
