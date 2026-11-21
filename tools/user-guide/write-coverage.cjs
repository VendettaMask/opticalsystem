const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const files = ['UserGuidePanel.Computation.cs', 'UserGuidePanel.Computation.Details.cs'];
const chapters = new Map();
for (const file of files) {
  const source = fs.readFileSync(path.join(root, 'src/OpticalSystemDesign.App/Panels', file), 'utf8');
  for (const match of source.matchAll(/yield return Calculation\("([^"]+)", "([^"]+)"/g)) chapters.set('calculation-' + match[1], match[2]);
}
const catalog = fs.readFileSync(path.join(root, 'src/OpticalSystemDesign.Application/Services/WorkbenchAnalysisCatalog.cs'), 'utf8').split('AliasesByName')[0];
const names = new Map([...catalog.matchAll(/\["([^"]+)"\] = "((?:\\.|[^"\\])*)"/g)].map(m => [m[1], JSON.parse('"' + m[2] + '"')]));
const mappingSource = fs.readFileSync(path.join(root, 'src/OpticalSystemDesign.App/Panels/UserGuidePanel.CalculationCoverage.cs'), 'utf8');
const mapping = mappingSource.split('key switch')[1]?.split('_ => null')[0];
if (!mapping) throw new Error('Calculation chapter switch was not found.');
const coverage = new Map();
for (const arm of mapping.matchAll(/((?:"[^"]+"\s*(?:or\s*)?)+)=>\s*"([^"]+)"/g)) {
  for (const key of arm[1].matchAll(/"([^"]+)"/g)) {
    if (coverage.has(key[1])) throw new Error('Duplicate analysis mapping: ' + key[1]);
    coverage.set(key[1], 'calculation-' + arm[2]);
  }
}
for (const [key, chapter] of coverage) {
  if (!names.has(key) || !chapters.has(chapter)) throw new Error('Invalid analysis mapping: ' + key);
}
for (const key of names.keys()) {
  if (!coverage.has(key)) throw new Error('Missing calculation chapter: ' + key);
}
// An explicitly supplied fresh runtime audit can cross-check the source map.
// No historical audit is a generation dependency.
if (process.argv[2]) {
  const audit = JSON.parse(fs.readFileSync(path.resolve(process.argv[2]), 'utf8').replace(/^\uFEFF/, ''));
  if (audit.analysisCount !== names.size || Object.keys(audit.coverage).length !== names.size)
    throw new Error('Runtime audit analysis count differs from the current catalog.');
  for (const [key, chapter] of coverage) {
    if (audit.coverage[key] !== chapter) throw new Error('Runtime audit differs: ' + key);
  }
}
let text = '# 分析入口与计算章节对应\n\n本表由当前分析目录和应用内章节映射生成，共 ' + names.size + ' 项；运行时定向检查另行核对全部描述符。它验证指南覆盖，不验证数值精度。公式见[数学附录](COMPUTATION_FORMULAS.md)，定义见[计算指南](COMPUTATION_GUIDE.md)。生成与复核方法见[文档维护](DOCUMENTATION_MAINTENANCE.md#三语应用内帮助与-pdf-同源维护)。\n\n| 分析入口 | 计算章节 |\n| --- | --- |\n';
for (const [key] of names) {
  const chapter = coverage.get(key);
  text += `| ${names.get(key)} (${key}) | ${chapters.get(chapter)} |\n`;
}
fs.writeFileSync(path.join(root, 'docs/COMPUTATION_COVERAGE.md'), text);
console.log(`Documented ${names.size} analysis entries from current sources.`);
