"""Authored 32-grid functional drawings, not transformed stock icon geometry.

Every definition names its intended object/action. Colour classes are theme-owned.
The small component vocabulary is shared only where the depicted meaning agrees.
"""
from html import escape

ART = {}

def p(d, role='signal', opacity=None, dash=None):
    return f'<path class="{role}" d="{d}"'+(f' opacity="{opacity}"' if opacity else '')+(f' stroke-dasharray="{dash}"' if dash else '')+'/>'
def r(x,y,w,h,role='structure',radius=1.5,opacity=None):
    return f'<rect class="{role}" x="{x}" y="{y}" width="{w}" height="{h}" rx="{radius}"'+(f' opacity="{opacity}"' if opacity else '')+'/>'
def c(x,y,rad,role='secondary',opacity=None):
    return f'<circle class="{role}" cx="{x}" cy="{y}" r="{rad}"'+(f' opacity="{opacity}"' if opacity else '')+'/>'
def reg(id_,label,meaning,body,category='操作与导航'):
    assert id_ not in ART,id_
    ART[id_]=dict(id=id_,label=label,meaning=meaning,body=body,category=category)
    return id_

AX=p('M4.5 5.5v22h24','structure')
AX_LOW=p('M4.5 12v15.5h24','structure')
PAGE=p('M19 3.5H7a2 2 0 0 0-2 2v21a2 2 0 0 0 2 2h18a2 2 0 0 0 2-2V11L19 3.5Z M19 3.5V11h8','structure')
TABLE=r(4.5,5,23,22)+p('M4.5 11.5h23M12 5v22','structure')
LENS=p('M14 5c-4 5-4 17 0 22M18 5c4 5 4 17 0 22M2.5 16h27','structure')
STACK=p('m4 9 12-5 12 5-12 5Z M4 15l12 5 12-5 M4 21l12 5 12-5','structure')
FOLDER=p('M3.5 10V7a2 2 0 0 1 2-2H12l3 4h11.5a2 2 0 0 1 2 2v2M3.5 11.5h25l-3 15H6Z','structure')

# Deliberately simple operation primitives; their clear silhouette survives 18-DIP display.
reg('action-add','添加','交叉的水平与垂直笔画，表示添加项目。',p('M6.5 16h19M16 6.5v19'))
reg('action-remove','移除','水平减号表示从当前集合移除。',p('M7 16h18'))
reg('action-delete','删除','容器、提手和两根内容线表示删除项目。',p('M6 8.5h20M12 8.5V5h8v3.5','secondary')+p('m8 8.5 1 18h14l1-18','structure')+p('M13 13v9M19 13v9'))
reg('action-accept','应用 / 验证','连贯勾线表示应用或验证通过。',p('m6.5 16.5 6 6 13-14'))
reg('action-next','下一项','向右的方向尖角表示顺序前进。',p('m12 7 9 9-9 9'))
reg('action-previous','上一项','向左的方向尖角表示顺序后退。',p('m20 7-9 9 9 9'))
reg('action-move-up','上移','行条目与向上箭头表示将项目向上调整。',p('M5 22h10M5 27h10','structure')+p('M23 25V6m-5 5 5-5 5 5'))
reg('action-move-down','下移','行条目与向下箭头表示将项目向下调整。',p('M5 5h10M5 10h10','structure')+p('M23 7v19m-5-5 5 5 5-5'))
reg('disclosure-expand','展开','向下的短尖角表示展开内容。',p('m8 12 8 8 8-8'))
reg('disclosure-collapse','收起','向上的短尖角表示收起内容。',p('m8 20 8-8 8 8'))
reg('disclosure-closed','展开分组','向右的短尖角表示折叠分组可展开。',p('m12 8 8 8-8 8'))
reg('action-more','更多操作','三个等距实心点表示更多操作菜单。',c(7,16,1.45,'signal-fill')+c(16,16,1.45,'signal-fill')+c(25,16,1.45,'signal-fill'))
reg('action-search','搜索','放大镜圆框与延伸柄表示查找。',c(13,13,8,'structure')+p('m19 19 8 8')+p('M9 11a4 4 0 0 1 4-2','secondary'))
reg('action-pin','固定面板','固定针的头部、横肩与针尖表示固定面板。',p('M10 4.5h12l-2 8 5 5H7l5-5Z','structure')+p('M16 17.5v11M11.5 13h9','signal'))
reg('action-run','开始运行','向前的播放三角表示开始运行。',p('M9 5.5v21l17-10.5Z'))
reg('action-stop','停止运行','独立方形表示停止运行。',r(7,7,18,18,'signal',2))
reg('action-recalculate','重新计算 / 刷新','首尾衔接的环形箭头表示更新计算或刷新。',p('M26 13A10.5 10.5 0 0 0 7 8m0-4v4h4M6 19a10.5 10.5 0 0 0 19 5m0 4v-4h-4'))
reg('action-reset-view','重置视图','回转箭头围绕中心定位点，表示恢复视图位置。',p('M7 11a10 10 0 1 1-1 8M7 5v6h6')+c(16,17,1.5,'secondary-fill'))
reg('action-fit-view','适配窗口','四角框与向外定位短线表示完整内容适配窗口。',p('M4.5 12V4.5H12M20 4.5h7.5V12M27.5 20v7.5H20M12 27.5H4.5V20','structure')+p('m10 10 4 4m8-4-4 4m-8 8 4-4m8 4-4-4','signal'))
for id_,sign,label in [('action-zoom-in',True,'放大'),('action-zoom-out',False,'缩小')]:
    reg(id_,label,'放大镜与'+('加号' if sign else '减号')+'表示'+label+'视图。',c(13,13,8,'structure')+p('m19 19 8 8')+p('M9 13h8'+('M13 9v8' if sign else '')))
reg('action-undo','撤销','向左回转箭头表示恢复上一步。',p('M6 13h12a9 9 0 0 1 9 9M6 13l7-7M6 13l7 7'))
reg('action-redo','重做','向右回转箭头表示重做下一步。',p('M26 13H14a9 9 0 0 0-9 9M26 13l-7-7M26 13l-7 7'))
reg('file-new','新建工程','空白工程页和加号表示建立新工程。',PAGE+p('M11 19h10M16 14v10'),category='文件与工作区')
reg('file-open','打开 / 载入','文件夹与进入的箭头表示打开已有文件。',FOLDER+p('M15 2v14m-4-4 4 4 4-4'),category='文件与工作区')
reg('file-save','保存','存储外壳、顶端记录槽及底部标签区表示保存。',p('M5 4.5h19l3.5 4v19H4.5V5Z','structure')+r(10,4.5,11,8,'signal',.5)+r(10,20,12,7.5,'secondary',.5),category='文件与工作区')
reg('file-import','导入','箭头从页外进入文档内，表示导入数据。',p('M20 5h7v23H5V5h6','structure')+p('M16 2v17m-5-5 5 5 5-5')+p('M10 24h12','secondary'),category='文件与工作区')
reg('file-export','导出','箭头从文档内部向外离开，表示导出数据。',p('M11 5H5v23h22V5h-7','structure')+p('M16 20V2m-5 5 5-5 5 5')+p('M10 24h12','secondary'),category='文件与工作区')
reg('file-export-text','导出文本','带文本行的页面及向外箭头，表示文本数据导出。',p('M21 4H5v24h22V16','structure')+p('M9 10h7M9 15h5M9 21h12','secondary')+p('M18 13 28 3M20 3h8v8'),category='文件与工作区')
reg('file-export-image','导出 PNG','图像框中的景物与离开边框的箭头表示导出图像。',p('M17 5H4.5v22.5H28V16','structure')+p('m7 24 6-9 5 5 3-3 4 7','secondary')+c(10,10,1.5,'secondary')+p('M20 12 28 4M21 4h7v7'),category='文件与工作区')
reg('file-import-image','导入 PNG','图像框与进入画框的箭头表示导入位图。',p('M18 5H4.5v22.5H28V16','structure')+p('m7 24 6-9 5 5 3-3 4 7','secondary')+p('M28 3 19 12M19 5v7h7'),category='文件与工作区')
reg('file-export-report','导出报告 / PDF','带标题、文本和小图表的文档及向外箭头表示报告导出。',p('M19 4H5v24h22V14','structure')+p('M9 10h7M9 15h5M9 23v-4m4 4v-6m4 6v-3','secondary')+p('M19 12 28 3M21 3h7v7'),category='文件与工作区')
reg('clipboard-copy-text','复制文本','剪贴板与文本横线表示复制文本。',p('M11 5H6v23h21V5h-5','structure')+r(11,3.5,11,5,'secondary',1)+p('M10 14h12M10 19h12M10 24h8'),category='文件与工作区')
reg('action-copy','复制项目','两张错位页面表示复制项目。',p('M10 22H4.5V4.5H22V10','structure')+r(10,10,17.5,17.5,'signal',1.5)+p('M15 16h8M15 21h6','secondary'))
reg('action-open-link','厂商页面','页面角框与向外箭头表示前往外部页面。',p('M15 5H5v22h22V17','structure')+p('M15 17 28 4M20 4h8v8'))
reg('help-topic','帮助','问号与信息圆框表示查看当前功能帮助。',c(16,16,11.5,'structure')+p('M12.5 11.5a3.5 3.5 0 0 1 7 .5c0 2.5-3.5 3.2-3.5 5.5')+c(16,23,1,'secondary-fill'))
reg('information-note','信息说明','信息圆框与独立信息符号表示说明。',c(16,16,11.5,'structure')+p('M16 14v9')+c(16,9,1.1,'secondary-fill'))
reg('analysis-warning','分析异常','带感叹号的分析图框表示结果异常提示。',r(4.5,5,23,22)+p('M8 23h6M9 18l3-4 3 2','secondary')+p('M22 9v9')+c(22,23,1,'signal-fill'),category='分析与科学')
reg('settings-general','设置','三个独立调节通道表示可调整设置。',p('M5 8h22M5 16h22M5 24h22','structure')+r(10,5,4,6,'signal',1)+r(20,13,4,6,'secondary',1)+r(8,21,4,6,'signal',1))
reg('format-font','格式与字体','大小字形骨架和基线表示字体与排版格式。',p('M4 8h15M11.5 8v19M7 27h9')+p('M21 14h8M25 14v13','secondary')+p('M4 4h24','structure'))
reg('settings-language','语言','经纬网球与对话框表示界面语言切换。',c(13,13,9,'structure')+p('M4 13h18M13 4c-5 4-5 14 0 18M13 4c5 4 5 14 0 18','secondary')+p('M18 19h10v8h-6l-4 3Z'))
reg('edit-target','编辑设计目标','目标曲线与铅笔表示编辑目标值。',p('M4.5 5.5v22H16','structure')+p('M8 21 13 14l6 3 5-9','secondary')+p('m18 27 1-4 7-7 3 3-7 7Z M25 17l3 3'))

# Domain objects and workspace operations.
reg('system-aperture','系统孔径','受光阑约束的开口与两侧边界表示系统孔径。',p('M5 4v24M27 4v24M5 10h8M19 10h8M5 22h8M19 22h8','structure')+p('M13 8v16M19 8v16')+p('M2 16h28','secondary'),category='光学对象')
reg('optical-system','光学系统','共轴的两组透镜与光轴表示光学系统。',p('M2.5 16h27','structure')+p('M8 5c-4 4-4 18 0 22M11 5c4 4 4 18 0 22')+p('M21 8c3 4 3 12 0 16M24 8c-3 4-3 12 0 16','secondary'),category='光学对象')
reg('field-settings','视场','三个场点及其指向光轴的光束扇表示视场。',p('M5 5v22M25 8v16','structure')+p('M7 6 25 16M7 16h18M7 26 25 16')+c(7,6,1.1,'secondary-fill')+c(7,16,1.1,'secondary-fill')+c(7,26,1.1,'secondary-fill'),category='光学对象')
reg('wavelength-settings','波长','完整正弦波与一个周期的跨度标记表示波长。',p('M3 18c3-12 6-12 9 0s6 12 9 0 6-12 9 0')+p('M6 5h16M6 3v4M22 3v4','structure'),category='光学对象')
reg('wavelength-weights','波长与权重','波形和高低权重柱表示光谱采样及权重。',p('M3 13c2-8 4-8 6 0s4 8 6 0 4-8 6 0','signal')+p('M7 27v-7M15 27V17M23 27V9','secondary')+p('M4 28h24','structure'),category='光学对象')
reg('polarization-settings','偏振','传播轴、正交电场和椭圆轨迹表示偏振。',p('M3 25 27 7M16 3v25M3 16h26','structure')+p('M9 7c9-8 23 11 14 18S0 15 9 7Z')+p('M12 11 20 21','secondary'),category='光学对象')
reg('materials-library','材料库','材料层片与玻璃棱块表示光学材料库。',p('m5 8 11-4 11 4-11 4ZM5 14l11 4 11-4M5 20l11 4 11-4','structure')+p('M16 12v12')+p('m21 23 5-2v6l-5 2-5-2v-6Z','secondary'),category='光学对象')
reg('environment-settings','环境','温度计与压力刻度表示光学环境参数。',p('M10 5a3 3 0 0 1 6 0v14a5 5 0 1 1-6 0Z','structure')+p('M13 11v13')+c(13,24,1.6,'signal-fill')+p('M22 6h6M22 12h4M22 18h6M22 24h4','secondary'),category='光学对象')
reg('lens-prescription','镜头数据','透镜形状所在的数据表表示镜头处方编辑。',TABLE+p('M17 15c-2 2-2 6 0 8M21 15c2 2 2 6 0 8')+p('M7 16h2M7 22h2','secondary'),category='文件与工作区')
reg('system-settings','系统选项','共轴透镜和可调节通道表示系统级设置。',LENS+p('M4 5h7M22 27h7','secondary')+c(7,5,1.4,'signal')+c(25,27,1.4,'signal'),category='光学对象')
reg('configuration-matrix','多重结构','表格列头与两组处方列表示多重结构矩阵。',TABLE+p('M17 5v22M22 5v22','structure')+p('M7 16h2M7 22h2M15 16h9M15 22h9')+c(20,8,1,'secondary-fill'),category='文件与工作区')
reg('layout-2d','2D 视图','侧视透镜截面与折射光线表示二维布局。',p('M3 16h26M12 5v22M22 7v18','structure')+p('M5 7 12 9 22 13 28 16M5 25 12 23 22 19 28 16')+p('M15 5c-3 4-3 18 0 22','secondary'),category='光学对象')
reg('layout-3d','3D 视图','透镜的立体椭圆边缘与空间光束表示三维布局。',p('M12 5c-8 0-8 22 0 22s8-22 0-22ZM24 8c-6 0-6 16 0 16s6-16 0-16Z','structure')+p('M4 10 12 11 24 14 29 16M4 23 12 22 24 18 29 16')+p('M12 5 24 8M12 27l12-3','secondary'),category='光学对象')
reg('solid-model','实体模型','透镜机械厚度、端面及剖面表示实体模型。',p('M7 6q9-5 18 0v20q-9 5-18 0ZM7 6q9 5 18 0M7 26q9-5 18 0','structure')+p('M12 9v14M17 11v10M22 10v12','secondary')+p('M3 16h26'),category='光学对象')
reg('nonsequential-mode','非序列模式','分支光线及反射边界表示非序列传播。',p('M23 3v26M3 6 23 16 5 25')+p('M10 10 23 16l-8 2','secondary')+p('M27 5v22','structure'),category='光学对象')
reg('sequential-mode','顺序模式','按顺序穿过三个光学面的连续光线表示顺序模式。',p('M8 4v24M16 6v20M24 4v24','structure')+p('M3 7 8 9 16 13 24 16l5 3')+p('m26 15 3 4-5 1','secondary'),category='光学对象')
reg('nonsequential-objects','对象数据','球、柱及倾斜面所在表格表示非序列对象编辑。',r(3.5,4.5,25,23)+p('M3.5 11h25M11 4.5v23','structure')+c(17,17,2.4,'signal')+p('m23 15 3 2-3 3-3-3ZM7 15v9','secondary'),category='光学对象')
reg('layout-dock','保留分栏停靠','相邻分栏和共同外框表示保留停靠布局。',r(3.5,5,25,22)+p('M13 5v22M3.5 11h25')+p('M17 16h7M17 21h7','secondary'),category='文件与工作区')
reg('layout-merge','合并单窗格','两个窗格的指向箭头汇入单一中央窗格。',r(9,8,14,19)+p('M3 4h10M19 4h10','structure')+p('M3 10h6m-3-3 3 3-3 3M29 10h-6m3-3-3 3 3 3')+p('M13 14h6M13 20h6','secondary'),category='文件与工作区')
reg('layout-float','全部独立浮动','分离窗口和斜向移出箭头表示浮动。',r(3.5,5,17,17)+r(12,13,16.5,15,'secondary')+p('M18 10 27 2M20 2h7v7'),category='文件与工作区')
reg('layout-tile','平铺全部','四个并列且互不覆盖的窗格表示平铺。',r(4.5,5,9,9)+r(18.5,5,9,9)+r(4.5,19,9,9)+r(18.5,19,9,9)+p('M7 8h4M21 22h4','signal'),category='文件与工作区')
reg('layout-cascade','层叠全部','三个有序错位窗口表示层叠排列。',p('M19 5H4.5v16M24 10H9.5v16','structure')+r(14,15,14,13,'signal',1)+p('M18 19h6','secondary'),category='文件与工作区')

# Scientific plot vocabulary: axes are neutral; actual analysis features own the signal colour.
def method_mark(kind,x=21,y=5):
    if kind=='fourier':return p(f'M{x} {y+4}v-2m3 3V{y}m3 5v-3','secondary')
    if kind=='huygens':return p(f'M{x} {y+5}a5 5 0 0 1 5-5M{x+3} {y+5}a2 2 0 0 1 2-2','secondary')
    if kind=='geometric':return p(f'M{x} {y}l6 3-6 3M{x} {y+3}h6','secondary')
    return ''
def field_mark():return p('M5 4 14 7 5 10M5 7h9','secondary')
def focus_mark():return p('M6 4v6M13 4v6M5 7h9','secondary')
def wave_mark():return p('M4 7c1.5-4 3-4 4.5 0s3 4 4.5 0','secondary')
for method,mlabel in [('fourier','傅里叶'),('huygens','惠更斯'),('geometric','几何')]:
    for domain,dlabel in [('frequency','MTF'),('focus','离焦 MTF'),('field','MTF VS 视场')]:
        if domain=='frequency':feature=p('M7 13C10 14 12 21 17 24s7 2 10 2')+p('M7 15c5 7 9 11 20 12','secondary',.85);badge=wave_mark();meaning='下降的调制传递曲线表示频率响应'
        elif domain=='focus':feature=p('M7 26c4 0 4-12 10-12s6 12 10 12')+p('M7 27c5 0 5-9 10-9s6 9 10 9','secondary',.8)+p('M17 12v15','structure',.55,'1 2.5');badge=focus_mark();meaning='具有最大值的穿焦曲线及焦面参考线表示离焦扫描'
        else:feature=p('M7 14c8-1 12 0 20 10')+p('M7 18c8-1 12 1 20 8','secondary',.85)+c(7,14,1,'signal-fill')+c(17,16.2,1,'signal-fill')+c(27,24,1,'signal-fill');badge=field_mark();meaning='离散场点和向边缘变化的传递曲线表示随视场扫描'
        reg(f'mtf-{method}-{domain}',mlabel+dlabel,meaning+'；'+{'fourier':'采样频谱短线标识傅里叶方法。','huygens':'次波圆弧标识惠更斯传播。','geometric':'会聚几何光线标识几何方法。'}[method],AX_LOW+badge+method_mark(method)+feature,category='MTF 分析')

def plot_icon(id_,label,meaning,curve,marker='',secondary=''):
    reg(id_,label,meaning,AX+marker+p(curve)+(p(secondary,'secondary') if secondary else ''),category='分析与科学')
def page_icon(id_,label,meaning,inside):reg(id_,label,meaning,PAGE+inside,category='报告与数据')
page_icon('report-surface-data','表面数据报告','文档中的光学面列和处方表表示逐面数据。',p('M9 14v10M15 14v10M21 14v10M9 18h14M9 23h14')+p('M9 8h6','secondary'))
page_icon('report-system-data','系统数据报告','文档中的共轴透镜及汇总行表示系统参数报告。',p('M9 17h14M14 12c-3 3-3 7 0 10M17 12c3 3 3 7 0 10')+p('M10 25h11','secondary'))
page_icon('report-classified-data','分类数据报告','树状分支和三类条目表示分类数据。',p('M10 13v11M10 14h5M10 19h5M10 24h5','structure')+p('M18 14h5M18 19h5M18 24h5')+c(10,10,1,'secondary-fill'))
page_icon('report-system-summary','系统数据摘要','少量关键指标与分隔线表示系统摘要。',p('M9 15h14M9 22h14','structure')+p('M10 12h5M18 12h4M10 19h5M18 19h4M10 26h12','signal'))
reg('report-cardinal-points','基面数据','光轴、主面及两侧基点表示光学基面数据。',p('M3 17h26M12 5v23M20 5v23','structure')+p('M15 7c-3 4-3 15 0 19M17 7c3 4 3 15 0 19')+c(6,17,1.5,'secondary-fill')+c(26,17,1.5,'secondary-fill')+p('M6 12v10M26 12v10','secondary'),category='分析与科学')
reg('ray-single-trace','单光线追迹','穿过两面并折转的单根光线表示逐面追迹。',p('M10 4v24M21 4v24','structure')+p('M3 6 10 9 21 14l8 7')+c(10,9,1.2,'secondary-fill')+c(21,14,1.2,'secondary-fill'),category='分析与科学')
reg('spot-standard','标准点列图','统一像面框内的聚焦点簇表示点列分布。',p('M4 11V4h7M21 4h7v7M28 21v7h-7M11 28H4v-7','structure')+''.join(c(x,y,rad,'signal-fill' if k%2==0 else 'secondary-fill') for k,(x,y,rad) in enumerate([(15,15,1.2),(18,12,1),(12,18,1.1),(18,19,1.2),(11,12,.9),(22,16,.9),(15,23,.9)])),category='分析与科学')
reg('ray-footprint','光迹图','倾斜入射光束在圆形表面上的足迹表示光线覆盖。',c(20,17,9,'structure')+p('M3 5 15 13M3 12l12 5M3 19l12 2')+p('M16 11c6-2 10 8 4 11s-10-8-4-11Z','secondary'),category='分析与科学')
reg('spot-through-focus','离焦点列图','三个轴向像面上的大、小、大点斑表示离焦变化。',p('M7 6v21M16 6v21M25 6v21','structure')+c(7,17,3.5,'secondary')+c(16,17,1.5,'signal')+c(25,17,3.5,'secondary')+p('M4 29h24','structure'),category='分析与科学')
reg('spot-full-field','全视场点列图','场点扇和多个位置的点簇表示全视场点列。',p('M3 3 13 7 3 11M3 7h10','secondary')+r(16,4,12,24)+''.join(c(x,y,.9,'signal-fill') for x,y in [(21,9),(24,10),(20,16),(23,17),(20,23),(24,24)]),category='分析与科学')
reg('spot-field-matrix','矩阵点列图','九格中各自独立的点斑表示视场矩阵点列。',r(4,4,24,24)+p('M12 4v24M20 4v24M4 12h24M4 20h24','structure')+''.join(c(x,y,.9,'signal-fill') for x,y in [(8,8),(16,8),(24,8),(8,16),(16,16),(24,16),(8,24),(16,24),(24,24)]),category='分析与科学')
reg('spot-configuration-matrix','结构矩阵点列图','双层列头与点斑网格表示按结构和视场排列的矩阵。',r(3.5,4,25,24)+p('M3.5 10h25M10 4v24M19 10v18M3.5 19h25','structure')+p('M13 7h3M22 7h3','secondary')+''.join(c(x,y,1.1,'signal-fill') for x,y in [(14.5,14.5),(23.5,14.5),(14.5,23.5),(23.5,23.5)]),category='分析与科学')
reg('vignetting','渐晕图','边缘光束被孔径截断，表示渐晕。',p('M20 3v9M20 22v7M25 3v26','structure')+p('M3 5 20 12M3 16h22M3 27l17-5')+p('M20 12 28 9M20 22l8 3','secondary',.5,'1 2'),category='分析与科学')
plot_icon('incidence-vs-height','入射角 vs. 像高','曲线与入射方向夹角标记表示入射角随像高变化。','M8 24 13 22 19 16 27 7',p('M7 7h10M7 7l7 7M12 7a5 5 0 0 1-1.5 3.5','secondary'))
plot_icon('y-ybar','Y-Ybar','两条光线高度轨迹及交点表示 Y–Ybar 关系。','M7 23 26 8',secondary='M7 10 26 23')
plot_icon('aberration-ray-fan','光线像差图','横向光瞳坐标与异号像差扇曲线表示光线像差。','M7 10c4 0 5 13 10 13s5-13 10-13',p('M5 17h23','structure',.5),secondary='M7 24c7 0 10-14 20-14')
reg('aberration-grid-distortion','网格畸变','弯曲的正交格线表示成像网格畸变。',p('M5 5q11 3 22 0M4 16q12 0 24 0M5 27q11-3 22 0M5 5q3 11 0 22M16 4v24M27 5q-3 11 0 22','structure')+p('M5 5q11 3 22 0M27 5q-3 11 0 22'),category='分析与科学')
plot_icon('aberration-field-curvature','场曲 / 畸变','成对弯曲焦面与场点标记表示场曲和畸变。','M11 6c15 7 15 14 0 21',field_mark(),secondary='M20 6c-7 7-7 14 0 21')
reg('aberration-axial','轴向像差','不同孔径高度光线沿光轴落在不同交点，表示轴向像差。',p('M3 18h26M10 4v24','structure')+p('M3 6 10 8 20 18M3 11l7 2 16 5')+c(20,18,1.3,'secondary-fill')+c(26,18,1.3,'secondary-fill'),category='分析与科学')
reg('aberration-lateral-color','垂轴色差','同一像面上分离的两个像点表示垂轴色差。',p('M11 4v24M27 4v24M3 16h26','structure')+p('M3 7 11 10 27 12M3 7l8 4 16 9','signal')+c(27,12,1.4,'secondary-fill')+c(27,20,1.4,'secondary-fill'),category='分析与科学')
reg('aberration-color-focus','色焦移','两种波形的轴向聚焦位置偏移表示色焦移。',p('M3 19h26M10 5v22','structure')+p('M3 6 10 9 19 19')+p('M3 11l7 2 18 6','secondary')+p('M19 23v5M28 23v5M19 26h9'),category='分析与科学')
page_icon('seidel-coefficients','赛德尔系数','系数表的五个独立条目表示五项初级像差系数。',p('M10 13h3M10 17h3M10 21h3M10 25h3','secondary')+p('M17 13h6M17 17h4M17 21h7M17 25h3'))
plot_icon('seidel-diagram','赛德尔图','跨越零线的不同高度柱表示有符号的赛德尔像差贡献。','M9 18V9h3v9M17 18v7h3v-7M25 18V5h3v13',p('M5 18h24','secondary'))
reg('aberration-pupil','光瞳像差','光瞳圆上的参考节点与偏离节点表示瞳像差。',c(16,16,11,'structure')+p('M5 16h22M16 5v22','structure',.55)+c(12,11,1,'secondary')+c(19,11,1,'secondary')+c(12,21,1,'secondary')+c(19,21,1,'secondary')+p('m12 11-3-2m10 2 3-1m-10 11-2 2m9-2 3 3'),category='分析与科学')
reg('aberration-full-field','全视场像差','多个场点上的局部像差扇表示全视场分析。',r(4,4,24,24)+p('M12 4v24M20 4v24M4 12h24M4 20h24','structure',.5)+p('m6 9 2-2 2 2m4 1 2-5 2 5m4 0 2-6 2 6m-20 8 2-3 2 3m4 1 2-6 2 6m4 0 2-5 2 5','signal'),category='分析与科学')

for domain,label,marker in [('field','RMS vs. 视场',field_mark()),('wavelength','RMS vs. 波长',wave_mark()),('focus','RMS vs. 离焦',focus_mark())]:
    plot_icon('rms-'+domain,label,'底部有最小值的 RMS 曲线与'+{'field':'视场扇','wavelength':'光谱波形','focus':'焦面跨度'}[domain]+'表示对应自变量。','M7 11c4 17 11 19 20-3',marker,secondary='M7 15c4 12 11 14 20 0')
reg('rms-field-map','二维视场 RMS 图','视场坐标框内的等值环和采样点表示二维 RMS 分布。',r(4.5,4.5,23,23)+p('M5 16h22M16 5v22','structure',.5)+p('M9 21c-6-10 8-17 14-8s-8 18-14 8Z')+c(16,16,3.3,'secondary')+c(22,8,1,'secondary-fill'),category='分析与科学')

for method,label in [('fourier','FFT'),('huygens','惠更斯')]:
    reg('psf-'+method,label+' PSF','中央点峰与同心衍射环表示二维点扩散函数；方法标记区分计算族。',c(15,18,9.5,'structure')+c(15,18,6,'secondary')+c(15,18,2.7,'signal')+c(15,18,.8,'signal-fill')+method_mark(method),category='分析与科学')
    plot_icon('psf-'+method+'-section',label+' PSF 截面图','窄主峰与较弱旁瓣表示点扩散函数的截面。','M7 26c2-8 4 0 6-3s1-17 4-17 2 14 4 17 4-5 6 3',method_mark(method),secondary='M7 27h20')
def spread_icon(id_,label,method):
    reg(id_,label,'峰形线扩散与阶跃边缘扩散共用坐标，表示线/边缘响应。',AX+p('M7 25c4 0 4-14 8-14s4 14 8 14')+p('M7 23c12 0 7-16 20-16','secondary')+method_mark(method),category='分析与科学')
spread_icon('spread-fourier','FFT 线 / 边缘扩散','fourier')
spread_icon('spread-geometric','几何线 / 边缘扩散','geometric')
for method,label in [('huygens','衍射圈入能量'),('geometric','几何圈入能量')]:
    reg('encircled-energy-'+method,label,'能量包围圈与随半径累积上升的曲线表示圈入能量。',AX+c(12,12,5.5,'structure')+c(12,12,2.6,'secondary')+p('M7 25c7 0 7-10 20-10')+method_mark(method),category='分析与科学')
reg('encircled-energy-extended','扩展光源圈入能量','扩展面源及其包围圈表示扩展光源的能量累积。',r(4,9,7,14,'structure',.7)+p('M6 12h3M6 16h3M6 20h3','secondary')+c(22,16,8.5,'structure')+c(22,16,4.5,'signal')+p('M11 11l8 2M11 21l8-2','secondary'),category='分析与科学')
reg('wavefront-opd','光程差图','两个传播路径相对参考线的程差表示 OPD。',p('M4 5v23M28 5v23M4 16h24','structure')+p('M4 10c6-9 14 9 24 0')+p('M4 22c8-9 14 8 24 0','secondary')+p('M18 10v12M16 10h4M16 22h4'),category='分析与科学')
reg('wavefront-map','波前图','光瞳边界和偏心等程差轮廓表示波前分布。',c(16,16,11.5,'structure')+p('M7 20c1-14 19-18 18-4S8 30 7 20Z')+p('M12 19c-1-6 9-11 10-5s-9 12-10 5Z','secondary'),category='分析与科学')
reg('wavefront-interferogram','干涉图','光瞳内相邻亮暗条带的轮廓表示干涉条纹。',c(16,16,11.5,'structure')+p('M7 9c7 1 5 14 14 15M10 6c8 3 5 14 14 16M16 5c6 4 4 10 11 13')+p('M5 15c5 1 4 10 10 12','secondary'),category='分析与科学')
reg('wavefront-foucault','傅科分析','刀口遮挡半瞳与收敛光束表示傅科刀口检验。',c(11,16,7.5,'structure')+p('M11 8.5v15M19 11l9 5-9 5')+p('M26 4v12M26 16h4v12','secondary'),category='分析与科学')
reg('contrast-loss','对比度损失图','两组幅度不同的周期信号表示对比度下降。',p('M4 5v22h24','structure')+p('M7 10h3v10h3V10h3v10h3')+p('M21 14h3v4h3v-4h2','secondary'),category='分析与科学')
for basis,label in [('fringe','Zernike Fringe 系数'),('standard','Zernike Standard 系数'),('annular','Zernike Annular 系数')]:
    pupil=c(10,11,6,'structure')+(c(10,11,2.5,'secondary') if basis=='annular' else p('M4 11h12M10 5v12','secondary') if basis=='fringe' else p('M5.5 8.5 14.5 13.5M5.5 13.5 14.5 8.5','secondary'))
    reg('zernike-'+basis,label,'系数表配合'+{'fringe':'径向十字瞳标记表示 Fringe 约定','standard':'对角模态标记表示 Standard 约定','annular':'带中心遮拦的环形瞳表示 Annular 基底'}[basis]+'。',pupil+r(19,5,9,22,'structure',.5)+p('M19 12h9M19 19h9','structure')+p('M21 9h4M21 16h4M21 23h4'),category='分析与科学')
plot_icon('zernike-vs-field','Zernike 系数 vs. 视场','不同模态系数随场点变化的有符号曲线表示视场系数扫描。','M7 24 13 19 20 18 27 8',field_mark(),secondary='M7 18 14 17 21 23 27 25')

# Imaging, source and detector analysis have explicit source -> propagation -> image semantics.
IMAGE_FRAME=r(4,5,24,22)+p('m7 24 6-9 5 5 3-3 4 7','secondary')+c(10,10,1.4,'secondary')
reg('image-simulation','图像模拟','物方图形经透镜投影到像面，表示光学图像模拟。',p('M4 7v18M15 4v24M28 7v18','structure')+p('M5 10 15 12 27 22M5 22l10-2L27 10')+p('M18 7c-3 4-3 14 0 18','secondary')+p('M2 10h4M2 22h4M26 10h4M26 22h4','secondary'),category='分析与科学')
reg('image-geometric','几何图像分析','对象点通过几何光线在像面形成离散像点。',p('M12 4v24M28 5v23','structure')+c(4,8,1.5,'secondary-fill')+p('M4 8 12 9 27 18M4 8l8 8 15 5')+c(27,18,1.2,'signal-fill')+c(27,21,1.2,'signal-fill'),category='分析与科学')
reg('image-geometric-bitmap','几何位图图像分析','输入像素格及像面上的离散光斑表示位图追迹。',r(3,6,9,20)+p('M3 13h9M3 20h9M7.5 6v20','structure')+p('M14 9 23 14M14 23l9-5','secondary')+r(23,6,6,20)+c(25,12,1,'signal-fill')+c(27,17,1,'signal-fill')+c(25,22,1,'signal-fill'),category='分析与科学')
reg('source-analysis','光源分析','发光面及不同角度射出的光束表示光源分布。',p('M4.5 7v18M2 10h5M2 22h5','structure')+p('M8 8 27 3M8 13l19-3M8 19l19 3M8 24l19 5')+p('M16 7c6 4 6 14 0 18','secondary'),category='分析与科学')
reg('image-partial-coherence','部分相干图像分析','两组部分重叠的次波轮廓及不同相位表示部分相干叠加。',c(11,15,8,'structure')+c(21,17,8,'structure')+p('M4 15c2-6 4-6 6 0s4 6 6 0')+p('M16 17c2-6 4-6 6 0s4 6 6 0','secondary'),category='分析与科学')
reg('image-extended-diffraction','扩展图像分析','多点扩展对象和传播到像面的波纹表示扩展衍射成像。',r(3,7,6,18,'structure',.5)+c(6,11,1,'secondary-fill')+c(6,16,1,'secondary-fill')+c(6,21,1,'secondary-fill')+p('M12 7c7 3 7 15 0 18M17 5c10 4 10 18 0 22')+p('M28 4v24','structure'),category='分析与科学')
plot_icon('relative-illumination','相对照度','随视场边缘逐渐下降的照度和环形场点标记表示相对照度。','M7 8c11 0 12 5 20 17',field_mark(),secondary='M7 11c10 0 11 5 20 16')
reg('viewer-ima-bim','IMA / BIM 图片浏览器','图像页与像素小格表示光学 IMA/BIM 图像数据。',PAGE+p('m9 24 5-7 4 4 4-5 2 8')+c(11,13,1.2,'secondary-fill')+p('M21 13h3M21 16h3','secondary'),category='文件与工作区')
reg('viewer-bitmap','位图文件查看器','边框中的离散像素与景物表示位图查看。',IMAGE_FRAME+r(20,7,3,3,'signal',.2)+r(24,11,3,3,'signal',.2),category='文件与工作区')
reg('detector-view','探测器查看器','像素阵列和选定探测点表示探测器数据。',r(4,4,24,24)+p('M12 4v24M20 4v24M4 12h24M4 20h24','structure',.65)+r(12,12,8,8,'signal',0)+c(16,16,1.3,'secondary-fill'),category='分析与科学')
reg('detector-clear','清空探测器','探测器像素阵列与清除斜线表示清空记录。',p('M27 15V4H4v24h12M12 4v24M20 4v11M4 12h23M4 20h11','structure')+p('m18 23 6-6 5 5-6 6h-5Z M23 28h6','signal'),category='分析与科学')
reg('trace-control','追迹控制','发射光线旁的播放控制表示启动非序列追迹。',p('M4 5v22M4 8 18 4M4 16h13M4 24l13 4','structure')+p('M20 11v13l10-6.5Z')+c(4,16,1.3,'secondary-fill'),category='分析与科学')
reg('ray-database','光线数据库','分层存储和定向光线表示光线数据库。',p('M5 8c0-5 22-5 22 0s-22 5-22 0ZM5 8v17c0 5 22 5 22 0V8M5 15c0 4 22 4 22 0','structure')+p('M9 22h14m-4-4 4 4-4 4'),category='分析与科学')
reg('ray-path-analysis','路径分析','有方向的分支传播及交互节点表示路径分析。',p('M4 7h10v9h14M14 16v10h14','structure')+p('m24 12 4 4-4 4m0 2 4 4-4 4')+c(14,7,1.4,'secondary-fill')+c(14,16,1.4,'secondary-fill')+c(14,26,1.4,'secondary-fill'),category='分析与科学')

# Material, optimization and engineering actions keep the named object in the glyph.
reg('material-dispersion-chart','色散图 / 材料分析','材料坐标上的离散玻璃样本表示材料色散分析。',AX+''.join(c(x,y,1.2,'signal-fill' if k%2 else 'secondary-fill') for k,(x,y) in enumerate([(9,9),(12,16),(17,13),(20,21),(24,18),(27,7)])),category='材料与制造')
plot_icon('material-dispersion-wavelength','色散 vs. 波长','折射率随波长逐渐下降的色散曲线配光谱标记。','M7 9c4 12 9 15 20 16',wave_mark(),secondary='M7 13c5 10 10 11 20 12')
reg('material-glass-map','玻璃图','玻璃样本分区中的多组材料点表示玻璃图。',AX+p('M7 22 14 8h14','structure',.6)+''.join(c(x,y,1.2,'signal-fill') for x,y in [(10,18),(14,20),(17,12),(21,15),(24,9)])+c(25,23,1,'secondary-fill'),category='材料与制造')
reg('material-athermal-map','无热化玻璃图','温度计与不同热响应材料轨迹表示无热化选材。',AX+p('M7 23 15 12 26 7')+p('M7 16 17 21 27 12','secondary')+p('M24 3v5','secondary')+c(24,8.5,1.5,'secondary'),category='材料与制造')
plot_icon('material-transmission','内部透过率 vs. 波长','透过率平台与吸收谷表示材料光谱透过率。','M7 9h6c3 0 1 15 5 15s1-15 6-15h3',wave_mark())
reg('material-copy-glass','复制玻璃','玻璃块及重叠副本表示复制材料条目。',p('m4 8 8-4 8 4-8 4ZM4 8v10l8 4V12','structure')+p('m13 16 8-4 8 4-8 4ZM13 16v10l8 4 8-4V16')+p('M21 20v10','secondary'),category='材料与制造')
page_icon('material-glass-report','复制玻璃报告','报告页面中的玻璃截面和数据行表示材料报告。',p('m10 15 6-3 6 3-6 3ZM10 15v5l6 3 6-3v-5M16 18v5')+p('M10 26h12','secondary'))
page_icon('catalog-data-sheet','数据表','文档中的表格和参数行表示厂商数据表。',p('M9 14h14M9 19h14M9 24h14M15 14v10','signal')+p('M9 9h6','secondary'))
reg('material-nd-vd','计算 Nd / Vd','透镜两侧的光谱采样与数据求取标记表示折射率和阿贝数计算。',LENS+p('M4 6h5M4 10h5M23 21h5M23 25h5','secondary')+p('M6.5 3v10M25.5 18v10'),category='材料与制造')
reg('lens-library','镜头库','多片透镜装在分格柜中，表示镜头设计库。',r(3.5,4.5,25,23)+p('M3.5 12h25M15.5 12v15','structure')+p('M8 16c-3 2-3 6 0 8M11 16c3 2 3 6 0 8M21 16c-3 2-3 6 0 8M24 16c3 2 3 6 0 8'),category='材料与制造')
reg('stock-lens-catalog','库存镜头查看','装盒镜片与浏览放大镜表示库存镜头目录。',p('m4 9 10-5 10 5-10 5ZM4 9v15l10 5V14M24 9v7','structure')+p('M9 11c-2 2-2 7 0 9','secondary')+c(23,23,5,'signal')+p('m27 27 3 3'),category='材料与制造')
reg('stock-lens-match','库存镜头匹配','目标镜片与库内镜片之间的匹配勾表示镜头匹配。',p('M7 5c-5 4-5 18 0 22M10 5c5 4 5 18 0 22','structure')+p('M23 7c-4 3-4 15 0 18M26 7c4 3 4 15 0 18','structure')+p('m13 16 3 3 6-7')+p('M5 29h23','secondary'),category='材料与制造')
reg('radius-variables','设全部半径变量','弧形表面、曲率半径连线与控制点表示半径变量。',p('M21 4c-13 6-13 18 0 24','structure')+p('M25 16H10m11-4 4 4-4 4')+c(25,16,1.4,'secondary-fill')+c(11,16,1.4,'secondary-fill'),category='优化与公差')
reg('thickness-variables','设全部厚度变量','两光学面间的双向尺寸标记表示厚度变量。',p('M7 4v24M25 4v24','structure')+p('M7 17h18m-14-4-4 4 4 4m10-8 4 4-4 4')+p('M10 7h12','secondary'),category='优化与公差')
reg('optimization-variables-clear','移除所有变量','参数表上的变量点与清除叉表示移除变量而保留条目。',TABLE+p('M16 15l9 9M25 15l-9 9')+c(8,17,1,'secondary-fill')+c(8,23,1,'secondary-fill'),category='优化与公差')
reg('optimization-focus','快速聚焦','收敛光束和可移动像面表示自动聚焦。',p('M10 4v24M25 5v22','structure')+p('M2 5 10 9 23 16M2 27l8-4 13-7')+p('M20 27h10m-7-3-3 3 3 3m4-6 3 3-3 3','secondary'),category='优化与公差')
reg('optimization-adjust','快速调整','共轴光学面与侧边调节杆表示快速调整光学参数。',LENS+p('M5 7h6M21 25h7','secondary')+c(7,7,1.5,'signal')+c(25,25,1.5,'signal'),category='优化与公差')
reg('optimization-visual','可视化优化器','由高到低的目标值轨迹与迭代节点表示可视化优化。',AX+p('M7 8 12 14 17 13 22 22 27 24')+''.join(c(x,y,1,'secondary-fill') for x,y in [(7,8),(12,14),(17,13),(22,22),(27,24)]),category='优化与公差')
reg('merit-function-editor','评价函数编辑器','多项加权条目与目标值列表示评价函数编辑。',TABLE+p('M16 16h8M16 22h5')+p('M7 17h2M7 22h2','secondary')+c(23,8,1,'secondary-fill'),category='优化与公差')
reg('merit-function-wizard','生成评价函数','由目标生成评价条目，箭头连接目标与函数表。',c(9,10,5,'structure')+c(9,10,2,'secondary')+p('M15 10h10m-4-4 4 4-4 4')+r(12,18,16,10,'structure',.7)+p('M16 22h8M16 25h5','secondary'),category='优化与公差')
reg('optimization-wizard','优化向导','目标、调整步骤和收敛结果组成向导流程。',c(7,8,3.5,'structure')+p('M11 8h10v7M21 15l-3-3M21 15l3-3')+r(16,19,12,9,'structure',.5)+p('M18 25 21 22l4 1','secondary')+p('M5 18v8h7','secondary'),category='优化与公差')
reg('tolerance-wizard','公差分析向导','名义区间、扰动箭头与通过标记表示公差向导。',r(4,5,24,22)+p('M8 10h16M11 7v6M21 7v6','secondary')+p('M8 19h6m-3-3 3 3-3 3m7 0 3 3 6-7'),category='优化与公差')
reg('optimization-random-search','随机扰动搜索','多个参数节点与不同探索分支表示随机扰动搜索。',p('M5 6 14 14 25 5M14 14l11 12M14 14 5 26','structure')+c(5,6,1.7,'signal')+c(25,5,1.7,'signal')+c(25,26,1.7,'secondary')+c(5,26,1.7,'secondary')+c(14,14,2,'signal-fill'),category='优化与公差')
reg('glass-replacement','玻璃替换模板','两个材料块和双向替换箭头表示玻璃替换。',r(3,5,9,12)+r(20,16,9,12)+p('M16 5h10v7m-4-4 4 4 4-4M16 27H6v-6m-4 4 4-4 4 4')+p('M6 9h3M23 20h3','secondary'),category='优化与公差')
reg('manufacturability-review','可加工性评估','机械边界、尺寸和检查勾表示制造适用性评估。',p('M7 4v20h16V4M4 27h22','structure')+p('M11 8h8M11 12h8M11 16h5','secondary')+p('m20 23 3 3 6-8'),category='材料与制造')
reg('optical-drawing','光学制图','透镜剖面与双向尺寸线表示光学工程制图。',p('M10 6c-5 4-5 16 0 20M17 6c5 4 5 16 0 20','structure')+p('M25 5v22m-3-19 3-3 3 3m-6 16 3 3 3-3')+p('M7 3h14M7 29h14','secondary'),category='材料与制造')
reg('catalog-add-active','加入当前','条目进入当前列表与加号表示启用材料库条目。',r(13,4,15,24)+p('M17 10h7M17 16h7M17 22h7','secondary')+p('M3 16h9m-4-4 4 4-4 4M5 5v6M2 8h6'),category='材料与制造')
reg('catalog-remove-active','移出当前','从当前列表移出的箭头与减号表示停用条目。',r(13,4,15,24)+p('M17 10h7M17 16h7M17 22h7','secondary')+p('M12 16H3m4-4-4 4 4 4M2 7h6'),category='材料与制造')
reg('field-add','添加视场','视场扇配加号表示添加场点。',ART['field-settings']['body']+p('M22 4h8M26 1v7'),category='光学对象')
reg('field-delete','删除视场','视场扇配减号表示删除场点。',ART['field-settings']['body']+p('M22 4h8'),category='光学对象')
reg('wavelength-add','添加波长','光谱波形配加号表示添加波长采样。',ART['wavelength-settings']['body']+p('M23 26h7M26.5 22.5v7'),category='光学对象')
reg('wavelength-delete','删除波长','光谱波形配减号表示删除波长采样。',ART['wavelength-settings']['body']+p('M23 26h7'),category='光学对象')

# Built-in laboratories: the objects are a stack, optical alignment and physical camera.
reg('coating-design','光学镀膜设计','基底上的多层薄膜截面表示光学镀膜。',p('M5 7h22v4H5ZM5 12h22v4H5ZM5 17h22v4H5Z','structure')+p('M4 22h24v6H4','secondary')+p('M4 6h24M8 22l-2 6m8-6-2 6m8-6-2 6m8-6-2 6'),category='实验室')
reg('coating-stack','堆栈几何','相邻薄膜条带及厚度尺寸表示实际层堆栈。',p('M6 4h17v5H6ZM6 10h17v7H6ZM6 18h17v4H6ZM5 23h19v5H5','structure')+p('M28 4v18M26 4h4M26 22h4')+p('M7 12h15','secondary'),category='实验室')
reg('coating-layer-add','添加膜层','膜层截面和顶端加号表示新增一层薄膜。',p('M4 17h24v5H4ZM4 23h24v5H4','structure')+p('M4 11h14v5H4','secondary')+p('M23 3v10M18 8h10'),category='实验室')
reg('coating-layer-reverse','反转膜层','厚薄不同的层条及上下对向箭头表示层序反转。',p('M4 5h14v4H4ZM4 11h14v8H4ZM4 21h14v6H4','structure')+p('M23 5v22m-3-19 3-3 3 3m-6 16 3 3 3-3'),category='实验室')
for locked,label in [(True,'锁定膜层'),(False,'解锁膜层')]:
    shackle=p('M18 17v-5a4 4 0 0 1 8 0v5' if locked else 'M18 17v-5a4 4 0 0 1 8 0', 'signal')
    reg('coating-layer-'+('lock' if locked else 'unlock'),label,'薄膜条带与'+('闭合' if locked else '打开')+'锁梁表示膜层厚度的优化锁定状态。',p('M4 5h15v4H4ZM4 11h10v5H4ZM4 18h10v5H4','structure')+shackle+r(16,17,13,11,'signal',1)+c(22.5,22,1,'secondary-fill'),category='实验室')
reg('coating-materials','实验材料管理','折射介质层及可编辑材料条目表示膜系材料管理。',p('m4 8 10-4 10 4-10 4ZM4 8v12l10 4V12M24 8v5','structure')+r(18,16,11,12,'signal',.7)+p('M21 20h5M21 24h5','secondary'),category='实验室')
reg('coating-spectrum','光谱结果','高反射带及透射谷表示膜系光谱分析结果。',AX+p('M7 23h4c3 0 1-16 5-16h11')+p('M7 10h4c3 0 1 15 5 15h11','secondary'),category='实验室')
reg('coating-target','膜系设计目标','目标光谱的范围带与可调节点表示膜系目标。',AX+p('M7 11h8v12h12')+p('M7 8h8M15 20h12','secondary',.65,'1 2')+c(15,11,1.5,'secondary-fill')+c(15,23,1.5,'secondary-fill'),category='实验室')
reg('coating-tolerance-metrics','指标与公差','目标曲线与上下允许边界表示指标和公差。',AX+p('M7 23 13 15 20 14 27 8')+p('M7 19 13 11 20 10 27 4M7 27 13 19 20 18 27 12','secondary',.6,'1 2'),category='实验室')
reg('design-candidates','候选方案','多个光学结构卡片和选择勾表示候选设计集合。',p('M19 4H4v17M24 9H9v17','structure')+r(14,14,14,14,'structure',1)+p('M18 18v6M23 18v6','secondary')+p('m18 23 3 3 6-8'),category='实验室')
reg('optical-examples','示例系统','包含光学截面的文件夹表示可载入的示例设计。',FOLDER+p('M12 15c-3 2-3 7 0 9M16 15c3 2 3 7 0 9')+p('M7 20h16','secondary'),category='实验室')
reg('initial-structure-design','AI 初始结构','由目标指标生成两片透镜结构，表示初始结构生成。',p('M4 6h10M4 11h7M4 16h5','structure')+p('M13 9h7m-3-3 3 3-3 3')+p('M17 16c-3 2-3 9 0 11M20 16c3 2 3 9 0 11M26 16v11','secondary')+p('M12 22h17'),category='实验室')
reg('structure-size','结构规模','多组有序光学面及计数短线表示镜片和结构规模。',p('M6 7v19M12 4v24M20 4v24M26 7v19','structure')+p('M3 16h26')+p('M5 30h8M19 30h8','secondary'),category='实验室')
reg('image-quality-acceptance','像质与验收','像面点斑和验收勾表示像质目标检查。',p('M4 10V4h18M4 10v18h18','structure')+c(12,14,4,'secondary')+c(12,14,1,'signal-fill')+p('m17 24 4 4 9-11'),category='实验室')
reg('search-strategy','搜索策略','分支参数路径和目标点表示设计搜索策略。',p('M4 5h10v10h13M14 15v12h13','structure')+c(4,5,1.5,'signal-fill')+c(27,15,2,'signal')+c(27,27,2,'secondary')+p('M14 5h13','secondary'),category='实验室')
reg('run-budget','运行预算','时钟和预算刻度表示运行时间与成本限制。',c(13,16,10,'structure')+p('M13 9v7l5 3')+p('M10 3h6M26 8h4M26 16h4M26 24h4','secondary'),category='实验室')
reg('optical-alignment','光学装调','透镜轴线与对中十字表示光学对准。',p('M5 4v24M25 4v24M3 16h26','structure')+p('M13 6c-4 3-4 17 0 20M17 6c4 3 4 17 0 20','secondary')+c(15,16,5,'signal')+p('M15 9v14M8 16h14'),category='实验室')
reg('alignment-measurement-head','测量头参数','测量镜头与定位标尺表示装调测量头。',p('M4 8h17v16H4ZM21 11h7v10h-7','structure')+p('M8 12c-2 2-2 6 0 8M12 12c2 2 2 6 0 8')+p('M6 28h19M9 26v4M16 26v4M23 26v4','secondary'),category='实验室')
reg('alignment-reticle','叉丝与探测面','像面框中的横纵叉丝表示装调检测图像。',r(4,4,24,24)+p('M16 6v20M6 16h20')+p('M9 12v8M23 12v8M12 9h8M12 23h8','secondary'),category='实验室')
reg('alignment-conjugates','表面共轭位置','两侧物像平面经光学面共轭表示共轭位置表。',p('M4 6v20M28 6v20M16 4v24','structure')+p('M4 7 16 12 28 25M4 25l12-5L28 7')+p('M2 29h28','secondary'),category='实验室')
reg('camera-imaging','相机成像仿真','前置镜头、像素传感器和相机外壳表示物理成像。',r(4,8,24,19)+p('M9 8V4h8v4','structure')+c(13,17.5,5.5,'signal')+c(13,17.5,2.7,'secondary')+p('M23 12v11M21 15h4M21 19h4','secondary'),category='实验室')

for face,label in [('front','前视图'),('back','后视图'),('left','左视图'),('right','右视图'),('top','俯视图'),('bottom','仰视图'),('isometric','等轴测视图')]:
    polygons=[[(16,4),(27,10),(16,16),(5,10)],[(5,10),(16,16),(16,28),(5,22)],[(16,16),(27,10),(27,22),(16,28)]]
    target={'front':(1,),'back':(1,),'left':(2,),'right':(2,),'top':(0,),'bottom':(0,),'isometric':(0,1,2)}[face]
    body=''
    for index,points in enumerate(polygons):
        points=[(32-x if face in ('back','right') else x,32-y if face=='bottom' else y) for x,y in points]
        d='M'+' L'.join(f'{x} {y}' for x,y in points)+'Z'
        body+=p(d,'structure')
        if index in target:
            body+=p(d,'signal-fill',.16)+p(d,'signal',dash='1.5 2' if face in ('back','right','bottom') else None)
    reg('view-'+face,label,'着色目标面表示'+label+'；后侧或下方面用虚线区别于近侧可见面，方向命令保持原语义。',body,category='视图方向')
