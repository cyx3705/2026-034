using System.Text;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 装配类配合（1.39.0，用户定：先按坐标摆位置，再加配合）：加配合、列配合、删配合。
/// </summary>
/// <remarks>
/// 加配合默认<b>不许动零部件</b>：加完读回 a 那边零部件的位置，挪了超过 0.01 mm / 0.01° 就撤掉配合、退回原位并报差多少——
/// 摆好的位置和配合说的是一回事才留下，配合同时成了摆位的核对。确实要配合把它拉过去就给 <c>move=是</c>。
/// a 那边的零部件固定着就先解除固定（配合不动固定件）；回执带它现在的约束状态（欠定义 / 完全定义 / 过定义）。
/// </remarks>
internal static class AsmMate
{
    public static QuickCommand MateCommand { get; } = new(
        Key: "asm-mate",
        CommandName: StrenuaIdentity.Domain + ".asm.mate",
        Title: "加配合",
        Summary: "在活动总装里给两个几何加配合（重合、同轴心、平行、垂直、相切、距离、角度、锁定）；默认不许零部件被拉动，拉动了就撤掉并报差多少。",
        Usage: "a、b 写 零部件@F12（面，序号见 strenua.asm.contact 回执）、零部件@前视 / 上视 / 右视 / 原点、@前视（总装自己的）；锁定只写两个零部件名。"
               + "a 那边是要动的零部件，固定着会先解除固定。「数值」距离 mm、角度 °；「对齐」同向 / 反向 / 最近（默认，跟着现在的摆位）；flip=是 反过来。"
               + "move=是 才允许配合把零部件拉过去，否则挪了就撤回。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("type", "种类", "重合 / 同轴心 / 平行 / 垂直 / 相切 / 距离 / 角度 / 锁定", true),
            new QuickParameter("a", "几何A", "要动的那边，如 底板-1@F12", true, 1),
            new QuickParameter("b", "几何B", "参照那边，如 型材-1@F3、@上视", true, 1),
            new QuickParameter("value", "数值", "距离 mm / 角度 °", false, 2),
            new QuickParameter("align", "对齐", "同向 / 反向 / 最近（默认）", false, 2),
            new QuickParameter("flip", "反向", "是 / 否（默认）", false, 2),
            new QuickParameter("move", "允许拉动", "是 / 否（默认）", false, 2),
        ],
        Example: StrenuaIdentity.Domain + ".asm.mate type=重合 a=底板-1@F5 b=AHS10-H4040-L1800-1@F12",
        Bind: arguments =>
        {
            var (type, typeName, unit) = MateSpec.Type(QuickArgs.Name(arguments, "type"));
            var a = MateSpec.Target(QuickArgs.Name(arguments, "a"), "a");
            var b = MateSpec.Target(QuickArgs.Name(arguments, "b"), "b");
            var lockMate = type == 16;
            if (lockMate != (a.Kind == MateTarget.Kinds.Component) || lockMate != (b.Kind == MateTarget.Kinds.Component))
                throw new QuickArgumentException(lockMate ? "锁定的 a、b 只写零部件名（不带 @）。" : "a、b 要写到几何：零部件@F序号 / @前视 / @原点；只有锁定写零部件名。");
            double value = 0;
            if (unit is not null)
            {
                var text = QuickArgs.Optional(arguments, "value") ?? throw new QuickArgumentException($"{typeName}配合要给 value=（{unit}）。");
                if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) || value < 0)
                    throw new QuickArgumentException($"value={text} 要写不小于 0 的数（{unit}）。");
            }

            var align = MateSpec.Align(QuickArgs.Optional(arguments, "align"));
            var flip = QuickArgs.Optional(arguments, "flip") is not null && QuickArgs.Flag(arguments, "flip");
            var move = QuickArgs.Optional(arguments, "move") is not null && QuickArgs.Flag(arguments, "move");
            return context => Add(context, type, typeName, a, b, value, align, flip, move);
        },
        SelfReport: true);

    public static QuickCommand ListCommand { get; } = new(
        Key: "asm-matelist",
        CommandName: StrenuaIdentity.Domain + ".asm.matelist",
        Title: "配合列表",
        Summary: "列活动总装的配合：名字、种类、连着哪些零部件、压缩、报错；可只看和某个零部件有关的。",
        Usage: "「零部件」填名字片段只列和它有关的；省略列全部。名字供删配合用。",
        Run: QuickCommand.Unbound,
        Parameters: [new QuickParameter("name", "零部件", "零部件名片段；省略列全部", false)],
        Example: StrenuaIdentity.Domain + ".asm.matelist name=立柱底板",
        Bind: arguments =>
        {
            var name = QuickArgs.Optional(arguments, "name");
            return context => List(context, name);
        },
        Readonly: true,
        SelfReport: true);

    public static QuickCommand DeleteCommand { get; } = new(
        Key: "asm-matedelete",
        CommandName: StrenuaIdentity.Domain + ".asm.matedelete",
        Title: "删配合",
        Summary: "按名字删活动总装的配合（逗号分开删几个）；零部件留在原位、不自动固定。",
        Usage: "「配合」填配合列表的 name，逗号分开。删完零部件不挪，要固定用 strenua.asm.move name=… fix=是。",
        Run: QuickCommand.Unbound,
        Parameters: [new QuickParameter("name", "配合", "配合名，逗号分开", true)],
        Example: StrenuaIdentity.Domain + ".asm.matedelete name=重合1",
        Bind: arguments =>
        {
            var names = QuickArgs.Names(QuickArgs.Name(arguments, "name"), "name");
            return context => Delete(context, names);
        },
        SelfReport: true);

    /// <summary>读回位置差多少算被配合拉动（mm / °）。</summary>
    private const double Moved = 0.01;

    /// <summary>
    /// 加一条配合（双同心、贴合树也走这里）。a 那边所属的<b>顶层</b>零部件是要动的那个（子装配里的面也是整个子装配动）。
    /// <paramref name="snap"/>：位置动了多少 mm / ° 以内算吸正（贴合树、双同心给贴合允差 0.05——真机 043 导入的模组差 0.03–0.05 mm），默认 0.01。
    /// </summary>
    internal static QuickOutcome Add(QuickCommandContext context, int type, string typeName, MateTarget a, MateTarget b, double value, int align, bool flip, bool move, double snap = Moved)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        var componentA = a.Component is null ? null : AssemblyGeometry.Component(api, assembly, a.Component);
        var componentB = b.Component is null ? null : AssemblyGeometry.Component(api, assembly, b.Component);
        var moving = AssemblyContacts.TopOf(api, componentA ?? componentB ?? throw new QuickArgumentException("a、b 至少一边要是零部件的几何。"));
        var movingName = api.CallString(moving, "IComponent2", "get_Name2");
        var (origin, axes) = ModelAccess.Frame(api, moving);
        var wasFixed = ModelAccess.Fixed(api, moving);
        if (wasFixed)
        {
            api.ClearSelection(assembly);
            api.Call(moving, "IComponent2", "Select4", false, null, false);
            api.Call(assembly, "IAssemblyDoc", "UnfixComponent");
        }

        api.ClearSelection(assembly);
        Select(api, assembly, a, componentA, false);
        Select(api, assembly, b, componentB, true);
        // 普通（非限位）距离 / 角度配合：上下限都填同一个值，填 0 会被当成限位、报过定义（真机）。
        var distance = type == 5 ? value / 1000 : 0.0;
        var angle = type == 6 ? value * Math.PI / 180 : 0.0;
        var existing = Mates(api, assembly).Select(feature => api.CallString(feature, "IFeature", "get_Name")).ToHashSet(StringComparer.Ordinal);
        object?[] args = [type, align, flip, distance, distance, distance, 1.0, 1.0, angle, angle, angle, false, false, 0, 0];
        var mate = api.Call(assembly, "IAssemblyDoc", "AddMate5", args);
        var error = Convert.ToInt32(args[14]);
        api.ClearSelection(assembly);
        var what = $"{typeName} {a} ↔ {b}{(type is 5 or 6 ? $" = {ModelSpec.Format(value)}{(type == 5 ? " mm" : "°")}" : string.Empty)}";
        if (mate is null || error != 1)
        {
            // 真机：过定义时 AddMate5 报错码 5，却照样在配合文件夹里留下一个出错的配合，要删掉。
            DeleteNew(api, assembly, existing);
            Restore(context, assembly, moving, axes, origin, wasFixed);
            return QuickOutcome.Fail($"加配合：{what} 没加上——{MateSpec.Error(error)}。「{movingName}」已退回原样。");
        }

        api.Call(assembly, "IModelDoc2", "EditRebuild3");
        var added = Mates(api, assembly).FirstOrDefault(feature => !existing.Contains(api.CallString(feature, "IFeature", "get_Name")));
        var mateName = added is null ? api.CallString(mate, "IFeature", "get_Name") : api.CallString(added, "IFeature", "get_Name");
        var (after, turned) = ModelAccess.Frame(api, moving);
        var shift = Math.Sqrt(Math.Pow(after.X - origin.X, 2) + Math.Pow(after.Y - origin.Y, 2) + Math.Pow(after.Z - origin.Z, 2));
        var turn = Angle(axes, turned);
        var drift = $"平移 {ModelSpec.Format(shift, "0.###")} mm、转 {ModelSpec.Format(turn, "0.###")}°";
        if ((shift > snap || turn > snap) && !move)
        {
            DeleteNew(api, assembly, existing);
            Restore(context, assembly, moving, axes, origin, wasFixed);
            return QuickOutcome.Fail($"加配合：{what} 和现在的摆位对不上——它会把「{movingName}」{drift}（原点 {ModelAccess.Format(origin)} → {ModelAccess.Format(after)}）。"
                                     + "已撤掉配合、退回原位。先查摆位（strenua.asm.contact），或确认要它拉过去就加 move=是。");
        }

        var warning = new object?[] { false };
        var code = api.CallInt(added ?? mate, "IFeature", "GetErrorCode2", warning);
        var status = api.CallInt(moving, "IComponent2", "GetConstrainedStatus") switch
        {
            2 => "欠定义",
            3 => "完全定义",
            4 => "过定义",
            5 or 6 => "无解",
            _ => "约束状态未知",
        };
        var text = $"加配合：{mateName}（{what}），「{movingName}」{(wasFixed ? "已解除固定，" : string.Empty)}{(shift > snap || turn > snap ? $"被拉动 {drift}，原点现在 {ModelAccess.Format(after)}" : shift > Moved || turn > Moved ? $"微调 {drift}（在允差内吸正）" : "位置没动")}，现在{status}（总装没存）。";
        if (code != 0)
            return QuickOutcome.Fail(text + $" 配合{(Convert.ToBoolean(warning[0]) ? "有警告" : "报错")}（码 {code}），用 strenua.asm.matelist 看。");
        return status is "过定义" or "无解" ? QuickOutcome.Fail(text) : QuickOutcome.Ok(text);
    }

    private static void Select(SolidWorksApi api, object assembly, MateTarget target, object? component, bool append)
    {
        var picked = target.Kind switch
        {
            MateTarget.Kinds.Face => api.CallBool(AssemblyGeometry.Face(api, component!, target.Index), "IEntity", "Select4", append, null),
            MateTarget.Kinds.Component => api.CallBool(component!, "IComponent2", "Select4", append, null, false),
            _ => api.CallBool(component is null
                    ? AssemblyGeometry.Feature(api, assembly, false, target.Kind, target.Index)
                    : AssemblyGeometry.Feature(api, component, true, target.Kind, target.Index), "IFeature", "Select2", append, 0),
        };
        if (!picked)
            throw new QuickCommandException($"选不中 {target}。");
    }

    /// <summary>把零部件放回原来的变换与固定状态。</summary>
    internal static void Restore(QuickCommandContext context, object assembly, object component, double[,] axes, (double X, double Y, double Z) origin, bool fix)
    {
        ModelAccess.Place(context, assembly, component, axes, origin, fix);
        context.Api.Call(assembly, "IModelDoc2", "EditRebuild3");
    }

    /// <summary>两组轴之间转了多少度。</summary>
    private static double Angle(double[,] a, double[,] b)
    {
        var trace = 0.0;
        for (var i = 0; i < 3; i++)
            trace += a[i, 0] * b[i, 0] + a[i, 1] * b[i, 1] + a[i, 2] * b[i, 2];
        return Math.Acos(Math.Clamp((trace - 1) / 2, -1, 1)) * 180 / Math.PI;
    }

    /// <summary>删掉配合文件夹里不在 <paramref name="existing"/> 里的配合（AddMate5 回的对象选不中，按特征树找）。</summary>
    internal static void DeleteNew(SolidWorksApi api, object assembly, IReadOnlySet<string> existing)
    {
        foreach (var feature in Mates(api, assembly).Where(feature => !existing.Contains(api.CallString(feature, "IFeature", "get_Name"))))
            DeleteMate(api, assembly, feature);
    }

    private static void DeleteMate(SolidWorksApi api, object assembly, object mate)
    {
        api.ClearSelection(assembly);
        api.Call(mate, "IFeature", "Select2", false, 0);
        api.Call(api.Call(assembly, "IModelDoc2", "get_Extension")!, "IModelDocExtension", "DeleteSelection2", 0);
        api.ClearSelection(assembly);
    }

    /// <summary>总装「配合」文件夹下的全部配合特征。</summary>
    internal static List<object> Mates(SolidWorksApi api, object assembly)
    {
        var mates = new List<object>();
        for (var feature = api.Call(assembly, "IModelDoc2", "FirstFeature"); feature is not null; feature = api.Call(feature, "IFeature", "GetNextFeature"))
        {
            if (api.CallString(feature, "IFeature", "GetTypeName2") != "MateGroup")
                continue;
            for (var sub = api.Call(feature, "IFeature", "GetFirstSubFeature"); sub is not null; sub = api.Call(sub, "IFeature", "GetNextSubFeature"))
                mates.Add(sub);
        }

        return mates;
    }

    private static QuickOutcome List(QuickCommandContext context, string? filter)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        var rows = new List<Dictionary<string, string>>();
        var text = new StringBuilder();
        foreach (var feature in Mates(api, assembly))
        {
            if (api.Call(feature, "IFeature", "GetSpecificFeature2") is not { } mate)
                continue;
            int type;
            List<string> parts;
            try
            {
                type = api.CallInt(mate, "IMate2", "get_Type");
                parts = Enumerable.Range(0, api.CallInt(mate, "IMate2", "GetMateEntityCount"))
                    .Select(i => api.Call(mate, "IMate2", "MateEntity", i) is { } entity && api.Call(entity, "IMateEntity2", "get_ReferenceComponent") is { } component
                        ? api.CallString(component, "IComponent2", "get_Name2")
                        : "总装")
                    .ToList();
            }
            catch (InvalidCastException)
            {
                continue; // 配合文件夹里的别的东西（如智能扣件）
            }

            if (filter is not null && !parts.Any(part => part.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                continue;
            var warning = new object?[] { false };
            var code = api.CallInt(feature, "IFeature", "GetErrorCode2", warning);
            var row = new Dictionary<string, string>
            {
                ["name"] = api.CallString(feature, "IFeature", "get_Name"),
                ["type"] = MateSpec.TypeName(type),
                ["components"] = string.Join(" ↔ ", parts),
                ["suppressed"] = api.CallBool(feature, "IFeature", "IsSuppressed") ? "是" : "否",
                ["error"] = code == 0 ? string.Empty : $"{(Convert.ToBoolean(warning[0]) ? "警告" : "错误")} {code}",
            };
            rows.Add(row);
            text.AppendLine($"· {row["name"]}  {row["type"]}  {row["components"]}{(row["suppressed"] == "是" ? "  压缩" : string.Empty)}{(row["error"].Length > 0 ? "  " + row["error"] : string.Empty)}");
        }

        var head = $"配合列表：「{api.CallString(assembly, "IModelDoc2", "GetTitle")}」{(filter is null ? string.Empty : $"和「{filter}」有关的")} {rows.Count} 个配合。";
        return rows.Count == 0 ? QuickOutcome.Nothing(head) : QuickOutcome.Ok(head + Environment.NewLine + text.ToString().TrimEnd(), rows);
    }

    private static QuickOutcome Delete(QuickCommandContext context, IReadOnlyList<string> names)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        var deleted = new List<string>();
        foreach (var name in names)
        {
            var mate = Mates(api, assembly).FirstOrDefault(feature => string.Equals(api.CallString(feature, "IFeature", "get_Name"), name, StringComparison.OrdinalIgnoreCase));
            if (mate is null)
                continue;
            DeleteMate(api, assembly, mate);
            if (!Mates(api, assembly).Any(feature => string.Equals(api.CallString(feature, "IFeature", "get_Name"), name, StringComparison.OrdinalIgnoreCase)))
                deleted.Add(name);
        }

        api.Call(assembly, "IModelDoc2", "EditRebuild3");
        var text = $"删配合：删了 {(deleted.Count > 0 ? string.Join("、", deleted) : "0 个")}（零部件不挪、没固定，总装没存）。";
        return deleted.Count == names.Count ? QuickOutcome.Ok(text) : QuickOutcome.Fail(text + $" 没找到或没删掉：{string.Join("、", names.Except(deleted))}。");
    }
}
