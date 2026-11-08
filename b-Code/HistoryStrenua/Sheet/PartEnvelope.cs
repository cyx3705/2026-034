using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>一侧读回的结果：零件 mm 的值（已加偏移）与情况（空 = 正常；「失效」「变了」这类给回执）。</summary>
internal sealed record EnvelopeReading(EnvelopeSide Side, double Value, string? Note);

/// <summary>一个零部件的包络读回：零件的六面、零件坐标包围盒与合成盒、总装里的外接盒。</summary>
internal sealed record EnvelopeState(object Model, bool Stored, IReadOnlyList<EnvelopeReading> Sides, (Vec3 Min, Vec3 Max) PartBox, (Vec3 Min, Vec3 Max) Box, (Vec3 Min, Vec3 Max) InAssembly, string? Crossed);

/// <summary>
/// 装配包络的读写（1.42.0）：零件自定义属性 <see cref="EnvelopeSpec.PropertyName"/>、零件坐标的包围盒与面几何。
/// </summary>
/// <remarks>
/// 面编号同 <see cref="AssemblyGeometry.Face"/>（零部件 <c>GetBodies2</c> 依次 <c>GetFaces</c>），几何是零件坐标（1.39.0 真机核实），不乘变换。
/// 包围盒用 <c>IPartDoc.GetPartBox(true)</c>（精确盒，零件坐标、米）。
/// </remarks>
internal static class PartEnvelope
{
    private const int CustomInfoText = 30, DeleteAndAdd = 2;

    /// <summary>零部件的零件文档；子装配、读不到文档（轻化 / 压缩）抛可纠正的错。</summary>
    public static object Model(SolidWorksApi api, object component, string name)
    {
        if (api.CallArray(component, "IComponent2", "GetChildren").Length > 0)
            throw new QuickCommandException($"「{name}」是子装配：包络只做零件，填它下面的零件（子装配-1/零件-1）。");
        if (api.CallBool(component, "IComponent2", "IsSuppressed"))
            throw new QuickCommandException($"「{name}」被压缩了，先解除压缩。");
        var model = api.Call(component, "IComponent2", "GetModelDoc2") ?? throw new QuickCommandException($"读不到「{name}」的零件文档（轻化了？先在 SolidWorks 里还原成完全解析）。");
        return api.CallInt(model, "IModelDoc2", "GetType") == SwConst.DocumentPart
            ? model
            : throw new QuickCommandException($"「{name}」不是零件：包络只做零件。");
    }

    /// <summary>读属性原文；没有为 null。</summary>
    public static string? Read(SolidWorksApi api, object model)
    {
        var manager = Manager(api, model);
        if (!api.CallArray(manager, "ICustomPropertyManager", "GetNames").OfType<string>().Contains(EnvelopeSpec.PropertyName, StringComparer.Ordinal))
            return null;
        // Get6 最后一个 out 是 bool，传错类型反射绑不上（同 PartProperties）。
        object?[] arguments = [EnvelopeSpec.PropertyName, false, string.Empty, string.Empty, false, false];
        api.Call(manager, "ICustomPropertyManager", "Get6", arguments);
        return arguments[2] as string ?? string.Empty;
    }

    /// <summary>
    /// 写属性并读回核对；零件写之前没有未保存改动就顺手存掉（只存了这一条属性），否则留着并在返回里说明。
    /// </summary>
    public static string Write(SolidWorksApi api, object model, IReadOnlyList<EnvelopeSide> sides)
    {
        var text = EnvelopeSpec.Format(sides);
        var clean = !api.CallBool(model, "IModelDoc2", "GetSaveFlag");
        var manager = Manager(api, model);
        var status = api.CallInt(manager, "ICustomPropertyManager", "Add3", EnvelopeSpec.PropertyName, CustomInfoText, text, DeleteAndAdd);
        if (status != 0 || Read(api, model) != text)
            throw new QuickCommandException($"SolidWorks 没写进零件属性「{EnvelopeSpec.PropertyName}」（返回 {status}）。");
        if (!clean)
            return "零件本来就有未保存的改动，包络属性跟着它没存（存零件时一起存）";
        object?[] save = [SwConst.SaveSilent, 0, 0];
        return api.CallBool(model, "IModelDoc2", "Save3", save) && !api.CallBool(model, "IModelDoc2", "GetSaveFlag")
            ? "已存进零件"
            : $"零件存盘失败（错误码 {save[1]}），包络属性还在内存里";
    }

    /// <summary>零件坐标精确包围盒（mm）。</summary>
    public static (Vec3 Min, Vec3 Max) PartBox(SolidWorksApi api, object model)
    {
        var b = api.CallDoubles(model, "IPartDoc", "GetPartBox", true);
        if (b.Length < 6)
            throw new QuickCommandException("读不到零件包围盒（零件没有实体？）。");
        return (new Vec3(b[0], b[1], b[2]) * 1000, new Vec3(b[3], b[4], b[5]) * 1000);
    }

    /// <summary>零部件第 <paramref name="index"/> 个面在零件坐标里的几何；不是平面 / 圆柱面抛可纠正的错，序号越界抛参数错。</summary>
    public static EnvelopeFaceGeometry Face(SolidWorksApi api, object component, int index)
    {
        var face = AssemblyGeometry.Face(api, component, index);
        var surface = api.Call(face, "IFace2", "GetSurface") ?? throw new QuickCommandException($"F{index} 读不到曲面。");
        var t = api.CallDoubles(face, "IFace2", "GetTessTriangles", true);
        if (t.Length < 3)
            throw new QuickCommandException($"F{index} 读不到面片。");
        if (api.CallBool(surface, "ISurface", "IsPlane"))
        {
            // IFace2.Normal 已按面朝向（朝实体外），同 AssemblyGeometry.Faces。
            var n = api.CallDoubles(face, "IFace2", "get_Normal");
            return n.Length < 3
                ? throw new QuickCommandException($"F{index} 读不到法向。")
                : new EnvelopeFaceGeometry(true, new Vec3(n[0], n[1], n[2]), new Vec3(t[0], t[1], t[2]) * 1000, 0, false);
        }

        if (api.CallBool(surface, "ISurface", "IsCylinder"))
        {
            var p = api.CallDoubles(surface, "ISurface", "get_CylinderParams");
            var norms = api.CallDoubles(face, "IFace2", "GetTessNorms");
            if (p.Length < 7 || norms.Length < 3)
                throw new QuickCommandException($"F{index} 读不到圆柱参数。");
            var axis = new Vec3(p[3], p[4], p[5]).Normalized();
            var w = new Vec3(t[0] - p[0], t[1] - p[1], t[2] - p[2]);
            var radial = w - axis * w.Dot(axis);
            var hole = new Vec3(norms[0], norms[1], norms[2]).Dot(radial) < 0;
            return new EnvelopeFaceGeometry(false, axis, new Vec3(p[0], p[1], p[2]) * 1000, p[6] * 1000, hole);
        }

        throw new QuickCommandException($"F{index} 不是平面也不是圆柱面，包络只认这两种。");
    }

    /// <summary>
    /// 读一个零部件的包络：属性没写过就全按包围盒（<see cref="EnvelopeState.Stored"/> 为假）。
    /// 跟面的侧现读面：面没了 / 不合格就用上次值并标「失效」，值和上次差 0.01 mm 以上标「变了」（不回写，改面或重建时才写）。
    /// </summary>
    public static EnvelopeState Load(SolidWorksApi api, object component, string name)
    {
        var model = Model(api, component, name);
        var raw = Read(api, model);
        var sides = EnvelopeSpec.Parse(raw);
        var box = PartBox(api, model);
        var readings = new List<EnvelopeReading>(6);
        var values = new Dictionary<int, double>();
        foreach (var side in sides)
        {
            switch (side.Source)
            {
                case EnvelopeSource.Box:
                    readings.Add(new EnvelopeReading(side, EnvelopeSpec.Component(side.Side % 2 == 0 ? box.Min : box.Max, EnvelopeSpec.AxisOf(side.Side)), null));
                    break;
                case EnvelopeSource.Manual:
                    values[side.Side] = side.Value;
                    readings.Add(new EnvelopeReading(side, side.Value, null));
                    break;
                default:
                    string? note = null;
                    double value;
                    try
                    {
                        var geometry = Face(api, component, side.Face);
                        EnvelopeSpec.Check(geometry, side.Side, side.Face);
                        value = EnvelopeSpec.Outward(side.Side, EnvelopeSpec.Locate(geometry, side.Side, side.Axis).Value, side.Offset);
                        if (Math.Abs(value - side.Value) > 0.01)
                            note = $"面变了：上次 {EnvelopeSpec.Num(side.Value)}，现在 {EnvelopeSpec.Num(value)}";
                    }
                    catch (Exception error) when (error is QuickCommandException or QuickArgumentException)
                    {
                        value = side.Value;
                        note = $"失效（{error.Message}），先用上次值 {EnvelopeSpec.Num(side.Value)}，用 asm.envface 重指";
                    }

                    values[side.Side] = value;
                    readings.Add(new EnvelopeReading(side, value, note));
                    break;
            }
        }

        var (min, max, crossed) = EnvelopeSpec.Compose(box, values);
        var (origin, axes) = ModelAccess.Frame(api, component);
        return new EnvelopeState(model, raw is not null, readings, box, (min, max), EnvelopeSpec.ToAssembly((min, max), axes, origin), crossed);
    }

    /// <summary>零部件有没有写过包络（列表用，读不到文档的当没有）。</summary>
    public static bool Has(SolidWorksApi api, object component)
    {
        try
        {
            return api.CallArray(component, "IComponent2", "GetChildren").Length == 0
                   && api.Call(component, "IComponent2", "GetModelDoc2") is { } model
                   && api.CallInt(model, "IModelDoc2", "GetType") == SwConst.DocumentPart
                   && Read(api, model) is not null;
        }
        catch (QuickCommandException)
        {
            return false;
        }
    }

    private static object Manager(SolidWorksApi api, object model)
    {
        var extension = api.Call(model, "IModelDoc2", "get_Extension") ?? throw new QuickCommandException("SolidWorks 没有返回 ModelDocExtension。");
        return api.Call(extension, "IModelDocExtension", "get_CustomPropertyManager", string.Empty) ?? throw new QuickCommandException("SolidWorks 没有返回属性管理器。");
    }
}
