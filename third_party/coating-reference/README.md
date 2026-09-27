# 镀膜设计参考源（不参与产品构建）

获取日期：2026-09-27。

| 归档 | 来源/版本 | SHA-256 |
|---|---|---|
| TFStudio-8b942b3.tar.gz | https://github.com/aai2k/TFStudio commit 8b942b3f956728a23cea2b35023c8b5d8de17428 | 055391199312d1ce1a3d30e5640aad1375b58b77ec88f2c3b55cda56d721cbe6 |
| tmmcore-0.4.2.tgz | https://registry.npmjs.org/tmmcore/-/tmmcore-0.4.2.tgz | fc16f4a16149c971178b6a354ecf094dc0511a2bdfa0630c977178c29b262f44 |

TFStudio 锁文件实际 tmmcore integrity：`sha512-KD/Sg2r1Zo0pcWE9NI15O9CaXBjQ6mxiWP/mzGPL53oXltA5nc9SGfS1Zh2bQpKS9nR+xyLK6hUpNPvVxrIKHg==`。两个项目均 MIT © 2026 Andrey Achapovsky，许可证原文保留于本目录。应用初始模板及交互参考 TFStudio；新 C# 散射求解器按相同物理约定实现并与 tmmcore 对照，没有嵌入上游桌面环境。

SiO2/Ta2O5 YAML 原始数据来自 https://github.com/polyanskiy/refractiveindex.info-database commit `c5c2f188e848453def5970e347399d653df2ffc2` 下 `database/data/main/{SiO2,Ta2O5}/nk/Rodriguez-de Marcos.yml`，CC0 1.0，来源说明与文献链接在原文件。仅将波长 μm 转 nm 后无损转为材料表，不拟合、不去除 k。

展开：`tar -xzf TFStudio-8b942b3.tar.gz -C <reference-dir>`；tmmcore 归档顶层为 `package/`。`validation/coating/generate-reference.mjs` 使用固定 tmmcore JavaScript，实际产生 120 组 R/T/A 对照。源归档不含 Git 子模块的数据内容，所用材料原文件已单独保存。

## 产品边界和相关文档

完整源归档保持上游原样；MIT 许可证与 CC0 来源声明不作改写。实验室输出复制 `*LICENSE.txt` / `*NOTICE.txt`；生产程序不加载归档、上游桌面环境或 Node。实际代码复用边界见[本地审计](../../docs/COATING_DESIGN_LAB.md)，数值对照和独立物理检查见[验收记录](../../validation/coating/README.md)，启动与示例见[实验室说明](../../labs/CoatingDesign/README.md)。
