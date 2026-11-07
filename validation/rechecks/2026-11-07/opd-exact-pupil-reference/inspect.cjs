const fs = require('node:fs');
const base = process.argv[2];
const read = (path) => JSON.parse(fs.readFileSync(`${base}/${path}`, 'utf8').replace(/^\uFEFF/, ''));
const max = values => Math.max(0, ...values.map(Math.abs));
const difference = (a, b, field) => a.map((x, i) => x[field] - b[i][field]);
const same = (a, b, field) => difference(a, b, field).every(v => v === 0);
const load = id => {
    const batch = read(`${id}/point-reference.json`).surfaces.at(-1).rays;
    const operand = read(`${id}/opdc.json`).rows;
    const geometry = read(`${id}/image-geometry.json`);
    return {batch, operand, geometry};
};
const ids = fs.readdirSync(base, {withFileTypes: true})
    .filter(dir => dir.isDirectory() && /^\d+$/.test(dir.name)).map(dir => dir.name).sort((a, b) => Number(a) - Number(b));
const reports = [];
for (const id of ids) {
    const {batch, operand, geometry} = load(id);
    const valid = row => row.error === 0 && row.vignette === 0 && row.intensity > 0;
    const report = {id, errorOrVignetted: batch.filter(x => !valid(x)).length, maxBatchOperand: max(difference(batch, operand, 'opdWaves').filter((x, i) => valid(batch[i])))};
    if (fs.existsSync(`${base}/${id}p/opdc.json`)) {
        const c = load(`${id}p`);
        report.control = {
            maskSame: geometry.every((ray, i) => valid(ray) === valid(c.geometry[i])),
            maxPosition: max(['x','y','z'].flatMap(k => difference(geometry, c.geometry, k))),
            maxDirection: max(['l','m','n'].flatMap(k => difference(geometry, c.geometry, k))),
            maxOpl: max(difference(geometry, c.geometry, 'opdWaves')),
            maxOpd: max(difference(batch, c.batch, 'opdWaves').filter((x, i) => valid(batch[i]))),
            maxBatchOperand: max(difference(c.batch, c.operand, 'opdWaves').filter((x, i) => valid(c.batch[i]))),
        };
        const numbers = ['x','y','z','l','m','n','opdWaves'];
        report.control.nonzero = numbers.filter(k => !same(geometry, c.geometry, k));
    }
    reports.push(report);
}
console.log(JSON.stringify(reports, null, 2));
