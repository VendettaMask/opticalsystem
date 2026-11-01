using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

internal static class NativeComparison
{
    internal static void Run(string root)
    {
        var source=Path.GetFullPath("samples/non-sequential/speos-v11-xyz/synthetic-v11.anisotropicbsdf");
        var data=BsdfFileImporter.Read(source,new(BsdfImportSides.Front));var model=new MeasuredBsdf(data);
        using var reference=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"native-isolated-active-v11.json")));
        var energies=reference.RootElement.GetProperty("Values").EnumerateArray().Select(p=>{
            var theta=p.GetProperty("IncidenceInput").GetDouble();var wave=p.GetProperty("WavelengthInput").GetDouble();
            var context=new BsdfContext(new(Math.Sin(theta*Math.PI/180),0,Math.Cos(theta*Math.PI/180)),new(0,0,1),wave);
            var actual=model.Integrate(context);var expected=p.GetProperty("Reflection").GetDouble()/100;
            return new{ThetaDegrees=theta,WavelengthNm=wave,ReferenceReflection=expected,ActualReflection=actual.Reflection,AbsoluteError=Math.Abs(actual.Reflection-expected)};
        }).ToArray();
        var path=Path.Combine(root,"synthetic-v11-loaded.txt");var lines=File.ReadAllLines(path);
        var axes=lines[4].Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();
        var size=lines[5].Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        var rows=lines.Skip(8).Take(size[1]).Select(l=>l.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray()).ToArray();
        if(rows.Any(r=>r.Length!=size[0]))throw new InvalidDataException("Native XMP text grid mismatch");
        var nativeLayers=NativeXmpControl.ReadLayers(Path.Combine(root,"synthetic-v11-loaded.xmp"),64,3);double decoderError=0;
        for(int y=0;y<64;y++)for(int x=0;x<64;x++)decoderError=Math.Max(decoderError,Math.Abs(rows[y][x]-nativeLayers.Sum(l=>(double)l[y*64+x])));
        if(decoderError>5e-11)throw new InvalidDataException("XMP corpus reader differs from native COM text: "+decoderError);
        var comparisons=new List<object>();double maxQ=0,maxF=0;double sumSquared=0,sumReference=0;
        for(int y=0;y<size[1];y++)for(int x=0;x<size[0];x++){
            var ax=axes[0]+(x+.5)*(axes[1]-axes[0])/size[0];var ay=axes[2]+(y+.5)*(axes[3]-axes[2])/size[1];var theta=Math.Sqrt(ax*ax+ay*ay);if(theta>=Math.PI/2)continue;
            // Equidistant conoscopic axes; Speos outgoing phi=0 maps to canonical -X.
            var radius=theta==0?1:Math.Sin(theta)/theta;var direction=new Vector3D(-ax*radius,-ay*radius,Math.Cos(theta));double actual=0;
            foreach(var incidence in new[]{0d,30d,60d}){var a=incidence*Math.PI/180;actual+=model.Evaluate(new(new(Math.Sin(a),0,Math.Cos(a)),new(0,0,1),400),direction);}
            var expected=rows[y][x];var q=actual*Math.Cos(theta);var qe=Math.Abs(q-expected);var fe=Math.Abs(actual-expected);maxQ=Math.Max(maxQ,qe);maxF=Math.Max(maxF,fe);sumSquared+=qe*qe;sumReference+=expected*expected;
            comparisons.Add(new{X=x,Y=y,ThetaDegrees=theta*180/Math.PI,Reference=expected,ActualProjected=q,ActualBsdf=actual,ProjectedAbsoluteError=qe});
        }
        var result=new{data.SourceSha256,data.DataSha256,Protocol=data.ComputationProtocol,CoreAssemblySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MeasuredBsdf).Assembly.Location))).ToLowerInvariant(),
            Coefficients=energies,MaximumCoefficientError=energies.Max(e=>e.AbsoluteError),NativeTextSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
            Map="Merged three native incidence layers 0/30/60 deg, 400 nm; projected intensity; equidistant axes",MaximumProjectedError=maxQ,MaximumBsdfError=maxF,ProjectedNormalizedRms=Math.Sqrt(sumSquared/sumReference),
            NativeXmpDecoderMaximumDifference=decoderError,DirectionalCertification=false,Values=comparisons};
        File.WriteAllText(Path.Combine(root,"native-core-comparison.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(new{result.MaximumCoefficientError,maxQ,maxF,result.ProjectedNormalizedRms}));
        var convergence=new List<object>();
        foreach(var mode in new[]{"loaded","white-disabled","white-enabled","reapplied-specular","generated-specular"})foreach(var resolution in new[]{64,128,256,512})
        {
            var map=Path.Combine(root,$"synthetic-v11-{mode}-{resolution}.xmp");var layers=NativeXmpControl.ReadLayers(map,resolution,3);
            convergence.Add(new{Mode=mode,Resolution=resolution,MapSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(map))).ToLowerInvariant(),
                IncidenceDegrees=new[]{0d,30d,60d},IntegratedProjectedMap=layers.Select(l=>NativeXmpControl.Integral(l,resolution)).ToArray()});
        }
        File.WriteAllText(Path.Combine(root,"native-map-convergence.json"),JsonSerializer.Serialize(new{NativeXmpDecoderMaximumDifference=decoderError,Values=convergence},new JsonSerializerOptions{WriteIndented=true}));
        var noWhitePath=Path.Combine(root,"synthetic-v11-white-disabled.anisotropicbsdf");var noWhite=BsdfFileImporter.Read(noWhitePath,new(BsdfImportSides.Front));
        var noWhiteMap=Path.Combine(root,"synthetic-v11-white-disabled-64.xmp");var controls=NativeXmpControl.ReadLayers(noWhiteMap,64,3);
        using var coefficientCapture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"native-white-disabled.json")));
        var captured=coefficientCapture.RootElement;var values=new List<BsdfReferenceValue>();
        foreach(var (incidence,layer) in new[]{(0d,0),(30d,1),(60d,2)})
        {
            var coefficient=captured.GetProperty("Values").EnumerateArray().Single(p=>p.GetProperty("IncidenceInput").GetDouble()==incidence&&p.GetProperty("WavelengthInput").GetDouble()==400);
            for(int y=0;y<64;y+=4)for(int x=0;x<64;x+=4)
            {
                var a=-Math.PI/2+(x+.5)*Math.PI/64;var b=-Math.PI/2+(y+.5)*Math.PI/64;var theta=Math.Sqrt(a*a+b*b);if(theta>=Math.PI/2)continue;
                var phi=Math.Atan2(-b,-a)*180/Math.PI;if(phi<0)phi+=360;
                values.Add(new(BsdfSide.Front,400,incidence,0,theta*180/Math.PI,phi,1,1,controls[layer][y*64+x]/Math.Cos(theta),coefficient.GetProperty("Reflection").GetDouble()/100,coefficient.GetProperty("Transmission").GetDouble()/100));
            }
        }
        var identity=new BsdfReferenceIdentity(BsdfReferenceOrigin.CommercialNativeExport,"Speos RPC XMP + Viewer coefficient",captured.GetProperty("Version").GetString()!,captured.GetProperty("ExecutableSha256").GetString()!,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(noWhiteMap))).ToLowerInvariant(),noWhite.SourceSha256,noWhite.DataSha256);
        // Float32 XMP controls, no metrology uncertainty claimed. Scoped numeric regression only.
        var tolerance=new BsdfReferenceTolerance(1e-8,2e-6,1e-10,0);
        var comparison=BsdfReferenceComparer.Compare(noWhite,identity,values,tolerance);
        File.WriteAllText(Path.Combine(root,"native-no-white-reference.json"),JsonSerializer.Serialize(new{Identity=identity,Tolerance=tolerance,Values=values,Scope="400 nm, measured input nodes 0/30/60 deg, original enhancement on, White Specular disabled; float32 conoscopic export",RpcExecutableSha256="277c76b6e609a8f97378f6647c1b8123943494fbfc81aa2a4e5f9e986ffa019d"},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(root,"native-no-white-comparison.json"),JsonSerializer.Serialize(comparison,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(new{NoWhitePoints=values.Count,comparison.NumericalComparisonPassed,MaximumValueError=comparison.Points.Max(p=>p.ValueError)}));
    }
}
