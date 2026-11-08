using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 建模类「异形孔」（1.43.0，用户定：零件上的孔一律走异形孔向导，不许拉伸切除打孔——不合规、出工程图孔标注认不出规格）。
/// </summary>
/// <remarks>
/// 一条指令在一张平面上打一组同规格的孔：选面上第一个孔心 → <c>HoleWizard5</c>（按 GB 孔库取标准尺寸）→
/// 进孔的位置草图补其余孔心 → 重建。真机：同一张面用 <c>SelectByRay</c> 选多次只算一次，只建出一个孔，所以多孔走补点。
/// 孔朝材料里打（孔向导按面自己定方向），reverse=是 反向。
/// </remarks>
internal static class ModelHole
{
    public static QuickCommand Command { get; } = new(
        Key: "model-hole",
        CommandName: StrenuaIdentity.Domain + ".model.hole",
        Title: "异形孔",
        Summary: "异形孔向导打孔：沉头 / 锥形沉头 / 通孔 / 螺纹 / 钻孔 / 销孔，按 GB 孔库取标准尺寸，一次在一张平面上打一组同规格孔；零件上的孔一律用它，不用拉伸切除。",
        Usage: "「类型」沉头 / 锥形沉头 / 通孔 / 螺纹 / 钻孔 / 销孔；「规格」螺钉类写 M6，钻孔 / 销孔写直径 6。"
               + "「孔心」零件坐标 mm 的 x,y,z，落在开孔那张平面上，多个分号隔开（第一个点选面）。「深度」省略 = 完全贯穿。"
               + "螺纹孔深度是底孔深，thread= 螺纹深（默认 2 倍公称直径）；沉头 cbore=直径,深度 覆盖标准沉孔；dia= 覆盖主孔径；fit= 紧 / 正常 / 松（螺钉配合）。"
               + "回执给建出的特征、各段孔径与孔数核对。改已有孔先 strenua.model.delete 删掉原切除。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("type", "类型", "沉头 / 锥形沉头 / 通孔 / 螺纹 / 钻孔 / 销孔", true),
            new QuickParameter("size", "规格", "M6（沉头 / 锥形沉头 / 通孔 / 螺纹）或直径 6（钻孔 / 销孔）", true),
            new QuickParameter("at", "孔心", "x,y,z;x,y,z…（零件坐标 mm，落在同一张平面上）", true, 1),
            new QuickParameter("depth", "深度", "mm；省略 = 完全贯穿", false, 2),
            new QuickParameter("thread", "螺纹深", "螺纹孔的螺纹深 mm，默认 2 倍公称直径", false, 2),
            new QuickParameter("cbore", "沉孔", "沉头：直径,深度；锥形沉头：直径（mm），省略按标准", false, 2),
            new QuickParameter("dia", "孔径", "覆盖主孔直径 mm，省略按标准", false, 2),
            new QuickParameter("fit", "配合", "紧 / 正常（默认）/ 松", false, 2),
            new QuickParameter("reverse", "反向", "是 = 反向打孔", false, 2),
            new QuickParameter("name", "名称", "特征改名，省略用 SolidWorks 默认名", false, 2),
        ],
        Example: StrenuaIdentity.Domain + ".model.hole type=沉头 size=M12 at=-20,-50,20;20,-50,20;-20,-90,20;20,-90,20",
        Bind: arguments =>
        {
            var kind = HoleKind.Parse(QuickArgs.Name(arguments, "type"));
            var size = HoleSpec.NormalizeSize(kind, QuickArgs.Name(arguments, "size"));
            var points = HoleSpec.ParsePoints(QuickArgs.Name(arguments, "at"));
            var depth = QuickArgs.Millimeters(QuickArgs.Optional(arguments, "depth"), "depth");
            var thread = QuickArgs.Millimeters(QuickArgs.Optional(arguments, "thread"), "thread");
            var diameter = QuickArgs.Millimeters(QuickArgs.Optional(arguments, "dia"), "dia");
            if (depth is <= 0 || thread is <= 0 || diameter is <= 0)
                throw new QuickArgumentException("depth= / thread= / dia= 要大于 0。");
            if (thread is not null && kind.Generic != HoleKind.Tap)
                throw new QuickArgumentException("thread= 只对螺纹孔。");
            if (thread is { } t && depth is { } dd && t > dd)
                throw new QuickArgumentException($"thread={ModelSpec.Format(t)} 比底孔深 depth={ModelSpec.Format(dd)} 还深。");
            var (counterDiameter, counterDepth) = Counter(kind, QuickArgs.Optional(arguments, "cbore"));
            var fit = Fit(QuickArgs.Optional(arguments, "fit"));
            var spec = new HoleSpec(kind, size, depth, points, fit, diameter, counterDiameter, counterDepth, thread, QuickArgs.Flag(arguments, "reverse"));
            var name = QuickArgs.Optional(arguments, "name");
            return context => Run(context, spec, name);
        });

    private static (double? Diameter, double? Depth) Counter(HoleKind kind, string? text)
    {
        if (text is null)
            return (null, null);
        var numbers = text.Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => QuickArgs.Millimeters(item, "cbore")!.Value).ToList();
        return kind.Generic switch
        {
            HoleKind.CounterBore when numbers.Count == 2 && numbers.All(n => n > 0) => (numbers[0], numbers[1]),
            HoleKind.CounterSink when numbers.Count == 1 && numbers[0] > 0 => (numbers[0], null),
            HoleKind.CounterBore => throw new QuickArgumentException($"cbore={text}：沉头写 直径,深度（mm）。"),
            HoleKind.CounterSink => throw new QuickArgumentException($"cbore={text}：锥形沉头只写锥孔直径（mm）。"),
            _ => throw new QuickArgumentException("cbore= 只对沉头 / 锥形沉头。"),
        };
    }

    private static HoleFit Fit(string? text) => text?.Trim() switch
    {
        null or "" or "正常" or "normal" => HoleFit.Normal,
        "紧" or "紧密" or "close" => HoleFit.Close,
        "松" or "宽松" or "loose" => HoleFit.Loose,
        _ => throw new QuickArgumentException($"fit={text}：写 紧 / 正常 / 松。"),
    };

    private const int StandardGb = 13;

    private static QuickOutcome Run(QuickCommandContext context, HoleSpec spec, string? name)
    {
        var api = context.Api;
        var part = ModelAccess.ActivePart(context);
        var extension = api.Call(part, "IModelDoc2", "get_Extension")!;
        var first = spec.Points[0];

        // 选面：第一个孔心所在的面，必须是平面，其余孔心都要落在这张面所在的平面上。
        api.ClearSelection(part);
        if (!api.CallBool(extension, "IModelDocExtension", "SelectByID2", string.Empty, "FACE", first.X / 1000, first.Y / 1000, first.Z / 1000, false, 0, null, 0))
            return QuickOutcome.Fail($"异形孔：在 {ModelAccess.Format(first)} 没选到零件表面（孔心要正好落在开孔那张面上，先 strenua.model.info 看包围盒）。");
        var selection = api.Call(part, "IModelDoc2", "get_SelectionManager")!;
        var face = api.Call(selection, "ISelectionMgr", "GetSelectedObject6", 1, -1);
        var surface = face is null ? null : api.Call(face, "IFace2", "GetSurface");
        if (surface is null || !api.CallBool(surface, "ISurface", "IsPlane"))
        {
            api.ClearSelection(part);
            return QuickOutcome.Fail($"异形孔：{ModelAccess.Format(first)} 落在曲面上，异形孔只在平面上打（斜孔先 strenua.model.plane 建基准面再说）。");
        }

        var normal = api.CallDoubles(face!, "IFace2", "get_Normal");
        var n = new ModelDirection(normal[0], normal[1], normal[2]).Normalized();
        var off = spec.Points.Select((p, i) => (i, gap: Math.Abs((p.X - first.X) * n.X + (p.Y - first.Y) * n.Y + (p.Z - first.Z) * n.Z)))
            .Where(item => item.gap > 0.01).ToList();
        if (off.Count > 0)
        {
            api.ClearSelection(part);
            return QuickOutcome.Fail($"异形孔：第 {string.Join("、", off.Select(item => item.i + 1))} 个孔心不在第一个孔心那张面上（差 {string.Join("、", off.Select(item => ModelSpec.Format(item.gap)))} mm）；"
                                     + "不同面的孔分开调用。");
        }

        var before = Volume(api, part);
        var values = spec.WizardValues();
        var features = api.Call(part, "IModelDoc2", "get_FeatureManager")!;
        var feature = api.Call(features, "IFeatureManager", "HoleWizard5",
            // 真机：EndType 是 short（传 int 反射报类型不符），其余编号是 int。
            spec.Kind.Generic, StandardGb, spec.Kind.Fastener, spec.Size, (short)spec.EndCondition,
            values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9],
            values[10], values[11], values[12], values[13], values[14],
            string.Empty, spec.Reverse, true, true, true, true, false);
        api.ClearSelection(part);
        if (feature is null)
            return QuickOutcome.Fail($"异形孔：{spec.Title} 没建成。常见原因：规格不在 GB 孔库「{spec.Kind.Description}」里（钻孔 / 销孔直径要是孔库里有的档），"
                                     + "或孔朝外打空了（试 reverse=是）。");

        var added = AddPoints(api, part, feature, spec.Points);
        api.Call(part, "IModelDoc2", "EditRebuild3");
        if (name is not null)
            api.Call(feature, "IFeature", "set_Name", name);
        var featureName = api.CallString(feature, "IFeature", "get_Name");
        var after = Volume(api, part);

        var (diameters, centers) = ReadBack(api, feature, first, n);
        var missing = spec.Points.Where(p => !centers.Any(c => HoleSpec.Distance(Project(p, first, n), c) < 0.05)).ToList();
        object?[] errorArgs = [false];
        var error = api.CallInt(feature, "IFeature", "GetErrorCode2", errorArgs);
        var text = $"异形孔：{spec.Title} × {spec.Points.Count} →「{featureName}」（GB {spec.Kind.Description}）；"
                   + $"孔径 {string.Join(" / ", diameters.Select(d => "Ø" + ModelSpec.Format(d)))}；"
                   + $"建出 {centers.Count} 个孔{(added.Failed > 0 ? $"，补点失败 {added.Failed} 个" : string.Empty)}；"
                   + $"体积 {ModelSpec.Format(before)} → {ModelSpec.Format(after)} cm³。";
        if (error != 0)
            return QuickOutcome.Fail(text + $" 特征重建报错（错误码 {error}），核对孔心与深度。");
        if (missing.Count > 0)
            return QuickOutcome.Fail(text + $" 这些孔心没打出孔：{string.Join(" ", missing.Select(ModelAccess.Format))}（孔心落在面外或打空）。");
        if (Math.Abs(after - before) < 1e-6)
            return QuickOutcome.Fail(text + " 体积没变，孔打空了：试 reverse=是。");
        return QuickOutcome.Ok(text);
    }

    /// <summary>
    /// 孔向导的位置草图（子特征里只有点、没有线段的那张）补齐其余孔心；草图点坐标 = 模型点经 <c>ModelToSketchTransform</c>（行向量 × 旋转 × 比例 + 平移）。
    /// </summary>
    private static (int Added, int Failed) AddPoints(SolidWorksApi api, object part, object feature, IReadOnlyList<(double X, double Y, double Z)> points)
    {
        object? position = null;
        object? sketch = null;
        for (var sub = api.Call(feature, "IFeature", "GetFirstSubFeature"); sub is not null; sub = api.Call(sub, "IFeature", "GetNextSubFeature"))
        {
            if (api.Call(sub, "IFeature", "GetSpecificFeature2") is not { } candidate)
                continue;
            if (api.CallArray(candidate, "ISketch", "GetSketchPoints2").Length > 0 && api.CallArray(candidate, "ISketch", "GetSketchSegments").Length == 0)
            {
                (position, sketch) = (sub, candidate);
                break;
            }
        }

        if (position is null || sketch is null)
            return (0, points.Count - 1);
        var data = api.CallDoubles(api.Call(sketch, "ISketch", "get_ModelToSketchTransform")!, "IMathTransform", "get_ArrayData");
        var targets = points.Select(p => ToSketch(data, p)).ToList();

        // 真机：SelectByID2 选面的点会偏一点（M12 沉头落在 -20.114,-50.074 而不是 -20,-50），孔向导就在那儿打第一个孔，
        // 所以位置草图里对不上孔心的点挪到还没占的孔心上，其余孔心再补点。
        api.ClearSelection(part);
        api.Call(position, "IFeature", "Select2", false, 0);
        api.Call(part, "IModelDoc2", "EditSketch");
        var active = api.Call(part, "IModelDoc2", "GetActiveSketch2") ?? sketch;
        var sketches = api.Call(part, "IModelDoc2", "get_SketchManager")!;
        api.Call(sketches, "ISketchManager", "set_AddToDB", true);
        var (added, failed) = (0, 0);
        try
        {
            var free = new List<(double X, double Y)>(targets);
            var strays = new List<object>();
            foreach (var point in api.CallArray(active, "ISketch", "GetSketchPoints2"))
            {
                var at = (X: Convert.ToDouble(api.Call(point, "ISketchPoint", "get_X")), Y: Convert.ToDouble(api.Call(point, "ISketchPoint", "get_Y")));
                var hit = free.FindIndex(q => Math.Abs(q.X - at.X) < 1e-5 && Math.Abs(q.Y - at.Y) < 1e-5);
                if (hit >= 0)
                    free.RemoveAt(hit);
                else
                    strays.Add(point);
            }

            foreach (var point in strays)
            {
                if (free.Count == 0)
                    break;
                if (api.CallBool(point, "ISketchPoint", "SetCoords", free[0].X, free[0].Y, 0.0))
                    free.RemoveAt(0);
            }

            foreach (var q in free)
            {
                if (api.Call(sketches, "ISketchManager", "CreatePoint", q.X, q.Y, 0.0) is not null)
                    added++;
                else
                    failed++;
            }
        }
        finally
        {
            api.Call(sketches, "ISketchManager", "set_AddToDB", false);
            api.Call(sketches, "ISketchManager", "InsertSketch", true);
            api.ClearSelection(part);
        }

        // 真机：改了位置草图后 EditRebuild3 不重建孔特征（草图里 5 个点、孔还是 1 个），要强制重建。
        api.Call(part, "IModelDoc2", "ForceRebuild3", false);
        return (added, failed);
    }

    private static (double X, double Y) ToSketch(double[] a, (double X, double Y, double Z) mm)
    {
        var (x, y, z) = (mm.X / 1000, mm.Y / 1000, mm.Z / 1000);
        var s = a.Length > 12 ? a[12] : 1;
        return ((x * a[0] + y * a[3] + z * a[6]) * s + a[9], (x * a[1] + y * a[4] + z * a[7]) * s + a[10]);
    }

    /// <summary>特征的圆柱面：各段直径（mm，从小到大）与孔心（投到开孔面上，mm，去重）。</summary>
    private static (List<double> Diameters, List<(double X, double Y, double Z)> Centers) ReadBack(SolidWorksApi api, object feature, (double X, double Y, double Z) origin, ModelDirection n)
    {
        var diameters = new List<double>();
        var centers = new List<(double X, double Y, double Z)>();
        foreach (var face in api.CallArray(feature, "IFeature", "GetFaces"))
        {
            if (api.Call(face, "IFace2", "GetSurface") is not { } surface || !api.CallBool(surface, "ISurface", "IsCylinder"))
                continue;
            var p = api.CallDoubles(surface, "ISurface", "get_CylinderParams");
            var diameter = Math.Round(p[6] * 2000, 3);
            if (!diameters.Any(d => Math.Abs(d - diameter) < 0.001))
                diameters.Add(diameter);
            var center = Project((p[0] * 1000, p[1] * 1000, p[2] * 1000), origin, n);
            if (!centers.Any(c => HoleSpec.Distance(c, center) < 0.05))
                centers.Add(center);
        }

        diameters.Sort();
        return (diameters, centers);
    }

    private static (double X, double Y, double Z) Project((double X, double Y, double Z) p, (double X, double Y, double Z) origin, ModelDirection n)
    {
        var d = (p.X - origin.X) * n.X + (p.Y - origin.Y) * n.Y + (p.Z - origin.Z) * n.Z;
        return (p.X - n.X * d, p.Y - n.Y * d, p.Z - n.Z * d);
    }

    private static double Volume(SolidWorksApi api, object part)
        => api.CallArray(part, "IPartDoc", "GetBodies2", 0, true).Sum(body =>
            api.CallDoubles(body, "IBody2", "GetMassProperties", 1.0) is { Length: > 3 } mass ? mass[3] * 1e6 : 0);
}
