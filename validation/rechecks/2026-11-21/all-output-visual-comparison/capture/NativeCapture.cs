using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using ZOSAPI;
using ZOSAPI.Analysis;

class NativeCapture
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup { public int cb; public string reserved,desktop,title; public int x,y,cx,cy,xChars,yChars,fill,flags; public short show,reserved2; public IntPtr reservedPtr,input,output,error; }
    [StructLayout(LayoutKind.Sequential)] struct PI { public IntPtr process,thread; public int pid,tid; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateDesktopW(string name,IntPtr device,IntPtr mode,uint flags,uint access,IntPtr security);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcessW(string app,string cmd,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string cwd,ref Startup startup,out PI pi);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static string Quote(string value) { return "\""+value+"\""; }
    [STAThread] static int Main(string[] args)
    {
        string output=Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
        if(args[0]=="broker")
        {
            string name="Codex_VisualComparison_"+Guid.NewGuid().ToString("N");
            IntPtr desktop=CreateDesktopW(name,IntPtr.Zero,IntPtr.Zero,0,0x83,IntPtr.Zero);
            if(desktop==IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            try
            {
                string self=Assembly.GetExecutingAssembly().Location;
                var si=new Startup { cb=Marshal.SizeOf(typeof(Startup)),desktop="winsta0\\"+name,flags=1,show=0 }; PI pi;
                string cmd=Quote(self)+" worker "+Quote(args[1])+" "+Quote(output)+" "+Quote(args[3]);
                if(args.Length>4) cmd+=" "+Quote(args[4]);
                if(!CreateProcessW(self,cmd,IntPtr.Zero,IntPtr.Zero,false,0,IntPtr.Zero,output,ref si,out pi)) throw new System.ComponentModel.Win32Exception();
                using(var process=Process.GetProcessById(pi.pid))
                {
                    IntPtr openedHandle=process.Handle;
                    CloseHandle(pi.process);CloseHandle(pi.thread);
                    File.WriteAllText(Path.Combine(output,"private-desktop.txt"),"No desktop switch; "+name+"; owned worker "+pi.pid);
                    if(!process.WaitForExit(600000)) { process.Kill(); File.WriteAllText(Path.Combine(output,"timeout.txt"),"Native capture exceeded ten minutes"); return 4; }
                    return File.Exists(Path.Combine(output,"complete.txt"))?0:2;
                }
            }
            finally { CloseDesktop(desktop); }
        }
        string api=args[3];
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e) { string p=Path.Combine(api,new AssemblyName(e.Name).Name+".dll"); return File.Exists(p)?Assembly.LoadFrom(p):null; };
        try { return Run(args[1],output,api,args.Length>4?args[4]:null); }
        catch(Exception e) { File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 2; }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(string source,string output,string api,string macroOverride)
    {
        if(!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(api)) throw new Exception("Initializer failed");
        IZOSAPI_Application app=null;
        try
        {
            app=new ZOSAPI_Connection().CreateNewApplication();
            File.WriteAllText(Path.Combine(output,"environment.txt"),app.OpticStudioVersion+"; license="+app.LicenseStatus+"; valid="+app.IsValidLicenseForAPI);
            if(!app.IsValidLicenseForAPI) throw new Exception("ZOS-API license unavailable");
            app.PrimarySystem.LoadFile(source,false);
            if(File.Exists(Path.Combine(output,"force-first-configuration.txt")))
            {
                app.PrimarySystem.MCE.SetCurrentConfiguration(1);
                File.WriteAllText(Path.Combine(output,"configuration.txt"),"SetCurrentConfiguration(1) succeeded before saving the disposable screenshot input.");
            }
            app.PrimarySystem.SaveAs(Path.Combine(output,"screenshot-input.ZMX"));
            foreach(string row in File.ReadAllLines(Path.Combine(output,"requests.tsv")))
            {
                var fields=row.Split('\t'); if(fields.Length<3) continue; IA_ a=null;
                try
                {
                    AnalysisIDM id=(AnalysisIDM)Enum.Parse(typeof(AnalysisIDM),fields[1]);
                    a=app.PrimarySystem.Analyses.New_Analysis_SettingsFirst(id);
                    var settings=a.GetSettings();
                    settings.SaveTo(Path.Combine(output,fields[0]+".CFG"));
                }
                catch(Exception e) { File.AppendAllText(Path.Combine(output,"settings-errors.txt"),fields[0]+": "+e+Environment.NewLine); }
                finally { if(a!=null) a.Close(); }
            }
        }
        finally { if(app!=null) app.CloseApplication(); }
        if(macroOverride!=null) return RunMacro(output,api,macroOverride);
        if(File.Exists(Path.Combine(output,"supplement-capture.zpl"))) return RunMacro(output,api,Path.Combine(output,"supplement-capture.zpl"));
        string macro=Path.Combine(output,"capture.zpl");
        var text=new System.Text.StringBuilder();
        text.AppendLine("LOADLENS \""+Path.Combine(output,"screenshot-input.ZMX")+"\"");
        text.AppendLine("UPDATE ALL");
        foreach(string row in File.ReadAllLines(Path.Combine(output,"requests.tsv")))
        {
            var f=row.Split('\t'); if(f.Length<3||!File.Exists(Path.Combine(output,f[0]+".CFG"))) continue;
            text.AppendLine("OPENANALYSISWINDOW \""+f[2]+"\", \""+Path.Combine(output,f[0]+".CFG")+"\"");
            text.AppendLine("PAUSE THREADS"); text.AppendLine("window_number = WINL()");
            text.AppendLine("EXPORTJPG window_number, \""+Path.Combine(output,f[0]+".jpg")+"\"");
            text.AppendLine("CLOSEWINDOW window_number");
        }
        text.AppendLine("OUTPUT \""+Path.Combine(output,"complete.txt")+"\"");text.AppendLine("PRINT \"completed\"");text.AppendLine("CLOSEWINDOW");
        if(!File.Exists(macro)) File.WriteAllText(macro,text.ToString());
        return RunMacro(output,api,macro);
    }
    static int RunMacro(string output,string api,string macro)
    {
        var start=new ProcessStartInfo(Path.Combine(api,"OpticStudio.exe"),"-zpl="+Quote(macro)) { WorkingDirectory=api,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true };
        using(var process=Process.Start(start))
        {
            var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
            File.WriteAllText(Path.Combine(output,"macro-pid.txt"),process.Id.ToString());
            if(!process.WaitForExit(480000)) { process.Kill();return 4; }
            File.WriteAllText(Path.Combine(output,"macro-exit.txt"),process.ExitCode+"\n"+stdout.Result+"\n"+stderr.Result);
            return File.Exists(Path.Combine(output,"complete.txt"))?0:2;
        }
    }
}
