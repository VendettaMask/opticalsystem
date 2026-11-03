using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

internal static class ModelProbes
{
    internal static async Task Run(string repo,string root,string core)
    {
        string Hash(string p)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
        using var lease=new FileStream(Path.Combine(repo,"artifacts/validation/.background-small-native-controls.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var api=ZemaxExecutor.Locate(null);await ZemaxExecutor.Build(api,root,CancellationToken.None);
        var summaries=new List<object>();
        foreach(var item in new[]{("toroid-0",0d,1),("toroid-30",30d,1),("toroid-45",45d,1),("toroid-60",60d,1),("cube-c1",0d,1),("cube-c2",0d,2)})
        {
            var toroid=item.Item1.StartsWith("toroid");
            var original=Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples",toroid?"Miscellaneous/Toroid.zmx":"Birefringent prisms/Birefringent cube.zmx");
            var source=original;
            if(toroid && item.Item2!=45) {source=Path.Combine(root,item.Item1+".zmx");File.WriteAllText(source,File.ReadAllText(original).Replace("PARM 5 45",FormattableString.Invariant($"PARM 5 {item.Item2:R}")).Replace("PARM 5 -45",FormattableString.Invariant($"PARM 5 {-item.Item2:R}")).Replace("PPAR 5 1 -1.000000000000E+00 0.000000000000E+00 0", ""));}
            var operands=new List<object>();
            object Op(string code,int int1,int int2,double a=0,double b=0,double c=0,double d=0)=>new{code,int1,int2,data1=a,data2=b,data3=c,data4=d,data5=0d,data6=0d};
            if(toroid)
            {
                foreach(var hy in new[]{0d,.5,1}) foreach(var code in new[]{"FCGS","FCGT"}) operands.Add(Op(code,1,0,0,hy));
            }
            else foreach(var wave in new[]{0,1,2,3}) {operands.Add(Op("EFFL",wave,0));foreach(var si in new[]{1,2,3,4,5})foreach(var code in new[]{"PARY","PATY"})operands.Add(Op(code,si,wave));}
            foreach(var hy in toroid?new[]{0d,.5,1}:new[]{0d}) foreach(var delta in new[]{0d,.001,.00001})foreach(var p in delta==0?new[]{(0d,0d)}:new[]{(delta,0d),(0d,delta)})
                foreach(var si in toroid?new[]{5}:new[]{1,2,3,4,5})foreach(var code in new[]{"REAX","REAY","REAZ","REAA","REAB","REAC"})operands.Add(Op(code,si,toroid?1:2,0,hy,p.Item1,p.Item2));
            var path=Path.Combine(root,item.Item1+".request.json");var folder=Path.Combine(root,item.Item1);
            JsonFiles.Write(path,new{zosApiPath=api,input=source,inputSha256=Hash(source),configuration=item.Item3,wavelength=toroid?1:2,zemaxVersion="2026 R1",adapter="surface-model-controls",captureScreenshots=false,formalAcceptanceRequest=false,modelOperands=operands});
            var engines=Process.GetProcessesByName("OpticStudio");var count=engines.Length;foreach(var p in engines)p.Dispose();if(count>1)throw new InvalidOperationException("No independent native slot");
            var execution=await ProcessIsolation.Run(Path.Combine(root,"host/ZemaxHost.exe"),[path,folder],root,120,CancellationToken.None,new Dictionary<string,string>{["PATH"]=api+Path.PathSeparator+Environment.GetEnvironmentVariable("PATH")});
            JsonFiles.Write(Path.Combine(folder,"execution.json"),execution);if(execution.ExitCode!=0)throw new InvalidDataException("Native model probe failed");
            var optic=(await WorkbenchExecutor.Import(source)).Configurations[item.Item3-1];var rays=new List<object>();
            foreach(var hy in toroid?new[]{0d,.5,1}:new[]{0d}) foreach(var delta in new[]{0d,.001,.00001})foreach(var p in delta==0?new[]{(0d,0d)}:new[]{(delta,0d),(0d,delta)})
            {var sample=optic.TraceGenericFinalSample(0,hy,p.Item1,p.Item2,toroid?.5:.5875618)!;rays.Add(new{hy,px=p.Item1,py=p.Item2,sample});}
            JsonFiles.Write(Path.Combine(folder,"core-rays.json"),rays);
            summaries.Add(new{item.Item1,item.Item2,item.Item3,source,sourceSha256=Hash(source),coreSha256=core,operands=operands.Count});JsonFiles.Write(Path.Combine(root,"summary.json"),summaries);Console.WriteLine(item.Item1+" captured");
        }
    }
}
