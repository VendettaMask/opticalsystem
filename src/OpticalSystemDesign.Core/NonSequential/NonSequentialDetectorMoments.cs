namespace OpticalSystemDesign.Core.NonSequential;

/// <summary>Exact ray-intercept moments in detector-local mm, before pixel binning.</summary>
public sealed record NonSequentialDetectorMoments(double PowerWatts, long Hits, double CentroidXMillimeters,
    double CentroidYMillimeters, double RmsXMillimeters, double RmsYMillimeters, double RmsRadiusMillimeters);

internal sealed class NonSequentialMomentAccumulator
{
    private double _power, _x, _y, _xx, _yy; private long _hits;
    internal void Add(double x, double y, double power)
    {
        _hits++; if (power <= 0) return;
        var total = _power + power; var dx = x - _x; var dy = y - _y; var fraction = power / total;
        var nextX = _x + fraction * dx; var nextY = _y + fraction * dy;
        _xx += power * dx * (x - nextX); _yy += power * dy * (y - nextY);
        _x = nextX; _y = nextY; _power = total;
    }
    internal NonSequentialDetectorMoments Snapshot() => new(_power, _hits, _x, _y,
        _power > 0 ? Math.Sqrt(Math.Max(0, _xx / _power)) : 0,
        _power > 0 ? Math.Sqrt(Math.Max(0, _yy / _power)) : 0,
        _power > 0 ? Math.Sqrt(Math.Max(0, (_xx + _yy) / _power)) : 0);
}
