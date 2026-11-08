using System.Text;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 装配类「装配包络」（1.42.0，用户定：零件包围盒常和装配关系不大——先建一个包围盒，再把几个装配面改成真实的装配面，别的方向不管）。
/// </summary>
/// <remarks>
/// 盒在零件坐标、轴对齐，数据存零件属性（<see cref="EnvelopeSpec"/>），跟零件走；改面用 <c>asm.contact</c> 回执里的 F 序号。
/// 列表、摆位回执里有包络就带上包络（总装坐标外接盒），干涉、接触仍按真实几何。
/// 只回文字不带 Data（真机：经 MCP 有 Data 时 AI 只拿到 Data，存盘情况、外接盒、面变了的提示全丢）。
/// 写属性前零件没有未保存改动就顺手存零件（只多了这一条属性，我定：不存就白建了）；有别的未保存改动就不存，回执说明。
/// </remarks>
internal static class AsmEnvelope
{
    public static QuickCommand EnvelopeCommand { get; } = new(
        Key: "asm-envelope",
        CommandName: StrenuaIdentity.Domain + ".asm.envelope",
        Title: "装配包络",
        Summary: "看或建一个零件的装配包络：零件坐标的盒，六个面各自跟包围盒 / 跟某个面 / 手填；没建过就按包围盒建。回执给零件坐标六面值和总装坐标外接盒。",
        Usage: "「零部件」填零部件列表的 name（只做零件，子装配里的写「子装配-1/零件-1」）。已有包络时只读回（面变了、失效会标出来）；reset=是 六面全改回包围盒。"
               + "改某一面用 strenua.asm.envface。数据存零件属性「Strenua包络」。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("name", "零部件", "零部件名（零部件列表的 name）", true),
            new QuickParameter("reset", "重建", "是 = 六面全改回包围盒；省略只读回 / 没有就建", false, 1),
        ],
        Example: StrenuaIdentity.Domain + ".asm.envelope name=立柱-1",
        Bind: arguments =>
        {
            var name = QuickArgs.Name(arguments, "name");
            var reset = QuickArgs.Flag(arguments, "reset");
            return context => Envelope(context, name, reset);
        },
        SelfReport: true);

    public static QuickCommand FaceCommand { get; } = new(
        Key: "asm-envface",
        CommandName: StrenuaIdentity.Domain + ".asm.envface",
        Title: "包络改面",
        Summary: "把装配包络的一面改成零件上真实的装配面（F 序号）、改回包围盒或手填；只动这一面，别的面不变。",
        Usage: "「零部件」同装配包络。「改成」写 F12（同 asm.contact 回执的面序号）/ 盒 / 数（零件坐标 mm）。"
               + "「哪面」+X/-X/+Y/-Y/+Z/-Z（零件坐标）：平面可省，按面的法向定；圆柱面、盒、数必须写。"
               + "平面法向要平行零件某根轴；圆柱轴线要垂直这一面，孔默认取轴线、轴默认取外侧切线，「取」写 轴 / 切 改。「偏移」往外为正（mm）。没建过包络会先按包围盒建。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("name", "零部件", "零部件名（零部件列表的 name）", true),
            new QuickParameter("to", "改成", "F序号 / 盒 / 数（零件 mm）", true),
            new QuickParameter("side", "哪面", "+X/-X/+Y/-Y/+Z/-Z；平面可省", false, 1),
            new QuickParameter("offset", "偏移", "往外为正 mm，默认 0", false, 1),
            new QuickParameter("at", "取", "圆柱面：轴（轴线）/ 切（切线）", false, 1),
        ],
        Example: StrenuaIdentity.Domain + ".asm.envface name=立柱-1 to=F12",
        Bind: arguments =>
        {
            var name = QuickArgs.Name(arguments, "name");
            var side = QuickArgs.Optional(arguments, "side") is { } sideText ? EnvelopeSpec.Side(sideText) : (int?)null;
            var offset = QuickArgs.Millimeters(QuickArgs.Optional(arguments, "offset"), "offset") ?? 0;
            bool? axis = QuickArgs.Optional(arguments, "at")?.Trim() switch
            {
                null => null,
                "轴" or "轴线" or "axis" => true,
                "切" or "切线" or "tangent" => false,
                var text => throw new QuickArgumentException($"at={text}：写 轴 或 切。"),
            };
            var to = QuickArgs.Name(arguments, "to").Trim();
            if (to.StartsWith('F') || to.StartsWith('f'))
            {
                var face = int.TryParse(to[1..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
                    ? index
                    : throw new QuickArgumentException($"to={to}：面写 F 加序号，如 F12。");
                return context => SetFace(context, name, side, face, offset, axis);
            }

            if (side is not { } given)
                throw new QuickArgumentException($"to={to} 不是面，要写 side= 说改哪一面。");
            if (axis is not null)
                throw new QuickArgumentException("at= 只给圆柱面用。");
            if (to == "盒" || to.Equals("box", StringComparison.OrdinalIgnoreCase))
            {
                if (offset != 0)
                    throw new QuickArgumentException("改回包围盒不带偏移；要偏就手填数。");
                return context => SetOther(context, name, new EnvelopeSide(given, EnvelopeSource.Box, -1, 0, false, 0));
            }

            var value = QuickArgs.Millimeters(to, "to") ?? throw new QuickArgumentException("to= 不能空。");
            if (offset != 0)
                throw new QuickArgumentException("手填数不带偏移，直接写算好的数。");
            return context => SetOther(context, name, new EnvelopeSide(given, EnvelopeSource.Manual, -1, 0, false, value));
        },
        SelfReport: true);

    public static QuickCommand ShowCommand { get; } = new(
        Key: "asm-envshow",
        CommandName: StrenuaIdentity.Domain + ".asm.envshow",
        Title: "显示包络",
        Summary: "在总装里画出一个零件装配包络的线框（3D 草图「包络-零部件名」），只给人看；hide=是 删掉线框。",
        Usage: "「零部件」同装配包络。重画会先删旧线框。线框不跟零件动，摆位、改面后要再画一次。总装没存。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("name", "零部件", "零部件名（零部件列表的 name）", true),
            new QuickParameter("hide", "删掉", "是 = 删掉线框", false, 1),
        ],
        Example: StrenuaIdentity.Domain + ".asm.envshow name=立柱-1",
        Bind: arguments =>
        {
            var name = QuickArgs.Name(arguments, "name");
            var hide = QuickArgs.Flag(arguments, "hide");
            return context => Show(context, name, hide);
        },
        SelfReport: true);

    /// <summary>列表 / 摆位回执用：有包络就给总装外接盒文字，没有为 null（读坏了也给 null，不拖累列表）。</summary>
    public static string? AssemblyBoxText(SolidWorksApi api, object component, string name)
    {
        if (!PartEnvelope.Has(api, component))
            return null;
        try
        {
            return EnvelopeSpec.BoxText(PartEnvelope.Load(api, component, name).InAssembly);
        }
        catch (QuickCommandException)
        {
            return null;
        }
    }

    /// <summary>
    /// 摆位贴面（asm.move face= onto=）：按零件 <paramref name="axes"/> 与原点 <paramref name="origin"/>，逐对把包络面沿目标法向贴上，返回新原点与回执片段。
    /// 一根轴只贴一面（参数层已拦）；几对目标面互相不垂直时后一对会挪动前一对，算完逐对复核，差 0.01 mm 以上就拒绝、不摆。
    /// </summary>
    public static ((double X, double Y, double Z) Origin, string Text)? Snap(QuickCommandContext context, object assembly, object component, string name, double[,] axes, (double X, double Y, double Z) origin, IReadOnlyList<EnvelopeSnap> snaps)
    {
        var api = context.Api;
        var state = PartEnvelope.Load(api, component, name);
        var targets = new List<(EnvelopeSnap Snap, ContactFace Face)>();
        foreach (var snap in snaps)
        {
            var other = AssemblyGeometry.Component(api, assembly, snap.Onto.Component ?? string.Empty);
            if (ReferenceEquals(other, component) || string.Equals(api.CallString(other, "IComponent2", "get_Name2"), name, StringComparison.OrdinalIgnoreCase))
                throw new QuickArgumentException($"onto={snap.Onto} 是它自己，要贴到别的零部件上。");
            AssemblyGeometry.Face(api, other, snap.Onto.Index); // 序号越界在这里报
            var face = AssemblyGeometry.Faces(api, other).FirstOrDefault(f => f.Index == snap.Onto.Index && f.Surface == ContactSurface.Plane)
                       ?? throw new QuickCommandException($"{snap.Onto} 不是平面，贴面只认平面。");
            targets.Add((snap, face));
        }

        var current = origin;
        foreach (var (snap, face) in targets)
            current = EnvelopeSpec.Snap(axes, current, state.Box, snap.Side, face.Point, face.Normal, snap.Gap).Origin;
        var parts = new List<string>();
        foreach (var (snap, face) in targets)
        {
            var residual = EnvelopeSpec.Snap(axes, current, state.Box, snap.Side, face.Point, face.Normal, snap.Gap).Moved;
            if (Math.Abs(residual) > 0.01)
                throw new QuickCommandException($"几个目标面不互相垂直，贴了后面的 {snap.Onto} 前面的又离开了（差 {EnvelopeSpec.Num(residual)} mm），没摆。分开贴或换目标面。");
            var source = state.Sides[snap.Side];
            parts.Add($"包络 {EnvelopeSpec.SideNames[snap.Side]}（{EnvelopeSpec.Describe(source.Side)}）贴 {snap.Onto}{(snap.Gap != 0 ? $" 隔 {EnvelopeSpec.Num(snap.Gap)}" : string.Empty)}");
        }

        var moved = new Vec3(current.X - origin.X, current.Y - origin.Y, current.Z - origin.Z).Length;
        return (current, $"{string.Join("、", parts)}（挪了 {EnvelopeSpec.Num(moved)} mm{(state.Stored ? string.Empty : "；没建包络，按包围盒")}）");
    }

    private static QuickOutcome Envelope(QuickCommandContext context, string name, bool reset)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        var component = AssemblyGeometry.Component(api, assembly, name);
        var state = PartEnvelope.Load(api, component, name);
        string? saved = null;
        if (reset || !state.Stored)
        {
            saved = PartEnvelope.Write(api, state.Model, EnvelopeSpec.Default());
            state = PartEnvelope.Load(api, component, name);
        }

        var head = reset ? "重建为包围盒" : saved is not null ? "新建（六面跟包围盒）" : "读回";
        return QuickOutcome.Ok(Report($"装配包络：「{name}」{head}", state, saved));
    }

    private static QuickOutcome SetFace(QuickCommandContext context, string name, int? side, int face, double offset, bool? axis)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        var component = AssemblyGeometry.Component(api, assembly, name);
        PartEnvelope.Model(api, component, name);
        var geometry = PartEnvelope.Face(api, component, face);
        var chosen = EnvelopeSpec.Check(geometry, side, face);
        var (value, useAxis) = EnvelopeSpec.Locate(geometry, chosen, axis);
        if (geometry.Plane && axis is not null)
            throw new QuickArgumentException($"F{face} 是平面，at= 只给圆柱面用。");
        var final = EnvelopeSpec.Outward(chosen, value, offset);
        var kind = geometry.Plane ? "平面" : geometry.Hole ? (useAxis ? "孔轴线" : "孔切线") : (useAxis ? "轴轴线" : "轴外侧切线");
        return Apply(context, component, name, new EnvelopeSide(chosen, EnvelopeSource.Face, face, offset, useAxis, final), $"{EnvelopeSpec.SideNames[chosen]} 改到 F{face}（{kind}）");
    }

    private static QuickOutcome SetOther(QuickCommandContext context, string name, EnvelopeSide side)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        var component = AssemblyGeometry.Component(api, assembly, name);
        var what = side.Source == EnvelopeSource.Box ? "改回包围盒" : $"手填 {EnvelopeSpec.Num(side.Value)}";
        return Apply(context, component, name, side, $"{EnvelopeSpec.SideNames[side.Side]} {what}");
    }

    private static QuickOutcome Apply(QuickCommandContext context, object component, string name, EnvelopeSide side, string what)
    {
        var api = context.Api;
        var before = PartEnvelope.Load(api, component, name);
        // 跟面的侧顺手把上次值刷成现读值（失效的现读值就是上次值，不变）。
        var sides = before.Sides.Select(reading => reading.Side.Source == EnvelopeSource.Face ? reading.Side with { Value = reading.Value } : reading.Side).ToArray();
        sides[side.Side] = side;
        var values = before.Sides.Where(reading => reading.Side.Source != EnvelopeSource.Box).ToDictionary(reading => reading.Side.Side, reading => reading.Value);
        values.Remove(side.Side);
        if (side.Source == EnvelopeSource.Box)
            values.Remove(side.Side);
        else
            values[side.Side] = side.Value;
        var (min, max, crossed) = EnvelopeSpec.Compose(before.PartBox, values);
        if (crossed is not null)
        {
            return QuickOutcome.Fail($"包络改面：「{name}」{what} 后 {crossed} 向两面交叉（−{crossed} 不小于 +{crossed}：{EnvelopeSpec.BoxText((min, max))}），没改。"
                                     + "多半是选错了面或侧，看一下 F 序号与 side=。");
        }

        var saved = PartEnvelope.Write(api, before.Model, sides);
        var after = PartEnvelope.Load(api, component, name);
        var oldValue = before.Sides[side.Side].Value;
        return QuickOutcome.Ok(Report($"包络改面：「{name}」{what}，{EnvelopeSpec.Num(oldValue)} → {EnvelopeSpec.Num(after.Sides[side.Side].Value)}", after, saved));
    }

    private static QuickOutcome Show(QuickCommandContext context, string name, bool hide)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        var feature = "包络-" + name.Replace('/', '_');
        var removed = DeleteFeature(api, assembly, feature);
        if (hide)
        {
            return removed
                ? QuickOutcome.Ok($"显示包络：删掉了「{feature}」（总装没存）。")
                : QuickOutcome.Nothing($"显示包络：总装里没有「{feature}」。");
        }

        var component = AssemblyGeometry.Component(api, assembly, name);
        var state = PartEnvelope.Load(api, component, name);
        var (origin, axes) = ModelAccess.Frame(api, component);
        var rows = new[] { new Vec3(axes[0, 0], axes[0, 1], axes[0, 2]), new Vec3(axes[1, 0], axes[1, 1], axes[1, 2]), new Vec3(axes[2, 0], axes[2, 1], axes[2, 2]) };
        var o = new Vec3(origin.X, origin.Y, origin.Z);
        Vec3 ToAssembly(Vec3 p) => (rows[0] * p.X + rows[1] * p.Y + rows[2] * p.Z + o) * 0.001;

        api.ClearSelection(assembly);
        var sketches = api.Call(assembly, "IModelDoc2", "get_SketchManager") ?? throw new QuickCommandException("SolidWorks 没有返回草图管理器。");
        api.Call(sketches, "ISketchManager", "Insert3DSketch", true);
        api.Call(sketches, "ISketchManager", "set_AddToDB", true);
        var drawn = 0;
        try
        {
            foreach (var (a, b) in EnvelopeSpec.Edges(state.Box))
            {
                var p = ToAssembly(a);
                var q = ToAssembly(b);
                if (api.Call(sketches, "ISketchManager", "CreateLine", p.X, p.Y, p.Z, q.X, q.Y, q.Z) is not null)
                    drawn++;
            }
        }
        finally
        {
            api.Call(sketches, "ISketchManager", "set_AddToDB", false);
            api.Call(sketches, "ISketchManager", "Insert3DSketch", true);
        }

        if (api.Call(assembly, "IModelDoc2", "FeatureByPositionReverse", 0) is { } sketch && api.CallString(sketch, "IFeature", "GetTypeName2") == "3DProfileFeature")
            api.Call(sketch, "IFeature", "set_Name", feature);
        api.ClearSelection(assembly);
        var text = $"显示包络：「{name}」画了 {drawn}/12 条棱（3D 草图「{feature}」{(removed ? "，旧的已删" : string.Empty)}，总装没存），总装外接盒 [{EnvelopeSpec.BoxText(state.InAssembly)}]。";
        return drawn == 12 ? QuickOutcome.Ok(text) : QuickOutcome.Fail(text + " 有的棱 SolidWorks 没画出来。");
    }

    private static bool DeleteFeature(SolidWorksApi api, object assembly, string name)
    {
        if (api.Call(assembly, "IAssemblyDoc", "FeatureByName", name) is not { } feature)
            return false;
        api.ClearSelection(assembly);
        api.Call(feature, "IFeature", "Select2", false, 0);
        var extension = api.Call(assembly, "IModelDoc2", "get_Extension")!;
        var deleted = api.CallBool(extension, "IModelDocExtension", "DeleteSelection2", 0);
        api.ClearSelection(assembly);
        return deleted;
    }

    private static string Report(string head, EnvelopeState state, string? saved)
    {
        var text = new StringBuilder(head);
        if (saved is not null)
            text.Append($"（{saved}）");
        text.AppendLine("。零件坐标 mm：");
        foreach (var reading in state.Sides)
        {
            text.Append($"· {EnvelopeSpec.SideNames[reading.Side.Side]} {EnvelopeSpec.Num(reading.Value)}  {EnvelopeSpec.Describe(reading.Side)}");
            if (reading.Side.Source != EnvelopeSource.Box)
            {
                var boxValue = EnvelopeSpec.Component(reading.Side.Side % 2 == 0 ? state.PartBox.Min : state.PartBox.Max, EnvelopeSpec.AxisOf(reading.Side.Side));
                text.Append($"（包围盒 {EnvelopeSpec.Num(boxValue)}）");
            }

            if (reading.Note is not null)
                text.Append($"  ⚠ {reading.Note}");
            text.AppendLine();
        }

        text.Append($"总装坐标外接盒 [{EnvelopeSpec.BoxText(state.InAssembly)}]");
        if (state.Crossed is not null)
            text.Append($"；⚠ {state.Crossed} 向两面交叉，改一下");
        return text.Append('。').ToString();
    }
}
