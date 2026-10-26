"""Standalone scientific summary; each boundary difference remains explicit."""
import json
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

root=Path(__file__).resolve().parent
summary=json.loads((root/'summary.json').read_text())
source=json.loads((root/'csharp.json').read_text())
normal=[row for row in summary['perCase'] if not row['boundary']]
fig,axes=plt.subplots(1,3,figsize=(14,4),constrained_layout=True)
labels=['Initial 42','Initial 101','Asphere 1','Asphere 2','Active constraints','Catalog stage 1','Catalog partial refine']
for offset,mode,color in [(-.18,'Cpu','#2563eb'),(.18,'Gpu','#ea580c')]:
    values=[max(metric['maximumGradientNormalizedError'] for row in source['reports'] if row['name']==case['name'] and row['mode']==mode for metric in row['comparisons']) for case in normal]
    axes[0].barh([i+offset for i in range(len(labels))],values,height=.34,color=color,label=mode)
axes[0].set(yticks=range(len(labels)),yticklabels=labels,xscale='log',xlabel='Maximum normalized gradient error',title='All optimizer coordinates: ordinary cases')
axes[0].axvline(5e-6,color='#64748b',linestyle='--',linewidth=1,label='Predeclared tolerance')
axes[0].legend(fontsize=8)
axes[0].invert_yaxis()
near=summary['substantiveBoundaryDifferences']['criticalNear']
axes[1].bar(['Original EPS=1e-12','EPS=0 diagnostic'],[near['maximumGradientNormalizedDifference'],near['controlledNoEpsilonMaximumGradientDifference']],color=['#dc2626','#2563eb'])
axes[1].set(yscale='log',ylabel='Maximum normalized gradient error',title='Near critical refraction: fixed same inputs')
axes[1].text(.5,.95,'Diagnostic changes upstream runtime constant only;\nnot a replacement for original baseline.',transform=axes[1].transAxes,ha='center',va='top',fontsize=8)
axes[2].bar(['Original strict edge','C# inclusive edge'],[60,84],color=['#2563eb','#ea580c'])
axes[2].set(ylim=(0,132),ylabel='Valid rays / 132',title='Exactly on planar-stop boundary')
axes[2].text(.5,.93,'24 differences are exactly at +r / -r.\nExact critical derivative is rejected by C#.',transform=axes[2].transAxes,ha='center',va='top',fontsize=8)
for axis in axes:
    axis.grid(axis='x' if axis is axes[0] else 'y',alpha=.2)
fig.savefig(root/'comparison.png',dpi=160)
plt.close(fig)
