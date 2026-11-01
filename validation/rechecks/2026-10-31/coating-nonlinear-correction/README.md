# 镀膜非线性约束修正验证（2026-10-31）

最终结果、成本、限制与失败记录见[报告](../../../../docs/COATING_NONLINEAR_CORRECTION_2026-10-31.md)。61项三臂冷复算，29/61达标、无新增整组达标；29改善、0退步、32持平。最终25/25定向测试，7998个独立R/T/A点通过。

- inputs：原55逐字节保持，加6预冻结新规格；before-raw含55历史输出和6新旧版完整优化。
- group-results及initial-full-summary：初轮61次完整Optimize；portfolio-phase为最终窄带阶段复算。after-raw候选历史与完整Optimize相同，6项final-full-replay逐值确认。
- control-phase、holdout-control-phase：旧质量点后一个同窗口无修正控制；总成本不与最终多方向/抛光相等。
- before/control/after-cold：同一最终Core的183个冷复算膜堆，保留原报告与判定。所有最终窗口仍共享原额外N。
- pre-target-priority-paired、rejected-target-priority-source及probe目录：未被采用的反例与探索；probe1中断、probe2/3失败均不算验证通过。
- reference-results：78历史+18新固定参考，96份结果均当前Core复验；不是等目标/等成本排名。
- tests：最终portfolio-final-focused.trx为25/25；首轮50项中两项新增HR测试误用NBF断言，修正后另有5/5及最终25/25。未执行本阶段完整模块测试。

重放：先构建工具，再运行 quality-run、allocation-recheck、allocation-physics；nonlinear-phase仅用于明确标注的最终阶段隔离，不能称完整优化。固定源码的构建引用同一Core/Application，绝不换n/k或门槛。

```powershell
dotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj --no-restore
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll quality-run validation/rechecks/2026-10-31/coating-nonlinear-correction/inputs <new-output-directory>
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll allocation-recheck validation/rechecks/2026-10-31/coating-nonlinear-correction/inputs <new-output-directory> <cold-output-directory>
```

忽略的*-runtime目录是本地构建，不是输入来源；版本、源码、输入与测试/比较二进制SHA记录于final-manifest。
