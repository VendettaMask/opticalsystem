# R2 scaled regularized local solver

> 后续已接入 [v11 自动镜片直径](../../../../docs/INITIAL_STRUCTURE_AUTOMATIC_DIAMETER_2026-09-26.md)。本文保留 v10 机制与工程证据；v10 完整搜索在 43/60 次时中断（30 次成功），留出集未执行，不能作为完整协议结论。

See the [implementation report](../../../../docs/INITIAL_STRUCTURE_DSEARCH_R2_2026-09-26.md). The optical engine, objective weights, frozen targets and acceptance gates are unchanged. The v10 optical phase uses Jacobian column scaling and a projected, regularized QR step. Geometry restoration and the generic solver default retain the prior dogleg path. This is not SYNOPSYS PSD or a full TRF implementation.

`ablation-600.json` records the initial 32-case diagnostic before default integration. `ablation-600-final.json` repeats all cases on the v10 integration, adds retained prescriptions and independently reconstructs/recalculates every retained point. All numerical results, validity states and work counts reproduce exactly. The eight starting snapshots are the unchanged [R1 inputs](../dsearch-r1-20260926/inputs.json); every input hash is recorded.

| Variant | Final physical passes | Retained physical passes | Solve evaluations | Invalid trials/probes |
| --- | ---: | ---: | ---: | ---: |
| Dogleg | 0/8 | 0/8 | 4772 | 0 |
| Scaled dogleg | 1/8 | 1/8 | 4800 | 0 |
| Regularized | 2/8 | 2/8 | 4800 | 1 |
| Scaled regularized | 4/8 | 5/8 | 4800 | 1 |

All 32 retained evaluations exactly match fresh Core calculations. Scaled regularization lowers final merit relative to dogleg in all eight cases, but three retained candidates still fail. The 62 mm `----` case has a physically accepted intermediate point and an unaccepted final point; incumbent retention is essential. These are independent mechanism checks, not additional passing tests or a complete search protocol. No wall-time speed claim is made.

The standalone probe adds no package or optical formulas. It uses the existing test friend-assembly identity and current default Release test assemblies; `-p:AuditBinaries=/absolute/path` can select a retained binary directory. From the repository root:

```sh
dotnet run --project labs/InitialStructure/benchmarks/dsearch-r2-20260926/probe/Probe.csproj -c Release -- labs/InitialStructure/benchmarks/dsearch-r1-20260926/inputs.json artifacts/validation/new-r2-ablation.json
```

`history/History.csproj` captures a real v9 checkpoint using frozen v9 assemblies, then verifies its independent continuation using v10. `history-capture.json` and `history-upgrade.json` preserve source/binary hashes, exact budget charges and export/reload status. The source checkpoint remains untouched. Raw checkpoints and TRX files live under `artifacts/validation/dsearch-r2-20260926`.

`interrupted.json` records the incomplete 43/60 protocol and its raw summary hash; `source-freeze.json` preserves the v10 source identity. `archive_results.py` and `verify_evidence.py` were prepared for a complete v10 60+15 archive, which was never produced. They are not evidence of a passing full protocol and their frozen-source checks do not apply to the current v11 tree.
