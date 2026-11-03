using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「螺纹孔」<c>strenua.dim.thread</c>（1.38.0，用户定：给特定孔标注专门的指令，并修掉手改前缀的字体错乱）：
/// 模型里螺纹孔画成底孔时，把那个孔标注改写成螺纹的标法。
/// </summary>
/// <remarks>
/// <para>写法见 <see cref="ThreadSpec.Lines"/>：隐藏尺寸值（底孔直径由规格定），上一行写进前缀、下一行写进孔标注下方那一段，
/// 符号全用符号码（<see cref="SwText"/>）——真机上手写 Unicode「↧」「Ø」的前缀画成乱码。原后缀里的「(反面)」「(含对面)」留着。</para>
/// <para>异形孔向导的孔标注模板（第 5 段）带变量，和前缀不是一段：两段都写，变量才不会接在后面又写一遍规格。改完按原折点对齐（文字变短引线不跟丢）。</para>
/// </remarks>
internal static class DimThread
{
    public static QuickCommand Command { get; } = new(
        Key: "dim-thread",
        CommandName: StrenuaIdentity.Domain + ".dim.thread",
        Title: "螺纹孔",
        Summary: "把一个孔标注改写成螺纹孔标法（4 x M6↧12 / 孔↧15，或 4 x M6 贯穿），符号用 SolidWorks 符号码不乱码；读回显示文字。",
        Usage: "「视图」「名字」填孔标注（标注列表里 kind=孔标注 或直径）。「规格」M6 / M6x0.75 / M6-6H；「螺纹深」mm，省略表示螺纹贯穿；"
               + "底孔深默认读原标注的「↧数」，可用「底孔深」改；孔数默认读原标注的「N x」，可用「孔数」改。底孔直径和规格对不上只提醒。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            QuickArgs.View("视图名（视图列表的 name 列）"),
            new QuickParameter("name", "名字", "孔标注名（标注列表的 name 列），如 RD7", true),
            new QuickParameter("spec", "规格", "螺纹规格：M6、M6x0.75、M6-6H", true, 2),
            new QuickParameter("depth", "螺纹深", "螺纹深 mm；省略 = 螺纹贯穿", false, 2),
            new QuickParameter("drill", "底孔深", "底孔深 mm；省略读原标注", false, 2),
            new QuickParameter("count", "孔数", "孔数；省略读原标注的 N x", false, 2),
        ],
        Example: StrenuaIdentity.Domain + ".dim.thread view=工程图视图2 name=RD7 spec=M6 depth=12",
        Bind: Bind,
        SelfReport: true);

    private static Func<QuickCommandContext, QuickOutcome> Bind(CommandContext arguments)
    {
        var view = QuickArgs.Name(arguments, "view");
        var name = QuickArgs.Name(arguments, "name");
        var spec = ThreadSpec.Parse(QuickArgs.Name(arguments, "spec"));
        var depth = QuickArgs.Millimeters(QuickArgs.Optional(arguments, "depth"), "depth");
        var drill = QuickArgs.Millimeters(QuickArgs.Optional(arguments, "drill"), "drill");
        if (depth is <= 0 || drill is <= 0)
            throw new QuickArgumentException("depth= / drill= 要大于 0。");
        if (depth is { } d && drill is { } h && h < d)
            throw new QuickArgumentException($"底孔深 {ThreadSpec.Format(h)} 比螺纹深 {ThreadSpec.Format(d)} 还浅。");
        int? count = QuickArgs.Optional(arguments, "count") is { } text
            ? int.TryParse(text, out var n) && n > 0 ? n : throw new QuickArgumentException($"count={text} 要写正整数。")
            : null;
        return context => Run(context, view, name, spec, depth, drill, count);
    }

    private static QuickOutcome Run(QuickCommandContext context, string viewName, string name, ThreadSpec spec, double? depth, double? drill, int? count)
    {
        var api = context.Api;
        var drawing = DrawingAccess.ActiveDrawing(context);
        var view = SheetLookup.View(api, drawing, viewName);
        var annotation = SheetLookup.Annotation(api, view, name);
        if (api.CallInt(annotation, "IAnnotation", "GetType") != SwConst.AnnotationDimension
            || api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is not { } display)
            return QuickOutcome.Fail($"螺纹孔：「{name}」不是尺寸或孔标注。");
        var dimension = api.Call(display, "IDisplayDimension", "GetDimension2", 0);
        var callout = api.CallBool(display, "IDisplayDimension", "IsHoleCallout");
        if (!callout && (dimension is null || api.CallInt(dimension, "IDimension", "GetType") != SwConst.DiameterDimension))
            return QuickOutcome.Fail($"螺纹孔：「{name}」不是孔标注，也不是直径尺寸。");

        var original = AnnotationRows.Shown(api, annotation);
        var oldSuffix = api.CallString(display, "IDisplayDimension", "GetText", SwConst.TextSuffix);
        var source = original + " " + oldSuffix + " " + api.CallString(display, "IDisplayDimension", "GetText", CalloutEdit.TextCalloutBelow);
        var holes = count ?? ThreadSpec.ReadCount(original);
        var drillDepth = drill ?? ThreadSpec.ReadDrillDepth(source);
        var throughDrill = drillDepth is null;
        if (depth is null && !throughDrill)
            return QuickOutcome.Fail($"螺纹孔：底孔是盲孔（深 {ThreadSpec.Format(drillDepth!.Value)}），给 depth= 螺纹深（一般比底孔浅 3 个螺距左右）。");
        if (depth is { } threadDepth && drillDepth is { } blind && threadDepth > blind + 1e-6)
            return QuickOutcome.Fail($"螺纹孔：螺纹深 {ThreadSpec.Format(threadDepth)} 比底孔深 {ThreadSpec.Format(blind)} 还深。");

        var warnings = new List<string>();
        var diameter = dimension is null ? 0 : Convert.ToDouble(api.Call(dimension, "IDimension", "get_SystemValue") ?? 0.0) * 1000;
        if (diameter > 0 && !spec.Fits(diameter))
            warnings.Add($"图上孔径 Ø{ThreadSpec.Format(diameter)} 不是 {spec.Text} 的底孔（Ø{ThreadSpec.Format(spec.Drill)}），核对模型");

        var geometry = new ViewGeometry(api, view);
        var shoulder = CalloutEdit.AttachedCircleCenters(api, geometry, annotation).Cast<SheetPoint?>().FirstOrDefault() is { } center
            ? CalloutEdit.ShoulderTowards(api, annotation, center)
            : null;
        var (above, below) = spec.Lines(holes, depth, drillDepth);
        var markers = string.Concat(new[] { HoleCoveragePlanner.BackSuffix, OppositePlanner.Suffix }.Where(marker => (original + oldSuffix).Contains(marker.Trim(), StringComparison.Ordinal)));
        context.SetState("改螺纹孔");
        api.Call(display, "IDisplayDimension", "set_ShowDimensionValue", false);
        api.Call(display, "IDisplayDimension", "SetText", SwConst.TextPrefix, above);
        if (api.CallString(display, "IDisplayDimension", "GetText", CalloutEdit.TextCalloutTemplate) is { Length: > 0 } template && template != above)
            api.Call(display, "IDisplayDimension", "SetText", CalloutEdit.TextCalloutTemplate, above);
        api.Call(display, "IDisplayDimension", "SetText", SwConst.TextSuffix, markers);
        api.Call(display, "IDisplayDimension", "SetText", CalloutEdit.TextCalloutBelow, below);
        api.Call(drawing, "IModelDoc2", "EditRebuild3");
        if (shoulder is { } s)
            CalloutEdit.AlignShoulder(api, annotation, s.Point, s.TextLeft);
        api.Redraw(drawing);

        var shown = SwText.Readable(AnnotationRows.Shown(api, annotation));
        var ok = shown.Contains(spec.Text, StringComparison.Ordinal) && !shown.Contains('Ø');
        var message = $"螺纹孔：视图「{viewName}」的「{name}」由「{SwText.Readable(original)}」改成「{shown}」。"
                      + (warnings.Count > 0 ? Environment.NewLine + "· " + string.Join("；", warnings) + "。" : string.Empty);
        return ok ? QuickOutcome.Ok(message) : QuickOutcome.Fail(message + Environment.NewLine + "· 读回的字里没有规格或还带着直径，SolidWorks 没按写的显示。");
    }
}
