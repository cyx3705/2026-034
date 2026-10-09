using System.Globalization;
using System.Text.RegularExpressions;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「两位小数」（1.16.0，检查类，用户定：单独一个按钮）：当前图纸页全部视图里，尺寸数字显示成小数点后两位（及以上）的，
/// 在 SolidWorks 里选中（高亮）并弹窗列出——这种一般不允许存在（多半是量到了不该量的地方，或精度设错了）。
/// </summary>
/// <remarks>
/// <para>看的是图上实际显示的字（<c>IDisplayData.GetTextAtIndex</c>），不是尺寸值：12.5 显示成「12.50」同样算。
/// 一个尺寸的显示数据里有好几段字（前缀、公差……），取数值与尺寸值对得上的那一段当主数字；±0.02 这类公差不算。</para>
/// <para>孔标注不查（规格里的小数是孔的规格，不是尺寸精度）。只读，只改选择。弹窗同「未标尺寸」（<see cref="DimensionCheck.Popup"/>）。</para>
/// </remarks>
internal static class DecimalCheck
{
    public static QuickCommand Command { get; } = new(
        Key: "check-decimal",
        CommandName: StrenuaIdentity.Domain + ".check.decimal",
        Title: "两位小数",
        Summary: "当前图纸页全部视图里，尺寸数字显示成小数点后两位及以上的选中（高亮）并弹窗警告，不改图；孔标注、公差不算。",
        Usage: "不用点视图：当前图纸页上全部视图（含轴测图）的尺寸逐个看图上显示的数字，小数点后有两位及以上的（如「12.81」「12.50」）一般不允许存在——在 SolidWorks 里选中（高亮）它们并弹窗列出，请自己改精度或改标法。孔标注的规格、±0.02 这类公差不算。不加、不删、不改任何尺寸。",
        Run: Run);

    // swDimensionType_e
    private const int AngularDimension = 3;

    private static readonly Regex Number = new(@"(?<![\d.,])\d+[.,](?<fraction>\d+)(?![\d])", RegexOptions.Compiled);

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var document = HoleScan.ActiveDrawing(context);
        var views = HoleScan.SheetViews(api, document);
        if (views.Count == 0)
            return QuickOutcome.Ok("当前图纸页上没有视图，没有可检查的。");

        context.SetState("检查");
        api.Call(document, "IModelDoc2", "ClearSelection2", true);
        var lines = new List<string>();
        var found = 0;
        var total = 0;
        var unread = 0;
        foreach (var view in views)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var viewName = api.CallString(view, "IView", "get_Name");
            var here = new List<string>();
            var display = api.Call(view, "IView", "GetFirstDisplayDimension5");
            while (display is not null)
            {
                if (!api.CallBool(display, "IDisplayDimension", "IsHoleCallout")
                    && api.Call(display, "IDisplayDimension", "GetAnnotation") is { } annotation)
                {
                    total++;
                    switch (Shown(api, display, annotation))
                    {
                        case null:
                            unread++;
                            break;
                        case { } text when Decimals(text) >= 2:
                            here.Add(text);
                            api.CallBool(annotation, "IAnnotation", "Select3", true, null);
                            break;
                    }
                }

                display = api.Call(display, "IDisplayDimension", "GetNext5");
            }

            if (here.Count == 0)
                continue;
            found += here.Count;
            lines.Add($"视图「{viewName}」：{string.Join("、", here.Select(text => $"「{text}」"))}");
        }

        api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        var tail = unread > 0 ? $"（{unread} 个读不出显示的数字，没查）" : string.Empty;
        if (found == 0)
            return QuickOutcome.Ok($"两位小数：当前图纸页 {total} 个尺寸没有显示成两位小数的{tail}。");
        var head = $"两位小数：当前图纸页 {total} 个尺寸里有 {found} 个显示成小数点后两位及以上，一般不允许，已在 SolidWorks 里选中（高亮）{tail}。";
        DimensionCheck.Popup(context, "两位小数警告", head, lines);
        return QuickOutcome.Ok(head + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }

    /// <summary>
    /// 图上显示的主数字（原样的字）：显示数据里各段字中，数值与尺寸值（线性 mm、角度 °）对得上的那个数；
    /// 一个带小数的都对不上时取第一个整数也对得上的（没有小数）；读不出返回 null。
    /// </summary>
    private static string? Shown(SolidWorksApi api, object display, object annotation)
    {
        try
        {
            if (api.Call(display, "IDisplayDimension", "GetDimension2", 0) is not { } dimension
                || api.Call(annotation, "IAnnotation", "GetDisplayData") is not { } data)
                return null;
            var value = Math.Abs(Convert.ToDouble(api.Call(dimension, "IDimension", "get_SystemValue")));
            var expected = api.CallInt(display, "IDisplayDimension", "get_Type2") == AngularDimension ? value * 180 / Math.PI : value * 1000;
            var count = api.CallInt(data, "IDisplayData", "GetTextCount");
            var texts = Enumerable.Range(0, count).Select(i => api.CallString(data, "IDisplayData", "GetTextAtIndex", i)).ToList();
            return MainNumber(texts, expected);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
        {
            return null;
        }
    }

    /// <summary>
    /// 几段显示的字里与 <paramref name="expected"/> 对得上的那个数（按它自己的小数位四舍五入后相等）；带小数的没有就找整数；都没有返回空串（当作没小数）。
    /// </summary>
    internal static string? MainNumber(IReadOnlyList<string> texts, double expected)
    {
        foreach (var text in texts)
        {
            foreach (Match match in Number.Matches(text))
            {
                var decimals = match.Groups["fraction"].Value.Length;
                if (double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var shown)
                    && Math.Abs(shown - Math.Round(expected, decimals)) < 1e-9 + Math.Pow(10, -decimals) / 2)
                    return match.Value;
            }
        }

        return texts.Count > 0 ? string.Empty : null;
    }

    /// <summary>数字的小数位数（没有小数点为 0）。</summary>
    internal static int Decimals(string number)
    {
        var at = number.IndexOfAny(['.', ',']);
        return at < 0 ? 0 : number.Length - at - 1;
    }
}
