// Build-time only: pinned MathJax produces self-contained glyph outlines.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { mathjax } = require('mathjax-full/js/mathjax.js');
const { TeX } = require('mathjax-full/js/input/tex.js');
const { SVG } = require('mathjax-full/js/output/svg.js');
const { liteAdaptor } = require('mathjax-full/js/adaptors/liteAdaptor.js');
const { RegisterHTMLHandler } = require('mathjax-full/js/handlers/html.js');
require('mathjax-full/js/input/tex/ams/AmsConfiguration.js');
const root = path.resolve(__dirname, '../..');
const directory = path.join(root, 'src/OpticalSystemDesign.App/Assets/UserGuideMath');
const formulas = JSON.parse(fs.readFileSync(path.join(directory, 'formulas.json'), 'utf8'));
const adaptor = liteAdaptor(); RegisterHTMLHandler(adaptor);
const document = mathjax.document('', { InputJax: new TeX({ packages: ['base', 'ams'] }), OutputJax: new SVG({ fontCache: 'none' }) });
const seen = new Set();
const rows = formulas.map(formula => {
  if (!/^[a-z0-9-]+$/.test(formula.id) || seen.has(formula.id)) throw new Error('Invalid/duplicate formula ID');
  seen.add(formula.id);
  const node = document.convert(formula.latex, { display: true });
  const svg = adaptor.outerHTML(adaptor.firstChild(node));
  if (/data-mjx-error|data-mml-node="merror"|<text\b|<image\b|<use\b/.test(svg)) throw new Error(`Non-outlined or invalid formula: ${formula.id}`);
  fs.writeFileSync(path.join(directory, formula.id + '.svg'), svg + '\n');
  return { id: formula.id, sha256: crypto.createHash('sha256').update(svg + '\n').digest('hex') };
});
fs.writeFileSync(path.join(directory, 'manifest.json'), JSON.stringify({ renderer: 'MathJax 3.2.2, TeX + AMS, SVG fontCache=none', formulas: rows }, null, 2) + '\n');
console.log(`Typeset ${rows.length} outlined vector formula blocks.`);
const titles = {
  'dispersion':'材料色散与单位换算', 'optical-path':'光线与光程', 'snell-reflection':'折射与反射', 'paraxial':'高度与斜率的近轴矩阵',
  'spot-rms':'质心与点列 RMS', 'pupil-phase':'本项目 FFT 的 OPD 相位约定', 'psf':'FFT、Huygens 与多色 PSF', 'mtf':'OTF 与 MTF',
  'encircled-energy':'衍射圈入能量', 'film-energy':'反射、透射与吸收功率', 'merit':'优化目标残差', 'yield':'Monte Carlo 估计良率',
  'power-accounting':'非序列功率账目', 'detector-pixel':'探测器像素功率与辐照度', 'camera-electrons':'相机光电子数',
  'conic-sag':'圆锥与偶次非球面矢高', 'grin':'GRIN 几何光学路径', 'pupil-quadrature':'光瞳加权积分', 'distortion':'相对畸变',
  'zernike-fit':'Zernike 未加权 QR 拟合', 'interferogram':'干涉条纹相位与强度', 'foucault':'焦面刀口与瞳再成像', 'angular-spectrum':'标量角谱传播',
  'relative-illumination':'扫描最大值归一化的相对照度', 'radiometry':'辐照度与辐射强度', 'line-edge':'线扩散与边缘扩散',
  'extended-imaging':'非相干与相干扩展像', 'mutual-intensity':'互强度及当前均匀相干模型', 'scan-grid':'RMS 视场扫描节点',
  'dls-step':'阻尼线性化步进', 'finite-difference':'中心差分', 'thin-film-matrix':'薄膜相位与稳定反射递推', 'seidel-wavefront':'三阶波像差基底示意',
  'assembly-conjugate':'装调导数外推与共轭位置', 'adam':'Adam 动量、偏差修正与更新', 'ray-otf':'几何与采样 OTF',
  'yybar':'高度视场的主光线归一化', 'contrast-loss':'Moore–Elliott 局部损失', 'jones-matrix':'Jones 瞳矩阵'
};
let markdown = '# 计算公式附录\n\n本文件由指南公式定义生成，适用条件、变量与单位见[计算指南](COMPUTATION_GUIDE.md)及软件内对应章节。公式为数学排版；软件使用预先生成的矢量轮廓，PDF 复用同一资源。\n';
for (const formula of formulas) {
  if (!titles[formula.id]) throw new Error('Missing formula title: ' + formula.id);
  markdown += `\n## ${titles[formula.id]}\n\n$$\n${formula.latex}\n$$\n`;
}
fs.writeFileSync(path.join(root, 'docs/COMPUTATION_FORMULAS.md'), markdown);
