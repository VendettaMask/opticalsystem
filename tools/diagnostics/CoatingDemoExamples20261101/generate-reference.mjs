// Offline reference generation only. Product code never loads Node or upstream JavaScript.
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { runInNewContext } from 'node:vm';
import { createHash } from 'node:crypto';
const root = resolve(process.argv[2] || '.');
const reference = resolve(root, '.tmp/coating-demo-reference-20261101');
const upstream = resolve(reference, 'TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428');
const { MATERIALS } = await import(pathToFileURL(resolve(upstream, 'src/utils/materials/materialDatabase.js')));
const { tmm } = await import(pathToFileURL(resolve(reference, 'package/src/index.js')));
const sourceFiles = ['web/demo-examples.js', 'src/utils/materials/materialDatabase.js', 'src/utils/materials/pchip.js'];
const source = { commit: '8b942b3f956728a23cea2b35023c8b5d8de17428', files: Object.fromEntries(sourceFiles.map(p => [p, createHash('sha256').update(readFileSync(resolve(upstream, p))).digest('hex')])) };
const context = { window: {} }; runInNewContext(readFileSync(resolve(upstream, sourceFiles[0]), 'utf8'), context);
const examples = context.window.DEMO_EXAMPLES;
const used = new Set(examples.flatMap(d => [d.incidentMedium, d.substrate.material, d.exitMedium, ...d.frontLayers.map(l => l.material)]));
const materials = MATERIALS.filter(m => used.has(m.id)).map(m => ({
  id: m.id, name: m.name, description: m.description, color: m.color,
  rangeNm: m.getNK.rangeNm || [200, 20000],
  constantNK: m.getNK.constantNK || null,
  coefficients: m.getNK.dispersionFormula ? [0, ...m.getNK.dispersionFormula.coefficients.slice(1)] : null,
  table: m.getNK.tabData || [], kTable: m.getNK.kTable || [], interpolation: m.getNK.interp || null,
}));
const dataPath = resolve(root, 'labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Data/reference-examples.json');
writeFileSync(dataPath, JSON.stringify({ source, materials, examples }, null, 2) + '\n');
const materialChecks = materials.map(m => {
  const getNK = MATERIALS.find(x => x.id === m.id).getNK;
  const knots = [...new Set([...m.table.map(r => r[0]), ...m.kTable.map(r => r[0])])].sort((a,b) => a-b);
  const wavelengths = [...new Set([400, 400.25, 550, 799.75, 800, ...knots, ...knots.slice(1).map((x,i) => (x + knots[i])/2)])].sort((a,b) => a-b);
  return { id: m.id, values: wavelengths.map(w => [w, ...getNK(w)]) };
});
const cases = examples.map(d => {
  const nk = (id, w) => MATERIALS.find(m => m.id === id).getNK(w);
  return { id: d.id, name: d.name, layers: d.frontLayers, angles: [0,35,57].map(angle => ({ angle, samples: Array.from({length:801}, (_,i) => {
    const w=400+i*.5; const layers=d.frontLayers.map(l => ({n:nk(l.material,w),d:l.thickness}));
    const evaluate = pol => {const v=tmm(w,angle,pol,nk(d.incidentMedium,w),nk(d.substrate.material,w),layers);return [v.R,v.T,v.A];};
    return [w,...evaluate('s'),...evaluate('p')];
  }) })) };
});
const fixture = resolve(root, 'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/tfstudio-demo-examples-8b942b3.json');
writeFileSync(fixture, JSON.stringify({source, materialChecks, cases}) + '\n');
console.log(JSON.stringify({examples:examples.length,materials:materials.length,spectralPolarizationCases:5*3*801*2,materialChecks:materialChecks.reduce((n,m)=>n+m.values.length,0),dataPath,fixture}));
