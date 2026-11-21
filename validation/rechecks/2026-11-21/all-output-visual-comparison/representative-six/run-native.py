import ctypes,json,subprocess,time
from ctypes import wintypes
from pathlib import Path

OUT=Path(__file__).resolve().parent
API=Path(r'D:\Program Files\ANSYS Inc\v261\Zemax OpticStudio')
TOOL=OUT.parent/'capture/NativeRepresentative.exe'
data=json.loads((OUT/'inputs.json').read_text(encoding='utf-8'))
kernel=ctypes.WinDLL('kernel32',use_last_error=True)
kernel.OpenProcess.argtypes=[wintypes.DWORD,wintypes.BOOL,wintypes.DWORD];kernel.OpenProcess.restype=wintypes.HANDLE
kernel.QueryFullProcessImageNameW.argtypes=[wintypes.HANDLE,wintypes.DWORD,wintypes.LPWSTR,ctypes.POINTER(wintypes.DWORD)]
kernel.GetProcessTimes.argtypes=[wintypes.HANDLE,ctypes.POINTER(wintypes.FILETIME),ctypes.POINTER(wintypes.FILETIME),ctypes.POINTER(wintypes.FILETIME),ctypes.POINTER(wintypes.FILETIME)]
kernel.TerminateProcess.argtypes=[wintypes.HANDLE,wintypes.UINT]
kernel.CloseHandle.argtypes=[wintypes.HANDLE]
def stop_owned_macro(folder,started):
    pidfile=folder/'macro-pid.txt'
    if not pidfile.exists() or pidfile.stat().st_mtime<started:return False
    handle=kernel.OpenProcess(0x1000|1,False,int(pidfile.read_text()))
    if not handle:return False
    try:
        size=wintypes.DWORD(32768);name=ctypes.create_unicode_buffer(size.value)
        if not kernel.QueryFullProcessImageNameW(handle,0,name,ctypes.byref(size)):return False
        if Path(name.value)!=API/'OpticStudio.exe':return False
        created,exited,user,system=(wintypes.FILETIME() for _ in range(4))
        if not kernel.GetProcessTimes(handle,ctypes.byref(created),ctypes.byref(exited),ctypes.byref(user),ctypes.byref(system)):return False
        epoch=((created.dwHighDateTime<<32)+created.dwLowDateTime)/10000000-11644473600
        if epoch<started-3:return False
        return bool(kernel.TerminateProcess(handle,4))
    finally:kernel.CloseHandle(handle)

def macro(folder,rows,attempt):
    target=folder/f'capture-attempt-{attempt:02d}.zpl'
    lines=[f'LOADLENS "{folder/"screenshot-input.ZMX"}"','UPDATE ALL']
    for slug,ident,code in rows:
        lines.extend([f'LOADLENS "{folder/"screenshot-input.ZMX"}"','UPDATE ALL',f'OUTPUT "{folder/"progress.txt"}"',f'PRINT "{slug}"',f'OPENANALYSISWINDOW "{code}", "{folder/(slug+".CFG")}"','PAUSE THREADS','window_number = WINL()',f'EXPORTJPG window_number, "{folder/(slug+".jpg")}"','CLOSEWINDOW window_number'])
    lines.extend([f'OUTPUT "{folder/"complete.txt"}"','PRINT "completed"','CLOSEWINDOW'])
    target.write_text('\n'.join(lines)+'\n',encoding='utf-8')
    return target

for lens in data['lenses']:
    folder=OUT/lens['name']/'zemax'
    rows=[tuple(r.split('\t')) for r in (folder/'requests.tsv').read_text(encoding='utf-8').splitlines()]
    skipped={};attempts=[]
    for attempt in range(1,5):
        captured={p.stem.lower() for p in folder.glob('*') if p.suffix.lower()=='.jpg' and p.stat().st_size>100}
        remaining=[r for r in rows if r[0] not in captured and r[0] not in skipped]
        if not remaining:break
        script=macro(folder,remaining,attempt);started=time.time();last_change=started;last_progress='';last_receipt=None
        with (folder/f'attempt-{attempt:02d}.log').open('w',encoding='utf-8') as log:
            process=subprocess.Popen([str(TOOL),'broker',lens['source'],str(folder),str(API),str(script)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
            stopped=False
            while process.poll() is None:
                progress=folder/'progress.txt'
                try:
                    raw=progress.read_bytes() if progress.exists() and progress.stat().st_mtime>=started else b''
                    current=raw.decode('utf-16' if raw.startswith((b'\xff\xfe',b'\xfe\xff')) else 'utf-8-sig').strip()
                except (OSError,UnicodeError):current=''
                image_files=[p for p in folder.glob('*') if p.suffix.lower()=='.jpg' and p.stat().st_size>100]
                receipt=(len(image_files),max((p.stat().st_mtime for p in image_files),default=0),progress.stat().st_mtime if progress.exists() else 0)
                if current!=last_progress or receipt!=last_receipt:
                    last_progress=current;last_change=time.time()
                    last_receipt=receipt
                if (time.time()-last_change>100 or time.time()-started>480) and stop_owned_macro(folder,started):
                    skipped[current or 'unknown-stage']='Capture watchdog deadline reached; only the owned macro process was stopped. This does not establish an optical analysis failure.'
                    stopped=True;break
                time.sleep(1)
            try:exitcode=process.wait(timeout=20)
            except subprocess.TimeoutExpired:process.terminate();exitcode=4
        attempts.append({'attempt':attempt,'exitCode':exitcode,'lastProgress':last_progress,'stoppedOwnedMacro':stopped})
        (folder/'visual-capture-status.json').write_text(json.dumps({'attempts':attempts,'skipped':skipped},indent=2),encoding='utf-8')
        if not stopped:break
    count=sum(p.stat().st_size>100 for p in folder.glob('*') if p.suffix.lower()=='.jpg')
    print(lens['name']+': native images='+str(count)+'; stalled windows='+str(len(skipped)),flush=True)
