namespace HistoryStrenua;

/// <summary>
/// 快捷指令「重标尺寸」（1.29.0，检查类，用户定）：只要一个尺寸能由别的尺寸算出来就报，给出成组的尺寸和算式，删哪个由 AI 权衡。只读，只改选择。算法见 <see cref="DimensionCheckPlanner.Redundant"/>。
/// </summary>
internal static class RedundantCheck
{
    public static QuickCommand Command { get; } = new(
        Key: "check-redundant",
        CommandName: StrenuaIdentity.Domain + ".check.redundant",
        Title: "重标尺寸",
        Summary: "当前图纸页全部视图（轴测图除外）查能由别的尺寸算出来的线性尺寸，R、C 也算在内，跨视图也算；按组列出多几个、建议删哪个及算式，不改图。",
        Usage: "不用点视图：同一零件的线性、坐标尺寸按方向连成图，成环的就是重标；R 连圆心与切线、C 连斜边两头（如 13 = 18 − R5），对称轴、阵列、未注倒角这些不用尺寸也定了的关系也算。每组列出全部尺寸和要删几个，并按「公差、阵列、总尺寸、R 和 C、整数优先留下」给出建议删的和算式；可以换成组里别的，用 dim.delete 删完再查一遍。孔径、角度、斜向距离不判。在 SolidWorks 里选中（高亮）建议删的；手动点时弹窗，AI 调用不弹。不加、不删任何尺寸。",
        Run: Run);

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var document = HoleScan.ActiveDrawing(context);
        if (HoleScan.SheetViews(api, document).Count == 0)
            return QuickOutcome.Ok("当前图纸页上没有视图，没有可检查的。");

        context.SetState("检查");
        var read = DimensionCheck.ReadViews(context, "重标尺寸");
        var loops = DimensionCheckPlanner.Redundant(read.Checks, DimensionCheckPlanner.Unnoted(DimensionCheck.TechText(api, document)).Chamfer);
        if (loops.Count == 0)
            return QuickOutcome.Ok($"重标尺寸：当前图纸页 {read.Checks.Count} 个视图没有查出能算出来的尺寸。");

        string Name(RedundantMember member)
            => $"{read.Checks[member.View].Name}/{api.CallString(read.Dimensions[member.View][member.Index].Annotation, "IAnnotation", "GetName")}";
        string Label(RedundantMember member)
            => $"{Name(member)}={DimensionCheckPlanner.Mm(member.Value)}{(member.Tolerance ? "（带公差）" : string.Empty)}";

        api.Call(document, "IModelDoc2", "ClearSelection2", true);
        foreach (var formula in loops.SelectMany(loop => loop.Suggested))
            api.CallBool(read.Dimensions[formula.Target.View][formula.Target.Index].Annotation, "IAnnotation", "Select3", true, null);
        api.Call(document, "IModelDoc2", "GraphicsRedraw2");

        var lines = new List<string>();
        for (var i = 0; i < loops.Count; i++)
        {
            var loop = loops[i];
            lines.Add($"第 {i + 1} 组（{(loop.Horizontal ? "水平" : "竖直")}）：{loop.Members.Count} 个尺寸多 {loop.Surplus} 个：{string.Join("、", loop.Members.Select(Label))}");
            lines.AddRange(loop.Suggested.Select(formula => "  · 建议删 " + Formula(formula, Name)));
        }

        var head = $"重标尺寸：当前图纸页共 {loops.Count} 组、多 {loops.Sum(loop => loop.Surplus)} 个尺寸，已在 SolidWorks 里选中（高亮）建议删的。";
        DimensionCheck.Popup(context, "重标尺寸", head, lines);
        return QuickOutcome.Ok(head + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }

    /// <summary>「V/RD5 = V/RD6(1114) − V/RD7(703)」；相切、对称补的值写在后面。</summary>
    internal static string Formula(RedundantFormula formula, Func<RedundantMember, string> name)
    {
        var text = name(formula.Target) + " = ";
        if (formula.Terms.Count == 0)
            return text + $"{DimensionCheckPlanner.Mm(formula.Target.Value)}（两头已由相切、对称或阵列定了）";
        for (var i = 0; i < formula.Terms.Count; i++)
        {
            var (term, sign) = formula.Terms[i];
            text += (i == 0 ? sign < 0 ? "−" : string.Empty : sign < 0 ? " − " : " + ") + $"{name(term)}({DimensionCheckPlanner.Mm(term.Value)})";
        }

        return Math.Abs(formula.Given) > 0
            ? text + $" {(formula.Given < 0 ? "−" : "+")} {DimensionCheckPlanner.Mm(Math.Abs(formula.Given))}（相切、对称或阵列定的）"
            : text;
    }
}
