# Sequential 全目录逐镜头实测

日期：2026-10-19。按用户指定的 `Documents/Zemax/Samples/Sequential` 全目录，对 14 个目录下 95 个名称逐一比较。完整结果按镜头查看：[逐镜头结果与具体差异](../validation/zemax/2026-r1/sequential-corpus-2026-10-19/BY_LENS.md)、[逐镜头 CSV](../validation/zemax/2026-r1/sequential-corpus-2026-10-19/lens-results.csv)。不是 95 个镜头全部通过。

## 按镜头的当前结果

以下为 ZMX 主矩阵的互斥分类；有分析差异的镜头可能同时存在计算错误或不可比项，详情均保留。

| 镜头状态 | 数量 | 说明 |
|---|---:|---|
| 本次请求全部通过 | 2 | Cooke 40 degree field、Inverse telephoto lens；各 30 项分析及 3 组光线通过 |
| 已比较项通过，存在未覆盖项 | 5 | Apochromat、Apochromat3、Double Gauss 28 degree field、Gaussian Quadrature Over Wavelength、Relay lens |
| 部分接近，仍需核对 | 1 | Doubly telecentric system：20 通过、1 接近、1 不可比 |
| 存在分析差异 | 29 | 具体分析、配置、视场、波长及误差逐镜头列出 |
| 存在光线差异，待核坐标约定 | 4 | 分析没有 Difference，但逐面控制存在差异；反射方向约定须独立核对 |
| 存在计算/采集错误 | 30 | 包括不支持的模型能力、软件计算和原生采集失败；不是 30 个已确认算法缺陷 |
| 导入/准备失败 | 16 | 14 个没有完成 Core 快照，Yolo 与 OAP Chebyshev 有部分准备结果 |
| 分析不可比，精度未认证 | 8 | 仍保留已执行光线及限制；不得算作分析通过 |

Cooke/Tessar 在本次用户明确要求的全目录统一比较内；没有新增独立残差修复任务。上述通过只限定于本次请求设置，不推广到任意采样、视场、波长或模式。

## 数值请求与格式控制

每个成功准备的配置执行原有 18 类主分析、每个正权波长的 4 项末视场控制及 GQ6 显式逐面光线。不是所有参数组合的穷举。现有毫米有焦分析适配器限制、原生警告、缺失曲线、坐标断点表面折叠及不支持的面型均明确记账。

| 范围 | 请求 | 通过 | 接近 | 差异 | 不可比 | 错误 |
|---|---:|---:|---:|---:|---:|---:|
| 95 镜头 ZMX 主矩阵 | 2526 | 770 | 26 | 150 | 744 | 836 |
| 13 组 ZOS 格式补充 | 356 | 87 | 2 | 6 | 207 | 54 |

主矩阵 217 组光线为 107 通过、28 差异、21 不可比、61 错误；补充 26 组为 7/8/1/10。13 组格式控制与原 95 个名称重叠，不算额外 13 个镜头。主矩阵的 836 个错误按互斥首要原因分为：未支持模型能力 758、主光线失败 35、软件计算失败 35、原生采集失败 6、准备失败 2。原生最终采集失败独立总数为 18，其中 12 项同时有软件失败，不能把互斥分类数当成采集失败总数。

ZMX/ZOS 第一配置模型读回为 87 相同、8 不同；独立原生导出后，除展示性的 NAME/NOTE/COMM 外，完整有序记录为 83 相同、12 不同。保守取差异并集的 13 组分别补采；例如 Double Gauss 28 的 ZOS 物距为 500，ZMX 为无限，不能按同名合并。补充分析以原始 ZOS 副本作原生输入，Core 使用该 ZOS 独立导出的 ZMX；不代表产品直接支持 ZOS。记录见[格式控制清单](../validation/zemax/2026-r1/sequential-corpus-2026-10-19/format-variant-ledger.json)及[有序记录核对](../validation/zemax/2026-r1/sequential-corpus-2026-10-19/ordered-native-record-comparison.json)。

## 当前问题与证据边界

Apochromat2 有 1 项衍射包围能量差异，Even Asphere 有 1 项 MTF 差异；210° 广角有 2 项末视场光程差差异；Zoom 三配置有 10 项差异，其中 9 项末视场光程差、1 项衍射包围能量，另外有 1 接近、7 不可比、3 错误。多支反射望远镜同时有成像分析及原始局部方向差异，需先排除方向/坐标适配器约定，再定位产品问题。跨分析最大 NRMSE 不用于镜头排名，近零参考尺度也会放大相对误差。

16 个导入/准备错误包括 PARM 0 参数索引、未支持坐标断点顺序、物方锥角孔径及导入后无效评价函数面号。大量 BINARY_3、PARAXIAL、TILTSURF、GRIN 等模型由能力门禁拒绝，保留只读原始参数；没有以近似几何代替。错误逐项见[错误分类表](../validation/zemax/2026-r1/sequential-corpus-2026-10-19/failure-matrix.csv)，分析原值与设置见[分析请求表](../validation/zemax/2026-r1/sequential-corpus-2026-10-19/analysis-matrix.csv)。

本机原生环境为 OpticStudio 2026 R1 EnterpriseEdition，API 许可有效。全部 295 个原文件哈希保持；ZDA 缓存只计入清单，没有当作数值参考。比较策略 SHA-256 为 `30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`。Core 使用此前验证的同一 Release 二进制，本次没有修改产品光学算法、原参考或容差。2668 个冻结资产在工作树及 HEAD 均通过原指纹核对；这证明完整性，不等于新增 95 镜头全部数值通过，也不是重新运行全仓测试。

执行期间保留了资源故障导致的 802 次原生 Remoting 失败和 3 次软件 CLR/分页文件启动失败；仅识别出的基础设施失败按原请求重试。原生重试每 50 项重开应用，全部最终任务有完成记录，真正计算/能力失败继续保留。4 项独立新实例点列控制与批量捕获的规范化输出哈希完全一致。两条准备中断后默认 Skipped 的占位行纠正为有原异常的 Error，旧 JSON 保留，数值数组与请求未改。

原始逐任务捕获、失败尝试和执行日志位于工作区 `artifacts/zemax-sequential-corpus/20261019/full-v1` 与 `zos-variants-v1`；本次尚未打包原始大体积捕获或上传远端。可携带的矩阵、源完整性、格式控制、失败分类和汇总已保存于上述 validation 目录。完整性与独立控制见[冻结资产核验](../validation/zemax/2026-r1/sequential-corpus-2026-10-19/frozen-integrity.json)、[独立点列捕获](../validation/zemax/2026-r1/sequential-corpus-2026-10-19/isolated-controls.json)。
