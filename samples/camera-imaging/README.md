# 相机传感器参数与响应示例

- `generic-sensor.camera-sensor.json`：完整传感器参数模板；所有单位由字段名及[实验室说明](../../docs/CAMERA_IMAGING_LAB.md)定义。主界面“保存参数/模板”也能生成同格式文件。
- `example-qe.csv`：比例单位的绝对总 QE，含 CFA 响应。
- `example-qe-percent.csv`：同一曲线的百分数写法，导入时按表头转换为比例。
- `illuminant-flat.csv`：400–700 nm 的 0.0001 W/m²/nm 平谱，总输入 0.03 W/m²；是理想白目标的像面标定光谱。
- `reflectance-neutral.csv`：全波段 50% 反射率。
- `reflectance-red.csv`：随波长上升的示例材质反射率。

所有文件都是人工构造的格式/数值验证样例，不对应具体厂商或实测数据。JSON/CSV 随构建与发布复制到 `Examples/CameraImaging/`。在实验室先用三波段模型运行并固定 A，再导入 QE、光源和反射率，选择“连续光谱积分”运行 B。默认 400–700 nm、100 nm 最大区间保留全部曲线折点，共 9 个积分节点。输入图作为同一材质的灰度空间图样，不反演 RGB 光谱。导出包内的 `sensor-profile.json`、`illuminant.csv`、`reflectance.csv` 可分别再导入；完整运行设置与实际积分节点保存在元数据。

CSV 需严格递增的 nm 波长、2–4096 个采样点。三波段运行时 QE 覆盖 450–650 nm，连续运行时三种光谱都覆盖指定积分范围；不外推或静默取交集。QE/反射率的百分数必须使用 `_percent` 表头。光源表头严格为 `wavelength_nm,irradiance_w_m2_nm`，不将相对功率、光源瓦数或 lux 当作像面 W/m²/nm。格式细节见[实验室说明](../../docs/CAMERA_IMAGING_LAB.md)。
