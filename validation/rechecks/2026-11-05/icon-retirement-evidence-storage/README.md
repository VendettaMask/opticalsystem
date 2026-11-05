# 旧图标退役与验证载荷去重

本轮按用户“旧图标可以直接删除了，退役，重复验证文件只留一份”执行。清理基于主线 `872218dcd4065afd0e07b08a7494c156fd528e57`，不重写Git历史，不控制或重启用户程序。

## 已完成范围

- 删除88个旧图标文件：旧Isekai/Pixel图标实现、两份旧JSON、83张FarmFresh PNG及Game Icons导入器。普通旧Lucide实现从共享文件移除；运行时与预览只读当前Scientific注册表，当前1969个语义/备用SVG名称及五主题图稿保持。
- Lucide构建输入移到`scripts/data`，供当前SVG再生成；当前Lucide ISC许可继续随包分发。两份退役图库的原许可移到[历史来源说明](../../../../third_party/retired-icons/README.md)，不随产品打包。
- 对19384个原文件完整计算原字节SHA-256，删除8614个非空重复载荷，减少 **3128839170字节，约2.914 GiB**。受管范围为`validation`、受跟踪的`artifacts`和`output`。上下文相关源码/脚本/Markdown和空日志保留各自角色，未将它们当成冗余数值载荷删除。
- [存储映射](../../../evidence-aliases.json)共2702组、8741个逻辑别名，包含此前127个别名。每组只存一份同哈希载荷，原捕获身份/请求/结果不改写。583项原冻结逻辑路径使用显式存储映射，4项既有产品更名迁移继续保留。

原逻辑路径仍由验证专用`EvidenceFiles`解析；项目构建按声明还原所需夹具到测试输出，两个测试项目本次分别还原524和447份。历史诊断可生成独立的忽略视图；禁止写回受管证据目录。映射拒绝越界、链接、链式别名、变更字节和重复副本重新出现；Git守卫要求映射本身也与检查的提交/索引一致。新检出仅下载必要的LFS权威载荷，再核对实际字节。入口及回放方式见[维护说明](../../../../tools/validation/README.md#验证载荷单一副本)。这些辅助代码只进入测试/验证工具，未修改Core/Application光学源码。

## 验证结果

| 范围 | 本轮结果 | 原始记录 |
| --- | --- | --- |
| 默认Release整方案构建 | 0警告、0错误 | [构建日志](solution-build-restored.log) |
| 当前图标、主题及逻辑存储定向测试 | 42/42通过，0跳过 | [TRX](cleanup-focused-final.trx) |
| 比较工具原冻结夹具/OPD定向测试 | 24/24通过，0跳过 | [TRX](comparison-fixtures-final.trx) |
| 别名路径/还原守卫 | 13/13通过 | [结果](aliases-expanded-final.json) |
| 工作区冻结守卫独立夹具 | 28/28通过 | [结果](frozen-tests-final.json) |
| Git冻结守卫独立夹具 | 22/22通过 | [结果](git-frozen-tests.json) |
| 存储映射与冻结身份交叉检查 | 8/8通过 | [结果](storage-guard-tests-final.json) |
| 当前完整受管载荷扫描 | 非空重复组0，映射有效 | [扫描结果](aliases-optimized.json) |
| 工作区原冻结证据 | 2668/2668原字节匹配 | [原字节账目](frozen-working-final.json) |
| Index原冻结证据及映射 | 2668/2668匹配 | [Git账目](frozen-index.json) |
| 本地化检查 | 3049词条、2825处引用，0缺失/错误 | [同字节权威日志](../../2026-11-04/local-ui-catalog/catalog-check.log) |
| 文档检查 | 1864份文档，0断链、0代码块问题 | `scripts/verify-documentation.ps1 -Strict` |

原2668项聚合SHA-256仍为`3a5fde98e1c881700f00be1c62d6ab4ff6f4feacc90454046ed787e80bf28c10`；既有迁移表SHA-256仍为`6eefb25cbb1c35a120f73e8bbb761aac16759e7b8491b4606475caec8dfd4f6e`。存储映射SHA-256为`bb5ad46409c5fc4fe5d7a7b11ee4fc89ab3f20fe791cada1c63845e40ff798ea`。新删除量与含旧127别名的累计节省量分别统计，详见[删除计划摘要](dedup-plan.json)。

## 保留的失败与验证边界

[扩展消费者检查](evidence-consumers.trx)为72过/8失败：其中1项新存储夹具的Windows斜线比较问题已修复，并由最终42项中的4项存储测试复验；另外7项是FCGT/FCGS场曲、应用预设场曲、EFNO/RELI照明及SFR的原数值断言失败，继续保留，未放宽、跳过或改算法。

两项场曲预设额外使用清理前HEAD的原测试与73份原物理文件单独复算，仍在原`1e-5 mm`门槛报告同一`0.00041672136290109643 mm`差异，见[原存储对照TRX](original-storage-control.trx)。该对照只复算这两项，不能扩展为其余5项逐一重认证。

初轮图标检查32过/5失败，暴露Isekai固定English后的旧选择器假设及共享禁用前景资源缺省；已修复选择器和资源默认值，保留[初轮TRX](icons-retired.trx)、[资源诊断](theme-resource-diagnostic.trx)、[中间37项通过](icons-final.trx)，最终42项包含这些案例，不能累计成额外通过数。首次整方案构建因本地化工具缺少还原资产而中止，保留[日志](solution-build-final.log)；[锁定还原](localization-restore.log)后使用正常Release输出构建通过。

本轮为退役/存储完整性和定向消费验证，未执行全仓数值重认证，未重新采集原生参考，未打包或宣称三平台运行验收。普通CI保留对应守卫，不能把工作流接入视作远端已通过。原历史失败和捕获报告不因去重改为成功。
