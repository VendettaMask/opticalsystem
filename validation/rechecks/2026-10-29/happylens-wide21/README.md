# 原广角任务同预算对照：证据入口

结果：**没有改善**。4次Delano准备均未满足原F数容差，回退原三片后的小池重生失败；两臂最终保留同一合格三片，默认减片分支均未达标。

- [完整说明与结果表](../../../../docs/HAPPYLENS_WIDE21_COMPARISON_2026-10-29.md)
- [运行前冻结协议](protocol.json)：原任务SHA、预算、准备分配、原验收与执行顺序。
- [两臂汇总](runs/summary.json)：旧38评价/27梯度/1,362,694追迹，新160/28/1,414,924；29.807/25.004秒，均在600/100/200万/60秒总额内。
- [新准备与全部拒绝终点](runs/delano-population/preparation.seed-search.json)：0/4保留，实际120评价、0追迹。
- [只读一阶诊断](preparation-first-order-audit.json)：额外12次Core近轴量请求、0追迹；最好F3.445628，相对原目标+3.1133%，原容差±2%。
- [旧检查点](runs/original/run.autonomous.json)、[新检查点](runs/delano-population/run.autonomous.json)，保留预约、真实阶段和全部追加账目。
- [保留的合格三片](runs/original/best-retained.staropt)、[旧失败两片](runs/original/trial-0-unqualified.staropt)、[新失败两片](runs/delano-population/trial-1-unqualified.staropt)。
- [5项回归](per-face-regression.trx)、[规格与预算核验](verification.json)、[运行程序集身份](runs/runtime.json)。

新起点没有完成35场像质验收，因此不存在可报告的新生成RMS改善。回退后的保留三片RMS/最大为49.292572/136.488028 µm，两片分支318.072237/731.311351 µm仍失败。此处使用原合格三片重新派发默认分支，范围与此前最佳失败两片接续分开。未做额外优化、加预算、减采样或放宽门槛；所有运行均后台CPU、两处理器、低优先级。
