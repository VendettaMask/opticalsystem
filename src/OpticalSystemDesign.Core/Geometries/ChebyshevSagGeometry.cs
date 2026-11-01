using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Geometries;

/// <summary>Spherical base plus normalized Chebyshev products, referenced to the vertex.</summary>
public sealed class ChebyshevSagGeometry : IGeometry
{
    public ChebyshevSagGeometry(double radius, int maximumXOrder, int maximumYOrder,
        double normalizationX, double normalizationY, IReadOnlyList<double> normalizedCoefficients)
    {
        if (maximumXOrder is < 0 or > 14 || maximumYOrder is < 0 or > 14
            || normalizedCoefficients.Count != (maximumXOrder + 1) * (maximumYOrder + 1) - 1)
            throw new ArgumentException("Chebyshev orders and coefficient count differ.");
        if (!double.IsFinite(normalizationX) || normalizationX <= 0 || !double.IsFinite(normalizationY) || normalizationY <= 0
            || normalizedCoefficients.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("Invalid Chebyshev normalization or coefficients.");
        Base = new StandardGeometry(radius); MaximumXOrder = maximumXOrder; MaximumYOrder = maximumYOrder;
        NormalizationX = normalizationX; NormalizationY = normalizationY;
        NormalizedCoefficients = Array.AsReadOnly(normalizedCoefficients.ToArray());
    }
    public string Kind => "chebyshev_sag";
    public StandardGeometry Base { get; }
    public int MaximumXOrder { get; }
    public int MaximumYOrder { get; }
    public double NormalizationX { get; }
    public double NormalizationY { get; }
    public IReadOnlyList<double> NormalizedCoefficients { get; }
    private bool InDomain(double x, double y) => Math.Abs(x) <= NormalizationX && Math.Abs(y) <= NormalizationY;
    public double Sag(double x, double y)
    {
        if (!InDomain(x,y)) return double.NaN;
        Span<double> tx=stackalloc double[15]; Span<double> ty=stackalloc double[15];
        Span<double> dx=stackalloc double[15]; Span<double> dy=stackalloc double[15];
        Fill(x/NormalizationX,tx,dx); Fill(y/NormalizationY,ty,dy);
        var sum=0.0;var index=0;
        for(var j=0;j<=MaximumYOrder;j++) for(var i=0;i<=MaximumXOrder;i++)
        {
            if(i==0&&j==0) continue;
            // The native surface has no independent piston coefficient.
            var vertex=(i%2==0 && j%2==0)?((i/2+j/2)%2==0?1:-1):0;
            sum+=NormalizedCoefficients[index++]*(tx[i]*ty[j]-vertex);
        }
        return Base.Sag(x,y)+NormalizationX*NormalizationY*sum;
    }
    public Vector3D SurfaceNormal(Vector3D point)
    {
        if (!InDomain(point.X,point.Y)) return new(double.NaN,double.NaN,double.NaN);
        var bn=Base.SurfaceNormal(point);var sx=-bn.X/bn.Z;var sy=-bn.Y/bn.Z;
        Span<double> tx=stackalloc double[15]; Span<double> ty=stackalloc double[15];
        Span<double> dx=stackalloc double[15]; Span<double> dy=stackalloc double[15];
        Fill(point.X/NormalizationX,tx,dx); Fill(point.Y/NormalizationY,ty,dy);
        var index=0;
        for(var j=0;j<=MaximumYOrder;j++) for(var i=0;i<=MaximumXOrder;i++)
        {
            if(i==0&&j==0) continue;
            var c=NormalizedCoefficients[index++];
            sx+=NormalizationY*c*dx[i]*ty[j];sy+=NormalizationX*c*tx[i]*dy[j];
        }
        var n=new Vector3D(-sx,-sy,1);return n/n.Length;
    }
    private static void Fill(double x, Span<double> t, Span<double> d)
    {
        t[0]=1;d[0]=0;t[1]=x;d[1]=1;
        for(var i=2;i<15;i++){t[i]=2*x*t[i-1]-t[i-2];d[i]=2*t[i-1]+2*x*d[i-1]-d[i-2];}
    }
    public IntersectionResult DistanceToIntersection(Vector3D origin,Vector3D direction)=>
        StandardGeometry.NewtonSolveDistance(origin,direction,Sag,SurfaceNormal);
    public IGeometry Clone()=>new ChebyshevSagGeometry(Base.Radius,MaximumXOrder,MaximumYOrder,
        NormalizationX,NormalizationY,NormalizedCoefficients);
}
