# 双筒FFT与瞄准镜衍射能量

原三项Close全部达到原Pass门槛：双筒两配置FFT截面最高NRMSE为4.70167e-6和0.000351861，瞄准镜衍射能量为7.38414e-8。自动标量FFT瞳孔加入实际出瞳长宽比，负间距保持不拉伸；无焦累计能量直接形成角域傅里叶坐标间隔，恢复闭单位圆的四个对角节点。

最终原150项为 **134 Pass / 0 Close / 3 Difference / 13 Incomparable**；前轮131个通过、固定网格20项保持。有焦原1159项与补充52项全部Pass，原来源、请求、参考和容差不变。最终定向测试133项Core、104项比较，共237个不同用例通过；新增10个冻结曲线用例。默认Release App同步构建，无桌面输入或窗口关闭。

最终Core为`7f0e82799ab3357413a0629b1616b02a1274d193052a413b546895ca790b86d2`，全局策略为`30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`。2668份冻结文件在HEAD/工作区保持。完整边界见[报告](../../../../docs/AFOCAL_FFT_ENERGY_REPAIR_2026-10-27.md)，机器检查见[账目](../../../../docs/validation/AFOCAL_FFT_ENERGY_REPAIR_2026-10-27.json)。源提交`3947b7b57ae404f65af772b2a06ca356f79e539f`加本地修正，没有提交推送或发布。

最终有效原生14请求位于`native-energy-grid-v2`和`native-binocular`，显式读回波长、视场、类型/间距；新双筒201点截面与原参考数值相同，瞄准镜新能量曲线也相同。配置2虽通过原曲线门槛，仍有2个仅Core与2个仅原生瞳孔节点，不能声称逐位一致。三项内置质心图差异和13项不可比继续保留。

首版132/2/3/13与102通过/2失败的测试、756项处停止的旧Core有焦重放均保留，不计最终通过。首批能量诊断Linear误用All波长、空样本筛选和编译失败记录也保留。旧阶段及重复测试不累计。最终只使用`final-150-v2`、`final-fixed-v2`、`focal-guards-v2`、两组`focal-supplemental-*-v2`和`*-final.trx`。

`evidence-current.zip`与`evidence-diagnostics.zip`分别保存最终无焦重算/参考/原生控制/测试/源码，以及阶段诊断原文；都是独立标准ZIP，解压到同一空目录即可恢复完整仓库相对路径。按统一`evidence-manifest.json`逐成员读回，各包身份见`archive-integrity.json`。第一份完整归档另保留于本机忽略目录，未删除历史字节。大型有焦原始数组也保留本机，两个交付归档含各版本完整度量、身份和每份输出哈希；未完成旧重放明确标记，不作通过项。

复现入口：

- 新诊断工具`tools/diagnostics/AfocalFftEnergy20261027`的`geometry`、`energy-probe`、`energy-native-audit`、`binocular-audit`均要求模式、输入根和新输出目录；不覆盖历史输出。
- `Prepare-Fixture.ps1`从原生截面文本/能量数组生成10个原请求夹具；原参考哈希随夹具保留。
- 原150项、固定20项和1159有焦重放使用既有`tools/diagnostics/AfocalSampling20261027`的`prepare`/`prepare-fixed`、`workbench`、`compare`、`focal-guards`；本轮最终二进制在`final-runner-v2`。
- 补充22分析和30个点控制使用Objectives20261020的`objectives-sampling-controls`与`objectives-completion`，输入原参考根记录在身份文件，最终二进制在`final-objectives-v2`。
- 原生请求和批次列表完整保存，重采需本机OpticStudio2026 R1许可及新输出目录；本轮宿主原文和身份一并归档。
- `Confirm-Evidence.ps1`校验最终结论、同Core身份、原输入/参考/策略、14请求和冻结字节；`Pack-Evidence.ps1`随后归档并逐成员读回。

前阶段131/3/3/13和135测试保持在[出瞳尺度证据](../afocal-pupil-sampling/README.md)，更早的参考中心修复保持在[参考证据](../afocal-reference-focus/README.md)，不替换或拼接为本轮执行。
