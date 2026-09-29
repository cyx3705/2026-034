namespace HistoryStrenua;

/// <summary>
/// 「重新标」共用的前半段：删掉一批注解，删完回读，还有就再删。
/// </summary>
/// <remarks>
/// 真机上第一遍删除偶有漏删（中心符号线实测），而 <c>EditDelete</c> 没有返回值，
/// 删掉几个只能靠回读数出来。所以每遍删完都重新读一次视图，最多 <see cref="Passes"/> 遍。
/// </remarks>
internal static class AnnotationEraser
{
    public const int Passes = 3;

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="document">活动工程图。</param>
    /// <param name="readObsolete">现读视图，返回此刻还该删的注解（<c>IAnnotation</c>）。</param>
    /// <returns>删掉的个数与最后仍在的个数。</returns>
    public static (int Removed, int Leftover) Erase(QuickCommandContext context, object document, Func<IReadOnlyList<object>> readObsolete)
    {
        var api = context.Api;
        var obsolete = readObsolete();
        var initial = obsolete.Count;
        for (var pass = 0; pass < Passes && obsolete.Count > 0; pass++)
        {
            foreach (var annotation in obsolete)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                api.Call(document, "IModelDoc2", "ClearSelection2", true);
                if (api.CallBool(annotation, "IAnnotation", "Select3", false, null))
                    api.Call(document, "IModelDoc2", "EditDelete");
            }

            obsolete = readObsolete();
        }

        return (initial - obsolete.Count, obsolete.Count);
    }
}
