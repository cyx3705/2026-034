using System.Globalization;
using System.Runtime.InteropServices;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「视图列表」<c>strenua.view.list</c>（1.20.0）：当前图纸页本身与它上面每个视图一行——名字、种类、父视图、比例、外框、中心。
/// 是 <c>strenua.view.move</c> 与 <c>strenua.dim.*</c> 的寻址表：视图名就是它们的 <c>view=</c>。只读。
/// </summary>
internal static class ViewList
{
    public static QuickCommand Command { get; } = new(
        Key: "view-list",
        CommandName: StrenuaIdentity.Domain + ".view.list",
        Title: "视图列表",
        Summary: "列出当前图纸页及其视图：名字、种类、父视图、比例、外框与中心（图纸 mm，左下角为原点）；视图名供视图、标注类的「视图」填。",
        Usage: "不用填：列出当前图纸页本身（第一行，外框 = 纸张）和上面每个视图——名字、种类（模型 / 投影 / 剖视……，轴测图另标）、父视图、引用的模型、比例、外框与中心，单位图纸 mm、左下角为原点，和图纸截图对得上。视图名就是「挪视图」与标注类各按钮的「视图」框要填的。只读，不改图。",
        Run: QuickCommand.Unbound,
        Parameters: [],
        Readonly: true,
        Bind: _ => Run);

    // swDrawingViewTypes_e
    private static readonly IReadOnlyDictionary<int, string> Kinds = new Dictionary<int, string>
    {
        [1] = "图纸页",
        [2] = "剖视",
        [3] = "局部",
        [4] = "投影",
        [5] = "辅助",
        [6] = "标准",
        [7] = "模型",
        [8] = "相对",
        [9] = "断开",
        [10] = "交替位置",
    };

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var drawing = HoleScan.ActiveDrawing(context);
        var rows = new List<IReadOnlyDictionary<string, string>>();
        var lines = new List<string>();
        foreach (var view in SheetLookup.Views(api, drawing))
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var row = Row(context, drawing, view);
            rows.Add(row);
            lines.Add(Line(row));
        }

        var title = api.CallString(drawing, "IModelDoc2", "GetTitle");
        var frame = DrawingSheet.FrameRect(api, drawing) is { } box
            ? $"；图框内可放区 ({QuickArgs.Format(box.Left)}, {QuickArgs.Format(box.Bottom)})–({QuickArgs.Format(box.Right)}, {QuickArgs.Format(box.Top)})"
            : string.Empty;
        return QuickOutcome.Ok($"视图列表：「{title}」当前图纸页 {rows.Count - 1} 个视图（图纸 mm，左下角为原点{frame}）。"
            + Environment.NewLine + string.Join(Environment.NewLine, lines), rows);
    }

    /// <summary>一个视图的行：图纸页给纸张大小；别的给外框、中心（<c>IView.Position</c>）、比例、父视图、引用模型。</summary>
    private static Dictionary<string, string> Row(QuickCommandContext context, object drawing, object view)
    {
        var api = context.Api;
        var type = api.CallInt(view, "IView", "get_Type");
        var row = new Dictionary<string, string>
        {
            ["name"] = DrawingSheet.Name(api, view),
            ["kind"] = Kinds.TryGetValue(type, out var kind) ? kind : $"类型 {type}",
            ["parent"] = string.Empty,
            ["model"] = string.Empty,
            ["scale"] = string.Empty,
        };
        if (type == SheetLookup.ViewSheet)
        {
            var sheet = api.Call(drawing, "IDrawingDoc", "GetCurrentSheet");
            var properties = sheet is null ? [] : api.CallDoubles(sheet, "ISheet", "GetProperties2");
            var (width, height) = properties.Length > 6 ? (properties[5], properties[6]) : (0.0, 0.0);
            if (properties.Length > 3 && properties[2] > 0 && properties[3] > 0)
                row["scale"] = DrawingPlanner.ScaleText(properties[2] / properties[3]);
            Rect(row, new SheetRect(0, 0, width, height));
            Center(row, new SheetPoint(width / 2, height / 2));
            return row;
        }

        if (api.Call(view, "IView", "GetBaseView") is { } parent)
            row["parent"] = DrawingSheet.Name(api, parent);
        if (api.Call(view, "IView", "get_ReferencedDocument") is not null)
        {
            row["model"] = Path.GetFileName(api.CallString(view, "IView", "GetReferencedModelName"));
            try
            {
                if (HoleScan.Frame(context, view).Axonometric)
                    row["kind"] += "·轴测";
            }
            catch (Exception ex) when (ex is COMException or QuickCommandException or InvalidCastException)
            {
                // 读不到视图变换（空视图之类）：不标轴测。
            }
        }

        row["scale"] = DrawingPlanner.ScaleText(Convert.ToDouble(api.Call(view, "IView", "get_ScaleDecimal"), CultureInfo.InvariantCulture));
        Rect(row, DrawingSheet.Outline(api, view));
        Center(row, MoveView.Position(api, view));
        return row;
    }

    private static void Rect(Dictionary<string, string> row, SheetRect rect)
    {
        row["left"] = QuickArgs.Format(rect.Left);
        row["bottom"] = QuickArgs.Format(rect.Bottom);
        row["right"] = QuickArgs.Format(rect.Right);
        row["top"] = QuickArgs.Format(rect.Top);
    }

    private static void Center(Dictionary<string, string> row, SheetPoint center)
    {
        row["x"] = QuickArgs.Format(center.X);
        row["y"] = QuickArgs.Format(center.Y);
    }

    /// <summary>回执一行，如「工程图视图2  投影  父=工程图视图1  1:2  中心 (120, 80)  外框 (60, 40)–(180, 120)」。</summary>
    internal static string Line(IReadOnlyDictionary<string, string> row)
    {
        var parts = new List<string> { row["name"], row["kind"] };
        if (row["parent"].Length > 0)
            parts.Add("父=" + row["parent"]);
        if (row["model"].Length > 0)
            parts.Add(row["model"]);
        if (row["scale"].Length > 0)
            parts.Add(row["scale"]);
        parts.Add($"中心 ({row["x"]}, {row["y"]})");
        parts.Add($"外框 ({row["left"]}, {row["bottom"]})–({row["right"]}, {row["top"]})");
        return "· " + string.Join("  ", parts);
    }
}
