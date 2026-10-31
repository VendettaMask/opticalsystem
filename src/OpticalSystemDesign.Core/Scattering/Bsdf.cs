using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Scattering;

public enum BsdfLobe { DiffuseReflection, DiffuseTransmission }
public enum BsdfSide { Front, Back }
public readonly record struct BsdfEnergy(double Reflection, double Transmission)
{
    public double Total => Reflection + Transmission;
}

/// <summary>World directions point AWAY from the interaction. Normal points into the incident hemisphere.</summary>
public sealed class BsdfContext
{
    public Vector3D IncomingDirection { get; }
    public Vector3D SurfaceNormal { get; }
    public double WavelengthNanometers { get; }
    public BsdfSide Side { get; }
    public Vector3D? MaterialTangent { get; }
    public double IncidentRefractiveIndex { get; }
    public double TransmittedRefractiveIndex { get; }
    public BsdfContext(Vector3D incomingDirection, Vector3D surfaceNormal, double wavelengthNanometers, BsdfSide side = BsdfSide.Front,
        Vector3D? materialXAxis = null, double incidentRefractiveIndex = 1, double transmittedRefractiveIndex = 1)
    {
        IncomingDirection = Unit(incomingDirection); SurfaceNormal = Unit(surfaceNormal);
        if (!double.IsFinite(wavelengthNanometers) || wavelengthNanometers <= 0 || Dot(IncomingDirection, SurfaceNormal) < 0)
            throw new ArgumentException("BSDF requires a positive wavelength and an incident-facing normal.");
        if (!Enum.IsDefined(side)) throw new ArgumentOutOfRangeException(nameof(side));
        WavelengthNanometers = wavelengthNanometers; Side = side;
        if (!double.IsFinite(incidentRefractiveIndex) || incidentRefractiveIndex <= 0 || !double.IsFinite(transmittedRefractiveIndex) || transmittedRefractiveIndex <= 0)
            throw new ArgumentException("BSDF refractive indices must be finite and positive.");
        IncidentRefractiveIndex = incidentRefractiveIndex; TransmittedRefractiveIndex = transmittedRefractiveIndex;
        if (materialXAxis is { } x) MaterialTangent = Unit(x - SurfaceNormal * Dot(x, SurfaceNormal));
    }
    internal static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    internal static Vector3D Cross(Vector3D a, Vector3D b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    internal static Vector3D Unit(Vector3D v)
    {
        var length = v.Length;
        if (!double.IsFinite(length) || length <= 0) throw new ArgumentException("BSDF directions must be finite and nonzero.");
        return v / length;
    }
}

/// <summary>Value and Pdf are per steradian, sampled with respect to solid angle. No pixel/area/exposure factor.</summary>
public readonly record struct BsdfSample(Vector3D Direction, double Value, double Pdf, BsdfLobe Lobe);

public interface IBsdf
{
    string Kind { get; }
    BsdfEnergy Integrate(BsdfContext context);
    double Evaluate(BsdfContext context, Vector3D outgoingDirection);
    double Pdf(BsdfContext context, Vector3D outgoingDirection);
    BsdfSample Sample(BsdfContext context, double u1, double u2);
}

/// <summary>Opaque diffuse reflection: f=rho/pi; cosine-hemisphere PDF; remaining power is absorbed.</summary>
public sealed class LambertianReflectionBsdf : IBsdf
{
    public string Kind => "LambertianReflection/1";
    public double Reflectance { get; }
    public BsdfEnergy Integrate(BsdfContext context) { ArgumentNullException.ThrowIfNull(context); return new(Reflectance, 0); }
    public LambertianReflectionBsdf(double reflectance)
    {
        if (!double.IsFinite(reflectance) || reflectance is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(reflectance));
        Reflectance = reflectance;
    }
    public double Evaluate(BsdfContext context, Vector3D outgoingDirection)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BsdfContext.Dot(context.SurfaceNormal, BsdfContext.Unit(outgoingDirection)) > 0 ? Reflectance / Math.PI : 0;
    }
    public double Pdf(BsdfContext context, Vector3D outgoingDirection)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Math.Max(0, BsdfContext.Dot(context.SurfaceNormal, BsdfContext.Unit(outgoingDirection))) / Math.PI;
    }
    public BsdfSample Sample(BsdfContext context, double u1, double u2)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!double.IsFinite(u1) || !double.IsFinite(u2) || u1 is < 0 or >= 1 || u2 is < 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(u1), "BSDF samples require independent values in [0,1).");
        var n = context.SurfaceNormal;
        var tangent = BsdfContext.Unit(BsdfContext.Cross(Math.Abs(n.Z) < .9 ? new(0, 0, 1) : new Vector3D(0, 1, 0), n));
        var bitangent = BsdfContext.Cross(n, tangent);
        var radius = Math.Sqrt(u1); var phi = 2 * Math.PI * u2;
        var direction = BsdfContext.Unit(tangent * (radius * Math.Cos(phi)) + bitangent * (radius * Math.Sin(phi)) + n * Math.Sqrt(1 - u1));
        return new(direction, Evaluate(context, direction), Pdf(context, direction), BsdfLobe.DiffuseReflection);
    }
}
