# 非序列模式示例

本目录的 106 个 ZMX 文件来自用户指定的本机
`Zemax/Samples/Non-sequential`，覆盖全部 16 类。文件夹、文件名、原始字节均保留，
这些输入不是本项目原创。`index.json` 保存来源、收录日期、相对路径与 SHA-256；
Git 属性禁止处方换行规范化。构建和发布复制至 `Examples/NonSequential`，
菜单与检查无需本机另有源目录。

非序列模式使用 **文件 → 示例 → 分类 → 示例名称**。分类显示遵循中英日界面语言，
示例名、路径提示和动作身份保持原样。顺序模式仍使用原有 14 类、95 个顺序示例。

选择示例后，支持的文件直接完整载入场景，并可撤销。
无法导入时只显示简短提示和原文件名，当前工程保持原样。

`Objects` 保留用户提供的资源子目录、文件名大小写和原始字节，收录当前示例引用的
26 份资源，共 3,304,662 字节。`asset-index.json` 记录每份资源的路径、哈希和引用示例。
构建及发布将资源复制到 `Examples/NonSequential/Objects`。

后续文件沿用同一布局：设计文件与 `Objects` 位于共同的数据根目录下，读取器从设计目录
向上查找配套资源。新增名字直接使用相同查找规则。完整目录示例见[导入指南](../../../../docs/NONSEQUENTIAL_IMPORT.md)。

2026-11-23 接入资源后，已映射 325 个对象；Color Fringes、Two-color mixing gives white、
Even asphere 和 sobol sampling 四个文件可完整导入并追迹。仍缺三张 BMP 图片，其他
对象和属性的具体状态见[最新逐文件清单](../../../../validation/rechecks/2026-11-23/nsc-objects-layout/files.md)
和[资源清单](../../../../validation/rechecks/2026-11-23/nsc-objects-layout/resources.md)。

`Birefringence/Color Fringes.ZMX` 支持完整记录解析、波片模式追迹和真彩色探测器显示。
XYZ 光源按记录的非负光谱拟合导入；使用方法见[彩色波片说明](../../../../docs/NSC_COLOR_FRINGES.md)。
