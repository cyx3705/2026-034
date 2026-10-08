namespace HistoryStrenua;

/// <summary>
/// 快捷指令「一键出图」（1.9.0，出图类）：依次跑出图类各步与孔标注全流程——新建工程图 → 投影视图 → 轴测图 → 技术要求 → 排版
/// → 孔标注全流程（<see cref="HoleFlow"/>，照页面开关）→ 全图圆心（1.14.2）→ 全图圆弧 → 全图倒角（1.10.0）。不保存。
/// </summary>
/// <remarks>
/// <para>每一步都是一条单独的指令（用户定：拆开后人手工、AI 经 MCP 都能一步步做、中间改；孔标注全流程本身是孔类的，不拆）。
/// 这里只是按顺序调它们，零件只读一次、主视图沿用刚建的那个。</para>
/// <para>顺序：视图建齐、排好 → 孔标注全流程（销钉符号、中心符号线、孔位尺寸、外轮廓、孔标注、销孔标注）→ 圆心位置 → 圆弧标注 → 倒角标注。
/// 圆心位置接在外轮廓后面：同是从基准量起的线性尺寸，排在孔位、外轮廓尺寸的外层。圆弧、倒角放最后：文字找空处时孔、圆心的尺寸已在位
/// （它躲视图里的线；孔类注解各自的避障不管 R 尺寸）。某一步没成不中断，回执里列出来。</para>
/// </remarks>
internal static class DrawingAuto
{
    public static QuickCommand Command { get; } = new(
        Key: "drawing-auto",
        CommandName: StrenuaIdentity.Domain + ".drawing.auto",
        Title: "一键出图",
        Summary: "依次做新建工程图、投影视图、轴测图、技术要求、排版、孔标注全流程、全图圆心、全图圆弧、全图倒角，一次出一张基本标好的图，不保存。",
        Usage: "在 SolidWorks 里打开要出图的零件（或在装配体里选中一个零件）再按：依次做「新建工程图 → 投影视图 → 轴测图 → 技术要求 → 排版」，再对新图全部视图（轴测图跳过）做「孔标注全流程」（销钉符号 → 中心符号线 → 孔位尺寸 → 外轮廓 → 孔标注 → 销孔标注，照工具条上的避障、尺寸链开关；技术要求、轴测图、投影视图、圆心、圆弧、倒角也照避障开关；技术要求插模板表格里设为默认的那份，「AI 填写技术要求」开着时由 AI 选基础并微调），最后「全图圆心」「全图圆弧」「全图倒角」。除技术要求外每一步也都能单独按（技术要求在「技术要求」类的表格里点）。某一步没成不中断，最后汇总。新图不保存，请检查、微调后自己保存。",
        Run: Run);

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var created = DrawingCreate.Create(context, next: false);
        if (!created.Outcome.Success || created.Drawing is not { } drawing || created.Main is not { } main || created.Part is not { } part)
            return created.Outcome;

        var api = context.Api;
        var lines = new List<string> { created.Outcome.Message };
        var failures = new List<string>();
        void Step(string title, Func<QuickOutcome> run)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            QuickOutcome outcome;
            try
            {
                outcome = run();
            }
            catch (QuickCommandException ex)
            {
                outcome = QuickOutcome.Fail($"{title}：{ex.Message}");
            }

            lines.Add(outcome.Message);
            if (!outcome.Success)
                failures.Add(title);
        }

        DrawingSheet Sheet() => DrawingSheet.Read(context, drawing, "一键出图", main, part);
        Step("投影视图", () => DrawingProject.Run(context, Sheet()));
        Step("轴测图", () => DrawingIso.Run(context, Sheet()));
        // 1.13.0：插默认技术要求模板（表格「设置」列定的那份），与点模板表格同一条路；
        // 1.14.0：「AI 填写技术要求」开着时由 AI 选基础并微调，AI 没成退回默认那份。
        Step("技术要求", () => context.Options.TechAi
            ? TechAi.Run(context, drawing, fallback: context.Options.TechDefault)
            : TechApply.Run(context, context.Options.TechDefault, drawing));
        Step("排版", () => DrawingArrange.Run(context, Sheet()));
        context.Report("一键出图：视图已建好、排好，开始孔标注全流程。");
        Step("孔标注全流程", () => HoleFlow.Run(context));
        Step("全图圆心", () => ArcCenterAll.Run(context));
        Step("全图圆弧", () => FilletAll.Run(context));
        Step("全图倒角", () => ChamferAll.Run(context));

        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        api.Call(drawing, "IModelDoc2", "ViewZoomtofit2");
        var head = "一键出图：" + (failures.Count == 0 ? "新图建好并标完（未保存）。" : $"新图建好（未保存），{failures.Count} 步没成（{string.Join("、", failures)}）。");
        var message = head + Environment.NewLine + string.Join(Environment.NewLine, lines);
        return failures.Count == 0 ? QuickOutcome.Ok(message) : QuickOutcome.Fail(message);
    }
}
