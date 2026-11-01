using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Scattering;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
using var app=WorkbenchApplication.Create();app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);foreach(var o in app.NonSequential.GetDocument().Objects.ToArray())app.NonSequential.DeleteObject(o.Id);
app.NonSequential.UpdateWavelengths([new(1,"synthetic green",550,1,true)]);
Guid Add(NonSequentialObjectKind kind,double z,NonSequentialObjectParameters parameters,double tilt=0,NonSequentialSurfaceBsdf? bsdf=null)
{var id=app.NonSequential.AddObject(kind);app.NonSequential.UpdateObject(new(id,true,true,kind,kind.ToString(),null,null,0,0,z,0,tilt,0,parameters,bsdf));return id;}
var points=(from side in new[]{BsdfSide.Front,BsdfSide.Back} from theta in new[]{30d,60} from phi in new[]{0d,90,180,270,360} select new MeasuredBsdfPoint(side,550,20,theta,phi,.2,0)).ToArray();
var data=new MeasuredBsdfData("Synthetic partial hemisphere","analytic.csv",new string('a',64),points,angularSupport:BsdfAngularSupport.MeasuredPolarRangeZeroOutside);
if(data.ComputationProtocol!="MeasuredBsdfGrid/9")throw new InvalidDataException("Wrong frozen binary");
var dto=new MeasuredBsdfDataDto(data.Name,data.SourceFileName,data.SourceSha256,data.DataSha256,data.Points.Select(p=>new MeasuredBsdfPointDto((int)p.Side,p.WavelengthNanometers,p.IncidentThetaDegrees,p.OutgoingThetaDegrees,p.OutgoingPhiDegrees,p.BrdfPerSteradian,p.BtdfPerSteradian)).ToArray(),AngularSupport:1);
Add(NonSequentialObjectKind.SourcePoint,.032,new SourcePointParameters(1e-11,1,0,20,4096),160);
Add(NonSequentialObjectKind.PlaneRectangle,0,new PlaneRectangleParameters(1,1,NonSequentialSurfaceBehavior.Reflective,"AIR","AIR"),bsdf:new(NonSequentialBsdfKind.Measured,0,dto));
var detector=Add(NonSequentialObjectKind.DetectorRectangle,.064,new DetectorRectangleParameters(3.2,3.2,32,32,false,true));
await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode:NonSequentialSplittingMode.FullFresnel));var input=await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector,"S2 & D3");
var request=new CameraImagingRequest(Source:CameraSource.NonSequentialDetector,Width:32,Height:32,Optics:CameraOptics.DetectorInput,Sensor:new(NoiseEnabled:false,DarkElectronsPerSecond:0,PrnuPercent:0,DsnuElectrons:0,GainDnPerElectron:.1,BlackLevel:0,FullWellElectrons:1e7,BitDepth:16,PixelPitchMicrometers:100),SpectralResponse:new("Analytic QE","Synthetic",[new(400,.2,.5,.8),new(700,.2,.5,.8)]),SpectralModel:CameraSpectralModel.DetectorBands,NscDetector:input);
var run=await app.CameraImaging.SimulateAsync(request);var zip=Path.Combine(output,"camera-polar-grid9-frozen.zip");await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier,1,run,run),zip);
string Sha(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
File.WriteAllText(Path.Combine(output,"capture.json"),JsonSerializer.Serialize(new{SyntheticOnly=true,CoreSha256=Sha(typeof(MeasuredBsdf).Assembly.Location),ApplicationSha256=Sha(typeof(WorkbenchApplication).Assembly.Location),DriverSha256=Sha(System.Reflection.Assembly.GetExecutingAssembly().Location),ZipSha256=Sha(zip),input.SurfaceBsdfProtocol,run.MeanPhotoElectrons},new JsonSerializerOptions{WriteIndented=true}));
