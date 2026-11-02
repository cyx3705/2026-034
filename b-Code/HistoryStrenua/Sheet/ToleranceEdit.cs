using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>公差标注的共用动作（1.36.0）：写尺寸公差、选形位公差和基准符号指向的对象。</summary>
internal static class ToleranceEdit
{
    private const int FitWithTolerance = 8, FitLinear = 3, FitDigits = 3, TextAbove = 3, TextBelow = 4;

    /// <summary>写线性 / 直径等普通尺寸的公差，读回核对类型。</summary>
    public static bool WriteDimension(SolidWorksApi api, object display, DimensionToleranceSpec spec)
    {
        if (api.Call(display, "IDisplayDimension", "GetDimension2", 0) is not { } dimension
            || api.Call(dimension, "IDimension", "get_Tolerance") is not { } tolerance)
            return false;
        api.Call(tolerance, "IDimensionTolerance", "set_Type", spec.Type);
        if (spec.Upper is { } upper && spec.Lower is { } lower)
        {
            // 同 DimensionScan.SetSymmetric：工程图从动尺寸 SetValues2 写不进去，SetValues 才行。
            var (max, min) = (upper * QuickArgs.Mm, lower * QuickArgs.Mm);
            if (!api.CallBool(tolerance, "IDimensionTolerance", "SetValues", min, max))
                api.Call(tolerance, "IDimensionTolerance", "SetValues2", min, max, 0, null);
            if (Math.Abs(Convert.ToDouble(api.Call(tolerance, "IDimensionTolerance", "GetMaxValue")) - max) > 1e-9)
                return false;
        }

        if (spec.HoleFit is not null || spec.ShaftFit is not null)
        {
            api.Call(tolerance, "IDimensionTolerance", "SetFitValues", spec.HoleFit ?? string.Empty, spec.ShaftFit ?? string.Empty);
            if (spec.Type == FitWithTolerance)
                api.Call(tolerance, "IDimensionTolerance", "set_FitDisplayStyle", FitLinear);
        }

        if (api.CallInt(tolerance, "IDimensionTolerance", "get_Type") != spec.Type)
            return false;
        if (spec.Type == FitWithTolerance)
            api.Call(display, "IDisplayDimension", "SetPrecision3", api.CallInt(display, "IDisplayDimension", "GetPrimaryPrecision2"),
                api.CallInt(display, "IDisplayDimension", "GetAlternatePrecision2"), FitDigits, api.CallInt(display, "IDisplayDimension", "GetAlternateTolPrecision2"));
        else
            DimensionScan.SetPrecision(api, display, api.CallInt(display, "IDisplayDimension", "GetPrimaryPrecision2"));
        return true;
    }

    /// <summary>
    /// 写孔标注的公差：公差在孔标注变量里。<paramref name="variable"/> 省略时写第一个直径变量。
    /// 返回写到的变量名；找不到变量回 null，<paramref name="available"/> 列出有哪些。
    /// </summary>
    public static string? WriteCallout(SolidWorksApi api, object display, DimensionToleranceSpec spec, string? variable, out IReadOnlyList<string> available)
    {
        const int lengthVariable = 1;
        var lengths = api.CallArray(display, "IDisplayDimension", "GetHoleCalloutVariables")
            .Where(item => item is not null && api.CallInt(item, "ICalloutVariable", "get_Type") == lengthVariable)
            .Select(item => (Item: item, Name: api.CallString(item, "ICalloutVariable", "get_VariableName")))
            .ToList();
        available = lengths.Select(item => item.Name).ToList();
        var chosen = variable is not null
            ? lengths.FirstOrDefault(item => item.Name.Equals(variable, StringComparison.OrdinalIgnoreCase))
            : lengths.FirstOrDefault(item => item.Name.Contains("diam", StringComparison.OrdinalIgnoreCase), lengths.FirstOrDefault());
        if (chosen.Item is null)
            return null;

        api.Call(chosen.Item, "ICalloutVariable", "set_ToleranceType", spec.Type);
        if (spec.Upper is { } upper && spec.Lower is { } lower)
        {
            api.Call(chosen.Item, "ICalloutVariable", "set_ToleranceMax", upper * QuickArgs.Mm);
            api.Call(chosen.Item, "ICalloutVariable", "set_ToleranceMin", lower * QuickArgs.Mm);
            if (DimensionGeometry.ToleranceDigits(upper, lower) is { } digits)
                api.Call(chosen.Item, "ICalloutLengthVariable", "set_TolerancePrecision", digits);
        }

        if (spec.HoleFit is not null)
            api.Call(chosen.Item, "ICalloutVariable", "set_HoleFit", spec.HoleFit);
        if (spec.ShaftFit is not null)
            api.Call(chosen.Item, "ICalloutVariable", "set_ShaftFit", spec.ShaftFit);
        if (spec.Type == FitWithTolerance)
        {
            api.Call(chosen.Item, "ICalloutVariable", "set_FitDisplayStyle", FitLinear);
            api.Call(chosen.Item, "ICalloutVariable", "set_ShowParenthesis", true);
            api.Call(chosen.Item, "ICalloutLengthVariable", "set_TolerancePrecision", FitDigits);
        }

        return api.CallInt(chosen.Item, "ICalloutVariable", "get_ToleranceType") == spec.Type ? chosen.Name : null;
    }

    /// <summary>
    /// 选指向的对象：<paramref name="on"/> 几何编号或 <paramref name="dimension"/> 尺寸名，两个都没有就不选（不带引线）。选不上回执里的原因，选上回 null。
    /// </summary>
    public static string? SelectTarget(QuickCommandContext context, object drawing, object view, string title, GeometryRef? on, string? dimension)
    {
        var api = context.Api;
        api.ClearSelection(drawing);
        if (dimension is not null)
            return SelectDimension(api, drawing, view, dimension) ? null : $"选不上尺寸「{dimension}」";
        if (on is not { } item)
            return null;
        var scan = HoleScan.Scan(context, title, withLines: true, view: view, quiet: true);
        return DimensionEdit.SelectGeometry(api, scan, item, false) ? null : $"视图里选不上 {item}（编号不存在或对象已变，重读 strenua.check.svg）";
    }

    // 真机：Select3 选尺寸没有点选位置，基准符号的落点跑到图纸原点（X 还被锁死）；按文字位置 SelectByID2 才有。
    private static bool SelectDimension(SolidWorksApi api, object drawing, object view, string name)
    {
        var annotation = SheetLookup.Annotation(api, view, name);
        var position = api.CallDoubles(annotation, "IAnnotation", "GetPosition");
        return position.Length >= 2
            && api.Call(drawing, "IModelDoc2", "get_Extension") is { } extension
            && api.CallBool(extension, "IModelDocExtension", "SelectByID2", $"{name}@{DrawingSheet.Name(api, view)}", "DIMENSION", position[0], position[1], 0.0, false, 0, null, 0);
    }

    /// <summary>注解现在的位置（回执用，指向尺寸时 SolidWorks 不一定放在要的地方）；读不到回要的位置。</summary>
    public static SheetPoint Actual(SolidWorksApi api, object annotation, SheetPoint wanted)
    {
        var position = api.CallDoubles(annotation, "IAnnotation", "GetPosition");
        return position.Length >= 2 ? new SheetPoint(position[0], position[1]) : wanted;
    }

    /// <summary>尺寸公差写成一句；孔标注、没有公差、读不到为空串。</summary>
    public static string DescribeDimension(SolidWorksApi api, object display)
    {
        if (api.CallBool(display, "IDisplayDimension", "IsHoleCallout")
            || api.Call(display, "IDisplayDimension", "GetDimension2", 0) is not { } dimension
            || api.Call(dimension, "IDimension", "get_Tolerance") is not { } tolerance)
            return string.Empty;
        return ToleranceSpec.Describe(api.CallInt(tolerance, "IDimensionTolerance", "get_Type"),
            Convert.ToDouble(api.Call(tolerance, "IDimensionTolerance", "GetMaxValue")) / QuickArgs.Mm,
            Convert.ToDouble(api.Call(tolerance, "IDimensionTolerance", "GetMinValue")) / QuickArgs.Mm,
            api.CallString(tolerance, "IDimensionTolerance", "GetHoleFitValue"),
            api.CallString(tolerance, "IDimensionTolerance", "GetShaftFitValue"));
    }

    /// <summary>公差框一行一行的字（符号 公差 基准），框上、框下文字各占一行。</summary>
    public static string GtolText(SolidWorksApi api, object gtol)
    {
        var rows = new List<string>();
        void Add(string text)
        {
            if (text.Length > 0)
                rows.Add(text);
        }

        Add(api.CallString(gtol, "IGtol", "GetText", TextAbove));
        var count = api.CallInt(gtol, "IGtol", "GetFrameCount");
        for (short frame = 1; frame <= count; frame++)
        {
            var symbols = api.CallArray(gtol, "IGtol", "GetFrameSymbols2", frame).Select(item => item?.ToString() ?? string.Empty).ToList();
            var values = api.CallArray(gtol, "IGtol", "GetFrameValues", frame).Select(item => item?.ToString() ?? string.Empty).ToList();
            var diameter = api.CallArray(gtol, "IGtol", "GetFrameDiameterSymbols", frame).FirstOrDefault() is true;
            var symbol = symbols.Count > 0 ? symbols[0] : string.Empty;
            var modifier = symbols.Count > 1 ? symbols[1] : string.Empty;
            var cells = new[] { symbol, (diameter ? "Ø" : string.Empty) + (values.Count > 0 ? values[0] : string.Empty) + modifier }.Concat(values.Skip(2));
            Add(AnnotationRows.Readable(string.Join(" ", cells.Where(cell => cell.Length > 0))));
        }

        Add(api.CallString(gtol, "IGtol", "GetText", TextBelow));
        return string.Join("\n", rows);
    }

    /// <summary>注解的引线（图纸坐标）：每条从框到落点；<paramref name="tips"/> 是每条的落点。</summary>
    public static List<SheetSegment> Leaders(SolidWorksApi api, object annotation, out List<SheetPoint> tips)
    {
        var lines = new List<SheetSegment>();
        tips = [];
        var count = api.CallInt(annotation, "IAnnotation", "GetLeaderCount");
        for (var index = 0; index < count; index++)
        {
            var points = api.CallDoubles(annotation, "IAnnotation", "GetLeaderPointsAtIndex", index);
            for (var i = 0; i + 5 < points.Length; i += 3)
                lines.Add(new SheetSegment(points[i], points[i + 1], points[i + 3], points[i + 4]));
            // 真机：第一个点在框上，最后一个才是落点。
            if (points.Length >= 3)
                tips.Add(new SheetPoint(points[^3], points[^2]));
        }

        return lines;
    }

    /// <summary>公差框、基准符号指向的尺寸名；不指尺寸为 null。</summary>
    public static string? AttachedDimension(SolidWorksApi api, object annotation)
    {
        foreach (var entity in api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3"))
        {
            try
            {
                if (entity is not null && api.Call(entity, "IDisplayDimension", "GetAnnotation") is { } attached)
                    return api.CallString(attached, "IAnnotation", "GetName");
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
            {
                // 不是尺寸（边、顶点）：看下一个。
            }
        }

        return null;
    }

    /// <summary>删掉建到一半的注解。</summary>
    public static void Discard(SolidWorksApi api, object drawing, object annotation)
    {
        api.ClearSelection(drawing);
        if (api.CallBool(annotation, "IAnnotation", "Select3", false, null))
            api.Call(drawing, "IModelDoc2", "EditDelete");
        api.ClearSelection(drawing);
    }
}
