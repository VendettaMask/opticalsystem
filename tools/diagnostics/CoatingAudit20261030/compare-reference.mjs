// Offline validation only: the unmodified pinned tmmcore JavaScript, never product computation.
import { tmm } from '../../../.tmp/coating-review-20261030/package/src/index.js';
import { readFileSync, writeFileSync } from 'node:fs';
const cases = JSON.parse(readFileSync(process.argv[2], 'utf8').replace(/^\uFEFF/, ''));
const results = cases.map(c => {
  const run = pol => tmm(c.Lambda, c.Angle, pol, c.N0, c.Ns, c.Layers.map(l => ({ n: l.N, d: l.D })));
  const expected = c.Pol === 'Unpolarized' ? (() => { const s=run('s'),p=run('p'); return {R:(s.R+p.R)/2,T:(s.T+p.T)/2,A:(s.A+p.A)/2}; })() : run(c.Pol.toLowerCase());
  return { name:c.Name, wavelength:c.Lambda, pol:c.Pol, expected, core:c.Core,
    errors: { R:Math.abs(c.Core.Reflectance-expected.R), T:Math.abs(c.Core.Transmittance-expected.T), A:Math.abs(c.Core.Absorptance-expected.A) } };
});
const maximum = key => Math.max(...results.map(r=>r.errors[key]));
const summary={source:'tmmcore 0.4.2 pinned JavaScript',cases:results.length,maximumErrors:{R:maximum('R'),T:maximum('T'),A:maximum('A')},results};
writeFileSync(process.argv[3], JSON.stringify(summary,null,2)+'\n');
console.log(JSON.stringify({cases:summary.cases,maximumErrors:summary.maximumErrors}));
if (Object.values(summary.maximumErrors).some(e=>!Number.isFinite(e)||e>2e-11)) process.exitCode=1;
