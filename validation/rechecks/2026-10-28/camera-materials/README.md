# 多材质反射率分区验证

范围和当前结论见[机器账目](../../../../docs/validation/CAMERA_MATERIALS_2026-10-28.json)与[实验室说明](../../../../docs/CAMERA_IMAGING_LAB.md#多材质反射率分区)。本目录记录本机后台定向测试，不代表全库、原生交互或外部数值认证。

- `materials-final.trx`：当前 60 项通过，包含新增 28 项。
- `materials-export-final.trx`：9 项重叠的导出/模式及 JSON 复验。
- `baseline.trx`：原连续模型 8 项基线，不另计通过数。
- `materials-numeric.trx`：首轮 42/44，静态 PNG 帧数判断的两个失败保留。
- `materials-import-ui.trx`：修复后 16/16，与最终集合重叠。
- `build-first.log`：早期颜色选择器歧义及并行报告编译错误；`build-final.log` 是默认 Release 输出的最终记录。
- `numeric/`：独立公式、边界卷积、共享镜头线性对照和可再次导入的 `materials-AB.zip`。
- `renders/`：明暗主题离屏分区、曲线、参数和 A/B 图，以及本轮回归时生成的已有相机图。

DeepSeek 仅在本地准备审查预览。任何代码传输必须按用户给出的规则取得明确授权；未授权不发送。
