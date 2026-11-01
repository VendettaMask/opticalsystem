using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Scattering;

internal static class RealCalibrationChain
{
    internal static double Reference(XyzRadiometricCalibration calibration,double x,double y,double z)
    {
        var sum=x+y+z;if(sum==0)return 0;var model=calibration.PositiveModel!;
        var ordered=model.Nodes.Select(p=>(Node:p,D:Math.Pow(x/sum-p.X,2)+Math.Pow(y/sum-p.Y,2)+Math.Pow(z/sum-p.Z,2))).OrderBy(p=>p.D).ThenBy(p=>p.Node.SampleId,StringComparer.Ordinal).ToArray();
        var exact=ordered.Where(p=>p.D==0).ToArray();if(exact.Length>0)return sum*exact.Average(p=>p.Node.Ratios[0]);
        var near=ordered.Take(model.NeighbourCount).ToArray();return sum*near.Sum(p=>p.Node.Ratios[0]/p.D)/near.Sum(p=>1/p.D);
    }
    internal static string Source(bool transmission,double[] color)
    {
        var text=new StringBuilder("# Synthetic Lambertian angular control; colour from a held-out measured direction, not a full measured BRDF\nSource Measured\nSymmetry PlaneSymmetrical\nSpectralContent XYZ\nScatterType "+(transmission?"BTDF":"BRDF")+"\nSampleRotation 1\n0\nAngleOfIncidence 1\n0\nScatterAzimuth 3\n0 90 180\nScatterRadial 5\n0 60 90 120 180\n");
        for(int c=0;c<3;c++){text.AppendLine(new[]{"TristimulusX","TristimulusY","TristimulusZ"}[c]);text.AppendLine("DataBegin");text.AppendLine("TIS "+(Math.PI*color[c]).ToString("G17",CultureInfo.InvariantCulture));for(int p=0;p<3;p++)text.AppendLine("1 1 1 1 1");text.AppendLine("DataEnd");}
        return text.ToString();
    }
    internal static async Task Run(string root,string calibrationRoot)
    {
        Directory.CreateDirectory(root);var results=new List<object>();
        foreach(var material in new[]{"garnet_red","krylon_blue"})foreach(var transmission in new[]{false,true})
        {
            var calibrationPath=Path.Combine(calibrationRoot,material,"approved-550-calibration.json");var calibration=XyzRadiometricCalibration.Read(calibrationPath);
            var rows=File.ReadAllLines(Path.Combine(calibrationRoot,material,"known-conditions-pairs.csv")).Skip(1).Select(s=>s.Split(',')).ToArray();
            var witness=rows.First(r=>r[5]=="1"&&double.Parse(r[1],CultureInfo.InvariantCulture)+double.Parse(r[2],CultureInfo.InvariantCulture)+double.Parse(r[3],CultureInfo.InvariantCulture)>0);
            var original=witness.Skip(1).Take(3).Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();var sum=original.Sum();
            var color=original.Select(v=>.1*v/sum).ToArray(); // Explicit synthetic angular control, fixed total XYZ=.1 sr^-1.
            var expectedPower=Math.PI*Reference(calibration,color[0],color[1],color[2]);
            var dir=Path.Combine(root,material+"-"+(transmission?"btdf":"brdf"));Directory.CreateDirectory(dir);
            var scratch=Path.Combine(Path.GetTempPath(),"optical-xyz-chain-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
            var source=Path.Combine(scratch,"control.BSDF");var copiedCalibration=Path.Combine(scratch,"calibration.json");File.WriteAllText(source,Source(transmission,color));File.Copy(source,Path.Combine(dir,"angular-control.BSDF"));File.Copy(calibrationPath,copiedCalibration);
            using var app=WorkbenchApplication.Create();app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);foreach(var o in app.NonSequential.GetDocument().Objects.ToArray())app.NonSequential.DeleteObject(o.Id);
            app.NonSequential.UpdateWavelengths([new(1,"green",550,1,true)]);
            Guid Add(NonSequentialObjectKind kind,double z,NonSequentialObjectParameters p,double tilt=0){var id=app.NonSequential.AddObject(kind);app.NonSequential.UpdateObject(new(id,true,true,kind,kind.ToString(),null,null,0,0,z,0,tilt,0,p));return id;}
            const double sourceWatts=1e-9; // Fixed, measurable ADC signal for both dark-material controls; calibration data is unchanged.
            Add(NonSequentialObjectKind.SourcePoint,.032,new SourcePointParameters(sourceWatts,1,0,20,8192),180);
            var plane=Add(NonSequentialObjectKind.PlaneRectangle,0,new PlaneRectangleParameters(1,1,transmission?NonSequentialSurfaceBehavior.Refractive:NonSequentialSurfaceBehavior.Reflective,"AIR","AIR"));
            var detector=Add(NonSequentialObjectKind.DetectorRectangle,transmission?-.064:.064,new DetectorRectangleParameters(.128,.128,32,32,!transmission,true));
            var imported=await app.NonSequential.ImportMeasuredBsdfAsync(plane,source,options:new(OpticalSystemDesign.Application.Contracts.BsdfImportSides.BothDeclaredIdentical,ZemaxXyzCalibrationPath:copiedCalibration));
            var trace=await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode:NonSequentialSplittingMode.FullFresnel));
            var closure=Math.Abs((trace.DetectorPowerWatts+trace.AbsorbedPowerWatts+trace.EscapedPowerWatts+trace.TruncatedPowerWatts)/trace.SourcePowerWatts-1);
            var energyError=Math.Abs(1-trace.AbsorbedPowerWatts/trace.SourcePowerWatts-expectedPower);
            if(closure>1e-12||energyError>1e-11)throw new InvalidDataException("Real-calibration synthetic-control power mismatch");
            var input=await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector,transmission?"B2 & D3":"S2 & R2 & D3");
            var unfiltered=await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector,"");
            File.WriteAllText(Path.Combine(dir,"capture-debug.json"),JsonSerializer.Serialize(new{trace.SourcePowerWatts,trace.DetectorPowerWatts,trace.AbsorbedPowerWatts,trace.EscapedPowerWatts,Filter=input.PathFilter,FilteredPower=input.Bands.Sum(b=>b.PixelPowerWatts.Sum()),UnfilteredPower=unfiltered.Bands.Sum(b=>b.PixelPowerWatts.Sum()),Objects=app.NonSequential.GetDocument().Objects.Select(o=>new{o.Id,o.Kind,o.Name}),InputModels=input.SurfaceBsdfs!.Select(m=>new{m.ObjectNumber,m.Name})},PositiveFits.Json));
            if(input.SurfaceBsdfProtocol!="MeasuredBsdfGrid/8"||input.BsdfStatistics is not {SampleCount:8192})throw new InvalidDataException("Protocol/statistics mismatch");
            var momentError=Math.Abs(input.BsdfStatistics.MeanCosine-2d/3);if(momentError>6*Math.Sqrt(1d/(18*8192)))throw new InvalidDataException("Sampling moment mismatch");
            var sensor=new CameraSensorParameters(NoiseEnabled:false,DarkElectronsPerSecond:0,PrnuPercent:0,DsnuElectrons:0,GainDnPerElectron:.1,BlackLevel:0,FullWellElectrons:1e7,BitDepth:16,PixelPitchMicrometers:4);
            var response=new CameraResponseCurve("Analytic QE control","Analytic, not a measured sensor",[new(400,.2,.5,.8),new(700,.2,.5,.8)]);
            var request=new CameraImagingRequest(Source:CameraSource.NonSequentialDetector,Width:32,Height:32,Optics:CameraOptics.DetectorInput,Sensor:sensor,SpectralResponse:response,SpectralModel:CameraSpectralModel.DetectorBands,NscDetector:input);
            var run=await app.CameraImaging.SimulateAsync(request);double photonError=0;const double hc=6.62607015e-34*299792458;
            for(int y=0;y<32;y++)for(int x=0;x<32;x++){var qe=(y%2,x%2) switch{(0,0)=>.2,(1,1)=>.8,_=>.5};var expected=input.Bands.Sum(b=>b.PixelPowerWatts[(31-y)*32+x]*.01*b.WavelengthNanometers*1e-9/hc*qe);photonError=Math.Max(photonError,Math.Abs(expected-run.PhotoElectrons[y*32+x]));}
            if(photonError>1e-11||run.MeanPhotoElectrons<=0||!run.RawDn.Any(v=>v>0)||!run.Developed.Rgb24.Any(v=>v>0))throw new InvalidDataException($"Photon/RAW/ISP check failed: error={photonError:G17}, mean electrons={run.MeanPhotoElectrons:G17}, RAW max={run.RawDn.Max()}, RGB max={run.Developed.Rgb24.Max()}");
            var experiment=Path.Combine(dir,"real-calibration.camera-experiment.zip");await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier,1,run,run),experiment);
            var project=Path.Combine(dir,"real-calibration.staropt");await app.Documents.SaveAsync(project);int version;
            using(var raw=File.OpenRead(project)){raw.Position=52;using var unzip=new BrotliStream(raw,CompressionMode.Decompress);using var json=await JsonDocument.ParseAsync(unzip);version=json.RootElement.GetProperty("FormatVersion").GetInt32();}
            if(version!=16)throw new InvalidDataException("Wrong project version");File.Delete(source);File.Delete(copiedCalibration);await app.NonSequentialAnalysis.ClearDetectorsAsync();app.Documents.NewBlank();
            await app.Documents.OpenAsync(project);if(app.NonSequential.GetDocument().Objects.Single(o=>o.Id==plane).SurfaceBsdf!.MeasuredData!.DataSha256!=imported.MeasuredData!.DataSha256)throw new InvalidDataException("Project digest mismatch");app.Documents.NewBlank();
            var opened=await app.CameraImaging.OpenExperimentAsync(experiment);var replay=await app.CameraImaging.RecomputeAsync(opened.B);
            var exact=run.PhotoElectrons.SequenceEqual(replay.PhotoElectrons)&&run.RawDn.SequenceEqual(replay.RawDn)&&run.Developed.Rgb24.SequenceEqual(replay.Developed.Rgb24);
            if(!exact)throw new InvalidDataException("Frozen real-calibration replay mismatch");
            var result=new{Material=material,Transmission=transmission,DetectorFrontOnly=!transmission,CalibrationSha256=PositiveFits.Sha(calibrationPath),InputWitness=witness[0],InputScale="Synthetic uniform angular control, XYZ total=.1 sr^-1",SourcePowerWatts=sourceWatts,CalibratedMaterialIsNotFullMeasuredAngularDistribution=true,ExpectedScatteredFraction=expectedPower,EnergyError=energyError,ClosureError=closure,PhotonErrorElectrons=photonError,ProjectVersion=version,ExactReplay=exact,input.BsdfStatistics};
            File.WriteAllText(Path.Combine(dir,"chain.json"),JsonSerializer.Serialize(result,PositiveFits.Json));results.Add(result);
        }
        File.WriteAllText(Path.Combine(root,"summary.json"),JsonSerializer.Serialize(new{CoreSha256=PositiveFits.Sha(typeof(XyzRadiometricCalibration).Assembly.Location),ApplicationSha256=PositiveFits.Sha(typeof(WorkbenchApplication).Assembly.Location),DriverSha256=PositiveFits.Sha(System.Reflection.Assembly.GetExecutingAssembly().Location),Cases=results},PositiveFits.Json));Console.WriteLine(JsonSerializer.Serialize(new{Cases=results.Count,Passed=true}));
    }
}
