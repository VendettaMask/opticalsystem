"""Preserve raw experimental overflows; produce a strict finite verdict separately."""
from pathlib import Path
import json, math
root=Path(__file__).resolve().parent
raw=json.loads((root/'exploration.json').read_text(),parse_constant=lambda s:s)
out=[]
for material in raw:
    finite=[m for m in material['Models'] if isinstance(m.get('Nrmse'),(float,int)) and math.isfinite(m['Nrmse']) and isinstance(m.get('Max'),(float,int)) and math.isfinite(m['Max'])]
    valid=[m for m in finite if m['Negative']==0]
    out.append(dict(Material=material['Material'],ExperimentalModels=len(material['Models']),
                    NonfiniteOrFailedModels=len(material['Models'])-len(finite),
                    PassingUnchangedGate=[m for m in valid if m['Nrmse']<=.01 and m['Max']<=.01],
                    BestNonnegativeExploratoryModel=min(valid,key=lambda m:m['Nrmse']),
                    Models=material['Models']))
(root/'exploration-finite.json').write_text(json.dumps(dict(
    DiagnosticOnly=True,ModelsNotAdoptedInProduct=True,
    OriginalSplitUsedDuringDevelopmentNotNewBlindValidation=True,
    NonfiniteValuesEncodedAsStringsAndRejected=True,Cases=out),indent=2,allow_nan=False),encoding='utf-8')
print([(r['Material'],len(r['PassingUnchangedGate']),r['BestNonnegativeExploratoryModel']['Nrmse']) for r in out])
