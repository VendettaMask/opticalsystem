"""Summarize identical-input before/after measurements and scoped regression TRX."""
import hashlib
import json
from pathlib import Path
import statistics
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent


def load(name):
    return json.loads((ROOT / name).read_text(encoding="utf-8-sig"))


def summarize(before_name, after_name):
    before, after = load(before_name), load(after_name)
    a = [p for p in before["Profiles"] if not p["Warmup"]]
    b = [p for p in after["Profiles"] if not p["Warmup"]]
    reference = before["Profiles"][0]
    median_before = statistics.median(p["TotalSeconds"] for p in a)
    median_after = statistics.median(p["TotalSeconds"] for p in b)
    return {
        "before": before_name, "after": after_name, "raysPerUpdate": before["RaysPerUpdate"], "parameters": before["Parameters"],
        "inputSha256Matches": before["InputSha256"] == after["InputSha256"],
        "samplingAndParameterCountsMatch": before["RaysPerUpdate"] == after["RaysPerUpdate"] and before["Parameters"] == after["Parameters"],
        "beforeMedianSeconds": median_before, "afterMedianSeconds": median_after, "speedup": median_before / median_after,
        "beforeAllocatedBytes": statistics.median(p["AllocatedBytes"] for p in a),
        "afterAllocatedBytes": statistics.median(p["AllocatedBytes"] for p in b),
        "exactNominalValuesAndMasksMatch": all(p["RayValues"] == reference["RayValues"] for p in before["Profiles"] + after["Profiles"]),
        "maximumLossAbsoluteDifference": max(abs(p["Loss"]-reference["Loss"]) for p in after["Profiles"]),
        "maximumGradientAbsoluteDifference": max(abs(x-y) for p in after["Profiles"] for x,y in zip(reference["Gradient"], p["Gradient"])),
        "maximumGradientNormalizedDifference": max(abs(x-y)/max(1,abs(x)) for p in after["Profiles"] for x,y in zip(reference["Gradient"], p["Gradient"])),
        "beforeCoreSha256": before["CoreSha256"], "afterCoreSha256": after["CoreSha256"],
        "afterBackend": after["Profiles"][1]["Images"]["Backend"],
        "warmupExcluded": True, "measuredRepetitionsPerConfiguration": len(a),
    }


cases = {"curriculum": summarize("curriculum-before-final.json", "curriculum-after-final.json"),
         "refinementSizedCatalog": summarize("refinement-before.json", "refinement-after.json")}
tests = []
for name in ["core-final.trx", "reference-final.trx"]:
    path = ROOT / name
    if path.exists():
        tree = ET.parse(path)
        ns = {"t":"http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
        tests.append({"file":name, "counters":tree.find(".//t:Counters",ns).attrib})
passed = all(c["inputSha256Matches"] and c["samplingAndParameterCountsMatch"] and c["exactNominalValuesAndMasksMatch"]
             and c["maximumLossAbsoluteDifference"] < 1e-12 and c["maximumGradientNormalizedDifference"] < 1e-10 for c in cases.values())
summary = {
    "date": "2026-10-26", "device": "NVIDIA GeForce RTX 5060 Laptop GPU", "dtype": "double throughout product Core",
    "scope": "Two default-sized single-update workloads at one frozen six-element training snapshot; not complete 5002-update timing or final optical quality",
    "referenceSnapshot": "Existing pre-change curriculum checkpoint at update 34; refinement-sized workload uses its catalog projection, not a claimed fully trained endpoint",
    "measurements": cases, "numericalChecksPassed": passed, "tests": tests,
    "algorithmChanges": "None to optimizer schedules, sample counts, objectives, materials, precision or independent physical acceptance",
    "implementationChanges": ["CUDA fixed-input directional derivatives along authoritative shared-Core nominal hits",
        "Bounded reuse of graph storage and CPU/CUDA working buffers for the retained recorded path", "Evaluation-local binding/profile preparation"],
    "limits": ["Timing is observed for this machine and snapshot only", "Original Python is temporarily suspended during each timed invocation and resumed in finally",
        "Cold compilation is recorded but excluded from the warm median", "Single-seed full-run comparison remains separate"],
    "evidenceSha256": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in ROOT.iterdir() if p.is_file()
                       and p.name not in {"performance-summary.json", "README.md"}},
}
(ROOT / "performance-summary.json").write_text(json.dumps(summary,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
rows = []
for name, c in cases.items():
    label = "课程默认规模" if name == "curriculum" else "精修默认规模（目录材料）"
    rows.append(f"| {label} | {c['raysPerUpdate']:,} | {c['parameters']} | {c['beforeMedianSeconds']:.3f} s | {c['afterMedianSeconds']:.3f} s | {c['speedup']:.2f} 倍 |")
test_lines = "\n".join(f"- `{t['file']}`：{t['counters']['passed']} 通过、{t['counters']['failed']} 失败、{t['counters']['notExecuted']} 未执行。" for t in tests)
text = f"""# AutoLens 耗时修复与实测（2026-10-26）

默认手机目标的同输入单步测量显示，固定光线的导数由 CPU 逐条建记录改为 CUDA 成批执行后，训练和精修规模均有明显提速。这个结果不是完整训练时间或最终像质比较。

| 工作负载 | 每步光线数 | 参数数 | 修改前预热中位数 | 修改后预热中位数 | 比值 |
| --- | ---: | ---: | ---: | ---: | ---: |
{chr(10).join(rows)}

每配置三次观测，第一次包含首次编译等预热工作，表中为后两次的中位数。旧引擎为修改前冻结的 Core DLL，SHA-256 记于机器账目；前后调用同一性能工具、同一输入状态、同一采样规则。测速时通过 `finally` 保护的暂挂/恢复隔离本任务原版 Python 的资源竞争。不能将这个比值直接乘到所有完整设计上。

修改前，课程/精修规模每步累计分配约 {cases['curriculum']['beforeAllocatedBytes']/1e9:.2f}/{cases['refinementSizedCatalog']['beforeAllocatedBytes']/1e9:.2f} GB 临时内存；修改后约 {cases['curriculum']['afterAllocatedBytes']/1e9:.2f}/{cases['refinementSizedCatalog']['afterAllocatedBytes']/1e9:.2f} GB。它们是累计分配量，不是峰值显存。

## 改动及计算边界

- 原先 CPU 为每条光线构造运算记录，GPU 每批仅反传这些记录；反复分配节点、CPU/GPU 缓冲和参数扫描占据大量时间。
- 有限物距固定输入现在使用 `cuda-fixed-input-forward-v1`，CUDA 按光线与参数并行执行方向导数。交点、方向、裁光及材料来自现有共享 Core 的正式传播结果；每面继续核对导数值与正式结果，并保留奇异域、扩展数值范围、取消及工作集限制。没有增加另一套寻根或物理追迹器。
- GPU 输出为 X、Y、方向 Z、累计弯折惩罚四项的值和全部参数导数。`GpuRays` 延续已完成有效光线导数的计数；无效光线保持显式掩码。即使一个 CUDA 批次没有有效输出，也保留其真实批次证据。
- 历史隐式光阑瞄准及相对厚度参数保留原来的六输出反传路径，增加有界内存复用。CPU 算法、double 精度、Adam、完整计划、默认采样、目录和独立硬验收门槛不变。
- 旧状态与原后端记录保留原身份，新工作记录实际后端，不改写历史工作证据。

## 数值与回归

两种规模下全部光线的标量输出及有效性掩码与旧引擎逐项完全相同，损失完全相同；全部参数梯度的最大归一化差分别为 {cases['curriculum']['maximumGradientNormalizedDifference']:.3g} 和 {cases['refinementSizedCatalog']['maximumGradientNormalizedDifference']:.3g}。归一化分母为 max(1, abs(旧梯度))；不宣称梯度逐位一致。

{test_lines}

回归覆盖 CPU 独立有限差分、真实 CUDA、复用后的不同参数/批次布局、非零非球面全部输出与导数、无效光线不冒充有效输出、小规模完整两阶段、状态后端保存、原版冻结规则、预算、取消和恢复。中间工作区剪贴板接口编译失败及本轮接口适配失败日志保留，以最终 TRX 为准；没有跑全仓库测试或发布安装包。

性能输入为先前 C# 长跑第 34 次更新的冻结处方；精修规模只为测试工作负载而投影目录玻璃，未冒充完成训练的终点。修改前长跑已经按用户“先解决耗时”要求停止并保留状态。原版完整运行仍独立保留，完整最终结果比较另记。

[机器账目](performance-summary.json) · [冻结输入](fixed-state.json) · [核心回归](core-final.trx) · [参考流程回归](reference-final.trx)
"""
(ROOT/"README.md").write_text(text,encoding="utf-8")
print(json.dumps({"numericalChecksPassed":passed,"measurements":cases,"tests":tests},ensure_ascii=True))
if not passed:
    raise SystemExit(1)
