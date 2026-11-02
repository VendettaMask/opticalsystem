# 镀膜设计参考源（不参与产品构建）

2026-11-02 双面扩展继续复用既有固定Byrnes `inc_tmm`与原MIT许可，新增162组零/一/两层背面、吸收膜/基板、三波长/三角度/s/p的独立数值输入，仍按2e-11比较。输入只保存于测试fixture `duplex-substrate-20261102.json`；生成器和出处身份见[本轮记录](../../validation/rechecks/2026-11-02/coating-editor-duplex/README.md)。产品双面组合仍由共享C# Core计算，不加载Python或上游应用。

2026-11-02：整块基板的新独立验证参考为 [sbyrnes321/tmm](https://github.com/sbyrnes321/tmm/blob/9c265c20e2c5f75b5ac46d63ab466665948eb821/tmm_core.py) 的 `inc_tmm`，固定提交 `9c265c20e2c5f75b5ac46d63ab466665948eb821`，源文件 SHA-256 `0b6831a436bc2be5f1c035d3aa79a88dcc4c7e000dd32e095f968a51afb35628`。MIT原文保留为 `Byrnes-tmm-LICENSE.txt`；54组混合相干/非相干数值在测试 fixture `incoherent-substrate-20261102.json`，生成入口为 `tools/diagnostics/CoatingWorkspace20261102/generate-slab-reference.py`。该Python参考只用于离线独立核验，产品仍使用共享C# Core，无新增运行时依赖。固定TFStudio的 `thinFilmMath/totalSystem.js` 用于对照系统组成；本地不沿用其真实角度近似、临界角裁剪或传输截断。物理约定可参考[Byrnes 推导](https://arxiv.org/abs/1603.02720)，实际范围见[本轮验证](../../validation/rechecks/2026-11-02/coating-workspace/README.md)。

2026-11-01：截图中的五套完整示例按本归档 `web/demo-examples.js` 加入产品静态JSON，材料系数、n/k表、PCHIP和端点规则来自同归档 `materialDatabase.js` / `pchip.js`。源归档仍不参与运行或构建导入，生产计算全部使用C#共享Core。保留MIT/CC0来源与BK7吸收，Ta₂O₅的400–500 nm端点延用标为非实测；原真实吸收模板和验收不变。见[实现与逐点范围](../../docs/COATING_REFERENCE_EXAMPLES_2026-11-01.md)。

获取日期：2026-09-27。

| 归档 | 来源/版本 | SHA-256 |
|---|---|---|
| TFStudio-8b942b3.tar.gz | https://github.com/aai2k/TFStudio commit 8b942b3f956728a23cea2b35023c8b5d8de17428 | 055391199312d1ce1a3d30e5640aad1375b58b77ec88f2c3b55cda56d721cbe6 |
| tmmcore-0.4.2.tgz | https://registry.npmjs.org/tmmcore/-/tmmcore-0.4.2.tgz | fc16f4a16149c971178b6a354ecf094dc0511a2bdfa0630c977178c29b262f44 |

TFStudio 锁文件实际 tmmcore integrity：`sha512-KD/Sg2r1Zo0pcWE9NI15O9CaXBjQ6mxiWP/mzGPL53oXltA5nc9SGfS1Zh2bQpKS9nR+xyLK6hUpNPvVxrIKHg==`。两个项目均 MIT © 2026 Andrey Achapovsky，许可证原文保留于本目录。应用初始模板及交互参考 TFStudio；新 C# 散射求解器按相同物理约定实现并与 tmmcore 对照，没有嵌入上游桌面环境。

SiO2/Ta2O5 YAML 原始数据来自 https://github.com/polyanskiy/refractiveindex.info-database commit `c5c2f188e848453def5970e347399d653df2ffc2` 下 `database/data/main/{SiO2,Ta2O5}/nk/Rodriguez-de Marcos.yml`，CC0 1.0，来源说明与文献链接在原文件。仅将波长 μm 转 nm 后无损转为材料表，不拟合、不去除 k。

展开：`tar -xzf TFStudio-8b942b3.tar.gz -C <reference-dir>`；tmmcore 归档顶层为 `package/`。`validation/coating/generate-reference.mjs` 使用固定 tmmcore JavaScript，实际产生 120 组 R/T/A 对照。源归档不含 Git 子模块的数据内容，所用材料原文件已单独保存。

## 产品边界和相关文档

2026-10-30 后续实现参考了镜段/腔阶、耦合阶和入射侧匹配流程；本地通过共享Core的实际复反射振幅及正式优化器做数值匹配，未移植上游无吸收解析公式。10个新原型、6组匹配控制和实际输出546点独立对照见[窄带竞争证据](../../validation/rechecks/2026-10-30/coating-competition/README.md)。源归档/许可证与真实材料k保持。

2026-10-30 复核另外阅读了 `seedGenerator/rank.js`、`filterDesign/filterTarget.js`、`meritFunction.js`、`prototypeLayers.js` 和 `coupledPrototype.js`。C# 新协议借鉴先短程优化后排序与目标缩放，有限结构提议用实际固定生成器核对；没有移植上游优化器或解析匹配综合。原归档/许可证不变，新增离线证据见[本轮记录](../../validation/rechecks/2026-10-30/coating-reference-review/README.md)。

完整源归档保持上游原样；MIT 许可证与 CC0 来源声明不作改写。实验室输出复制 `*LICENSE.txt` / `*NOTICE.txt`；生产程序不加载归档、上游桌面环境或 Node。实际代码复用边界见[本地审计](../../docs/COATING_DESIGN_LAB.md)，数值对照和独立物理检查见[验收记录](../../validation/coating/README.md)，启动与示例见[实验室说明](../../labs/CoatingDesign/README.md)。

2026-11-01 离线结构反馈对照调用固定TFStudio的`proposeMutation`、`tidyLayers`、随机源、Metropolis接受与DLS，8个相同起点均以真实n/k和原验收门槛复验。参考目标是点平方误差，与本地FWHM/最差OD/最差R不同；连续调用主额度相近不代表总查询成本相同。所用源码逐项核对原归档未改，4389个实际输出R/T/A点另对固定tmmcore通过既有2e-11标准。产品新增通用极值采样，未引入Node或移植参考结构优化器；见[本轮结果与边界](../../docs/COATING_TOPOLOGY_FEEDBACK_2026-11-01.md)。

后续重采样反馈验证补入同一数据库提交的`TiO2-Siefke.yml`、`Nb2O5-Lemarchand.yml`及`HfO2-Siefke.yml`原始薄膜n/k表，CC0及样品/文献说明见`MATERIAL-NOTICE.txt`。保留报告的吸收及零值，只转换波长单位，不外推；HfO2表到619.921 nm，验证仅用覆盖的500/550 nm规格。测试组合为SiO2/TiO2、SiO2/Nb2O5、HfO2/TiO2，加原SiO2/Ta2O5；不代表不同制程的普适常数或实物产品认证。参考结构优化仍调用未修改归档，生产代码不依赖Node；见[修正采样下的对照](../../docs/COATING_RESAMPLED_FEEDBACK_2026-11-01.md)。
