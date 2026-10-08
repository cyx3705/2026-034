using System.Runtime.InteropServices;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>读出来的一个尺寸：SolidWorks 对象与它的几何。</summary>
/// <param name="Display"><c>IDisplayDimension</c>。</param>
/// <param name="Annotation"><c>IAnnotation</c>。</param>
/// <param name="Geometry">几何（<see cref="ViewDimension.Index"/> 是在这批里的下标）。</param>
internal sealed record ScannedDimension(object Display, object Annotation, ViewDimension Geometry);

/// <summary>
/// 把视图里的全部尺寸读成 <see cref="ViewDimension"/>（1.8.0）：每个附着对象试着当圆边、模型直边、视图草图直线读，
/// 不按附着类型码筛（真机上类型码会报错、过后读成空数组，见 <see cref="HoleCallout.AttachedCircleCenters"/>）。
/// </summary>
internal static class DimensionScan
{
    // swDimensionTextParts_e
    public const int TextPrefix = 1;
    public const int TextSuffix = 2;

    public static List<ScannedDimension> Read(SolidWorksApi api, ScannedView scan)
    {
        var result = new List<ScannedDimension>();
        var display = api.Call(scan.View, "IView", "GetFirstDisplayDimension5");
        while (display is not null)
        {
            if (api.Call(display, "IDisplayDimension", "GetAnnotation") is { } annotation)
                result.Add(new ScannedDimension(display, annotation, ReadOne(api, scan, display, annotation, result.Count)));
            display = api.Call(display, "IDisplayDimension", "GetNext5");
        }

        return result;
    }

    private static ViewDimension ReadOne(SolidWorksApi api, ScannedView scan, object display, object annotation, int index)
    {
        var callout = api.CallBool(display, "IDisplayDimension", "IsHoleCallout");
        var type = api.CallInt(display, "IDisplayDimension", "get_Type2");
        var value = double.NaN;
        var tolerance = 0;
        // 公差与前后缀只有线性尺寸用得上（销孔间 ±0.02、阵列前缀、「(公差仅对销孔)」）；坐标尺寸一组几十个，少问几次快得多。
        var linear = !callout && HolePositionPlanner.IsLinear(type);
        if (!callout && api.Call(display, "IDisplayDimension", "GetDimension2", 0) is { } dimension)
        {
            value = Convert.ToDouble(api.Call(dimension, "IDimension", "get_SystemValue"));
            if (linear && api.Call(dimension, "IDimension", "get_Tolerance") is { } dimensionTolerance)
                tolerance = api.CallInt(dimensionTolerance, "IDimensionTolerance", "get_Type");
        }

        var anchors = new List<DimensionAnchor>();
        foreach (var entity in api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3"))
        {
            if (entity is not null && Anchor(api, scan, entity) is { } anchor)
                anchors.Add(anchor);
        }

        return new ViewDimension(
            index, type, callout, value, anchors,
            linear ? api.CallString(display, "IDisplayDimension", "GetText", TextPrefix) : string.Empty,
            linear ? api.CallString(display, "IDisplayDimension", "GetText", TextSuffix) : string.Empty,
            tolerance);
    }

    /// <summary>一个附着对象：圆边 → 圆心；模型直边、视图草图直线 → 直线；别的（或已失效）不出。</summary>
    private static DimensionAnchor? Anchor(SolidWorksApi api, ScannedView scan, object entity)
    {
        if (scan.Geometry.TryReadCircleCenter(entity) is { } center)
            return DimensionAnchor.Center(center);
        try
        {
            if (scan.Geometry.TryReadLine(entity) is { } line)
                return DimensionAnchor.Line(line, modelLine: true);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or System.Reflection.TargetException)
        {
            // 不是模型边（视图草图线之类）：往下试。
        }

        return HolePosition.SketchLineOnSheet(api, scan, entity) is { } sketch ? DimensionAnchor.Line(sketch, modelLine: false) : null;
    }

    /// <summary>
    /// 给线性尺寸设对称公差 ±<paramref name="plusMinus"/>（模型长度，米），读回核对类型。
    /// </summary>
    public static bool SetSymmetric(SolidWorksApi api, object display, double plusMinus)
    {
        if (api.Call(display, "IDisplayDimension", "GetDimension2", 0) is not { } dimension
            || api.Call(dimension, "IDimension", "get_Tolerance") is not { } tolerance)
            return false;
        api.Call(tolerance, "IDimensionTolerance", "set_Type", DimensionGeometry.ToleranceSymmetric);
        // 真机（SW 2025 SP5，底板 View2）：工程图里加的从动尺寸 SetValues2 不论哪种配置选项都返回 false、值仍是 0（显示「±0」），
        // SetValues 才写得进去。
        if (!api.CallBool(tolerance, "IDimensionTolerance", "SetValues", -plusMinus, plusMinus))
            api.Call(tolerance, "IDimensionTolerance", "SetValues2", -plusMinus, plusMinus, 0, null);
        return api.CallInt(tolerance, "IDimensionTolerance", "get_Type") == DimensionGeometry.ToleranceSymmetric
               && Math.Abs(Convert.ToDouble(api.Call(tolerance, "IDimensionTolerance", "GetMaxValue")) - plusMinus) < 1e-9;
    }
}
