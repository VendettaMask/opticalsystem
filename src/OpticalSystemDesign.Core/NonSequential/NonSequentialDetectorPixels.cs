namespace OpticalSystemDesign.Core.NonSequential;

internal static class NonSequentialDetectorPixels
{
    internal static bool TryIndex(double coordinate, double extent, int count, out int index)
    {
        index = -1;
        if (!double.IsFinite(coordinate) || coordinate < -extent / 2 || coordinate > extent / 2) return false;
        // The physical rectangle includes both edges. The positive edge belongs
        // to the last pixel, rather than a nonexistent pixel numbered count.
        index = Math.Clamp((int)Math.Floor((coordinate / extent + 0.5) * count), 0, count - 1);
        return true;
    }
}
