using System.Text;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 装配类「自由度」<c>strenua.asm.dof</c>（1.40.0，用户定：取出所有零件的自由度及已有的约束关系）：每个顶层零部件固定没有、SolidWorks 的约束状态、
/// 挂着哪些配合、按配合算出来还剩几个自由度、能往哪动。
/// </summary>
/// <remarks>
/// 自由度按 <see cref="MateTreePlanner.Freedom"/>（对方当作不动）；配合几何取 <c>IMateEntity2.EntityParams</c>（真机：总装坐标、米，点 3 + 方向 3 + 半径 2；
/// <c>ReferenceType2</c> 是选择类型，面 = 2，平面还是圆柱看半径是否大于 0）。只看总装自己的配合文件夹，子装配里的配合不算。
/// </remarks>
internal static class AsmDof
{
    public static QuickCommand Command { get; } = new(
        Key: "asm-dof",
        CommandName: StrenuaIdentity.Domain + ".asm.dof",
        Title: "自由度",
        Summary: "列活动总装每个顶层零部件：固定没有、约束状态、挂着的配合（种类与对方）、按配合算还剩几个自由度、能沿哪移 / 绕哪转。",
        Usage: "「零部件」填名字片段只看这些；省略看全部。自由度把配合的对方当作不动来算（对方自己能动的，看它那一行）；固定的记 0。只读。",
        Run: QuickCommand.Unbound,
        Parameters: [new QuickParameter("name", "零部件", "零部件名片段；省略看全部", false)],
        Example: StrenuaIdentity.Domain + ".asm.dof name=脚杯连接板",
        Bind: arguments =>
        {
            var name = QuickArgs.Optional(arguments, "name");
            return context => List(context, name);
        },
        Readonly: true,
        SelfReport: true);

    /// <summary>一条配合挂在某个顶层零部件上的样子：配合名、种类、对方、本零部件这边的约束。</summary>
    internal sealed record Hook(string Mate, int Type, string Partner, MateConstraint Constraint);

    /// <summary>总装配合文件夹里每条配合按顶层零部件拆开（两边各记一条）。</summary>
    internal static Dictionary<string, List<Hook>> Hooks(SolidWorksApi api, object assembly)
    {
        var hooks = new Dictionary<string, List<Hook>>(StringComparer.OrdinalIgnoreCase);
        foreach (var feature in AsmMate.Mates(api, assembly))
        {
            if (api.CallBool(feature, "IFeature", "IsSuppressed") || api.Call(feature, "IFeature", "GetSpecificFeature2") is not { } mate)
                continue;
            int type;
            List<(string Owner, MateGeometry Geometry, Vec3 Point, Vec3 Vector)> sides;
            try
            {
                type = api.CallInt(mate, "IMate2", "get_Type");
                sides = Enumerable.Range(0, api.CallInt(mate, "IMate2", "GetMateEntityCount"))
                    .Select(i => api.Call(mate, "IMate2", "MateEntity", i))
                    .Where(entity => entity is not null)
                    .Select(entity => Side(api, entity!))
                    .ToList();
            }
            catch (InvalidCastException)
            {
                continue;
            }

            var name = api.CallString(feature, "IFeature", "get_Name");
            for (var i = 0; i < sides.Count; i++)
            {
                var partner = sides.Count == 2 ? sides[1 - i] : sides[i];
                var c = new MateConstraint(type, sides[i].Geometry, sides[i].Point, sides[i].Vector, partner.Vector);
                (hooks.TryGetValue(sides[i].Owner, out var list) ? list : hooks[sides[i].Owner] = []).Add(new Hook(name, type, partner.Owner, c));
            }
        }

        return hooks;
    }

    /// <summary>
    /// 已定住的零部件：固定的、总装自己，再逐轮把「只靠连到已定住零部件的配合就定死」的加进来，直到不再变。
    /// 配合连到还能动的零部件不算约束（真机 043：子零部件配到立柱底板上，不能反过来算成立柱底板被定死）。
    /// </summary>
    internal static HashSet<string> Grounded(SolidWorksApi api, IReadOnlyDictionary<string, object> tops, IReadOnlyDictionary<string, List<Hook>> hooks, IReadOnlySet<string> fixedNames)
    {
        var grounded = new HashSet<string>(fixedNames, StringComparer.OrdinalIgnoreCase) { "总装" };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (name, top) in tops)
            {
                if (grounded.Contains(name) || hooks.GetValueOrDefault(name) is not { } own)
                    continue;
                var (origin, _) = ModelAccess.Frame(api, top);
                var anchored = own.Where(hook => grounded.Contains(hook.Partner) && !hook.Partner.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(hook => hook.Constraint).ToList();
                if (anchored.Count > 0 && MateTreePlanner.Freedom(anchored, new Vec3(origin.X, origin.Y, origin.Z)).Dof == 0)
                    changed = grounded.Add(name) || changed;
            }
        }

        return grounded;
    }

    private static (string Owner, MateGeometry Geometry, Vec3 Point, Vec3 Vector) Side(SolidWorksApi api, object entity)
    {
        var owner = api.Call(entity, "IMateEntity2", "get_ReferenceComponent") is { } component
            ? api.CallString(AssemblyContacts.TopOf(api, component), "IComponent2", "get_Name2")
            : "总装";
        var p = api.CallDoubles(entity, "IMateEntity2", "get_EntityParams");
        if (p.Length < 6)
            return (owner, MateGeometry.Other, default, default);
        var reference = api.CallInt(entity, "IMateEntity2", "get_ReferenceType2");
        // swSelectType_e：面 2、顶点 3、基准面 4、基准轴 5、边 1、原点 / 点 11、22。
        var geometry = reference switch
        {
            2 => p.Length > 6 && p[6] > 1e-9 ? MateGeometry.Cylinder : MateGeometry.Plane,
            4 => MateGeometry.Plane,
            1 or 5 => MateGeometry.Line,
            3 or 11 or 22 => MateGeometry.Point,
            _ => MateGeometry.Other,
        };
        return (owner, geometry, new Vec3(p[0], p[1], p[2]) * 1000, new Vec3(p[3], p[4], p[5]));
    }

    private static QuickOutcome List(QuickCommandContext context, string? filter)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        var hooks = Hooks(api, assembly);
        var rows = new List<Dictionary<string, string>>();
        var text = new StringBuilder();
        var (fixedCount, full, under) = (0, 0, 0);
        var tops = AssemblyContacts.Tops(api, assembly).ToDictionary(top => api.CallString(top, "IComponent2", "get_Name2"), StringComparer.OrdinalIgnoreCase);
        var fixedNames = tops.Where(top => ModelAccess.Fixed(api, top.Value)).Select(top => top.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var grounded = Grounded(api, tops, hooks, fixedNames);
        foreach (var (name, top) in tops)
        {
            if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            var own = hooks.GetValueOrDefault(name) ?? [];
            var isFixed = fixedNames.Contains(name);
            var (origin, _) = ModelAccess.Frame(api, top);
            var anchored = own.Where(hook => grounded.Contains(hook.Partner) && !hook.Partner.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            var (dof, free) = isFixed ? (0, (IReadOnlyList<string>)[]) : MateTreePlanner.Freedom(anchored.Select(hook => hook.Constraint).ToList(), new Vec3(origin.X, origin.Y, origin.Z));
            var status = api.CallInt(top, "IComponent2", "GetConstrainedStatus") switch
            {
                2 => "欠定义",
                3 => "完全定义",
                4 => "过定义",
                5 or 6 => "无解",
                _ => "未知",
            };
            if (isFixed)
                fixedCount++;
            else if (dof == 0)
                full++;
            else
                under++;
            var mates = string.Join("；", own.Select(hook => $"{hook.Mate} {MateSpec.TypeName(hook.Type)}↔{AsmInterference.Short(hook.Partner)}"));
            var state = isFixed ? "固定" : dof == 0 ? $"自由度 0（{status}）" : $"剩 {dof} 个自由度（{status}）：{string.Join("、", free)}";
            var loose = isFixed ? 0 : own.Count - anchored.Count;
            var redundant = isFixed && own.Any(hook => fixedNames.Contains(hook.Partner));
            text.AppendLine($"· {name}  {state}{(own.Count > 0 ? $"  配合 {own.Count}：{mates}" : "  无配合")}"
                            + (loose > 0 ? $"（其中 {loose} 条连到还能动的零部件，没算进去）" : string.Empty)
                            + (redundant ? "  ⚠ 固定着又和固定件配合（多余）" : string.Empty));
            rows.Add(new Dictionary<string, string>
            {
                ["name"] = name,
                ["fixed"] = isFixed ? "是" : "否",
                ["status"] = status,
                ["dof"] = dof.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["free"] = string.Join("、", free),
                ["mates"] = mates,
            });
        }

        var head = $"自由度：「{api.CallString(assembly, "IModelDoc2", "GetTitle")}」{rows.Count} 个顶层零部件，固定 {fixedCount}、配合定死 {full}、还能动 {under}（只算连到已定住零部件的配合：从固定件起逐轮往外推）。";
        return rows.Count == 0 ? QuickOutcome.Nothing(head) : QuickOutcome.Ok(head + Environment.NewLine + text.ToString().TrimEnd(), rows);
    }
}
