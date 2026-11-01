using System.Security.Cryptography;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Scattering;

public enum XyzAbsoluteRbfFeatures { LogXyz, ChromaticityLogStrength }
public sealed record XyzAbsoluteRbfNode(string SampleId, double X, double Y, double Z, IReadOnlyList<double> RadiometricValues);
/// <summary>Positive empirical absolute-XYZ transfer under fixed normalized observer/illuminant
/// conditions. Unlike the old colour-only model it is not intensity-homogeneous.</summary>
public sealed record XyzAbsoluteRbfModel(XyzAbsoluteRbfFeatures Features, int NeighbourCount, double Smoothing,
    double ResponseOffsetPerSteradian, IReadOnlyList<double> CoordinateScales, double MinimumNonzeroStrength, double MaximumStrength,
    IReadOnlyList<XyzAbsoluteRbfNode> Nodes, string Method = "PositiveAbsoluteLocalRbf/1")
{
    internal XyzAbsoluteRbfModel Freeze(int bands)
    {
        if (Method != "PositiveAbsoluteLocalRbf/1" || !Enum.IsDefined(Features) || NeighbourCount is < 4 or > 256 || !double.IsFinite(Smoothing) || Smoothing is < 1e-10 or > 1000
            || !double.IsFinite(ResponseOffsetPerSteradian) || ResponseOffsetPerSteradian is < 1e-12 or > 1
            || CoordinateScales is null || CoordinateScales.Count != 3 || CoordinateScales.Any(s => !double.IsFinite(s) || s is < 1e-15 or > 1e15)
            || !double.IsFinite(MinimumNonzeroStrength) || MinimumNonzeroStrength <= 0 || !double.IsFinite(MaximumStrength) || MaximumStrength < MinimumNonzeroStrength
            || bands is < 1 or > 32 || Nodes is null || Nodes.Count < NeighbourCount || Nodes.Count > 4096 || (long)Nodes.Count * bands > XyzRadiometricCalibrator.MaximumObservations
            || Nodes.Any(p => p is null || string.IsNullOrWhiteSpace(p.SampleId) || p.SampleId.Length > 256 || new[] {p.X,p.Y,p.Z}.Any(v => !double.IsFinite(v) || v is < 0 or > 1e12)
                || p.X + p.Y + p.Z == 0 || !ContainsStrength(p.X+p.Y+p.Z) || p.RadiometricValues is null || p.RadiometricValues.Count != bands || p.RadiometricValues.Any(v => !double.IsFinite(v) || v is < 0 or > 1e12))
            || Nodes.Select(p => p.SampleId).Distinct(StringComparer.Ordinal).Count() != Nodes.Count) throw new InvalidDataException("XYZ 绝对正值模型的参数、训练节点或强度域无效/超出预算。");
        return new(Features, NeighbourCount, Smoothing, ResponseOffsetPerSteradian, Array.AsReadOnly(CoordinateScales.ToArray()), MinimumNonzeroStrength, MaximumStrength,
            Array.AsReadOnly(Nodes.OrderBy(p=>p.SampleId,StringComparer.Ordinal).Select(p=>p with{RadiometricValues=Array.AsReadOnly(p.RadiometricValues.ToArray())}).ToArray()),Method);
    }
    private bool ContainsStrength(double strength) => strength >= MinimumNonzeroStrength * (1 - 64 * 2.2204460492503131e-16) && strength <= MaximumStrength * (1 + 64 * 2.2204460492503131e-16);
    internal static double[] RawFeatures(XyzAbsoluteRbfFeatures kind, double x, double y, double z)
    {
        var sum=x+y+z; return kind==XyzAbsoluteRbfFeatures.LogXyz ? [Math.Log(x+1e-12),Math.Log(y+1e-12),Math.Log(z+1e-12)] : [x/sum,y/sum,Math.Log(sum+1e-12)];
    }
    internal string Digest()
    {
        using var stream=new MemoryStream();using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
        {
            w.Write(Method);w.Write((int)Features);w.Write(NeighbourCount);w.Write(Smoothing);w.Write(ResponseOffsetPerSteradian);foreach(var s in CoordinateScales)w.Write(s);w.Write(MinimumNonzeroStrength);w.Write(MaximumStrength);w.Write(Nodes.Count);
            foreach(var p in Nodes){w.Write(p.SampleId);w.Write(p.X);w.Write(p.Y);w.Write(p.Z);w.Write(p.RadiometricValues.Count);foreach(var f in p.RadiometricValues)w.Write(f);}
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
    private static double Distance(double[] a,double[] b) {double d=0;for(int j=0;j<3;j++)d+=(a[j]-b[j])*(a[j]-b[j]);return d;}
    private static double Kernel(double distanceSquared) => distanceSquared==0?0:distanceSquared*Math.Log(distanceSquared)/2;
    internal double[] Evaluate(double x,double y,double z)
    {
        var sum=x+y+z;if(!double.IsFinite(sum)||x<0||y<0||z<0)throw new InvalidDataException("无效绝对XYZ输入。");if(sum==0)return new double[Nodes[0].RadiometricValues.Count];
        if(!ContainsStrength(sum))throw new InvalidDataException("绝对XYZ强度在声明域外；不能外推或按任意光强缩放。");
        double[] Coordinates(double a,double b,double c)=>RawFeatures(Features,a,b,c).Select((v,i)=>v/CoordinateScales[i]).ToArray();
        var query=Coordinates(x,y,z);var near=Nodes.Select(p=>{var f=Coordinates(p.X,p.Y,p.Z);return(Node:p,Features:f,Distance:Distance(f,query));}).OrderBy(p=>p.Distance).ThenBy(p=>p.Node.SampleId,StringComparer.Ordinal).Take(NeighbourCount).ToArray();
        var n=near.Length;var shift=Enumerable.Range(0,3).Select(j=>(near.Max(p=>p.Features[j])+near.Min(p=>p.Features[j]))/2).ToArray();
        var scale=Enumerable.Range(0,3).Select(j=>(near.Max(p=>p.Features[j])-near.Min(p=>p.Features[j]))/2).Select(s=>s==0?1:s).ToArray();
        // Deterministic rank-revealing affine span, with two orthogonalization passes.
        // Constant-colour local clouds need fewer polynomial columns, not fabricated rank.
        var polynomial=new List<double[]>();var queryPolynomial=new List<double>();var orthonormal=new List<double[]>();
        for(int column=0;column<4;column++)
        {
            var values=near.Select(p=>column==0?1:(p.Features[column-1]-shift[column-1])/scale[column-1]).ToArray();var q=(double[])values.Clone();var initial=values.Sum(v=>v*v);
            for(int pass=0;pass<2;pass++)foreach(var basis in orthonormal){double dot=0;for(int i=0;i<n;i++)dot+=q[i]*basis[i];for(int i=0;i<n;i++)q[i]-=dot*basis[i];}
            var norm=Math.Sqrt(q.Sum(v=>v*v));if(norm<=1e-12*Math.Sqrt(initial))continue;
            orthonormal.Add(q.Select(v=>v/norm).ToArray());polynomial.Add(values);queryPolynomial.Add(column==0?1:(query[column-1]-shift[column-1])/scale[column-1]);
        }
        var size=n+polynomial.Count;var matrix=new double[size,size];
        for(int i=0;i<n;i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();for(int j=0;j<=i;j++)matrix[i,j]=matrix[j,i]=Kernel(Distance(near[i].Features,near[j].Features));matrix[i,i]+=Smoothing;
            for(int j=0;j<polynomial.Count;j++)matrix[i,n+j]=matrix[n+j,i]=polynomial[j][i];
        }
        var result=new double[Nodes[0].RadiometricValues.Count];
        for(int band=0;band<result.Length;band++)
        {
            var target=new double[size];for(int i=0;i<n;i++)target[i]=Math.Log(near[i].Node.RadiometricValues[band]+ResponseOffsetPerSteradian);
            var weights=PivotedLinearSystem.Solve(matrix,target);double value=0;for(int i=0;i<n;i++)value+=weights[i]*Kernel(near[i].Distance);for(int j=0;j<polynomial.Count;j++)value+=weights[n+j]*queryPolynomial[j];
            result[band]=Math.Exp(value);if(!double.IsFinite(result[band]))throw new InvalidDataException("绝对XYZ正值模型预测溢出。");
        }
        return result;
    }
}
