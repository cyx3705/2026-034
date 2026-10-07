using System.Runtime.InteropServices;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 「重新标」共用的前半段：删掉一批注解，删完回读，还有就再删。
/// </summary>
/// <remarks>
/// 真机上第一遍删除偶有漏删（中心符号线实测），而 <c>EditDelete</c> 没有返回值，
/// 删掉几个只能靠回读数出来。所以每遍删完都重新读一次视图，最多 <see cref="Passes"/> 遍。
/// <para>
/// 给了视图时（1.14.1）删完把视图里其余尺寸放回原处：真机上删掉里层的尺寸，SolidWorks 会把外层的尺寸往里收一层
/// （XJ05A-01 侧视图：删掉孔位「5」，外轮廓「10」从第二层掉到第一层，重标的「5」又落在第一层，两个叠在一起）。
/// </para>
/// </remarks>
internal static class AnnotationEraser
{
    public const int Passes = 3;

    /// <summary>RPC_E_DISCONNECTED：对象在 SolidWorks 那边已经不在了。</summary>
    private const int Disconnected = unchecked((int)0x80010108);

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="document">活动工程图。</param>
    /// <param name="readObsolete">现读视图，返回此刻还该删的注解（<c>IAnnotation</c>）。</param>
    /// <param name="view">给了就在删完后把这个视图里其余尺寸放回删之前的位置（<c>IView</c>）。</param>
    /// <returns>删掉的个数与最后仍在的个数。</returns>
    public static (int Removed, int Leftover) Erase(QuickCommandContext context, object document, Func<IReadOnlyList<object>> readObsolete, object? view = null)
    {
        var api = context.Api;
        var obsolete = readObsolete();
        var initial = obsolete.Count;
        var doomed = obsolete.Select(annotation => Name(api, annotation)).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, double[]> places = view is null || initial == 0
            ? new Dictionary<string, double[]>()
            : DimensionPlaces(api, view).Where(place => !doomed.Contains(place.Key)).ToDictionary(place => place.Key, place => place.Value.Position);
        for (var pass = 0; pass < Passes && obsolete.Count > 0; pass++)
        {
            foreach (var annotation in obsolete)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                api.Call(document, "IModelDoc2", "ClearSelection2", true);
                try
                {
                    if (api.CallBool(annotation, "IAnnotation", "Select3", false, null))
                        api.Call(document, "IModelDoc2", "EditDelete");
                }
                catch (COMException ex) when (ex.HResult == Disconnected)
                {
                    // 已经跟着别的一起没了：删坐标尺寸组里的一个，SolidWorks 会连带删掉 / 重建同组其余的（1.7.0 真机）。
                    // 删没删干净由下面回读决定。
                }
            }

            obsolete = readObsolete();
        }

        if (view is not null && places.Count > 0)
            Restore(api, view, places);
        return (initial - obsolete.Count, obsolete.Count);
    }

    /// <summary>把视图里还在的尺寸挪回 <paramref name="places"/> 记下的位置（挪动不到 0.01 mm 的不动）。</summary>
    private static void Restore(SolidWorksApi api, object view, IReadOnlyDictionary<string, double[]> places)
    {
        foreach (var (name, (annotation, position)) in DimensionPlaces(api, view))
        {
            if (!places.TryGetValue(name, out var before) || before.Length < 3 || position.Length < 3
                || Math.Abs(before[0] - position[0]) + Math.Abs(before[1] - position[1]) < 1e-5)
                continue;
            try
            {
                api.Call(annotation, "IAnnotation", "SetPosition2", before[0], before[1], before[2]);
            }
            catch (COMException ex) when (ex.HResult == Disconnected)
            {
                // 跟着删掉的组一起重建了：放不回去就算了。
            }
        }
    }

    /// <summary>视图里每个尺寸：注解名 →（注解、位置）。</summary>
    private static Dictionary<string, (object Annotation, double[] Position)> DimensionPlaces(SolidWorksApi api, object view)
    {
        var places = new Dictionary<string, (object, double[])>(StringComparer.Ordinal);
        var dimension = api.Call(view, "IView", "GetFirstDisplayDimension5");
        while (dimension is not null)
        {
            if (api.Call(dimension, "IDisplayDimension", "GetAnnotation") is { } annotation)
                places[Name(api, annotation)] = (annotation, api.CallDoubles(annotation, "IAnnotation", "GetPosition"));
            dimension = api.Call(dimension, "IDisplayDimension", "GetNext5");
        }

        return places;
    }

    private static string Name(SolidWorksApi api, object annotation)
    {
        try
        {
            return api.CallString(annotation, "IAnnotation", "GetName");
        }
        catch (COMException ex) when (ex.HResult == Disconnected)
        {
            return string.Empty;
        }
    }
}
