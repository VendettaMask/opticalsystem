// Build-time data conversion only. Runtime interpolation belongs to the shared C# Core.
import { readFileSync, writeFileSync } from 'node:fs';
const records = ['SiO2', 'Ta2O5'].map(formula => {
  const text = readFileSync(new URL(`../../third_party/coating-reference/${formula}-Rodriguez-de-Marcos.yml`, import.meta.url), 'utf8');
  const rows = text.split(/\r?\n/).map(line => line.trim().split(/\s+/)).filter(row => row.length === 3 && row.every(x => Number.isFinite(Number(x)))).map(row => row.map(Number));
  if (rows.length < 100) throw new Error('Unexpected material table');
  return {
    Name: `${formula} 薄膜（Rodríguez-de Marcos 2016）`,
    Source: 'CC0; refractiveindex.info c5c2f188e848453def5970e347399d653df2ffc2; DOI 10.1364/OME.6.003622; reactive electron-beam evaporation 300 °C',
    WavelengthNm: rows.map(row => row[0] * 1000), N: rows.map(row => row[1]), K: rows.map(row => row[2])
  };
});
writeFileSync(new URL('../../labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.Engine/Data/films.json', import.meta.url), JSON.stringify(records, null, 2) + '\n');
