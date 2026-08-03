# 测试镜头样例

用于手工界面和导入器测试的 Zemax 顺序模式源文件位于：

`local-data/lens-library/originals/user-zmx/project/samples/lenses`

- `achromatic-doublet.zmx`：带角度视场的 N-BK7/N-F2 胶合消色差双片。
- `double-gauss-50mm.zmx`：中央光阑、四组对称的摄影物镜。
- `telephoto-four-element.zmx`：正前组、负远摄组和后场镜组成的四片系统。
- `finite-conjugate-macro.zmx`：有限物距、物高视场系统。
- `real-image-height-demo.zmx`：使用 Zemax `FTYP 3` 实像高视场，主光线目标为局部像面坐标。

在应用中通过“文件 → 打开”选择上述源文件。目录玻璃由内置 Zemax 数据库解析，不需要另行安装 AGF。当前目录保留转换后的 `.staropt` 查看器样例。

评价函数顺序回归使用相邻 `project/root/[MS-L7](10X大NA大视场).ZMX`：源文件包含 103 行评价函数，当前导入结果保持同样顺序。这个夹具验证导入与显示，不表示其中全部操作数都已具备 Zemax 等价求值；九类兼容类型仍为禁用只读记录。

仓库根目录的 `Convert-Zemax-Lens.cmd` 可以在这里新增转换后的 `.staropt` 工程，同时把同一工程及其元数据安装到打包后的“数据库 → 镜头库”目录。
