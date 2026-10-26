"""Summarize all observations, including failed/singular boundary evaluations."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parent
source = Path(r"D:\optical system design\tmp\autolens-upstream-20261026\source")


def load(name):
    return json.loads((ROOT/name).read_text(encoding="utf-8-sig"))


upstream = load("upstream.json")
native = load("csharp.json")
diagnostic = load("csharp-epsilon-diagnostic.json")
if any("error" in case for case in upstream["cases"]):
    raise RuntimeError("Upstream case generation is incomplete.")
if native["inputSha256"].lower() != hashlib.sha256((ROOT/"upstream.json").read_bytes()).hexdigest():
    raise RuntimeError("C# input fingerprint mismatch.")
if native["binaries"] != diagnostic["binaries"]:
    raise RuntimeError("Controlled attribution did not use the same C# binaries.")
runtime = Path(r"D:\optical system design\tmp\autolens-multiprescription-runtime")
for name, digest in native["binaries"].items():
    if hashlib.sha256((runtime/name).read_bytes()).hexdigest().lower() != digest.lower():
        raise RuntimeError("Frozen measured binary fingerprint changed: " + name)
rows = native["reports"]
assert len(rows) == 2 * len(upstream["cases"])
assert all(row["name"] == "critical" for row in rows if "error" in row)
ordinary = [row for row in rows if not row.get("boundary")]
assert len(ordinary) == 14 and all("comparisons" in row for row in ordinary)
successful = [row for row in rows if "comparisons" in row]
assert all(row["execution"]["Backend"] == "cuda-fixed-input-forward-v1" and not row["execution"].get("FallbackReason")
           for row in successful if row["mode"] == "Gpu")
ordinary_comparisons = [metric for row in ordinary for metric in row["comparisons"]]
fd = [item for row in ordinary for item in row["finiteDifferences"]]


def agrees(row):
    return "comparisons" in row and row["maskMismatchCount"] == 0 and row["outputMismatchCount"] == 0 and all(
        metric["valueNormalizedError"] <= 5e-8 and metric["gradientMismatchCount"] == 0 for metric in row["comparisons"])


by_case = []
for case in upstream["cases"]:
    pair = [row for row in rows if row["name"] == case["name"]]
    by_case.append({"name":case["name"], "boundary":case["boundary"], "parameters":len(case["parameters"]),
                    "rays":sum(len(field["inputs"]) for field in case["fields"]),
                    "cpuAgrees":agrees(next(row for row in pair if row.get("mode")=="Cpu")),
                    "gpuAgrees":agrees(next(row for row in pair if row.get("mode")=="Gpu")),
                    "errors":[row["error"] for row in pair if "error" in row],
                    "maximumMaskMismatches":max([row.get("maskMismatchCount",0) for row in pair]),
                    "maximumValueNormalizedError":max([metric["valueNormalizedError"] for row in pair for metric in row.get("comparisons",[])]or[0]),
                    "maximumGradientNormalizedError":max([metric["maximumGradientNormalizedError"] for row in pair for metric in row.get("comparisons",[])]or[0])})

gpu_comparisons = []
for case in upstream["cases"]:
    pair = {row.get("mode"):row for row in rows if row["name"]==case["name"]}
    if not all("comparisons" in pair[mode] for mode in ["Cpu","Gpu"]):
        continue
    mask_sets = [{(row["field"],row["ray"]) for row in pair[mode]["maskMismatches"]} for mode in ["Cpu","Gpu"]]
    gradient_error = max(abs(a["actual"]-b["actual"])/max(1,abs(a["actual"]))
                         for a_metric,b_metric in zip(pair["Cpu"]["comparisons"],pair["Gpu"]["comparisons"])
                         for a,b in zip(a_metric["gradients"],b_metric["gradients"]))
    value_error = max(abs(a["actual"]-b["actual"])/max(1,abs(a["actual"]))
                      for a,b in zip(pair["Cpu"]["comparisons"],pair["Gpu"]["comparisons"]))
    gpu_comparisons.append({"name":case["name"],"maskDifferences":len(mask_sets[0]^mask_sets[1]),
                            "maximumValueNormalizedDifference":value_error,"maximumGradientNormalizedDifference":gradient_error,
                            "actualBackend":pair["Gpu"]["execution"]["Backend"]})

case_map={row["name"]:row for row in by_case}
ordinary_stats={"caseCount":7,"cpuGpuEvaluations":len(ordinary),"allAgreed":all(agrees(row) for row in ordinary),
                "maskMismatches":sum(row["maskMismatchCount"] for row in ordinary),
                "maximumPositionAbsoluteErrorMm":max(max(row["outputErrors"][:2]) for row in ordinary),
                "maximumValueNormalizedError":max(metric["valueNormalizedError"] for metric in ordinary_comparisons),
                "maximumGradientNormalizedError":max(metric["maximumGradientNormalizedError"] for metric in ordinary_comparisons),
                "maximumGradientScaledError":max(metric["maximumGradientScaledError"] for metric in ordinary_comparisons),
                "gradientSlotsCompared":sum(len(metric["gradients"]) for metric in ordinary_comparisons),
                "finiteDifferenceDirections":len(fd),"allFiniteDifferenceMasksStable":all(item["sameMasks"] for item in fd),
                "maximumFiniteDifferenceAbsoluteError":max(item["absoluteError"] for item in fd),
                "maximumFiniteDifferenceRelativeError":max(item["relativeError"] for item in fd),
                "finiteDifferenceProbe":"traceX + 0.37*traceY + 0.1*traceDirectionZ + 0.01*traceBend; all coordinates simultaneously perturbed, no detached image-weight finite difference claim"}
diag_near=[row for row in diagnostic["reports"] if row["name"]=="critical-near-below"]
diagnostic_max=max(metric["maximumGradientNormalizedError"] for row in diag_near for metric in row["comparisons"])
near_total=max(metric["maximumGradientNormalizedError"] for row in successful if row["name"]=="critical-near-below"
               for metric in row["comparisons"] if metric["name"] in {"total-centroid","total-pinhole"})
code_files=["1_autolens.py","deeplens/config.py","deeplens/ray.py","deeplens/geolens.py",
            "deeplens/geometric_surface/base.py","deeplens/geometric_surface/plane.py","deeplens/geometric_surface/aperture.py",
            "deeplens/geometric_surface/aspheric.py","deeplens/geolens_pkg/optim.py","deeplens/material/materials.py"]
source_clean=subprocess.run(["git","diff","--exit-code","--",*code_files],cwd=source,capture_output=True,text=True)
if source_clean.returncode:
    raise RuntimeError("Upstream tracked numerical sources changed.")
summary={"createdUtc":datetime.now(timezone.utc).isoformat(),"scope":"Short fixed-input checks only; no optimizer updates, no complete refinement, no long-run restart",
         "cases":len(by_case),"distinctFixedImageRays":sum(row["rays"] for row in by_case),
         "distinctPrescriptionCount":len({json.dumps({key:case[key] for key in ["surfaces","sensor","optimizeMaterials"]},sort_keys=True) for case in upstream["cases"]}),
         "allOptimizerParameters":True,"parameterKinds":["AxialPosition","Curvature","Conic","EvenAsphereCoefficient(r4-r18)","MaterialIndex","MaterialAbbe"],
         "lossesAndProbes":["image centroid","image pinhole","geometry/material constraints","CRA+bend","full centroid objective","full pinhole objective","weighted image X/Y, direction Z, bend VJPs"],
         "ordinary":ordinary_stats,"perCase":by_case,"cpuVsGpu":gpu_comparisons,
         "totalScalarGradientSlotsCompared":sum(len(metric["gradients"]) for row in successful for metric in row["comparisons"]),
         "upstreamNonfiniteGradientSlots":sum(len(metric["nonfiniteRawGradientSlots"]) for case in upstream["cases"] for metric in case["losses"]),
         "substantiveBoundaryDifferences":{
             "aperture":{"mismatchingRaysPerBackend":case_map["aperture"]["maximumMaskMismatches"],"inputRays":132,
                         "upstreamValid":60,"csharpValid":84,"affectedRadii":"Exactly +r or -r on the planar stop; +/-1e-10 and +/-1e-7 relative offsets agree",
                         "reason":"Upstream Aperture inherits Plane.intersect with r^2 strict comparison; Core circular physical aperture includes boundary with numerical tolerance."},
             "criticalNear":{"minimumRelativeHeightMargin":1e-10,"maskMismatches":0,
                             "maximumGradientNormalizedDifference":case_map["critical-near-below"]["maximumGradientNormalizedError"],
                             "maximumTotalObjectiveGradientNormalizedDifference":near_total,
                             "controlledNoEpsilonMaximumGradientDifference":diagnostic_max,
                             "attribution":"Temporarily setting only upstream base module EPSILON=0 reduces the gradient discrepancy greatly. This controlled output is not the baseline. Residual is consistent with ill conditioning/roundoff; not claimed bit equality."},
             "criticalExact":{"upstream":"Original baseline produced finite stabilized outputs and gradients in this case.",
                              "csharp":"CPU and CUDA reject the singular derivative for a batch containing the critical ray. One affected ray can make the fixed-input request fail.",
                              "scope":"Physical/AD domain policy difference; not changed or treated as agreement."}},
         "tolerances":{"valueNormalized":5e-8,"gradientNormalized":5e-6,"normalization":"abs(actual-reference)/max(1,abs(reference)); specified in the probe before the final measurements",
                       "scaledGradient":"abs(gradient error)*characteristic parameter scale: axial/conic/nd=1, curvature=1/max(1,r), ai=1/max(1,r)^order, V=50. Distinct units require scaled or relative interpretation."},
         "timingSeconds":{"upstreamBaseline":upstream["seconds"],"csharpBaseline":native["seconds"],"controlledCsharp":diagnostic["seconds"]},
         "buildScope":"Core and Engine were successfully compiled in this task. A later project rebuild encountered concurrent unrelated Interferogram source errors; retained that log and compiled the probe alone against the successful binary set. Results bind to the exact binary SHA256, not an assertion that all current concurrent workspace edits compile.",
         "upstreamCommit":subprocess.check_output(["git","rev-parse","HEAD"],cwd=source,text=True).strip(),"upstreamTrackedSourcesUnchanged":True,
         "upstreamSourceSha256":{relative:hashlib.sha256((source/relative).read_bytes()).hexdigest() for relative in code_files},
         "binaries":native["binaries"],"productNumericCodeChanged":False,"longTrainingRemainsStopped":True,
         "frozenRuntime":r"D:\optical system design\tmp\autolens-multiprescription-runtime",
         "earlierAssessmentToolCorrection":"Earlier EndpointComparison.ImportOriginal only imported mechanical semi-diameters and omitted physical apertures. It is now corrected with explicit Core SetRadius calls for every physical row. Old raw evidence is retained; corrected saved-optic assessment is stored separately in autolens-full-comparison/available-results-comparison/common-analysis-corrected-apertures. First-order focal and training-objective comparisons have independent provenance.",
         "earlierAssessmentCorrectionVerification":json.loads((ROOT.parent/'autolens-full-comparison/available-results-comparison/aperture-correction-check.json').read_text(encoding='utf-8')),
         "remainingScope":["No complete paired refinement endpoints or multi-seed success rates","Centered front-stop spherical/even-aspheric prescriptions only; no full CAMERA_CONFIG topology","Same float64 and fixed inputs isolate mathematics; original float32/Philox long-training differences remain","VJPs and scalar loss gradients cover every optimizer coordinate; not a full per-ray Jacobian in every case"]}
(ROOT/"summary.json").write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

def sci(value): return f"{value:.3g}"
table="\n".join(f"| {row['name']} | {row['parameters']} | {row['rays']} | {'一致' if row['cpuAgrees'] else '差异/拒绝'} | {'一致' if row['gpuAgrees'] else '差异/拒绝'} | {sci(row['maximumGradientNormalizedError']) if not row['errors'] else '奇异域拒绝'} |" for row in by_case)
report=f"""# AutoLens 多处方、全参数梯度与临界情况直接对照

本轮只做耗时短的固定输入验证，未恢复训练、未执行优化更新或完整精修。使用未修改的原版 e58 数值函数、相同处方/输入光线、双方 float64，C# 分别执行 CPU 与真实 CUDA。

## 结论

**7 组常规处方的追迹、有效性、两种图像损失、几何/材料约束、CRA/弯折正则及完整目标的全部优化参数梯度相符；仍存在三处可复现的边界行为差异。** 不将带差异的临界用例算作通过，不将普通区域接近扩大为所有处方等价。

共有 **{summary['distinctPrescriptionCount']} 种不同处方、{len(by_case)} 个输入场景、{summary['distinctFixedImageRays']} 条固定图像光线**，CPU/CUDA 各请求一遍，奇异点请求的拒绝另计。常规六片处方每组 145 个参数（含 n/V），目录材料处方 133 个，小透镜边界例 25 个。包含所有轴向位置（含像面）、曲率、圆锥、r⁴–r¹⁸，以及可变材料 n/V。10 个标量探针含两种图像损失、几何/材料约束、固定输入 CRA/弯折、两个完整训练目标和四个不同加权输出组合的梯度；不是逐光线完整 Jacobian 的全面认证。

## 常规处方结果

- 两个随机初始化种子、两组非零高阶非球面、激活材料/间隙约束的处方、已保存的原版第一阶段及部分精修目录处方。
- CPU/CUDA 共 14 次评价，**掩码差异 0**；最大落点差 **{sci(ordinary_stats['maximumPositionAbsoluteErrorMm'])} mm**。
- 10 个标量探针的值最大归一化误差 **{sci(ordinary_stats['maximumValueNormalizedError'])}**；全部参数梯度最大归一化误差 **{sci(ordinary_stats['maximumGradientNormalizedError'])}**。
- 常规组共比较 **{ordinary_stats['gradientSlotsCompared']} 个标量参数梯度项**。高阶系数梯度单位与量级不同，原始绝对梯度误差不能跨参数直接比较；同时保留按自然参数尺度计算的误差，最大 **{sci(ordinary_stats['maximumGradientScaledError'])}**。
- 对固定加权输出组合 X + 0.37Y + 0.1方向Z + 0.01弯折，21 个中心差分方向同时扰动每组全部参数，掩码均稳定。方向导数绝对误差最大 **{sci(ordinary_stats['maximumFiniteDifferenceAbsoluteError'])}**，相对误差最大 **{sci(ordinary_stats['maximumFiniteDifferenceRelativeError'])}**；最大相对误差对应很小的方向导数，不能忽略绝对量级。不把该差分称为对含 detach 权重的整个图像目标进行差分。
- 原版本轮未出现需要被 NaN/Inf 清零的梯度项。

## 三处边界差异

1. **平面光阑的精确边缘。** 原版 `Plane.intersect` 使用 `x²+y² < r²`，共享 Core 的圆形物理口径包含边界并有数值容差。在 132 条测试光线中，原版 60 条、C# 84 条有效；相差的 24 条全都恰好位于 ±r。距离边缘 ±1e-10/±1e-7 的其他测试点判断一致。由于有效光线集合变化，该用例的损失与梯度也随之变化。这是有效性约定差异，不能描述成普通像质计算错误或忽略。

2. **极接近全反射临界点但仍可折射。** 对相对临界高度差最小 1e-10 的用例，双方掩码一致，但最大归一化梯度差约 **{100*case_map['critical-near-below']['maximumGradientNormalizedError']:.4f}%**，完整训练目标梯度最大约 **{100*near_total:.4f}%**。原版折射用 `sqrt(k + 1e-12)`；Core 使用标准物理根并做奇异域保护。受控归因实验仅在运行时暂把原版 base 模块 EPSILON 改成 0，随后恢复，源码未改；该临界用例最大梯度差降为 **{sci(diagnostic_max)}**，说明稳定项是主要来源。受控结果单独保存，绝不替代原版基线。剩余差异在此病态区域仍受浮点/寻根影响，未声称完全归因或逐位相同。

3. **恰好包含临界折射点的批次。** 原版本例继续给出有限稳定化结果；Core CPU/CUDA 固定输入求导显式报告奇异导数，一条此类光线可使整批请求失败。这是实际运行行为差异，不仅是小数位误差。本轮未改变 Core 的物理定义或把异常算作通过。

临界点以下留有较大余量、临界点以上被全反射剔除、球面轮廓擦边/域外及全部无效光线组，也均保留在下表。它们在本次输入下双方一致。

| 用例 | 参数数 | 图像光线 | CPU 对原版 | CUDA 对原版 | 最大梯度归一化差 |
| --- | ---: | ---: | --- | --- | ---: |
{table}

## 范围与可复查性

产品数值代码未修改。本次正常比较采用 max(1, |参考值|) 归一化，值阈值 5e-8、梯度阈值 5e-6 在最终测量前即已写入工具；临界差异没有放宽阈值遮盖。所有原始值、逐参数梯度、最差项、有效性差异、实际 GPU 后端、二进制与输入 SHA256 均保留。

Python 直接调用原始追迹/损失/约束函数；CRA/弯折仅在采样边界注入固定光线以隔离随机数，未替换物理公式。归因实验对原版模块常量的临时修改有显式标记，不算原版基线。受 Git 跟踪的原版数值源码检查保持未修改。

本轮 Core/Engine 首次构建成功。稍后全项目引用重编译遇到其他并行任务尚未完成的 Interferogram 源码错误，保留失败记录；验证工具单独编译，使用本轮已成功构建的同一数值库，两次实验的 SHA256 已核对一致。不能据此声称当前工作树所有并行改动均构建通过，也没有跑全仓测试。初轮验证工具的临界输入整数类型、物理口径映射及空参数差分调用错误均保留，未计入最终结果。

仍不覆盖完整默认精修终点、多种子成功率、原版 float32/Philox 训练差异或完整 CAMERA_CONFIG 拓扑。无需为上述短程检查重跑任何完整精修。

另发现之前的处方评价工具仅导入机械/显示半口径，没有为原版设定实际物理口径。本轮已修正 `EndpointComparison.ImportOriginal`，逐行调用 Core 的口径设置函数，旧证据保留，修正后的已保存处方评价另存 `autolens-full-comparison/available-results-comparison/common-analysis-corrected-apertures`。已核实原版全部 13 个物理口径与 JSON 相同。对原先两份冻结处方、相同采样补算后，表内指标与逐视场 RMS/最大半径/80% 半径的变化均为 0，故此前那组光斑/采样有效率数字在这些样本下得到重新确认；这不代表缺漏可以忽略或对其他处方无影响。原报告及机器数据已加更正记录。此工具遗漏不与上表三处原版/Core 边界差异混为一谈。

[机器汇总](summary.json) · [对照图](comparison.png) · [原版基线](upstream.json) · [C# 原始结果](csharp.json) · [稳定项归因原版](upstream-epsilon-diagnostic.json) · [归因对照](csharp-epsilon-diagnostic.json)
"""
(ROOT/"README.md").write_text(report,encoding='utf-8')
print(json.dumps({"ordinary":ordinary_stats,"substantiveBoundaryDifferences":summary['substantiveBoundaryDifferences'],
                  "cpuGpuMaxGradientDifference":max(row['maximumGradientNormalizedDifference'] for row in gpu_comparisons)},ensure_ascii=False,indent=2))
