using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private const string Viewer = @"D:\Program Files\ANSYS Inc\v261\Optical Products\Viewers\BSDF_BRDF_Anisotropic_Viewer.exe";
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct Startup { public int cb; public string? reserved,desktop,title; public int x,y,cx,cy,xChars,yChars,fill,flags; public short show,reserved2; public IntPtr reservedPtr,input,output,error; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr process,thread; public int pid,tid; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr CreateDesktopW(string name, IntPtr device,IntPtr mode,uint flags,uint access,IntPtr security);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool CreateProcessW(string app,string command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string cwd,ref Startup startup,out ProcessInfo info);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    private static Process Spawn(string executable,string arguments,string desktop)
    {
        var startup = new Startup { cb=Marshal.SizeOf<Startup>(),desktop=desktop,flags=1,show=0 };
        if(!CreateProcessW(executable,"\""+executable+"\" "+arguments,IntPtr.Zero,IntPtr.Zero,false,0,IntPtr.Zero,Path.GetDirectoryName(executable)!,ref startup,out var pi)) throw new System.ComponentModel.Win32Exception();
        var managed=Process.GetProcessById(pi.pid);_=managed.Handle;CloseHandle(pi.process);CloseHandle(pi.thread);return managed;
    }
    private static void Save(string path,object value)=>File.WriteAllText(path,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
    [STAThread] public static int Main(string[] args)
    {
        if(args[0] is "rpc" or "trace" or "prepare" or "inspect") return RpcCapture.Run(args).GetAwaiter().GetResult();
        var mode=args[0];var result=Path.GetFullPath(args[1]);var source=Path.GetFullPath(args[2]);var xmp=mode.EndsWith("-xmp");var viewer=xmp?Path.Combine(Path.GetDirectoryName(Viewer)!,"XmpViewer.exe"):Viewer;
        if(mode.StartsWith("broker"))
        {
            if(Process.GetProcessesByName(Path.GetFileNameWithoutExtension(viewer)).Length>0){Save(result,new{Status="Existing viewer: no attach or alteration"});return 2;}
            var name="Codex_SpeosReference_"+Guid.NewGuid().ToString("N");var desktop=CreateDesktopW(name,IntPtr.Zero,IntPtr.Zero,0,0x83,IntPtr.Zero);
            if(desktop==IntPtr.Zero){Save(result,new{Status="Private desktop creation failed",Error=Marshal.GetLastWin32Error()});return 2;}
            try{
                var self=Path.Combine(AppContext.BaseDirectory,"SpeosIsolatedCapture20261031.exe");using var worker=Spawn(self,(xmp?"worker-xmp":"worker")+" \""+result+"\" \""+source+"\" \"winsta0\\"+name+"\"","winsta0\\"+name);
                if(!worker.WaitForExit(45000)){worker.Kill(true);worker.WaitForExit();Save(result,new{Status="Timed out in private desktop; owned worker tree stopped",PrivateDesktop=name,NoDesktopSwitch=true});return 2;}
                return worker.ExitCode;
            } finally{CloseDesktop(desktop);}
        }
        Process? server=null;object? instance=null;
        try{
            server=Spawn(viewer,"/Embedding",args[3]);
            dynamic? app=null;var type=Type.GetTypeFromProgID(xmp?"XmpViewer.Application261":"SimpleBSDFSurfaceViewer.Application261");Exception? activation=null;
            for(int i=0;i<60;i++){
                if(server.HasExited)throw new InvalidOperationException("Owned embedded server exited with "+server.ExitCode);
                try{instance=Activator.CreateInstance(type!);app=instance;break;}catch(COMException ex){activation=ex;Thread.Sleep(250);}
            }
            if(app is null)throw activation??new InvalidOperationException("No COM factory");
            int pid=Convert.ToInt32(app.GetPID());if(pid!=server.Id)throw new InvalidOperationException("Factory belongs to another process; no calls made");
            app.Show((short)0);bool opened=Convert.ToBoolean(app.OpenFile(source));
            if(!opened)throw new InvalidDataException("Native OpenFile returned false");
            if(xmp){var exported=app.ExtendedExportTXT(Path.ChangeExtension(source,".txt"),(short)1);var layers=new List<object>();int count=Convert.ToInt32(app.ExtendedGetNbSource());for(int i=0;i<count;i++){object name="";app.ExtendedGetSourceName(i,ref name);layers.Add(new{Index=i,Name=name});}Save(result,new{Status="Native XMP numeric export",ExportStatus=exported,XMin=app.XMin(),XMax=app.XMax(),XNb=app.XNb(),YMin=app.YMin(),YMax=app.YMax(),YNb=app.YNb(),MapType=app.MapType(),UnitType=app.UnitType(),ValueType=app.ValueType(),Layers=layers,SourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant(),OwnedServerPid=pid,NoDesktopSwitch=true});return 0;}
            var samples=new List<object>();foreach(var theta in new[]{0d,.10471975511965977,6d,30d,60d})foreach(var wave in new[]{400d,550d,700d}){
                double r=0,t=0;object ro=app.GetReflectionCoefficient(theta,0d,wave,ref r);object to=app.GetTransmissionCoefficient(theta,0d,wave,ref t);samples.Add(new{IncidenceInput=theta,WavelengthInput=wave,Reflection=r,Transmission=t,ReflectionStatus=ro,TransmissionStatus=to});
            }
            Save(result,new{Status="Native coefficients captured",Engine="Speos Viewer COM",Version=FileVersionInfo.GetVersionInfo(Viewer).FileVersion,ExecutableSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Viewer))).ToLowerInvariant(),Source=source,SourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant(),PrivateDesktop=args[3],NoDesktopSwitch=true,OwnedServerPid=pid,Values=samples,DirectionalBsdfValuesCaptured=false});return 0;
        }catch(Exception ex){Save(result,new{Status="Native capture failed",Error=ex.ToString(),PrivateDesktop=args[3],NoDesktopSwitch=true,OwnedServerPid=server?.Id,DirectionalBsdfValuesCaptured=false});return 2;}
        finally{if(instance is not null)Marshal.FinalReleaseComObject(instance);if(server is not null){if(!server.HasExited){server.Kill(true);server.WaitForExit();}server.Dispose();}}
    }
}
