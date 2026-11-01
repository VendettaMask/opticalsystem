using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

internal static class MeasuredLogChain
{
    // Independent quadrature of the retained, piecewise-bilinear normal-centred
    // angular ordinates. No product Evaluate/Integrate call supplies these moments.
    internal static (double Power,double Mean,double Variance,double[] Probabilities) Reference(MeasuredBsdfData data,double incidence,int order)
    {
        var points=data.Points.Where(p=>p.Side==BsdfSide.Front&&p.IncidentThetaDegrees==incidence).ToArray();
        var mus=points.Select(p=>Math.Cos(p.OutgoingThetaDegrees*Math.PI/180)).Distinct().Order().ToArray();
        var phis=points.Select(p=>p.OutgoingPhiDegrees*Math.PI/180).Distinct().Order().ToArray();
        var nodes=points.ToDictionary(p=>(Math.Cos(p.OutgoingThetaDegrees*Math.PI/180),p.OutgoingPhiDegrees*Math.PI/180),p=>p.BrdfPerSteradian+p.BtdfPerSteradian);
        double[] sums=new double[3];var histogram=new double[32];var gaussNodes=Gauss(order);
        for(int i=0;i<mus.Length-1;i++)for(int j=0;j<phis.Length-1;j++)
        {
            var mb=new[]{mus[i],mus[i+1]}.Concat(new[]{.25,.5,.75}.Where(x=>x>mus[i]&&x<mus[i+1])).Order().ToArray();
            var pb=new[]{phis[j],phis[j+1]}.Concat(Enumerable.Range(1,7).Select(k=>k*Math.PI/4).Where(x=>x>phis[j]&&x<phis[j+1])).Order().ToArray();
            for(int m=0;m<mb.Length-1;m++)for(int p=0;p<pb.Length-1;p++)
            foreach(var gauss in gaussNodes)foreach(var b in gaussNodes)
            {
                var mu=(mb[m]+mb[m+1])/2+gauss.Item1*(mb[m+1]-mb[m])/2;var phi=(pb[p]+pb[p+1])/2+b.Item1*(pb[p+1]-pb[p])/2;
                var u=(mu-mus[i])/(mus[i+1]-mus[i]);var v=(phi-phis[j])/(phis[j+1]-phis[j]);
                double Encode(double f)=>data.LogInterpolation is null?f:Math.Log(1+f/data.LogInterpolation.ScalePerSteradian);
                var encoded=Encode(nodes[(mus[i],phis[j])])*(1-u)*(1-v)+Encode(nodes[(mus[i+1],phis[j])])*u*(1-v)+Encode(nodes[(mus[i],phis[j+1])])*(1-u)*v+Encode(nodes[(mus[i+1],phis[j+1])])*u*v;
                var f=data.LogInterpolation is null?encoded:data.LogInterpolation.ScalePerSteradian*(Math.Exp(encoded)-1);
                var weight=(mb[m+1]-mb[m])*(pb[p+1]-pb[p])/4*gauss.Item2*b.Item2;
                for(int k=0;k<3;k++)sums[k]+=f*Math.Pow(mu,k+1)*weight;
                histogram[Math.Min(3,(int)(4*mu))*8+Math.Min(7,(int)(phi/(Math.PI/4)))]+=f*mu*weight;
            }
        }
        var power=data.ZemaxDefinition?.Integrals.First(t=>t.Side==BsdfSide.Front).Fraction??sums[0];
        return(power,sums[1]/sums[0],sums[2]/sums[0]-Math.Pow(sums[1]/sums[0],2),histogram.Select(v=>v/sums[0]).ToArray());
    }
    private static (double,double)[] Gauss(int n)
    {
        var result=new (double,double)[n];
        for(int i=0;i<(n+1)/2;i++)
        {
            double z=Math.Cos(Math.PI*(i+.75)/(n+.5)),derivative;
            for(int iteration=0;;iteration++)
            {
                double a=1,b=0;for(int j=1;j<=n;j++){var old=b;b=a;a=((2*j-1)*z*b-(j-1)*old)/j;}
                derivative=n*(z*a-b)/(z*z-1);var next=z-a/derivative;if(Math.Abs(next-z)<2e-15){z=next;break;}if(iteration>30)throw new InvalidDataException("Independent Legendre root failed");z=next;
            }
            var weight=2/((1-z*z)*derivative*derivative);result[i]=(-z,weight);result[n-1-i]=(z,weight);
        }
        return result;
    }
    internal static async Task Run(string root,string repo)
    {
        var cases=new List<object>();
        foreach(var name in new[]{"garnet_red","krylon_blue","acryl_blue","mystique","cayman","EDC50","EDS40","HILAM"})
        {
            var transmission=name[0] is 'E' or 'H';var wave=name=="HILAM"?632:550d;
            var path=transmission?Directory.GetFiles(Path.Combine(repo,"validation/rechecks/2026-11-01/xyz-angular/measured-btdf",name),"*.bsdf").Single()
                :name=="mystique"?Path.Combine(repo,"validation/rechecks/2026-10-31/commercial-bsdf-calibration/mystique.astm")
                :name=="cayman"?Path.Combine(repo,"validation/rechecks/2026-10-31/white-specular-runtime/xyz-measured/cayman.astm")
                :Path.Combine(repo,"validation/rechecks/2026-11-01/xyz-radiometry/measured",name+".astm");
            // Full eight measured incidences, with an explicitly selected new interpolation model.
            foreach(var requestedIncidence in transmission?new[]{0d}:Enumerable.Range(1,8).Select(i=>10d*i).ToArray())
            {
                var options=new BsdfFileImportOptions(OpticalSystemDesign.Core.Scattering.BsdfImportSides.BothDeclaredIdentical,transmission?wave:null,transmission,AstmWavelengthNanometers:transmission?null:550,AstmLogInterpolationScalePerSteradian:transmission?null:1e-6);
                var data=BsdfFileImporter.Read(path,options);var incidence=data.Points.Select(p=>p.IncidentThetaDegrees).Distinct().MinBy(t=>Math.Abs(t-requestedIncidence));
                var dir=Path.Combine(root,name+"-"+incidence.ToString("F5",CultureInfo.InvariantCulture));Directory.CreateDirectory(dir);
                var reference=Reference(data,incidence,16);var refined=Reference(data,incidence,24);var referenceDrift=Math.Abs(reference.Power-refined.Power);if(referenceDrift>1e-11||Math.Abs(reference.Mean-refined.Mean)>1e-11||reference.Probabilities.Zip(refined.Probabilities).Any(p=>Math.Abs(p.First-p.Second)>1e-11))throw new InvalidDataException("Independent angular quadrature did not converge");var bsdf=new MeasuredBsdf(data);
                var c=new BsdfContext(new(Math.Sin(incidence*Math.PI/180),0,Math.Cos(incidence*Math.PI/180)),new(0,0,1),wave,BsdfSide.Front,data.Coordinates==BsdfCoordinates.FixedMaterialAxes?new Vector3D(1,0,0):null);
                if(Math.Abs(bsdf.Integrate(c).Total-reference.Power)>1e-11)throw new InvalidDataException("Independent angular power mismatch");
                const int sampleCount=32768;var random=new Random(380911);double sum=0,maxImportanceError=0;var counts=new int[32];
                for(int i=0;i<sampleCount;i++){var s=bsdf.Sample(c,random.NextDouble(),random.NextDouble());var mu=Math.Abs(s.Direction.Z);sum+=mu;maxImportanceError=Math.Max(maxImportanceError,Math.Abs(s.Value*mu/s.Pdf-reference.Power));var phi=transmission?Math.Atan2(s.Direction.X,s.Direction.Y):Math.Atan2(s.Direction.Y,s.Direction.X);if(phi<0)phi+=2*Math.PI;counts[Math.Min(3,(int)(4*mu))*8+Math.Min(7,(int)(phi/(Math.PI/4)))]++;}
                var momentError=Math.Abs(sum/sampleCount-reference.Mean);var allowance=6*Math.Sqrt(reference.Variance/sampleCount);
                if(momentError>allowance||maxImportanceError>1e-11)throw new InvalidDataException("Independent angular sampling mismatch");
                var binErrors=counts.Select((n,i)=>Math.Abs((double)n/sampleCount-reference.Probabilities[i])).ToArray();
                var binAllowances=reference.Probabilities.Select(p=>6*Math.Sqrt(p*(1-p)/sampleCount)+1d/sampleCount).ToArray();
                if(binErrors.Where((e,i)=>e>binAllowances[i]).Any())throw new InvalidDataException("Independent two-dimensional angular histogram mismatch");
                File.WriteAllText(Path.Combine(dir,"angular-histogram.json"),JsonSerializer.Serialize(new{Counts=counts,ExpectedProbabilities=reference.Probabilities,Errors=binErrors,SixSigmaPlusOneCountAllowance=binAllowances},PositiveFits.Json));
                using var app=WorkbenchApplication.Create();app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);foreach(var o in app.NonSequential.GetDocument().Objects.ToArray())app.NonSequential.DeleteObject(o.Id);
                app.NonSequential.UpdateWavelengths([new(1,"explicit measured/control wavelength",wave,1,true)]);
                Guid Add(NonSequentialObjectKind kind,double z,NonSequentialObjectParameters parameters,double tilt=0)
                {var id=app.NonSequential.AddObject(kind);app.NonSequential.UpdateObject(new(id,true,true,kind,kind.ToString(),null,null,0,0,z,0,tilt,0,parameters));return id;}
                const double sourcePower=5e-12;
                Add(NonSequentialObjectKind.SourcePoint,.032,new SourcePointParameters(sourcePower,1,0,20,8192),180-incidence);
                var plane=Add(NonSequentialObjectKind.PlaneRectangle,0,new PlaneRectangleParameters(1,1,transmission?NonSequentialSurfaceBehavior.Refractive:NonSequentialSurfaceBehavior.Reflective,"AIR","AIR"));
                var detector=Add(NonSequentialObjectKind.DetectorRectangle,transmission?-.064:.064,new DetectorRectangleParameters(3.2,3.2,32,32,false,true));
                var scratch=Path.Combine(Path.GetTempPath(),"measured-angular-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);var local=Path.Combine(scratch,Path.GetFileName(path));File.Copy(path,local);
                var imported=await app.NonSequential.ImportMeasuredBsdfAsync(plane,local,options:new(OpticalSystemDesign.Application.Contracts.BsdfImportSides.BothDeclaredIdentical,transmission?wave:null,transmission,AstmWavelengthNanometers:transmission?null:550,AstmLogInterpolationScalePerSteradian:transmission?null:1e-6));
                var trace=await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode:NonSequentialSplittingMode.FullFresnel));
                var closure=Math.Abs((trace.DetectorPowerWatts+trace.AbsorbedPowerWatts+trace.EscapedPowerWatts+trace.TruncatedPowerWatts)/trace.SourcePowerWatts-1);
                var energy=Math.Abs(1-trace.AbsorbedPowerWatts/trace.SourcePowerWatts-reference.Power);if(closure>1e-12||energy>1e-11)throw new InvalidDataException("Measured angular scene energy mismatch");
                var input=await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector,transmission?"B2 & D3":"S2 & D3");
                if(input.SurfaceBsdfProtocol!=data.ComputationProtocol||input.BsdfStatistics?.SampleCount!=8192)throw new InvalidDataException("Measured protocol or NSC sampling count mismatch");
                if(Math.Abs(input.BsdfStatistics.MeanCosine-reference.Mean)>6*Math.Sqrt(reference.Variance/8192))throw new InvalidDataException("NSC measured angular moment mismatch");
                var request=new CameraImagingRequest(Source:CameraSource.NonSequentialDetector,Width:32,Height:32,Optics:CameraOptics.DetectorInput,
                    Sensor:new(NoiseEnabled:false,DarkElectronsPerSecond:0,PrnuPercent:0,DsnuElectrons:0,GainDnPerElectron:2,BlackLevel:0,FullWellElectrons:1e7,BitDepth:16,PixelPitchMicrometers:100),
                    SpectralResponse:new("Analytic QE control","Not measured sensor data",[new(400,.2,.5,.8),new(700,.2,.5,.8)]),SpectralModel:CameraSpectralModel.DetectorBands,NscDetector:input);
                var run=await app.CameraImaging.SimulateAsync(request);double photonError=0;const double hc=6.62607015e-34*299792458;
                for(int y=0;y<32;y++)for(int x=0;x<32;x++){var qe=(y%2,x%2) switch{(0,0)=>.2,(1,1)=>.8,_=>.5};var expected=input.Bands.Sum(b=>b.PixelPowerWatts[(31-y)*32+x]*.01*b.WavelengthNanometers*1e-9/hc*qe);photonError=Math.Max(photonError,Math.Abs(expected-run.PhotoElectrons[y*32+x]));}
                if(photonError>1e-11||run.MeanPhotoElectrons<=0||!run.RawDn.Any(v=>v>0)||!run.Developed.Rgb24.Any(v=>v>0))throw new InvalidDataException("Measured angular photon/RAW/ISP mismatch");
                var experiment=Path.Combine(dir,"measured.camera-experiment.zip");await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier,1,run,run),experiment);
                var project=Path.Combine(dir,"measured.staropt");await app.Documents.SaveAsync(project);int version;using(var raw=File.OpenRead(project)){raw.Position=52;using var unzip=new BrotliStream(raw,CompressionMode.Decompress);using var json=await JsonDocument.ParseAsync(unzip);version=json.RootElement.GetProperty("FormatVersion").GetInt32();}
                File.Delete(local);await app.NonSequentialAnalysis.ClearDetectorsAsync();app.Documents.NewBlank();await app.Documents.OpenAsync(project);
                if(app.NonSequential.GetDocument().Objects.Single(o=>o.Id==plane).SurfaceBsdf!.MeasuredData!.DataSha256!=imported.MeasuredData!.DataSha256)throw new InvalidDataException("Measured project digest mismatch");app.Documents.NewBlank();
                var opened=await app.CameraImaging.OpenExperimentAsync(experiment);var replay=await app.CameraImaging.RecomputeAsync(opened.B);var exact=run.PhotoElectrons.SequenceEqual(replay.PhotoElectrons)&&run.RawDn.SequenceEqual(replay.RawDn)&&run.Developed.Rgb24.SequenceEqual(replay.Developed.Rgb24);if(!exact)throw new InvalidDataException("Measured frozen replay mismatch");
                var result=new{Material=name,Transmission=transmission,IncidenceDegrees=incidence,WavelengthNanometers=wave,WavelengthDeclaration=transmission?"Manufacturer supplied demonstration ZMX; not independently measured spectral calibration":"Original measured spectral column",BackSideDeclaredIdenticalNotMeasured=true,SourcePowerWatts=sourcePower,data.ComputationProtocol,data.AngularSupport,data.SourceSha256,data.DataSha256,data.Points.Count,ReferencePower=reference.Power,ReferenceMeanCosine=reference.Mean,ReferenceRefinementDrift=referenceDrift,IndependentOrders=new[]{16,24},DirectionSampleCount=sampleCount,MomentError=momentError,SixSigmaAllowance=allowance,MaximumImportanceError=maxImportanceError,ClosureError=closure,EnergyError=energy,PhotonErrorElectrons=photonError,ProjectVersion=version,ExactReplay=exact,input.BsdfStatistics,FullMeasuredAngularTableRetained=true,XyzCalibrationApprovalImplied=false};
                File.WriteAllText(Path.Combine(dir,"chain.json"),JsonSerializer.Serialize(result,PositiveFits.Json));cases.Add(result);Console.WriteLine(name+" / "+incidence+": chain passed");
            }
        }
        File.WriteAllText(Path.Combine(root,"summary.json"),JsonSerializer.Serialize(new{CoreSha256=PositiveFits.Sha(typeof(MeasuredBsdf).Assembly.Location),ApplicationSha256=PositiveFits.Sha(typeof(WorkbenchApplication).Assembly.Location),DriverSha256=PositiveFits.Sha(System.Reflection.Assembly.GetExecutingAssembly().Location),Cases=cases},PositiveFits.Json));Console.WriteLine(JsonSerializer.Serialize(new{Passed=cases.Count}));
    }
}
