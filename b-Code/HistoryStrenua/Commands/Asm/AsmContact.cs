using System.Text;

namespace HistoryStrenua;

/// <summary>
/// 装配类「接触面检测」<c>strenua.asm.contact</c>（1.39.0，用户定：2026-043 型材连接不符合预期，要摸清装配点）：
/// 一个零部件和别的零部件的干涉区，以及它的每个接触面（同一个面一个对象，不同方向各一个）的截面。
/// </summary>
/// <remarks>
/// 接触判定在 <see cref="ContactPlanner"/>（平面对平面、孔对轴，按三角面片求重叠）；干涉沿用 <see cref="AsmInterference.Scan"/>。
/// 差一点没贴上的（<c>near</c> 内）也报，摆位错 0.5 mm 这种最常见的问题靠它看出来；包围盒挨着却一个接触面都没有的零部件单列。
/// 回执的 [F序号 ↔ 零部件@F序号] 直接给 <c>strenua.asm.mate</c>。截面一律进回执（用户定）：化简后的 SVG 文字比 PNG 省就接 SVG 原文，否则画 PNG 给路径（<see cref="ContactPlanner.Cost"/>）。
/// </remarks>
internal static class AsmContact
{
    public static QuickCommand Command { get; } = new(
        Key: "asm-contact",
        CommandName: StrenuaIdentity.Domain + ".asm.contact",
        Title: "接触面检测",
        Summary: "查活动总装里一个零部件和别的零部件的干涉区与接触面：每个接触面一个对象（平面 / 孔轴），写清贴合、差多少没贴上、重叠多大在哪，附截面 SVG。",
        Usage: "「零部件」填零部件列表的 name（子装配里的写「子装配-1/零件-1」，填子装配就查它整体）。「对象」写名字片段只看这些对方；"
               + "「范围」差多少 mm 以内的也报（默认 1）；「允差」以内算贴合（默认 0.05）。回执里的 [F12 ↔ 型材-1@F3] 是两边的面，直接给 strenua.asm.mate。"
               + "截面随回执给：文字版（SVG）比图省就直接接在回执后面，否则给 PNG 路径（模块数据目录 snapshots），SVG 文件也一并存着。只读，不改总装。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("name", "零部件", "零部件名（零部件列表的 name）", true),
            new QuickParameter("with", "对象", "对方名字片段，逗号分开；省略看全部", false, 1),
            new QuickParameter("near", "范围", "差多少 mm 以内也报，默认 1", false, 1),
            new QuickParameter("tol", "允差", "多少 mm 以内算贴合，默认 0.05", false, 1),
        ],
        Example: StrenuaIdentity.Domain + ".asm.contact name=AHS10-H4040-L700-1 near=2",
        Bind: arguments =>
        {
            var name = QuickArgs.Name(arguments, "name");
            var with = QuickArgs.Optional(arguments, "with") is { } text ? QuickArgs.Names(text, "with") : [];
            var near = Positive(arguments, "near", 1);
            var tol = Positive(arguments, "tol", 0.05);
            if (tol > near)
                throw new QuickArgumentException($"tol={tol} 比 near={near} 还大。");
            return context => Check(context, name, with, near, tol);
        },
        Readonly: true,
        SelfReport: true);

    /// <summary>回执最多列多少个接触对象、每个对象多少对。</summary>
    private const int ShownGroups = 30, ShownPairs = 8;

    private static double Positive(HistoryVulcan.Core.Commands.CommandContext arguments, string parameter, double fallback)
    {
        if (QuickArgs.Optional(arguments, parameter) is not { } text)
            return fallback;
        return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new QuickArgumentException($"{parameter}={text} 要写大于 0 的数（mm）。");
    }

    private static QuickOutcome Check(QuickCommandContext context, string name, IReadOnlyList<string> with, double near, double tol)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        var target = AssemblyGeometry.Component(api, assembly, name);
        var mineLeaves = AssemblyGeometry.LeavesOf(api, target);
        var mineNames = mineLeaves.Select(leaf => api.CallString(leaf, "IComponent2", "get_Name2")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var box = AssemblyGeometry.Box(api, target) ?? throw new QuickCommandException($"读不到「{name}」的包围盒（压缩了或没有实体？）。");
        bool Wanted(string other) => with.Count == 0 || with.Any(part => other.Contains(part, StringComparison.OrdinalIgnoreCase));

        context.SetState("读面");
        var mine = mineLeaves.SelectMany(leaf => AssemblyGeometry.Faces(api, leaf)).ToList();
        var pairs = new List<ContactPair>();
        var neighbours = new List<string>();
        foreach (var other in AssemblyGeometry.Leaves(api, assembly))
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var otherName = api.CallString(other, "IComponent2", "get_Name2");
            if (mineNames.Contains(otherName) || !Wanted(otherName) || AssemblyGeometry.Box(api, other) is not { } otherBox
                || !AssemblyGeometry.Touches([otherBox.Min, otherBox.Max], box, near))
                continue;
            neighbours.Add(otherName);
            var close = mine.Where(face => AssemblyGeometry.Touches([face.Box.Min, face.Box.Max], otherBox, near)).ToList();
            if (close.Count == 0)
                continue;
            foreach (var face in AssemblyGeometry.Faces(api, other, box, near))
            {
                var faceBox = face.Box;
                foreach (var own in close)
                {
                    if (AssemblyGeometry.Touches([faceBox.Min, faceBox.Max], own.Box, near) && ContactPlanner.Match(own, face, near, tol) is { } pair)
                        pairs.Add(pair);
                }
            }
        }

        context.SetState("干涉检查");
        var (interferences, touching) = AsmInterference.Scan(context, assembly, names => !names.Any(mineNames.Contains) || !names.Where(n => !mineNames.Contains(n)).Any(Wanted));
        var groups = ContactPlanner.Group(pairs, tol);
        var title = api.CallString(assembly, "IModelDoc2", "GetTitle");
        var panels = ContactPlanner.Panels(groups, tol);
        var svg = ContactPlanner.Svg($"{title} · {name}", panels);
        var file = DrawingSvg.Save(context, name + "-接触面", svg);

        var touchingParts = groups.SelectMany(group => group.Pairs).Select(pair => pair.Other.Owner).Distinct().ToList();
        var text = new StringBuilder($"接触面检测：「{name}」与 {touchingParts.Count} 个零部件有接触面（{groups.Count(g => g.Surface == ContactSurface.Plane)} 个平面、{groups.Count(g => g.Surface == ContactSurface.Cylinder)} 个孔 / 轴），"
                                     + $"干涉 {interferences.Count} 对{(touching > 0 ? $"（另有 {touching} 处零厚度贴合）" : string.Empty)}；总装 mm，范围 {ModelSpec.Format(near)}、允差 {ModelSpec.Format(tol)}。").AppendLine();
        if (interferences.Count > 0)
        {
            text.AppendLine("干涉：");
            foreach (var (volume, a, b, extent) in interferences.OrderByDescending(item => item.Volume).Take(ShownGroups))
            {
                var other = mineNames.Contains(a) ? b : a;
                text.AppendLine($"· {ModelSpec.Format(volume, "0.#")} mm³ ↔ {AsmInterference.Short(other)}  区域[{string.Join(",", extent.Select(value => ModelSpec.Format(value, "0.#")))}]");
            }
        }

        if (groups.Count > 0)
            text.AppendLine("接触面（一个面一个对象）：");
        foreach (var group in groups.Take(ShownGroups))
        {
            text.AppendLine($"{group.Id} {ContactPlanner.Describe(group)}");
            var lines = ContactPlanner.Lines(group, tol);
            foreach (var line in lines.Take(ShownPairs))
                text.AppendLine("  " + line);
            if (lines.Count > ShownPairs)
                text.AppendLine($"  …另有 {lines.Count - ShownPairs} 对。");
        }

        if (groups.Count > ShownGroups)
            text.AppendLine($"…另有 {groups.Count - ShownGroups} 个面，见 SVG。");
        var lonely = neighbours.Except(touchingParts).Where(other => !interferences.Any(item => item.A == other || item.B == other)).ToList();
        if (lonely.Count > 0)
            text.AppendLine($"包围盒挨着、却没有接触面也不干涉：{string.Join("、", lonely.Take(20).Select(AsmInterference.Short))}{(lonely.Count > 20 ? "…" : string.Empty)}");
        if (groups.Count == 0 && interferences.Count == 0)
            text.AppendLine($"「{name}」{ModelSpec.Format(near)} mm 内没有挨着任何零部件的面：悬空，或只靠别的曲面接触。");
        if (panels.Count > 0)
        {
            var (textCost, imageCost) = ContactPlanner.Cost(svg, panels.Count);
            if (textCost <= imageCost)
                text.AppendLine($"截面（SVG 文字约 {textCost} token，比图省；重叠区只画外接框，实际面积见 data-area）：").Append(svg);
            else
                text.Append($"截面图（PNG，文字版约 {textCost} token 比图的约 {imageCost} 贵，给图）：{SectionPng.Save(context, name + "-接触面", panels)}；SVG：{file}");
        }

        var rows = groups.SelectMany(group => group.Pairs.Select(pair => new Dictionary<string, string>
        {
            ["id"] = group.Id,
            ["face"] = ContactPlanner.Describe(group),
            ["mine"] = pair.Mine.Ref,
            ["other"] = pair.Other.Ref,
            ["state"] = ContactPlanner.State(pair, tol),
            ["amount"] = ModelSpec.Format(pair.Amount, "0.#"),
        })).ToList();
        return QuickOutcome.Ok(text.ToString().TrimEnd(), rows);
    }
}
