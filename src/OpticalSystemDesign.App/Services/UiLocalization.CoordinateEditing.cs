namespace OpticalSystemDesign.App.Services;

internal static partial class UiLocalization
{
    private static void AddCoordinateEditingTexts(Action<string, string, string> add)
    {
        add("表面 {0}", "Surface {0}", "面 {0}");
        add("表面 {0} 属性", "Surface {0} Properties", "面 {0} のプロパティ");
        add("上插入坐标间断", "Insert Coordinate Break Above", "上に座標ブレークを挿入");
        add("下插入坐标间断", "Insert Coordinate Break Below", "下に座標ブレークを挿入");
        add("先偏心，后倾斜", "Decenter, then tilt", "偏心後に傾斜");
        add("先倾斜，后偏心", "Tilt, then decenter", "傾斜後に偏心");
        add("不返回", "No return", "戻さない");
        add("返回方向，保持当前位置", "Return orientation, keep position", "位置を保持して方向を戻す");
        add("返回参考原点与方向", "Return origin and orientation", "参照の原点と方向に戻す");
        add("变换顺序", "Transform Order", "変換順序");
        add("坐标返回", "Coordinate Return", "座標復帰");
        add("参考表面", "Reference Surface", "参照面");
        add("偏心和倾斜使用进入本行的局部坐标；顺序决定旋转和位移如何组合。", "Decenter and tilt use this row's incoming local frame. The order determines how rotation and translation combine.", "偏心と傾斜はこの行に入る局所座標を使用します。順序により回転と移動の組み合わせが決まります。");
        add("返回先建立参考坐标，再应用本行偏心和倾斜；返回原点可能产生有符号的虚传播段。", "A return establishes the reference frame before applying this row's decenter and tilt. Returning to an earlier origin can produce a signed virtual propagation segment.", "復帰で参照座標を設定してから、この行の偏心と傾斜を適用します。前の原点への復帰では符号付きの仮想伝搬区間が生じる場合があります。");
        add("应用仅更新当前结构的坐标参数；插入和删除同步所有结构。修改后可撤销，并随工程保存。", "Apply updates coordinate parameters in the current configuration. Insertion and deletion affect all configurations. Changes support undo and project saving.", "適用は現在の構成の座標パラメータを更新します。挿入と削除は全構成に反映されます。変更は元に戻せ、プロジェクトに保存されます。");
        add("X 偏心 ({0})", "X Decenter ({0})", "X 偏心 ({0})");
        add("Y 偏心 ({0})", "Y Decenter ({0})", "Y 偏心 ({0})");
        add("X 倾斜 (°)", "X Tilt (°)", "X 傾斜 (°)");
        add("Y 倾斜 (°)", "Y Tilt (°)", "Y 傾斜 (°)");
        add("Z 倾斜 (°)", "Z Tilt (°)", "Z 傾斜 (°)");
        add("该参数由拾取或求解控制。", "This parameter is controlled by a pickup or solve.", "このパラメータはピックアップまたはソルブで制御されています。");
        add("将本行坐标参数加入优化变量。", "Add this coordinate parameter to the optimization variables.", "この座標パラメータを最適化変数に追加します。");
        add("此行不是坐标间断。请在需要变换的位置插入坐标间断。", "This row is not a coordinate break. Insert a coordinate break where the transformation is needed.", "この行は座標ブレークではありません。座標変換が必要な位置に座標ブレークを挿入してください。");
        add("旧工程未保存原始变换参数。本次偏心和倾斜以保留的表面坐标为基准。", "The older project did not retain the original transformation parameters. These edits use the preserved surface frame as their basis.", "旧プロジェクトには元の変換パラメータが保存されていません。保持された面座標を基準に編集します。");
        add("编辑坐标间断参数；拾取或求解控制的参数保持只读。", "Edit coordinate break parameters. Parameters controlled by pickups or solves remain read-only.", "座標ブレークのパラメータを編集します。ピックアップまたはソルブで制御された項目は読み取り専用です。");
        add("不能在物面之前插入。", "Cannot insert before the object surface.", "物体面の前には挿入できません。");
        add("不能在像面之后插入。", "Cannot insert after the image surface.", "像面の後には挿入できません。");
        add("应用当前结构的坐标设置。", "Apply coordinate settings to the current configuration.", "現在の構成に座標設定を適用します。");
        add("请先选择可编辑的坐标间断。", "Select an editable coordinate break first.", "編集可能な座標ブレークを選択してください。");
        add("工程已变化，请还原并重新编辑坐标设置。", "The project has changed. Revert and edit the coordinate settings again.", "プロジェクトが変更されました。復元してから座標設定を編集し直してください。");
        add("该表面正被坐标返回或参数拾取引用，请先修改引用。", "This surface is referenced by a coordinate return or parameter pickup. Update the reference first.", "この面は座標復帰またはパラメータのピックアップで参照されています。先に参照を変更してください。");
        add("该表面正被评价函数引用，请先修改操作数。", "This surface is referenced by the merit function. Update the operand first.", "この面は評価関数で参照されています。先にオペランドを変更してください。");
        add("坐标返回必须引用前面的表面。", "A coordinate return must reference an earlier surface.", "座標復帰では前の面を参照してください。");
        add("偏心和倾斜必须为有限数值。", "Decenter and tilt must be finite values.", "偏心と傾斜には有限値を指定してください。");
        add("坐标链含未映射的参数依赖，请先在原始处方解除该依赖后重新导入。", "The coordinate chain has an unsupported parameter dependency. Remove that dependency in the source prescription and import again.", "座標チェーンに未対応のパラメータ依存関係があります。元の処方で依存関係を解除してから再度インポートしてください。");
    }
}
