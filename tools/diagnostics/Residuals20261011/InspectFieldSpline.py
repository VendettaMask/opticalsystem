"""Offline inspection of captured field-scan interpolation; never used by Core."""
import json
import pathlib
import sys
import numpy as np
from scipy.interpolate import CubicSpline

root = pathlib.Path(sys.argv[1]).resolve()
actual = json.loads((root / 'artifacts/validation/huygens-offaxis-repair-20261011/field-spline-before.json').read_text(encoding='utf-8-sig'))
native = json.loads((root / 'validation/zemax/2026-r1/ms-l7-analysis-expansion-2026-09-06/huygens-mtf-vs-field-c1/data.json').read_text(encoding='utf-8-sig'))
x = np.array(native['dataSeries'][5]['x'])
y = np.array(native['dataSeries'][5]['y'])[:, 0]
knots = np.linspace(0, 1, 11)
values = np.array(actual['values']['TangentialByFrequency'][5])
for boundary in ['natural', 'not-a-knot', ((1, (values[1]-values[0])*10), (1, (values[-1]-values[-2])*10))]:
    prediction = CubicSpline(knots, values, bc_type=boundary)(x)
    print(str(boundary), 'maximum', np.max(np.abs(prediction-y)), 'relativeL2', np.linalg.norm(prediction-y)/np.linalg.norm(y))
for interval in [0, 1, 8, 9]:
    selected = (x >= knots[interval]) & (x <= knots[interval+1])
    polynomial = np.polyfit(x[selected]-knots[interval], y[selected], 3)
    print('native interval', interval, 'coefficients', polynomial, 'knot error', polynomial[-1]-values[interval],
          'second at endpoints', 2*polynomial[1], 2*polynomial[1]+.6*polynomial[0], 'fit max',
          np.max(np.abs(np.polyval(polynomial,x[selected]-knots[interval])-y[selected])))
