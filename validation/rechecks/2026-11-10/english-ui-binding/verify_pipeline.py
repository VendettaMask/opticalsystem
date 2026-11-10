from pathlib import Path
import subprocess,json,tempfile,sys
root=Path(sys.argv[1]) if len(sys.argv)>1 else Path(tempfile.mkdtemp(prefix='osd-language-pipeline-'));app=root/'fixture/src/OpticalSystemDesign.App';inputs=root/'fixture/localization';(app/'Controls').mkdir(parents=True,exist_ok=True);inputs.mkdir(parents=True,exist_ok=True)
seed='class UiLocalization { static object BuildTexts() { void Add(string source, string en, string ja) {} Add("保存", "Save", "保存"); Add("厚度 1.25 nm", "Thickness 1.25 nm", "膜厚 1.25 nm"); return new object(); } }'
view='class View { void Build() { var button = new Button { Content = "保存" }; } }'
sp=app/'UiLocalization.cs';vp=app/'View.cs';sp.write_text(seed);vp.write_text(view);(inputs/'optical-terms.tsv').write_text('辐照度\tIrradiance\t放射照度\n')
results=[]
def check(name,command,success,diagnostic=''):
 r=subprocess.run(['dotnet','tools/OpticalSystemDesign.Localization/bin/Release/net10.0/OpticalSystemDesign.Localization.dll',command,str(root/'fixture')],capture_output=True,text=True)
 passed=((r.returncode==0)==success) and diagnostic in r.stdout+r.stderr
 results.append(dict(name=name,exitCode=r.returncode,expectedSuccess=success,passed=passed));assert passed,(name,r.stdout,r.stderr)
check('baseline-sync','sync',True);check('baseline-check','check',True)
cp=app/'Assets/Localization/ui.catalog.json';original=cp.read_bytes();vp.write_text(view.replace('"保存"','"未翻译的新增控件"'))
check('missing-label','check',False,'Untranslated UI');check('missing-label-sync','sync',False,'Untranslated UI');assert cp.read_bytes()==original
vp.write_text(view);cp.write_text(original.decode().replace('Save','Damaged'));check('stale-catalog','check',False,'Local catalog is stale');cp.write_bytes(original)
sp.write_text(seed.replace('Thickness 1.25 nm','Thickness 1.25 mm'));check('changed-unit','sync',False,'unit symbol mismatch');sp.write_text(seed)
(inputs/'ui-translations.tsv').write_text('辐照度\tIlluminance\t照度\n');check('conflicting-term','check',False,'Optical term override conflicts');(inputs/'ui-translations.tsv').write_text('');check('restored-check','check',True)
(root/'results.json').write_text(json.dumps({'runner':'Python, equivalent command controls from VerifyPipeline.ps1 (PowerShell unavailable)','results':results},indent=2)+'\n');print(len(results),'command controls passed')
