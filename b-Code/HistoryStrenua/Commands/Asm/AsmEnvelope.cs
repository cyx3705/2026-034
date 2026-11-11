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
        CatalogSummary: "查看或创建零部件的装配包络",
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
        CatalogSummary: "调整装配包络的指定面",
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
        CatalogSummary: "显示或隐藏零部件装配包络线框",
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

    public static QuickCommand SnapCommand { get; } = new(
        Key: "asm-envsnap",
        CommandName: StrenuaIdentity.Domain + ".asm.envsnap",
        Title: "包络贴面",
        CatalogSummary: "按装配包络修正零部件的位置",
        Summary: "按包络改正按包围盒摆的零件：每一侧包围盒面到包络面差多少，就沿零件自己的轴反向挪多少，包络面落到原来包围盒面的位置；with=全部 同一零件文件的全部实例一起改。",
        Usage: "「零部件」填零部件列表的 name（顶层零件）。「一起」写 全部（同一零件文件的全部顶层实例）或几个实例名逗号分开，省略只改这一个。"
               + "「包络面」省略就用包络里改到真实面的那几侧（strenua.asm.envface 定过的装配面），也可写 -Y,-X。"
               + "默认「盒差」：假定零件是按包围盒摆好的，不看周围零件、姿态随零件轴；**只能做一次**，做完再做会再挪一遍（先 apply=否 预览）。"
               + "mode=找：不假定怎么摆的，每一面找别的零件上法向正对、投影重叠、前后「范围」内（默认 5 mm）最近的平面贴上，可重复做；「间隙」只给这种用。"
               + "单个指定目标面用 strenua.asm.move face= onto=。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("name", "零部件", "零部件名（零部件列表的 name）", true),
            new QuickParameter("with", "一起", "全部 / 实例名逗号分开；省略只贴这一个", false, 1),
            new QuickParameter("face", "包络面", "+X/-X/…逗号分开；省略 = 包络里跟着面的那几侧", false, 1),
            new QuickParameter("mode", "方式", "盒差（默认，按包围盒摆过的改正一次）/ 找（找旁边的面贴）", false, 1),
            new QuickParameter("near", "范围", "mode=找：找目标面的前后范围 mm，默认 5", false, 2),
            new QuickParameter("gap", "间隙", "mode=找：mm，正 = 离开，默认 0", false, 2),
            new QuickParameter("apply", "执行", "否 = 只预览", false, 2),
            new QuickParameter("fix", "固定", "是（默认）/ 否", false, 2),
        ],
        Example: StrenuaIdentity.Domain + ".asm.envsnap name=AHCA4010-4040-1 with=全部",
        Bind: arguments =>
        {
            var name = QuickArgs.Name(arguments, "name");
            var with = QuickArgs.Optional(arguments, "with") is { } withText ? QuickArgs.Names(withText, "with") : [];
            var faces = QuickArgs.Optional(arguments, "face") is { } faceText ? QuickArgs.Names(faceText, "face").Select(text => EnvelopeSpec.Side(text, "face")).ToList() : null;
            if (faces is not null && faces.GroupBy(EnvelopeSpec.AxisOf).Any(group => group.Count() > 1))
                throw new QuickArgumentException("face= 里同一根轴写了两面，一根轴只能贴一面。");
            var find = QuickArgs.Optional(arguments, "mode")?.Trim() switch
            {
                null or "盒差" or "box" => false,
                "找" or "find" => true,
                var text => throw new QuickArgumentException($"mode={text}：写 盒差 或 找。"),
            };
            if (!find && (QuickArgs.Optional(arguments, "near") is not null || QuickArgs.Optional(arguments, "gap") is not null))
                throw new QuickArgumentException("near= / gap= 只给 mode=找 用；盒差直接按包围盒到包络的差值挪。");
            var near = QuickArgs.Millimeters(QuickArgs.Optional(arguments, "near"), "near") ?? 5;
            if (near <= 0)
                throw new QuickArgumentException("near= 要大于 0。");
            var gap = QuickArgs.Millimeters(QuickArgs.Optional(arguments, "gap"), "gap") ?? 0;
            var apply = QuickArgs.Optional(arguments, "apply") is null || QuickArgs.Flag(arguments, "apply");
            var fix = QuickArgs.Optional(arguments, "fix") is null || QuickArgs.Flag(arguments, "fix");
            return context => SnapAll(context, name, with, faces, find, near, gap, apply, fix);
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
        RejectTilted(state, snaps.Select(snap => snap.Side), "asm.move 贴面");
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

    /// <summary>
    /// 批量贴面：同一零件文件的实例共用一份包络（存在零件里），每个实例各自找目标面——同一种零件装配关系类似，但坐在不同的梁上。
    /// 实例逐个摆，后摆的会把先摆好的当目标（同种零件互相贴也行）。
    /// </summary>
    private static QuickOutcome SnapAll(QuickCommandContext context, string name, IReadOnlyList<string> with, IReadOnlyList<int>? faces, bool find, double near, double gap, bool apply, bool fix)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        var first = ModelAccess.Component(api, assembly, name);
        var path = api.CallString(first, "IComponent2", "GetPathName");
        var state = PartEnvelope.Load(api, first, name);
        var sides = faces ?? state.Sides.Where(reading => reading.Side.Source == EnvelopeSource.Face).Select(reading => reading.Side.Side).ToList();
        if (sides.Count == 0)
            throw new QuickCommandException($"「{name}」的包络还没有改到真实装配面的侧：先用 strenua.asm.envface 定装配面，或写 face=。");
        if (sides.GroupBy(EnvelopeSpec.AxisOf).Any(group => group.Count() > 1))
            throw new QuickCommandException($"包络里同一根轴两面都跟着面（{string.Join(",", sides.Select(side => EnvelopeSpec.SideNames[side]))}），一根轴只能贴一面：写 face= 选。");

        var instances = new List<(string Name, object Component)>();
        if (with.Count == 1 && (with[0] == "全部" || with[0].Equals("all", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var component in api.CallArray(assembly, "IAssemblyDoc", "GetComponents", true))
            {
                if (string.Equals(api.CallString(component, "IComponent2", "GetPathName"), path, StringComparison.OrdinalIgnoreCase)
                    && !api.CallBool(component, "IComponent2", "IsSuppressed"))
                    instances.Add((api.CallString(component, "IComponent2", "get_Name2"), component));
            }
        }
        else
        {
            instances.Add((name, first));
            foreach (var other in with.Where(other => !string.Equals(other, name, StringComparison.OrdinalIgnoreCase)))
            {
                var component = ModelAccess.Component(api, assembly, other);
                if (!string.Equals(api.CallString(component, "IComponent2", "GetPathName"), path, StringComparison.OrdinalIgnoreCase))
                    throw new QuickArgumentException($"with= 里「{other}」不是同一个零件文件（{Path.GetFileName(path)}），包络不通用。");
                instances.Add((other, component));
            }
        }

        if (!find)
            return BoxDeltaAll(context, assembly, path, state, instances, sides, faces is null, apply, fix);
        RejectTilted(state, sides, "mode=找");

        var lines = new StringBuilder();
        int moved = 0, untouched = 0, partial = 0;
        foreach (var (instance, component) in instances)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            context.SetState($"贴 {instance}");
            var (origin, axes) = ModelAccess.Frame(api, component);
            var reach = EnvelopeSpec.ToAssembly(state.Box, axes, origin);
            var candidates = AssemblyGeometry.Leaves(api, assembly)
                .Where(leaf => !ReferenceEquals(leaf, component) && !string.Equals(api.CallString(leaf, "IComponent2", "get_Name2"), instance, StringComparison.OrdinalIgnoreCase))
                .Where(leaf => AssemblyGeometry.Box(api, leaf) is { } box && AssemblyGeometry.Touches([box.Min, box.Max], reach, near))
                .SelectMany(leaf => AssemblyGeometry.Faces(api, leaf, reach, near))
                .ToList();
            var current = origin;
            var done = new List<string>();
            var missing = new List<string>();
            var targets = new List<(int Side, ContactFace Face)>();
            foreach (var side in sides)
            {
                if (EnvelopeSpec.Nearest(axes, current, state.Box, side, candidates, near) is not { } found)
                {
                    missing.Add(EnvelopeSpec.SideNames[side]);
                    continue;
                }

                current = EnvelopeSpec.Snap(axes, current, state.Box, side, found.Face.Point, found.Face.Normal, gap).Origin;
                targets.Add((side, found.Face));
                var how = found.Distance >= 0 ? "差" : "压进";
                done.Add($"{EnvelopeSpec.SideNames[side]} 贴 {found.Face.Ref}（{how} {EnvelopeSpec.Num(Math.Abs(found.Distance))}）");
            }

            var conflict = targets.Any(target => Math.Abs(EnvelopeSpec.Snap(axes, current, state.Box, target.Side, target.Face.Point, target.Face.Normal, gap).Moved) > 0.01);
            var shift = new Vec3(current.X - origin.X, current.Y - origin.Y, current.Z - origin.Z).Length;
            lines.Append($"· {instance}：");
            if (targets.Count == 0)
            {
                untouched++;
                lines.AppendLine($"没找到可贴的面（{string.Join("、", missing)} 前后 {EnvelopeSpec.Num(near)} mm 内没有正对的平面），没动。");
                continue;
            }

            if (conflict)
            {
                untouched++;
                lines.AppendLine($"{string.Join("、", done)}，但几个目标面不互相垂直、贴了后面前面又离开，没动。");
                continue;
            }

            if (apply && shift > 1e-6)
                ModelAccess.Place(context, assembly, component, axes, current, fix);
            if (missing.Count > 0)
                partial++;
            else
                moved++;
            var skipped = missing.Count > 0 ? $"；{string.Join("、", missing)} 没找到目标没贴" : string.Empty;
            var verb = apply ? "挪了" : "要挪";
            var change = shift <= 1e-6 ? "本来就贴着" : $"{verb} {EnvelopeSpec.Num(shift)} mm";
            lines.AppendLine($"{string.Join("、", done)}{skipped}，{change}。");
        }

        if (apply)
            api.Call(assembly, "IModelDoc2", "EditRebuild3");
        var preview = apply ? string.Empty : "（预览，没挪）";
        var which = faces is null ? "（包络里的装配面）" : string.Empty;
        var tail = apply ? (fix ? "，已固定（总装没存）" : "，浮动（总装没存）") : string.Empty;
        var head = $"包络贴面{preview}：{Path.GetFileName(path)} {instances.Count} 个实例，贴的面 {string.Join(",", sides.Select(side => EnvelopeSpec.SideNames[side]))}{which}；全贴上 {moved}、部分 {partial}、没动 {untouched}{tail}。";
        var text = head + Environment.NewLine + lines.ToString().TrimEnd();
        return untouched == instances.Count ? QuickOutcome.Fail(text) : QuickOutcome.Ok(text);
    }

    /// <summary>盒差改正：每个实例按自己的零件轴把「包围盒 → 包络」的差值反向挪过去，不看周围零件。</summary>
    private static QuickOutcome BoxDeltaAll(QuickCommandContext context, object assembly, string path, EnvelopeState state, IReadOnlyList<(string Name, object Component)> instances, IReadOnlyList<int> sides, bool fromEnvelope, bool apply, bool fix)
    {
        var api = context.Api;
        var each = new StringBuilder();
        var center = (state.PartBox.Min + state.PartBox.Max) * 0.5;
        var planes = new List<(int Side, Vec3 Normal, Vec3 Point)>();
        foreach (var side in sides)
        {
            var k = EnvelopeSpec.AxisOf(side);
            var b = EnvelopeSpec.Component(side % 2 == 0 ? state.PartBox.Min : state.PartBox.Max, k);
            var reading = state.Sides[side];
            var e = reading.Side.Source == EnvelopeSource.Box ? b : reading.Value;
            var point = k switch { 0 => center with { X = e }, 1 => center with { Y = e }, _ => center with { Z = e } };
            planes.Add((side, reading.Side.Tilt ?? EnvelopeSpec.Unit(side), point));
            var tilt = reading.Side.Tilt is { } n ? $"（斜 {EnvelopeSpec.Num(EnvelopeSpec.TiltDegrees(n, side))}°）" : string.Empty;
            each.Append($"{(each.Length > 0 ? "、" : string.Empty)}{EnvelopeSpec.SideNames[side]} 包围盒 {EnvelopeSpec.Num(b)} → 包络 {EnvelopeSpec.Num(e)}{tilt}");
        }

        var lines = new StringBuilder();
        var moved = 0;
        foreach (var (instance, component) in instances)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var (origin, axes) = ModelAccess.Frame(api, component);
            var (turned, target, degrees) = EnvelopeSpec.Correction(axes, origin, state.PartBox, planes);
            var delta = new Vec3(target.X - origin.X, target.Y - origin.Y, target.Z - origin.Z);
            if (delta.Length <= 1e-6 && degrees <= 1e-6)
            {
                lines.AppendLine($"· {instance}：包络和包围盒在这几侧一样，不用挪。");
                continue;
            }

            if (apply)
                ModelAccess.Place(context, assembly, component, turned, target, fix);
            moved++;
            var rotate = degrees > 1e-6 ? $"，转 {EnvelopeSpec.Num(degrees)}°" : string.Empty;
            lines.AppendLine($"· {instance}：原点 {ModelAccess.Format(origin)} → {ModelAccess.Format(target)}（{(apply ? "挪了" : "要挪")} {EnvelopeSpec.Num(delta.X)},{EnvelopeSpec.Num(delta.Y)},{EnvelopeSpec.Num(delta.Z)}{rotate}）。");
        }

        if (apply)
            api.Call(assembly, "IModelDoc2", "EditRebuild3");
        var head = $"包络贴面（盒差{(apply ? string.Empty : "，预览没挪")}）：{Path.GetFileName(path)} {instances.Count} 个实例，{each}{(fromEnvelope ? "（包络里的装配面）" : string.Empty)}；"
                   + $"{(apply ? "挪了" : "要挪")} {moved} 个{(apply ? (fix ? "，已固定（总装没存）" : "，浮动（总装没存）") : string.Empty)}。"
                   + "假定它们是按包围盒摆的，只做一次：再做会再挪一遍。";
        return QuickOutcome.Ok(head + Environment.NewLine + lines.ToString().TrimEnd());
    }

    /// <summary>斜的装配面只有盒差会转零件；贴面、找面只会平移，遇到就拒绝。</summary>
    private static void RejectTilted(EnvelopeState state, IEnumerable<int> sides, string what)
    {
        var tilted = sides.Where(side => state.Sides[side].Side.Tilt is not null).Select(side => EnvelopeSpec.SideNames[side]).ToList();
        if (tilted.Count > 0)
            throw new QuickCommandException($"包络 {string.Join("、", tilted)} 是斜面，{what}只会平移、贴不正：用 strenua.asm.envsnap 默认的盒差（会把零件转回去）。");
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
        var partBox = PartEnvelope.PartBox(api, PartEnvelope.Model(api, component, name));
        var (value, useAxis) = EnvelopeSpec.Locate(geometry, chosen, axis, (partBox.Min + partBox.Max) * 0.5);
        var tilt = EnvelopeSpec.Tilt(geometry, chosen);
        if (geometry.Plane && axis is not null)
            throw new QuickArgumentException($"F{face} 是平面，at= 只给圆柱面用。");
        var final = EnvelopeSpec.Outward(chosen, value, offset);
        var kind = geometry.Plane ? (tilt is { } n ? $"斜面 {EnvelopeSpec.Num(EnvelopeSpec.TiltDegrees(n, chosen))}°，盒差时零件会转回去" : "平面") : geometry.Hole ? (useAxis ? "孔轴线" : "孔切线") : (useAxis ? "轴轴线" : "轴外侧切线");
        return Apply(context, component, name, new EnvelopeSide(chosen, EnvelopeSource.Face, face, offset, useAxis, final, tilt), $"{EnvelopeSpec.SideNames[chosen]} 改到 F{face}（{kind}）");
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
