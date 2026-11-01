using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>插中心符号线的共用动作。</summary>
internal static class CenterMarkEdit
{
    internal static object? Insert(SolidWorksApi api, ScannedView scan, IReadOnlyList<int> edgeIndices, int style, bool slot)
    {
        api.ClearSelection(scan.Document);
        for (var i = 0; i < edgeIndices.Count; i++)
        {
            if (!api.CallBool(scan.View, "IView", "SelectEntity", scan.Edges[edgeIndices[i]], i > 0))
                return null;
        }

        return api.Call(scan.Document, "IDrawingDoc", "InsertCenterMark3", style, false, slot);
    }

    /// <summary>点名的孔：一个建单个，多个建线性组并打开连接线（<see cref="DimCenter"/>）。</summary>
    internal static object? InsertMarks(SolidWorksApi api, ScannedView scan, IReadOnlyList<int> edgeIndices)
    {
        var mark = Insert(api, scan, edgeIndices, edgeIndices.Count > 1 ? StyleLinearGroup : StyleSingle, slot: false);
        if (mark is not null && edgeIndices.Count > 1 && api.CallInt(mark, "ICenterMark", "get_Style") == StyleLinearGroup)
            api.Call(mark, "ICenterMark", "set_ConnectionLines", LinearConnectLines);
        return mark;
    }

    internal const int LinearConnectLines = 1;

    internal const int StyleSingle = 2;

    internal const int StyleLinearGroup = 3;
}
