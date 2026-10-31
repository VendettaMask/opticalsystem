# 无焦分析结果复核与光阑实际差异

依据“所有适用分析结果按原容差一致”的标准，完整重放四文件/五配置的原150项及固定角20项。最终163项数值一致、7项不适用/不可比、314,478次对齐比较无超限；不把该范围外的分析记为通过。新增5组原生光阑控制共4,696值通过原门槛，相关230个不同回归通过。

[专项报告](../../../../docs/AFOCAL_ANALYSIS_RESULTS_2026-10-31.md)按18类分析列出结果与待验证范围。[assessment.json](assessment.json)给出完整请求结论、Core身份、按分析名称的覆盖及Core目录中54个本批未覆盖名称。54个名称不表示54项错误或全部适用于无焦。

`evidence.zip` 只封存本次输出、诊断、测试和源码，不复制旧原生数据或带入专有程序集。`entry-map.json` 按内容SHA256去重，每个逻辑路径映射到实际成员；全部成员写入后读回核对长度和摘要。[verification.json](verification.json)绑定归档身份。

原输入、参考及旧结果依赖保持原字节：

- [原无焦捕获归档](../../2026-10-27/afocal-fft-energy/README.md)，SHA256 `235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1`。
- [原170项有效门槛与历史账目](../../2026-10-29/final-core-170/README.md)，由输入身份文件绑定具体源账目、归档和请求指纹。
- [新增原生夹具](../../../zemax/2026-r1/afocal-aperture-state-2026-10-31/manifest.json)，逐文件绑定源ZMX及原始数组；APMN/APMX未激活和CLAP已激活的原生API读回均另存于证据包。

纯C#复跑入口是 `FinalAfocalRecheck20261029.dll prepare-current <repository> <fresh-run-root>`，随后执行 `compute` 和 `audit`；运行组件应先复制到独立runner目录。历史Python准备器保持其原阶段，不用于本轮。`Seal-ResultAudit.ps1 -RepositoryRoot D:/Projects/opticalsystem` 验证完整矩阵、全部输入、原门槛、原生反例、最终测试和统一Core后封存，拒绝覆盖已有证据。

全程后台执行，使用独立Zemax应用、禁用截图，不附着或关闭用户应用。没有提交、推送、安装或发布。
