using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「改尺寸」<c>strenua.dim.edit</c>（1.20.0）：按名字改一个尺寸的前缀、后缀、对称公差。不改尺寸值、不改附着。
/// </summary>
/// <remarks>
/// <para>前后缀走 <c>IDisplayDimension.SetText</c>（前缀 1、后缀 2，同阵列标法与「(含对面)」）。真机上 <c>GetText(2)</c> 读回是空的，
/// 所以回执给的是显示数据里读回的整段字，看后缀在不在要看那段字。</para>
/// <para>对称公差同销孔间 ±0.02（<see cref="DimensionScan.SetSymmetric"/>：工程图从动尺寸只有 <c>SetValues</c> 写得进去）；孔标注的公差在孔标注变量里，不在这里改。</para>
/// </remarks>
internal static class DimEdit
{
    public static QuickCommand Command { get; } = new(
        Key: "dim-edit",
        CommandName: StrenuaIdentity.Domain + ".dim.edit",
        Title: "改尺寸",
        Summary: "按名字改一个尺寸的前缀、后缀或对称公差（±mm，none 去掉）；不改尺寸值与附着，读回显示文字。",
        Usage: "填「视图」「名字」，再填要改的：前缀（如 2 x C）、后缀（如 (含对面)）、公差（对称 ±，写 0.02）；空着的不改，写 none 清掉。不改尺寸值，也不改连着哪条边。孔标注的公差在孔标注变量里，这里不改。改完回执给出图上现在显示的字。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            QuickArgs.View("视图名（视图列表的 name 列）"),
            new QuickParameter("name", "名字", "尺寸名（标注列表的 name 列），如 D1", true),
            new QuickParameter("prefix", "前缀", "前缀文字，如 2 x C；none 清掉；省略不改", false, 2),
            new QuickParameter("suffix", "后缀", "后缀文字，如 (含对面)；none 清掉；省略不改", false, 2),
            new QuickParameter("tol", "公差", "对称公差 ±mm，如 0.02；none 去掉公差；省略不改。孔标注不支持", false, 2),
        ],
        Example: StrenuaIdentity.Domain + ".dim.edit view=工程图视图1 name=D1 tol=0.02",
        Bind: Bind,
        SelfReport: true);

    private const int ToleranceNone = 0;

    private static Func<QuickCommandContext, QuickOutcome> Bind(CommandContext arguments)
    {
        var view = QuickArgs.Name(arguments, "view");
        var name = QuickArgs.Name(arguments, "name");
        var prefix = QuickArgs.Text(arguments, "prefix");
        var suffix = QuickArgs.Text(arguments, "suffix");
        var tolText = QuickArgs.Optional(arguments, "tol");
        var tolerance = tolText is not null;
        var plusMinus = tolText is null ? null : QuickArgs.Tolerance(tolText);
        if (prefix is null && suffix is null && !tolerance)
            throw new QuickArgumentException("没有要改的：给 prefix=、suffix= 或 tol= 至少一个。");
        return context => Run(context, view, name, prefix, suffix, tolerance, plusMinus);
    }

    // tolerance：要改公差；plusMinus：对称公差（mm），tolerance 为 true 而它是 null 表示去掉公差。
    private static QuickOutcome Run(QuickCommandContext context, string viewName, string name, string? prefix, string? suffix, bool tolerance, double? plusMinus)
    {
        var api = context.Api;
        var drawing = DrawingAccess.ActiveDrawing(context);
        var view = SheetLookup.View(api, drawing, viewName);
        var annotation = SheetLookup.Annotation(api, view, name);
        if (api.CallInt(annotation, "IAnnotation", "GetType") != SwConst.AnnotationDimension
            || api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is not { } display)
            return QuickOutcome.Fail($"改尺寸：「{name}」不是尺寸，只能改尺寸的前后缀与公差。");

        context.SetState("改尺寸");
        var done = new List<string>();
        var failed = new List<string>();
        if (prefix is not null)
        {
            api.Call(display, "IDisplayDimension", "SetText", SwConst.TextPrefix, prefix);
            done.Add(prefix.Length == 0 ? "清掉前缀" : $"前缀「{prefix}」");
        }

        if (suffix is not null)
        {
            api.Call(display, "IDisplayDimension", "SetText", SwConst.TextSuffix, suffix);
            done.Add(suffix.Length == 0 ? "清掉后缀" : $"后缀「{suffix}」");
        }

        if (tolerance)
        {
            if (api.CallBool(display, "IDisplayDimension", "IsHoleCallout"))
                failed.Add("孔标注的公差在孔标注变量里，这里不改");
            else if (plusMinus is { } mm)
            {
                if (DimensionScan.SetSymmetric(api, display, mm * QuickArgs.Mm))
                    done.Add($"公差 ±{mm.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                else
                    failed.Add($"公差 ±{mm.ToString(System.Globalization.CultureInfo.InvariantCulture)} 读回对不上");
            }
            else if (api.Call(display, "IDisplayDimension", "GetDimension2", 0) is { } dimension
                     && api.Call(dimension, "IDimension", "get_Tolerance") is { } tol)
            {
                api.Call(tol, "IDimensionTolerance", "set_Type", ToleranceNone);
                if (api.CallInt(tol, "IDimensionTolerance", "get_Type") == ToleranceNone)
                    done.Add("去掉公差");
                else
                    failed.Add("公差没去掉");
            }
            else
                failed.Add("读不到公差");
        }

        api.Call(drawing, "IModelDoc2", "EditRebuild3");
        api.Redraw(drawing);
        var shown = AnnotationRows.Shown(api, annotation);
        var message = $"改尺寸：视图「{viewName}」的「{name}」" + (done.Count > 0 ? string.Join("、", done) : "没改成")
            + (shown.Length > 0 ? $"，现在显示「{shown}」" : string.Empty) + "。"
            + (failed.Count > 0 ? Environment.NewLine + "· " + string.Join("；", failed) + "。" : string.Empty);
        return failed.Count == 0 ? QuickOutcome.Ok(message) : QuickOutcome.Fail(message);
    }
}
