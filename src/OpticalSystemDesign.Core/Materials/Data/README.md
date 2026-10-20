# 内置玻璃目录

`glass-catalog.json` 是独立的内嵌静态材料目录。历史导入来源是 Optiland 0.5.8 分发的 refractiveindex.info 数据；此处保留来源名称仅用于数据溯源，不表示产品依赖该软件或其运行时。现有文件字节和材料系数保持不变，以保持折射率、消光系数和追迹结果。

上游 refractiveindex.info 数据库声明采用 CC0 1.0 公共领域授权。生成资源只保留运行时需要的厂商、玻璃名称、有效波长范围、色散系数和表格光学常数。

`zemax-glass-catalogs.ogdb` 是 Workbench 自有的带模式版本、GZip 压缩数据库，由 `tools/OpticalSystemDesign.GlassCatalogConverter` 从 63 个 Zemax AGF 目录生成。它包含 5,502 条源记录，并保留目录身份、全部 13 种色散公式、热学/机械数据、耐久性数据、有效波长范围、内部透过率和应力数据。

正式 Core 默认玻璃目录运行时读取这两份产品资源，不读取测试参考或外部工具。旧目录生成器已经删除；不得重新通过 Optiland 生成材料或精度对照。后续材料更新使用独立来源和 C# 目录转换流程，并单独验证数值变化。Zemax AGF 目录可通过上述 .NET 转换工具维护。

`zemax-mil-profiles.json` 是两种 MIL 数字玻璃 `517642`、`648338` 的独立色散输入表，来自本机 OpticStudio 2026 R1 的 `LDE.GetIndex` 材料查询，温度 20°C、相对压强 1。它与镜头像差、MTF、能量等比较输出分开采集；没有把镜头结果写入产品计算。每种材料包含 704 个波长节点，覆盖 350–1050 nm，记录来源身份；原节点精确返回，节点间在共享 Core 用局部四点插值，拒绝外推。另采每种 700 个半纳米核验点。快照保存节点和来源，可离线恢复。这只补齐上述两个已采集的 MIL 编号，不是任意六位编号的通用公式，也不把 MIL 近似玻璃替换为相近的真实目录玻璃。原两份材料目录字节未变。

## 独立镀膜材料资源

镀膜实验室另有 [films.json](../../../../labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Data/films.json)，包含固定 refractiveindex.info 提交的 SiO₂/Ta₂O₅ 真实 n/k 表，来源与 CC0 说明见[参考归档](../../../../third_party/coating-reference/README.md)。该文件由实验室嵌入读取，再构建正式 `CatalogGlassMaterial`；色散与插值仍由 Core 实现，不替换本目录的玻璃数据库。维护脚本只转换单位及数据组织，不参与产品计算。
