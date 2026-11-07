const fs = require('node:fs');
const root = process.argv[2];
const earlier = process.argv[3];
const read = f => JSON.parse(fs.readFileSync(f, 'utf8').replace(/^\uFEFF/, ''));
const readAt = name => read(`${root}/${name}`);
const maxAbs = values => Math.max(0, ...values.map(Math.abs));
const dot = (a, b) => a.reduce((s, x, i) => s + x * b[i], 0);
const position = ray => [ray.x, ray.y, ray.z];
// Three documented mirror reflections reverse the native final-surface LMN sign.
const direction = ray => [-ray.l, -ray.m, -ray.n];
const reports = [];
for (const field of [1, 2]) for (const wave of [1, 2, 3]) {
    const tag = `f${field}w${wave}`;
    const request = readAt(`requests/path-${tag}.json`);
    const source = readAt(`path-${tag}/point-reference.json`);
    const opth = readAt(`path-${tag}/opth.json`);
    const geometry = readAt(`path-${tag}/image-geometry.json`);
    const plotDirectory = field === 2 && wave !== 2 ? `${earlier}/opd-${wave===1?458:466}-x8` : `${root}/plot-${tag}`;
    const plot = read(`${plotDirectory}/data.json`);
    const w = readAt(`path-${tag}/model.json`).wavelengths[wave - 1].data.Wavelength;
    const chief = geometry[0];
    const grating = source.surfaces.find(s => s.surface === 3).rays;
    const imageOpth = opth.surfaces.at(-1).rows;
    const samples = [];
    const phaseSamples = [];
    const factors = plot.dataSeries.map(series => (series.x.length - 1) / (2 * request.rayCount));
    const slopes = readAt('matrix-candidates.json').find(r => r.id === 458);
    const oldAxial = slopes.sphere.radius * readAt('path-f2w1/image-geometry.json')[0].n;
    const projectedAxial = slopes.proposedRadius * readAt('path-f2w1/image-geometry.json')[0].n;
    for (const [axis, axisIndex] of [['tangential', 0], ['sagittal', 1]]) {
        let k = -1;
        for (const [i, p] of source.inputs.entries()) {
            if (p.role !== axis) continue;
            k++;
            if (geometry[i].error || geometry[i].vignette || p.coordinate === 0) continue;
            const nativeIndex = factors[axisIndex] * k;
            const curve = plot.dataSeries[axisIndex];
            if (Math.abs(curve.x[nativeIndex] - p.coordinate) > 1e-12) throw Error('Plot and batch nodes differ');
            const nativeValue = curve.y[nativeIndex].find(x => typeof x === 'number');
            const phase = 0.1 * w * (grating[i].y - grating[0].y);
            for (const surface of source.surfaces) {
                const s = surface.surface;
                const rays = s === source.surfaces.at(-1).surface ? geometry : surface.rays;
                const measured = opth.surfaces[s - 1].rows;
                const rawOpl = rays[i].opdWaves - rays[0].opdWaves;
                const nativeOpth = measured[i].opthMillimeters - measured[0].opthMillimeters;
                phaseSamples.push({ surface: s, axis, x: p.coordinate, nativeOpth, rawOpl, expectedPhase: s >= 3 ? phase : 0,
                    error: nativeOpth - rawOpl - (s >= 3 ? phase : 0) });
            }
            if (!Number.isFinite(nativeValue)) continue;
            const displacement = position(geometry[i]).map((x,j) => x - position(chief)[j]);
            const b = dot(direction(geometry[i]), displacement);
            const e = dot(displacement, displacement) - b*b;
            const opl = imageOpth[i].opthMillimeters - imageOpth[0].opthMillimeters;
            const q = nativeValue * w * 1e-3 + opl - b;
            const impliedAxial = -(e + q*q) / (2*q) * chief.n;
            const evaluate = axial => {
                const r = axial / chief.n;
                return (b - e / (Math.sqrt(r*r-e) + r) - opl) / (w*1e-3);
            };
            const old = evaluate(oldAxial);
            const projected = evaluate(projectedAxial);
            const pointBound = 1e-5 + .001 * Math.abs(nativeValue);
            samples.push({ axis, x: p.coordinate, nativeValue, old, projected, oldError: old-nativeValue,
                projectedError: projected-nativeValue, pointBound, impliedAxial, opthMillimeters: opl });
        }
    }
    const stable = samples.filter(p => Math.abs(p.x) >= .1 && Math.abs(p.impliedAxial) < 1);
    reports.push({ field, wave, wavelengthMicrometers: w, tag, oldAxial, projectedAxial,
        nativeOperand: readAt(`path-${tag}/exit-pupil.json`), nativeImpliedAxial: [Math.min(...stable.map(p=>p.impliedAxial)),Math.max(...stable.map(p=>p.impliedAxial))],
        maximumPhaseDifferenceMillimeters: maxAbs(phaseSamples.map(p=>p.error)),
        maximumOldDifference: maxAbs(samples.map(p=>p.oldError)), maximumProjectedDifference: maxAbs(samples.map(p=>p.projectedError)),
        oldPointwiseFailures: samples.filter(p=>Math.abs(p.oldError)>p.pointBound).length,
        projectedPointwiseFailures: samples.filter(p=>Math.abs(p.projectedError)>p.pointBound).length,
        samples, phaseSamples });
}
console.log(JSON.stringify({ reports }, null, 2));
