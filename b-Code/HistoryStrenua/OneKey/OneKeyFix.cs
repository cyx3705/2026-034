namespace HistoryStrenua;

/// <summary>
/// 一键出完写在回执里的修正要点（1.28.0，用户定）：一键效果不够好，出完必须由 AI 修。条目来自 2026-10-30 三轮真机长流程实测里反复手修的地方。
/// 这里只写修什么、用哪条指令；修图时新的体会由 AI 记进笔记区（<see cref="WorkNotes"/>）。
/// </summary>
internal static class OneKeyFix
{
    /// <summary>接在一键回执后面的整段提示词。</summary>
    public static string Text(PartKind kind)
        => string.Join(Environment.NewLine,
            [$"【修正要点·{PartKindPlanner.Title(kind)}】这只是初稿，不能直接交付。逐条看图修完才算完成：",
             "1. 整页：strenua.check.snapshot 看截图，strenua.check.svg view= 看单个视图。主视图选窄边、视图挤或大片空白，用 strenua.view.move 调；比例、图幅不合适就告诉用户，不要当没看见。",
             .. Specific(kind),
             "· 孔标注与尺寸：strenua.check.overlap 查压字、引线过长与交叉（尺寸界线穿孔属误报），字放到视图外或引线穿尺寸的用 strenua.dim.move 挪回视图旁；尺寸出图框的挪回框内。",
             "· 漏标与错标：strenua.check.dimension 报的 [E]/[H] 对照图判断，真漏的用 strenua.dim.add / strenua.dim.hole / strenua.dim.center 补，多余的 strenua.dim.delete；再跑 strenua.check.dangling、strenua.check.decimal。",
             "· 技术要求：一键可能选错模板、猜错材料。不合零件就 strenua.tech.apply name= 换一份；零件没写材料时不要编。",
             "· 标题栏：零件没有属性时是空的，把缺什么告诉用户，不要编。",
             "· 收尾：改完再看一次截图，strenua.doc.save 只在用户要保存时用。这次修图学到的规律用 strenua.note.write 记下来。"]);

    /// <summary>这一类特有的点。</summary>
    private static IEnumerable<string> Specific(PartKind kind) => kind switch
    {
        PartKind.SheetMetal =>
        [
            "2. 板厚：必须有一个视图看得到板厚并标出来，一键常漏；折边高度也要有。",
            "3. 基准：一律以外轮廓为基准，折弯切线、折弯区短边不能当基准或被标注；折弯半径不标。",
            "4. 孔与方形槽：同种孔不共线时中心符号线会斜拉长线，要删掉重标；窗口的宽高、定位都要齐。",
            "5. 对称：回执说对称轴「未生成贯穿视图」时，确认这个视图的尺寸基准还对不对。",
            "6. 技术要求必须是钣金模板；一键常插成机加钢材。",
        ],
        PartKind.Frame =>
        [
            "2. 型材说明：核对轴测图上的「采用 xx 铝型材」。没有焊件时 xx 是按截面猜的，猜不准要改对。",
            "3. 尺寸：总长宽高必须有；每根型材至少定一个位置。一键按型材宽筛掉了近的站，缺的用 strenua.dim.add 补，T 型槽口的小台阶不标。",
            "4. 孔：框架一键不标孔。型材上有加工孔、连接孔的话自己用 strenua.dim.hole 补。",
            "5. 技术要求：不要留机加钢材模板里不适用的条目。",
        ],
        _ =>
        [
            "2. 孔识别：对照截图数孔。导入件（Imported）常认错：沉孔被当腰型孔，甚至一个孔都没认出，这时「未标尺寸」也不报，要自己补 strenua.dim.hole 和位置尺寸。",
            "3. 孔的后缀：背面的沉孔要「(反面)」、对面同孔要「(含对面)」，缺了用 strenua.dim.edit suffix= 补。",
            "4. 孔位：同一方向被拆成碎链（如 4+36）、数字甩到别处的删掉重标；同种孔不共线时中心符号线斜拉长线，要删掉重标。",
            "5. 销孔：核对 H7 与销孔间 ±0.02 都在。",
            "6. 圆弧倒角：确认 R、C 都标了（C1 按约定不标），全图圆心有没有挪动粗糙度符号。",
        ],
    };
}
