using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Core.NonSequential;

/// <summary>One shared oriented boundary. Materials and coating are embedded, not catalogue-name guesses.</summary>
public sealed class NonSequentialOpticalInterface
{
    private readonly ComponentSnapshot _before, _after, _coating;
    private readonly ComponentSnapshot? _aperture;
    public int SourceSurfaceNumber { get; }
    public double VertexZMillimeters { get; }
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public double RadiusMillimeters { get; }
    public double Conic { get; }
    public double SemiDiameterMillimeters { get; }
    public double MechanicalSemiDiameterMillimeters { get; }
    public bool IsStop { get; }
    public double RollDegrees { get; }
    public string SourceLabel { get; }
    [JsonIgnore] public string MaterialBeforeName => Before.Name;
    [JsonIgnore] public string MaterialAfterName => After.Name;
    public ComponentSnapshot MaterialBefore => Copy(_before)!;
    public ComponentSnapshot MaterialAfter => Copy(_after)!;
    public ComponentSnapshot Coating => Copy(_coating)!;
    public ComponentSnapshot? Aperture => Copy(_aperture);
    [JsonIgnore] internal IMaterial Before { get; }
    [JsonIgnore] internal IMaterial After { get; }
    [JsonIgnore] internal ICoatingModel Film { get; }
    [JsonIgnore] internal IPhysicalAperture? ClearAperture { get; }
    [JsonIgnore] internal StandardGeometry Geometry { get; }
    [JsonIgnore] internal double EdgeZ => VertexZMillimeters + Geometry.Sag(MechanicalSemiDiameterMillimeters, 0);
    [JsonIgnore] internal int PayloadEntries { get; }
    [JsonIgnore] internal long PayloadCharacters { get; }

    [JsonConstructor]
    public NonSequentialOpticalInterface(int sourceSurfaceNumber, double vertexZMillimeters,
        double radiusMillimeters, double conic, double semiDiameterMillimeters, double mechanicalSemiDiameterMillimeters,
        ComponentSnapshot materialBefore, ComponentSnapshot materialAfter, ComponentSnapshot coating,
        ComponentSnapshot? aperture = null, bool isStop = false, double rollDegrees = 0, string sourceLabel = "")
    {
        if(sourceSurfaceNumber<1 || !double.IsFinite(vertexZMillimeters) || double.IsNaN(radiusMillimeters)
            || !double.IsFinite(conic) || !double.IsFinite(semiDiameterMillimeters) || semiDiameterMillimeters<=0
            || !double.IsFinite(mechanicalSemiDiameterMillimeters) || mechanicalSemiDiameterMillimeters<semiDiameterMillimeters
            || mechanicalSemiDiameterMillimeters>1e6 || !double.IsFinite(rollDegrees) || sourceLabel is null || sourceLabel.Length>4096)
            throw new InvalidDataException("复合镜组界面的编号、位置、曲率或独立口径无效。");
        SourceSurfaceNumber=sourceSurfaceNumber;VertexZMillimeters=vertexZMillimeters;RadiusMillimeters=radiusMillimeters;
        Conic=conic;SemiDiameterMillimeters=semiDiameterMillimeters;MechanicalSemiDiameterMillimeters=mechanicalSemiDiameterMillimeters;IsStop=isStop;
        RollDegrees=rollDegrees;SourceLabel=sourceLabel;
        var budget=new PayloadBudget();
        _before=CopyCore(materialBefore,0,budget)??throw new InvalidDataException("Missing incident material.");
        _after=CopyCore(materialAfter,0,budget)??throw new InvalidDataException("Missing transmitted material.");
        _coating=CopyCore(coating,0,budget)??throw new InvalidDataException("Missing coating declaration.");_aperture=CopyCore(aperture,0,budget);
        PayloadEntries=budget.Entries;PayloadCharacters=budget.Characters;
        Before=RestoreMaterial(_before);After=RestoreMaterial(_after);
        if(_coating.Kind is not ("none" or "simple" or "coherent_multilayer"))throw new NotSupportedException("复合镜组只接受明确的无膜、R/T 常量膜或物理相干多层膜；经验镀膜不能升级为物理分裂。");
        Film=ComponentSnapshotFactory.ToCoating(_coating);
        if(Film is SimpleCoatingModel simple && (!double.IsFinite(simple.Transmittance)||!double.IsFinite(simple.Reflectance)
            ||simple.Transmittance<0||simple.Reflectance<0||simple.Transmittance+simple.Reflectance>1))throw new InvalidDataException("逐面常量膜必须满足非负 R/T 和 R+T≤1。");
        RequireCanonical(_coating,ComponentSnapshotFactory.FromCoating(Film));
        ClearAperture=_aperture is null?null:ComponentSnapshotFactory.ToAperture(_aperture,semiDiameterMillimeters);
        RequireCanonical(_aperture,ComponentSnapshotFactory.FromAperture(ClearAperture));
        Geometry=new StandardGeometry(radiusMillimeters,conic);
        if(!double.IsFinite(EdgeZ))throw new InvalidDataException("机械口径超过曲面实数定义域。");
    }
    internal bool PassesAperture(Vector3D point)
    {
        var angle=RollDegrees*Math.PI/180;var c=Math.Cos(angle);var s=Math.Sin(angle);
        return ClearAperture is null || ClearAperture.Contains(new(c*point.X+s*point.Y,-s*point.X+c*point.Y,point.Z));
    }
    private static IMaterial RestoreMaterial(ComponentSnapshot snapshot)
    {
        if(snapshot.Kind is "catalog" or "unresolved" or "gradient_index" or "uniaxial")throw new NotSupportedException("复合镜组需要完整、已解析的均匀各向同性材料。");
        var material=ComponentSnapshotFactory.ToMaterial(snapshot,"",new MaterialRegistry());
        if(material.PropagationModel is not HomogeneousPropagationModel)throw new NotSupportedException("复合镜组不支持近似或非均匀介质传播。");
        RequireCanonical(snapshot,ComponentSnapshotFactory.FromMaterial(material));return material;
    }
    internal static string Identity(ComponentSnapshot value)=>JsonSerializer.Serialize(Copy(value));
    private static void RequireCanonical(ComponentSnapshot? input,ComponentSnapshot? restored)
    {if(JsonSerializer.Serialize(Copy(input))!=JsonSerializer.Serialize(Copy(restored)))throw new InvalidDataException("光学组件包含未知、缺失或被隐式更改的数据。");}
    private sealed class PayloadBudget { public int Nodes,Entries; public long Characters; }
    internal static ComponentSnapshot? Copy(ComponentSnapshot? value)=>CopyCore(value,0,new PayloadBudget());
    private static ComponentSnapshot? CopyCore(ComponentSnapshot? value,int depth,PayloadBudget budget)
    {
        if(value is null)return null;
        if(depth>8||string.IsNullOrWhiteSpace(value.Kind)||value.Kind.Length>128||value.Numbers is null||value.Text is null
            ||value.Numbers.Count>16384||value.Text.Count>4096||value.Children?.Count>1024
            ||value.Numbers.Any(p=>p.Key.Length>256||!double.IsFinite(p.Value))||value.Text.Any(p=>p.Key.Length>256||p.Value is null||p.Value.Length>8192)
            ||value.Children?.Any(p=>p.Key.Length>256||p.Value is null)==true)throw new InvalidDataException("复合镜组组件载荷无效或超出预算。");
        budget.Nodes++;
        budget.Entries+=value.Numbers.Count+value.Text.Count+(value.Children?.Count??0);
        budget.Characters+=value.Kind.Length+value.Numbers.Sum(p=>(long)p.Key.Length)+value.Text.Sum(p=>(long)p.Key.Length+p.Value.Length)+(value.Children?.Sum(p=>(long)p.Key.Length)??0);
        if(budget.Nodes>4096||budget.Entries>32768||budget.Characters>2*1024*1024)throw new InvalidDataException("复合镜组逐面组件载荷超出总预算。");
        return new(value.Kind,value.Numbers.OrderBy(p=>p.Key,StringComparer.Ordinal).ToDictionary(p=>p.Key,p=>p.Value),
            value.Text.OrderBy(p=>p.Key,StringComparer.Ordinal).ToDictionary(p=>p.Key,p=>p.Value),value.Children?.OrderBy(p=>p.Key,StringComparer.Ordinal).ToDictionary(p=>p.Key,p=>CopyCore(p.Value,depth+1,budget)!));
    }
}

/// <summary>Coaxial boundary representation with one interface per original surface and tapered mechanical rims.</summary>
public sealed record OpticalAssemblyParameters : NonSequentialObjectParameters
{
    private readonly bool[] _ambientRegions;
    public const string Protocol="OpticalAssembly/1";
    public IReadOnlyList<NonSequentialOpticalInterface> Interfaces { get; }
    [JsonIgnore] public double MaximumRadius => Interfaces.Max(f=>f.MechanicalSemiDiameterMillimeters);
    [JsonIgnore] public double MinimumZ => Interfaces.Min(f=>Math.Min(f.VertexZMillimeters,f.EdgeZ));
    [JsonIgnore] public double MaximumZ => Interfaces.Max(f=>Math.Max(f.VertexZMillimeters,f.EdgeZ));
    [JsonIgnore] internal IMaterial Ambient => Interfaces[0].Before;
    [JsonConstructor]
    public OpticalAssemblyParameters(IReadOnlyList<NonSequentialOpticalInterface> interfaces)
    {
        if(interfaces is null||interfaces.Count is <2 or >512||interfaces.Any(f=>f is null)
            ||interfaces.Select(f=>f.SourceSurfaceNumber).Distinct().Count()!=interfaces.Count)throw new InvalidDataException("复合镜组需要 2–512 个唯一原始界面。");
        Interfaces=Array.AsReadOnly(interfaces.ToArray());
        if(Interfaces.Sum(f=>(long)f.PayloadEntries)>262144||Interfaces.Sum(f=>f.PayloadCharacters)>16*1024*1024)
            throw new InvalidDataException("复合镜组组件载荷超出整组预算。");
        for(int i=0;i<Interfaces.Count-1;i++)
        {
            var a=Interfaces[i];var b=Interfaces[i+1];
            if(b.VertexZMillimeters-a.VertexZMillimeters<=1e-9||b.EdgeZ-a.EdgeZ<=1e-9
                ||NonSequentialOpticalInterface.Identity(a.MaterialAfter)!=NonSequentialOpticalInterface.Identity(b.MaterialBefore))throw new InvalidDataException($"复合镜组 S{a.SourceSurfaceNumber}→S{b.SourceSurfaceNumber} 界面顺序、机械边缘或相邻介质不连续。中心间隔={b.VertexZMillimeters-a.VertexZMillimeters:G17} mm，边缘间隔={b.EdgeZ-a.EdgeZ:G17} mm，介质={a.After.Name}→{b.Before.Name}。");
            var radius=Math.Min(a.MechanicalSemiDiameterMillimeters,b.MechanicalSemiDiameterMillimeters);
            var samples=new List<double>{0,radius};
            if(double.IsFinite(a.RadiusMillimeters)&&double.IsFinite(b.RadiusMillimeters)&&a.RadiusMillimeters*b.RadiusMillimeters>0&&a.Conic!=b.Conic)
            {var r2=(a.RadiusMillimeters*a.RadiusMillimeters-b.RadiusMillimeters*b.RadiusMillimeters)/(a.Conic-b.Conic);if(r2>0&&r2<radius*radius)samples.Add(Math.Sqrt(r2));}
            if(samples.Any(r=>b.VertexZMillimeters+b.Geometry.Sag(r,0)-a.VertexZMillimeters-a.Geometry.Sag(r,0)<=1e-9))throw new InvalidDataException("复合镜组实体边界相交或厚度不足。");
        }
        if(NonSequentialOpticalInterface.Identity(Interfaces[0].MaterialBefore)!=NonSequentialOpticalInterface.Identity(Interfaces[^1].MaterialAfter))throw new NotSupportedException("复合镜组外部介质必须闭合；不同外部介质需要显式容器边界。");
        var exterior=NonSequentialOpticalInterface.Identity(Interfaces[0].MaterialBefore);
        _ambientRegions=Interfaces.Take(Interfaces.Count-1).Select(f=>NonSequentialOpticalInterface.Identity(f.MaterialAfter)==exterior).ToArray();
    }
    public bool IsAmbientRegion(int regionIndex)=>_ambientRegions[regionIndex];
    internal IMaterial? MediumAt(Vector3D p)
    {
        var r=Math.Sqrt(p.X*p.X+p.Y*p.Y);
        for(int i=0;i<Interfaces.Count-1;i++)
        {
            if(IsAmbientRegion(i))continue;
            var a=Interfaces[i];var b=Interfaces[i+1];var lo=a.EdgeZ;var hi=b.EdgeZ;
            var fraction=Math.Clamp((p.Z-lo)/(hi-lo),0,1);
            var rim=a.MechanicalSemiDiameterMillimeters*(1-fraction)+b.MechanicalSemiDiameterMillimeters*fraction;
            if(r>rim)continue;
            var front=r<=a.MechanicalSemiDiameterMillimeters?a.VertexZMillimeters+a.Geometry.Sag(r,0):lo;
            var back=r<=b.MechanicalSemiDiameterMillimeters?b.VertexZMillimeters+b.Geometry.Sag(r,0):hi;
            if(p.Z>front&&p.Z<back)return a.After;
        }
        return null;
    }
}
