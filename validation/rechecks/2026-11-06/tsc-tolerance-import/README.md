# OpticStudio TSC 装调子集验收

2026-11-06，Windows x64，.NET 10，Release。脚本和评价表为本地生成的等效输入，用 Cooke 验证 Workbench 的导入、装调和样件复算。

| 项目 | 结果 |
| --- | --- |
| 命令和路径 | 读取两段 CLEARCOMP、LOADMERIT、COMP、OPTIMIZE 和末尾 GETMERIT。脚本含带引号的原始 Windows 路径，使用人工选定的同名 ZMX。三个 ZMX 用 UTF-16 写入，以现有 ZMX 解析器读取。 |
| 第一阶段 | 先施加 +0.04 mm 制造误差，装调面 2 后空气间隔至 6.05755 mm。阶段评价为 3.33×10⁻⁹。 |
| 第二阶段 | 锁定前一阶段并调节面 6 后空气间隔至 42.25778 mm。阶段评价为 1.66×10⁻⁸。 |
| 最终验收 | 独立 Acceptance 表算出 0.0250000166。手动设定的上限为 0.01，整机判为未通过。v5 样件在 Tessar 工作区复算一致。 |
| 阻塞和拾取 | 缺表、缺行程、同名歧义、CPAR、自动或混合循环、重复释放、SAVE 和 OPENFILE 均不能应用。CMCO 核对所选配置、厚度行及原有 TMCO 的保留。 |
| 预览 | 560 DIP、150% 缩放。缺表或行程时应用按钮禁用。补齐后显示每阶段循环数、最终验收表和可点击的应用动作。 |

[脚本](two-stage.tsc) · [第一阶段 ZMX](Gap%20Setup.zmx) · [第二阶段 ZMX](Focus.zmx) · [最终验收 ZMX](Acceptance.zmx) · [复算样件](imported-script.startolsample.json)

[阻塞预览](tsc-preview-blocked-light-560.png) · [可应用预览](tsc-preview-ready-light-560.png) · [首轮定向回归](tsc-initial.trx) · [扩展回归](tsc-acceptance.trx) · [修改前基线](tsc-prior-zmx.trx) · [最终定向回归](tsc-focused.trx)

最终定向回归 160/160 通过，排除整组特殊面参数零的 6 个诊断用例。中英日词库检查为 3189 条译文、2902 处引用，零缺失、零错误。

扩展回归 163 项通过、3 项失败。以修改前 HEAD `a839ca7c` 的 Core 重建并运行同一参数零测试，BINARY_2、FZERNSAG 和 BIRE__IN 三项测试名称与失败消息逐字匹配。保留两份 TRX。

导入使用显式 Workbench 行程 ±0.2 mm。首批范围为类型 0 的空气间隔 COMP、配置厚度 CMCO、各阶段相同的正固定 DLS 循环数和选定 ZMX 评价表。原生 MF 解析、其他脚本命令与原生逐值对照仍待完成。[Ansys 命令说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/The_Tolerance_Script_Commands.html)
