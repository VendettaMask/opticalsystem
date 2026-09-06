# 内置玻璃目录

`glass-catalog.json` 是独立的内嵌静态材料目录。历史导入来源是 Optiland 0.5.8 分发的 refractiveindex.info 数据；此处保留来源名称仅用于数据溯源，不表示产品依赖该软件或其运行时。现有文件字节和材料系数保持不变，以保持折射率、消光系数和追迹结果。

上游 refractiveindex.info 数据库声明采用 CC0 1.0 公共领域授权。生成资源只保留运行时需要的厂商、玻璃名称、有效波长范围、色散系数和表格光学常数。

`zemax-glass-catalogs.ogdb` 是 Workbench 自有的带模式版本、GZip 压缩数据库，由 `tools/OptilandWorkbench.GlassCatalogConverter` 从 63 个 Zemax AGF 目录生成。它包含 5,502 条源记录，并保留目录身份、全部 13 种色散公式、热学/机械数据、耐久性数据、有效波长范围、内部透过率和应力数据。

运行时只读取这两份产品资源，不读取测试参考或外部工具。旧目录生成器已经删除；不得重新通过 Optiland 生成材料或精度对照。后续材料更新使用独立来源和 C# 目录转换流程，并单独验证数值变化。Zemax AGF 目录可通过上述 .NET 转换工具维护。
