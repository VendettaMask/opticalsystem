# 非球面与逐面偏心/倾斜转换证据

2026-11-01。最终 **521/521** 定向测试通过，新增 **33** 项，零失败/跳过；[实现与明确支持域](../../../../docs/ASPHERIC_POSED_NSC_2026-11-01.md)。

- [最终汇总](final-verification-summary.json)、[TRX](final-focused.trx)、[日志](test-final-focused.log)；[最终默认Release App](build-app-image-plane.log)含两个内置模块和[测试驱动](build-tests-image-plane.log)均零警告/错误。
- [八组逐面光路](final-controls/path-False-False-True.json)与其余`path-*.json`共32面，覆盖空气/浸没、偶次/奇次、有无独立偏心/倾斜；[有限口径倾斜平板与8192条采样](final-controls/tilted-plane-statistics.json)。解析控制不是实测镜头或商业数值认证。
- 四组三波段链路，以下每行保留一个当前权威工程、数据库和相机实验。

| 场景 | 工程 | 光线数据库 | 相机实验 | 指标 |
| --- | --- | --- | --- | --- |
| 空气/直射 | [STAROPT](final-controls/chain-False-False.staropt) | [STARRDB](final-controls/chain-False-False.starrdb) | [实验](final-controls/chain-False-False.camera-experiment.zip) | [JSON](final-controls/chain-False-False.json) |
| 空气/Lambertian | [STAROPT](final-controls/chain-False-True.staropt) | [STARRDB](final-controls/chain-False-True.starrdb) | [实验](final-controls/chain-False-True.camera-experiment.zip) | [JSON](final-controls/chain-False-True.json) |
| 浸没/直射 | [STAROPT](final-controls/chain-True-False.staropt) | [STARRDB](final-controls/chain-True-False.starrdb) | [实验](final-controls/chain-True-False.camera-experiment.zip) | [JSON](final-controls/chain-True-False.json) |
| 浸没/Lambertian | [STAROPT](final-controls/chain-True-True.staropt) | [STARRDB](final-controls/chain-True-True.starrdb) | [实验](final-controls/chain-True-True.camera-experiment.zip) | [JSON](final-controls/chain-True-True.json) |

链路检查实际NSC功率、主/反射路径分区、一次光子换算、工程重开后精确NSC重追迹，以及电子、RAW和ISP逐值重放。三波段为450/550/650 nm，声明的光源/传感器控制；散射场景包含多次返回Lambertian面的分支。没有用功率归一化使结果通过。

[离屏编辑器](final-controls/posed-assembly-editor-offscreen.png)显示逐面的非球面种类、系数数目和真实坐标轴，图像已查看；属性应用保留载荷和像面口径。没有控制用户桌面。

[编译校验](compiled-source-audit.json)覆盖四个实际执行程序集的105份相关源码；[映射收据](source-snapshot-map.json)共133个文件，仅18个本轮变更文件保存源码修订，其余保留版本/哈希收据。像面保护只追加三个变更版本，未再复制整棵源码。90个执行文件[前](before-final-run.json)/[后](after-final-run.json)不变，22个夹具副本与当前源输入核对一致。执行位置见[运行时记录](execution-location.json)：复用既有任务所有的C盘测试运行时，没有新建依赖快照。旧v20两个工程和两个数据库直接读取此前权威文件，摘要和原封存哈希保持，不复制旧数据集。

[验证后的并行工作树变化](post-verification-worktree-changes.json)单独记录GeometryModels.cs中PlaneGeometry参考入射/报角元数据与构造签名的后续修改，不属于本轮已通过范围。保留对方工作树，受测源码从所记提交恢复为一份[精确参考](source-reference/GeometryModels.cs)，哈希与PDB一致；其余未变源码不复制。运行前后零漂移是验证时间窗的结论，不将它扩大为后续工作树状态。

早期求交残差重撞、测试签名编译失败、无限平板参考遗漏有限口径，以及像面保护前的一轮521项记录分别保留。[归档索引](attempt-archive-index.json)指向两份已完成逐条长度/SHA256核验的候选档案；冗余展开副本已移除，不把候选轮数加到最终通过数。早期默认输出锁定，以及并行镀膜代码一条重复using警告也保留；最终默认输出重试成功，未关闭用户程序。

复现需要.NET 10和完整仓库。先正常构建App及测试项目，复用或准备一份任务所有的测试运行时，在新证据目录运行`prepare-verification.ps1 -ExecutionDirectory <目录>`、现有`tools/diagnostics/OpticalAssembly20261101/PdbAudit.csproj`、`run-verification.ps1`及`verify-final.ps1`。检查收据与代码版本后使用新目录，禁止覆盖本封存结果。历史归档入口通过索引访问；未变输入和依赖不需要重复打包。

[封存清单](evidence-manifest.json)仅列本轮权威新数据、变更源码和必要记录。当前没有全自由曲面/折返/强吸收体、精确镜筒CAD、GPU、优化公差或实拍一致性认证；适用域与保守几何拒绝见实现说明。
