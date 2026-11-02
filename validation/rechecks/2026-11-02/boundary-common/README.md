# Maximum口径与边缘光线复核

实现、官方依据与完整限制见[专题报告](../../../../docs/MAXIMUM_APERTURE_REPAIR_2026-11-02.md)。

- [summary.json](summary.json)：216项前后结果、6项边缘状态、141项测试、默认双模块App构建及Core身份。
- [results-final.json](results-final.json)：191 Pass、5 Close、20 Incomparable，原Pass保持；Close及缺参考均未计通过。
- [fixed-ray-results.json](fixed-ray-results.json)：48条固定输入、432组逐面对照、接受/首个裁光结果及原生哈希。
- [source-verification.json](source-verification.json)：Core、Application、App、诊断、测试的执行PDB及源码校验。
- [archive-index.json](archive-index.json)、[archive-verification.json](archive-verification.json)：逐文件SHA-256及完整读回收据。

索引的`archive`相对本目录；有`entry`时读取zip成员，`entry=null`时路径本身为唯一实体文件。六份`native-rays/c*-w*/native`固定原生输入保留用于诊断直接重放，不在zip中重复。其他新字节保留在`evidence.zip`，旧字节复用此前检测、Huygens、相位归档；逐文件核验后退役相应展开副本。

主要逻辑目录：`probe-before`保留旧端点裁光；`probe-after`为遗漏瞄准的失败足迹诊断，不参与验收；`probe-corrected`为修正后的探测；`native-solves`读回Maximum/FloatingAperture，重复的8条输入只用于类型确认，不增加独立光线覆盖。`fixed-rays-final`为最终Core对6份原生输入比较；`analyses-accepted`是最终216项。`tests`保留中间失败和最终141项TRX，重复执行不累计。

`native-host-source`保留首次6份原生捕获时的原始Host源码，后续Host仅新增只读的口径类型输出；当前源码同时归档。捕获源处方未改变，原始低采样分析参考保持。复算入口为`tools/diagnostics/BoundaryCommon20261102`的`compare`和`tools/diagnostics/PhaseCommon20261102`的`full`，选择空输出目录，不覆盖原记录。

封存后另一任务开始修改绘图视口、分析/查看器面板及语言词条，并新增相关按钮和测试。本轮保留其修改；正常App构建只绑定封存的执行PDB与源码，不认证这些后续并行改动。实际源码变动名单见`final-review.json`，共享Core身份保持。
