# Speos 二进制与 Zemax BSDF 验证封存

2026-10-31，默认 Release，共享纯 C# Core，无外部光学引擎或原生桌面操作。支持已核验的 archive-3 Speos .brdf v8 / .anisotropicbsdf v7/v9，以及需要明确测量波长/辐射度声明的 Zemax Monochrome 格式。具体边界见[实施说明](../../../../docs/BINARY_ZEMAX_BSDF_2026-10-31.md)。

## 最终结果

- `build-final-app.log`、`build-final-tests.log`：默认 App（含两个内置模块）及同输出图的测试驱动，均零警告、零错误。
- `final-focused.trx`、`test-final-focused.log`：**191/191**，零失败、零跳过，包含本阶段新增 **35** 项及前四阶段相关回归；没有执行整个主测试套件。重复运行数不相加。
- `before-final-run.json` / `after-final-run.json`：44 个范围内源码/夹具及 9 个二进制身份在末次运行前后无漂移。来自共享脏工作区，BaseCommit 仅供溯源；不将二进制位模式当成数值验收门限。
- `manifest.json`：结果、范围、输入来源和所有封存产物 SHA-256。

## 输入和数值证据

- `installed-speos-inventory.json`、`installed-v8-flags.json`、`inspect-*.ps1`：本机 Ansys 2026 R1 库只读格式检查。未将安装库材料复制进产品；这些检查不构成商业数值认证，部分安装材料明确超过现行波长/节点预算。
- `R_test.anisotropicbsdf`：Ansys PySpeos 固定提交 `bc5f75644f568c6b1c9b9fa1b53d22d18170cf99` 的不变官方 LZ4 二进制夹具。源文件 SHA-256 `224e8a56520ac72f58c2124fbd9a030259e09b00b1951eb52ba3c7e6c8e0b72d`；来源和许可见[夹具 NOTICE](../../../../tests/OpticalSystemDesign.Tests/Fixtures/speos-r-test-v9.NOTICE.txt)。诊断下载的 Gaussian Fresnel 文本样例同属该仓库，仅用于版本结构检查。产品项目不引用这些测试资产。
- `official-speos-v9.json`：展开 65,436 节点，在声明 6° 参考角积分 R=0.6226999999999994，与文件比例 0.6227 一致；没有据此认证其他入射角下的厂商数值结果。
- `binary.brdf`、`zemax-r.BSDF`、`zemax-t.BSDF` 及 `binary-import.json`：自有辐射度合成控制，明确不是实测材料。另有测试内生成的 v7/v9 LZ4 双瓣夹具验证两侧光谱和透射半球。
- `narrow-specular-statistics.json`：seed 8317，8192 次 5° 窄峰采样，沿镜面轴均值 0.9981194315，对照解析值 0.9980985580；朝法线切向均值 −0.0006683556，对照 0.0010991941。此窄支撑控制采用 `6×取值范围/sqrt(N)` 的保守统计界限，并逐样本核对锥角与 PDF 权重；没有将该界限称为精确方差。
- `grazing-statistics-False/True.json`：各 8192 次、seed 9107，85° 掠射 BRDF/BTDF 的实际半球剪裁与投影采样。对照平均 |cosθ|=2/3、X/Y=0，以解析方差的六倍标准差控制，同时逐样本检查 f|cosθ|/pdf。
- `nsc-medium-database-project.json`：4096 条射线，TIS=0.6、余量吸收，实际折射率 1→1.5；能量相对闭合优于 1e−12，并检查独立方形探测器视因子、介质光程、B 筛选、数据库协议及 STAROPT v11 往返。
- `chain-binary.json` / `chain-zemax-r.json` / `chain-zemax-t.json`：三种输入进入同一探测器/传感器；逐像元光子换算最大误差分别 2.84217e−14、5.68434e−14、5.68434e−14 e−。对应实验 zip 保存，删除来源/场景/数据库后光电子、RAW、ISP 均精确重放。
- `editor-False/True.png`、`camera-False/True.png`：最终真实控件的 Avalonia Headless/Skia 离屏帧。已检查 Zemax 波长、辐射度/介质声明、Measured BSDF 摘要及 NSC→RAW/ISP；新回归也验证无效 Zemax 声明文本不会阻止 Speos 导入。

## 中间记录

`development-unit.trx` 保留初次 14 项中的 6 个积分收敛失败；改变径向积分变量后消除了平方根端点奇异性。`development-chain.trx` 的 1 个失败是官方二进制夹具末尾辅助表尚未解析，后续补齐完整布局与活动选项校验。`development-kernel-chain.trx` 的 27/27 是进入最终合集前的开发验证，不单独累计。

`prior-focused.trx` 和 `prior-before/after-run.json` 是修复无关 Zemax 文本校验前的 190 项通过记录；`pre-normal-gap-*` 保留界面修正后、补充法向方位缺口保护前的 190 项记录；最终 `final-focused.trx` 的 191 项还包含法向完整性保护及其负向回归。最终结果以末次 TRX 为准。`run-focused.ps1` 会更新同目录的末次结果/统计/截图，要保留历史时先复制目录；指南更新脚本仅记录本次文档编辑。
