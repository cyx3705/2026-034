using System.Text;

namespace HistoryStrenua;

/// <summary>
/// 装配类「贴合树」<c>strenua.asm.matetree</c>（1.40.0，用户定：从固定块沿贴合面像树一样往外长到末端，成环则断开最远处的贴合关系；有加工件的装配体才上约束）。
/// </summary>
/// <remarks>
/// 流程是「先摆位置、解除固定再上约束」：每个子零部件配到它的父零部件上（<see cref="MateTreePlanner.Choose"/> 挑配合，只要能去掉自由度的），
/// 经 <see cref="AsmMate.Add"/> 加：子零部件固定着先解除固定，加完位置动了就撤回——摆好的位置就是核对标准。根保持固定。
/// 子装配当一整块（它里面有没有加工件、要不要上约束，打开那个子装配单独跑）。
/// </remarks>
internal static class AsmMateTree
{
    public static QuickCommand Command { get; } = new(
        Key: "asm-matetree",
        CommandName: StrenuaIdentity.Domain + ".asm.matetree",
        Title: "贴合树",
        Summary: "从固定块沿贴合面广度优先往外长成树，每个零部件配到它的父零部件上（最大贴合面重合、两同心、别的方向的贴合面），成环处断开最远的贴合；默认直接加配合。",
        Usage: "「根」填固定块的零部件名，省略我挑（固定着的、贴合关系最多的）。apply=否 只列树和打算加的配合、不动总装。"
               + "子零部件固定着会先解除固定；加完位置动了就撤回那条（摆位为准），一个零部件有一条没加上就把它这一组全撤回、恢复固定。"
               + "配完还定不死的（型材只靠一个面贴着、只有一个孔）默认保持固定、不加，partial=是 才照加。总装里全是外购件时不上约束。子装配当一整块。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("root", "根", "固定块的零部件名；省略自动挑", false),
            new QuickParameter("apply", "加配合", "是（默认）/ 否 = 只预览", false),
            new QuickParameter("partial", "欠定义也加", "否（默认，定不死的保持固定）/ 是", false),
        ],
        Example: StrenuaIdentity.Domain + ".asm.matetree apply=否",
        Bind: arguments =>
        {
            var root = QuickArgs.Optional(arguments, "root");
            var apply = QuickArgs.Optional(arguments, "apply") is null || QuickArgs.Flag(arguments, "apply");
            var partial = QuickArgs.Optional(arguments, "partial") is not null && QuickArgs.Flag(arguments, "partial");
            return context => Grow(context, root, apply, partial);
        },
        SelfReport: true);

    /// <summary>贴合算到多少 mm；同轴孔轴线偏差也用它。</summary>
    internal const double Tol = 0.05;

    /// <summary>同轴孔轴向隔开多少 mm 以内也算（螺钉孔对模组上的螺纹孔，中间常隔着沉头）。</summary>
    internal const double Near = 1;

    private static QuickOutcome Grow(QuickCommandContext context, string? rootName, bool apply, bool partial)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        AssemblyContacts.RequireMachined(api, assembly, "贴合树");
        context.SetState("扫贴合面");
        var tops = AssemblyContacts.Tops(api, assembly).ToDictionary(top => api.CallString(top, "IComponent2", "get_Name2"), StringComparer.OrdinalIgnoreCase);
        var links = AssemblyContacts.Scan(context, assembly, Tol, Near);
        var edges = links.Where(link => link.Planes.Count > 0).Select(link => new ContactEdge(link.A, link.B, link.Area)).ToList();
        var nodes = tops.Keys.ToList();
        var fixedNodes = tops.Where(top => ModelAccess.Fixed(api, top.Value)).Select(top => top.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = rootName is null
            ? MateTreePlanner.Root(nodes, edges, fixedNodes)
            : tops.Keys.FirstOrDefault(key => key.Equals(rootName, StringComparison.OrdinalIgnoreCase)) ?? throw new QuickArgumentException($"总装顶层没有「{rootName}」。");
        var (tree, broken, unreached) = MateTreePlanner.Tree(nodes, edges, root);

        context.SetState(apply ? "加配合" : "预览");
        var hooks = AsmDof.Hooks(api, assembly);
        var text = new StringBuilder($"贴合树：根「{root}」{(fixedNodes.Contains(root) ? "（固定）" : "（没固定，建议先 asm.move fix=是）")}，{tree.Count} 条树边、成环断开 {broken.Count} 条、连不上 {unreached.Count} 个{(apply ? string.Empty : "；只预览，没加配合")}。").AppendLine();
        var rows = new List<Dictionary<string, string>>();
        var (added, failed, kept) = (0, 0, 0);
        // 已经放好的：根与前面配过的。子零部件已有的配合只算连到这些的（配到它身上的下游配合不算约束它）。
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root, "总装" };
        foreach (var edge in tree)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var link = links.First(item => (item.A == edge.Parent && item.B == edge.Child) || (item.A == edge.Child && item.B == edge.Parent));
            var (origin, _) = ModelAccess.Frame(api, tops[edge.Child]);
            var at = new Vec3(origin.X, origin.Y, origin.Z);
            var existing = (hooks.GetValueOrDefault(edge.Child) ?? []).Where(hook => placed.Contains(hook.Partner)).Select(hook => hook.Constraint).ToList();
            placed.Add(edge.Child);
            var chosen = MateTreePlanner.Choose(link.Planes, link.Axes, link.A == edge.Child, existing, at);
            var (dof, free) = MateTreePlanner.Freedom(existing.Concat(chosen.Select(c => c.Constraint)).ToList(), at);
            var head = $"{new string(' ', Math.Min(edge.Depth, 6) * 2)}{edge.Depth}. {AsmInterference.Short(edge.Child)} ← {AsmInterference.Short(edge.Parent)}（贴合 {ModelSpec.Format(edge.Area, "0")} mm²）：";
            var rest = dof > 0 ? $"剩 {dof} 个自由度（{string.Join("、", free)}）" : "定死";
            string line;
            if (chosen.Count == 0)
            {
                line = "已有配合，不加";
            }
            else if (dof > 0 && !partial)
            {
                kept++;
                line = $"定不死，保持固定不加（能加的只有 {string.Join("；", Apply(context, chosen, false).Lines)}，{rest}）";
            }
            else
            {
                var child = tops[edge.Child];
                var (axes, wasFixed) = (ModelAccess.Frame(api, child).Axes, ModelAccess.Fixed(api, child));
                var before = AsmMate.Mates(api, assembly).Select(feature => api.CallString(feature, "IFeature", "get_Name")).ToHashSet(StringComparer.Ordinal);
                var (lines, ok) = Apply(context, chosen, apply);
                if (apply && ok < chosen.Count)
                {
                    // 一组没加全就整组撤回、恢复原来的固定，免得留下一个半配半飘的零部件。
                    AsmMate.DeleteNew(api, assembly, before);
                    AsmMate.Restore(context, assembly, child, axes, origin, wasFixed);
                    failed++;
                    line = string.Join("；", lines) + "；有一条没加上，这一组已撤回、恢复原样";
                }
                else
                {
                    added += ok;
                    line = string.Join("；", lines) + "；" + rest;
                }
            }

            text.AppendLine(head + line);
            rows.Add(new Dictionary<string, string>
            {
                ["depth"] = edge.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["child"] = edge.Child,
                ["parent"] = edge.Parent,
                ["mates"] = line,
                ["dof"] = dof.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        if (broken.Count > 0)
            text.AppendLine($"成环断开（离根最远的贴合，不加配合）：{string.Join("、", broken.Select(edge => $"{AsmInterference.Short(edge.A)}–{AsmInterference.Short(edge.B)}"))}");
        if (unreached.Count > 0)
            text.AppendLine($"连不上根（没有贴合面通到它，保持原样）：{string.Join("、", unreached.Select(AsmInterference.Short))}");
        if (apply)
            text.Append($"共加 {added} 条配合{(failed > 0 ? $"，{failed} 个零部件没配上已撤回（见上）" : string.Empty)}{(kept > 0 ? $"，{kept} 个定不死保持固定" : string.Empty)}；总装没存，strenua.asm.dof 看自由度。");
        var outcome = text.ToString().TrimEnd();
        return failed > 0 ? QuickOutcome.Fail(outcome) : QuickOutcome.Ok(outcome, rows);
    }

    /// <summary>逐条加（或只写出来）；返回每条的一句话与成功条数。双同心也用它。</summary>
    internal static (IReadOnlyList<string> Lines, int Ok) Apply(QuickCommandContext context, IReadOnlyList<MateCandidate> chosen, bool apply)
    {
        var lines = new List<string>();
        var ok = 0;
        foreach (var candidate in chosen)
        {
            var kind = candidate.Type == 1 ? "同轴心" : "重合";
            var what = $"{kind} {candidate.Child.Split('@')[1]}↔{AsmInterference.Short(candidate.Parent)}";
            if (!apply)
            {
                lines.Add(what);
                continue;
            }

            var outcome = AsmMate.Add(context, candidate.Type, kind, MateSpec.Target(candidate.Child, "a"), MateSpec.Target(candidate.Parent, "b"), 0, 2, false, false, Tol + 1e-6);
            if (outcome.Success)
            {
                ok++;
                // 在允差内被吸正的写出来（「微调 平移 0.03 mm」）。
                lines.Add(outcome.Message.Contains("微调", StringComparison.Ordinal) ? $"{what}（微调 {outcome.Message.Split("微调 ")[1].Split('（')[0]}）" : what);
            }
            else
            {
                // 回执头一句之后是原因（「没加上——…」「和现在的摆位对不上——…」）。
                var reason = outcome.Message.Split("——") is { Length: > 1 } parts ? parts[1].Split('。')[0] : outcome.Message;
                lines.Add($"{what} ✗ {reason}");
            }
        }

        return (lines, ok);
    }
}
