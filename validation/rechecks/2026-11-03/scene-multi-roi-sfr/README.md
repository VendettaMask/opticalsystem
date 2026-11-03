# 全幅线性图的多 ROI 斜边测量

2026-11-03。范围为计划 M3 的本机测量：读取已生成的 float64 辐照度，管理多个实际图像 ROI，测量近水平/近竖直边，保留独立成功/失败和原图重放。独立工具、同条件正式光学 MTF、采样收敛、原例完整幅面和 ROM 不在本轮通过范围。

## 原始数据与物理控制

既有两个批次直接引用 [M2 权威归档](../../2026-11-02/scene-linear-batch/README.md)，未复制、改写或重新归一化像元。新伴随记录为 `double-gauss-512-lens-wavelengths.scene-sfr-rois.json` 和 `double-gauss-128-article-source-shapes.scene-sfr-rois.json`；完整源摘要、明确 ROI、质量和数值见 [native-roi-summary.json](native-roi-summary.json)。

512² 三单线批次的 12 个诊断组合只有 1 项通过测量质量：d 波段 `V bottom` 的残差 0.24417 px，MTF50 0.08837636 cycles/pixel。128² 有限源谱的 6 个组合只有第一源谱的近竖直 ROI 通过，残差 0.074076 px，MTF50 0.1053053 cycles/pixel。其余保留角度、残差、窗口/分箱或无有效边缘的失败，不填充曲线。这些粗采样 M2 数据原本就没有测量精度或收敛承诺，不能将本轮失败抹掉或推广成镜头算法故障。

新增权威归档 [double-gauss-cross-128.scene-linear.zip](double-gauss-cross-128.scene-linear.zip) 使用当前真实 Double Gauss 处方和保存像面，不调焦；100 m 处 2×2 发光棋盘、每格 2000 mm、中心 X/Y 为 0、Z 旋转 5°，明确单线 587.6 nm。完整传感器 128×128、3 µm、每像元 2×2、瞳面 8×16，仍按一次总预算完成 8,388,608 条光线、45,088,768 次实际表面传播。每块 17 行，未缩图或重复重置预算。

同一幅原图上的四个 ROI：`V top=(24,0,80,48)`、`H left=(0,24,48,80)`、`V bottom=(24,80,80,48)`、`H right=(80,24,48,80)`，法线半窗口均为 12 px。四项均通过原测量质量门限，残差约 0.033577 px；竖直 MTF50 约 0.06245633、水平约 0.06245636 cycles/pixel。包含交点的 `(16,16,96,96)` ROI 被拒绝，保留失败。完整曲线、ESF/LSF、样本数、实际法线和毫米位置见 [伴随记录](double-gauss-cross-128.scene-sfr-rois.json)。

三个记录均从原归档重新测量并逐值一致，读取期间未重新追迹。新控制的靶标取景、像元和瞳面采样不同于旧 M2 批次，因此上述数值不是一组收敛阶梯，也不是与正式 MTF 的误差验收。

## 软件验证

[multi-roi-final.trx](multi-roi-final.trx) 的 44 项定向检查全部通过。覆盖新增 Core/服务/两主题离屏界面、既有单 ROI、逐源谱、原估计器与相机工作区。随后保存保护的 10 项同身份补测单列于 [multi-roi-save-guard.trx](multi-roi-save-guard.trx)，最后无效格式校验的服务用例见 [multi-roi-format-guard.trx](multi-roi-format-guard.trx)；按用例去重仍为 44，不累加通过数。机器可读汇总见 [verification-summary.json](verification-summary.json)。

正常 Release App 与内置模块最终构建零警告/错误，未换输出目录。App 与最终格式守护测试输出的三个产品程序集因每次构建生成的 AssemblyInfo 不同而字节不同；[编译来源核对](compiled-source-audit.json)验证了两端 DLL/PDB 绑定，并比较全部源码文档，差异仅为这三个生成文件，手写源码一致。

最终[全仓文档严格检查](documentation-audit.json)发现 4 处断链，均位于并行改动的 `scripts/icon-gallery-inputs/gallery.html`，指向该图库自身的总览/清单/说明/许可输入；本轮相关指南、计划和证据没有断链或围栏问题。未修改其他任务的图库输入来取得全仓通过，也不将此检查报告为全部通过。

新增检查包含非方形偏移 ROI、正负角度、亮暗反转、水平转置、两种行序下的法线/毫米坐标、独立 logistic LSF 解析频率响应、失败隔离、交点、无光、重复标识与边界、取消、真实场景三波段权重 2/1/0、伴随记录原图重测、错误源/曲线篡改与保存保护。实际物理成像使用共享 Core；解析边缘仅用于测试。

离屏界面验证了原幅面选框在两种窗口尺寸/留白下的坐标、ROI 增删、任务选择、SFR/ESF/LSF、零权重失败后的曲线清除、工程切换后的冻结来源和重开时清除旧 ROI。[浅色 SFR](light-profile-0.png)、[浅色 ESF](light-profile-1.png)、[浅色 LSF](light-profile-2.png)和对应[深色 SFR](dark-profile-0.png)、[深色 ESF](dark-profile-1.png)、[深色 LSF](dark-profile-2.png)来自真实控件共享导出路径；这是有限共轭解析镜头测试夹具，并非上述 Double Gauss 数值图。未占用用户桌面或重启应用。

首次受限环境运行的测试宿主在 90 秒连接超时，运行中止，保留 `multi-roi-core-service.trx`，不计为通过。后续正常后台测试完成；中间开发测试与最终相同身份，完整覆盖核对后不重复保留。未终止用户进程、使用替代输出或放宽数值门限。

## 重现

后台诊断入口为 [SceneMultiRoi20261103](../../../../tools/diagnostics/SceneMultiRoi20261103/Program.cs)。先正常构建该项目，再用 `dotnet run --project tools/diagnostics/SceneMultiRoi20261103/Probe.csproj -c Release --no-build --no-restore -- <仓库绝对路径> verify` 校验保留的批次和重测全部 ROI；去掉 `verify` 会重新生成声明的新单色控制批次。原始 M2 文件继续沿用既有权威路径。伴随记录依赖相应原批次，不能只携带 JSON 后声称拥有完整可重放图像。
