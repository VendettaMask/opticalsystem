$docRoot = (Get-Location).Path
function Edit-Doc([string]$relative, [scriptblock]$edit) {
    $path = Join-Path $docRoot $relative
    $text = Get-Content -Raw -LiteralPath $path
    $text = & $edit $text
    Set-Content -LiteralPath $path -Value $text -NoNewline -Encoding utf8
}
Edit-Doc 'docs/STAROPT_FILE_FORMAT.md' {
 param($t)
 $t.Replace('**2 / 最大 9 / 6**','**2 / 最大 10 / 6**').Replace('声明实测 BSDF 的工程（含禁用对象）写 v9','声明商业格式或固定轴 BSDF 的工程（含禁用对象）写 v10，原各向同性 CSV v1 实测 BSDF 写 v9').Replace('v1–9','v1–10').Replace('拒绝 v8/v9','拒绝不支持的 v8/v9/v10').Replace('模型及兼容边界见 [实测 BSDF/BTDF](MEASURED_BSDF_BTDF_2026-10-31.md)','模型及兼容边界见 [商业 BSDF 与固定轴](COMMERCIAL_BSDF_AXES_2026-10-31.md)').Replace('当前工程负载最大版本为 9','当前工程负载最大版本为 10').Replace('v9 内嵌实测 BSDF 表格、文件名与原文件/规范数据摘要；仅实测模型使用 v9。','v10 保存商业格式转换后的 BSDF、固定材料坐标/局部轴、来源格式与转换说明；低版本夹带新模型会拒绝。v9 内嵌原各向同性 CSV v1 实测 BSDF 表格、文件名与原文件/规范数据摘要。').Replace('负载按实测/Lambertian/无 BSDF 选择 v9/v8/v7','负载按商业或固定轴/原实测 v1/Lambertian/无 BSDF 选择 v10/v9/v8/v7')
}
Edit-Doc 'docs/FILE_FORMATS_AND_PLUGINS.md' {
 param($t)
 $t.Replace('更新：2026-10-26。','更新：2026-10-31。').Replace('非序列实测 BSDF/BTDF 使用带角度/单位声明的自有 CSV，内嵌到 STAROPT v9；只有 Lambertian 时仍写 v8。','非序列 BSDF/BTDF 支持自有 CSV v1/v2 和 Speos v8 TEXT 的 .brdf/.anisotropicbsdf；商业或固定轴数据写 STAROPT v10，原 CSV v1 写 v9，只有 Lambertian 时仍写 v8。').Replace('尚无商业私有 BSDF、ASTM 或 Radiance XML 的直接导入。','商业格式需要显式入射侧声明，固定轴使用对象局部坐标；完整子集与转换说明见[商业 BSDF 与固定轴](COMMERCIAL_BSDF_AXES_2026-10-31.md)。尚无 Speos 二进制/强度、Zemax .BSDF、XYZ 光度、ASTM 或 Radiance XML 的直接导入。')
}
Edit-Doc 'docs/CAMERA_IMAGING_LAB.md' {
 param($t)
 $t.Replace('已接入实测 CSV 网格 BSDF/BTDF；固定材质轴各向异性、商业文件直接导入和相干路径仍未实现。','已接入实测 CSV 网格与 Speos v8 文本 BSDF/BTDF、固定材料轴各向异性；相干路径仍未实现。').Replace('散射模型、文件约定、采样与能量验收见[专项说明](MEASURED_BSDF_BTDF_2026-10-31.md)','散射模型、文件约定、采样与能量验收见[专项说明](COMMERCIAL_BSDF_AXES_2026-10-31.md)').Replace('实测 CSV BSDF/BTDF 已接通，继续各向异性/商业格式与独立光度/色度验收','实测 CSV/Speos v8 TEXT BSDF/BTDF 与固定材料轴已接通，继续其他商业格式与独立光度/色度验收').Replace('已接入对象级 Lambertian 及实测 CSV BSDF/BTDF','已接入对象级 Lambertian、实测 CSV/Speos v8 文本 BSDF/BTDF 与固定材料轴').Replace('冻结完整表格、文件/数据摘要及筛选前全追迹 R/T 统计','冻结完整表格、文件/数据摘要、局部/世界材料轴及筛选前全追迹 R/T 和材料方向统计').Replace('固定材质轴各向异性、商业 BSDF 格式直接导入、体散射','其他商业 BSDF 格式（现支持 Speos v8 TEXT 两类）、体散射')
}
Edit-Doc 'docs/NON_SEQUENTIAL_TRACING.md' {
 param($t)
 $t.Replace('[实测 BSDF CSV](MEASURED_BSDF_BTDF_2026-10-31.md)','[实测 CSV 与 Speos v8 文本 BSDF](COMMERCIAL_BSDF_AXES_2026-10-31.md)').Replace('保存光谱/角度分布与原文件摘要','保存光谱/角度分布、原文件摘要及转换说明；商业文件显式指定入射侧，固定材料轴以对象局部 XYZ 定义并随完整参考坐标链旋转')
}
Edit-Doc 'docs/NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md' {
 param($t)
 $t.Replace('当前仍未实现：STEP/IGES/SAT导入、CAD面映射、布尔对象、各向异性/任意散点/商业 BSDF 格式直接导入','当前已增加[Speos v8 TEXT 两类格式与固定材料轴](COMMERCIAL_BSDF_AXES_2026-10-31.md)，接入同一 NSC→RAW/ISP，使用 MeasuredBsdfGrid/2。当前仍未实现：STEP/IGES/SAT导入、CAD面映射、布尔对象、任意散点及其他商业 BSDF 格式直接导入')
}
Edit-Doc 'docs/NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md' {
 param($t)
 $t.Replace('固定材质轴各向异性、商业 BSDF 格式与商业数值对标仍未完成','[固定材料轴与 Speos v8 文本两类格式](COMMERCIAL_BSDF_AXES_2026-10-31.md)已实现，其他商业格式与商业数值对标仍未完成').Replace('镀膜、各向异性/商业 BSDF 格式、逐面覆盖','镀膜、其他商业 BSDF 格式、逐面覆盖').Replace('对象级 Lambertian 及实测光谱/角度 BSDF/BTDF 多次散射','对象级 Lambertian、实测光谱/角度及固定轴 BSDF/BTDF 多次散射').Replace('后续增加逐面覆盖、金属及多层膜、Gaussian、ABg、各向异性和商业文件直接导入','已增加 Speos v8 TEXT 两类格式及固定轴各向异性；后续增加逐面覆盖、金属及多层膜、Gaussian、ABg 和其他商业文件导入')
}
Edit-Doc 'docs/README.md' {
 param($t)
 $t.Replace('- [实测 BSDF 文件、插值、采样与 BTDF](MEASURED_BSDF_BTDF_2026-10-31.md)', '- [实测 BSDF 文件、插值、采样与 BTDF](MEASURED_BSDF_BTDF_2026-10-31.md)' + [Environment]::NewLine + '- [商业 BSDF 文本、固定材料轴与相机链路](COMMERCIAL_BSDF_AXES_2026-10-31.md)')
}
Edit-Doc 'docs/MEASURED_BSDF_BTDF_2026-10-31.md' {
 param($t)
 $t.Replace('## 当前格式与操作','本阶段记录自有 CSV v1 的实现与原始 132 项验证。后续已增加 Speos v8 两类文本格式与固定材料轴，当前入口为“导入 BSDF 文件”，见[后续阶段](COMMERCIAL_BSDF_AXES_2026-10-31.md)；下文保留原阶段范围。' + [Environment]::NewLine + [Environment]::NewLine + '## 本阶段格式与操作')
}
