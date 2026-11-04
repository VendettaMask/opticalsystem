using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

var repo=Path.GetFullPath(args[0]);var root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);
var input=Path.Combine(repo,"validation/rechecks/2026-11-04/all-example-accuracy");
var entries=JsonFiles.Read<PayloadRef[]>(Path.Combine(input,"archive-index.json")).ToDictionary(e=>e.LogicalPath);
var opened=new Dictionary<string,ZipArchive>();
byte[] Read(string logical)
{
 var e=entries[logical];var file=Path.GetFullPath(Path.Combine(input,e.Archive));byte[] bytes;
 if(e.Entry is null)bytes=File.ReadAllBytes(file);
 else{if(!opened.TryGetValue(file,out var zip))opened[file]=zip=ZipFile.OpenRead(file);using var stream=zip.GetEntry(e.Entry)!.Open();using var memory=new MemoryStream();stream.CopyTo(memory);bytes=memory.ToArray();}
 if(bytes.Length!=e.Length||JsonFiles.Hash(bytes)!=e.Sha256)throw new InvalidDataException(logical);return bytes;
}
try
{
 if(args.Length>2&&args[2]=="phase")
 {
  var diagnostics=new List<object>();var models=JsonSerializer.Deserialize<JsonElement[]>(Read("study/models.json"),JsonFiles.Options)!;
  foreach(var number in new[]{17,20})
  {
   var model=models.Single(m=>m.GetProperty("index").GetInt32()==number);var lens=model.GetProperty("lens").GetString()!;
   var original=JsonSerializer.Deserialize<OpticSnapshot>(Read($"study/models/{number:D3}-c1.optic.json"),JsonFiles.Options)!;
   var restored=Optic.FromSnapshot(original);var freshOptic=(await WorkbenchExecutor.Import(Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples",lens))).Configurations[0];
   foreach(var (label,optic) in new[]{("before",restored),("after",freshOptic)})
   foreach(var (name,pupil,pitch) in new[]{("PSF",64,.25),("MTF",64,-1d),("FFT PSF Cross Section",32,0d)})
   {
    var wave=optic.Wavelengths.First(w=>w.IsPrimary);
    var psf=DiffractionEngine.ComputeFftPsf(optic,(0,0),wave,pupil,2*pupil,cellCenteredPupil:true,zemaxFftSampling:true,
     imageDelta:pitch,ignoreScalarTransmission:name!="MTF",interpolateScalarPupilAmplitude:name!="MTF");
    diagnostics.Add(new{number,lens,label,name,pupil,imageDelta=pitch,psf.PupilPhaseSampling});
   }
  }
  JsonFiles.Write(Path.Combine(root,"phase-diagnostics.json"),diagnostics);
  JsonFiles.Write(Path.Combine(root,"identity.json"),new{coreSha256=JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location)),scope="Original FFT pupil grids, independent diagnostic only"});return;
 }
 if(args.Length>2&&args[2]=="imports")
 {
  var models=JsonSerializer.Deserialize<JsonElement[]>(Read("study/models.json"),JsonFiles.Options)!;var audit=new List<object>();
  foreach(var model in models)
  {
   var lens=model.GetProperty("lens").GetString()!;var source=Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples",lens);
   var sourceHash=JsonFiles.Hash(File.ReadAllBytes(source));if(sourceHash!=model.GetProperty("sourceSha256").GetString())throw new InvalidDataException("Source changed");
   var configurations=new List<object>();string? error=null;
   try
   {
    var optics=await WorkbenchExecutor.Import(source);var oldPaths=model.GetProperty("snapshots").EnumerateArray().Select(p=>p.GetString()!).ToArray();
    if(optics.Configurations.Count!=oldPaths.Length)throw new InvalidDataException("Configuration count changed");
    for(var i=0;i<oldPaths.Length;i++)
    {
     var original=Read("study/models/"+Path.GetFileName(oldPaths[i]));var current=JsonSerializer.SerializeToUtf8Bytes(optics.Configurations[i].ToSnapshot(),JsonFiles.Options);
     var same=JsonElement.DeepEquals(JsonDocument.Parse(original).RootElement,JsonDocument.Parse(current).RootElement);
     if(!same){File.WriteAllBytes(Path.Combine(root,Path.GetFileName(oldPaths[i])),current);File.WriteAllBytes(Path.Combine(root,"old-"+Path.GetFileName(oldPaths[i])),original);}
     configurations.Add(new{configuration=i+1,structurallyIdentical=same,originalSha256=JsonFiles.Hash(original),currentSha256=JsonFiles.Hash(current)});
    }
   }
   catch(Exception e){error=e.Message;}
   audit.Add(new{lens,sourceHash,configurations,error});JsonFiles.Write(Path.Combine(root,"import-audit.json"),audit);
  }
  JsonFiles.Write(Path.Combine(root,"identity.json"),new{coreSha256=JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location)),scope="95 source imports compared to prior 114 model snapshots",sourcePrescriptionsUnchanged=true});return;
 }
 var fresh=args.Length>2&&args[2]=="fresh";
 var indices=args.Length>(fresh?3:2)?args.Skip(fresh?3:2).Select(int.Parse).ToArray():new[]{579,580,583,584};
 var rows=JsonSerializer.Deserialize<JsonElement[]>(Read("study/rows.json"),JsonFiles.Options)!;
 var imported=new Dictionary<int,string>();var results=new List<object>();
 foreach(var index in indices)
 {
  var job=JsonSerializer.Deserialize<WorkbenchJob>(Read($"study/analyses/{index:D4}/request.json"),JsonFiles.Options)!;
  var row=rows.Single(r=>r.GetProperty("index").GetInt32()==index);var lens=row.GetProperty("lens").GetString()!;
  var path=Path.Combine(root,Path.GetFileName(job.SnapshotPath));
  if(fresh)
  {
   var lensIndex=row.GetProperty("lensIndex").GetInt32();
   if(!imported.TryGetValue(lensIndex,out path!))
   {
    var source=Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples",lens);
    var previousSource=Path.Combine(repo,"artifacts/zemax-sequential-corpus/20261021/full-v1/sources",lens);
    if(JsonFiles.Hash(File.ReadAllBytes(source))!=JsonFiles.Hash(File.ReadAllBytes(previousSource)))throw new InvalidDataException("Prescription changed");
    var optics=await WorkbenchExecutor.Import(source);path=Path.Combine(root,Path.GetFileName(job.SnapshotPath));
    JsonFiles.Write(path,optics.Configurations[job.Request.Configuration-1].ToSnapshot());imported[lensIndex]=path;
   }
  }
  else File.WriteAllBytes(path,Read("study/models/"+Path.GetFileName(job.SnapshotPath)));
  var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(path));
  JsonFiles.Write(Path.Combine(root,index+"-optic.json"),new{optic.FieldDefinition,epd=optic.Paraxial.EstimateEntrancePupilDiameter(),epz=optic.Paraxial.EstimateEntrancePupilLocation(),efl=optic.Paraxial.EstimateEffectiveFocalLength(),fno=optic.Paraxial.EstimateFNumber(),surfaces=optic.SurfaceGroup.Items.Select(s=>new{s.Number,s.Radius,s.Thickness,origin=s.CoordinateSystem.Origin,interaction=s.InteractionModel.Kind})});
  var executedJob=job with{SnapshotPath=path,OutputDirectory=Path.Combine(root,index.ToString())};
  WorkbenchExecutor.Execute(executedJob);
  JsonFiles.Write(Path.Combine(root,index.ToString(),"request.json"),executedJob);
  JsonFiles.Write(Path.Combine(root,index.ToString(),"original-request.json"),job);
  File.WriteAllBytes(Path.Combine(root,index+"-previous-data.json"),Read($"study/analyses/{index:D4}/data.json"));
  var nativeDirectory=Directory.GetDirectories(Path.Combine(repo,"artifacts/zemax-sequential-corpus/20261021/full-v1/analyses"),$"{index:D4}-*").Single();
  var native=Path.Combine(nativeDirectory,"native","data.json");var settings=row.GetProperty("settings").Deserialize<AnalysisConfiguration>(JsonFiles.Options)!;
  var run=new AnalysisRun{Key=job.Request.CanonicalAnalysisKey,Directory=Path.Combine(root,index.ToString()),Request=job.Request};string? error=null;
  try
  {
   var entry=AnalysisComparisonRegistry.Get(run.Key);var z=ResultNormalizer.Zemax(native,entry,job.Request);
   var data=JsonFiles.Read<JsonElement>(Path.Combine(run.Directory,"data.json"));
   if(data.GetProperty("outcome").GetString() is "Unavailable" or "NotApplicable")
   {run.Conclusion=Conclusion.Error;error=data.GetProperty("outcomeReason").GetString();}
   else{var w=ResultNormalizer.Workbench(Path.Combine(run.Directory,"data.json"),entry,job.Request);ComparisonRunner.Compare(run,entry,settings,w,z,run.Directory);}
  }
  catch(InvalidDataException e){run.Conclusion=Conclusion.Incomparable;error=e.Message;}
  catch(Exception e){run.Conclusion=Conclusion.Error;error=e.ToString();}
  JsonFiles.Write(Path.Combine(run.Directory,"comparison.json"),run);
  results.Add(new{index,lens,key=run.Key,run.Conclusion,error,run.Metrics,requestFingerprint=job.Request.Fingerprint,snapshotSha256=JsonFiles.Hash(File.ReadAllBytes(path))});
  JsonFiles.Write(Path.Combine(root,"results.json"),results);
  Console.WriteLine(index+" "+job.Request.CanonicalAnalysisKey+" "+run.Conclusion);
 }
 JsonFiles.Write(Path.Combine(root,"identity.json"),new{coreSha256=JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location)),freshImport=fresh,indices,originalPrescriptions=true,originalRequests=true,originalReferences=true,originalTolerances=true,nativeCaptured=false});
}
finally{foreach(var zip in opened.Values)zip.Dispose();}
internal sealed record PayloadRef(string LogicalPath,string Archive,string? Entry,string Sha256,long Length);
