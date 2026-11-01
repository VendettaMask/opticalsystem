# 望远镜与光学检测：真实遮挡和几何参考修复，2026-11-01

**原 7 支镜头的 13 项旧通过未保持现已全部恢复通过原门槛。** 本轮覆盖两个菜单类别的 16 支镜头、426 个可执行原请求；原账目另有 22 行非可执行/覆盖缺口，未算作通过。没有重跑全部 95 支示例，也没有更改原处方、配置、波长、请求、比较容差或真实通光条件。

## 实际修复

1. 将几何相位、坐标原点和微分焦点基准与实际通光采样分开。FFT/Huygens、波前图、OPD 和场曲/畸变扫描可显式取得受遮挡的几何参考；实际瞳孔、质心和积分仍来自物理追迹。通用波前 API 默认仍要求原物理参考，严格操作数仍拒绝受挡光线。完全无通光的新增参考路径明确返回无数据。
2. ZMX 导入新增 `OBSC` 圆形/环带遮挡，保留半径范围、长度单位、`OBDC` 偏心及配置中的 APMN/APMX/APDX/APDY。遮挡是圆形/环形孔径的补集，不额外构造一个外侧通光边界。它与 `CLAP` 环孔、自动 DIAM 和机械 MEMA 保持不同含义。定义参见 [Ansys 表面孔径说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Aperture_surface_properties.html)，实际接受/裁光另由原生固定输入验证。
3. 点列的 `chief` 参考在中心主光线受遮挡时保留几何主光线原点，不再静默退为质心；点列中的实际光线和权重不变。施密特–卡塞格林还同时补齐了漏读的第三面 OBSC。
4. `circular_obscuration` 组件完整进入快照、STAROPT 和 ZMX 往返，并验证有限、有序参数。不升级既有容器/快照版本；不认识该组件的旧读取器须按未知孔径类型拒绝。未扩展为所有遮挡形状或任意外部格式支持。

## 原 13 项逐镜头结果

| 镜头 | 原请求数 | 本轮结果 |
|---|---:|---|
| Offner reflecting compensator | 2 | FFT PSF、Huygens MTF 通过 |
| Cassegrain-type Ritchey Chretien | 1 | Huygens MTF 通过 |
| Houghton | 2 | Huygens MTF、场曲/畸变通过 |
| Maksutov offset secondary | 2 | 同上 |
| Maksutov | 2 | 同上 |
| Schmidt-Cassegrain spider obscuration | 2 | 同上；不等于该镜头其他分析全部通过 |
| Schmidt-Cassegrain | 2 | 场曲/畸变、点列通过 |

施密特点列的 RMS 半径为 2.950585016464818 µm，对原生 2.9505850164624756 µm，差约 2.34e-12 µm；几何半径差约 1.74e-10 µm。仅修复几何参考而不补 OBSC 时，该点列仍有差异；中间失败已保留。

7 支镜头共有 16 组既有固定光线控制：**11 组通过、5 组仍因蜘蛛遮挡系统的折叠面号映射未建立而不可比**。14,320 次有效表面比较中，接受、首次裁光、坐标、方向均无超限。施密特三个波长、各 216 条输入的原 58 处接受差异和 22 处首次裁光差异均降至 0；没有删除被挡光线或更换归一化输入。

## 两类别的同版本复核

| 范围 | Pass | Close | Difference | Incomparable | Error |
|---|---:|---:|---:|---:|---:|
| 本任务修改前，同一组 426 请求 | 150 | 17 | 39 | 64 | 156 |
| 本任务最终 Core，同一组 426 请求 | 224 | 20 | 28 | 101 | 53 |
| 最终：光学检测，154 请求 | 57 | 9 | 14 | 26 | 48 |
| 最终：望远镜，272 请求 | 167 | 11 | 14 | 75 | 5 |

原 150 个 Pass 保持 149，新增 75 个 Pass；**平场施密特 2278 的原 32 档包围能量由 Pass 变为 Difference**，未从表中扣除或改标。该低采样记录和下节的收敛认证分别保留。Close、缺参考和返回失败均不计通过；不能据净增宣称全部两类已完成验收。

## 平场施密特包围能量：独立收敛参考

补齐真实遮挡后，2278 的原 32 档实际曲线最差 NRMSE 为 0.01127787，保持 Difference。随后对相同原始 ZMX、配置 1、视场 1、波长 2、质心参考、0–5 µm 范围独立执行 Zemax 2026 R1 Enterprise Server 的 32/64/128/256/512 档捕获，以及各档共享 Core 计算。原生不操作用户窗口、关闭截图，不保存源文件；五次执行前后源哈希不变。

32→64 的原生结果为 Difference，后续到 256→512 才达到原 Pass 门槛，因此不能把旧 32 档当作已收敛精度权威。512 档的两个输出曲线分别为衍射极限和实际能量，原 EnergyFraction NRMSE 门槛仍为 0.003。

| 独立检查 | 最差 NRMSE | 最大绝对差 | 结果 |
|---|---:|---:|---|
| Core 512 对 Zemax 512 | 0.0003354345 | 0.0009765894 | Pass |
| Zemax 256→512 | 0.0013116266 | 0.0040705654 | Pass |
| Core 256→512 | 0.0015728265 | 0.0048639439 | Pass |

比较保留原逐分析策略和全范围覆盖。高采样结论只认证此处声明的范围；原 32 档结果不改写，也没有声称任意低采样档都与收敛值一致。产品不读取原生参考数组或本轮残差，不按镜头名特判。

## 构建、检查与剩余工作

- 101 项聚焦回归通过，另 66 项无焦 Huygens 遮挡/参考/采样回归通过，无失败或跳过，两组用例身份不重叠。包含 9 项新增回归，覆盖物理环瞳、相位、焦点坐标、点列原点、完全遮挡拒绝和 MM/CM 保存往返。
- 默认 Release 主程序及两个内置模块完整构建成功，0 警告、0 错误。主程序、两组测试和实际计算使用同一个 Core SHA-256 `e7d456a81e66b05ca075a9573125219eac6ef20683951ea8cd56828f68109200`。
- 此后工作区还有其他任务继续修改初始结构、镀膜、App 及非顺序/工程保存源码；上述构建只绑定记录中的实际执行版本，不外推为这些后续修改的完整验收。本轮 Core 修改、测试和实际执行程序集另按哈希保存。
- 并行构建后来替换了共享默认输出；独立执行副本保持。Core PDB 的 432 份手写源码在首次核验时全部一致；结束前再次确认本任务修改的 13 份 Core 文件仍逐一吻合，其他任务后来改动的文件单列于[源码核验](../validation/rechecks/2026-11-01/obscured-reference/source-verification.json)，不把后来的主程序字节当作本轮已测字节。
- 早期还原/编译失败、沙箱 testhost 通信中止与注册表访问失败分别保留，未算作成功验证。测试在允许本地通信管道后完成，直接原生运行在独立后台实例完成。
- 封存首轮因读取自身仍在写入的日志而失败；重试排除活动日志，旧失败日志保留，失败容器只在对完整容器逐成员核验后退役。
- 剩余差异主要在蜘蛛遮挡施密特的其他分析、Unobscured Gregorian、Yolo，以及双程零位检验和两类干涉仪。部分实际光线/参考仍不能到达像面，不能用几何参考补造不存在的传播；照度、像面样本、折叠面号及缺原生数据仍待分别处理。最终 53 个 Error 的逐项原因保留，不全部归为同一遮挡缺陷。
- 本轮不代表完整两类别、全部 95 支、全部分析入口或新的前台交互认证；没有安装、发布、提交或推送。

## 证据与接续

可直接阅读[结果摘要](../validation/rechecks/2026-11-01/obscured-reference/summary.json)、[426 项结果](../validation/rechecks/2026-11-01/obscured-reference/category-results.json)、[原 13 项](../validation/rechecks/2026-11-01/obscured-reference/regression-results.json)、[固定光线](../validation/rechecks/2026-11-01/obscured-reference/ray-results.json)、[原生与 Core 收敛](../validation/rechecks/2026-11-01/obscured-reference/energy-convergence.json)。

实际数组、逐项 provenance、原生捕获、测试记录、编译日志和执行源码/程序集按内容哈希去重保存在[唯一证据包](../validation/rechecks/2026-11-01/obscured-reference/evidence.zip)，逻辑路径由[索引](../validation/rechecks/2026-11-01/obscured-reference/archive-index.json)映射到 `payloads/<sha256>`；完整读回验证见[封存回执](../validation/rechecks/2026-11-01/obscured-reference/archive-verification.json)。记录中的绝对执行路径属于原运行位置，不表示仍保留展开副本。复用原生数组另外与既有权威 ZIP 按成员哈希核对，见[参考核验](../validation/rechecks/2026-11-01/obscured-reference/reference-verification.json)。

下一步继续核对光学检测三个系统的实际传播/像面有效性和望远镜余项，再按[分类交接](EXAMPLE_ACCURACY_HANDOFF_2026-11-01.md)推进其他优先项。
