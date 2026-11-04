# 成都光明官方材料数据

- 厂商：成都光明光电股份有限公司（CDGM）。
- 官方[下载页](https://www.cdgmgd.com/go.htm?k=ge_lei_xia_zai&url=downList)的“zemax格式文档”当前指向[原件下载](https://www.cdgmgd.com/downloadFile.htm?uid=vkYlfwDmuNtvITOp4NV)。英文旧页面的数字ID入口返回“文件不存在”，本次使用中文官网实际入口。
- 下载日期：2026-11-04；响应文件名 `CDGM-ZEMAX202609.AGF`，文件首行 `CC Updated Sep. 2026`。这是下载当日官网提供的版本，不按下载年份虚构目录版本。
- [原始AGF](CDGM-ZEMAX202609.AGF)：UTF-16 LE，1,325,696字节，352个唯一材料名称；原件字节未经修改。
- SHA-256：`e0933fe14749919b2b7c5170f814765af570ef53b78171dc98dae6b542f8e9ce`。
- [响应元数据](download-headers.json)保留官方文件名、内容类型及长度。此数据来自厂商公开下载，不属于辅助refractiveindex.info目录的CC0来源。

产品只嵌入自有压缩数据库中的规范化CDGM目录，保留AGF的全部字段和数据，不在运行时访问官网。四份旧CDGM直接替换；其他59家目录记录逐项一致。旧版唯一牌号有12个不在最新文件中，不向新目录补造；新增21个、当前共352个。已有完整材料快照不被更新流程重写。

[更新收据与验证](../../../validation/rechecks/2026-11-04/material-library-details/README.md)保留完整增删名单、源/数据库哈希、C#转换命令、历史失败及离屏结果。未复制旧目录包，原Git对象与原始本机数据仍为其历史身份来源。
