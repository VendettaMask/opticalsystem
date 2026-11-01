using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

internal static class RuntimePowerAudit
{
    internal static void Run(string root, string? outputRoot = null)
    {
        outputRoot ??= root; Directory.CreateDirectory(outputRoot);
        string Sha(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
        var sources=Directory.GetFiles(root,"*.anisotropicbsdf").Append(Path.GetFullPath(Path.Combine(root,"../commercial-bsdf-calibration/native-valid-control-loaded.anisotropicbsdf")))
            .GroupBy(Sha).ToDictionary(g=>g.Key,g=>g.First());
        using var ledger=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"native-power-ledger.json")));
        var results=new List<object>();var passed=0;var skipped=0;double maximum=0;
        foreach(var row in ledger.RootElement.EnumerateArray())
        {
            var name=row.GetProperty("Case").GetString()!;var sha=row.GetProperty("SourceSha256").GetString()!;
            try
            {
                var data=BsdfFileImporter.Read(sources[sha],new(BsdfImportSides.Front));var model=new MeasuredBsdf(data);
                var wave=row.GetProperty("WavelengthNm").GetDouble();
                var xyz=row.GetProperty("TargetNormal").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                var normal=new Vector3D(xyz[0],xyz[1],xyz[2]);normal/=normal.Length;
                var c=new BsdfContext(new(0,0,1),normal,wave);
                var normalInput=row.GetProperty("TargetIncidenceDegrees").GetDouble()==0;
                var energy=model.Integrate(c).Reflection;var escaped=row.GetProperty("EscapedWatts").GetDouble();
                // Integrate the actual black 0.02 mm emitter backing at z=10 mm, averaged
                // over the uniformly emitting square. Two-point Gauss on each of four axes.
                const double halfWidth=.01, distance=10;
                var node=halfWidth/Math.Sqrt(3);double occluded=0;
                foreach(var sx in new[]{-node,node})foreach(var sy in new[]{-node,node})
                foreach(var px in new[]{-node,node})foreach(var py in new[]{-node,node}){
                    var z=distance+sx*normal.X/normal.Z;
                    var dx=px-sx;var dy=py-sy;var radius=Math.Sqrt(dx*dx+dy*dy+z*z);
                    var mu=(normal.X*dx+normal.Y*dy+normal.Z*z)/radius;
                    occluded+=model.Evaluate(c,new(dx/radius,dy/radius,z/radius))*mu*z/(radius*radius*radius)*halfWidth*halfWidth/4;
                }
                var expectedEscaped=energy-occluded;
                var error=Math.Abs(expectedEscaped-escaped);maximum=Math.Max(maximum,error);
                var nRays=row.GetProperty("ActualRays").GetInt64();
                var tolerance=.5e-6+6*Math.Sqrt(Math.Max(0,energy*occluded-occluded*occluded)/nRays);
                var values=NativeXmpControl.ReadLayers(Path.Combine(root,name,"native-direct-result.xmp"),64,1)[0];
                var flux=NativeXmpControl.Integral(values,64);
                var observed=NativeXmpControl.Integral(values,64,1)/flux;
                const int n=49152;double mass=0,first=0,second=0;
                for(var i=0;normalInput&&i<n;i++){
                    var theta=(i+.5)*Math.PI/(2*n);var mu=Math.Cos(theta);
                    var weight=model.Evaluate(c,new(Math.Sin(theta),0,mu))*mu*Math.Sin(theta)*Math.PI*Math.PI/n;
                    mass+=weight;first+=weight*mu;second+=weight*mu*mu;
                }
                double? mean=normalInput?first/mass:null;
                double? sigma=normalInput?6*Math.Sqrt((second/mass-mean!.Value*mean.Value)/nRays):null;
                var powerPass=error<=tolerance;bool? momentPass=normalInput?Math.Abs(observed-mean!.Value)<=sigma!.Value:null;
                if(powerPass&&(momentPass??true))passed++;
                results.Add(new{Case=name,SourceSha256=sha,data.DataSha256,Protocol=data.ComputationProtocol,WavelengthNm=wave,InputDegrees=row.GetProperty("TargetIncidenceDegrees").GetDouble(),CoreFullHemisphereWatts=energy,CoreBlackEmitterOcclusionWatts=occluded,CoreExpectedEscapedWatts=expectedEscaped,NativeEscapedWatts=escaped,ScenePowerErrorWatts=error,ScenePowerToleranceWatts=tolerance,PowerPassed=powerPass,CoreMeanCosine=mean,NativeSensorMeanCosine=observed,MeanCosineSixSigma=sigma,MomentPassed=momentPass,IndependentIntegratedWatts=normalInput?(double?)mass:null});
            }
            catch(InvalidDataException ex){skipped++;results.Add(new{Case=name,Unsupported=true,Reason=ex.Message});}
        }
        var report=new{Scope="Captured 1 W White Specular controls only: 20 normal incidence power/moment controls and three non-node heldout power controls. Tilted controls do not certify angular shape. Scene escape subtracts independently integrated black emitter occlusion from full hemisphere power. Allowance is HTML 0.5 microW rounding plus six-sigma weighted-MC occlusion variance from actual ray count. Original fixed-2-microW preliminary audit is retained separately; it mixed full-material and scene power for narrow peaks. Finite sensor flux remains separate. Normal-incidence directional moment uses independent integration and the original six-sigma statistical gate, not pointwise BSDF certification.",CoreAssemblySha256=Sha(typeof(MeasuredBsdfData).Assembly.Location),DiagnosticAssemblySha256=Sha(typeof(RuntimePowerAudit).Assembly.Location),Passed=passed,Unsupported=skipped,MaximumScenePowerErrorWatts=maximum,Results=results};
        File.WriteAllText(Path.Combine(outputRoot,"runtime6-native-power-audit.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(new{Passed=passed,Unsupported=skipped,MaximumScenePowerErrorWatts=maximum}));
    }
}
