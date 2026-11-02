# 2026-11-02 Huygens 与同域照度复核

实现、完整范围及限制见[专题报告](../../../../docs/HUYGENS_COMMON_REPAIR_2026-11-02.md)。

- [summary.json](summary.json)：同一最终 Core、426 项分类、270 个不同测试、默认双模块 App 构建及范围。
- [results-final.json](results-final.json)：原设置逐请求结果，不用高采样认证改写低采样分类。
- [high-sampling-results.json](high-sampling-results.json)：32 条同档/收敛比较，含低采样未收敛记录；1024 全部 Pass。
- [source-verification.json](source-verification.json)：执行 PDB 的源校验，早期诊断入口的精确源码另存；没有覆盖当前源码。
- [archive-index.json](archive-index.json)、[archive-verification.json](archive-verification.json)：逐文件长度/哈希、归档成员及完整读回收据。

索引的 `archive` 相对本目录解析，成员名在 `entry`。旧字节复用 2026-11-01 的已核验 CAS 归档，不复制原参考、源码树和运行依赖。`retainedFile` 是保留在本目录的唯一实体文件；四份新通焦原生输入留作诊断直接重放，不在 zip 内重复保存。其余新内容仅在 `evidence.zip` 的 SHA-256 成员中保存。归档核验完成后退役对应展开副本。

主要逻辑目录：`convergence-final` 为最终 Core 通焦复算，复用 `convergence-first` 的新原生 512/1024；128/256 引用旧归档。`psf-convergence-corrected` 为完整 PSF/MTF/截面专用适配器捕获。`categories` 为426项原设置；`illumination-native` 保留原生“无法计算相对照度”及数值参考缺口。`tests` 包含269项和单项补验 TRX，中止的 testhost 记录不算通过。`logs` 保留构建、恢复、错误适配器诊断和首次归档日志锁失败；这些失败不进入正式数值认证。

复算入口为 `tools/diagnostics/HuygensCommon20261102`；`replay` 保留原32×32窗口，`psf` 使用专用适配器，`illumination` 不重复失败的原生请求。新输出应选空目录，原记录不要覆盖。旧归档与保留原生输入是活跃参考，不因日期较早而删除。本轮没有全仓测试、前台 UI 验收、提交、推送或发布。
