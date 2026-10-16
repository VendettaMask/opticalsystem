namespace OpticalSystemDesign.Core.Services;

/// <summary>Exact solid angle for bins in atan2(direction.X, direction.Z), atan2(direction.Y, direction.Z).</summary>
public static class ProjectedAngleSolidAngle
{
    public static double RectangleDegrees(double minimumX, double maximumX, double minimumY, double maximumY)
    {
        if (!double.IsFinite(minimumX) || !double.IsFinite(maximumX)
            || !double.IsFinite(minimumY) || !double.IsFinite(maximumY)
            || minimumX > maximumX || minimumY > maximumY)
            throw new ArgumentOutOfRangeException(nameof(minimumX), "Projected-angle bounds must be finite and ordered.");

        // Positive Z occupies the central square. Negative Z occupies the four
        // corner squares, separated by the atan2 seams; mixed-sign cosines have no directions.
        return Patch(-90, 90, -90, 90)
            + Patch(-180, -90, -180, -90) + Patch(-180, -90, 90, 180)
            + Patch(90, 180, -180, -90) + Patch(90, 180, 90, 180);

        double Patch(double x0, double x1, double y0, double y1)
        {
            x0 = Math.Max(x0, minimumX);
            x1 = Math.Min(x1, maximumX);
            y0 = Math.Max(y0, minimumY);
            y1 = Math.Min(y1, maximumY);
            if (x0 >= x1 || y0 >= y1) return 0;
            return Math.Max(0, Primitive(x1, y1) - Primitive(x0, y1) - Primitive(x1, y0) + Primitive(x0, y0));
        }
    }

    private static double Primitive(double xDegrees, double yDegrees)
    {
        var (sx, cx) = Math.SinCos(xDegrees * Math.PI / 180);
        var (sy, cy) = Math.SinCos(yDegrees * Math.PI / 180);
        // The rectangular-cone antiderivative, written without tan overflow at ±90 degrees.
        return Math.Atan2(sx * sy, Math.Sqrt(cx * cx * cy * cy + sx * sx * cy * cy + cx * cx * sy * sy));
    }
}
