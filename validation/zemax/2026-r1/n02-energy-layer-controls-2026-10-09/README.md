# N02 同源能量与接口控制

2026-10-09 新增原生输出，来源为 OpticStudio 26.1.0 / build 260127、有效 Enterprise API 许可。原 MS-L7 处方 SHA-256 为 `8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8`， exact-byte 副本位于相邻 FFT 瞳面捕获目录。49 份原始文件由 `manifest.json` 逐份记账。

`dee-32` 复现原 N02 的全部数值曲线：配置 1、最终像面、轴上视场 1、波长 1（0.4861327 µm）、FFT 32、自动间距、质心、最大半径 5 µm、偏振关闭、显示衍射极限。新曲线的 401 个 X/Y 点与原冻结捕获逐值相同。`dee-range-2p5` 和 `dee-range-10` 仅改变最大半径；`dee-x-32` 是 X 狭缝控制。

`inspect` 保存 FFT PSF、DEE、Huygens PSF 的实际设置接口契约。检查到的 FFT PSF 接口没有原生理想二维输出选项；原生 DEE 内部积分网格和物理总能量分母没有取得。独立 FFT 图不能直接被认定为 DEE 私有积分网格。

`operand-controls-03` 保留审计后的 DENF 原生列、56 项原生标量和系统设置。API 列 2..9 为 Samp/Wave/Field/Dist/Type/Refp/I Samp/I Delta。正式产品既有本地槽位顺序未迁移，原生导入继续只读；列审计不等于数值等价认证。`Advanced.HuygensIntegralMethod=Auto` 是实际源文件设置，不能据此断言原生 N01 最终采用平面或球面。

早期错误 DENF 参数顺序、启动失败和中止捕获均未收入此参考目录。原参考、输入和容差未覆盖。差异与缺口见 [修复报告](../../../../docs/FFT_PUPIL_PHASE_REPAIR_2026-10-09.md)；本 README 为人工说明，不冒充原生输出。
