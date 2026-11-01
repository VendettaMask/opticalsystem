param([string]$TypeName='CAnisotropicScatteringSurface')
$ErrorActionPreference='Stop'
# Read-only diagnostic metadata. Does not load, patch or execute the native DLL.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
public static class NativeSurfaceSymbols {
 public static object[] Read(string path,string name) {
  var b=File.ReadAllBytes(path);var pe=BitConverter.ToInt32(b,60);var n=BitConverter.ToUInt16(b,pe+6);var optional=pe+24;var table=optional+BitConverter.ToUInt16(b,pe+20);var image=BitConverter.ToUInt64(b,optional+24);
  var sections=new List<(int Va,int Raw,int Size)>();for(int i=0;i<n;i++){var p=table+40*i;sections.Add((BitConverter.ToInt32(b,p+12),BitConverter.ToInt32(b,p+20),BitConverter.ToInt32(b,p+16)));}
  int Rva(int at){foreach(var s in sections)if(at>=s.Raw&&at<s.Raw+s.Size)return s.Va+at-s.Raw;throw new Exception("No section");}
  var text=Encoding.Latin1.GetString(b);int index=text.IndexOf(".?AV"+name+"@@\0",StringComparison.Ordinal);if(index<0)throw new Exception("Type not found");int type=Rva(index-16);var output=new List<object>();
  for(int p=0;p<b.Length-24;p+=4)if(BitConverter.ToInt32(b,p)==1&&BitConverter.ToInt32(b,p+12)==type&&BitConverter.ToInt32(b,p+20)==Rva(p)){
   ulong col=image+(uint)Rva(p);for(int q=0;q<b.Length-264;q+=8)if(BitConverter.ToUInt64(b,q)==col){var functions=new List<object>();for(int k=0;k<80;k++){ulong va=BitConverter.ToUInt64(b,q+8+8*k);functions.Add(new{Slot=k,Offset="0x"+(8*k).ToString("X"),Va="0x"+va.ToString("X")});}output.Add(new{Type=name,TypeRva="0x"+type.ToString("X"),CompleteObjectLocatorRva="0x"+Rva(p).ToString("X"),ObjectOffset=BitConverter.ToInt32(b,p+4),VtableRva="0x"+Rva(q+8).ToString("X"),Functions=functions});}
  }
  return output.ToArray();
 }
}
'@
$nativePath=Join-Path $PSScriptRoot '../commercial-bsdf-calibration/native-runtime/Optical Products/SPEOS_RPC/Optis.Speos.dll'
[NativeSurfaceSymbols]::Read((Resolve-Path $nativePath).Path,$TypeName) | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $PSScriptRoot ($TypeName+'-symbols.json'))
Get-Content (Join-Path $PSScriptRoot ($TypeName+'-symbols.json'))
