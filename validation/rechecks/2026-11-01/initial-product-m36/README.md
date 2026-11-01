# M3.6 产品展示与报告后台验证

本轮实现与使用见[产品说明](../../../../docs/INITIAL_STRUCTURE_PRODUCT_REVIEW_2026-11-01.md)。原口径、像质、制造门槛、所有数值策略和预算不变；F/4原规格冲突保持。以下证据只支持实际覆盖范围，不代表前台GUI、安装或发布验收。

- `tests/m36-v*.trx` 和构建日志保留开发期失败及修正；`m36-v5.trx`为13项专项通过。不能把重复运行相加成不同测试数。
- `tests/m36-final.trx` 为冻结运行时的相关桌面/准备断点/展示/报告回归；`tests/runtime-before.json`、`runtime-after.json`记录9个实际二进制前后校验。
- `evidence-final/` 保留五个原F/8处方的原目标、源账目、独立验收及STAROPT回读结果，并保留原F/4冲突零工作记录。这里重验已有端点，不计为新独立冷启动或新增设计。
- `screenshots-final/`及单独的最终窄窗口截图为离屏渲染；不控制用户前台。三语报告示例随对应证据目录保存。
- `product-session-final/`为最终原F/8真实自动准备、正常停止/重开/续跑、一次主搜索试验、单次交接、STAROPT回读及零追迹报告/方案库验证。准备额度仍为原600函数/600梯度/千万光线/600活动秒；只限制后续主搜索为一次试验，不把该额外会话加入M3.4五种子时间统计。
- `final-source.zip`、源/运行时清单及`final-verification.json`封存实际相关源、驱动、范围和证据SHA-256，历史M3.4/M3.5证据保持。严格文档与默认主程序构建分别留档。

旧检查点继续使用原格式和协议。方案库和单份报告快照为新的只读文件，不能替代原检查点续跑，也不升级旧数值状态。查看、入库及报告为零新增追迹；显式请求结构显示的Core追迹单独计费。未记录的旧阶段费用/设备不视为零或GPU执行。

最终14项专项见`tests/m36-release-final.trx`及`evidence-release-final/`，最终三语/窄屏截图在`screenshots-release-final/`。`tests/product-release-final.trx`和`product-release-reopen.trx`各1项通过；旧`product-session`和`product-session-v2`保存已完成数值会话但收尾审计失败的记录，另有零追迹重开修正试验，费用不隐藏。83项冻结回归早于最后的过渡/紧凑序列化修正，最终14项和原任务完整链路专门覆盖这些修正；重复测试身份不累计成不同测试或不同设计。
