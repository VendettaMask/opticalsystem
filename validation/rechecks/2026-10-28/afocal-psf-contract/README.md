# 无焦 PSF 比较契约证据

完整范围见[本轮报告](../../../../docs/AFOCAL_PSF_CONTRACT_REPAIR_2026-10-28.md)。机器汇总为 [verification.json](verification.json)。

- `afocal-before.trx`：原有15项基线通过。
- `contract-before.trx` / `.log`：首批23项新增回归全部失败，保留修复前证据。
- `contract-after.trx` / `.log`：同23项通过。
- `afocal-focused.trx` / `focused.log`：无焦相关142项通过。
- `contract-final.trx` / `.log`：最终新契约40项通过；与前述合并去重为144个不同用例，不按执行次数求和。
- `binocular-current.json`：既有审计器重放两配置冻结复数场，配置1零独有节点，配置2双方各2个独有节点；不作通过项累计。原始来源字节见 `binocular-source-manifest.json`，没有新原生捕获。
- `format.log`：定向 whitespace 验证通过（空日志）；`local-checks.json`：diff whitespace、相关文档本地链接检查和受保护路径核对。
- `closure-diagnosis.json`：只读归档的步长/零点索引核验及三组质心参考切换原曲线一致性，附来源成员哈希。
- `binocular-nodes-final.json`：最后两配置诊断，仅增加四个差异端点的索引输出，不改算法、采样或接纳；归一坐标按Core网格解释。
- `scope-test-audit.json`：测试名（含参数）去重清单、按类计数、最终diff范围与受保护文件检查。

逐请求坐标差、强度NRMSE与coverage记录于最终TRX及汇总。自动10项为4 Pass/6 Incomparable，独立固定角PSF10项全Pass；不可比断言通过不等于该光学比较通过。原150项134/0/3/13仅为原完整矩阵记录，本轮未重跑完整矩阵。

当前测试通过[成员清单](../../../zemax/2026-r1/afocal-psf-contract-2026-10-28/manifest.json)直接复用[2026-10-27原归档](../../2026-10-27/afocal-fft-energy/evidence-current.zip)，按归档及逐成员SHA核验，不再复制一份原生数组。原阶段派生的 `native-grids.zip` 仅按下述Library存储记录保留，不是现行测试依赖。参考数组、模型和捕获设置字节不变；本节的比较契约修复阶段没有修改Core/Application，后续交会修改的独立身份见下文。

Continuation: `existing-fields-final.json`, `capture-plan-verification.json`, `closure-regression.trx/.log` record the existing native endpoints/pupil text and 80 repeated passing tests. Native diagnostic host changes are not compiled/executed here. See the report and Capture-Remaining.py README for the bounded external jobs.

Independent physics phase: 8 additional tests pass (152 distinct recorded identities in total). Three probe tests assert finite data, NOT scale stability. See [independent report](../../../../docs/AFOCAL_INDEPENDENT_PHYSICS_2026-10-28.md), independent-first/final.trx and independent-physics-summary.json. No Core changes or new native runs.

Core conditioning phase: FitLines only changed; 4/6 synthetic failures repaired without gate changes. 125 distinct affected tests rerun and pass; 164 historical/latest records must not be read as all rerun after Core change. See [conditioning report](../../../../docs/AFOCAL_PUPIL_CONDITIONING_2026-10-28.md). Binocular sensitivity remains, first detected at polygon_group surface 6.

Polygon path phase: two observation-invariance tests pass for 42 rays. Independent plane/Snell checks pass; same quad order but triangle selection changes across sign at noncoplanar frozen POB faces. See [path report](../../../../docs/AFOCAL_POLYGON_PATHS_2026-10-28.md). No new optical algorithm change.

GitHub storage: large derived ZIP and verbose TRX are excluded from new commits and preserved in Library `libfile_bd0a0b365150819188071f73122732bd` version 4; see `evidence-storage.json` for exact paths/hashes. Small original failures and text summaries remain here. PSF tests now read the existing committed 2026-10-27 archive using the new per-member hash manifest; no native reference changes.

GitHub fixture packaging check: `github-fixture-reuse.trx` records 40 passing contract tests after switching to the existing source archive, 0 failures/skips, ~20 seconds. Original thresholds and outcomes unchanged.

Native edge contract phase: `native-edge-plan-verification.json` checks 11 offline jobs with two 33-point full-surface probe sets. NSC context capture added but native compilation/execution remains unverified. See the dated native-edge-contract report for official 2026 R1 sources and remaining ambiguity.
