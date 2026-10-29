# 本机 Zemax 无焦调查恢复证据（2026-10-29）

**原生运行环境已验证；精度问题仍开放。** [阶段报告](../../../../docs/AFOCAL_NATIVE_RESUME_2026-10-29.md)
列出新证据、否决假设及当前边界。产品Core源码最终不变，原处方/采样/参考/
容差保持，没有将原生掩码或轴交换写入产品。

- [assessment.json](assessment.json)：20个成功原生任务的退出码、版本/许可、
  实际NSC设置、POB字节、独立参考选择器、三份新旧FFT完整通道相等及最终五请求。
- [verification.json](verification.json)：证据ZIP长度/SHA-256、逻辑文件与唯一
  字节成员计数、Core程序集和Polygon源码身份。
- [evidence.zip](evidence.zip)：新原生数组、实际设置、33点顺序面与内部NSC路径、
  26点接缝/Glue控制、输入请求、执行日志、修改前/否决假设/撤回后的诊断以及源码。
  包内`entry-map.json`记录每个逻辑路径到唯一成员的长度和SHA-256；相同字节
  只保存一份。没有专有ZOS-API程序集或未执行GUI截图。
- [native-guards.trx](native-guards.trx)、[test.log](test.log)：现有POB依赖保护和
  原生配置选择 **12/12**，失败/跳过0。它们不是20个原生采集或5份光学比较的新增覆盖。

首次沙箱IPC失败和第一次原生路径面型本地化守卫失败保留在ZIP，后续均已解决。
原生实际完整完成20任务，另外2次失败尝试不计入成功数；本批无超时。原准备
清单11任务不等于全部执行：各批真实执行集合只看`execution.json`，准备时的
`existing-evidence.json/nativeExecuted=false`保留其准备阶段身份。

最终五份原FFT截面仍为4份逐点合格、配置2一份失败；最大差
**0.0011946610381988604**、**335**点超限。退出0表示诊断完成，
`allProfilePointsPassed=false`明确表示没有全部通过。未重跑170项或全仓。
三对独立Field2能量曲线与三个新旧FFT输出的相等是参考核实，不能计成光学修复。

本次没有接触桌面或已有交互会话，完成后没有遗留Zemax进程；没有安装、发布
或触发远端构建。复现见[C#诊断工具](../../../../tools/diagnostics/FftProfileNative20261029/README.md)，
封存工具逐成员重新核验，不覆写已有证据ZIP。
