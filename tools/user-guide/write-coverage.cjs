const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const audit = JSON.parse(fs.readFileSync(path.join(root, 'validation/rechecks/2026-10-27/user-guide/pdf-audit/coverage.json'), 'utf8').replace(/^\uFEFF/, ''));
const files = ['UserGuidePanel.Computation.cs', 'UserGuidePanel.Computation.Details.cs'];
const chapters = new Map();
for (const file of files) {
  const source = fs.readFileSync(path.join(root, 'src/OpticalSystemDesign.App/Panels', file), 'utf8');
  for (const match of source.matchAll(/yield return Calculation\("([^"]+)", "([^"]+)"/g)) chapters.set('calculation-' + match[1], match[2]);
}
const catalog = fs.readFileSync(path.join(root, 'src/OpticalSystemDesign.Application/Services/WorkbenchAnalysisCatalog.cs'), 'utf8').split('AliasesByName')[0];
const names = new Map([...catalog.matchAll(/\["([^"]+)"\] = "((?:\\.|[^"\\])*)"/g)].map(m => [m[1], JSON.parse('"' + m[2] + '"')]));
let text = '# 分析入口与计算章节对应\n\n本表来自当前实际分析目录和定向测试的对应关系，共 ' + audit.analysisCount + ' 项。它验证指南覆盖，不验证数值精度。公式见[数学附录](COMPUTATION_FORMULAS.md)，定义见[计算指南](COMPUTATION_GUIDE.md)。\n\n| 分析入口 | 计算章节 |\n| --- | --- |\n';
for (const [key, chapter] of Object.entries(audit.coverage)) {
  if (!chapters.has(chapter) || !names.has(key)) throw new Error('Incomplete coverage: ' + key);
  text += `| ${names.get(key)} (${key}) | ${chapters.get(chapter)} |\n`;
}
fs.writeFileSync(path.join(root, 'docs/COMPUTATION_COVERAGE.md'), text);
console.log(`Documented ${audit.analysisCount} analysis entries.`);
