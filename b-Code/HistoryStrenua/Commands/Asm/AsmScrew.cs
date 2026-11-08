using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace HistoryStrenua;

/// <summary>
/// 装配类「一键螺丝」<c>strenua.asm.screw</c>（1.44.0，用户定）：按总装里已有的螺纹孔（异形孔向导）自动配蚂蚁工场螺钉并装上。
/// </summary>
/// <remarks>
/// <para>粒度粗（用户要减少负担）：①一种类型装全部 ②按规格分类型（sizes=M3=盘头）③按沉头形状自动选类型（auto，默认开）④长度算出来、不暴露 ⑤范围 = 全部 / 子装配 / 零件。</para>
/// <para>螺纹孔沿孔口往外找一叠过孔（每个零件的过孔半径 d/2–0.75d、上下贴着），最外那个零件定头落在哪：柱形沉孔贴沉孔底，锥形沉头孔沉头螺钉锥面对锥面，否则贴表面。
/// 长度挑旋入深度最接近 1.5d（不少于 min(d, 螺纹深)、不超过螺纹深）的那档。螺钉按零件原点摆、固定、不加配合（同 asm.insert），插在当前总装顶层；已经有杆插着的孔跳过，所以能重复跑。</para>
/// </remarks>
internal static class AsmScrew
{
    public static QuickCommand Command { get; } = new(
        Key: "asm-screw",
        CommandName: StrenuaIdentity.Domain + ".asm.screw",
        Title: "一键螺丝",
        Summary: "按活动总装里的螺纹孔（异形孔向导）自动配蚂蚁工场螺钉装上：认出过孔那叠零件，沉孔贴沉孔底、锥形沉头孔用沉头螺钉、其余贴表面，长度自动算；可只装子装配或某个零件。",
        Usage: "「类型」全部规格默认用哪种（" + "内六角圆柱头 / 弹垫组合 / 平弹垫组合 / 内六角沉头 / 内六角半圆头 / 十字盘头 / 十字沉头，默认内六角圆柱头）；"
               + "「按规格」逐个规格改类型，如 M3=十字盘头,M8=弹垫组合；「按沉头」默认是：锥形沉头孔自动用沉头螺钉、柱形沉孔用圆柱头一类、平面孔口用选的，写否就照选的装（装不上的跳过）。"
               + "「范围」省略 = 整个总装，写子装配或零件名（零部件列表的 name）只装涉及它的螺纹孔。长度不用给：旋入深度取最接近 1.5d、不超过螺纹深的标准长度。"
               + "螺钉文件存进总装旁「外购件」，插在总装顶层、固定、不加配合，总装不存；已插着螺钉的孔跳过（可重复跑）。先 apply=否 看清单（不下载）。只认异形孔向导的螺纹孔。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("type", "类型", "全部规格默认的螺钉类型，默认内六角圆柱头", false, 1),
            new QuickParameter("sizes", "按规格", "规格=类型，逗号分开，如 M3=十字盘头,M8=弹垫组合", false, 1),
            new QuickParameter("auto", "按沉头", "是（默认）= 按孔口形状自动换沉头 / 圆柱头；否 = 照选的装", false, 1),
            new QuickParameter("scope", "范围", "省略 = 全部；子装配或零件名", false, 2),
            new QuickParameter("grade", "性能", "蚂蚁工场性能代号，默认 S11", false, 2),
            new QuickParameter("apply", "执行", "否 = 只列清单、不下载不插入", false, 2),
        ],
        Example: StrenuaIdentity.Domain + ".asm.screw type=内六角圆柱头 sizes=M4=十字盘头",
        Bind: arguments =>
        {
            var type = QuickArgs.Optional(arguments, "type") is { } typeText ? ScrewKind.Parse(typeText) : ScrewKind.Cap;
            var sizes = ScrewSpec.Sizes(QuickArgs.Optional(arguments, "sizes"));
            var auto = QuickArgs.Optional(arguments, "auto") is null || QuickArgs.Flag(arguments, "auto");
            var scope = QuickArgs.Optional(arguments, "scope");
            var grade = QuickArgs.Optional(arguments, "grade") ?? "S11";
            var apply = QuickArgs.Optional(arguments, "apply") is null || QuickArgs.Flag(arguments, "apply");
            return context => Run(context, new Options(type, sizes, auto, scope, grade, apply));
        },
        SelfReport: true);

    private sealed record Options(ScrewKind Type, IReadOnlyDictionary<double, ScrewKind> Sizes, bool Auto, string? Scope, string Grade, bool Apply);

    /// <summary>一个要装的螺钉。</summary>
    private sealed record Plan(TappedHole Hole, ScrewStack Stack, ScrewKind Kind, double Length, double Engage, string Number, string? Path, (double[,] Axes, (double X, double Y, double Z) Origin) Pose, string? Note);

    /// <summary>回执最多列多少行明细。</summary>
    private const int Shown = 40;

    private static QuickOutcome Run(QuickCommandContext context, Options options)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        var assemblyPath = api.CallString(assembly, "IModelDoc2", "GetPathName");
        if (options.Apply && assemblyPath.Length == 0)
            throw new QuickCommandException("总装还没存过，定不了「外购件」放哪：先存总装。");
        var title = api.CallString(assembly, "IModelDoc2", "GetTitle");
        var leaves = AssemblyGeometry.Leaves(api, assembly);
        var names = leaves.ToDictionary(leaf => api.CallString(leaf, "IComponent2", "get_Name2"), leaf => leaf, StringComparer.OrdinalIgnoreCase);
        HashSet<string>? scope = null;
        if (options.Scope is { } scopeName)
        {
            var target = AssemblyGeometry.Component(api, assembly, scopeName);
            scope = AssemblyGeometry.LeavesOf(api, target).Select(leaf => api.CallString(leaf, "IComponent2", "get_Name2")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        context.SetState("找螺纹孔");
        var boxes = new Dictionary<string, (Vec3 Min, Vec3 Max)>(StringComparer.OrdinalIgnoreCase);
        var purchased = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 外购子装配（模组这类）：内部的孔厂家有意不带螺丝，不装也不报。
        var purchasedAssemblies = api.CallArray(assembly, "IAssemblyDoc", "GetComponents", false)
            .Where(component => api.CallArray(component, "IComponent2", "GetChildren").Length > 0 && IsPurchased(api, component))
            .Select(component => api.CallString(component, "IComponent2", "get_Name2"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cache = new Dictionary<string, IReadOnlyList<RoundFace>>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<RoundFace> Round(string name) => cache.TryGetValue(name, out var cached) ? cached : cache[name] = ScrewScan.RoundFaces(api, names[name]);
        var holes = new List<(TappedHole High, TappedHole Low, double Length)>();
        foreach (var (name, leaf) in names)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            if (AssemblyGeometry.Box(api, leaf) is { } box)
                boxes[name] = box;
            if (IsPurchased(api, leaf))
                purchased.Add(name);
            var wizard = ScrewScan.TappedHoles(api, leaf);
            holes.AddRange(wizard);
            // 没打异形孔螺纹的：孔径等于攻丝底孔就按底孔认（外购件型材端面孔这类），排掉已有异形孔螺纹的轴线。
            holes.AddRange(ScrewSpec.DrilledHoles(Round(name), (point, axis) => wizard.Any(item =>
                Math.Abs(item.High.Outward.Dot(axis)) > 0.9999 && ScrewSpec.Offset(point, item.High) <= ScrewSpec.Coaxial)));
        }

        var skipped = new List<string>();
        var occupied = 0;
        var found = new List<(TappedHole Hole, ScrewStack Stack)>();
        context.SetState("认过孔");
        foreach (var (high, low, length) in holes)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var d = high.Diameter;
            var faces = new List<AxialFace>();
            foreach (var (name, box) in boxes)
            {
                if (string.Equals(name, high.Owner, StringComparison.OrdinalIgnoreCase) || !ScrewScan.Along(box, high, length + 300, 300, d))
                    continue;
                faces.AddRange(Round(name).Select(face => ScrewSpec.Axial(face, high)).OfType<AxialFace>());
            }

            // 另一头朝外：孔轴坐标翻过来（low 的孔口在 high 往里 length 处）。
            var flipped = ScrewSpec.Flip(faces, length);
            var options2 = new[] { (Hole: high, Faces: (IReadOnlyList<AxialFace>)faces), (Hole: low, Faces: flipped) }
                .Select(item => (item.Hole, Result: ScrewSpec.Stack(item.Faces, d)))
                .ToList();
            var hit = options2.FirstOrDefault(item => item.Result.Stack is not null);
            if (hit.Result.Stack is not { } stack)
            {
                // 按底孔认的没对上过孔多半本来就不是螺纹孔（型材通孔中段、模组上空着的安装孔），不报。
                if (ScrewSpec.Internal(high.Owner, [], purchasedAssemblies))
                    continue;
                if (!high.Drilled && Occupied(faces, d, length))
                    occupied++;
                else if (!high.Drilled && (scope is null || scope.Contains(high.Owner)))
                    skipped.Add($"{high.Owner} {high.Size}（{high.Feature}）@{Where(high)}：{options2[0].Result.Reason}");
                continue;
            }

            if (scope is not null && !scope.Contains(hit.Hole.Owner) && !stack.Owners.Any(scope.Contains))
                continue;
            if (ScrewSpec.Internal(hit.Hole.Owner, stack.Owners, purchasedAssemblies))
                continue;
            if (Occupied(faces, d, length))
            {
                occupied++;
                continue;
            }

            if (hit.Hole.Drilled && !purchased.Contains(hit.Hole.Owner))
            {
                skipped.Add($"{hit.Hole.Owner} {hit.Hole.Size}@{Where(hit.Hole)}：{hit.Hole.Feature} 对着 {string.Join(" / ", stack.Owners)} 的过孔，但不是异形孔螺纹孔（加工件要先 strenua.model.hole type=螺纹 打螺纹）");
                continue;
            }

            found.Add((hit.Hole, stack));
        }

        if (found.Count == 0)
        {
            var empty = new StringBuilder($"一键螺丝：「{title}」{(options.Scope is null ? string.Empty : $"范围「{options.Scope}」")}里没有要装的螺纹孔");
            empty.Append(holes.Count == 0 ? "（没有螺纹孔：异形孔向导螺纹孔、外购件攻丝底孔都没有）。" : $"（已装 {occupied} 个）。");
            foreach (var line in skipped.Take(Shown))
                empty.AppendLine().Append("· 跳过 ").Append(line);
            return QuickOutcome.Nothing(empty.ToString());
        }

        var planner = new Planner(context, options, assemblyPath);
        var plans = new List<Plan>();
        foreach (var (hole, stack) in found)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            context.SetState($"选螺钉 {plans.Count + 1}/{found.Count}");
            var (plan, reason) = planner.Plan(hole, stack);
            if (plan is null)
                skipped.Add($"{hole.Owner} {hole.Size} @{Where(hole)}：{reason}");
            else
                plans.Add(plan);
        }

        var inserted = new List<string>();
        if (options.Apply && plans.Count > 0)
        {
            context.SetState("插入螺钉");
            inserted.AddRange(Insert(context, assembly, title, plans));
            api.Call(assembly, "IModelDoc2", "EditRebuild3");
        }

        // 只回文字：经 MCP 有 Data 时宿主只回 Data，跳过与换类型的说明会丢（同 1.42.0 包络）。
        return QuickOutcome.Ok(Report(title, options, plans, skipped, occupied, inserted));
    }

    /// <summary>外购件：文件在某个「外购件」文件夹下（蚂蚁工场插入的都在那）。</summary>
    private static bool IsPurchased(HistoryStrenua.SolidWorks.SolidWorksApi api, object component)
        => api.CallString(component, "IComponent2", "GetPathName").Contains(Path.DirectorySeparatorChar + MymoooRules.Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>孔里已经有杆（凸圆柱、半径约 d/2）插着：算装过了。</summary>
    private static bool Occupied(IReadOnlyList<AxialFace> faces, double d, double length)
        => faces.Any(face => !face.Hole && !face.Cone && face.MinRadius >= 0.4 * d && face.MinRadius <= 0.55 * d && face.From < 0 && face.To > -length);

    /// <summary>按长度表、下载件现量挑螺钉，同一种同一规格只查一次表、只量一次。</summary>
    private sealed class Planner(QuickCommandContext context, Options options, string assemblyPath)
    {
        private readonly Dictionary<(ScrewKind, double), IReadOnlyList<double>> lengths = new();
        private readonly Dictionary<(ScrewKind, double), ScrewProfile> profiles = new();
        private readonly Dictionary<string, (string Number, long Product)> numbers = new();

        public (Plan? Plan, string? Reason) Plan(TappedHole hole, ScrewStack stack)
        {
            var d = hole.Diameter;
            var wanted = options.Sizes.TryGetValue(d, out var bySize) ? bySize : options.Type;
            var (kind, note) = ScrewSpec.Choose(wanted, stack.Seat, options.Auto);
            if (kind is null)
                return (null, note);
            if (Lengths(kind, d).Count == 0 && options.Auto && kind != ScrewKind.Cap && kind.Head == ScrewHead.Cap)
            {
                note = $"{kind.Name}没有 {hole.Size}，换内六角圆柱头";
                kind = ScrewKind.Cap;
            }

            var table = Lengths(kind, d);
            if (table.Count == 0)
                return (null, $"蚂蚁工场{kind.Name}没有 {hole.Size}");

            var profile = Profile(kind, d, table, hole, stack);
            if (stack.Seat == ScrewSeat.Counterbore && profile.BearingRadius > stack.SeatRadius + 0.01)
            {
                if (!options.Auto || kind == ScrewKind.Cap)
                    return (null, $"{kind.Name}头 / 垫圈半径 {ModelSpec.Format(profile.BearingRadius)} 比沉孔半径 {ModelSpec.Format(stack.SeatRadius)} 大");
                note = $"{kind.Name}垫圈进不了沉孔（Ø{ModelSpec.Format(stack.SeatRadius * 2)}），换内六角圆柱头";
                kind = ScrewKind.Cap;
                table = Lengths(kind, d);
                profile = Profile(kind, d, table, hole, stack);
            }

            var window = ScrewSpec.Engagement(hole);
            var top = ScrewSpec.TopAt(stack, profile, kind.Head);
            double Engage(double length) => profile.TipFor(length) - top;
            if (ScrewSpec.Length(table, Engage, window) is not { } length)
                return (null, $"{kind.Name} {hole.Size}：{ScrewSpec.NoLength(table, Engage, window)}");

            var number = Number(kind, d, length);
            if (number is null)
                return (null, $"{kind.Name} {hole.Size} L{ModelSpec.Format(length)} 性能 {options.Grade} 选不全（换 grade=）");
            string? path = null;
            if (options.Apply)
                path = MymoooParts.FileFor(context, number.Value.Product, number.Value.Number, assemblyPath).Path;
            var pose = ScrewSpec.Place(profile, hole.Outward, hole.Entry + hole.Outward * top);
            return (new Plan(hole, stack, kind, length, Engage(length), number.Value.Number, path, pose, note), null);
        }

        /// <summary>长度表：选了规格之后 L 剩下的可选值；没有这个规格为空。</summary>
        private IReadOnlyList<double> Lengths(ScrewKind kind, double d)
        {
            if (lengths.TryGetValue((kind, d), out var cached))
                return cached;
            MymoooParts.Resolve(kind.Product, [new("M", Text(d))], context.Cancellation, out var selection, out var notes);
            var parameter = MymoooRules.Parameter(selection["productParameters"] as JsonArray ?? [], "L");
            var table = notes.Count > 0 || parameter is null ? [] : MymoooRules.Values(parameter);
            lengths[(kind, d)] = table;
            return table;
        }

        /// <summary>
        /// 这种这个规格的现量尺寸：只预览时按经验估；要装就下载（按估的先挑一档）、打开量一遍，量过的同种同规格复用。
        /// </summary>
        private ScrewProfile Profile(ScrewKind kind, double d, IReadOnlyList<double> table, TappedHole hole, ScrewStack stack)
        {
            if (profiles.TryGetValue((kind, d), out var cached))
                return cached;
            var estimate = ScrewSpec.Estimate(kind, d);
            if (!options.Apply)
                return estimate;
            var top = ScrewSpec.TopAt(stack, estimate, kind.Head);
            var guess = ScrewSpec.Length(table, length => estimate.TipFor(length) - top, ScrewSpec.Engagement(hole)) ?? table[table.Count / 2];
            var number = Number(kind, d, guess) ?? throw new QuickCommandException($"{kind.Name} {hole.Size} L{ModelSpec.Format(guess)} 性能 {options.Grade} 选不全（换 grade=）。");
            var path = MymoooParts.FileFor(context, number.Product, number.Number, assemblyPath).Path;
            context.SetState($"量 {kind.Name} {hole.Size}");
            var profile = WithOpen(context, path, part => ScrewScan.Profile(context.Api, part, d, guess));
            profiles[(kind, d)] = profile;
            return profile;
        }

        private (string Number, long Product)? Number(ScrewKind kind, double d, double length)
        {
            var key = $"{kind.Product}|{Text(d)}|{Text(length)}|{options.Grade}";
            if (numbers.TryGetValue(key, out var cached))
                return cached;
            var result = MymoooParts.Resolve(kind.Product, [new("M", Text(d)), new("L", Text(length)), new("XN", options.Grade)], context.Cancellation, out _, out var notes);
            if (result is null || notes.Count > 0)
                return null;
            numbers[key] = result.Value;
            return result;
        }
    }

    /// <summary>打开零件（已开着就直接用）做事，原来没开着的做完关掉。</summary>
    private static T WithOpen<T>(QuickCommandContext context, string path, Func<object, T> work)
    {
        var api = context.Api;
        var application = context.Session.Application;
        var already = api.Call(application, "ISldWorks", "GetOpenDocumentByName", path);
        object?[] open = [path, SwConst.DocumentPart, 1, string.Empty, 0, 0];
        var part = already ?? api.Call(application, "ISldWorks", "OpenDoc6", open)
                   ?? throw new QuickCommandException($"SolidWorks 没打开 {path}（错误码 {open[4]}）。");
        try
        {
            return work(part);
        }
        finally
        {
            if (already is null)
                api.CallForCleanup(application, "ISldWorks", "CloseDoc", api.CallString(part, "IModelDoc2", "GetTitle"));
        }
    }

    /// <summary>按文件分批插：同一文件开一次、插 N 个、逐个摆好固定。回每个计划对应的零部件名（顺序同 <paramref name="plans"/>）。</summary>
    private static IReadOnlyList<string> Insert(QuickCommandContext context, object assembly, string assemblyTitle, IReadOnlyList<Plan> plans)
    {
        var api = context.Api;
        var application = context.Session.Application;
        var names = new string[plans.Count];
        foreach (var group in plans.Select((plan, index) => (plan, index)).GroupBy(item => item.plan.Path!, StringComparer.OrdinalIgnoreCase))
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var already = api.Call(application, "ISldWorks", "GetOpenDocumentByName", group.Key);
            object?[] open = [group.Key, SwConst.DocumentPart, 1, string.Empty, 0, 0];
            var part = already ?? api.Call(application, "ISldWorks", "OpenDoc6", open)
                       ?? throw new QuickCommandException($"SolidWorks 没打开 {group.Key}（错误码 {open[4]}）。");
            var partTitle = api.CallString(part, "IModelDoc2", "GetTitle");
            try
            {
                api.Call(application, "ISldWorks", "ActivateDoc3", assemblyTitle, false, 1, 0);
                foreach (var (plan, index) in group)
                {
                    var (x, y, z) = plan.Pose.Origin;
                    var component = api.Call(assembly, "IAssemblyDoc", "AddComponent5", group.Key, 0, string.Empty, false, string.Empty, x / 1000, y / 1000, z / 1000)
                                    ?? throw new QuickCommandException($"SolidWorks 没把 {group.Key} 插进「{assemblyTitle}」。");
                    ModelAccess.Place(context, assembly, component, plan.Pose.Axes, plan.Pose.Origin, true);
                    names[index] = api.CallString(component, "IComponent2", "get_Name2");
                }
            }
            finally
            {
                if (already is null)
                    api.CallForCleanup(application, "ISldWorks", "CloseDoc", partTitle);
                api.CallForCleanup(application, "ISldWorks", "ActivateDoc3", assemblyTitle, false, 1, 0);
            }
        }

        return names;
    }

    private static string Report(string title, Options options, IReadOnlyList<Plan> plans, IReadOnlyList<string> skipped, int occupied, IReadOnlyList<string> inserted)
    {
        var text = new StringBuilder(options.Apply
            ? $"一键螺丝：「{title}」装了 {inserted.Count} 颗螺钉"
            : $"一键螺丝（预览，没下载没插入）：「{title}」要装 {plans.Count} 颗");
        text.Append(options.Scope is null ? string.Empty : $"（范围「{options.Scope}」）");
        if (occupied > 0)
            text.Append($"，{occupied} 个孔已有螺钉跳过");
        if (skipped.Count > 0)
            text.Append($"，{skipped.Count} 个孔没装");
        text.Append(options.Apply ? "；固定、没加配合，总装没存。" : "；长度按经验估，真装时按下载件现量再挑一次。").AppendLine();
        foreach (var group in plans.GroupBy(plan => plan.Number))
            text.AppendLine($"· {group.Key} × {group.Count()}（{group.First().Kind.Name}）");
        text.AppendLine("明细（螺纹孔 ← 孔口 · 旋入深度）：");
        foreach (var plan in plans.Take(Shown))
        {
            var clamp = string.Join(" / ", plan.Stack.Owners);
            text.AppendLine($"  {plan.Hole.Owner} {plan.Hole.Size}@{Where(plan.Hole)} ← {clamp} {SeatName(plan.Stack.Seat)} · 旋入 {ModelSpec.Format(plan.Engage, "0.#")} → {plan.Number}{(plan.Note is null ? string.Empty : "（" + plan.Note + "）")}");
        }

        if (plans.Count > Shown)
            text.AppendLine($"  …另有 {plans.Count - Shown} 颗。");
        foreach (var line in skipped.Take(Shown))
            text.AppendLine("· 跳过 " + line);
        if (skipped.Count > Shown)
            text.AppendLine($"· …另有 {skipped.Count - Shown} 个跳过。");
        return text.ToString().TrimEnd();
    }

    private static string SeatName(ScrewSeat seat) => seat switch
    {
        ScrewSeat.Counterbore => "沉孔",
        ScrewSeat.Countersink => "锥形沉头孔",
        _ => "表面",
    };

    private static string Where(TappedHole hole) => ModelAccess.Format((hole.Entry.X, hole.Entry.Y, hole.Entry.Z));

    private static string Text(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
