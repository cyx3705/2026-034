using System.Text;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 装配类「双同心」<c>strenua.asm.concentric</c>（1.40.0，用户定：所有接触面取出来以后，两个零部件之间有两组同心圆就自动上两个同轴心）。
/// </summary>
/// <remarks>
/// 只看互相贴合的两块（有贴合平面），它们之间同轴的孔对孔 / 孔对轴（<see cref="ContactPlanner.Coaxial"/>，同一根轴线只算一组）有两组以上时，
/// 取相距最远的两组各加一个同轴心，最大贴合面没配过就一起加重合（<see cref="MateTreePlanner.Choose"/>，能去掉自由度的才加）。动的一边：一边固定一边不固定就动不固定的，
/// 都固定就动包围盒小的。有加工件的装配体才上约束。
/// </remarks>
internal static class AsmConcentric
{
    public static QuickCommand Command { get; } = new(
        Key: "asm-concentric",
        CommandName: StrenuaIdentity.Domain + ".asm.concentric",
        Title: "双同心",
        Summary: "扫活动总装里互相贴合的零部件：两块之间有两组以上同轴孔（孔对孔、孔对轴）就取相距最远的两组各加一个同轴心；默认直接加。",
        Usage: "「零部件」填名字只扫和它贴合的；省略扫全部。apply=否 只列找到的同轴孔与打算加的配合。动的是不固定的那边（都固定就动小的那块），位置动了就撤回。"
               + "总装里全是外购件时不上约束。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("name", "零部件", "零部件名；省略扫全部", false),
            new QuickParameter("apply", "加配合", "是（默认）/ 否 = 只预览", false),
        ],
        Example: StrenuaIdentity.Domain + ".asm.concentric apply=否",
        Bind: arguments =>
        {
            var name = QuickArgs.Optional(arguments, "name");
            var apply = QuickArgs.Optional(arguments, "apply") is null || QuickArgs.Flag(arguments, "apply");
            return context => Run(context, name, apply);
        },
        SelfReport: true);

    private static QuickOutcome Run(QuickCommandContext context, string? name, bool apply)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        AssemblyContacts.RequireMachined(api, assembly, "双同心");
        var tops = AssemblyContacts.Tops(api, assembly).ToDictionary(top => api.CallString(top, "IComponent2", "get_Name2"), StringComparer.OrdinalIgnoreCase);
        if (name is not null && !tops.ContainsKey(name))
            throw new QuickArgumentException($"总装顶层没有「{name}」。");
        context.SetState("扫同轴孔");
        var only = name is null ? null : new HashSet<string>([name], StringComparer.OrdinalIgnoreCase);
        var links = AssemblyContacts.Scan(context, assembly, AsmMateTree.Tol, AsmMateTree.Near, only).Where(link => link.Planes.Count > 0 && link.Axes.Count >= 2).ToList();
        if (links.Count == 0)
            return QuickOutcome.Nothing($"双同心：{(name is null ? "总装里" : $"和「{name}」贴合的")}没有两块之间有两组以上同轴孔的。");

        var hooks = AsmDof.Hooks(api, assembly);
        var text = new StringBuilder($"双同心：{links.Count} 对零部件之间有两组以上同轴孔{(apply ? string.Empty : "（只预览，没加配合）")}：").AppendLine();
        var (added, failed) = (0, 0);
        foreach (var link in links)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var (a, b) = (tops[link.A], tops[link.B]);
            var (fixedA, fixedB) = (ModelAccess.Fixed(api, a), ModelAccess.Fixed(api, b));
            var childIsA = fixedA != fixedB ? !fixedA : Volume(api, a) <= Volume(api, b);
            var child = childIsA ? link.A : link.B;
            var (origin, _) = ModelAccess.Frame(api, childIsA ? a : b);
            var existing = (hooks.GetValueOrDefault(child) ?? []).Select(hook => hook.Constraint).ToList();
            // 只上两同心零部件会沿孔轴向飘：贴合面没配过就连最大贴合面的重合一起加（能去掉自由度的才加）。
            var chosen = MateTreePlanner.Choose(link.Planes, link.Axes, childIsA, existing, new Vec3(origin.X, origin.Y, origin.Z));
            var (lines, ok) = AsmMateTree.Apply(context, chosen, apply);
            added += ok;
            failed += apply ? chosen.Count - ok : 0;
            var holes = string.Join("、", link.Axes.Select(pair => $"Ø{ModelSpec.Format((childIsA ? pair.Mine : pair.Other).Radius * 2)}"));
            text.AppendLine($"· {AsmInterference.Short(child)} → {AsmInterference.Short(childIsA ? link.B : link.A)}：{link.Axes.Count} 组同轴（{holes}）；"
                            + (chosen.Count == 0 ? "已有配合定死，不加" : string.Join("；", lines)));
        }

        if (apply)
            text.Append($"共加 {added} 条同轴心{(failed > 0 ? $"，{failed} 条没加上" : string.Empty)}；总装没存。");
        var outcome = text.ToString().TrimEnd();
        return failed > 0 ? QuickOutcome.Fail(outcome) : QuickOutcome.Ok(outcome);
    }

    private static double Volume(SolidWorksApi api, object component)
        => AssemblyGeometry.Box(api, component) is { } box ? (box.Max.X - box.Min.X) * (box.Max.Y - box.Min.Y) * (box.Max.Z - box.Min.Z) : double.MaxValue;
}
