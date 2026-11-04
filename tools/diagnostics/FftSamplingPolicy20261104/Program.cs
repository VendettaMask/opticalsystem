using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;

var repo=Path.GetFullPath(args[0]);var root=Path.GetFullPath(args[1]);
if(args.Length>2&&args[2]=="summarize")
{
 ConvergenceProbe.Summarize(repo,root,args.Skip(3).ToArray());return;
}
Directory.CreateDirectory(root);
using var inputs=new FrozenInputs(Path.Combine(repo,"validation/rechecks/2026-11-04/all-example-accuracy"));
var afocal=args.Length>2&&args[2]=="afocal";
var indices=afocal?new[]{9}:args.Length>2?args.Skip(2).Select(int.Parse).ToArray()
 :new[]{313,559,585,587,589,675,677,679,789,791,831,835,969,991,1115,1119,1365,1367,1369,1372,1767,1989,2229,2567,2569,2679,2813,2835,2853,2877};
foreach(var index in indices)
{
 WorkbenchJob job;
 if(afocal)
 {
  var request=JsonSerializer.Deserialize<CanonicalAnalysisRequest>(inputs.Read("afocal-study/inputs/native/0009-001-afocal-riflescope-c1-primary-f0-w0-mtf/request.json"),JsonFiles.Options)!;
  var snapshot=inputs.Restore("afocal-study/inputs/models/001-afocal-riflescope-c1.json",Path.Combine(root,"inputs","afocal-riflescope.json"));
  job=new(snapshot,Path.Combine(root,"original",index.ToString()),request);
 }
 else
 {
  var original=JsonSerializer.Deserialize<WorkbenchJob>(inputs.Read($"study/analyses/{index:D4}/request.json"),JsonFiles.Options)!;
  var snapshot=inputs.Restore("study/models/"+Path.GetFileName(original.SnapshotPath),Path.Combine(root,"inputs",Path.GetFileName(original.SnapshotPath)));
  job=original with{SnapshotPath=snapshot,OutputDirectory=Path.Combine(root,"original",index.ToString())};
 }
 JsonFiles.Write(Path.Combine(root,index.ToString(),"job.json"),job);
 WorkbenchExecutor.Execute(job);
 ConvergenceProbe.Run(repo,root,index,job);
}
JsonFiles.Write(Path.Combine(root,"identity.json"),new{scope="Independent Core self-refinement, not a native convergence capture",afocal,indices,
 originalPolicySha256=JsonFiles.Hash(File.ReadAllBytes(Path.Combine(repo,"tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"))),
 coreSha256=JsonFiles.Hash(File.ReadAllBytes(typeof(OpticalSystemDesign.Core.Optic).Assembly.Location)),
 toolSha256=JsonFiles.Hash(File.ReadAllBytes(typeof(FrozenInputs).Assembly.Location)),
 originalSamplingChanged=false,referenceDataChanged=false,tolerancesChanged=false,nativeCaptured=false});

internal sealed class FrozenInputs(string root):IDisposable
{
 private readonly Dictionary<string,PayloadRef> entries=JsonFiles.Read<PayloadRef[]>(Path.Combine(root,"archive-index.json")).ToDictionary(e=>e.LogicalPath);
 private readonly Dictionary<string,ZipArchive> opened=new();
 internal byte[] Read(string logical)
 {
  var item=entries[logical];var path=Path.GetFullPath(Path.Combine(root,item.Archive));byte[] bytes;
  if(item.Entry is null)bytes=File.ReadAllBytes(path);
  else{if(!opened.TryGetValue(path,out var zip))opened[path]=zip=ZipFile.OpenRead(path);using var input=zip.GetEntry(item.Entry)!.Open();using var memory=new MemoryStream();input.CopyTo(memory);bytes=memory.ToArray();}
  if(bytes.Length!=item.Length||JsonFiles.Hash(bytes)!=item.Sha256)throw new InvalidDataException(logical);
  return bytes;
 }
 internal string Restore(string logical,string path){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,Read(logical));return path;}
 public void Dispose(){foreach(var zip in opened.Values)zip.Dispose();}
}
internal sealed record PayloadRef(string LogicalPath,string Archive,string? Entry,string Sha256,long Length);
