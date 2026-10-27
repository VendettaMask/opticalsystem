# 无焦切向出瞳与 FFT 栅格核查

当前结果 **131 Pass / 3 Close / 3 Difference / 13 Incomparable**。新增关闭棱镜的自动 FFT PSF 与自动 Huygens PSF；原129项通过保持，固定角网格20项全部通过。有焦原1159项和补充52项保持原请求/来源/参考并全部通过。定向Core41项、无焦比较94项，共135个不同测试通过。

默认Release App与最终数值运行器的Core SHA-256：`9e084a60bd4fe65db149db5d8ad3329db4486f6ee21145664182fc32db658b3e`。原全局容差策略`30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`和2668份冻结字节保持。

新增有效原生请求10个：四文件配置1的Real/Imaginary复数场8个、两个同节点709点光线控制。双筒配置1和棱镜的FFT掩码各少10/14点，但显式原生逐光线全部正常；未据此在产品删除边缘光线。第一批8个旧适配器Linear误捕获、早期反演中心错误、跳过棋盘内插但未闭合Close的试验，均保留为失败诊断。有效复数场是`native-complex-pupil-v2`，最终反演是`pupil-inverse-v7`；它们属于诊断，不能混算数值通过项。

报告见[实现与剩余边界](../../../../docs/AFOCAL_PUPIL_SAMPLING_REPAIR_2026-10-27.md)，机器摘要见[最终账目](../../../../docs/validation/AFOCAL_PUPIL_SAMPLING_REPAIR_2026-10-27.json)。源提交为`3947b7b57ae404f65af772b2a06ca356f79e539f`，本轮修正尚在工作区；没有Git推送、安装或发布，没有桌面输入/窗口重启。

`evidence.zip`以仓库相对路径保存无焦原始重算、新增原生原文/读回/请求、原自动及固定网格参考、阶段诊断、测试TRX、原策略及最终Core。全部成员按`evidence-manifest.json`逐字节读回核验，归档身份见`archive-integrity.json`。有焦大型数组保留在本机`artifacts/validation/afocal-sampling-20261027`；归档含完整度量、身份、请求/参考核对和每份原始输出的哈希，未把旧数组冒充本轮重算。

重现入口为`tools/diagnostics/AfocalSampling20261027`：

- `Prepare-Fixture.ps1`从原生网格生成两项冻结回归夹具，拒绝覆盖已有输出。
- 以该目录csproj构建运行器；`prepare`、`workbench`、`compare`的三个参数依次为模式、`artifacts/validation/afocal-recompare-20261026`和新的输出目录；固定网格先用`prepare-fixed`。
- `pupil-inverse`输入有效复数捕获根和新输出目录；它检查实际类型读回、恢复循环中心并调用共享Core傅里叶变换。
- `focal-guards`执行原1159个有焦请求。补充22项和30项分别使用既有Objectives20261020的`objectives-sampling-controls`、`objectives-completion`模式，原参考位置见归档内身份。
- 复数与逐光线原生请求均保留`native-jobs.json`及完整`request.json`，需本机OpticStudio2026 R1许可和相同宿主接口；重采应使用新的输出目录。
- `Confirm-Evidence.ps1`核对本轮计数、同Core身份、原请求/参考、冻结完整性和新增原生设置读回；`Pack-Evidence.ps1`在全部完成后归档并逐成员读回。

初次94项比较测试在增加参考平面异常保护前运行；最终94项与41项使用最终同一Core，按不同用例计135，重复执行不累计。此前参考中心修复的477项保留在[前阶段证据](../afocal-reference-focus/README.md)，不与本轮累计。其余三项Close、三项内置质心图差异、六项自动网格及原无数据/不适用范围仍开放。
