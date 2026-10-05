namespace HistoryStrenua;

/// <summary>
/// 快捷指令「排版」（1.9.0，出图类，从「新建工程图」拆出）：当前图纸页按实际大小重新排开——主视图连同左右上下投影视图从图框左上角排起、
/// 给尺寸留地方，轴测图进右边的空地，技术要求放标题栏正上方；整页排不下就把图纸比例降一档再排。
/// </summary>
/// <remarks>
/// <para>谁是主视图、谁摆哪边见 <see cref="DrawingSheet"/>；怎么排见 <see cref="DrawingPlanner.Layout"/>。认不出位置的视图（剖视、局部、后视……）
/// 原地不动、连同标注空间当障碍躲开。已标的尺寸属于视图，跟着视图走。</para>
/// <para>轴测图先改回跟图纸比例走再量，需要时重新缩（不然反复排版会越缩越小）。</para>
/// </remarks>
internal static class DrawingArrange
{
    public static QuickCommand Command { get; } = new(
        Key: "drawing-arrange",
        CommandName: StrenuaIdentity.Domain + ".drawing.arrange",
        Title: "排版",
        Summary: "当前图纸页的主视图、投影视图、轴测图、技术要求按实际大小重新排开不重叠、给尺寸留地方；排不下就把图纸比例降一档。",
        Usage: "不用点视图：认出当前图纸页的主视图、摆在它四边的投影视图、轴测图和图框里的技术要求，按实际大小重新排——主视图连同投影视图从图框左上角排起，视图左边、上边按孔的种数留出尺寸的地方；轴测图放右边的空地（放不下缩一两档比例）；技术要求先放标题栏正上方。躲开标题栏、修改栏、图号框与图框里的其他注释。整页排不下就把图纸比例降一档再排。剖视、局部等认不出位置的视图原地不动、当障碍躲开；已标的尺寸跟着视图走。",
        Run: context => Run(context, DrawingSheet.Read(context, HoleScan.ActiveDrawing(context), "排版")));

    internal static QuickOutcome Run(QuickCommandContext context, DrawingSheet sheet)
    {
        var api = context.Api;
        context.SetState("排版");
        var scales = DrawingPlanner.Scales(sheet.Part);
        var start = sheet.Scale;
        var scale = start;
        var steps = new List<string>();
        if (sheet.Iso is not null)
            DrawingSheet.ScaleIso(api, sheet.Iso, scale, 1, scales);

        LayoutResult layout;
        while (true)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            api.Call(sheet.Drawing, "IModelDoc2", "EditRebuild3");
            var (noteWidth, noteHeight) = DrawingSheet.NoteSize(api, sheet.Note?.Note);
            var request = new LayoutRequest(
                sheet.Box(context, sheet.Main),
                sheet.Sides.ToDictionary(pair => pair.Key, pair => sheet.Box(context, pair.Value, slot: pair.Key)),
                sheet.Iso is null ? new ViewBox(0, 0, 0, 0) : sheet.Box(context, sheet.Iso, annotations: false),
                noteWidth,
                noteHeight,
                DrawingPlanner.IsoFactors(sheet.Part, scale));
            layout = DrawingPlanner.Layout(sheet.Space(context), request);
            var current = scale;
            var smaller = scales.Where(s => s < current - 1e-12).Select(s => (double?)s).FirstOrDefault();
            if (layout.Fits || smaller is null)
                break;
            context.Report($"排版：按实际大小 {DrawingPlanner.ScaleText(scale)} 排不下（{layout.Problem}），比例降到 {DrawingPlanner.ScaleText(smaller.Value)} 再排。");
            steps.Add($"{DrawingPlanner.ScaleText(scale)} {layout.Problem}");
            scale = smaller.Value;
            DrawingSheet.SetSheetScale(api, sheet.Sheet, scale);
        }

        DrawingSheet.SetPosition(api, sheet.Main, layout.Main);
        foreach (var (slot, view) in sheet.Sides)
            if (layout.Sides.TryGetValue(slot, out var center))
                DrawingSheet.SetPosition(api, view, center);
        var isoFactor = 1.0;
        if (sheet.Iso is not null && layout.Iso is { } isoCenter)
        {
            isoFactor = DrawingSheet.ScaleIso(api, sheet.Iso, scale, layout.IsoShrink, scales);
            DrawingSheet.SetPosition(api, sheet.Iso, isoCenter);
        }

        if (sheet.Note is not null)
        {
            // 视图组排不下时排版不给技术要求的位置：至少挪到图框左下角。
            var (_, noteHeight) = DrawingSheet.NoteSize(api, sheet.Note.Note);
            var frame = SheetSpace.Standard(sheet.Width, sheet.Height).Frame;
            var topLeft = layout.NoteTopLeft ?? new SheetPoint(frame.Left + 2 * DrawingPlanner.Gap, frame.Bottom + 2 * DrawingPlanner.Gap + noteHeight);
            api.Call(sheet.Note.Annotation, "IAnnotation", "SetPosition2", topLeft.X, topLeft.Y, 0.0);
        }

        api.Call(sheet.Drawing, "IModelDoc2", "EditRebuild3");
        api.Call(sheet.Drawing, "IModelDoc2", "ClearSelection2", true);

        var actual = DrawingSheet.SheetScale(api, sheet.Sheet, scale);
        var items = new List<string> { $"主视图「{sheet.MainName}」" };
        items.AddRange(sheet.Sides.Select(pair => $"{DrawingSheet.SlotName(pair.Key)}「{DrawingSheet.Name(api, pair.Value)}」"));
        if (sheet.Iso is not null)
            items.Add($"轴测图「{DrawingSheet.Name(api, sheet.Iso)}」");
        if (sheet.Note is not null)
            items.Add("技术要求");
        var lines = new List<string>
        {
            $"排版：{string.Join("、", items)}按实际大小" + (layout.Fits ? "排开" : "尽量摆开") + $"，比例 {DrawingPlanner.ScaleText(actual)}。",
        };
        if (steps.Count > 0)
            lines.Add($"· 比例从 {DrawingPlanner.ScaleText(start)} 降到 {DrawingPlanner.ScaleText(actual)}：{string.Join("；", steps)}。");
        if (isoFactor < 1)
            lines.Add($"· 轴测图比例缩成 {DrawingPlanner.ScaleText(actual * isoFactor)}。");
        if (sheet.Others.Count > 0)
            lines.Add($"· {sheet.Others.Count} 个视图（{string.Join("、", sheet.Others.Select(view => "「" + DrawingSheet.Name(api, view) + "」"))}）不是从主视图投影到四边的，没有挪它（与父视图对齐的跟着父视图走），当障碍躲开。");
        if (!layout.Fits)
            lines.Add($"· {layout.Problem}，比例已降到底，请手工挪一下或换大图幅。");
        else if (layout.Problem.Length > 0)
            lines.Add($"· {layout.Problem}，请手工挪一下。");
        var message = string.Join(Environment.NewLine, lines);
        return layout.Fits ? QuickOutcome.Ok(message) : QuickOutcome.Fail(message);
    }
}
