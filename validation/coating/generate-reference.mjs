// Validation only. Never referenced by a product project.
// Extract third_party/coating-reference/tmmcore-0.4.2.tgz to .tmp/coating-upstream/tmmcore first.
import { tmm } from '../../.tmp/coating-upstream/tmmcore/package/src/index.js';
import { writeFileSync } from 'node:fs';
const cases = [];
for (const angle of [0, 23, 56, 75]) for (const pol of ['s', 'p']) {
  for (const lambda of [420, 550, 810]) for (const family of ['bare', 'ar', 'hr', 'absorbing', 'thick']) {
    let layers = [];
    if (family === 'ar') layers = [{ n: [Math.sqrt(1.52), 0], d: 550 / (4 * Math.sqrt(1.52)) }];
    if (family === 'hr') layers = Array.from({ length: 80 }, (_, i) => ({ n: [i % 2 ? 1.45 : 2.25, 0], d: 550 / (4 * (i % 2 ? 1.45 : 2.25)) }));
    if (family === 'absorbing') layers = [{ n: [1.7, 0.04], d: 130 }, { n: [0.2, 3.1], d: 32 }, { n: [1.45, 0], d: 81 }];
    if (family === 'thick') layers = [{ n: [0.3, 4], d: 4000 }];
    const input = { name: `${family}-${angle}-${pol}-${lambda}`, lambda, angle, pol, n0: [1, 0], ns: [1.52, 0], layers };
    cases.push({ ...input, expected: tmm(lambda, angle, pol, input.n0, input.ns, layers) });
  }
}
writeFileSync(new URL('./tmmcore-0.4.2.json', import.meta.url), JSON.stringify({ source: 'TFStudio 8b942b3f956728a23cea2b35023c8b5d8de17428 / tmmcore 0.4.2 JavaScript', cases }, null, 2) + '\n');
console.log(`Generated ${cases.length} actual tmmcore reference cases.`);
