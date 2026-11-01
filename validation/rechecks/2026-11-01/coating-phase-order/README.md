# 镀膜相位阶数排查（2026-11-01）

本轮是离线诊断，产品Engine未修改，没有新增整组达标。详见[报告](../../../../docs/COATING_PHASE_ORDER_AUDIT_2026-11-01.md)。

- inputs / baseline-source：8个真实热起点、原材料/门槛和开始时Engine源码。
- probe-nb-a / probe-nb-b / probe-hr：491个正式邻域候选、96次短程精化及最终保留；probe-all / probe-cold为保存重开复算。
- full-nb-a / full-nb-b及full-cold：10个相位起点的原N完整Optimize，均未获得窄带共同改善；临时失败处方完整保留，不作为正式结果。
- control-nb-a / control-hr：5个旧流程续算控制；计入额外计算，不冒称等总成本。
- reference-inputs.json / reference-results.json / reference-native：24组参考及本地原门槛复验，点平方目标不同。
- physics-comparison.json：2583个独立R/T/A物理核对点；verification.json逐项记录源身份、边界、旧证据与参考完整性，diagnostic-results.json汇总完整账目。
- audit-runtime是首次扫描二进制，polish-runtime额外增加完整Optimize入口及精化处方输出字段，扫描数学保持。运行时目录忽略，摘要另存；未推广到产品或修改材料。logs保存两次独立构建原始记录。

没有本轮新产品测试通过数、默认产品构建、全仓或桌面验收；未提交、推送或发布。final-manifest.json只封存本轮证据；上一阶段证据只读保持。有限扫描不证明全局无解，自适应结构反馈仍未实现。
