# 离焦、照度窗口及报告目录修复 — 2026-10-30

## 实现范围

- 离焦源图的图例侧栏使用测量宽度，保留最大宽度限制和真实超框拒绝。未裁切、缩字或改变默认 5 列 × 3 视场。
- 非相干照度新增显式 `useSurfaceSamplingWindow`，Core 默认为 false；桌面参数 `UseSurfaceSamplingWindow` 默认 true。仅无物理孔径时允许以实际表面半口径作为方形分箱窗口，已有孔径优先，未知孔径仍拒绝。光线追迹、物理遮挡、入射功率和上游损耗定义不变。
- PDF 目录按实际分页填页码和原生跳转目标，支持长目录、多个配置、跨页表、附录。预排只为获取页码，不重算光学，不重新排版源图。
- Word/PDF/HTML 表头 #EAF1EF、正文 #2D3431、隔行 #F7FAF8、边框 #D9E3DE，HTML 打印同步。Markdown 颜色由阅读器决定。

## 自动验证

`focused-final.trx` 79 项通过；`final-source.trx` 10 项通过，其中 9 项为重复 PDF 回归、1 项为新增未知孔径保护，共 **80 个不同用例**。覆盖固定源图几何/字号/窗口稳定性、真实超框拒绝、图例过滤和长标签、目录、报告格式、严格 Core 历史契约、独立单位功率积分、探测器入射功率、上游损耗与实际遮挡。

`default-app-build.log` 为最终默认 App Release 构建，零警告/错误。`format-final.log` 为指定变更文件的文件夹模式空白检查，退出 0。

保留不完整重试：`focused.trx` 为受限环境测试进程连接超时，未执行用例；`final-build-and-test.log` 为默认测试输出被其他后台任务 testhost 44884 占用的复制失败，未中断该任务，最终 10 项改用 `tmp/report-directory-final-tests/`；`format.log` 为受限环境构建宿主启动失败，后以文件夹模式完成同一组文件检查。上述重试均不计入通过数。

## 实际高斯镜头与版式复核

源：`src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx`。
SHA256：`F994005D633D4AF742402469C9E4E2E0C289A57AED3AE876FB3231D1D3F95460`，导出前后未修改。

最终 `preview-final/` 使用正式产品导出链，7 项全部完成，9 页，5 张图片。包含 2D/3D、离焦、非相干照度、赛德尔系数、Zernike Fringe 系数、傅里叶 MTF；不包含完整处方，也没有重新运行此前全部 75 项。

- `preview-final/generation.json`：输入、工程快照、原图规格及导出结果。
- `preview-final/inspection.json`：7 个实际目录页码/跳转；5 张图均 2404×1804，PDF 原图框为约 523.28×392.678 pt（184.60×138.53 mm），与 PNG 解码像素逐一一致。
- `preview-final/qa/`：Poppler 渲染的全部 9 页；已查看目录、两种布局、15 格离焦、9 格照度、赛德尔表、37 行 Zernike 跨页表及 MTF。整图、图签、边框保留，未作图片二次排版。
- `source-images/`、`viewport-pixel-inspection.json`：7 个分析族的三种视窗 PNG；修复两项在 420×420、1000×800、1600×1100 视窗下字节完全一致。
- `preview-final/irradiance-power.json`：默认 5 光线、128×128、random、归一化；9 格均有非零样本，45 条有效入射光线。采样窗口为实际像面半口径 ±24.57053308904182 mm，未添加物理孔径。焦斑集中于少量像元，本轮不把默认稀疏采样作为高精度照度验证。
- `contents-75-sections.pdf`、`contents-inspection.json`：75 节、多配置、长标题、横向表及附录，共 115 页；76 个目录目标与印刷页码逐项一致，目录自动续页。

最终桌面：`C:/Users/19851/Desktop/高斯28度_图框与目录修复_20261030_135158/高斯28度_图框与目录修复预览.pdf`。
PDF SHA256：`B46DC48D17280104AAA6476E1D5830DA9EED2FA4483EE83833BBA3D95846CB6E`。

这是当前 Workbench 的定向计算与格式检查，不是新的 Zemax 精度认证或全部分析验收。前一轮完整报告的历史失败记录保留，辐射强度/光源分析的密集源图布局不在本轮修复范围。未操作桌面、提交、同步远端或发布安装包。
