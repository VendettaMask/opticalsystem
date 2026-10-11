using System.Numerics;
using System.Reflection;
using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Metrics;
using OptilandWorkbench.ZemaxComparison.Normalization;

if (args.Length != 3) throw new ArgumentException("Usage: <repository-root> <fresh-output-directory> <mode>");
if (!new[] { "energy", "huygens", "knots", "dense", "full-dense", "energy-grids", "cubic-grid", "reproduce", "inputs", "rays", "airy", "plane-phase", "integer-grid", "native-huygens", "geo-nodes", "gaussian-controls", "kernel-controls", "geometry-controls", "finite-cells", "dee-native-grid", "dee-phase-controls", "dee-grid-scale" }.Contains(args[2]))
    throw new ArgumentException("Unknown diagnostic mode.");
var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Use a fresh output directory.");
Directory.CreateDirectory(output);
var source = Path.Combine(root, "validation/zemax/2026-r1/ms-l7-analysis-expansion-2026-09-06/source.ZMX");
if (Hash(source) != "8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8")
    throw new InvalidDataException("Frozen source identity differs.");
var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
optic.RayAimingEnabled = true;
var wave = optic.Wavelengths[0];
if (args.Length < 3 || args[2] == "energy") Energy();
if (args.Length < 3 || args[2] == "huygens") Huygens();
if (args.Length >= 3 && args[2] == "knots") Knots();
if (args.Length >= 3 && args[2] == "dense") Dense();
if (args.Length >= 3 && args[2] == "full-dense") Dense();
if (args.Length >= 3 && args[2] == "energy-grids") Dense();
if (args.Length >= 3 && args[2] == "cubic-grid") CubicGrid();
if (args.Length >= 3 && args[2] == "reproduce") Reproduce();
if (args.Length >= 3 && args[2] == "inputs") Inputs();
if (args.Length >= 3 && args[2] == "rays") Rays();
if (args[2] == "airy") Airy();
if (args[2] == "plane-phase") PlanePhase();
if (args[2] == "integer-grid") IntegerGrid();
if (args[2] == "native-huygens") NativeHuygens();
if (args[2] == "geo-nodes") GeoNodes();
if (args[2] == "gaussian-controls") GaussianControls();
if (args[2] is "kernel-controls" or "geometry-controls") KernelControls();
if (args[2] == "finite-cells") FiniteCells();
if (args[2] == "dee-native-grid") DeeNativeGrid();
if (args[2] == "dee-phase-controls") DeePhaseControls();
if (args[2] == "dee-grid-scale") DeeGridScale();
File.WriteAllText(Path.Combine(output, "provenance.json"), JsonSerializer.Serialize(new {
    generatedUtc = DateTime.UtcNow, mode = args.Length >= 3 ? args[2] : "all-original-controls", source,
    sourceSha256 = Hash(source), coreAssembly = typeof(DiffractionEngine).Assembly.Location,
    coreSha256 = Hash(typeof(DiffractionEngine).Assembly.Location), toolSha256 = Hash(Assembly.GetExecutingAssembly().Location),
    frozen = new[] {
        "validation/zemax/2026-r1/huygens-method-controls-2026-10-09/grid/Auto/data.json",
        "validation/zemax/2026-r1/huygens-method-controls-2026-10-09/grid/Planar/data.json",
        "validation/zemax/2026-r1/huygens-method-controls-2026-10-09/grid/Spherical/data.json",
        "validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/dee-32/data.json",
        "validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/dee-range-2p5/data.json",
        "validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/dee-range-10/data.json",
        "validation/zemax/2026-r1/fft-pupil-phase-2026-10-09/ms-l7-32/pupil-32/pupil-grid.json"
    }.Select(path => new { path, sha256 = Hash(Path.Combine(root, path)) }),
    semantics = "Offline diagnostic controls, not native certification. No reference values, tolerances, settings or product calculations are changed. Dense/control interpolation results are not substituted for captured-settings recalculation."
}, jsonOptions));

string Hash(string path) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

void DeeGridScale()
{
    var records = new List<object>();
    var saved = optic;
    foreach (var (name,sampling) in new[] { ("ms-l7",32), ("sampling64",64), ("sampling128",128), ("cooke",32), ("tessar",32) })
    foreach (var ideal in new[] { true, false })
    {
        var standard = name is "cooke" or "tessar";
        optic = standard ? OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(root,$"validation/zemax/2026-r1/fft-pupil-phase-2026-10-09/{name}-32/source.ZMX")),".zmx") : Optic.FromSnapshot(saved.ToSnapshot());
        optic.RayAimingEnabled = !standard;
        var selected = optic.Wavelengths[0];var size=sampling*2;var scale=Math.Sqrt(sampling/4d-1);
        var f = DiffractionEngine.WorkingFNumber(optic,(0,0),selected,optic.RayAimingEnabled);
        if (ideal) Console.WriteLine($"geometry {name}: selectedF={f:R}, primaryF={DiffractionEngine.WorkingFNumber(optic,(0,0),optic.Wavelengths.First(w=>w.IsPrimary),optic.RayAimingEnabled):R}, primaryEXPP={optic.Paraxial.EstimateExitPupilLocation():R}, selectedEXPP={optic.Paraxial.EstimateExitPupilLocation(selected.Micrometers):R}, primaryEXPD={optic.Paraxial.EstimateExitPupilDiameter():R}, selectedEXPD={optic.Paraxial.EstimateExitPupilDiameter(selected.Micrometers):R}");
        var pitch = selected.Micrometers * f * (sampling-2)/(size*scale);
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0,0), selected,sampling,size,ignoreOpd:ideal,cellCenteredPupil:true,zemaxFftSampling:true,aimAtStop:optic.RayAimingEnabled,referenceWavelength:selected,ignoreScalarTransmission:true,imageDelta:pitch);
        if (ideal) Console.WriteLine($"launch {name}: aim={psf.UseRayAiming}, fallback={psf.StopAimingFallbackUsed}, stretch={psf.PupilGridStretch:R}, F={psf.WorkingFNumber:R}");
        var points=Enumerable.Range(0,size).SelectMany(r=>Enumerable.Range(0,size).Select(c=>new WeightedEnergyPoint((c-size/2d+1)*psf.SampleSpacingMicrometers,(r-size/2d+1)*psf.SampleSpacingMicrometers,psf.Values[(r+1)%size,(c+1)%size]))).ToArray();
        var center=new GeometricEnergyDistribution(points,GeometricEnergyReference.Centroid,EnergyRegion.Circle).Center;
        var distribution=new DiffractionEnergyDistribution(points,center,EnergyRegion.Circle);
        var integrator=typeof(DiffractionEnergyDistribution).GetField("_grid",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(distribution)!;
        var fraction=integrator.GetType().GetMethod("Fraction")!;
        var nativePath=name=="ms-l7"?"validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/dee-32/data.json":standard?$"artifacts/validation/residuals-20261011/followup/dee-standard-controls/{name}/data.json":$"artifacts/validation/residuals-20261011/followup/dee-geometry-controls/{name}/data.json";
        using var native=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,nativePath)));
        if (native.RootElement.GetProperty("dataSeries").GetArrayLength() != 2)
        {
            records.Add(new { name, ideal, status="NativeNoData", nativeSha256=Hash(Path.Combine(root,nativePath)) });
            Console.WriteLine($"{name}: native analysis returned no curves; excluded from certification.");
            continue;
        }
        var series=native.RootElement.GetProperty("dataSeries")[ideal?0:1];
        var x=series.GetProperty("x").EnumerateArray().Select(v=>v.GetDouble()).ToArray();var target=series.GetProperty("y").EnumerateArray().Select(v=>v[0].GetDouble()).ToArray();
        var axis=Enumerable.Range(0,sampling).Select(i=>5*i/(sampling-1d)).ToArray();
        var knots=axis.Select(r => points.Sum(p => p.Weight * Math.Clamp((r-double.Hypot(p.X-center.Item1,p.Y-center.Item2))/psf.SampleSpacingMicrometers+.5,0,1))/points.Sum(p=>p.Weight)).ToArray();
        knots[0]=0;knots[1]=knots[2]/4;
        var spline=typeof(MtfThroughFocusAnalysis).GetMethod("CubicSplineInterpolate",BindingFlags.NonPublic|BindingFlags.Static)!;
        var values=(double[])spline.Invoke(null,[axis,knots,x])!;
        records.Add(new { name,scale,size,ideal,spacing=psf.SampleSpacingMicrometers,center,metric=Metric(values,target),knots,values,pixels=Enumerable.Range(0,size).Select(r=>Enumerable.Range(0,size).Select(c=>psf.Values[(r+1)%size,(c+1)%size]).ToArray()) });
        Console.WriteLine($"scale {name}/{scale:R}/{size}/{ideal}: pitch={psf.SampleSpacingMicrometers:R}, center={center}, metric={Metric(values,target)}");
    }
    File.WriteAllText(Path.Combine(output,"dee-grid-scale.json"),JsonSerializer.Serialize(records,jsonOptions));
    optic=saved;
}

void DeePhaseControls()
{
    var records = new List<object>();
    foreach (var aiming in new[] { false, true })
    foreach (var launch in new[] { false, true })
    foreach (var phase in new[] { "raw", "checker-chief", "checker" })
    foreach (var ideal in new[] { true, false })
    {
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, 64,
            ignoreOpd: ideal, cellCenteredPupil: true, zemaxFftSampling: true,
            aimAtStop: aiming, referenceWavelength: wave, ignoreScalarTransmission: launch);
        var wf = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32, true, aiming,
            zemaxCentered: true, referenceWavelength: wave);
        if (launch) wf = wf with { Samples = wf.Samples.Select(s => s.Intensity > 0 ? s with { Intensity = s.LaunchIntensity ?? s.Intensity } : s).ToArray() };
        var nodes = wf.Samples.ToDictionary(s => ((int)Math.Round(s.NormalizedPupilX * 15 + 16), (int)Math.Round(s.NormalizedPupilY * 15 + 16)));
        if (phase != "raw") wf = wf with { Samples = wf.Samples.Select(s =>
        {
            var c = (int)Math.Round(s.NormalizedPupilX * 15 + 16); var r = (int)Math.Round(s.NormalizedPupilY * 15 + 16);
            if ((c + r) % 2 != 0 || phase == "checker" && c == 16 && r == 16
                || !nodes.TryGetValue((c-1,r),out var a) || !nodes.TryGetValue((c+1,r),out var b)
                || !nodes.TryGetValue((c,r-1),out var e) || !nodes.TryGetValue((c,r+1),out var f)) return s;
            return s with { OpdWaves = (a.OpdWaves + b.OpdWaves + e.OpdWaves + f.OpdWaves) / 4 };
        }).ToArray() };
        var builder = typeof(DiffractionEngine).GetMethod("BuildComplexPupilCore", BindingFlags.NonPublic | BindingFlags.Static)!;
        var pupil = (Complex[,])builder.Invoke(null, [wf, null, 32, wave, psf.TangentialWorkingFNumber, psf.SagittalWorkingFNumber, true, 0d, 1d, ideal, true])!;
        var padded = new Complex[64,64];
        for (var r = 0; r < 32; r++) for (var c = 0; c < 32; c++) padded[r+16,c+16] = pupil[r,c];
        typeof(DiffractionEngine).GetMethod("FourierTransform2D", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [padded, false]);
        var values = Enumerable.Range(0,64).Select(r => Enumerable.Range(0,64).Select(c => padded[(r+33)%64,(c+33)%64].Magnitude * padded[(r+33)%64,(c+33)%64].Magnitude).ToArray()).ToArray();
        var points = Enumerable.Range(0,64).SelectMany(r=>Enumerable.Range(0,64).Select(c=>new WeightedEnergyPoint((c-31)*psf.SampleSpacingMicrometers,(r-31)*psf.SampleSpacingMicrometers,values[r][c]))).ToArray();
        var center = new GeometricEnergyDistribution(points,GeometricEnergyReference.Centroid,EnergyRegion.Circle).Center;
        records.Add(new { aiming,launch,phase,ideal,spacing=psf.SampleSpacingMicrometers,center,values });
        Console.WriteLine($"phase {aiming}/{launch}/{phase}/{ideal}: pitch={psf.SampleSpacingMicrometers:R}, center={center}");
    }
    File.WriteAllText(Path.Combine(output,"dee-phase-controls.json"),JsonSerializer.Serialize(records,jsonOptions));
}

void DeeNativeGrid()
{
    var records=new List<object>();
    foreach(var sampling in new[]{8,16,32,64})
    foreach(var ideal in new[]{true,false})
    {
        var psf=DiffractionEngine.ComputeFftPsf(optic,(0,0),wave,sampling,sampling*2,ignoreOpd:ideal,cellCenteredPupil:true,zemaxFftSampling:true,aimAtStop:true,referenceWavelength:wave,ignoreScalarTransmission:true);
        foreach(var window in new[]{"full","half"})
        foreach(var origin in new[]{0,1})
        {
            var size=window=="half"?sampling:sampling*2;var start=(psf.GridSize-size)/2;
            var points=Enumerable.Range(0,size).SelectMany(r=>Enumerable.Range(0,size).Select(c=>new WeightedEnergyPoint((c-size/2d+origin)*psf.SampleSpacingMicrometers,(r-size/2d+origin)*psf.SampleSpacingMicrometers,psf.Values[r+start,c+start]))).ToArray();
            var center=new GeometricEnergyDistribution(points,GeometricEnergyReference.Centroid,EnergyRegion.Circle).Center;
            var distribution=new DiffractionEnergyDistribution(points,center,EnergyRegion.Circle);
            var integrator=typeof(DiffractionEnergyDistribution).GetField("_grid",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(distribution)!;
            var fraction=integrator.GetType().GetMethod("Fraction")!;
            foreach(var range in new[]{2.5,5,10})
            {
                var slug=range==2.5?"dee-range-2p5":range==5?"dee-32":"dee-range-10";
                using var native=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,$"validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/{slug}/data.json")));
                var series=native.RootElement.GetProperty("dataSeries")[ideal?0:1];
                var xx=series.GetProperty("x").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                var target=series.GetProperty("y").EnumerateArray().Select(v=>v[0].GetDouble()).ToArray();
                var axis=Enumerable.Range(0,32).Select(i=>range*i/31).ToArray();
                var knots=axis.Select(r=>(double)fraction.Invoke(integrator,[r])!).ToArray();knots[1]=knots[2]/4;
                var spline=typeof(MtfThroughFocusAnalysis).GetMethod("CubicSplineInterpolate",BindingFlags.NonPublic|BindingFlags.Static)!;
                var values=(double[])spline.Invoke(null,[axis,knots,xx])!;
                records.Add(new{sampling,ideal,window,origin,range,center,metric=Metric(values,target),knots,values});
                if(range==5)Console.WriteLine($"internal grid {sampling}/{ideal}/{window}/{origin}: center={center}, {Metric(values,target)}");
            }
        }
    }
    File.WriteAllText(Path.Combine(output,"dee-native-grid.json"),JsonSerializer.Serialize(records,jsonOptions));
}

void FiniteCells()
{
    var records=new List<object>();
    foreach(var ideal in new[]{true,false})
    foreach(var imageSize in new[]{64,512})
    {
        var psf=DiffractionEngine.ComputeFftPsf(optic,(0,0),wave,32,imageSize,ignoreOpd:ideal,cellCenteredPupil:true,zemaxFftSampling:true,aimAtStop:true,referenceWavelength:wave,ignoreScalarTransmission:true);
        var period=psf.SampleSpacingMicrometers*imageSize;
        var airyRange=.61*wave.Micrometers*Math.Abs(optic.Paraxial.EstimateExitPupilLocation())*30/Math.Abs(optic.Paraxial.EstimateExitPupilDiameter());
        foreach(var extent in new[]{period,period/2,airyRange,airyRange/2})
        foreach(var window in new[]{"full","half"})
        {
            var size=window=="half"?imageSize/2:imageSize;var start=(imageSize-size)/2;
            double Sinc(double x)=>Math.Abs(x)<1e-9?1:Math.Sin(x)/x;
            var points=Enumerable.Range(0,size).SelectMany(r=>Enumerable.Range(0,size).Select(c=>
            {
                var x=(c-size/2d)*psf.SampleSpacingMicrometers;var y=(r-size/2d)*psf.SampleSpacingMicrometers;
                var footprint=Math.Abs(x)>=extent||Math.Abs(y)>=extent?0:Sinc(Math.PI*x/extent)*Sinc(Math.PI*y/extent);
                return new WeightedEnergyPoint(x,y,psf.Values[r+start,c+start]*footprint*footprint);
            })).ToArray();
            var center=new GeometricEnergyDistribution(points,GeometricEnergyReference.Centroid,EnergyRegion.Circle).Center;
            var distribution=new DiffractionEnergyDistribution(points,center,EnergyRegion.Circle);
            var integrator=typeof(DiffractionEnergyDistribution).GetField("_grid",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(distribution)!;
            var fraction=integrator.GetType().GetMethod("Fraction")!;
            foreach(var range in new[]{2.5,5,10})
            {
                var slug=range==2.5?"dee-range-2p5":range==5?"dee-32":"dee-range-10";
                using var native=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,$"validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/{slug}/data.json")));
                var series=native.RootElement.GetProperty("dataSeries")[ideal?0:1];
                var xx=series.GetProperty("x").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                var target=series.GetProperty("y").EnumerateArray().Select(v=>v[0].GetDouble()).ToArray();
                var axis=Enumerable.Range(0,32).Select(i=>range*i/31).ToArray();
                var knots=axis.Select(r=>(double)fraction.Invoke(integrator,[r])!).ToArray();knots[1]=knots[2]/4;
                var spline=typeof(MtfThroughFocusAnalysis).GetMethod("CubicSplineInterpolate",BindingFlags.NonPublic|BindingFlags.Static)!;
                var values=(double[])spline.Invoke(null,[axis,knots,xx])!;
                records.Add(new{ideal,imageSize,period,extent,window,range,center,total=points.Sum(p=>p.Weight),metric=Metric(values,target),knots,values});
                Console.WriteLine($"finite {ideal}/{imageSize}/{extent}/{window}/{range}: {Metric(values,target)}");
            }
        }
    }
    File.WriteAllText(Path.Combine(output,"finite-cells.json"),JsonSerializer.Serialize(records,jsonOptions));
}

void KernelControls()
{
    var records = new List<object>();
    var saved = optic;
    foreach (var (name, sampling, waveIndex) in args[2]=="geometry-controls" ? new[] { ("half-aperture",32,0), ("on-axis-only",32,0), ("single-wave",32,0), ("cooke",32,0), ("tessar",32,0) } : new[] { ("image64",32,0), ("delta-half",32,0), ("pupil64",64,0), ("wave2",32,1) })
    {
        optic=Optic.FromSnapshot(saved.ToSnapshot());
        if(name is "cooke" or "tessar") optic=OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(root,$"validation/zemax/2026-r1/fft-pupil-phase-2026-10-09/{name}-32/source.ZMX")),".zmx");
        optic.RayAimingEnabled=true;
        if(name=="half-aperture") optic.Aperture.Value*=.5;
        if(name=="on-axis-only") while(optic.Fields.Count>1)optic.Fields.RemoveAt(optic.Fields.Count-1);
        if(name=="single-wave") {while(optic.Wavelengths.Count>1)optic.Wavelengths.RemoveAt(optic.Wavelengths.Count-1);optic.Wavelengths[0].IsPrimary=true;}
        var selected = optic.Wavelengths[waveIndex];
        var wf = WavefrontEngine.GenerateChiefRayUniform(optic,(0,0),selected,sampling,zemaxCentered:true,aimAtStop:true);
        var pupils = wf.Samples.Select(s=>new PupilSample(s.NormalizedPupilX,s.NormalizedPupilY,1)).ToArray();
        var rays = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0,0,selected.Micrometers,pupils,true));
        var chief = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0,0,0,0,selected.Micrometers,true)).Single()!;
        var frame = DiffractionEngine.CreateHuygensImageFrame(optic,(0,0),selected,true);
        var folder = Path.Combine(root,"artifacts/validation/residuals-20261011/followup/"+args[2]);
        using var real = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,name+"-real/Planar/data.json")));
        using var imag = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,name+"-imaginary/Planar/data.json")));
        var grid=real.RootElement.GetProperty("dataGrids")[0];
        var rv=grid.GetProperty("values").EnumerateArray().SelectMany(r=>r.EnumerateArray().Select(v=>v.GetDouble())).ToArray();
        var iv=imag.RootElement.GetProperty("dataGrids")[0].GetProperty("values").EnumerateArray().SelectMany(r=>r.EnumerateArray().Select(v=>v.GetDouble())).ToArray();
        var native=rv.Zip(iv,(r,i)=>new Complex(r,i)).ToArray();
        var size=grid.GetProperty("nx").GetInt32();
        var center=size/2*size+size/2;
        var amplitude=pupils.Select((p,i)=>rays[i] is null?0:Math.Exp(-512*(p.X*p.X+p.Y*p.Y))).ToArray();
        var actual=new Complex[size*size];
        for(var row=0;row<size;row++)for(var col=0;col<size;col++)
        {
            var x=(grid.GetProperty("minX").GetDouble()+col*grid.GetProperty("dx").GetDouble())/1000;
            var y=(grid.GetProperty("minY").GetDouble()+row*grid.GetProperty("dy").GetDouble())/1000;
            var point=frame.Center+frame.TangentX*x+frame.TangentY*y;
            var sum=Complex.Zero;
            for(var i=0;i<rays.Count;i++)if(rays[i] is {} ray)
            {
                var offset=point-ray.Position;
                var phase=ray.CumulativeOpticalPathLength-chief.CumulativeOpticalPathLength+wf.ImageRefractiveIndex*(ray.Direction.X*offset.X+ray.Direction.Y*offset.Y+ray.Direction.Z*offset.Z);
                sum+=Complex.FromPolarCoordinates(amplitude[i],-2*Math.PI*phase/(selected.Micrometers/1000));
            }
            actual[row*size+col]=sum/amplitude.Sum();
        }
        var piston=Complex.FromPolarCoordinates(1,(native[center]/actual[center]).Phase);
        var ratio=Enumerable.Range(0,size/2).Select(i=>native[center+i]/(actual[center+i]*piston)).ToArray();
        var pitch=grid.GetProperty("dx").GetDouble();
        var target=ratio[1].Magnitude/ratio[0].Magnitude;
        var lo=0d;var hi=2d;
        for(var j=0;j<70;j++){var t=(lo+hi)/2;var v=Math.Sin(t*pitch)/(t*pitch);if(v>target)lo=t;else hi=t;}
        var coefficient=(lo+hi)/2;
        Console.WriteLine($"geometry: EFL={optic.Paraxial.EstimateEffectiveFocalLength():R}, F={optic.Paraxial.EstimateFNumber():R}, EP={optic.Paraxial.EstimateEntrancePupilDiameter():R}, EPZ={optic.Paraxial.EstimateEntrancePupilLocation():R}, XPZ={optic.Paraxial.EstimateExitPupilLocation():R}; pupilExtent={wf.Samples.Max(s=>s.PupilX)-wf.Samples.Min(s=>s.PupilX):R}; imageZ={frame.Center.Z:R}");
        var marginal=optic.Paraxial.MarginalRay(selected.Micrometers);
        var card=optic.Paraxial.CardinalPlanes(1,optic.SurfaceGroup.Items.Count-2,selected.Micrometers,0);
        Console.WriteLine($"more geometry: selectedEFL={card.ImageFocalLength:R}, XPdiam={optic.Paraxial.EstimateExitPupilDiameter(selected.Micrometers):R}, marginalFinalSlope={marginal.Slopes[^1][0]:R}; sincConstant={coefficient*selected.Micrometers*(sampling-2)/Math.PI:R}; expectedEFL={Math.Sqrt(2)*optic.Paraxial.EstimateEntrancePupilDiameter()/card.ImageFocalLength:R}");
        var predicted=Math.PI*Math.Abs(optic.Paraxial.EstimateExitPupilDiameter())/(.61*selected.Micrometers*Math.Abs(optic.Paraxial.EstimateExitPupilLocation())*(sampling-2));
        Console.WriteLine($"Airy-cell: XPdiam-primary={optic.Paraxial.EstimateExitPupilDiameter():R}, predicted={predicted:R}, ratio={coefficient/predicted:R}");
        if(name=="image64") foreach(var fy in new[]{0d,-10.6/15,-1d})
        {
            var rr=optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0,fy,selected.Micrometers,pupils,true)).Where(r=>r is not null).Select(r=>r!).ToArray();
            Console.WriteLine($"field {fy}: l={rr.Min(r=>r.Direction.X):R}..{rr.Max(r=>r.Direction.X):R}, m={rr.Min(r=>r.Direction.Y):R}..{rr.Max(r=>r.Direction.Y):R}, radial={rr.Max(r=>Math.Sqrt(r.Direction.X*r.Direction.X+r.Direction.Y*r.Direction.Y)):R}");
        }
        records.Add(new{name,sampling,wavelength=selected.Micrometers,pitch,coefficient,sincZero=Math.PI/coefficient,
            axes=DiffractionEngine.WorkingFNumbers(optic,(0,0),selected,true,zemaxDirectionalAverage:true),radius=wf.Radius,
            phase=ratio.Select(v=>v.Phase),amplitude=ratio.Select(v=>v.Magnitude),
            actualReal=actual.Select(v=>v.Real),actualImaginary=actual.Select(v=>v.Imaginary)});
        Console.WriteLine($"kernel {name}: amplitude0={ratio[0].Magnitude:R}, coefficient={coefficient:R}, zero={Math.PI/coefficient:R}, phase1={ratio[1].Phase:R}");
    }
    File.WriteAllText(Path.Combine(output,"kernel-controls.json"),JsonSerializer.Serialize(records,jsonOptions));
    optic=saved;
}

void GaussianControls()
{
    var chief = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0,0,0,0,wave.Micrometers,true)).Single()!;
    var frame = DiffractionEngine.CreateHuygensImageFrame(optic,(0,0),wave,true);
    var records = new List<object>();
    foreach (var nodes in new[] { "endpoints", "centers", "zemax", "halfopen", "closed32intervals" })
    {
        var wf = WavefrontEngine.GenerateChiefRayUniform(optic, (0,0), wave, 32, cellCentered:nodes=="centers",zemaxCentered:nodes=="zemax",aimAtStop:true);
        var pupils = nodes is "halfopen" or "closed32intervals"
            ? Enumerable.Range(0,nodes=="halfopen"?32:33).SelectMany(r=>Enumerable.Range(0,nodes=="halfopen"?32:33).Select(c=>new PupilSample((c-16)/16d,(r-16)/16d,1))).Where(p=>p.X*p.X+p.Y*p.Y<=1).ToArray()
            : wf.Samples.Select(s=>new PupilSample(s.NormalizedPupilX,s.NormalizedPupilY,1)).ToArray();
        var rays = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0,0,wave.Micrometers,pupils,true));
        foreach (var factor in new[] {4,32,512})
        foreach (var weight in new[] {"uniform","cos","sqrtcos","invcos"})
        {
            var path = Path.Combine(root,$"artifacts/validation/residuals-20261011/followup/gaussian/{factor}-linear/Planar/data.json");
            using var native = JsonDocument.Parse(File.ReadAllText(path));
            var grid = native.RootElement.GetProperty("dataGrids")[0];
            var target = grid.GetProperty("values").EnumerateArray().SelectMany(r=>r.EnumerateArray().Select(v=>v.GetDouble())).ToArray();
            var amplitude = pupils.Select((p,i)=>rays[i] is null?0:Math.Exp(-factor*(p.X*p.X+p.Y*p.Y))*(weight=="cos"?rays[i]!.Direction.Z:weight=="sqrtcos"?Math.Sqrt(rays[i]!.Direction.Z):weight=="invcos"?1/rays[i]!.Direction.Z:1)).ToArray();
            var normalization = Math.Pow(amplitude.Sum(),2);
            var values = new double[1024];
            for(var row=0;row<32;row++)for(var column=0;column<32;column++)
            {
                var x=(grid.GetProperty("minX").GetDouble()+column*grid.GetProperty("dx").GetDouble())/1000;
                var y=(grid.GetProperty("minY").GetDouble()+row*grid.GetProperty("dy").GetDouble())/1000;
                var point=frame.Center+frame.TangentX*x+frame.TangentY*y;
                var sum=Complex.Zero;
                for(var i=0;i<rays.Count;i++)if(rays[i] is {} ray)
                {
                    var offset=point-ray.Position;
                    var phase=ray.CumulativeOpticalPathLength-chief.CumulativeOpticalPathLength+wf.ImageRefractiveIndex*(ray.Direction.X*offset.X+ray.Direction.Y*offset.Y+ray.Direction.Z*offset.Z);
                    sum+=Complex.FromPolarCoordinates(amplitude[i],2*Math.PI*phase/(wave.Micrometers/1000));
                }
                values[row*32+column]=sum.Magnitude*sum.Magnitude/normalization;
            }
            var metric=Metric(values,target);
            records.Add(new{nodes,factor,weight,nativeSha256=Hash(path),metric,values});
            Console.WriteLine($"gaussian {nodes}/{factor}/{weight}: {metric}");
        }
    }
    File.WriteAllText(Path.Combine(output,"gaussian-controls.json"),JsonSerializer.Serialize(records,jsonOptions));
}

void GeoNodes()
{
    var records = new List<object>();
    var image = optic.SurfaceGroup.Items[^1].CoordinateSystem;
    foreach (var fieldIndex in new[] { 1, 2 })
    {
        (double Hx, double Hy) field = FieldCoordinates.Normalize(optic.Fields, optic.Fields[fieldIndex].X, optic.Fields[fieldIndex].Y);
        foreach (var count in new[] { 30,31,32,33,34,64 })
        foreach (var nodes in new[] { "endpoints", "centers", "zemax", "halfopen", "closedintervals" })
        {
            var wf = WavefrontEngine.GenerateChiefRayUniform(optic, field, wave, count, cellCentered: nodes == "centers", zemaxCentered: nodes == "zemax", aimAtStop: true);
            var pupils = nodes is "halfopen" or "closedintervals"
                ? Enumerable.Range(0, nodes == "halfopen" ? count : count+1).SelectMany(r => Enumerable.Range(0, nodes == "halfopen" ? count : count+1).Select(c => new PupilSample((c - count / 2d) / (count / 2d), (r - count / 2d) / (count / 2d), 1))).Where(p => p.X*p.X+p.Y*p.Y<=1).ToArray()
                : wf.Samples.Select(s => new PupilSample(s.NormalizedPupilX, s.NormalizedPupilY, 1)).ToArray();
            var rays = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(field.Hx, field.Hy, wave.Micrometers, pupils, true)).Where(r=>r is not null && r.Intensity>0).ToArray();
            var x = rays.Average(r=>image.ToLocalPoint(r!.Position).X); var y=rays.Average(r=>image.ToLocalPoint(r!.Position).Y);
            records.Add(new { fieldNumber=fieldIndex+1, count, nodes, valid=rays.Length, x, y });
            Console.WriteLine($"geo-nodes F{fieldIndex+1}/{count}/{nodes}/{rays.Length}: ({x:R},{y:R}) mm");
        }
    }
    File.WriteAllText(Path.Combine(output,"geo-nodes.json"),JsonSerializer.Serialize(records,jsonOptions));
}

void NativeHuygens()
{
    var directory = Path.Combine(root, "artifacts/validation/residuals-20261011/followup/native");
    using var real = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "huygens-real/Planar/data.json")));
    using var imaginary = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "huygens-imaginary/Planar/data.json")));
    var rg = real.RootElement.GetProperty("dataGrids")[0]; var ig = imaginary.RootElement.GetProperty("dataGrids")[0];
    var rv = rg.GetProperty("values").EnumerateArray().SelectMany(r => r.EnumerateArray().Select(v => v.GetDouble())).ToArray();
    var iv = ig.GetProperty("values").EnumerateArray().SelectMany(r => r.EnumerateArray().Select(v => v.GetDouble())).ToArray();
    var target = rv.Zip(iv, (r, i) => new Complex(r, i)).ToArray();
    var records = new List<object>();
    foreach (var nodes in new[] { "endpoints", "zemax" })
    {
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, $"pupil-{nodes}/pupil-grid.json")));
        var inputs = input.RootElement.GetProperty("inputs").EnumerateArray().ToArray();
        var rows = input.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        var pupils = inputs.Select(p => new PupilSample(p.GetProperty("px").GetDouble(), p.GetProperty("py").GetDouble(), 1)).ToArray();
        var wf = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), wave, pupils.Select(p => (p.X, p.Y)).ToArray(), aimAtStop: true, referenceWavelength: wave);
        var rays = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0, wave.Micrometers, pupils, true));
        double Value(int i, string key) => rows[i].GetProperty(key).GetDouble();
        var chief = Array.FindIndex(pupils, p => p.X == 0 && p.Y == 0);
        var chiefOpl = rays[chief]!.CumulativeOpticalPathLength;
        var opdErrors = Enumerable.Range(0, pupils.Length).Select(i => wf.Samples[i].OpdWaves - Value(i, "opd")).ToArray();
        Console.WriteLine($"native rays {nodes}: OPD max={opdErrors.Max(Math.Abs):R}, chief={Value(chief, "opd"):R}, R={wf.Radius:R}");
        foreach (var sourcePhase in new[] { "formal-opl", "native-opd", "formal-opd" })
        foreach (var weight in new[] { "uniform", "cos", "sqrtcos", "invcos" })
        {
            var amplitudes = Enumerable.Range(0, pupils.Length).Select(i => weight switch { "cos" => Value(i, "n"), "sqrtcos" => Math.Sqrt(Value(i, "n")), "invcos" => 1 / Value(i, "n"), _ => 1d }).ToArray();
            if (nodes == "endpoints") amplitudes[chief] = 0; // Explicit audit chief is not an extra integration node.
            var peak = amplitudes.Sum();
            var values = new Complex[1024];
            for (var row = 0; row < 32; row++)
            for (var column = 0; column < 32; column++)
            {
                var x = (rg.GetProperty("minX").GetDouble() + column * rg.GetProperty("dx").GetDouble()) / 1000;
                var y = (rg.GetProperty("minY").GetDouble() + row * rg.GetProperty("dy").GetDouble()) / 1000;
                var sum = Complex.Zero;
                for (var i = 0; i < pupils.Length; i++)
                {
                    var px = Value(i, "x"); var py = Value(i, "y"); var pz = Value(i, "z");
                    var l = Value(i, "l"); var m = Value(i, "m"); var n = Value(i, "n");
                    var dot = px * l + py * m + pz * n;
                    var path = sourcePhase == "formal-opl" ? rays[i]!.CumulativeOpticalPathLength - chiefOpl + wf.ImageRefractiveIndex * (l * (x - px) + m * (y - py) - n * pz)
                        : -(sourcePhase == "native-opd" ? Value(i, "opd") : wf.Samples[i].OpdWaves) * wave.Micrometers / 1000
                            + wf.ImageRefractiveIndex * (Math.Sqrt(dot * dot + wf.Radius * wf.Radius - px * px - py * py - pz * pz) - wf.Radius + l * x + m * y);
                    sum += Complex.FromPolarCoordinates(amplitudes[i], 2 * Math.PI * path / (wave.Micrometers / 1000));
                }
                values[row * 32 + column] = sum / peak;
            }
            // Only a global, intensity-invariant piston is removed for complex
            // diagnostic comparison. Neither this piston nor native amplitudes
            // may be substituted into a product calculation.
            var phase = (target[528] / values[528]).Phase;
            var rotation = Complex.FromPolarCoordinates(1, phase);
            var complexL2 = Math.Sqrt(values.Zip(target, (a, b) => Complex.Abs(a * rotation - b) * Complex.Abs(a * rotation - b)).Sum() / target.Sum(v => v.Magnitude * v.Magnitude));
            var metric = Metric(values.Select(v => v.Magnitude * v.Magnitude).ToArray(), target.Select(v => v.Magnitude * v.Magnitude).ToArray());
            records.Add(new { nodes, sourcePhase, weight, opdMaximum = opdErrors.Max(Math.Abs), complexL2, pistonRadians = phase, metric, real = values.Select(v => v.Real), imaginary = values.Select(v => v.Imaginary) });
            Console.WriteLine($"native-huygens {nodes}/{sourcePhase}/{weight}: intensity={metric}, complex={complexL2:R}");
        }
    }
    File.WriteAllText(Path.Combine(output, "native-huygens.json"), JsonSerializer.Serialize(records, jsonOptions));
}

void IntegerGrid()
{
    var records = new List<object>();
    var bilinear = typeof(PsfAnalysis).GetMethod("BilinearSample", BindingFlags.NonPublic | BindingFlags.Static)!;
    foreach (var ideal in new[] { true, false })
    foreach (var nodeFamily in new[] { "zemax", "endpoints", "centers" })
    {
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, 64, ignoreOpd: ideal,
            cellCenteredPupil: nodeFamily != "endpoints", zemaxFftSampling: nodeFamily == "zemax", aimAtStop: true, referenceWavelength: wave, ignoreScalarTransmission: true);
        foreach (var size in new[] { 32, 64 })
        foreach (var origin in new[] { 0d, .5, 1 })
        {
            var pitch = psf.SampleSpacingMicrometers;
            var points = Enumerable.Range(0, size).SelectMany(row => Enumerable.Range(0, size).Select(column =>
            {
                var x = (column - size / 2d + origin) * pitch;
                var y = (row - size / 2d + origin) * pitch;
                return new WeightedEnergyPoint(x, y, (double)bilinear.Invoke(null, [psf, x, y])!);
            })).ToArray();
            var center = new GeometricEnergyDistribution(points, GeometricEnergyReference.Centroid, EnergyRegion.Circle).Center;
            var distribution = new DiffractionEnergyDistribution(points, center, EnergyRegion.Circle);
            var integrator = typeof(DiffractionEnergyDistribution).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(distribution)!;
            var fraction = integrator.GetType().GetMethod("Fraction")!;
            foreach (var range in new[] { 2.5, 5, 10 })
            {
                var name = range == 5 ? "dee-32" : range == 2.5 ? "dee-range-2p5" : "dee-range-10";
                using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, $"validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/{name}/data.json")));
                var series = native.RootElement.GetProperty("dataSeries")[ideal ? 0 : 1];
                var x = series.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                var y = series.GetProperty("y").EnumerateArray().Select(v => v[0].GetDouble()).ToArray();
                var axis = Enumerable.Range(0, 32).Select(i => range * i / 31).ToArray();
                var knots = axis.Select(r => (double)fraction.Invoke(integrator, [r])!).ToArray();
                foreach (var display in new[] { "direct", "spline", "origin" })
                {
                    var corrected = (double[])knots.Clone();
                    if (display == "origin") corrected[1] = corrected[2] / 4;
                    var spline = typeof(MtfThroughFocusAnalysis).GetMethod("CubicSplineInterpolate", BindingFlags.NonPublic | BindingFlags.Static)!;
                    var values = display == "direct" ? x.Select(r => (double)fraction.Invoke(integrator, [r])!).ToArray()
                        : ((double[])spline.Invoke(null, [axis, corrected, x])!).Select(v => Math.Clamp(v, 0, 1)).ToArray();
                    records.Add(new { ideal, nodeFamily, size, origin, display, range, center, total = points.Sum(p => p.Weight), metric = Metric(values, y), knots = corrected, values });
                    if (range == 5 && origin == 1 && display == "origin") Console.WriteLine($"integer-grid {ideal}/{nodeFamily}/{size}/{origin}/{display}/{range} center={center}: {Metric(values, y)}");
                }
            }
        }
    }
    File.WriteAllText(Path.Combine(output, "integer-grid.json"), JsonSerializer.Serialize(records, jsonOptions));
}

void Airy()
{
    var method = typeof(DiffractionEncircledEnergyAnalysis).GetMethod("IdealAiryEncircledEnergy", BindingFlags.NonPublic | BindingFlags.Static)!;
    using var knots = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "artifacts/validation/residuals-20261011/verified/knots/knots.json")));
    var axes = DiffractionEngine.WorkingFNumbers(optic, (0, 0), wave, true, zemaxDirectionalAverage: true);
    var f = Math.Sqrt(axes.Tangential * axes.Sagittal);
    var rows = new List<object>();
    foreach (var item in knots.RootElement.EnumerateArray().Where(r => r.GetProperty("ideal").GetBoolean()))
    {
        var radii = item.GetProperty("axis").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var native = item.GetProperty("nativeKnots").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var values = radii.Select(r => (double)method.Invoke(null, [r, wave.Micrometers, f])!).ToArray();
        rows.Add(new { range = item.GetProperty("range").GetDouble(), f, radii, native, values, metric = Metric(values, native) });
        Console.WriteLine($"Airy range={item.GetProperty("range")} f={f:R}: {Metric(values, native)}");
        for (var i = 0; i < 6; i++) Console.WriteLine($"r={radii[i]:R} native={native[i]:R} Airy={values[i]:R}");
    }
    File.WriteAllText(Path.Combine(output, "airy.json"), JsonSerializer.Serialize(rows, jsonOptions));
}

void PlanePhase()
{
    using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "validation/zemax/2026-r1/huygens-method-controls-2026-10-09/grid/Auto/data.json")));
    var grid = native.RootElement.GetProperty("dataGrids")[0];
    var target = grid.GetProperty("values").EnumerateArray().SelectMany(r => r.EnumerateArray().Select(v => v.GetDouble())).ToArray();
    var chief = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, wave.Micrometers, true)).Single()!;
    var frame = DiffractionEngine.CreateHuygensImageFrame(optic, (0, 0), wave, true);
    var k = 2 * Math.PI / (wave.Micrometers * .001);
    var records = new List<object>();
    foreach (var nodes in new[] { "endpoints", "centers", "zemax", "halfopen", "closed32intervals" })
    {
        var wf = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32, cellCentered: nodes == "centers", zemaxCentered: nodes == "zemax", aimAtStop: true);
        var pupils = nodes is "halfopen" or "closed32intervals"
            ? Enumerable.Range(0, nodes == "halfopen" ? 32 : 33).SelectMany(r => Enumerable.Range(0, nodes == "halfopen" ? 32 : 33).Select(c => new PupilSample((c - 16) / 16d, (r - 16) / 16d, 1))).Where(p => p.X * p.X + p.Y * p.Y <= 1).ToArray()
            : wf.Samples.Select(s => new PupilSample(s.NormalizedPupilX, s.NormalizedPupilY, 1)).ToArray();
        var rays = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0, wave.Micrometers, pupils, true));
        var pitch = nodes == "zemax" ? 1 / 15d : nodes == "endpoints" ? 2 / 31d : 1 / 16d;
        var lookup = pupils.Select((p, i) => (p, i)).ToDictionary(v => ((long)Math.Round(v.p.X / pitch * 1000), (long)Math.Round(v.p.Y / pitch * 1000)), v => v.i);
        foreach (var interpolation in new[] { "none", "even", "odd", "all" })
        foreach (var weight in new[] { "uniform", "cos", "sqrtcos", "simpson", "simpson-cos", "trapezoid", "trapezoid-cos" })
        {
            if (interpolation != "none" && weight.Contains('-') || interpolation != "none" && weight is "simpson" or "trapezoid") continue;
            var paths = rays.Select(r => r is null ? 0 : r.CumulativeOpticalPathLength - chief.CumulativeOpticalPathLength).ToArray();
            for (var i = 0; i < paths.Length; i++)
            {
                var p = pupils[i]; var x = (long)Math.Round(p.X / pitch * 1000); var y = (long)Math.Round(p.Y / pitch * 1000);
                var parity = ((long)Math.Round(p.X / pitch) + (long)Math.Round(p.Y / pitch)) & 1;
                if (interpolation == "none" || p.X * p.X + p.Y * p.Y < 1e-24 || interpolation == "even" && parity != 0 || interpolation == "odd" && parity == 0) continue;
                var neighbors = new[] { (x - 1000, y), (x + 1000, y), (x, y - 1000), (x, y + 1000) }.Select(key => lookup.GetValueOrDefault(key, -1)).ToArray();
                if (neighbors.Any(j => j < 0 || rays[j] is null)) continue;
                paths[i] = neighbors.Average(j => rays[j]!.CumulativeOpticalPathLength - chief.CumulativeOpticalPathLength);
            }
            double Quadrature(int i)
            {
                var offset = nodes is "zemax" ? 16 : nodes is "halfopen" or "closed32intervals" ? 16 : 15.5;
                var size = nodes == "closed32intervals" ? 33 : 32;
                var x = (int)Math.Round(pupils[i].X / pitch + offset); var y = (int)Math.Round(pupils[i].Y / pitch + offset);
                double Factor(int j) => j == 0 || j == size - 1 ? (weight.StartsWith("simpson") ? 1 / 3d : .5)
                    : weight.StartsWith("simpson") ? (j % 2 == 0 ? 2 : 4) / 3d : 1;
                return weight.StartsWith("simpson") || weight.StartsWith("trapezoid") ? Factor(x) * Factor(y) : 1;
            }
            var amplitudes = rays.Select((r,i) => r is null || r.Intensity <= 0 ? 0 : Quadrature(i) * (weight == "cos" || weight.EndsWith("-cos") ? r.Direction.Z / chief.Direction.Z : weight == "sqrtcos" ? Math.Sqrt(r.Direction.Z / chief.Direction.Z) : 1)).ToArray();
            var peak = Math.Pow(amplitudes.Sum(), 2);
            var values = new double[1024];
            for (var row = 0; row < 32; row++)
            for (var column = 0; column < 32; column++)
            {
                var point = frame.Center + frame.TangentX * ((grid.GetProperty("minX").GetDouble() + column * grid.GetProperty("dx").GetDouble()) / 1000) + frame.TangentY * ((grid.GetProperty("minY").GetDouble() + row * grid.GetProperty("dy").GetDouble()) / 1000);
                var sum = Complex.Zero;
                for (var i = 0; i < rays.Count; i++)
                {
                    if (rays[i] is not { } ray) continue;
                    var offset = point - ray.Position;
                    var phase = paths[i] + wf.ImageRefractiveIndex * (ray.Direction.X * offset.X + ray.Direction.Y * offset.Y + ray.Direction.Z * offset.Z);
                    sum += Complex.FromPolarCoordinates(amplitudes[i], k * phase);
                }
                values[row * 32 + column] = sum.Magnitude * sum.Magnitude / peak;
            }
            records.Add(new { nodes, interpolation, weight, metric = Metric(values, target), values });
            Console.WriteLine($"plane-phase {nodes}/{interpolation}/{weight}: {Metric(values, target)}");
        }
    }
    File.WriteAllText(Path.Combine(output, "plane-phase.json"), JsonSerializer.Serialize(records, jsonOptions));
}

void Rays()
{
    var nativePath = Path.Combine(root, "validation/zemax/2026-r1/fft-pupil-phase-2026-10-09/ms-l7-32/pupil-32/pupil-grid.json");
    using var native = JsonDocument.Parse(File.ReadAllText(nativePath));
    var inputs = native.RootElement.GetProperty("inputs").EnumerateArray().ToArray();
    var nativeRows = native.RootElement.GetProperty("rows").EnumerateArray().ToArray();
    var pupils = inputs.Select(p => new PupilSample(p.GetProperty("px").GetDouble(), p.GetProperty("py").GetDouble(), 1)).ToArray();
    var rays = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0, wave.Micrometers, pupils, true));
    var wf = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), wave, pupils.Select(p => (p.X, p.Y)).ToArray(), referenceWavelength: wave);
    var frame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
    var rows = Enumerable.Range(0, pupils.Length).Select(i =>
    {
        if (nativeRows[i].GetProperty("error").GetInt32() != 0 || nativeRows[i].GetProperty("vignette").GetInt32() != 0 || rays[i] is null)
            throw new InvalidDataException("This valid-ray control must preserve propagation/vignetting masks.");
        var ray = rays[i]!;
        var position = frame.ToLocalPoint(ray.Position);
        var direction = frame.ToLocalDirection(ray.Direction);
        double Value(string name) => nativeRows[i].GetProperty(name).GetDouble();
        return new { pupils[i].X, pupils[i].Y,
            dx = position.X - Value("x"), dy = position.Y - Value("y"), dz = position.Z - Value("z"),
            dl = direction.X - Value("l"), dm = direction.Y - Value("m"), dn = direction.Z - Value("n"),
            dOpdWaves = wf.Samples[i].OpdWaves - Value("opd"), dIntensity = ray.Intensity - Value("intensity") };
    }).ToArray();
    var summary = new { count = rows.Length, nativeSha256 = Hash(nativePath),
        maximumPositionDifferenceMillimeters = rows.Max(r => Math.Max(Math.Max(Math.Abs(r.dx), Math.Abs(r.dy)), Math.Abs(r.dz))),
        maximumDirectionDifference = rows.Max(r => Math.Max(Math.Max(Math.Abs(r.dl), Math.Abs(r.dm)), Math.Abs(r.dn))),
        maximumOpdDifferenceWaves = rows.Max(r => Math.Abs(r.dOpdWaves)), maximumIntensityDifference = rows.Max(r => Math.Abs(r.dIntensity)),
        semantics = "Same normalized native pupil inputs; native image-local position/direction compared through the formal image coordinate system. Reference-sphere OPD remains distinct from cumulative optical path.", rows };
    File.WriteAllText(Path.Combine(output, "rays.json"), JsonSerializer.Serialize(summary, jsonOptions));
    Console.WriteLine($"rays={summary.count}; position={summary.maximumPositionDifferenceMillimeters:R} mm; direction={summary.maximumDirectionDifference:R}; OPD={summary.maximumOpdDifferenceWaves:R} waves; intensity={summary.maximumIntensityDifference:R}");
}

void Reproduce()
{
    var rows = new List<object>();
    foreach (var (key, slug) in new[] { ("Huygens PSF Cross Section", "huygens-psf-cross-section"), ("Diffraction Encircled Energy", "diffraction-encircled-energy") })
    {
        var directory = Path.Combine(root, $"validation/zemax/2026-r1/ms-l7-analysis-expansion-2026-09-06/{slug}-c1");
        using var captured = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "captured-settings.json")));
        var request = captured.RootElement.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var entry = AnalysisComparisonRegistry.Get(key);
        var runtime = new WorkbenchRuntime(OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx"));
        var data = runtime.BuildAnalysisData(key, AnalysisComparisonRegistry.MapWorkbench(entry, request));
        data = JsonSerializer.Deserialize<AnalysisData>(JsonSerializer.Serialize(data, JsonFiles.Options), JsonFiles.Options)!;
        var actual = ExtendedResultNormalizer.Workbench(data, request);
        var expected = ExtendedResultNormalizer.Zemax(Path.Combine(directory, "data.json"), request);
        foreach (var curve in expected.Series)
        {
            var metric = ComparisonMetrics.Calculate(curve.Id, curve.YAxis.Unit,
                ComparisonMetrics.Align(actual.Series.Single(s => s.Id == curve.Id), curve, []), entry.DefaultTolerances[curve.YAxis.Quantity]);
            rows.Add(new { key, request, capturedSettingsSha256 = Hash(Path.Combine(directory, "captured-settings.json")), nativeDataSha256 = Hash(Path.Combine(directory, "data.json")), metric });
            Console.WriteLine($"formal {key}/{curve.Id}: NRMSE={metric.Nrmse:R}; max={metric.MaxAbsolute:R}; {metric.Conclusion}");
        }
    }
    File.WriteAllText(Path.Combine(output, "formal.json"), JsonSerializer.Serialize(rows, JsonFiles.Options));
}

void Inputs()
{
    foreach (var nodes in new[] { "endpoints", "centers", "zemax", "halfopen", "closed32intervals" })
    {
        var wf = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32,
            cellCentered: nodes == "centers", zemaxCentered: nodes == "zemax", aimAtStop: true);
        var pupil = nodes is "halfopen" or "closed32intervals"
            ? Enumerable.Range(0, nodes == "halfopen" ? 32 : 33).SelectMany(row => Enumerable.Range(0, nodes == "halfopen" ? 32 : 33)
                .Select(column => new PupilSample((column - 16) / 16d, (row - 16) / 16d, 1))).Where(p => p.X * p.X + p.Y * p.Y <= 1).ToArray()
            : wf.Samples.Select(s => new PupilSample(s.NormalizedPupilX, s.NormalizedPupilY, 1)).ToArray();
        var inputs = pupil.Select((p, i) => new { hx = 0d, hy = 0d, px = p.X, py = p.Y, fieldIndex = 0, pupilIndex = i }).ToArray();
        File.WriteAllText(Path.Combine(output, nodes + ".json"), JsonSerializer.Serialize(inputs, JsonFiles.Options));
        Console.WriteLine($"{nodes}: {inputs.Length} exact normalized pupil inputs");
    }
}

void CubicGrid()
{
    var records = new List<object>();
    foreach (var ideal in new[] { true, false })
    {
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, 64,
            ignoreOpd: ideal, cellCenteredPupil: true, zemaxFftSampling: true, aimAtStop: true, referenceWavelength: wave);
        foreach (var kernel in new[] { "linear", "catmull-rom", "keys", "lagrange" })
        foreach (var factor in new[] { 4, 8 })
        {
            double Interpolate(double xx, double yy)
            {
                var c = xx / psf.SampleSpacingMicrometers + 32;
                var r = yy / psf.SampleSpacingMicrometers + 32;
                var column = (int)Math.Floor(c); var row = (int)Math.Floor(r);
                var total = 0d;
                for (var j = -1; j <= 2; j++)
                for (var i = -1; i <= 2; i++) total += psf.Values[row + j, column + i] * Weight(c - column, i) * Weight(r - row, j);
                return Math.Max(0, total);
            }
            double Weight(double t, int node)
            {
                if (kernel == "lagrange")
                {
                    var value = 1d;
                    for (var other = -1; other <= 2; other++) if (other != node) value *= (t - other) / (node - other);
                    return value;
                }
                var x = Math.Abs(t - node);
                if (kernel == "linear") return Math.Max(0, 1 - x);
                var a = kernel == "keys" ? -1d : -.5;
                return x < 1 ? (a + 2) * x * x * x - (a + 3) * x * x + 1
                    : x < 2 ? a * x * x * x - 5 * a * x * x + 8 * a * x - 4 * a : 0;
            }
            var size = 32 * factor;
            var pitch = psf.SampleSpacingMicrometers / factor;
            var points = Enumerable.Range(0, size).SelectMany(row => Enumerable.Range(0, size).Select(column =>
            {
                var xx = (column - size / 2d + .5) * pitch;
                var yy = (row - size / 2d + .5) * pitch;
                return new WeightedEnergyPoint(xx, yy, Interpolate(xx, yy));
            })).ToArray();
            var distribution = new DiffractionEnergyDistribution(points, (0, 0), EnergyRegion.Circle);
            var integrator = typeof(DiffractionEnergyDistribution).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(distribution)!;
            var fraction = integrator.GetType().GetMethod("Fraction")!;
            foreach (var range in new[] { 2.5, 5, 10 })
            {
                var name = range == 5 ? "dee-32" : range == 2.5 ? "dee-range-2p5" : "dee-range-10";
                using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
                    $"validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/{name}/data.json")));
                var series = native.RootElement.GetProperty("dataSeries")[ideal ? 0 : 1];
                var x = series.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                var y = series.GetProperty("y").EnumerateArray().Select(v => v[0].GetDouble()).ToArray();
                var axis = Enumerable.Range(0, 32).Select(i => range * i / 31).ToArray();
                var knots = axis.Select(r => (double)fraction.Invoke(integrator, [r])!).ToArray();
                knots[1] = knots[2] / 4;
                var spline = typeof(MtfThroughFocusAnalysis).GetMethod("CubicSplineInterpolate", BindingFlags.Static | BindingFlags.NonPublic)!;
                var values = (double[])spline.Invoke(null, [axis, knots, x])!;
                var previous = 0d;
                values = values.Select(v => previous = Math.Clamp(Math.Max(previous, v), 0, 1)).ToArray();
                var metric = Metric(values, y);
                records.Add(new { ideal, kernel, factor, range, knots, metric, values });
                Console.WriteLine($"cubic ideal={ideal} kernel={kernel} factor={factor} range={range}: {metric.relativeL2:R}; first={knots[1]:R}, second={knots[2]:R}");
            }
        }
    }
    File.WriteAllText(Path.Combine(output, "cubic-grid.json"), JsonSerializer.Serialize(records, jsonOptions));
}

void Dense()
{
    var records = new List<object>();
    foreach (var ideal in new[] { true, false })
    foreach (var imageSize in args[2] == "energy-grids" ? new[] { 64, 256 } : args[2] == "full-dense" ? new[] { 64, 256, 512 } : new[] { 32, 64, 128, 256, 512 })
    foreach (var smoothed in args[2] == "full-dense" && !ideal ? new[] { true, false } : new[] { true })
    foreach (var zemax in args[2] == "energy-grids" ? new[] { false, true } : new[] { true })
    foreach (var cellCentered in args[2] == "energy-grids" && !zemax ? new[] { false, true } : new[] { true })
    {
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, imageSize,
            ignoreOpd: ideal, cellCenteredPupil: cellCentered, zemaxFftSampling: zemax, aimAtStop: true, referenceWavelength: wave);
        if (!smoothed)
        {
            var wf = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32, true, true, zemaxCentered: true, referenceWavelength: wave);
            var builder = typeof(DiffractionEngine).GetMethod("BuildComplexPupilCore", BindingFlags.NonPublic | BindingFlags.Static)!;
            var pupil = (Complex[,])builder.Invoke(null, [wf, null, 32, wave, psf.TangentialWorkingFNumber, psf.SagittalWorkingFNumber, true, 0d, 1d, ideal, true])!;
            var padded = new Complex[imageSize, imageSize];
            for (var row = 0; row < 32; row++)
            for (var column = 0; column < 32; column++) padded[row + (imageSize - 32) / 2, column + (imageSize - 32) / 2] = pupil[row, column];
            typeof(DiffractionEngine).GetMethod("FourierTransform2D", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [padded, false]);
            var count = pupil.Cast<Complex>().Count(c => c.Magnitude > 0);
            for (var row = 0; row < imageSize; row++)
            for (var column = 0; column < imageSize; column++)
                psf.Values[row, column] = Math.Pow(padded[(row + imageSize / 2) % imageSize, (column + imageSize / 2) % imageSize].Magnitude / count, 2) * 100;
        }
        var spacing = psf.SampleSpacingMicrometers;
        // Keep exactly the original physical half-width and pupil rays. Only the
        // independent integration quadrature changes; this is not a new capture.
        var size = args[2] == "full-dense" || imageSize == 32 ? imageSize : imageSize / 2;
        var start = (imageSize - size) / 2;
        var points = Enumerable.Range(0, size).SelectMany(row => Enumerable.Range(0, size).Select(column =>
            new WeightedEnergyPoint((column - size / 2d) * spacing, (row - size / 2d) * spacing, psf.Values[row + start, column + start]))).ToArray();
        var center = new GeometricEnergyDistribution(points, GeometricEnergyReference.Centroid, EnergyRegion.Circle).Center;
        var distribution = new DiffractionEnergyDistribution(points, center, EnergyRegion.Circle);
        var integrator = typeof(DiffractionEnergyDistribution).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(distribution)!;
        var fraction = integrator.GetType().GetMethod("Fraction")!;
        foreach (var range in new[] { 2.5, 5, 10 })
        {
            var name = range == 5 ? "dee-32" : range == 2.5 ? "dee-range-2p5" : "dee-range-10";
            using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
                $"validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/{name}/data.json")));
            var series = native.RootElement.GetProperty("dataSeries")[ideal ? 0 : 1];
            var x = series.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var y = series.GetProperty("y").EnumerateArray().Select(v => v[0].GetDouble()).ToArray();
            var axis = Enumerable.Range(0, 32).Select(i => range * i / 31).ToArray();
            var knots = axis.Select(r => (double)fraction.Invoke(integrator, [r])!).ToArray();
            foreach (var originCorrection in new[] { false, true })
            {
                var corrected = knots.ToArray();
                if (originCorrection) corrected[1] = corrected[2] / 4;
                var spline = typeof(MtfThroughFocusAnalysis).GetMethod("CubicSplineInterpolate", BindingFlags.Static | BindingFlags.NonPublic)!;
                var values = (double[])spline.Invoke(null, [axis, corrected, x])!;
                var metric = Metric(values, y);
                records.Add(new { ideal, imageSize, smoothed, zemax, cellCentered, range, spacing, center, originCorrection, knots = corrected, metric, values });
                Console.WriteLine($"dense ideal={ideal} image={imageSize} smoothed={smoothed} zemax={zemax} centered={cellCentered} range={range} origin={originCorrection}: {metric.relativeL2:R}; first={corrected[1]:R}, second={corrected[2]:R}");
            }
        }
    }
    File.WriteAllText(Path.Combine(output, "dense.json"), JsonSerializer.Serialize(records, jsonOptions));
}

void Knots()
{
    var records = new List<object>();
    foreach (var ideal in new[] { true, false })
    {
        var heatmap = new PsfAnalysis(optic, 32, 64, 1, 1, type: "linear", displayAs: "heatmap", zemaxCompatible: true, ignoreOpd: ideal)
            .GenerateData().PlotSeries.First(s => s.Kind == AnalysisSeriesKind.Heatmap);
        var points = heatmap.Points.Select(p => new WeightedEnergyPoint(p.X, p.Y, p.Value!.Value)).ToArray();
        var xs = points.Select(p => p.X).Distinct().Order().Skip(16).Take(32).ToHashSet();
        var ys = points.Select(p => p.Y).Distinct().Order().Skip(16).Take(32).ToHashSet();
        var window = points.Where(p => xs.Contains(p.X) && ys.Contains(p.Y)).ToArray();
        var center = new GeometricEnergyDistribution(window, GeometricEnergyReference.Centroid, EnergyRegion.Circle).Center;
        var distribution = new DiffractionEnergyDistribution(window, center, EnergyRegion.Circle);
        var integrator = typeof(DiffractionEnergyDistribution).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(distribution)!;
        var fraction = integrator.GetType().GetMethod("Fraction")!;
        foreach (var range in new[] { 2.5, 5, 10 })
        {
            var name = range == 5 ? "dee-32" : range == 2.5 ? "dee-range-2p5" : "dee-range-10";
            using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
                $"validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/{name}/data.json")));
            var series = native.RootElement.GetProperty("dataSeries")[ideal ? 0 : 1];
            var x = series.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var y = series.GetProperty("y").EnumerateArray().Select(v => v[0].GetDouble()).ToArray();
            // Each polynomial is reconstructed solely to identify the native display
            // interpolation. These values never feed the formal product calculation.
            double NativeAt(double radius)
            {
                var segment = Math.Min(30, (int)Math.Floor(radius / range * 31));
                var start = (int)Math.Ceiling(segment * 400 / 31d) + 2;
                start = Math.Min(397, start);
                var total = 0d;
                for (var a = start; a < start + 4; a++)
                {
                    var factor = y[a];
                    for (var b = start; b < start + 4; b++) if (a != b) factor *= (radius - x[b]) / (x[a] - x[b]);
                    total += factor;
                }
                return total;
            }
            var axis = Enumerable.Range(0, 32).Select(i => range * i / 31).ToArray();
            var nativeKnots = axis.Select(NativeAt).ToArray();
            var formalKnots = axis.Select(r => (double)fraction.Invoke(integrator, [r])!).ToArray();
            var spline = typeof(MtfThroughFocusAnalysis).GetMethod("CubicSplineInterpolate", BindingFlags.Static | BindingFlags.NonPublic)!;
            var restored = (double[])spline.Invoke(null, [axis, nativeKnots, x])!;
            records.Add(new { ideal, range, center, total = window.Sum(p => p.Weight), peak = window.Max(p => p.Weight), axis, nativeKnots, formalKnots, restoredMetric = Metric(restored, y), restored, nativeValues = y });
            Console.WriteLine($"knots ideal={ideal} range={range} centroid={center} total={window.Sum(p => p.Weight):R} peak={window.Max(p => p.Weight):R} spline={Metric(restored, y).relativeL2:R}");
            for (var i = 0; i < 8; i++) Console.WriteLine($"r={axis[i]:R}, native={nativeKnots[i]:R}, formal={formalKnots[i]:R}");
        }
    }
    File.WriteAllText(Path.Combine(output, "knots.json"), JsonSerializer.Serialize(records, jsonOptions));
}

void Energy()
{
    var rows = new List<object>();
    foreach (var ideal in new[] { true, false })
    {
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, 64,
            ignoreOpd: ideal, cellCenteredPupil: true, zemaxFftSampling: true, aimAtStop: true, referenceWavelength: wave);
        var bilinear = typeof(PsfAnalysis).GetMethod("BilinearSample", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var range in new[] { 2.5, 5, 10 })
        {
            var name = range == 5 ? "dee-32" : range == 2.5 ? "dee-range-2p5" : "dee-range-10";
            using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
                $"validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/{name}/data.json")));
            var series = native.RootElement.GetProperty("dataSeries")[ideal ? 0 : 1];
            var x = series.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var y = series.GetProperty("y").EnumerateArray().Select(v => v[0].GetDouble()).ToArray();
            foreach (var count in new[] { 16, 32, 64, 128 })
            foreach (var extent in new[] { 1d, 2d })
            foreach (var centered in new[] { false, true })
            {
                var pitch = range * extent / count;
                var points = Enumerable.Range(0, count).SelectMany(row => Enumerable.Range(0, count).Select(column =>
                {
                    var xx = (column - count / 2d + (centered ? .5 : 0)) * pitch;
                    var yy = (row - count / 2d + (centered ? .5 : 0)) * pitch;
                    return new WeightedEnergyPoint(xx, yy, (double)bilinear.Invoke(null, [psf, xx, yy])!);
                })).ToArray();
                var center = new GeometricEnergyDistribution(points, GeometricEnergyReference.Centroid, EnergyRegion.Circle).Center;
                var distribution = new DiffractionEnergyDistribution(points, center, EnergyRegion.Circle);
                var integrator = typeof(DiffractionEnergyDistribution).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(distribution)!;
                var fraction = integrator.GetType().GetMethod("Fraction")!;
                var values = x.Select(radius => (double)fraction.Invoke(integrator, [radius])!).ToArray();
                var metric = Metric(values, y);
                rows.Add(new { ideal, range, count, extent, centered, pitch, center, metric, values });
                if (metric.relativeL2 < .025) Console.WriteLine($"energy ideal={ideal} range={range} n={count} extent={extent} centered={centered}: {metric.relativeL2:R}");
            }
            File.WriteAllText(Path.Combine(output, $"native-{ideal}-{range}.json"), JsonSerializer.Serialize(new { x, y }));
        }
    }
    File.WriteAllText(Path.Combine(output, "energy-controls.json"), JsonSerializer.Serialize(rows, jsonOptions));
}

void Huygens()
{
    using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
        "validation/zemax/2026-r1/huygens-method-controls-2026-10-09/grid/Auto/data.json")));
    var grid = native.RootElement.GetProperty("dataGrids")[0];
    var target = grid.GetProperty("values").EnumerateArray().SelectMany(row => row.EnumerateArray().Select(v => v.GetDouble())).ToArray();
    var dx = grid.GetProperty("dx").GetDouble(); var dy = grid.GetProperty("dy").GetDouble();
    var minX = grid.GetProperty("minX").GetDouble(); var minY = grid.GetProperty("minY").GetDouble();
    var chief = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, wave.Micrometers, true)).Single()!;
    var frame = DiffractionEngine.CreateHuygensImageFrame(optic, (0, 0), wave, true);
    var k = 2 * Math.PI / (wave.Micrometers * .001);
    var records = new List<object>();
    var pixelGridType = typeof(DiffractionEnergyDistribution).Assembly.GetType("OptilandWorkbench.Core.Analysis.PsfPixelEnergyGrid")!;
    var pixelType = pixelGridType.GetNestedType("Pixel", BindingFlags.NonPublic)!;
    var areaFraction = pixelGridType.GetMethod("AreaFraction", BindingFlags.Static | BindingFlags.NonPublic)!;
    foreach (var aiming in new[] { true, false })
    foreach (var nodes in new[] { "endpoints", "centers", "zemax", "halfopen", "closed32intervals" })
    {
        var wf = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32,
            cellCentered: nodes == "centers", zemaxCentered: nodes == "zemax", aimAtStop: aiming);
        var pupil = nodes is "halfopen" or "closed32intervals"
            ? Enumerable.Range(0, nodes == "halfopen" ? 32 : 33).SelectMany(row => Enumerable.Range(0, nodes == "halfopen" ? 32 : 33)
                .Select(column => new PupilSample((column - 16) / 16d, (row - 16) / 16d, 1))).Where(p => p.X * p.X + p.Y * p.Y <= 1).ToArray()
            : wf.Samples.Select(s => new PupilSample(s.NormalizedPupilX, s.NormalizedPupilY, 1)).ToArray();
        var rays = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0, wave.Micrometers, pupil, aiming));
        var pitch = nodes == "zemax" ? 1 / 15d : nodes == "endpoints" ? 2 / 31d : 1 / 16d;
        var areas = pupil.Select(p => (double)areaFraction.Invoke(null, [Activator.CreateInstance(pixelType,
            [p.X - pitch / 2, p.X + pitch / 2, p.Y - pitch / 2, p.Y + pitch / 2, 1d])!, 1d, "encircled"])!).ToArray();
        foreach (var weight in new[] { "uniform", "sqrtcos", "cos", "invsqrtcos", "invcos", "obliquity", "area", "area-sqrtcos", "area-cos" })
        {
            double Amplitude(int i)
            {
                var ray = rays[i];
                if (ray is null || ray.Intensity <= 0) return 0;
                var cos = ray.Direction.Z / chief.Direction.Z;
                var factor = weight switch { "sqrtcos" => Math.Sqrt(cos), "cos" => cos, "invsqrtcos" => 1 / Math.Sqrt(cos), "invcos" => 1 / cos, "obliquity" => (1 + cos) / 2, _ => 1 };
                if (weight.StartsWith("area")) factor = areas[i] * (weight == "area-cos" ? cos : weight == "area-sqrtcos" ? Math.Sqrt(cos) : 1);
                return Math.Sqrt(ray.Intensity) * factor;
            }
            var peak = Math.Pow(Enumerable.Range(0, pupil.Length).Sum(Amplitude), 2);
            var values = new double[1024];
            for (var row = 0; row < 32; row++)
            for (var column = 0; column < 32; column++)
            {
                var point = frame.Center + frame.TangentX * ((minX + column * dx) / 1000) + frame.TangentY * ((minY + row * dy) / 1000);
                var sum = Complex.Zero;
                for (var i = 0; i < rays.Count; i++)
                {
                    if (rays[i] is not { } ray) continue;
                    var offset = point - ray.Position;
                    var propagation = ray.Direction.X * offset.X + ray.Direction.Y * offset.Y + ray.Direction.Z * offset.Z;
                    var path = ray.CumulativeOpticalPathLength - chief.CumulativeOpticalPathLength + wf.ImageRefractiveIndex * propagation;
                    sum += Complex.FromPolarCoordinates(Amplitude(i), k * path);
                }
                values[row * 32 + column] = sum.Magnitude * sum.Magnitude / peak;
            }
            var metric = Metric(values, target);
            records.Add(new { aiming, nodes, weight, metric, values });
            Console.WriteLine($"huygens aiming={aiming} nodes={nodes} weight={weight}: L2={metric.relativeL2:R}, max={metric.maximum:R}");
        }
    }
    var formal = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wave, 32, 32, .00025, aimAtStop: true);
    Console.WriteLine($"formal Huygens L2={Metric(formal.Values.Cast<double>().Select(v => v / 100).ToArray(), target).relativeL2:R}");
    File.WriteAllText(Path.Combine(output, "huygens-controls.json"), JsonSerializer.Serialize(records, jsonOptions));
}

static (double relativeL2, double maximum) Metric(double[] actual, double[] expected) =>
    (Math.Sqrt(actual.Zip(expected, (a, b) => Math.Pow(a - b, 2)).Sum() / expected.Sum(v => v * v)),
        actual.Zip(expected, (a, b) => Math.Abs(a - b)).Max());
