namespace OpticalSystemDesign.App.Services;

internal static partial class UiLocalization
{
    private static void AddSurfaceDrawingTexts(Action<string, string, string> add)
    {
        add("近轴面", "Paraxial", "近軸面");
        add("近轴 XY 面", "Paraxial XY", "近軸XY面");
        add("近轴透镜", "Paraxial Lens", "近軸レンズ");
        add("近轴参数按导入值保留；此处只读。", "Imported paraxial parameters are preserved; read-only here.", "読み込んだ近軸パラメータを保持します。ここでは読み取り専用です。");
        add("焦距：{0} {1}\nOPD 模式：{2}", "Focal length: {0} {1}\nOPD mode: {2}", "焦点距離：{0} {1}\nOPDモード：{2}");
        add("X 焦距：{0} {1}\nY 焦距：{2} {3}\nOPD 模式：{4}", "X focal length: {0} {1}\nY focal length: {2} {3}\nOPD mode: {4}", "X焦点距離：{0} {1}\nY焦点距離：{2} {3}\nOPDモード：{4}");
        add("表面透明度", "Surface Opacity", "面の不透明度");
        add("100%（默认）", "100% (default)", "100%（既定）");
        add("0%（完全透明）", "0% (transparent)", "0%（透明）");
        add("显示不透明度比例：100% 保持默认绘制强度，0% 完全透明；不改变材料透过率。", "Display opacity: 100% keeps the normal drawing strength; 0% is transparent. Material transmission is unchanged.", "表示不透明度。100% は通常の描画強度、0% は透明です。材料の透過率は変わりません。");
        add("隐藏这个表面的光线", "Hide rays to this surface", "この面への光線を非表示");
        add("光线忽略这个面", "Skip this surface when drawing rays", "光線の描画でこの面をスキップ");
        add("不显示此表面", "Do not draw this surface", "この面を描画しない");
        add("不显示此表面的边缘", "Do not draw edges from this surface", "この面からのエッジを描画しない");
        add("显示局部坐标轴", "Draw local axes", "ローカル座標軸を描画");
        add("绘制局部原点", "Draw local origin point", "ローカル原点を描画");
        add("边缘显示", "Draw edges as", "エッジの描画方法");
        add("绘图精度", "Drawing resolution", "描画解像度");
        add("方形到下表面", "Squared to next surface", "次の面まで直角に");
        add("锥形到下表面", "Tapered to next surface", "次の面までテーパー状に");
        add("平直到下表面", "Flat to next surface", "次の面まで平らに");
        add("不绘制边缘", "No edges", "エッジなし");
        add("中等", "Medium", "中");
        add("高", "High", "高");
        add("演示", "Presentation", "プレゼンテーション");
        add("（面型参数只读，可编辑绘图与行颜色）", " (surface parameters are read-only; drawing and row color remain editable)", "（面のパラメーターは読み取り専用。描画と行色は編集可能）");
        add("隐藏到达此表面的绘图光线段，后续光线段继续显示。", "Hide incoming drawing segments at this surface; later segments remain visible.", "この面への描画光線区間を非表示にし、以降の区間は表示します。");
        add("绘图时跳过此面节点，直连前后节点；物理追迹仍经过此面。用于无光焦度的辅助面。", "Connect the preceding and following drawing nodes directly. Physical tracing still includes this surface. Intended for optically inactive dummy surfaces.", "前後の描画点を直接接続します。物理追跡はこの面を通ります。光学的に作用しない補助面に使用します。");
        add("平直到下表面仅用于二维布局；三维布局和实体模型采用方形到下表面。", "Flat to next surface applies to 2D Layout; 3D Layout and Solid Model use squared edges.", "次の面まで平らには2Dレイアウトに適用します。3Dレイアウトとソリッドモデルでは直角エッジを使用します。");
        add("提高表面曲线和网格的绘图采样密度，不改变光线追迹精度。", "Increase drawing samples for surface curves and meshes without changing ray-trace accuracy.", "面の曲線とメッシュの描画サンプル密度を上げます。光線追跡精度は変わりません。");
        add("行颜色", "Row Color", "行の色");
        add("自定义行颜色", "Custom Row Color", "カスタム行色");
        add("设置当前表面的镜头表行颜色，点击应用后生效；默认颜色恢复材料和表面类型配色。", "Set the lens-editor row color for this surface and click Apply. Default restores material and surface-type colors.", "この面のレンズ表の行色を設定し、適用を押します。既定色は材料と面の種類の配色に戻します。");
        add("输入 #RRGGBB，例如 #E8DCF3。", "Enter #RRGGBB, for example #E8DCF3.", "#RRGGBB を入力してください。例：#E8DCF3。");
        add("行颜色请输入 #RRGGBB 格式的六位十六进制颜色。", "Enter a six-digit hexadecimal row color in #RRGGBB format.", "行の色を #RRGGBB 形式の6桁の16進数で入力してください。");
    }
}
