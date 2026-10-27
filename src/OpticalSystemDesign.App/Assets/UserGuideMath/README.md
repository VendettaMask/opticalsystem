# 指南数学排版资源

`formulas.json` 是 39 组公式的唯一 LaTeX 定义。开发时使用固定版本 MathJax 3.2.2 的 TeX/AMS 编排为自包含 SVG：只有几何路径、矩形和变换，无 PNG、外部字体引用、脚本或远程资源。

产品用 C# 读取受限的自有 SVG 格式，界面按主题绘制矢量路径，PDF 通过 Skia 原生 PDF 使用同一组轮廓。排版工具不参与运行时或光学求解。数字计算仍由共享 Core 完成。

维护时在仓库根目录执行：

```powershell
pnpm --dir tools/user-guide install --frozen-lockfile --ignore-scripts
node tools/user-guide/build-formulas.cjs
```

脚本同时生成 SVG、哈希清单和 `docs/COMPUTATION_FORMULAS.md`。新增公式必须被指南章节引用，定向测试检查定义与引用一致、所有路径可绘制及 PDF 最低可读宽度。不要手改生成的轮廓或复制另一套公式。

排版来源：[MathJax SVG](https://docs.mathjax.org/en/v3.2/options/output/svg.html)。MathJax 的 Apache 2.0 许可见同目录 `MATHJAX-LICENSE.txt`，随产品分发。物理约定、变量和单位见计算指南与源码，不以排版工具作为数值依据。
