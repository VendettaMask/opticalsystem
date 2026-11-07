const fs = require('node:fs');
const root = process.argv[2];
const earlier = process.argv[3];
const read = f => JSON.parse(fs.readFileSync(f, 'utf8').replace(/^\uFEFF/, ''));
const report = [];
for (const field of [1, 2]) for (const wave of [1, 2, 3]) {
    const tag = `f${field}w${wave}`;
    const actual = read(`${root}/current-spectro/${tag}/data.json`);
    if (actual.outcome !== 'Success' || actual.values.WavelengthNumber !== wave || actual.values.FieldNumber !== field
        || actual.values.NumberOfRaysEachSide !== 160 || actual.values.ReferenceOpdMode !== 'ExitPupil') {
        throw Error(`Unexpected current Workbench identity for ${tag}`);
    }
    const referenceDirectory = field === 2 && wave !== 2 ? `${earlier}/opd-${wave===1?458:466}-x8` : `${root}/plot-${tag}`;
    const native = read(`${referenceDirectory}/data.json`);
    const nativeReadback = read(`${referenceDirectory}/captured-settings.json`);
    if (nativeReadback.selectors.Field !== field || nativeReadback.selectors.Wavelength !== wave
        || nativeReadback.properties.NumberOfRays !== 160) throw Error(`Unexpected original selector readback for ${tag}`);
    const axes = native.dataSeries.map((curve, axis) => {
        const points = actual.plotPanes[axis].series[0].points;
        if (curve.x.length !== points.length || curve.y.length !== points.length) throw Error(`Pupil count differs for ${tag}`);
        const plotted = [];
        for (let i = 0; i < points.length; i++) {
            const workbench = points[i];
            if (Math.abs(curve.x[i] - workbench.x) > 1e-12) throw Error(`Pupil coordinate differs for ${tag}`);
            const native = curve.y[i].find(x => typeof x === 'number');
            if (native === undefined) continue;
            if (!Number.isFinite(workbench.y)) throw Error(`Finite native node has no Workbench trace for ${tag}`);
            const absoluteError = Math.abs(workbench.y - native);
            const pointBound = 1e-5 + .001 * Math.abs(native);
            plotted.push({ pupil: curve.x[i], core: workbench.y, native, absoluteError, pointBound, pass: absoluteError <= pointBound });
        }
        return { axis: axis === 0 ? 'tangential' : 'sagittal', count: plotted.length,
            maxAbsoluteWaves: Math.max(...plotted.map(p => p.absoluteError)),
            pointwiseFailures: plotted.filter(p => !p.pass).length,
            recovered: plotted.find(p => Math.abs(p.pupil + .3) <= 1e-12) };
    });
    report.push({ field, wave, tag, axes });
}
console.log(JSON.stringify(report,null,2));
