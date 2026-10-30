namespace HistoryStrenua;

/// <summary>
/// 快捷指令「挪标注」<c>strenua.dim.move</c>（1.20.0）：按名字把一个尺寸 / 注解的文字挪到 x/y 或挪动 dx/dy（图纸 mm）。
/// </summary>
/// <remarks>
/// <para><c>IAnnotation.SetPosition2</c>：线性尺寸挪文字就是挪尺寸线（换层）；孔标注整块文字刚性平移，SolidWorks 自己换引线侧。
/// 位置是 <c>GetPosition</c> 那个点（尺寸是文字中点，不是折点），和 <c>strenua.dim.list</c> 的 x/y 是同一个。</para>
/// <para>中心符号线、中心线、销钉符号跟着几何走，没有可挪的位置，直接拒。坐标尺寸组 SolidWorks 自己折弯排，挪一个可能被拉回去，读回核对。</para>
/// </remarks>
internal static class DimMove
{
    public static QuickCommand Command { get; } = new(
        Key: "dim-move",
        CommandName: StrenuaIdentity.Domain + ".dim.move",
        Title: "挪标注",
        Summary: "按名字挪一个尺寸或注解的文字：挪到 x/y 或挪动 dx/dy（图纸 mm）；线性尺寸挪文字即换层，读回核对位置。",
        Usage: "填「视图」「名字」，再填 X / Y（文字挪到哪，同标注列表的位置，图纸 mm）或 右移 / 上移（挪多少 mm），两种只能用一种。线性尺寸挪文字就是换层；孔标注整块平移，引线侧 SolidWorks 自己换。挪完读回，被 SolidWorks 摆回去（坐标尺寸组常这样）回执失败。中心符号线、中心线、销钉符号跟着几何走，不能挪。不看避障开关。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            QuickArgs.View("视图名（视图列表的 name 列；图纸页上的注释用图纸页名）"),
            new QuickParameter("name", "名字", "注解名（标注列表的 name 列），如 D1", true),
            QuickArgs.X("挪到：文字位置 X（图纸 mm，同标注列表的 x）；只给 x 则 Y 不动。不能和 dx/dy 同用"),
            QuickArgs.Y("挪到：文字位置 Y（图纸 mm）"),
            QuickArgs.Dx("挪动：向右为正（mm）"),
            QuickArgs.Dy("挪动：向上为正（mm），如 6"),
        ],
        Example: StrenuaIdentity.Domain + ".dim.move view=工程图视图1 name=D1 dy=6",
        SelfReport: true,
        Bind: arguments =>
        {
            var view = QuickArgs.Name(arguments, "view");
            var name = QuickArgs.Name(arguments, "name");
            var request = QuickArgs.Move(arguments);
            return context => Run(context, view, name, request);
        });

    private static QuickOutcome Run(QuickCommandContext context, string viewName, string name, MoveRequest request)
    {
        var api = context.Api;
        var drawing = HoleScan.ActiveDrawing(context);
        var view = SheetLookup.View(api, drawing, viewName);
        var annotation = SheetLookup.Annotation(api, view, name);
        var type = api.CallInt(annotation, "IAnnotation", "GetType");
        if (SheetLookup.Position(api, annotation, type) is not { } from)
            return QuickOutcome.Fail($"挪标注：「{name}」是{AnnotationCheckPlanner.KindName(type)}，跟着几何走，没有可挪的位置。");

        context.SetState("挪标注");
        var target = QuickArgs.Target(request, from);
        api.Call(annotation, "IAnnotation", "SetPosition2", target.X, target.Y, 0.0);
        api.Call(drawing, "IModelDoc2", "GraphicsRedraw2");
        var actual = SheetLookup.Position(api, annotation, type) ?? target;
        var missed = MoveView.Missed(target, actual);
        var message = $"挪标注：视图「{viewName}」的「{name}」{QuickArgs.Format(from)} → {QuickArgs.Format(actual)}。";
        if (DrawingSheet.Footprint(api, annotation) is { } rect)
            message += $"{Environment.NewLine}· 现在占 ({QuickArgs.Format(rect.Left)}, {QuickArgs.Format(rect.Bottom)})–({QuickArgs.Format(rect.Right)}, {QuickArgs.Format(rect.Top)})。";
        if (missed.Count > 0)
            message += $"{Environment.NewLine}· {string.Join("、", missed)}方向没到（要 {QuickArgs.Format(target)}），SolidWorks 把它摆回去了（坐标尺寸组、对齐的尺寸常这样）。";
        return missed.Count == 0 ? QuickOutcome.Ok(message) : QuickOutcome.Fail(message);
    }
}
