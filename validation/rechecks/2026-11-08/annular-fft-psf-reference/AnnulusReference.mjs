import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join } from 'node:path';

const [repo, work, option] = process.argv.slice(2);
const verifyAdaptive = option === '--adaptive';
const parse = path => JSON.parse(readFileSync(path, 'utf8').replace(/^\uFEFF/, ''));
const sha = path => createHash('sha256').update(readFileSync(path)).digest('hex');
const source = name => join(repo, 'src/OpticalSystemDesign.App/Assets/SequentialExamples', name);
const before = parse(join(repo, 'validation/rechecks/2026-11-07/incomparable-meter/meter-comparison.json'));
const nativeIdentity = parse(join(repo, 'validation/rechecks/2026-11-07/incomparable-meter/meter-explicit-fan-results.json'));
const nativeArchive = join(repo, 'validation/rechecks/2026-11-07/incomparable-meter/meter-explicit-fan-native.zip');

function readPrescription(path) {
    const bytes = readFileSync(path);
    const text = (bytes[0] === 255 && bytes[1] === 254 ? bytes.toString('utf16le') : bytes.toString('utf8')).replace(/^\uFEFF/, '');
    const [head, ...surfaces] = text.split(/^SURF \d+\s*$/m);
    const token = (lines, name) => lines.match(new RegExp('^\\s*' + name + '\\s+(\\S+)', 'm'))?.[1];
    const value = (lines, name) => Number(token(lines, name) ?? 0);
    const mirror = lines => token(lines, 'GLAS') === 'MIRROR';
    if (token(head, 'UNIT') !== 'METER' || surfaces.length !== 5 || !surfaces.every(lines => token(lines, 'TYPE') === 'STANDARD') ||
        token(surfaces[0], 'DISZ') !== 'INFINITY' || !mirror(surfaces[2]) || !mirror(surfaces[3]) ||
        surfaces.some((lines, i) => i !== 2 && i !== 3 && token(lines, 'GLAS')) ||
        !surfaces[2].match(/^\s*STOP\s*$/m) || token(surfaces[2], 'COAT') || token(surfaces[3], 'COAT') ||
        value(head, 'APOD') !== 0)
        throw new Error('Expected the audited axial uniform annular two-mirror prescription.');
    const outer = value(head, 'ENPD') / 2;
    const [inner, outerClear] = surfaces[2].match(/^\s*CLAP\s+(\S+)\s+(\S+)/m).slice(1).map(Number);
    const obstruction = Number(surfaces[1].match(/^\s*OBSC\s+\S+\s+(\S+)/m)[1]);
    if (!(inner > obstruction && outer < outerClear))
        throw new Error('Annular pupil ordering changed.');
    const firstZ = value(surfaces[1], 'DISZ');
    const secondZ = firstZ + value(surfaces[2], 'DISZ');
    const imageZ = secondZ + value(surfaces[3], 'DISZ');
    const radii = surfaces.slice(2).map(lines => 1 / value(lines, 'CURV'));
    const conics = surfaces.slice(2).map(lines => value(lines, 'CONI'));
    return { outer, inner, outerClear, obstruction, firstZ, secondZ, imageZ, radii, conics,
        secondaryRadius: value(surfaces[3], 'DIAM'), detectorRadius: value(surfaces[4], 'DIAM'),
        wavelengthMicrometers: Number(head.match(/^WAVM 1\s+(\S+)/m)[1]) };
}

function intersect(ray, vertex, radius, conic) {
    const z = ray.z - vertex;
    const a = ray.u * ray.u + (1 + conic) * ray.v * ray.v;
    const b = 2 * (ray.y * ray.u + (1 + conic) * z * ray.v - radius * ray.v);
    const c = ray.y * ray.y + (1 + conic) * z * z - 2 * radius * z;
    const discriminant = b * b - 4 * a * c;
    if (discriminant < 0) throw new Error('Missing forward conic intersection.');
    const q = -.5 * (b + Math.sign(b || 1) * Math.sqrt(discriminant));
    const distance = [q / a, c / q].filter(s => s > 0 &&
        (((1 + conic) * (z + s * ray.v) - radius) * -radius) > 0).sort((a, b) => a - b)[0];
    if (!Number.isFinite(distance)) throw new Error('Missing intersection on the vertex branch.');
    return { y: ray.y + distance * ray.u, z: ray.z + distance * ray.v, u: ray.u, v: ray.v, distance };
}

function reflect(ray, vertex, radius, conic) {
    const ny = ray.y;
    const nz = (1 + conic) * (ray.z - vertex) - radius;
    const projection = 2 * (ray.u * ny + ray.v * nz) / (ny * ny + nz * nz);
    return { ...ray, u: ray.u - projection * ny, v: ray.v - projection * nz };
}

function trace(lens, pupilRadius) {
    const [r1, r2, ri] = lens.radii;
    const [k1, k2, ki] = lens.conics;
    const a = reflect(intersect({ y: lens.outer * pupilRadius, z: 0, u: 0, v: 1 }, lens.firstZ, r1, k1),
        lens.firstZ, r1, k1);
    const b = reflect(intersect(a, lens.secondZ, r2, k2), lens.secondZ, r2, k2);
    const image = intersect(b, lens.imageZ, ri, ki);
    const mirrorSeparation = lens.firstZ - lens.secondZ;
    const pupilPosition = lens.secondZ - 1 / (1 / mirrorSeparation - 2 / r2);
    const radius = lens.imageZ - pupilPosition;
    const dz = image.z - lens.imageZ;
    const axial = image.y * image.u + dz * image.v;
    const transverseSquared = (image.y * image.v - dz * image.u) ** 2;
    const spherePathChange = axial - transverseSquared / (radius + Math.sqrt(radius ** 2 - transverseSquared));
    const chiefPath = lens.firstZ + mirrorSeparation + (lens.imageZ - lens.secondZ);
    const opdMeters = chiefPath - (a.distance + b.distance + image.distance) + spherePathChange;
    return { angularSine: Math.abs(image.u), phaseWaves: opdMeters / (lens.wavelengthMicrometers * 1e-6),
        secondaryY: Math.abs(b.y), imageY: image.y };
}

function bessel(order, x) {
    let term = order === 0 ? 1 : x / 2;
    let sum = term;
    for (let k = 1; k <= 40; k++) {
        term *= -x * x / (4 * k * (k + order));
        sum += term;
        if (Math.abs(term) < 1e-19) break;
    }
    return sum;
}

function makeRadialReference(lens, epsilon, edgeSine, intervals, useActualSine) {
    const step = (1 - epsilon) / intervals;
    const normalization = 2 / (1 - epsilon * epsilon);
    const samples = Array.from({ length: intervals + 1 }, (_, i) => {
        const radius = epsilon + i * step;
        const data = trace(lens, radius);
        const phase = -2 * Math.PI * data.phaseWaves;
        const weight = normalization * radius * step / 3 *
            (i === 0 || i === intervals ? 1 : i % 2 === 0 ? 2 : 4);
        return { sine: useActualSine ? data.angularSine : radius * edgeSine,
            real: weight * Math.cos(phase), imaginary: weight * Math.sin(phase), ...data };
    });
    const integrate = r => {
        let real = 0, imaginary = 0;
        const frequency = 2 * Math.PI * r / lens.wavelengthMicrometers;
        for (const sample of samples) {
            const kernel = bessel(0, frequency * sample.sine);
            real += sample.real * kernel;
            imaginary += sample.imaginary * kernel;
        }
        return real * real + imaginary * imaginary;
    };
    return { samples, integrate };
}

function metric(values, reference, tolerance) {
    const errors = values.map((v, i) => Math.abs(v - reference[i]));
    const scale = Math.max(Math.max(...reference.map(Math.abs)), tolerance.absolute);
    const nrmse = Math.sqrt(errors.reduce((sum, v) => sum + v * v, 0) / errors.length) / scale;
    const pointwise = errors.every((e, i) => e <= tolerance.absolute + tolerance.relative * Math.abs(reference[i]));
    const coverage = errors.filter(Number.isFinite).length / errors.length;
    const conclusion = coverage < tolerance.minimumCoverage ? 'Incomparable'
        : pointwise || nrmse <= tolerance.nrmse ? 'Pass' : nrmse <= tolerance.closeNrmse ? 'Close' : 'Difference';
    return { count: errors.length, coverage, maximumAbsolute: Math.max(...errors), nrmse, conclusion };
}

const controls = [
    [653, 650, 'Image Simulation/Example 4, a diffraction limited system.ZMX', 'example4'],
    [2335, 2332, 'Telescopes/Hubble.zmx', 'hubble']
];

const studies = controls.map(([index, nativeIndex, lensName, short]) => {
    const path = source(lensName);
    const lens = readPrescription(path);
    const receipt = nativeIdentity.nativeGroups.find(item => item.index === nativeIndex);
    const nativePath = join(work, 'native-' + nativeIndex + '.json');
    const sourceSha256 = sha(path);
    const nativeSha256 = sha(nativePath);
    if (sourceSha256 !== receipt.sourceSha256 || nativeSha256 !== receipt.nativePointReferenceSha256)
        throw new Error('Prescription or archived native explicit-ray identity changed.');
    const native = parse(nativePath);
    const epsilon = lens.inner / lens.outer;
    const nativeNodes = native.surfaces.at(-1).rays.map((ray, i) => {
        const input = native.inputs[i];
        if (input.hx !== 0 || input.hy !== 0) throw new Error('Native on-axis field changed.');
        const pupilRadius = Math.hypot(input.px, input.py);
        const valid = ray.error === 0 && ray.vignette === 0 && ray.intensity > 0;
        if (valid !== (pupilRadius >= epsilon && pupilRadius <= 1) || (valid && ray.intensity !== 1))
            throw new Error('Native pupil mask or uniform amplitude differs from the original annulus.');
        return { pupilRadius, valid, sine: valid ? Math.hypot(ray.l, ray.m) : null, phase: ray.opdWaves };
    }).filter(ray => ray.valid);
    const edgeSine = nativeNodes.find(n => n.pupilRadius === 1).sine;
    const directionResidual = Math.max(...nativeNodes.map(n => Math.abs(trace(lens, n.pupilRadius).angularSine - n.sine)));
    const opdResidual = Math.max(...nativeNodes.map(n => Math.abs(trace(lens, n.pupilRadius).phaseWaves - n.phase)));
    if (directionResidual > 1e-10 || opdResidual > 1e-6)
        throw new Error('Independent native phase/direction check failed.');

    const low = makeRadialReference(lens, epsilon, edgeSine, 128, false);
    const high = makeRadialReference(lens, epsilon, edgeSine, 256, false);
    const physical = makeRadialReference(lens, epsilon, edgeSine, 256, true);
    const analytic = radius => {
        const z = 2 * Math.PI * radius * edgeSine / lens.wavelengthMicrometers;
        if (z === 0) return 1;
        const amplitude = 2 * (bessel(1, z) - epsilon * bessel(1, epsilon * z)) / ((1 - epsilon ** 2) * z);
        return amplitude * amplitude;
    };
    const maxSecondaryRadius = Math.max(...high.samples.map(s => s.secondaryY));
    const maxImageRadius = Math.max(...high.samples.map(s => Math.abs(s.imageY)));
    if (maxSecondaryRadius >= lens.secondaryRadius + 1e-10 || maxImageRadius >= lens.detectorRadius)
        throw new Error('Radial quadrature hits an additional physical aperture.');
    const phaseSamples = high.samples.map(s => s.phaseWaves);
    const phasePeakToValley = Math.max(...phaseSamples) - Math.min(...phaseSamples);
    const frozen = before.find(item => item.index === index);
    const gate = frozen.recordedSettings.quantities.Irradiance;
    const originalPath = join(work, 'original-' + short + '-request.json');
    const originalRequest = parse(originalPath).request;
    const physicalSettings = request => {
        const { pupilSampling, gridSize, workbenchSettings, ...top } = request;
        const { Sampling, FourierSamplingMode, FourierMaximumSampling, FourierRelativeAccuracy, ...workbench } = workbenchSettings;
        return JSON.stringify({ top, workbench });
    };
    const numbers = short === 'hubble' ? [128, 256, 512, 1024, 2048] : [512, 1024, 2048];
    function readOutput(folder, n, requestedSampling = n) {
        const path = join(folder, 'data.json');
        const data = parse(path);
        const job = parse(join(folder, 'request.json'));
        const provenance = parse(join(folder, 'provenance.json'));
        const series = data.series ?? data.seriesList?.[0];
        if (physicalSettings(job.request) !== physicalSettings(originalRequest) ||
            job.request.pupilSampling !== requestedSampling || data.values.PupilSampling !== n ||
            data.outcome !== 'Success' || data.values.ImageDeltaMicrometers !== .25 ||
            data.values.DisplaySize !== 128 || series?.points.length !== 16384 ||
            series.xUnit !== 'Micrometer' || series.yUnit !== 'Micrometer' ||
            series.valueQuantity !== 'Irradiance' || series.valueUnit !== 'Dimensionless' ||
            job.request.configuration !== 1 || job.request.field !== 1 || job.request.wavelength !== 1 ||
            job.request.workbenchSettings.Normalized !== 'False' || job.request.workbenchSettings.UsePolarization !== 'False')
            throw new Error('Workbench original focal analysis contract changed at tier ' + n);
        const points = series.points.map((p, i) => {
            if (p.x !== (i % 128 - 63) * .25 || p.y !== (Math.floor(i / 128) - 63) * .25 || !Number.isFinite(p.value))
                throw new Error('Grid no longer matches the original physical coordinates.');
            return p;
        });
        return { n, points, snapshotSha256: provenance.snapshotSha256, codeFingerprint: provenance.codeFingerprint, requestFingerprint: provenance.requestFingerprint,
            dataSha256: sha(path) };
    }
    const outputs = numbers.map(n => readOutput(join(work, short + '-' + n), n));
    const radii = outputs[0].points.map(p => Math.hypot(p.x, p.y));
    const uniqueRadii = [...new Set(radii)];
    const projected = evaluator => {
        const cache = new Map(uniqueRadii.map(r => [r, evaluator(r)]));
        return radii.map(r => cache.get(r));
    };
    const annulus = projected(analytic);
    const lower = projected(low.integrate);
    const higher = projected(high.integrate);
    const actual = projected(physical.integrate);
    const comparisons = outputs.map(({ points, ...item }, i) => ({
        ...item,
        versusAnalytic: metric(points.map(p => p.value), annulus, gate),
        versusNativeTiedConicReference: metric(points.map(p => p.value), higher, gate),
        ...(i > 0 ? { versusPrior: metric(points.map(p => p.value), outputs[i - 1].points.map(p => p.value), gate) } : {})
    }));
    const adaptive = !verifyAdaptive ? undefined : [1024, 2048].map(max => {
        const folder = join(work, 'retry-' + short + '-Adaptive-' + max);
        const { values } = parse(join(folder, 'data.json'));
        const report = values.FourierSamplingValidation;
        const first = report.steps[0];
        const [prior, final] = report.steps.slice(-2);
        if (report.mode !== 'Adaptive' || !report.validated || report.maximumPupilSampling !== max ||
            report.relativeRmsAccuracy !== .001 || first.pupilSampling !== 64 || first.phaseResolved ||
            !first.unavailableReason || report.requestedRelativeRmsDeviation !== null ||
            report.requestedMaximumAbsoluteDeviation !== null || !prior.phaseResolved || !final.phaseResolved ||
            final.relativeRmsChange > report.relativeRmsAccuracy || final.pupilSampling !== report.returnedPupilSampling)
            throw new Error('Adaptive result lacks the required disconnected-pupil or convergence evidence.');
        const { points, ...item } = readOutput(folder, report.returnedPupilSampling, 64);
        return { ...item, report, versusNativeTiedConicReference: metric(points.map(p => p.value), higher, gate),
            versusFixed2048: metric(points.map(p => p.value), outputs.at(-1).points.map(p => p.value), gate) };
    });

    return { index, nativeIndex, lensName, sourceSha256, nativeSha256, originalRequestSha256: sha(originalPath),
        lens, epsilon, nativeValidNodes: nativeNodes.length,
        nativeMaskNodes: native.surfaces.at(-1).rays.length, nativeWorkingFNumber: .5 / edgeSine, nativeAngularSine: edgeSine,
        maximumNativeDirectionResidual: directionResidual, maximumNativeOpdResidualWaves: opdResidual,
        maximumSecondaryRayHeightMeters: maxSecondaryRadius, maximumImageRayHeightMeters: maxImageRadius,
        independentQuadraturePhasePeakToValleyWaves: phasePeakToValley, physicalGrid: { rows: 128, columns: 128, pixelPitchMicrometers: .25 },
        originalGate: gate, independentQuadratureConvergence: metric(lower, higher, gate), annulusVersusTraced: metric(annulus, higher, gate),
        realAngleMappingControl: metric(actual, higher, gate), workbench: comparisons, adaptive };
});

const findings = verifyAdaptive ? studies.map(({ index, originalRequestSha256, adaptive }) => ({
    index, originalRequestSha256, adaptive
})) : studies;
writeFileSync(join(work, verifyAdaptive ? 'annulus-adaptive-verification.json' : 'annulus-verification.json'),
    JSON.stringify({ nativeArchiveSha256: sha(nativeArchive), studies: findings }, null, 2) + '\n');
for (const row of studies) {
    console.log(JSON.stringify({ index: row.index, phasePv: row.independentQuadraturePhasePeakToValleyWaves,
        nativeDirection: row.maximumNativeDirectionResidual, nativeOpd: row.maximumNativeOpdResidualWaves,
        conicConvergence: row.independentQuadratureConvergence, angleControl: row.realAngleMappingControl,
        annulusControl: row.annulusVersusTraced,
        workbench: row.workbench.map(item => [item.n, item.versusNativeTiedConicReference.nrmse, item.versusPrior?.nrmse]) }));
}
