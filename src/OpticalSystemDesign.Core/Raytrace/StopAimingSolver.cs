using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Raytrace;

internal interface IStopMap
{
    bool Sample(double x,double y,out Vector3D point);
}

internal static class StopAimingSolver
{
    internal static double Hypot(double x,double y)
    {
        var a=Math.Abs(x); var b=Math.Abs(y); var hi=Math.Max(a,b); var lo=Math.Min(a,b);
        if (double.IsInfinity(hi)) return double.PositiveInfinity;
        return hi == 0 ? 0 : hi*Math.Sqrt(1+(lo/hi)*(lo/hi));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static bool Solve<T>(ref T map,double targetX,double targetY,double tolerance,
        ref double x,ref double y,out double residual) where T:struct,IStopMap
    {
        residual=double.PositiveInfinity;
        for (var iteration=0;iteration<24;iteration++)
        {
            if (!map.Sample(x,y,out var point)) return false;
            var ex=targetX-point.X; var ey=targetY-point.Y;
            residual=Hypot(ex,ey);
            if (residual <= tolerance) return true;
            var hx=1e-6*Math.Max(1,Math.Abs(x)); var hy=1e-6*Math.Max(1,Math.Abs(y));
            if (!map.Sample(x+hx,y,out var px)) { hx=-hx; if (!map.Sample(x+hx,y,out px)) return false; }
            if (!map.Sample(x,y+hy,out var py)) { hy=-hy; if (!map.Sample(x,y+hy,out py)) return false; }
            var a=(px.X-point.X)/hx; var b=(py.X-point.X)/hy;
            var c=(px.Y-point.Y)/hx; var d=(py.Y-point.Y)/hy;
            var determinant=a*d-b*c;
            if (!double.IsFinite(determinant) || Math.Abs(determinant)<=1e-24) return false;
            var dx=(d*ex-b*ey)/determinant; var dy=(a*ey-c*ex)/determinant;
            var improved=false; var scale=1d;
            for (var backtrack=0;backtrack<12;backtrack++,scale*=.5)
            {
                var nextX=x+scale*dx; var nextY=y+scale*dy;
                if (!map.Sample(nextX,nextY,out var next) || Hypot(targetX-next.X,targetY-next.Y)>=residual) continue;
                x=nextX; y=nextY; improved=true; break;
            }
            if (!improved) return false;
        }
        return false;
    }
}
