# 近轴像高入射相位复核

规则、误差与范围见[专题报告](../../../../docs/PARAXIAL_FIELD_PHASE_REPAIR_2026-11-02.md)。

- [summary.json](summary.json)：216项前后同范围结果、9项关闭身份、36项测试和正常双模块App构建。
- [results-final.json](results-final.json)：全部原请求的实际分类；188 Pass、2 Close、26 Incomparable，没有Difference/Error。不可比不算通过。
- [source-verification.json](source-verification.json)：最终Core、诊断和测试的执行PDB及源码校验。
- [archive-index.json](archive-index.json)、[archive-verification.json](archive-verification.json)：SHA-256内容索引与完整读回收据。

索引`archive`相对本目录；`entry`指向zip成员，`entry=null`表示该路径本身就是单一权威文件。旧字节引用Huygens或2026-11-01检测归档，不重复冻结源码、运行时或参考。新增字节只保留在`evidence.zip`，校验后移除展开副本。逻辑路径`before`为94项定位，`before-full`及`after`为两个Core各自的216项原设置重放；`tests`为36项TRX，`logs`保留构建和网络元数据警告。

原生数组沿用逐请求`nativeReference`及`nativeDataSha256`所指冻结输入，未新采集或修改。保留原门槛和低采样参考状态；本轮没有95支统一重跑、全仓测试、前台UI操作、提交或远端同步。复算入口为`tools/diagnostics/PhaseCommon20261102`，使用空输出目录，`triage`只定位基础分析，`full`执行三支全部216项。
