# 冻结的 Optiland 0.5.8 历史回归

这些文件是辅助历史回归证据，不能代表当前产品对 Optiland 的兼容承诺、运行后端或未来发展方向。禁止新增、重新捕获或通过外部 Optiland 再生成参考。后续外部数值验证以仓库提交的 Zemax OpticStudio 2026 R1 基准为主。

`fixtures/` 中已有的 11 份数值 JSON、ZMX 输入和孔径顶点文件保持原始字节。`manifest.json` 记录原位置及 SHA-256，测试验证这些哈希；历史 JSON 中残留的生成脚本路径、Python 字典字段和版本名只是原始溯源信息，不再用于加载、运行或导出该格式。原 Cooke/Tessar 两份专用 Python JSON 导入夹具及全部参考生成器已删除。

`inputs/finite-system.optic.json` 和 `inputs/components.json` 是在删除旧 C# 适配器前，对已有测试输入的一次性原生快照转存，共含 1 个有限物距模型和 29 个已有组件模型。转存没有执行 Python、重新计算外部参考或改变预期数值；也不保留转存器。它们只帮助历史测试使用项目自己的快照构造模型，未加入正式产品资源。

保留的数值检查覆盖 Cooke/Tessar 处方、光线、分析、材料、视场、物理孔径、切趾、相位、衍射、薄透镜及历史 ZMX 输入。格式往返和专用读写测试已删除；历史圈入能量专用算法及对应两个用例已删除，其原始数据仍留在冻结文件中作为档案，不通过恢复产品兼容分支来追求通过。

`tests/OptilandWorkbench.Tests/Validation/History` 是测试端读取边界；其它格式和材料测试也可通过 `FrozenHistoryFixture` 读取冻结输入。只有测试项目复制本目录，`src`、打包和启动流程不得引用它。历史容差沿用原测试值，不扩大容差以掩盖产品变化；几何 MTF 已有的 `2e-5` 近似约束是历史差异，并非 Zemax 验收阈值。

正式产品中的 refractiveindex.info 静态玻璃目录属于独立产品材料数据，并不迁入此处。程序集、命名空间、资源名中的 `OptilandWorkbench` 属于独立重命名议题，本次不处理。

冻结字节以 manifest 中 sourceRevision 的已提交 Git blob 为准，保留仓库规定的 LF 换行；不采用旧 Windows 工作副本的 CRLF 来定义哈希。两个新转存输入同样使用 LF，解析后的 JSON 值不变，以保证干净检出后的完整性检查可复现。
