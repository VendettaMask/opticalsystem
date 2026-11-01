from pathlib import Path
import json,math
root=Path(__file__).resolve().parent;results=[]
def strict(value):
    if isinstance(value,float) and not math.isfinite(value):return 'FAILED_NONFINITE:'+str(value)
    if isinstance(value,list):return [strict(v) for v in value]
    if isinstance(value,dict):return {k:strict(v) for k,v in value.items()}
    return value
for name in ['calibration-models.json','rbf-extended.json','rbf-regularized.json','rbf-kernels.json','rbf-response.json','rbf-whitening.json','rbf-weighted.json']:
    path=root/name
    if path.exists():results.append(dict(RawFile=name,DevelopmentSplitExplorationNotBlindValidation=True,ExperimentOutputIsProductQualification=False,ProductQualificationsReportedSeparatelyIn='final-rbf-fit/summary.json',Results=strict(json.loads(path.read_text(encoding='utf-8-sig')))))
(root/'exploration-finite.json').write_text(json.dumps(results,allow_nan=False,indent=2),encoding='utf-8')
