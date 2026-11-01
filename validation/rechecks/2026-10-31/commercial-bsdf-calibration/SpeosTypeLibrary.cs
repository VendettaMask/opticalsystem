using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
public static class SpeosTypeLibrary {
 [DllImport("oleaut32.dll", CharSet=CharSet.Unicode, PreserveSig=false)] static extern void LoadTypeLibEx(string path,int flags,out ITypeLib lib);
 public static void Dump(string path){ITypeLib lib;LoadTypeLibEx(path,2,out lib);for(int n=0;n<lib.GetTypeInfoCount();n++){ITypeInfo info;lib.GetTypeInfo(n,out info);IntPtr p;info.GetTypeAttr(out p);var a=Marshal.PtrToStructure<TYPEATTR>(p);string name,doc,file;int id;info.GetDocumentation(-1,out name,out doc,out id,out file);Console.WriteLine(name+" "+a.typekind+" "+a.guid);for(int j=0;j<a.cFuncs;j++){IntPtr f;info.GetFuncDesc(j,out f);var d=Marshal.PtrToStructure<FUNCDESC>(f);string[] names=new string[32];int count;info.GetNames(d.memid,names,names.Length,out count);Console.WriteLine(d.invkind+" "+string.Join(",",names,0,count));for(int k=0;k<d.cParams;k++){var e=Marshal.PtrToStructure<ELEMDESC>(IntPtr.Add(d.lprgelemdescParam,k*Marshal.SizeOf<ELEMDESC>()));Console.WriteLine("  "+k+" vt="+e.tdesc.vt+" flags="+e.desc.paramdesc.wParamFlags);}info.ReleaseFuncDesc(f);}info.ReleaseTypeAttr(p);}}
}
