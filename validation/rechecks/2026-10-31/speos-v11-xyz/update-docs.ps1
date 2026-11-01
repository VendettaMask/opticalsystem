$ErrorActionPreference='Stop'
function Edit([string]$path,[scriptblock]$edit){$absolute=Join-Path (Get-Location) $path;$text=[IO.File]::ReadAllText($absolute);$text=&$edit $text;[IO.File]::WriteAllText($absolute,$text)}
Edit 'docs/CURRENT_STATUS.md' {param($s)
$section=Get-Content -Raw validation/rechecks/2026-10-31/speos-v11-xyz/status-section.md
$s=$s.Insert($s.IndexOf("`n"),$section)
$s=$s.Replace('Speos v11/未知布局、活动增强/White Specular、XYZ 光度及独立 Zemax R/T 双文件合并仍未支持；没有据格式回归宣称商业数值一致。下方阶段保留各自原始验证范围。','该批 191 项冻结时尚无 v11、活动增强/White Specular 或 XYZ；后续实现见上方新阶段，独立 Zemax R/T 双文件合并仍未支持。没有据格式回归宣称商业数值一致。下方阶段保留各自原始验证范围。')
$s=$s.Replace('XYZ 光度、ASTM/Radiance XML、逐面覆盖、体散射、偏振、相干和 MIS 仍未实现。','该阶段不含 XYZ；后续校准 XYZ 见上方新阶段。ASTM/Radiance XML、逐面覆盖、体散射、偏振、相干和 MIS 仍未实现。')
$s}
Edit 'docs/STAROPT_FILE_FORMAT.md' {param($s)
$s=$s.Replace('2 / 最大 11 / 6','2 / 最大 12 / 6').Replace('读取支持 v1–11','读取支持 v1–12').Replace('当前工程负载最大版本为 11（读取 v1–11）。','当前工程负载最大版本为 12（读取 v1–12）。v12 保存原生镜面峰/锥/White Specular、Speos 可分光谱及校准 XYZ 原始通道和校准来源；这些字段进入 MeasuredBsdfGrid/4 摘要。').Replace('工程负载v1–11','工程负载v1–12').Replace('v8/v9/v10/v11，','v8/v9/v10/v11/v12，')
$s=$s.Replace('声明投影 BSDF 或 Zemax 镜面中心表格的工程','声明镜面增强、Speos 可分光谱或校准 XYZ 的工程（含禁用对象）写 v12；仅声明投影 BSDF 或 Zemax 镜面中心表格的工程')
$s=$s.Replace('负载按投影或 Zemax/原商业文本或固定轴/原实测 v1/Lambertian/无 BSDF 选择 v11/v10/v9/v8/v7。','负载按增强或校准 XYZ/投影或 Zemax/原商业文本或固定轴/原实测 v1/Lambertian/无 BSDF 选择 v12/v11/v10/v9/v8/v7。')
$s=$s.Replace('[二进制与 Zemax BSDF](BINARY_ZEMAX_BSDF_2026-10-31.md)','[v11、镜面增强与校准 XYZ](SPEOS_V11_XYZ_2026-10-31.md)')
$s}
Edit 'docs/FILE_FORMATS_AND_PLUGINS.md' {param($s)
$old=($s-split"`n"|Where-Object {$_-like'非序列 BSDF/BTDF 支持自有*'}|Select-Object -First 1)
$new='非序列 BSDF/BTDF 支持自有 CSV v1/v2、Speos v8/v11 TEXT、已核验 archive-3 的 .brdf v8 与 .anisotropicbsdf v7/v9/v11 二进制，以及显式声明的 Zemax Monochrome 或校准 XYZ .BSDF。v11 可分光谱、镜面增强/White Specular 和 XYZ 使用 MeasuredBsdfGrid/4、STAROPT v12；原投影/Zemax、商业文本、实测 v1 和 Lambertian 继续写各自旧版本。STARRDB 按需记录协议及类型化 R/T 散射事件，相机冻结原始通道/角表/光谱、锥、校准及摘要，空新增字段不改旧指纹。商业文件需显式入射侧，固定轴使用对象局部坐标。XYZ 必须另选非负辐射度校准 JSON，不能直接把 Y 当功率；镜面采用已声明的原生配准，尚无商业逐值认证。格式、预算和活动布局边界见[v11/增强/XYZ 契约](SPEOS_V11_XYZ_2026-10-31.md)；未知二进制、活动 v8/v9、Retroreflection、ASTM 与 Radiance XML 仍未实现。'
$s.Replace($old,$new)}
Edit 'docs/CAMERA_IMAGING_LAB.md' {param($s)
$s=$s.Replace('Zemax 单色辐射度 .BSDF','Zemax 单色辐射度及校准 XYZ .BSDF').Replace('Zemax 单色 BSDF/BTDF','Zemax 单色/校准 XYZ BSDF/BTDF').Replace('Speos v8 文本与已核验二进制','Speos v8/v11 文本、镜面增强/White Specular 与已核验二进制').Replace('现支持 Speos v8 TEXT、部分二进制版本及 Zemax 单色辐射度','现支持 Speos v8/v11 TEXT、已核验二进制、原生镜面配准/White Specular 及 Zemax 单色/校准 XYZ').Replace('(BINARY_ZEMAX_BSDF_2026-10-31.md)','(SPEOS_V11_XYZ_2026-10-31.md)')
$s}
Edit 'docs/NON_SEQUENTIAL_TRACING.md' {param($s)
$s=$s.Replace('[实测 CSV、Speos 文本/已核验二进制与 Zemax 单色 BSDF](BINARY_ZEMAX_BSDF_2026-10-31.md)','[实测 CSV、Speos v8/v11 文本/已核验二进制、镜面增强/White Specular 与 Zemax 单色/校准 XYZ](SPEOS_V11_XYZ_2026-10-31.md)').Replace('Zemax 须声明测量波长、原数据为辐射度及 BTDF 介质','Zemax 单色须声明测量波长和辐射度，XYZ 须另选辐射度校准 JSON，BTDF 须声明介质')
$s}
Edit 'docs/NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md' {param($s)
$s=$s.Replace('保留镜面中心分布和投影数量，接入同一传感器。当前仍未实现：','保留镜面中心分布和投影数量，接入同一传感器。后续已补齐 [Speos v11、原生镜面配准/White Specular 和校准 XYZ](SPEOS_V11_XYZ_2026-10-31.md)，新增模型使用协议 4，沿用同一功率与冻结链路。当前仍未实现：')
$s}
Edit 'docs/NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md' {param($s)
$s=$s.Replace('已接通；其他格式与商业数值对标仍未完成。','已接通；[v11、原生镜面增强/White Specular 和校准 XYZ](SPEOS_V11_XYZ_2026-10-31.md)已实现，其他格式与商业数值对标仍未完成。').Replace('Zemax 单色辐射度格式及固定轴各向异性；后续增加','Zemax 单色/校准 XYZ、Speos v11/原生镜面增强与 White Specular 及固定轴各向异性；后续增加')
$s}
Edit 'docs/README.md' {param($s)
$s=$s.Replace('- [Speos 二进制、Zemax 镜面中心 BSDF 与相机链路](BINARY_ZEMAX_BSDF_2026-10-31.md)',"- [Speos 二进制、Zemax 镜面中心 BSDF 与相机链路](BINARY_ZEMAX_BSDF_2026-10-31.md)`n- [Speos v11、原生镜面配准/White Specular 与校准 XYZ](SPEOS_V11_XYZ_2026-10-31.md)")
$s}
foreach($path in @('docs/BINARY_ZEMAX_BSDF_2026-10-31.md','docs/COMMERCIAL_BSDF_AXES_2026-10-31.md')){Edit $path {param($s) $note="`n`n后续阶段：[Speos v11、原生镜面增强/White Specular 与校准 XYZ](SPEOS_V11_XYZ_2026-10-31.md)。本报告的测试数量、来源与未实现边界保留当时冻结身份。";$s.Insert($s.IndexOf("`n"),$note)}}
Edit 'docs/SPEOS_V11_XYZ_2026-10-31.md' {param($s) $s.Replace('未知观察者/照明和缺少校准的数据','缺少观察者/照明声明或校准的数据').Replace('Speos 原始节点≤262,144','Speos TEXT 原始节点≤262,144、二进制原始节点≤131,072')}
