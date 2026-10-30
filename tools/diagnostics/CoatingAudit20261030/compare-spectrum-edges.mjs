// Offline primary-source comparison only. Product code never calls Node or this package.
import { readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { resolve } from 'node:path';
const { tmm } = await import(pathToFileURL(resolve(process.argv[2], 'src/tmm.js')));
const cases = JSON.parse(readFileSync(process.argv[3], 'utf8').replace(/^\uFEFF/, ''));
const results = cases.map(c => {
  const run = pol => tmm(c.Lambda, c.Angle, pol, c.N0, c.Ns, c.Layers.map(l => ({ n: l.N, d: l.D })));
  const expected = c.Pol === 'Unpolarized' ? (() => { const s=run('s'),p=run('p'); return {R:(s.R+p.R)/2,T:(s.T+p.T)/2,A:(s.A+p.A)/2}; })() : run(c.Pol.toLowerCase());
  return { name:c.Name, wavelength:c.Lambda, pol:c.Pol, expected, core:c.Core,
    errors:{R:Math.abs(c.Core.Reflectance-expected.R),T:Math.abs(c.Core.Transmittance-expected.T),A:Math.abs(c.Core.Absorptance-expected.A)} };
});
const maximumErrors = Object.fromEntries(['R','T','A'].map(k => [k,Math.max(...results.map(r=>r.errors[k]))]));
writeFileSync(process.argv[4], JSON.stringify({source:'Unmodified pinned tmmcore 0.4.2 JavaScript',cases:cases.length,maximumErrors,results},null,2)+'\n');
console.log(JSON.stringify({cases:cases.length,maximumErrors}));
if(Object.values(maximumErrors).some(v=>!Number.isFinite(v)||v>2e-11)) process.exitCode=1;
