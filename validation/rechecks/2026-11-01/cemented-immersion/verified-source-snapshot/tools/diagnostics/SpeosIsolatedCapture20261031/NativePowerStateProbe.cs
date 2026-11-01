using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

// Read-only numerical inspection of this tool's OWNED, version-pinned RPC process only.
// No patching, injection, registry changes, product dependency or licensing-state access.
internal static class NativePowerStateProbe
{
    [StructLayout(LayoutKind.Sequential)] private struct Region { public nint Base,Allocation;public uint AllocationProtect;public ushort Partition,Padding;public nuint Size;public uint State,Protect,Type,Padding2; }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern nint OpenProcess(uint rights,bool inherit,int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool ReadProcessMemory(nint handle,nint address,byte[] bytes,nuint size,out nuint read);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern nuint VirtualQueryEx(nint handle,nint address,out Region info,nuint size);

    internal static void Capture(Process owned,string source,string output)
    {
        owned.Refresh();var module=owned.Modules.Cast<ProcessModule>().Single(m=>m.ModuleName=="Optis.Speos.dll");
        var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module.FileName))).ToLowerInvariant();
        if(FileVersionInfo.GetVersionInfo(module.FileName).FileVersion!="2026.1.0.37094")throw new InvalidDataException("Native version changed");
        var vtable=unchecked((ulong)(long)module.BaseAddress)+0x12DCD70UL;
        var handle=OpenProcess(0x410,false,owned.Id);if(handle==0)throw new System.ComponentModel.Win32Exception();
        byte[]? Read(ulong address,int size){var bytes=new byte[size];return ReadProcessMemory(handle,unchecked((nint)(long)address),bytes,(nuint)size,out var n)&&n==(nuint)size?bytes:null;}
        ulong Pointer(byte[] bytes,int at)=>BitConverter.ToUInt64(bytes,at);
        double[]? Doubles(ulong address,int count){var b=Read(address,count*8);return b is null?null:Enumerable.Range(0,count).Select(i=>BitConverter.ToDouble(b,i*8)).ToArray();}
        var result=new List<object>();ulong scanned=0;
        try
        {
            ulong cursor=0;
            while(cursor<0x00007fff00000000UL&&scanned<512UL*1024*1024)
            {
                if(VirtualQueryEx(handle,unchecked((nint)(long)cursor),out var region,(nuint)Marshal.SizeOf<Region>())==0)break;
                var address=unchecked((ulong)(long)region.Base);var size=(ulong)region.Size;var next=address+size;if(next<=cursor)break;cursor=next;
                if(region.State!=0x1000||region.Type!=0x20000||(region.Protect&0x100)!=0||(region.Protect&0xEE)==0||size>32*1024*1024)continue;
                var bytes=Read(address,(int)size);if(bytes is null)continue;scanned+=size;
                for(var i=0;i<bytes.Length-0x260;i+=8)if(Pointer(bytes,i)==vtable)
                {
                    var obj=bytes.AsSpan(i,0x260).ToArray();var nr=BitConverter.ToInt32(obj,0x1D8);
                    if(obj[0xB0]!=1||obj[0xB1]!=0||nr is <1 or >65)continue;
                    var counts=Read(Pointer(obj,0x130),nr*4);var dRows=Read(Pointer(obj,0x1B0),nr*8);var iRows=Read(Pointer(obj,0x1C0),nr*8);
                    if(counts is null||dRows is null||iRows is null)continue;
                    var rows=new List<object>();bool valid=true;
                    for(var r=0;r<nr;r++){var count=BitConverter.ToInt32(counts,r*4);if(count is <1 or >64){valid=false;break;}var diffuse=Doubles(Pointer(dRows,r*8),count);var full=Doubles(Pointer(iRows,r*8),count);if(diffuse is null||full is null||diffuse.Concat(full).Any(v=>!double.IsFinite(v)||v<0||v>1e6)){valid=false;break;}rows.Add(new{RotationIndex=r,DiffuseIntegrals=diffuse,FullIntegrals=full});}
                    if(valid)result.Add(new{Alpha=BitConverter.ToDouble(obj,0xC0),White=obj[0x240],Rows=rows});
                }
            }
        }
        finally{CloseHandle(handle);}
        File.WriteAllText(output,JsonSerializer.Serialize(new{Scope="Read-only controlled numerical buffers, owned RPC process; fixed binary identity and RTTI layout; not a product loader",SourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant(),ModuleSha256=hash,ScannedBytes=scanned,Values=result},new JsonSerializerOptions{WriteIndented=true}));
    }
}
