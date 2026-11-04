# 内置玻璃目录

`glass-catalog.json` 是独立的内嵌静态材料目录。历史导入来源是 Optiland 0.5.8 分发的 refractiveindex.info 数据；此处保留来源名称仅用于数据溯源，不表示产品依赖该软件或其运行时。辅助资源原件的字节和系数保持；其中旧CDGM条目已退出运行时解析，由下述官网目录取代，因此不能将历史CDGM数值对照视为最新版数据的验证。

上游 refractiveindex.info 数据库声明采用 CC0 1.0 公共领域授权。生成资源只保留运行时需要的厂商、玻璃名称、有效波长范围、色散系数和表格光学常数。

`zemax-glass-catalogs.ogdb` 是产品自有的带模式版本、GZip 压缩数据库，当前包含60个AGF目录、5,047条源记录（5,046个目录内唯一名称）。2026-11-04按用户要求，将原四份CDGM目录直接替换为厂家官网下载的 `CDGM-ZEMAX202609.AGF`（Updated Sep. 2026），统一为单一CDGM、352种唯一材料；其他59家目录的全部记录逐项保持一致。保留目录身份、全部13种色散公式、热学/机械数据、耐久性、有效波长范围、内部透过率和应力数据。见[官网原件及来源](../../../../third_party/material-catalogs/cdgm/README.md)和[更新验收](../../../../validation/rechecks/2026-11-04/material-library-details/README.md)。

正式Core运行时读取这两份产品资源，不读取测试参考或外部工具。CDGM仅使用上述官方AGF；辅助JSON原件字节保留，但其旧CDGM条目不再参与运行时目录，避免覆盖新数据或恢复官网已移除牌号。其他厂家的名称身份保持。旧CDGM-H/CDGM-ZEMAX202309/CDGM20113-P查询键指向当前CDGM；已保存的显式材料快照仍保留自己的数值。旧目录生成器已经删除；不得重新通过Optiland生成材料或精度对照。维护工具新增 `--replace <input.ogdb> <source.agf> <catalog name> <replaced names,comma-separated> <output.ogdb>`，使用共享C#解析器和公式验证后原子写入。

`zemax-mil-profiles.json` 是两种 MIL 数字玻璃 `517642`、`648338` 的独立色散输入表，来自本机 OpticStudio 2026 R1 的 `LDE.GetIndex` 材料查询，温度 20°C、相对压强 1。它与镜头像差、MTF、能量等比较输出分开采集；没有把镜头结果写入产品计算。每种材料包含 704 个波长节点，覆盖 350–1050 nm，记录来源身份；原节点精确返回，节点间在共享 Core 用局部四点插值，拒绝外推。另采每种 700 个半纳米核验点。快照保存节点和来源，可离线恢复。这只补齐上述两个已采集的 MIL 编号，不是任意六位编号的通用公式，也不把 MIL 近似玻璃替换为相近的真实目录玻璃。MIL采集步骤未改动当时两份材料目录；其后CDGM目录的独立更新见上文。

## 独立镀膜材料资源

镀膜实验室另有 [films.json](../../../../labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Data/films.json)，包含固定 refractiveindex.info 提交的 SiO₂/Ta₂O₅ 真实 n/k 表，来源与 CC0 说明见[参考归档](../../../../third_party/coating-reference/README.md)。该文件由实验室嵌入读取，再构建正式 `CatalogGlassMaterial`；色散与插值仍由 Core 实现，不替换本目录的玻璃数据库。维护脚本只转换单位及数据组织，不参与产品计算。
