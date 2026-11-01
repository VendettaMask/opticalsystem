# 匹配后反馈与正式极值采样证据

正式修正为通用极值锚点、验收稳定性与结果元数据；结构反馈仍为离线实验。见[报告](../../../../docs/COATING_TOPOLOGY_FEEDBACK_2026-11-01.md)。

- inputs/baseline-source：9个原实际起点、真实n/k及开始源码。*-feedback-final / *-prototype-final / *-control-final为采样/1下9+6+9个实际运行；较大旧N另2项。
- current-*、current-paired.json、paired-final.json：同采样/2按实际膜厚重评；原优化轨迹未改写成新采样运行。
- all-stage-prescriptions.json / current-stages：240个实际处方重评、48个full及新通过short光谱，实际成本留原summary。
- fine-sampling-before/after.json：同门槛400/1600/3200的实际Checks及8192/32768均匀网格核对。
- verification.json / execution-ledger.json：输入、数值门槛/Measure、固定层、源码/参考及旧169项证据完整性。physics-comparison-current.json含4389个独立R/T/A比较点，含各臂重复处方。
- 日程主额度相同不等总成本；粗指导、恢复/质量及OpenAsync本来就会复算。DenseCalls原字段不含short内部，VerifiedDenseCalls补加；新Result极值查询按Evaluate单列，不冒称全搜索精确总查询。
- superseded-*.json及初版源码/日志为停止的诊断器问题，不计最终完成；首次加密obj冲突/可空警告保留，修正后构建通过。logs/dense-extrema-focused.trx为最终12项。

无新增整组达标、全局不可行证明、完整模块/全仓或原生桌面验收，未提交/推送/发布。final-manifest.json封存本轮，运行时忽略但摘要单列。
