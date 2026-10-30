using System.Runtime.InteropServices;

namespace HistoryStrenua;

/// <summary>
/// 每一步回执（1.21.0，用户定：AI 经 MCP 调一条改图的指令，回执里带本步改了什么的 SVG；改得太多就降成截图）。
/// </summary>
/// <remarks>
/// <para>做法：执行前读一遍图（不扫几何，只读视图外框、比例与全部注解），执行后再读一遍，按 <see cref="SheetDelta.Diff"/> 比出差异；
/// 只把差异里要几何或要尺寸两头的视图重新扫，再按 <see cref="DrawingSvg.Budgeted"/> 的预算写——完整、几何 + 精简标注、只有标注，都超 1 万字就回整页截图。</para>
/// <para>只对 MCP 来的调用（来源带「MCP」）做：页面上人点按钮看得见图，不用多花前后两遍读图的时间。只读的指令（列表、检查）不做。</para>
/// <para>反馈本身出错不影响指令的结论，只在回执里说一句。</para>
/// </remarks>
internal static class StepFeedback
{
    // swDocumentTypes_e
    private const int DocumentDrawing = 3;

    /// <summary>这次调用要不要带反馈：只看来源是不是 MCP。</summary>
    public static bool Wanted(string? source) => source?.Contains("MCP", StringComparison.OrdinalIgnoreCase) == true;

    public static Func<QuickCommandContext, QuickOutcome> Wrap(Func<QuickCommandContext, QuickOutcome> run)
        => context =>
        {
            var before = TryRead(context, Drawing(context));
            var outcome = run(context);
            try
            {
                return Attach(context, before, outcome);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or QuickCommandException or IOException)
            {
                return outcome with { Message = outcome.Message + Environment.NewLine + "· 本步反馈没读出来：" + ex.Message };
            }
        };

    private static QuickOutcome Attach(QuickCommandContext context, SvgSheet? before, QuickOutcome outcome)
    {
        if (Drawing(context) is not { } drawing)
            return outcome;
        context.SetState("读本步改动");
        var after = DrawingSvg.Read(context, drawing, scan: _ => false);
        var diff = SheetDelta.Diff(before, after);
        if (diff.Empty)
            return outcome with { Message = outcome.Message + Environment.NewLine + "· 本步反馈：图上没有变化。" };
        var scanned = diff.Scan.Count == 0
            ? new Dictionary<string, SvgView>()
            : DrawingSvg.Read(context, drawing, scan: diff.Scan.Contains).Views
                .Where(view => diff.Scan.Contains(view.Name))
                .ToDictionary(view => view.Name, StringComparer.Ordinal);
        var delta = SheetDelta.Fill(after, diff, scanned);
        var (svg, how) = DrawingSvg.Budgeted(context, drawing, delta);
        var message = outcome.Message + Environment.NewLine + $"· 本步反馈：{diff.Summary}；{how}。";
        // 接在文字后面、不放进数据（同一份不占两遍，见 DrawingSvg.Budgeted）。
        return outcome with { Message = svg is null ? message : message + Environment.NewLine + svg };
    }

    /// <summary>指令绑定的那张图，没有就当前活动文档；不是工程图返回 null（一键出图前活动的是零件）。</summary>
    private static object? Drawing(QuickCommandContext context)
    {
        var document = context.TargetDrawing ?? context.Session.ActiveDocument();
        return document is not null && context.Api.CallInt(document, "IModelDoc2", "GetType") == DocumentDrawing ? document : null;
    }

    private static SvgSheet? TryRead(QuickCommandContext context, object? drawing)
    {
        if (drawing is null)
            return null;
        try
        {
            return DrawingSvg.Read(context, drawing, scan: _ => false);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or QuickCommandException)
        {
            return null;
        }
    }
}
