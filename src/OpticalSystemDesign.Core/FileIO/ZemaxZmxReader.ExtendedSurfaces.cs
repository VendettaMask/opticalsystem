using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Core.FileIO;

internal static partial class ZemaxZmxReader
{
    private static bool IsExtendedPolynomial(string type) => type is "XASPHERE" or "XOSPHERE" or "XPOLYNOM";

    private static IGeometry CreateExtendedPolynomial(ZemaxSurface surface, double radius)
    {
        if (surface.Parameters.Any(parameter => parameter.Value != 0))
            return CreateOpaqueZemaxGeometry(surface);
        var basis = surface.Type switch
        {
            "XASPHERE" => SagPolynomialBasis.EvenRadial,
            "XOSPHERE" => SagPolynomialBasis.Radial,
            _ => SagPolynomialBasis.Cartesian
        };
        var maximum = basis == SagPolynomialBasis.Cartesian ? 230 : 240;
        var count = surface.ExtraData.GetValueOrDefault(1);
        if (count != Math.Truncate(count) || count < 0 || count > maximum
            || surface.ExtraData.Keys.Any(index => index > maximum + 2))
            throw new InvalidDataException("Extended surface term count or coefficient index is invalid.");
        var storedCount = Math.Max((int)count, surface.ExtraData.Keys.DefaultIfEmpty(2).Max() - 2);
        return new NormalizedPolynomialGeometry(radius, surface.Conic,
            surface.ExtraData.GetValueOrDefault(2, 1), basis, (int)count,
            Enumerable.Range(3, storedCount).Select(index => surface.ExtraData.GetValueOrDefault(index)).ToArray());
    }

    private static IGeometry CreateBiconic(ZemaxSurface surface)
    {
        if (surface.Parameters.Any(parameter => parameter.Key is not (0 or 1) && parameter.Value != 0)
            || surface.ExtraData.Any(parameter => parameter.Value != 0))
            return CreateOpaqueZemaxGeometry(surface);
        var radiusX = surface.Parameter(0);
        return new BiconicGeometry(radiusX == 0 ? double.PositiveInfinity : radiusX,
            surface.Radius, surface.Parameter(1), surface.Conic);
    }
}
