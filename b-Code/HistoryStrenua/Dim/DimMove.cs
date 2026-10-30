namespace HistoryStrenua;

/// <summary>
/// 细粒度指令「挪注解」<c>strenua.dim.move</c>（1.20.0）：按名字把一个尺寸 / 注解的文字挪到 x/y 或挪动 dx/dy（图纸 mm）。
/// </summary>
/// <remarks>
/// <para><c>IAnnotation.SetPosition2</c>：线性尺寸挪文字就是挪尺寸线（换层）；孔标注整块文字刚性平移，SolidWorks 自己换引线侧。
/// 位置是 <c>GetPosition</c> 那个点（尺寸是文字中点，不是折点），和 <c>strenua.dim.list</c> 的 x/y 是同一个。</para>
/// <para>中心符号线、中心线、销钉符号跟着几何走，没有可挪的位置，直接拒。坐标尺寸组 SolidWorks 自己折弯排，挪一个可能被拉回去，读回核对。</para>
/// </remarks>
internal static class DimMove
{
    public static FineCommand Command { get; } = new(
        Key: "dim-move",
        CommandName: StrenuaIdentity.Domain + ".dim.move",
        Title: "挪注解",
        Summary: "按名字挪一个尺寸或注解的文字：挪到 x/y 或挪动 dx/dy（图纸 mm）；线性尺寸挪文字即换层，读回核对位置。",
        Example: StrenuaIdentity.Domain + ".dim.move view=工程图视图1 name=D1 dy=6",
        Parameters:
        [
            FineArgs.Text("view", "视图名（strenua.view.list 的 name 列；图纸页上的注释用图纸页名）", true, 0),
            FineArgs.Text("name", "注解名（strenua.dim.list 的 name 列），如 D1", true, 1),
            FineArgs.Text("x", "挪到：文字位置 X（图纸 mm，同 dim.list 的 x）；只给 x 则 Y 不动。不能和 dx/dy 同用", false, 2),
            FineArgs.Text("y", "挪到：文字位置 Y（图纸 mm）", false, 3),
            FineArgs.Text("dx", "挪动：向右为正（mm）", false, 4),
            FineArgs.Text("dy", "挪动：向上为正（mm），如 6", false, 5),
        ],
        Readonly: false,
        Bind: arguments =>
        {
            var view = FineArgs.Name(arguments, "view");
            var name = FineArgs.Name(arguments, "name");
            var request = FineArgs.Move(arguments);
            return context => Run(context, view, name, request);
        });

    private static QuickOutcome Run(QuickCommandContext context, string viewName, string name, MoveRequest request)
    {
        var api = context.Api;
        var drawing = HoleScan.ActiveDrawing(context);
        var view = FineLookup.View(api, drawing, viewName);
        var annotation = FineLookup.Annotation(api, view, name);
        var type = api.CallInt(annotation, "IAnnotation", "GetType");
        if (FineLookup.Position(api, annotation, type) is not { } from)
            return QuickOutcome.Fail($"挪注解：「{name}」是{AnnotationCheckPlanner.KindName(type)}，跟着几何走，没有可挪的位置。");

        context.SetState("挪注解");
        var target = FineArgs.Target(request, from);
        api.Call(annotation, "IAnnotation", "SetPosition2", target.X, target.Y, 0.0);
        api.Call(drawing, "IModelDoc2", "GraphicsRedraw2");
        var actual = FineLookup.Position(api, annotation, type) ?? target;
        var missed = MoveView.Missed(target, actual);
        var message = $"挪注解：视图「{viewName}」的「{name}」{FineArgs.Format(from)} → {FineArgs.Format(actual)}。";
        if (DrawingSheet.Footprint(api, annotation) is { } rect)
            message += $"{Environment.NewLine}· 现在占 ({FineArgs.Format(rect.Left)}, {FineArgs.Format(rect.Bottom)})–({FineArgs.Format(rect.Right)}, {FineArgs.Format(rect.Top)})。";
        if (missed.Count > 0)
            message += $"{Environment.NewLine}· {string.Join("、", missed)}方向没到（要 {FineArgs.Format(target)}），SolidWorks 把它摆回去了（坐标尺寸组、对齐的尺寸常这样）。";
        return missed.Count == 0 ? QuickOutcome.Ok(message) : QuickOutcome.Fail(message);
    }
}
