# Zemax定义核查证据

[核查结论和官方来源](../../../../docs/ZEMAX_DEFINITION_REVIEW_2026-11-04.md)。两处实现缺口及一个入口能力差距仍未修复；本轮没有改产品、原处方、参考或容差，也没有新原生捕获。

- [summary.json](summary.json)、[identity.json](identity.json)：范围、Core及源码身份；20次Workbench请求和4组共享波前探针，不作为通过测试数。
- [surfaces.json](surfaces.json)：OPD/波前的面号不生效及无效999仍成功；FFT PSF/MTF中间面准备的异常/拒绝。
- [reference-modes.json](reference-modes.json)：四模式曲线相同，Absolute像面光程定义探针的差异；该探针不是原生采集值，只用于Absolute/Absolute2判断。
- [source-inventory.json](source-inventory.json)：95份不变源文件均使用ExitPupil，故新缺口不能冒称既有默认参考残差根因。
- [archive-index.json](archive-index.json)、[archive-verification.json](archive-verification.json)、[evidence.zip](evidence.zip)：19份逻辑输出/16份唯一载荷，完整读回核验。

归档SHA256为 `761485e4f17df49837e29c05b4670e16fa85748d462ff6884609b0baa62019fe`；Core为 `1af8b46ae62b581a85b05b3954f0d8dd1a1414e989d1c826feaed272836036a3`。实际原始输出在归档索引下，不把新控制混入前一轮52 Pass或全3024行。

[纯C#复算工具](../../../../tools/diagnostics/ZemaxDefinitionReview20261104/README.md)。仅后台读取与计算，无桌面操作、发布或推送。
