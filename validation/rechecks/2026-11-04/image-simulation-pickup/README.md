# 图像模拟参数拾取修复证据

结果见[专题](../../../../docs/IMAGE_SIMULATION_PICKUP_REPAIR_2026-11-04.md)。原60项为52 Pass/4 Close/2 Difference/2 Incomparable/0 Error，新增52 Pass；原处方、采样、参考、容差及FFT保护保持。

- [summary.json](summary.json)、[request-results.csv](request-results.csv)：完整A/B及各请求剩余差异。
- [execution-identity.json](execution-identity.json)：120次实际执行的请求指纹和模型哈希。早期工具的request.json复制了原归档Job路径，原样保留；各目录新增execution-job.json记录经provenance核实的实际执行路径，数值请求未修改。
- [import-audit.json](import-audit.json)：95份源文件哈希保持，114配置中112结构相同、仅两份准直面光焦度纠正，原一份导入限制保持。
- [phase-diagnostics.json](phase-diagnostics.json)：原网格的PSF/MTF/截面相位步进，修复后均低于原0.5波界限。
- [reference-integrity.json](reference-integrity.json)：60份原生目录共600个载荷、原归档及比较配置的完整性。
- [pickup-regression-final.trx](pickup-regression-final.trx)、[pickup-roundtrip.trx](pickup-roundtrip.trx)：主集合156项通过；后续11项保存复算子集覆盖相同身份，不累加。另[相机双高斯8项](double-gauss-protection.trx)通过，总计164个不同用例。[首次夹具失败](pickup-regression.trx)保留。
- [archive-index.json](archive-index.json)、[archive-verification.json](archive-verification.json)、[evidence.zip](evidence.zip)：2070份逻辑文件，内容寻址去重并复用旧权威载荷；全部完整读回后才可清理展开副本。

归档SHA256为 `9126c24e94ff98e3f90a353245a6781947b140dc8866d25ce78e029bf5218204`；最终Core为 `1af8b46ae62b581a85b05b3954f0d8dd1a1414e989d1c826feaed272836036a3`。原生能量失败及六项OPD未通过保持，不宣称全目录或新增收敛认证。

[复算工具与模式](../../../../tools/diagnostics/ImageSimulationPropagation20261104/README.md)。仅后台运行，未操作用户软件、推送或发布。
