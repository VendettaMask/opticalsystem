# 内置玻璃目录

`glass-catalog.json` 由 `tools/python-reference/generate_glass_catalog.py` 根据 Python Optiland 0.5.8 随附数据库中的玻璃数据生成。

上游 refractiveindex.info 数据库声明采用 CC0 1.0 公共领域授权。生成资源只保留运行时需要的厂商、玻璃名称、有效波长范围、色散系数和表格光学常数。

`zemax-glass-catalogs.ogdb` 是 Workbench 自有的带模式版本、GZip 压缩数据库，由 `tools/OptilandWorkbench.GlassCatalogConverter` 从 63 个 Zemax AGF 目录生成。它包含 5,502 条源记录，并保留目录身份、全部 13 种色散公式、热学/机械数据、耐久性数据、有效波长范围、内部透过率和应力数据。

运行时只读取这些生成资源；更新源目录后应通过对应生成工具重建，避免手工修改生成文件。
