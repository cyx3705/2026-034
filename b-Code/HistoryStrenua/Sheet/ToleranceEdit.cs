using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>公差标注的共用动作（1.36.0）：写尺寸公差、选形位公差和基准符号指向的对象。</summary>
internal static class ToleranceEdit
{
    private const int FitWithTolerance = 8, FitLinear = 3, FitDigits = 3;

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
            return api.CallBool(SheetLookup.Annotation(api, view, dimension), "IAnnotation", "Select3", false, null) ? null : $"选不上尺寸「{dimension}」";
        if (on is not { } item)
            return null;
        var scan = HoleScan.Scan(context, title, withLines: true, view: view, quiet: true);
        return DimensionEdit.SelectGeometry(api, scan, item, false) ? null : $"视图里选不上 {item}（编号不存在或对象已变，重读 strenua.check.svg）";
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
