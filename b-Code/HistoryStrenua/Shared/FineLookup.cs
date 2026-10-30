using System.Runtime.InteropServices;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 细粒度指令按名字找对象（1.20.0）：当前图纸页上的视图（图纸页本身也算一个，名字就是图纸页名，放的是图纸上的注释），
/// 视图里的注解（<c>IAnnotation.GetName</c>，在一个视图里唯一，删掉之前不变）。
/// </summary>
internal static class FineLookup
{
    // swDrawingViewTypes_e
    public const int ViewSheet = 1;

    /// <summary>图纸页本身（<c>GetFirstView</c>）在前，后面是这一页的视图。</summary>
    public static List<object> Views(SolidWorksApi api, object drawing)
    {
        var views = new List<object>();
        if (api.Call(drawing, "IDrawingDoc", "GetFirstView") is { } sheet)
            views.Add(sheet);
        views.AddRange(HoleScan.SheetViews(api, drawing));
        return views;
    }

    /// <summary>按名字找视图，找不到抛错并列出这一页有哪些。</summary>
    public static object View(SolidWorksApi api, object drawing, string name)
    {
        var views = Views(api, drawing);
        return views.FirstOrDefault(view => DrawingSheet.Name(api, view) == name)
            ?? throw new QuickCommandException($"当前图纸页上没有视图「{name}」。有：{string.Join("、", views.Select(view => DrawingSheet.Name(api, view)))}。");
    }

    public static bool IsSheet(SolidWorksApi api, object view) => api.CallInt(view, "IView", "get_Type") == ViewSheet;

    /// <summary>视图里的注解：名字 → <c>IAnnotation</c>（同名只留第一个）。</summary>
    public static List<(string Name, object Annotation)> Annotations(SolidWorksApi api, object view)
    {
        var result = new List<(string, object)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var annotation in api.CallArray(view, "IView", "GetAnnotations"))
        {
            if (annotation is null)
                continue;
            string name;
            try
            {
                name = api.CallString(annotation, "IAnnotation", "GetName");
            }
            catch (COMException)
            {
                continue;
            }

            if (seen.Add(name))
                result.Add((name, annotation));
        }

        return result;
    }

    /// <summary>按名字找注解，找不到抛错。</summary>
    public static object Annotation(SolidWorksApi api, object view, string name)
        => Annotations(api, view).FirstOrDefault(item => item.Name == name).Annotation
           ?? throw new QuickCommandException($"视图「{DrawingSheet.Name(api, view)}」里没有注解「{name}」（先用 strenua.dim.list 查名字）。");

    /// <summary>注解位置（图纸坐标，米）；中心符号线、销钉符号这类读不到或不在图纸坐标的回 null。</summary>
    public static SheetPoint? Position(SolidWorksApi api, object annotation, int type)
    {
        if (!Movable(type))
            return null;
        var position = api.CallDoubles(annotation, "IAnnotation", "GetPosition");
        return position.Length >= 2 ? new SheetPoint(position[0], position[1]) : null;
    }

    /// <summary>
    /// 能按位置挪的注解类型：中心符号线（13，<c>GetPosition</c> 是模型坐标）、销钉符号（10，没有位置）、中心线（15，位置为空）跟着几何走，不挪。
    /// </summary>
    public static bool Movable(int type) => type is not (10 or 13 or 15);
}
