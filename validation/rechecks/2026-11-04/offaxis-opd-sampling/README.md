# 离轴OPD与19项采样核查

见[根因、修复、完整范围与边界](../../../../docs/OFFAXIS_OPD_SAMPLING_REPAIR_2026-11-04.md)。

- [最终摘要](summary.json)、[OPD/跨系统结果](opd-results.json)、[逐面平面波检查](plane-controls.json)、[等效副本与实际光线保持](equivalent-source-controls.json)。
- [19项表格](sampling-audit.csv)、[完整原容差度量](sampling-results.json)、[缓存实际读回](cache-readback.json)、[原容差文件核验](policy-verification.json)。
- `archive-index.json`映射每个逻辑文件到已核验的内容载荷；`archive-verification.json`记录完整读回结果。复用原归档而不复制未变化载荷；展开运行仅在完整验证后退役。
- 额外99项既有离轴OPD均保持Pass，见`extra-edge-results.json`与`extra-edge-verification.json`。`supplemental-evidence.zip`及其完整读回记录保留这组补充控制、最终文档和最终摘要，主归档保持原字节。
- `source-verification.json`将最终Core/Application/比较器/诊断/测试绑定到PDB文档哈希；历史诊断二进制和失败日志单列，不声称其源码与最终诊断相同。

产品原19项仍Unavailable；新等效OPD协议不替换原4 Close/2 Difference。不同Core、原采样与独立加密结果分别维护，无新全95支统计或远端同步。
