"""Cloud-only Sparkle replacement regression using complete disposable bundles.

The AppKit fixture, loopback feed and throwaway trust key are test-only. The
product's actual managed/native payload is copied without modification; fixture
Info.plists select test builds/key/local network. No user's installation is used.
"""
import functools
import hashlib
import http.server
import json
import pathlib
import plistlib
import subprocess
import sys
import tempfile
import threading
import time
import xml.etree.ElementTree as ET

def run(*args, **kwargs):
    return subprocess.run([str(a) for a in args], check=True, text=True, capture_output=True, timeout=180, **kwargs).stdout.strip()

def manifest(bundle):
    return {str(p.relative_to(bundle)): ('link:'+p.readlink().as_posix() if p.is_symlink() else hashlib.file_digest(p.open('rb'),'sha256').hexdigest())
        for p in sorted(bundle.rglob('*')) if p.is_file() or p.is_symlink()}

def modify(bundle, public, build):
    path = bundle/'Contents/Info.plist'
    info = plistlib.loads(path.read_bytes())
    info.update(CFBundleVersion=build, SUPublicEDKey=public, SUEnableAutomaticChecks=False,
        SUAutomaticallyUpdate=False, SUVerifyUpdateBeforeExtraction=True, SURequireSignedFeed=True,
        NSAppTransportSecurity={'NSAllowsLocalNetworking':True,'NSAllowsArbitraryLoads':True})
    path.write_bytes(plistlib.dumps(info))
    run('/usr/bin/codesign','--force','--sign','-','--timestamp=none',bundle)
    run('/usr/bin/codesign','--verify','--deep','--strict',bundle)

class QuietHandler(http.server.SimpleHTTPRequestHandler):
    def log_message(self,*args): pass

def main():
    if sys.platform != 'darwin': raise RuntimeError('Run only on an isolated Mac cloud worker')
    source, dependency, output = map(lambda p:pathlib.Path(p).resolve(),sys.argv[1:])
    if output.exists(): raise RuntimeError('A fresh owned output directory is required')
    output.mkdir(parents=True)
    repository = pathlib.Path(__file__).resolve().parent.parent
    sparkle = dependency/'Sparkle-2.10.1'
    tool = sparkle/'bin/sign_update'
    web = output/'web'; web.mkdir()
    server = http.server.ThreadingHTTPServer(('127.0.0.1',0),functools.partial(QuietHandler,directory=str(web)))
    threading.Thread(target=server.serve_forever,daemon=True).start()
    url = f'http://127.0.0.1:{server.server_port}'
    results=[]
    try:
        with tempfile.TemporaryDirectory(prefix='osd-ota-private-') as private_directory:
            secret = pathlib.Path(private_directory)/'signing-key'
            swift = ('import Foundation; import CryptoKit; let key=Curve25519.Signing.PrivateKey(); '
                'FileManager.default.createFile(atPath:CommandLine.arguments[1],contents:Data(key.rawRepresentation.base64EncodedString().utf8),attributes:[.posixPermissions:0o600]); '
                'print(key.publicKey.rawRepresentation.base64EncodedString())')
            public = run('swift','-e',swift,secret)
            target_dir=output/'target'; target_dir.mkdir()
            target=target_dir/'Optical System Design.app'
            run('/usr/bin/ditto',source,target)
            modify(target,public,'1.0.2')
            expected_manifest=manifest(target)
            dmg=web/'update.dmg'
            run('/usr/bin/hdiutil','create','-volname','Optical System Design OTA fixture','-srcfolder',target_dir,'-format','UDZO','-ov',dmg)
            signature=run(tool,'--ed-key-file',secret,'-p',dmg)
            original_digest=hashlib.file_digest(dmg.open('rb'),'sha256').hexdigest()
            for mode in ['cancel-download','cancel-ready','tampered-archive','apply']:
                case=output/mode; case.mkdir()
                host=case/'Optical System Design.app'
                run('/usr/bin/ditto',source,host); modify(host,public,'1.0.1')
                baseline=manifest(host)
                profile=case/'profile'; profile.mkdir()
                (profile/'project.staropt').write_bytes(b'owned-project-sentinel')
                (profile/'settings.json').write_bytes(b'{"theme":"Dark","fixture":true}')
                user_before=manifest(profile)
                fixture=case/'OTA Fixture.app'
                payload=fixture/'Contents/MacOS'; payload.mkdir(parents=True)
                framework=fixture/'Contents/Frameworks/Sparkle.framework'
                run('/usr/bin/ditto',sparkle/'Sparkle.framework',framework)
                info=dict(CFBundleIdentifier='com.starlabs.optical-system-design.ota-fixture',CFBundleName='OTA Fixture',
                    CFBundleExecutable='fixture',CFBundlePackageType='APPL',CFBundleVersion='1.0',LSUIElement=True,
                    OSDFixtureRoot=str(case),OSDFixtureMode=mode,OSDFixtureHost=str(host),OSDFixtureFeed=url+'/appcast.xml',
                    NSAppTransportSecurity={'NSAllowsLocalNetworking':True,'NSAllowsArbitraryLoads':True})
                (fixture/'Contents/Info.plist').write_bytes(plistlib.dumps(info))
                run('xcrun','clang','-arch','arm64','-mmacosx-version-min=14.0','-fobjc-arc','-fblocks','-Wall','-Werror',
                    '-F',sparkle,'-framework','Sparkle','-framework','AppKit','-Wl,-rpath,@executable_path/../Frameworks',
                    repository/'packaging/macos/UpdaterInstallFixture.m','-o',payload/'fixture')
                run('/usr/bin/codesign','--force','--sign','-','--timestamp=none',fixture)
                ns='http://www.andymatuschak.org/xml-namespaces/sparkle'; ET.register_namespace('sparkle',ns)
                rss=ET.Element('rss',{'version':'2.0'}); channel=ET.SubElement(rss,'channel'); item=ET.SubElement(channel,'item')
                ET.SubElement(channel,'title').text='Cloud-only OTA fixture'
                ET.SubElement(item,'title').text='Fixture update'
                ET.SubElement(item,'{'+ns+'}version').text='1.0.2'
                ET.SubElement(item,'{'+ns+'}shortVersionString').text='1.0.4'
                ET.SubElement(item,'enclosure',{'url':url+'/update.dmg','length':str(dmg.stat().st_size),
                    'type':'application/octet-stream','{'+ns+'}edSignature':signature})
                feed=web/'appcast.xml'; feed.write_bytes(ET.tostring(rss,encoding='utf-8',xml_declaration=True))
                run(tool,'--ed-key-file',secret,feed)
                if mode=='tampered-archive':
                    with dmg.open('r+b') as f: f.seek(32); original_byte=f.read(1); f.seek(32); f.write(bytes([original_byte[0]^1]))
                log=case/'fixture.log'
                with log.open('w') as stream:
                    process=subprocess.Popen([str(payload/'fixture')],stdout=stream,stderr=stream)
                    try:
                        deadline=time.monotonic()+170
                        while time.monotonic()<deadline and not (case/'result.json').exists(): time.sleep(.25)
                        if not (case/'result.json').exists(): raise RuntimeError(f'{mode}: updater timed out; see fixture.log')
                        result=json.loads((case/'result.json').read_text())
                        if not result['Success']: raise RuntimeError(f'{mode}: {result}')
                        process.wait(timeout=15)
                    finally:
                        if process.poll() is None: process.terminate(); process.wait(timeout=10)
                if mode=='tampered-archive':
                    with dmg.open('r+b') as f: f.seek(32); f.write(original_byte)
                assert manifest(profile)==user_before, 'User data changed'
                if mode=='apply':
                    assert result['Detail']=='Relaunched after installation',result
                    assert manifest(host)==expected_manifest,'Replacement bundle manifest mismatch'
                    report_path=case/'product-verification.json'
                    run(host/'Contents/MacOS/OpticalSystemDesign.App','--verify-installation',report_path)
                    report=json.loads(report_path.read_text())
                    assert report['Success'] and report['ModulesInProcess'] and report['MacOtaNativeBridgeVerified']
                else: assert manifest(host)==baseline,'Cancelled or rejected update modified the installation'
                results.append(dict(Mode=mode,Passed=True,UserDataUnchanged=True,ProductBundleCompared=True,Relaunched=mode=='apply'))
                (output/'summary.json').write_text(json.dumps({'Cases':results,'ProductGuiStarted':False,'ThrowawayKey':True},indent=2)+'\n')
                print(f'Mac native OTA fixture: {mode} passed',flush=True)
            assert hashlib.file_digest(dmg.open('rb'),'sha256').hexdigest()==original_digest
    finally: server.shutdown(); server.server_close()

if __name__=='__main__': main()
