using System.Buffers;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Raytrace;

internal sealed class PooledRayStateBuffer : IDisposable
{
    private readonly int _length;
    private bool _disposed;

    public PooledRayStateBuffer(int length)
    {
        _length = length;
        OriginX = RentDoubles(length);
        OriginY = RentDoubles(length);
        OriginZ = RentDoubles(length);
        DirectionX = RentDoubles(length);
        DirectionY = RentDoubles(length);
        DirectionZ = RentDoubles(length);
        Wavelength = RentDoubles(length);
        Intensity = RentDoubles(length);
        OpticalPathDifference = RentDoubles(length);
        CumulativePath = RentDoubles(length);
        CumulativeOpticalPath = RentDoubles(length);
        Polarization = ArrayPool<Matrix3x3?>.Shared.Rent(Math.Max(1, length));
        Materials = ArrayPool<IMaterial>.Shared.Rent(Math.Max(1, length));
        Active = ArrayPool<bool>.Shared.Rent(Math.Max(1, length));
        Normalized = ArrayPool<bool>.Shared.Rent(Math.Max(1, length));
        Array.Clear(CumulativePath, 0, length);
        Array.Clear(CumulativeOpticalPath, 0, length);
        Array.Clear(Polarization, 0, length);
        Array.Clear(Materials, 0, length);
        Array.Fill(Active, true, 0, length);
        Array.Clear(Normalized, 0, length);
    }

    public double[] OriginX { get; }
    public double[] OriginY { get; }
    public double[] OriginZ { get; }
    public double[] DirectionX { get; }
    public double[] DirectionY { get; }
    public double[] DirectionZ { get; }
    public double[] Wavelength { get; }
    public double[] Intensity { get; }
    public double[] OpticalPathDifference { get; }
    public double[] CumulativePath { get; }
    public double[] CumulativeOpticalPath { get; }
    public Matrix3x3?[] Polarization { get; }
    public IMaterial[] Materials { get; }
    public bool[] Active { get; }
    public bool[] Normalized { get; }

    public RayState this[int index]
    {
        get => new(
            new Vector3D(OriginX[index], OriginY[index], OriginZ[index]),
            new Vector3D(DirectionX[index], DirectionY[index], DirectionZ[index]),
            Wavelength[index],
            Intensity[index],
            OpticalPathDifference[index],
            Polarization[index],
            Normalized[index]);
        set
        {
            OriginX[index] = value.Origin.X;
            OriginY[index] = value.Origin.Y;
            OriginZ[index] = value.Origin.Z;
            DirectionX[index] = value.Direction.X;
            DirectionY[index] = value.Direction.Y;
            DirectionZ[index] = value.Direction.Z;
            Wavelength[index] = value.WavelengthNanometers;
            Intensity[index] = value.Intensity;
            OpticalPathDifference[index] = value.OpticalPathDifference;
            Polarization[index] = value.PolarizationMatrix;
            Normalized[index] = value.IsNormalized;
        }
    }

    public void Initialize(
        RealRayBundle bundle,
        PooledDirectionBatch initialDirections,
        IMaterial ambientMaterial)
    {
        for (var index = 0; index < _length; index++)
        {
            var source = bundle.Rays[index];
            var direction = initialDirections[index];
            OriginX[index] = source.Origin.X;
            OriginY[index] = source.Origin.Y;
            OriginZ[index] = source.Origin.Z;
            DirectionX[index] = direction.X;
            DirectionY[index] = direction.Y;
            DirectionZ[index] = direction.Z;
            Wavelength[index] = source.WavelengthNanometers;
            Intensity[index] = source.Intensity;
            OpticalPathDifference[index] = source.OpticalPathDifference;
            Polarization[index] = source.PolarizationMatrix;
            Materials[index] = ambientMaterial;
            Normalized[index] = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReturnDoubles(OriginX);
        ReturnDoubles(OriginY);
        ReturnDoubles(OriginZ);
        ReturnDoubles(DirectionX);
        ReturnDoubles(DirectionY);
        ReturnDoubles(DirectionZ);
        ReturnDoubles(Wavelength);
        ReturnDoubles(Intensity);
        ReturnDoubles(OpticalPathDifference);
        ReturnDoubles(CumulativePath);
        ReturnDoubles(CumulativeOpticalPath);
        ArrayPool<Matrix3x3?>.Shared.Return(Polarization, clearArray: true);
        ArrayPool<IMaterial>.Shared.Return(Materials, clearArray: true);
        ArrayPool<bool>.Shared.Return(Active, clearArray: true);
        ArrayPool<bool>.Shared.Return(Normalized, clearArray: true);
    }

    private static double[] RentDoubles(int length) =>
        ArrayPool<double>.Shared.Rent(Math.Max(1, length));

    private static void ReturnDoubles(double[] values) =>
        ArrayPool<double>.Shared.Return(values, clearArray: true);
}
