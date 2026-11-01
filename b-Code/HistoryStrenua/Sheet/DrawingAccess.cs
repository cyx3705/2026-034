using System.Diagnostics;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>每条指令开头都要的：确认活动文档是工程图、当前页的视图、等用户点选视图。</summary>
internal static class DrawingAccess
{
    internal const int SelectDrawingView = 12;
    internal const int SelectSheet = 19;

    public static readonly TimeSpan SelectionTimeout = TimeSpan.FromSeconds(60);

    internal static object? SelectedView(SolidWorksApi api, object selection)
    {
        var count = api.CallInt(selection, "ISelectionMgr", "GetSelectedObjectCount2", -1);
        for (var index = 1; index <= count; index++)
        {
            var type = api.CallInt(selection, "ISelectionMgr", "GetSelectedObjectType3", index, -1);
            var view = type switch
            {
                SelectDrawingView => api.Call(selection, "ISelectionMgr", "GetSelectedObject6", index, -1),
                SelectSheet => null,
                _ => api.Call(selection, "ISelectionMgr", "GetSelectedObjectsDrawingView2", index, -1),
            };
            if (view is not null && api.CallInt(view, "IView", "get_Type") != SwConst.ViewSheet)
                return view;
        }

        return null;
    }

    /// <summary>工程图里当前选中的视图（点在视图里的任何东西都算），没选返回 null，不等（出图类「投影视图」「轴测图」用）。</summary>
    public static object? PreselectedView(SolidWorksApi api, object document)
        => api.Call(document, "IModelDoc2", "get_SelectionManager") is { } selection ? SelectedView(api, selection) : null;

    /// <summary>
    /// 当前选择里有视图就用它；没有就等用户去点。点在视图里的任何东西（边、尺寸）都算点了这个视图，
    /// 只点在图纸空白处不算。
    /// </summary>
    internal static object WaitForView(QuickCommandContext context, object document, string title)
    {
        var api = context.Api;
        var selection = api.Call(document, "IModelDoc2", "get_SelectionManager")
            ?? throw new QuickCommandException("SolidWorks 没有返回选择管理器。");
        if (SelectedView(api, selection) is { } preselected)
            return preselected;

        context.SetState("等待点选视图");
        context.Report($"{title}：请在 SolidWorks 里点一下要处理的视图（{SelectionTimeout.TotalSeconds:0} 秒内）。");
        var stopwatch = Stopwatch.StartNew();
        var picked = context.Session.Yield(() =>
        {
            while (stopwatch.Elapsed < SelectionTimeout)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                Thread.Sleep(250);
                if (SelectedView(api, selection) is { } found)
                    return found;
            }

            return null;
        });
        if (picked is not null)
            return picked;

        throw new QuickCommandException($"{SelectionTimeout.TotalSeconds:0} 秒内没有点选视图，{title}已放弃。");
    }

    /// <summary>视图引用的是哪个模型：文件路径 + 配置（1.8.1，外轮廓跨视图去重只比同一个模型的视图）。</summary>
    public static string ModelKey(SolidWorksApi api, object view)
        => api.CallString(view, "IView", "GetReferencedModelName").ToUpperInvariant()
           + "|" + api.CallString(view, "IView", "get_ReferencedConfiguration");

    /// <summary>
    /// 视图的朝向（1.8.1，判轴测图、外轮廓跨视图去重用）。比 <see cref="HoleScan.Scan"/> 轻得多：只问视图变换，不读边。
    /// </summary>
    public static ViewFrame Frame(QuickCommandContext context, object view)
        => new ViewGeometry(context.Api, view).Frame;

    /// <summary>
    /// 当前图纸页上的全部视图（不含图纸本身），按 SolidWorks 的视图顺序。
    /// <c>IDrawingDoc.GetFirstView</c> 返回的是当前图纸页，往后 <c>GetNextView</c> 只走这一页的视图。
    /// </summary>
    public static List<object> SheetViews(SolidWorksApi api, object document)
    {
        var views = new List<object>();
        var view = api.Call(document, "IDrawingDoc", "GetFirstView") is { } sheet
            ? api.Call(sheet, "IView", "GetNextView")
            : null;
        while (view is not null)
        {
            if (api.CallInt(view, "IView", "get_Type") != SwConst.ViewSheet)
                views.Add(view);
            view = api.Call(view, "IView", "GetNextView");
        }

        return views;
    }

    /// <summary>活动文档，必须是工程图。</summary>
    public static object ActiveDrawing(QuickCommandContext context)
    {
        var api = context.Api;
        var active = context.Session.ActiveDocument();
        var document = context.TargetDrawing ?? active
            ?? throw new QuickCommandException("SolidWorks 里没有打开的文档。请先打开工程图。");
        if (api.CallInt(document, "IModelDoc2", "GetType") != SwConst.DocumentDrawing)
            throw new QuickCommandException($"当前活动文档「{api.CallString(document, "IModelDoc2", "GetTitle")}」不是工程图。请切到工程图再按。");
        if (context.TargetDrawing is not null && !ReferenceEquals(active, document)
            && (active is null || api.CallString(active, "IModelDoc2", "GetTitle") != api.CallString(document, "IModelDoc2", "GetTitle")))
        {
            // REQ-DQ-06：右防护板真机在中心符号线之后活动窗口变为引用零件。
            // 只能激活本命令已经绑定的图；图被关闭/激活失败就停这步，不落到别的活动文档上。
            object?[] args = [api.CallString(document, "IModelDoc2", "GetTitle"), false, 1, 0];
            if (api.Call(context.Session.Application, "ISldWorks", "ActivateDoc3", args) is null)
                throw new QuickCommandException("原工程图无法重新激活，本步骤未执行。");
        }
        context.TargetDrawing = document;
        return document;
    }
}
