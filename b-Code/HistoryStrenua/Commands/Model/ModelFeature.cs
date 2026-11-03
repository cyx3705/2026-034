using System.Text;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>建模类（1.38.0）：拉伸 / 切除、偏置基准面、删特征、模型信息。</summary>
internal static class ModelFeature
{
    public static QuickCommand ExtrudeCommand { get; } = new(
        Key: "model-extrude",
        CommandName: StrenuaIdentity.Domain + ".model.extrude",
        Title: "拉伸切除",
        Summary: "把一张草图拉伸成实体或切除（盲孔深度 / 两向贯穿 / 中面对称），切除方向不对自动换向；回执给零件包围盒与体积变化。",
        Usage: "「草图」填草图名（画草图回执里给的，省略取最后一张）。「深度」mm；切除写 cut=是，through=是 两向全贯穿（不用给深度）；"
               + "mid=是 两边对称拉伸；reverse=是 反向。拉伸默认朝基准面法向（前视 +Z、上视 +Y、右视 +X）。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("sketch", "草图", "草图名；省略取特征树最后一张草图", false),
            new QuickParameter("depth", "深度", "mm；through=是 时不用给", false, 1),
            new QuickParameter("cut", "切除", "是 = 切除，否 = 拉伸（默认）", false, 1),
            new QuickParameter("through", "贯穿", "是 = 两向完全贯穿（只对切除）", false, 1),
            new QuickParameter("mid", "对称", "是 = 两侧对称（总深 = 深度）", false, 1),
            new QuickParameter("reverse", "反向", "是 = 反向", false, 1),
        ],
        Example: StrenuaIdentity.Domain + ".model.extrude sketch=草图1 depth=20",
        Bind: arguments =>
        {
            var sketch = QuickArgs.Optional(arguments, "sketch");
            var depth = QuickArgs.Millimeters(QuickArgs.Optional(arguments, "depth"), "depth");
            var cut = QuickArgs.Flag(arguments, "cut");
            var through = QuickArgs.Flag(arguments, "through");
            var mid = QuickArgs.Flag(arguments, "mid");
            var reverse = QuickArgs.Flag(arguments, "reverse");
            if (through && !cut)
                throw new QuickArgumentException("through=是 只对切除（cut=是）。");
            if (!through && depth is not > 0)
                throw new QuickArgumentException("要给 depth=（mm，大于 0），或切除写 through=是。");
            return context => Extrude(context, sketch, depth ?? 0, cut, through, mid, reverse);
        });

    public static QuickCommand PlaneCommand { get; } = new(
        Key: "model-plane",
        CommandName: StrenuaIdentity.Domain + ".model.plane",
        Title: "偏置基准面",
        Summary: "从基准面平移一段距离建新基准面（沿法向，负数反向），回执给新基准面名，供画草图用。",
        Usage: "「基准」写 前视 / 上视 / 右视 或基准面名；「距离」mm，正数沿法向（前视 +Z、上视 +Y、右视 +X），负数反向。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("base", "基准", "前视 / 上视 / 右视 / 基准面名", true),
            new QuickParameter("offset", "距离", "mm，可为负", true, 1),
        ],
        Example: StrenuaIdentity.Domain + ".model.plane base=上视 offset=20",
        Bind: arguments =>
        {
            var name = QuickArgs.Name(arguments, "base");
            var offset = QuickArgs.Millimeters(QuickArgs.Name(arguments, "offset"), "offset")!.Value;
            if (offset == 0)
                throw new QuickArgumentException("offset=0 就是原来的面，直接用它画草图。");
            return context => Plane(context, name, offset);
        });

    public static QuickCommand DeleteCommand { get; } = new(
        Key: "model-delete",
        CommandName: StrenuaIdentity.Domain + ".model.delete",
        Title: "删特征",
        Summary: "按名字删零件里的特征（逗号分开可删几个；删拉伸连带它的草图）。",
        Usage: "「特征」填特征树里的名字，逗号分开。删拉伸 / 切除时它吃掉的草图一起删，依赖它的后续特征 SolidWorks 会报错，回执列出。",
        Run: QuickCommand.Unbound,
        Parameters: [new QuickParameter("name", "特征", "特征名，逗号分开", true)],
        Example: StrenuaIdentity.Domain + ".model.delete name=切除-拉伸2",
        Bind: arguments =>
        {
            var names = QuickArgs.Names(QuickArgs.Name(arguments, "name"), "name");
            return context => Delete(context, names);
        });

    public static QuickCommand InfoCommand { get; } = new(
        Key: "model-info",
        CommandName: StrenuaIdentity.Domain + ".model.info",
        Title: "模型信息",
        Summary: "读活动零件：包围盒、体积、材料、特征树、全部孔（直径、轴向、孔心、深度），给定位与接口核对用。",
        Usage: "不带参数，读活动零件。孔按几何认（整圈内凹圆柱面），同轴同径合并；孔心是零件坐标 mm。",
        Run: Info,
        Readonly: true,
        SelfReport: true);

    // swEndConditions_e
    private const int Blind = 0;
    private const int ThroughAll = 1;
    private const int MidPlane = 6;

    private static QuickOutcome Extrude(QuickCommandContext context, string? sketchName, double depth, bool cut, bool through, bool mid, bool reverse)
    {
        var api = context.Api;
        var part = ModelAccess.ActivePart(context);
        var sketch = (sketchName is null ? LastSketch(api, part) : Feature(api, part, sketchName))
                     ?? throw new QuickArgumentException(sketchName is null ? "零件里没有草图：先 strenua.model.sketch。" : $"零件里没有特征「{sketchName}」。");
        var name = api.CallString(sketch, "IFeature", "get_Name");
        var before = Volume(api, part);
        var features = api.Call(part, "IModelDoc2", "get_FeatureManager")!;
        object? created = null;
        var flipped = false;
        foreach (var flip in new[] { reverse, !reverse })
        {
            api.ClearSelection(part);
            api.Call(sketch, "IFeature", "Select2", false, 0);
            var end = through ? ThroughAll : mid ? MidPlane : Blind;
            var meters = depth / 1000;
            created = cut
                ? api.Call(features, "IFeatureManager", "FeatureCut4", !through, false, flip, end, through ? ThroughAll : Blind, meters, through ? meters : 0.0,
                    false, false, false, false, 0.0, 0.0, false, false, false, false, false, true, true, false, false, false, 0, 0.0, false, false)
                : api.Call(features, "IFeatureManager", "FeatureExtrusion3", true, false, flip, end, Blind, meters, 0.0,
                    false, false, false, false, 0.0, 0.0, false, false, false, false, true, true, true, 0, 0.0, false);
            // 切除朝外面空着的一侧 SolidWorks 不建特征（返回空），换个方向再来一次。拉伸两边都能建，不换。
            if (created is not null || !cut || through)
                break;
            flipped = true;
        }

        api.ClearSelection(part);
        if (created is null)
            return QuickOutcome.Fail($"{(cut ? "切除" : "拉伸")}：草图「{name}」没建成特征（草图不封闭、切除两个方向都切不到实体，或拉伸碰不到已有实体又不是第一个）。");
        api.Call(part, "IModelDoc2", "EditRebuild3");
        var after = Volume(api, part);
        var featureName = api.CallString(created, "IFeature", "get_Name");
        var how = through ? "两向贯穿" : $"{(mid ? "对称 " : string.Empty)}{ModelSpec.Format(depth)} mm";
        var text = $"{(cut ? "切除" : "拉伸")}：草图「{name}」→「{featureName}」，{how}{(flipped ? "（原方向切不到，已换向）" : string.Empty)}；"
                   + $"体积 {ModelSpec.Format(before)} → {ModelSpec.Format(after)} cm³，包围盒 {BoxText(api, part)}。";
        if (cut && Math.Abs(after - before) < 1e-6)
            return QuickOutcome.Fail(text + " 体积没变，切空了：核对草图位置或方向。");
        return QuickOutcome.Ok(text);
    }

    private static QuickOutcome Plane(QuickCommandContext context, string baseName, double offset)
    {
        var api = context.Api;
        var part = ModelAccess.ActivePart(context);
        var plane = ModelSketch.PlaneFeature(api, part, baseName) ?? throw new QuickArgumentException($"base={baseName}：没有这个基准面。");
        api.ClearSelection(part);
        api.Call(plane, "IFeature", "Select2", false, 0);
        // swRefPlaneReferenceConstraint_Distance = 8，加 OptionFlip（256）反向。返回的是 RefPlane 不是 Feature，取特征树最后一个。
        var constraint = 8 | (offset < 0 ? FlipOption : 0);
        var features = api.Call(part, "IModelDoc2", "get_FeatureManager")!;
        var created = api.Call(features, "IFeatureManager", "InsertRefPlane", constraint, Math.Abs(offset) / 1000, 0, 0.0, 0, 0.0);
        api.ClearSelection(part);
        if (created is null)
            return QuickOutcome.Fail($"偏置基准面：从「{baseName}」偏 {ModelSpec.Format(offset)} mm 没建成。");
        var name = api.Call(part, "IModelDoc2", "FeatureByPositionReverse", 0) is { } feature ? api.CallString(feature, "IFeature", "get_Name") : "?";
        return QuickOutcome.Ok($"偏置基准面：从「{baseName}」沿法向 {ModelSpec.Format(offset)} mm 建了「{name}」，画草图写 plane={name}（草图坐标轴同「{baseName}」）。");
    }

    private const int FlipOption = 256;

    private static QuickOutcome Delete(QuickCommandContext context, IReadOnlyList<string> names)
    {
        var api = context.Api;
        var part = ModelAccess.ActivePart(context);
        var extension = api.Call(part, "IModelDoc2", "get_Extension")!;
        var deleted = new List<string>();
        var missing = new List<string>();
        foreach (var name in names)
        {
            if (Feature(api, part, name) is not { } feature)
            {
                missing.Add(name);
                continue;
            }

            api.ClearSelection(part);
            api.Call(feature, "IFeature", "Select2", false, 0);
            // swDelete_Absorbed = 2：连带被它吃掉的草图；swDelete_Children（1）不给，后续特征留着由 SolidWorks 报错。
            if (api.CallBool(extension, "IModelDocExtension", "DeleteSelection2", 2))
                deleted.Add(name);
            else
                missing.Add(name + "（SolidWorks 拒删）");
        }

        api.ClearSelection(part);
        api.Call(part, "IModelDoc2", "EditRebuild3");
        var errors = FeatureErrors(api, part);
        var text = $"删特征：删了 {string.Join("、", deleted)}{(deleted.Count == 0 ? "（无）" : string.Empty)}"
                   + (missing.Count > 0 ? $"；没删：{string.Join("、", missing)}" : string.Empty)
                   + (errors.Count > 0 ? $"；重建报错的特征：{string.Join("、", errors)}" : string.Empty) + "。";
        return missing.Count == 0 && errors.Count == 0 ? QuickOutcome.Ok(text) : QuickOutcome.Fail(text);
    }

    private static QuickOutcome Info(QuickCommandContext context)
    {
        var api = context.Api;
        var part = ModelAccess.ActivePart(context);
        var text = new StringBuilder($"模型信息：{api.CallString(part, "IModelDoc2", "GetTitle")}（{api.CallString(part, "IModelDoc2", "GetPathName")}）").AppendLine();
        var material = api.CallString(part, "IPartDoc", "GetMaterialPropertyName2", string.Empty, string.Empty);
        text.AppendLine($"包围盒 {BoxText(api, part)}，体积 {ModelSpec.Format(Volume(api, part))} cm³，材料 {(material.Length > 0 ? material : "（未指定）")}");
        var features = new List<string>();
        for (var feature = api.Call(part, "IModelDoc2", "FirstFeature"); feature is not null; feature = api.Call(feature, "IFeature", "GetNextFeature"))
        {
            var type = api.CallString(feature, "IFeature", "GetTypeName2");
            if (type is "ProfileFeature" or "RefPlane" or "Extrusion" or "Cut" or "ICE" or "HoleWzd" or "Fillet" or "Chamfer" or "LPattern" or "CirPattern" or "MirrorPattern" or "BaseBody" or "Boss" or "Revolution" or "RevCut" or "SMBaseFlange" or "SheetMetal")
                features.Add($"{api.CallString(feature, "IFeature", "get_Name")}[{type}]");
        }

        text.AppendLine("特征：" + string.Join("、", features));
        var geometry = PartScan.Read(context, part);
        var holes = geometry.Holes.Where(hole => !double.IsNaN(hole.AxialMin)).ToList();
        text.AppendLine($"孔 {holes.Count} 个（零件坐标 mm；孔心取轴线上离原点最近的点，深度沿轴）：");
        foreach (var group in holes.GroupBy(hole => (Math.Round(hole.Radius * 2000, 2), AxisName(hole.Axis))))
        {
            var centers = group.Select(hole => Center(hole)).Select(c => $"({ModelSpec.Format(c.X)},{ModelSpec.Format(c.Y)},{ModelSpec.Format(c.Z)})");
            var depths = group.Select(hole => ModelSpec.Format((hole.AxialMax - hole.AxialMin) * 1000)).Distinct();
            text.AppendLine($"· Ø{ModelSpec.Format(group.Key.Item1)} 沿 {group.Key.Item2} ×{group.Count()}，深 {string.Join("/", depths)}：{string.Join(" ", centers)}");
        }

        return QuickOutcome.Ok(text.ToString().TrimEnd());
    }

    /// <summary>孔轴线上离原点最近的点（mm）：沿轴的分量去掉，只剩垂直于轴的位置。</summary>
    private static (double X, double Y, double Z) Center(PartHole hole)
    {
        var axis = hole.Axis.Normalized();
        var along = hole.Point.Dot(axis);
        return ((hole.Point.X - axis.X * along) * 1000, (hole.Point.Y - axis.Y * along) * 1000, (hole.Point.Z - axis.Z * along) * 1000);
    }

    private static string AxisName(ModelDirection axis)
    {
        var a = axis.Normalized();
        return Math.Abs(a.X) > 0.999 ? "X" : Math.Abs(a.Y) > 0.999 ? "Y" : Math.Abs(a.Z) > 0.999 ? "Z" : $"({ModelSpec.Format(a.X, "0.###")},{ModelSpec.Format(a.Y, "0.###")},{ModelSpec.Format(a.Z, "0.###")})";
    }

    private static object? Feature(SolidWorksApi api, object part, string name)
        => api.Call(part, "IPartDoc", "FeatureByName", name);

    private static object? LastSketch(SolidWorksApi api, object part)
    {
        for (var i = 0; api.Call(part, "IModelDoc2", "FeatureByPositionReverse", i) is { } feature; i++)
        {
            if (api.CallString(feature, "IFeature", "GetTypeName2") == "ProfileFeature")
                return feature;
        }

        return null;
    }

    /// <summary>实体总体积（cm³）；没有实体为 0。</summary>
    private static double Volume(SolidWorksApi api, object part)
        => api.CallArray(part, "IPartDoc", "GetBodies2", 0, true).Sum(body =>
            api.CallDoubles(body, "IBody2", "GetMassProperties", 1.0) is { Length: > 3 } mass ? mass[3] * 1e6 : 0);

    private static string BoxText(SolidWorksApi api, object part)
    {
        var box = api.CallDoubles(part, "IPartDoc", "GetPartBox", true);
        if (box.Length < 6 || api.CallArray(part, "IPartDoc", "GetBodies2", 0, true).Length == 0)
            return "（没有实体）";
        var mm = box.Select(value => ModelSpec.Format(value * 1000)).ToArray();
        return $"X {mm[0]}~{mm[3]}，Y {mm[1]}~{mm[4]}，Z {mm[2]}~{mm[5]}";
    }

    private static List<string> FeatureErrors(SolidWorksApi api, object part)
    {
        var errors = new List<string>();
        for (var feature = api.Call(part, "IModelDoc2", "FirstFeature"); feature is not null; feature = api.Call(feature, "IFeature", "GetNextFeature"))
        {
            object?[] args = [false];
            if (api.CallInt(feature, "IFeature", "GetErrorCode2", args) != 0)
                errors.Add(api.CallString(feature, "IFeature", "get_Name"));
        }

        return errors;
    }
}
