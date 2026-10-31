namespace HistoryStrenua;

/// <summary>
/// 快捷指令「尺寸链」（1.30.0，检查类，用户定）：不从基准开始的尺寸必须在尺寸链上，不在的列出来。只读，只改选择。算法见 <see cref="DimensionCheckPlanner.Chains"/>。
/// </summary>
internal static class ChainCheck
{
    public static QuickCommand Command { get; } = new(
        Key: "check-chain",
        CommandName: StrenuaIdentity.Domain + ".check.chain",
        Title: "尺寸链",
        Summary: "当前图纸页全部视图（轴测图除外）查线性尺寸：不从基准开始、也不在尺寸链上的列出来，并说明是换了行、链没接到基准，还是孤立，不改图。",
        Usage: "不用点视图：基准是最左、最上的直边，贯穿视图的对称轴也算。从基准沿「同一行、首尾相接」走得到的才算在链上。换行的用 dim.move 挪回那一行；孤立的改成从基准标（回执给出值），或接到链上。坐标尺寸、C 倒角、带公差的尺寸不查。在 SolidWorks 里选中（高亮）有问题的；手动点时弹窗，AI 调用不弹。不加、不删任何尺寸。",
        Run: Run);

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var document = HoleScan.ActiveDrawing(context);
        if (HoleScan.SheetViews(api, document).Count == 0)
            return QuickOutcome.Ok("当前图纸页上没有视图，没有可检查的。");

        context.SetState("检查");
        var read = DimensionCheck.ReadViews(context, "尺寸链");
        var lines = new List<string>();
        var count = 0;
        api.Call(document, "IModelDoc2", "ClearSelection2", true);
        for (var v = 0; v < read.Checks.Count; v++)
        {
            var dimensions = read.Dimensions[v];
            string Name(ChainItem item) => api.CallString(dimensions[item.Index].Annotation, "IAnnotation", "GetName");
            double Value(ChainItem item) => dimensions[item.Index].Geometry.Value;
            foreach (var issue in DimensionCheckPlanner.Chains(read.Checks[v], Positions(api, dimensions)))
            {
                count++;
                api.CallBool(dimensions[issue.Item.Index].Annotation, "IAnnotation", "Select3", true, null);
                lines.Add($"视图「{read.Checks[v].Name}」{Name(issue.Item)}={DimensionCheckPlanner.Mm(Value(issue.Item))}（{(issue.Item.Axis == PositionAxis.Horizontal ? "水平" : "竖直")}）：{Describe(issue, Name)}");
            }
        }

        api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        if (count == 0)
            return QuickOutcome.Ok($"尺寸链：当前图纸页 {read.Checks.Count} 个视图的尺寸都从基准开始或在链上。");

        var head = $"尺寸链：当前图纸页 {count} 个尺寸不从基准开始、也不在链上，已在 SolidWorks 里选中（高亮）。";
        DimensionCheck.Popup(context, "尺寸链", head, lines);
        return QuickOutcome.Ok(head + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }

    /// <summary>尺寸文字位置（图纸坐标），下标是 <see cref="ViewDimension.Index"/>。</summary>
    internal static Dictionary<int, SheetPoint> Positions(SolidWorks.SolidWorksApi api, IReadOnlyList<ScannedDimension> dimensions)
    {
        var positions = new Dictionary<int, SheetPoint>();
        foreach (var dimension in dimensions)
        {
            if (api.CallDoubles(dimension.Annotation, "IAnnotation", "GetPosition") is { Length: >= 2 } at)
                positions[dimension.Geometry.Index] = new SheetPoint(at[0], at[1]);
        }

        return positions;
    }

    internal static string Describe(ChainIssue issue, Func<ChainItem, string> name) => issue.Fault switch
    {
        ChainFault.Wrapped => $"和 {name(issue.Peer!)} 首尾相接但不在一行，挪到 {(issue.Item.Axis == PositionAxis.Horizontal ? "y" : "x")}={DimensionCheckPlanner.Mm(issue.Peer!.Line)}（图纸 mm）那一行。",
        ChainFault.Unrooted => $"和 {name(issue.Peer!)} 成链，但整条链接不到基准；补上基准到链头的尺寸，或改成从基准标（到较远一头 {DimensionCheckPlanner.Mm(issue.FromDatum)}）。",
        _ => $"两头都不在基准上，也没接上别的尺寸；改成从基准标（到较远一头 {DimensionCheckPlanner.Mm(issue.FromDatum)}），或接到链上。",
    };
}
