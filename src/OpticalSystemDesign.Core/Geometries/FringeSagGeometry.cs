using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Geometries;

/// <summary>Conic/even-asphere sag plus decentered, unnormalized Fringe Zernike sag terms.</summary>
public sealed class FringeSagGeometry : IGeometry
{
    public FringeSagGeometry(double radius, double conic, IReadOnlyList<double> asphereCoefficients,
        double normalizationRadius, IReadOnlyList<double> fringeCoefficients,
        double decenterX = 0, double decenterY = 0, bool extrapolate = false)
    {
        if (!double.IsFinite(normalizationRadius) || normalizationRadius <= 0)
            throw new ArgumentOutOfRangeException(nameof(normalizationRadius));
        if (asphereCoefficients.Count > 8 || fringeCoefficients.Count > 37
            || asphereCoefficients.Any(v => !double.IsFinite(v)) || fringeCoefficients.Any(v => !double.IsFinite(v))
            || !double.IsFinite(decenterX) || !double.IsFinite(decenterY))
            throw new ArgumentException("Invalid Fringe sag coefficients or decenter.");
        Base = new EvenAsphereGeometry(radius, conic, asphereCoefficients);
        NormalizationRadius = normalizationRadius; DecenterX = decenterX; DecenterY = decenterY; Extrapolate = extrapolate;
        FringeCoefficients = Array.AsReadOnly(fringeCoefficients.ToArray());
    }
    public string Kind => "fringe_sag";
    public EvenAsphereGeometry Base { get; }
    public double NormalizationRadius { get; }
    public IReadOnlyList<double> FringeCoefficients { get; }
    public double DecenterX { get; }
    public double DecenterY { get; }
    public bool Extrapolate { get; }
    public double Sag(double x, double y)
    {
        var sag = Base.Sag(x, y);
        var u = (x - DecenterX) / NormalizationRadius; var v = (y - DecenterY) / NormalizationRadius;
        if (!Extrapolate && u * u + v * v > 1) return sag;
        for (var i = 0; i < FringeCoefficients.Count; i++)
        {
            if (FringeCoefficients[i] == 0) continue;
            sag += FringeCoefficients[i] * FringeZernikeBasis.Evaluate(i, u, v).Value;
        }
        return sag;
    }
    public Vector3D SurfaceNormal(Vector3D point)
    {
        var normal = Base.SurfaceNormal(point);
        var u=(point.X-DecenterX)/NormalizationRadius;var v=(point.Y-DecenterY)/NormalizationRadius;
        if (!Extrapolate && u*u+v*v>1) return normal;
        var sx=-normal.X/normal.Z;var sy=-normal.Y/normal.Z;
        for(var i=0;i<FringeCoefficients.Count;i++)
        {
            if(FringeCoefficients[i]==0) continue;
            var (_,dx,dy)=FringeZernikeBasis.Evaluate(i,u,v);
            sx+=FringeCoefficients[i]*dx/NormalizationRadius;sy+=FringeCoefficients[i]*dy/NormalizationRadius;
        }
        var result=new Vector3D(-sx,-sy,1);return result/result.Length;
    }
    public IntersectionResult DistanceToIntersection(Vector3D origin, Vector3D direction) =>
        StandardGeometry.NewtonSolveDistance(origin, direction, Sag, SurfaceNormal);
    public IGeometry Clone() => new FringeSagGeometry(Base.Base.Radius, Base.Base.Conic, Base.Coefficients,
        NormalizationRadius, FringeCoefficients, DecenterX, DecenterY, Extrapolate);
}

internal static class FringeZernikeBasis
{
    internal static readonly IReadOnlyList<(int N, int M)> Orders = Array.AsReadOnly(new (int N, int M)[]
    {
        (0,0), (1,1),(1,-1), (2,0),(2,2),(2,-2), (3,1),(3,-1), (4,0), (3,3),(3,-3),
        (4,2),(4,-2), (5,1),(5,-1), (6,0), (4,4),(4,-4), (5,3),(5,-3), (6,2),(6,-2),
        (7,1),(7,-1), (8,0), (5,5),(5,-5), (6,4),(6,-4), (7,3),(7,-3), (8,2),(8,-2),
        (9,1),(9,-1), (10,0), (12,0)
    });
    // Cartesian polynomial derivatives remain well defined at the pupil center.
    internal static (double Value,double Dx,double Dy) Evaluate(int index,double x,double y)
    {
        var (n,signedM)=Orders[index];var m=Math.Abs(signedM);
        var re=1.0;var im=0.0;var previousRe=1.0;var previousIm=0.0;
        for(var i=0;i<m;i++)
        {
            previousRe=re;previousIm=im;(re,im)=(re*x-im*y,re*y+im*x);
        }
        var angular=signedM<0?im:re;
        var ax=m==0?0:m*(signedM<0?previousIm:previousRe);
        var ay=m==0?0:m*(signedM<0?previousRe:-previousIm);
        var r2=x*x+y*y;var radial=0.0;var derivative=0.0;
        for(var k=0;k<=(n-m)/2;k++)
        {
            var p=(n-m)/2-k;
            var c=(k%2==0?1:-1)*Factorial(n-k)/(Factorial(k)*Factorial((n+m)/2-k)*Factorial(p));
            radial+=c*Math.Pow(r2,p);
            if(p>0) derivative+=c*p*Math.Pow(r2,p-1);
        }
        return (radial*angular,2*x*derivative*angular+radial*ax,2*y*derivative*angular+radial*ay);
    }
    private static double Factorial(int n){var value=1.0;for(var i=2;i<=n;i++)value*=i;return value;}
}
