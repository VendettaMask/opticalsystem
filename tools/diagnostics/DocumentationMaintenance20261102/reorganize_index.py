"""One-time index migration; retains the original history links and adds unindexed historical reports."""
import json
import os
import pathlib
import re
import urllib.parse

root = pathlib.Path.cwd()
old = (root / "docs/README.md").read_text(encoding="utf-8-sig")
history = old.split("## 历史报告与阶段提案", 1)[1]
target = root / "docs/history/README.md"
if target.exists():
    raise RuntimeError("History index already exists; review it in place instead of overwriting.")

def relocate(match):
    label, link = match.group(1), match.group(2)
    if re.match(r"[a-zA-Z][a-zA-Z0-9+.-]*:", link) or link.startswith("#"):
        return match.group(0)
    file, sep, anchor = link.partition("#")
    resolved = root / "docs" / urllib.parse.unquote(file)
    relative = pathlib.Path(os.path.relpath(resolved, target.parent)).as_posix()
    return f"[{label}]({relative}{sep}{anchor})"

history = re.sub(r"\[([^\]]+)\]\(([^)\s]+)\)", relocate, history)
before = json.loads((root / "artifacts/validation/documentation-maintenance-20261102/before.json").read_text(encoding="utf-8-sig"))
catalog = json.loads((root / "docs/documentation-lifecycle.json").read_text(encoding="utf-8"))
active = set(catalog["ActivePlans"])
historic = {e["Path"] for e in before["Inventory"] if e["Category"] == "Historical"} | set(catalog["HistoricalDocuments"])
extra = []
for name in sorted(historic):
    p = root / name
    if p.parent != root / "docs" or p.suffix != ".md" or name in active or not p.exists():
        continue
    if f"../{p.name})" in history:
        continue
    title = next((l.lstrip("# ") for l in p.read_text(encoding="utf-8-sig").splitlines() if l.startswith("# ")), p.stem)
    extra.append(f"- [{title}](../{p.name})")
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text("# 历史文档索引\n\n整理：2026-11-02。此处保存有来源、独有记录或实际消费者的阶段报告，不作为当前操作或继续执行旧任务的授权。日期或冻结标签本身不是保留理由；重复且已被完整替代的材料仍应按维护记录退役。当前能力见[现行索引](../README.md)、[状态页](../CURRENT_STATUS.md)和[维护规则](../DOCUMENTATION_MAINTENANCE.md)。\n\n## 原分组历史入口\n" + history + "\n\n## 从原现行区移入及补齐的阶段报告\n\n" + "\n".join(extra) + "\n", encoding="utf-8")

current = """# 项目文档索引

核对：2026-11-02。先按现行指南操作；历史通过数、旧独立模块命令及内部对齐目标不作为当前验收。各阶段实际结果见[当前状态](CURRENT_STATUS.md)，旧记录见[历史索引](history/README.md)。

## 从这里开始

| 任务 | 入口 |
| --- | --- |
| 使用产品 | [用户指南](USER_GUIDE.md)、[桌面工作流](GUI_QUICKSTART_REFACTOR.md)、[能力矩阵](PARITY_MATRIX.md) |
| 构建、测试与安装 | [构建发布](BUILD_AND_RELEASE.md)、[Windows 安装/恢复](WINDOWS_INSTALLATION_UPGRADE_2026-10-12.md)、[云发布](GITHUB_CLOUD_RELEASE.md)、[Mac OTA](MAC_OTA.md) |
| 核对精度与未关闭问题 | [数值验收规则](NUMERICAL_PARITY.md)、[问题清单](OPEN_ISSUES_2026-10-08.md)、[95 示例分类交接](EXAMPLE_ACCURACY_HANDOFF_2026-11-01.md) |
| 维护实现 | [架构](ARCHITECTURE.md)、[代码地图](CODE_READING_MAP.md)、[UI 规范](UI_DESIGN_SPEC.md)、[协作约束](../AGENTS.md) |
| 维护文档 | [文档生命周期与检查](DOCUMENTATION_MAINTENANCE.md)、[历史报告索引](history/README.md) |

## 当前指南与功能契约

### 计算、工程与交付

- [计算指南](COMPUTATION_GUIDE.md)、[数学附录](COMPUTATION_FORMULAS.md)、[目录覆盖](COMPUTATION_COVERAGE.md)
- [格式和插件](FILE_FORMATS_AND_PLUGINS.md)、[STAROPT 版本](STAROPT_FILE_FORMAT.md)、[操作数支持](ZEMAX_OPERAND_SUPPORT.md)
- [优化算法](OPTIMIZATION_ALGORITHMS.md)、[公差](TOLERANCING.md)、[制造图纸](MANUFACTURING_DRAWINGS.md)
- [设计报告](MARKDOWN_REPORT_EXPORT.md)、[内置模块交付](BUILT_IN_MODULES_2026-10-13.md)

### 初始结构、镀膜与相机

- [初始结构当前范围](INITIAL_STRUCTURE_LAB_PLAN.md)、[内置模块操作](../labs/InitialStructure/README.md)、[GPU 支持域](AUTOLENS_GPU.md)
- [镀膜计算契约](COATING_DESIGN_LAB.md)、[膜系编辑/光谱/堆栈/停靠操作](../labs/CoatingDesign/README.md)
- [相机与 NSC→RAW/ISP](CAMERA_IMAGING_LAB.md)、[单色/多波长局部斜边 SFR](MONOCHROME_SLANTED_EDGE_SFR.md)
- [非序列工作流](NON_SEQUENTIAL_TRACING.md)、[会话/数据库与杂散光](NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md)、[光源](NONSEQUENTIAL_SOURCES.md)
- [光学装调](OPTICAL_ASSEMBLY_LAB.md)、[后续非序列路线图](NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md)

### 正在推进的计划

- [初始结构搜索阶段计划](INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md)：按最新进度区分已交付、暂缓及仍未实现项。
- [Ansys 场景斜边与 ROM/黑盒计划](ANSYS_SCENE_SFR_IMPLEMENTATION_PLAN_2026-11-02.md)：现有局部 PSF/SFR 不代表该物方场景工作流已经完成。

已完成的镀膜停靠计划、M0–M6 首版拓扑计划及其他阶段验收放在历史索引，现行操作分别由模块指南维护。AutoLens 上游对齐只作来源与兼容证据，不再作为产品开发目标。

<details>
<summary>全部现行专门指南</summary>

"""
guides = {e["Path"] for e in before["Inventory"] if e["Category"] == "Guide"}
guides -= set(catalog["HistoricalDocuments"]) | {e["Path"] for e in catalog["RetiredDocuments"]}
for name in sorted(guides):
    p = root / name
    if p.parent != root / "docs" or p.suffix != ".md" or p.name == "README.md" or not p.exists():
        continue
    title = next((l.lstrip("# ") for l in p.read_text(encoding="utf-8-sig").splitlines() if l.startswith("# ")), p.stem)
    current += f"- [{title}]({p.name})\n"
current += """
</details>

## 模块、工具和样例入口

- [初始结构](../labs/InitialStructure/README.md)、[镀膜设计](../labs/CoatingDesign/README.md)、[相机样例](../samples/camera-imaging/README.md)
- [顺序镜头样例](../samples/lenses/README.md)、[非序列样例](../samples/non-sequential/README.md)、[材料数据](../src/OpticalSystemDesign.Core/Materials/Data/README.md)
- [Zemax/Workbench 比较工具](../tools/OpticalSystemDesign.ZemaxComparison/README.md)、[原生采集工具](../tools/zemax_parity/README.md)、[参考与准备输入核验](../tools/validation/README.md)

验证工具不进入产品运行依赖；普通构建不启动 Zemax。

## 历史报告与阶段提案

[完整历史索引](history/README.md)保留原分组和补齐的阶段报告。旧材料是否保留取决于实际消费者、独有证据与替代情况；不能因标为 frozen 自动永久保留，也不能仅因日期较早删除仍在使用的参考。
"""
(root / "docs/README.md").write_text(current, encoding="utf-8")
