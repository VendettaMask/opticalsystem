using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using SkiaSharp;
using OpticalSystemDesign.ZemaxComparison;

var json=new JsonSerializerOptions{WriteIndented=true,PropertyNameCaseInsensitive=true,
    PropertyNamingPolicy=JsonNamingPolicy.CamelCase,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals};
json.Converters.Add(new JsonStringEnumConverter());
if(args.Length==2 && args[0]=="inventory")
{
    var root=Path.GetFullPath(args[1]);
    var modelPath=Path.Combine(root,File.Exists(Path.Combine(root,"prepared-all-models.json"))?"prepared-all-models.json":"models.json");
    var models=JsonDocument.Parse(File.ReadAllText(modelPath)).RootElement;
    var rows=new List<object>();
    foreach(var model in models.EnumerateArray())
    {
        var configuration=0;
        foreach(var path in model.GetProperty("snapshots").EnumerateArray())
        {
            var optic=Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(File.ReadAllText(path.GetString()!),json)!);
            rows.Add(new{lensIndex=model.GetProperty("index").GetInt32(),lens=model.GetProperty("lens").GetString(),
                configuration=++configuration,issues=OpticCapabilityPreflight.Inspect(optic)});
        }
    }
    File.WriteAllText(Path.Combine(root,"capability-inventory.json"),JsonSerializer.Serialize(rows,json));return;
}
if(args.Length==3 && args[0]=="compare-controls")
{
    var root=Path.GetFullPath(args[1]);var output=Path.GetFullPath(args[2]);var comparisons=new List<object>();
    foreach(var job in JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"jobs.json"))).RootElement.EnumerateArray())
    {
        var id=job.GetProperty("id").GetString()!;if(id.StartsWith("paraxial"))continue;
        var request=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,job.GetProperty("request").GetString()!))).RootElement;
        var source=request.GetProperty("input").GetString()!;
        if(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source)))!=job.GetProperty("inputSha256").GetString())
            throw new InvalidDataException("Source changed");
        var optic=await new ZemaxZmxImporter().ImportFileAsync(source);
        var surface=optic.SurfaceGroup.Items.First(s=>s.Geometry is ChebyshevSagGeometry or FringeSagGeometry or TernaryMirrorArrayGeometry || s.MaterialAfter is GradientIndexMaterial);
        var native=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,id,"model-controls.json"))).RootElement;
        var nativeModel=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,id,"model.json"))).RootElement;
        var signs=NativeReflectionConvention.PropagationSigns(nativeModel);
        foreach(var row in native.GetProperty("rows").EnumerateArray())
        {
            var p=row.GetProperty("input");var code=p.GetProperty("code").GetString()!;
            if(p.GetProperty("int2").GetInt32()>nativeModel.GetProperty("wavelengths").GetArrayLength()
                && code is not ("SSAG" or "SSLP"))
            {
                comparisons.Add(new{id,code,input=p,skipped="Outside native declared wavelength list; excluded even if the native operand substitutes its primary wavelength"});
                continue;
            }
            double N(string k)=>p.GetProperty(k).GetDouble();
            double? actual=null;string? skipped=null;
            if(code=="SSAG")
            {
                // SSAG returns zero outside the native clear aperture; do not compare that sentinel to sag.
                if(id=="chebyshev-oap"&&N("data2")==0) skipped="Outside declared rectangular clear aperture";
                else actual=surface.Geometry.Sag(N("data1"),N("data2"));
            }
            else if(code=="INDX")
            {
                var wavelength=nativeModel.GetProperty("wavelengths")[p.GetProperty("int2").GetInt32()-1].GetProperty("data").GetProperty("Wavelength").GetDouble()*1000;
                actual=((GradientIndexMaterial)optic.SurfaceGroup.Items[p.GetProperty("int1").GetInt32()].MaterialAfter).RefractiveIndex(new(0,0,0),wavelength);
            }
            else if(code.StartsWith("REA") || code=="OPTH")
            {
                var wavelength=nativeModel.GetProperty("wavelengths")[p.GetProperty("int2").GetInt32()-1].GetProperty("data").GetProperty("Wavelength").GetDouble();
                var target=optic.SurfaceGroup.Items.Any(s=>s.MaterialAfter is GradientIndexMaterial)
                    ?optic.SurfaceGroup.Items[p.GetProperty("int1").GetInt32()]:surface;
                var rays=optic.SequentialRayTracer.RayGenerator.GenerateGeneric(N("data1"),N("data2"),N("data3"),N("data4"),wavelength);
                using var trace=optic.SequentialRayTracer.Trace(rays,TraceRequest.Selected([target.Number]));
                var sample=trace.GetRaySamples(0).Single() ?? throw new InvalidDataException("Core ray did not reach the controlled surface");
                var position=target.CoordinateSystem.ToLocalPoint(sample.Position);
                var direction=target.CoordinateSystem.ToLocalDirection(sample.Direction);
                actual=code switch{"REAX"=>position.X,"REAY"=>position.Y,"REAZ"=>position.Z,
                    "REAA"=>direction.X,"REAB"=>direction.Y,"REAC"=>direction.Z,"OPTH"=>sample.PhaseInclusiveOpticalPathLength,_=>throw new InvalidDataException(code)};
            }
            var rawExpected=row.GetProperty("value").GetDouble();
            var normalizationSign=code is "REAA" or "REAB" or "REAC" ? signs[p.GetProperty("int1").GetInt32()] : 1;
            var expected=rawExpected*normalizationSign;
            comparisons.Add(new{id,code,input=p,rawExpected,normalizationSign,expected,actual,
                absoluteError=actual.HasValue?Math.Abs(expected-actual.Value):(double?)null,skipped});
        }
    }
    File.WriteAllText(output,JsonSerializer.Serialize(comparisons,json));return;
}
if(args.Length == 3 && args[0] == "svg")
{
    var root = XDocument.Load(args[1]).Root!;
    using var bitmap = new SKBitmap((int)Number(root,"width")*4,(int)Number(root,"height")*4);
    using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White);canvas.Scale(4);
    using var paint = new SKPaint{IsAntialias=true,Color=SKColors.Black,Style=SKPaintStyle.Fill};
    Visit(root,Matrix3x2.Identity);
    using var data=bitmap.Encode(SKEncodedImageFormat.Png,100);using var output=File.Create(args[2]);data.SaveTo(output);
    void Visit(XElement e,Matrix3x2 parent)
    {
        var m=Transform((string?)e.Attribute("transform"))*parent;
        if(e.Name.LocalName=="path")
        {
            using var p=SKPath.ParseSvgPathData((string)e.Attribute("d")!);
            p.Transform(new SKMatrix(m.M11,m.M21,m.M31,m.M12,m.M22,m.M32,0,0,1));canvas.DrawPath(p,paint);
        }
        else foreach(var child in e.Elements())Visit(child,m);
    }
    return;
}
throw new ArgumentException("svg <input.svg> <output.png> | inventory <study> | compare-controls <native-folder> <output.json>");
static float Number(XElement e,string name)=>float.Parse((string?)e.Attribute(name)??"0",CultureInfo.InvariantCulture);
static Matrix3x2 Transform(string? text)
{
    var result=Matrix3x2.Identity;
    foreach(Match match in Regex.Matches(text??"",@"(translate|scale|matrix)\s*\(([^)]*)\)"))
    {
        var n=Regex.Matches(match.Groups[2].Value,@"[-+]?(?:\d*\.)?\d+(?:[eE][-+]?\d+)?")
            .Select(v=>float.Parse(v.Value,CultureInfo.InvariantCulture)).ToArray();
        result=(match.Groups[1].Value switch
        {
            "translate"=>Matrix3x2.CreateTranslation(n[0],n.Length>1?n[1]:0),
            "scale"=>Matrix3x2.CreateScale(n[0],n.Length>1?n[1]:n[0]),
            "matrix"=>new Matrix3x2(n[0],n[1],n[2],n[3],n[4],n[5]),
            _=>throw new InvalidDataException()
        })*result;
    }
    return result;
}
