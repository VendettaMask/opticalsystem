
## 2026-10-31 Speos v11、镜面增强、White Specular 与校准 XYZ

已实现 Speos v11 TEXT 与两种已核验 archive-3/LZ4 归档变体；可分 R/T 光谱避免角表按波长展开，本机 81/125 光谱点材料均完整导入为 40,002 角度节点。新增镜面/背景分解、带雅可比的原生 SlopePowerTransport/1 配准和 White Specular；XYZ 保留三原始通道及 TIS，须提供非负辐射度校准 JSON，不自动由 XYZ 推导光谱。桌面导入、NSC→光谱 W/像元→RAW/ISP、数据库与冻结重放已接通；新增模型用 MeasuredBsdfGrid/4、STAROPT v12，旧身份不升级。

最终合并定向回归 **229/229**，新增 **38** 项，零失败、零跳过；默认 Release App/测试驱动零警告、零错误。覆盖 v11 实际文件、独立解析密度和逆映射 CDF/两阶矩、85° 入射、三波段 R/T 频数、White Specular 非被动拒绝、XYZ 校准/摘要、1→1.5 介质、v12 及降版本拒绝、四条三波段一次光子换算与完整 RAW/ISP 重放、五个旧冻结身份和三条离屏控件路径。逐像元换算最大误差 9.10e−13 e−；运行前后 57 个范围内源码/夹具、9 个二进制指纹一致。

镜面配准是已声明并验证的原生功率模型，尚无活动商业增强/White Specular 的逐值数值认证；未知二进制/活动 v8/v9 布局、Retroreflection、XYZ 无校准光谱恢复等仍不支持。没有原生桌面验收、全量测试或安装包发布。实现、校准契约和具体边界见[本阶段说明](SPEOS_V11_XYZ_2026-10-31.md)与[证据](../validation/rechecks/2026-10-31/speos-v11-xyz/README.md)。下列阶段保留各自冻结范围。

