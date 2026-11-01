# NSC → 统一光谱探测器 → RAW / ISP 验证

最终 **46 个不同用例的最新结果全部通过**，无失败或跳过。`final-focused.trx` 为 46/46；最终小幅入口保护和证据输出修改后，`postbuild-focused.trx` 再验其中 16/16，不累计成 62 个用例。逐用例去重账目见 [test-summary.json](test-summary.json)。默认 Release App/测试项目构建 0 警告/错误，见 [build-final.log](build-final.log)。

覆盖两项相机修复、NSC 功率/单位/方向/CFA/曝光、主路径/鬼像分割、空数据与错误/取消/过期、A/B 保存及源数据库删除后的复算、中英日明暗离屏窗口、V5–V8 和旧采样报告、原 NSC 数据库会话/筛选行为。没有全仓、跨平台原生操作或商业软件数值认证。实际产物/相关源码指纹见 [file-hashes.json](file-hashes.json)。

## 可以直接检查的结果

- [完整实验](examples/nsc-main-ghost.camera-experiment.zip)：在相机窗口“打开实验”，A 为玻璃板主路径，B 为全部保存路径。保留独立功率、滤波表达式、QE/传感器和结果，无需原数据库。
- [PNG/物理 CSV 对照包](examples/nsc-comparison.zip)：含 `nsc-detector.json`、顶部起始的 `detector-power.csv`、RAW/电子数及全部预览。
- [数值摘要](examples/verification.json)：总功率 `2.752759764580793e-13 W`，主路径 `2.747674759713109e-13 W`，鬼像 `5.085004867684214e-16 W`；逐像元光电子分割误差最大 `1.0302869668521453e-13 e⁻`。这是独立人工玻璃板控制，非实测或商业认证。
- 离屏预览：[中文浅色](previews/nsc-camera-Light-zh-CN.png)、[英文深色](previews/nsc-camera-Dark-en-US.png)、[日文浅色](previews/nsc-camera-Light-ja-JP.png)。新输入卡和输出图已目检；没有控制用户桌面。

## 重放

在仓库根目录后台构建，定向运行新增修复/桥接测试；设置证据目录会生成上述样本与预览：

```powershell
dotnet build tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:1 -nr:false
$env:OPTICAL_CAMERA_CAPTURE_DIR = (Join-Path (Get-Location) 'validation/rechecks/2026-10-31/nsc-camera-chain/previews')
$env:OPTICAL_CAMERA_NSC_EVIDENCE_DIR = (Join-Path (Get-Location) 'validation/rechecks/2026-10-31/nsc-camera-chain/examples')
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~CameraNonSequential|FullyQualifiedName~CameraBridgeRegressionTests'
```

较宽 46 项的具体用例名见去重账目，包括原相机实验、传感器桥接、3D 窗口和 NSC 会话行为。

## 失败和重试

`fixes.trx` 是宿主连接超时的中止运行，不计通过；本机测试通信可用后 `fixes-retry.trx` 的 6 项通过。较宽首轮发现会话时间/数据库时间定义不同后被主动中止，未留下完整 TRX，不计通过。`service.trx` 为 3/4，唯一失败是失效测试缩短波长表但仍保留 W2 源的夹具错误；`window-first.trx` 为 0/3，暴露延迟文本事件取消下一次读取的问题。两者修正后 `nsc-retry.trx` 为 7/7，随后最终 46/46 和 16/16 完成。旧记录保留，不将其与最终结果重复相加。

实现、支持域和后续缺口见[专项记录](../../../../docs/CAMERA_NSC_CHAIN_2026-10-31.md)，操作说明见[相机指南](../../../../docs/CAMERA_IMAGING_LAB.md#非序列分析到-raw--isp)。
