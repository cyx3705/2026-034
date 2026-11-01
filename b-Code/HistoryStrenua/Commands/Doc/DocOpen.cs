using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 文档类（1.25.0，用户定：AI 逐张修图要自己开、切、存、关）：列表、打开、切换。这几条都是 AI 或人明确点名才动，别的指令仍不替用户开关文档。
/// </summary>
internal static class DocOpen
{
    public static QuickCommand ListCommand { get; } = new(
        Key: "doc-list",
        CommandName: StrenuaIdentity.Domain + ".doc.list",
        Title: "文档列表",
        Summary: "列出 SolidWorks 里开着窗口的文档：名字、路径、种类、有没有未保存的改动、是不是当前活动文档。",
        Usage: "列出开着窗口的文档（总装带着加载、没有窗口的零件只报个数）。title 列供「切换文档」「关文档」用。",
        Run: List,
        Readonly: true,
        SelfReport: true);

    public static QuickCommand OpenCommand { get; } = new(
        Key: "doc-open",
        CommandName: StrenuaIdentity.Domain + ".doc.open",
        Title: "打开文档",
        Summary: "按完整路径打开零件、装配体或工程图并切到它（已开着就切过去），读回活动文档。",
        Usage: "「路径」填 .SLDPRT / .SLDASM / .SLDDRW 的完整路径。打开后它就是活动文档，之后的指令都作用在它上面。",
        Run: QuickCommand.Unbound,
        Parameters: [new QuickParameter("path", "路径", "文档的完整路径", true)],
        Example: StrenuaIdentity.Domain + @".doc.open path=D:\图纸\零件1.SLDDRW",
        Bind: arguments =>
        {
            var path = QuickArgs.Name(arguments, "path");
            var type = TypeOf(path);
            return context => Open(context, path, type);
        },
        SelfReport: true);

    public static QuickCommand ActivateCommand { get; } = new(
        Key: "doc-activate",
        CommandName: StrenuaIdentity.Domain + ".doc.activate",
        Title: "切换文档",
        Summary: "按名字（文档列表的 title 列）切到一个开着的文档，读回活动文档。",
        Usage: "「文档」填文档列表的 title，如「零件1 - 图纸1」或「零件1.SLDPRT」。",
        Run: QuickCommand.Unbound,
        Parameters: [new QuickParameter("name", "文档", "文档列表的 title 列", true)],
        Example: StrenuaIdentity.Domain + ".doc.activate name=零件1.SLDPRT",
        Bind: arguments =>
        {
            var name = QuickArgs.Name(arguments, "name");
            return context => Activate(context, name);
        },
        SelfReport: true);

    private const int OpenSilent = 1;
    private const int ActivateSilent = 1;

    internal static int TypeOf(string path) => Path.GetExtension(path).ToUpperInvariant() switch
    {
        ".SLDPRT" => SwConst.DocumentPart,
        ".SLDASM" => SwConst.DocumentAssembly,
        ".SLDDRW" => SwConst.DocumentDrawing,
        _ => throw new QuickArgumentException($"path={path} 不是 .SLDPRT / .SLDASM / .SLDDRW。"),
    };

    private static string KindName(int type) => type switch
    {
        SwConst.DocumentPart => "零件",
        SwConst.DocumentAssembly => "装配体",
        SwConst.DocumentDrawing => "工程图",
        _ => "其他",
    };

    private static QuickOutcome List(QuickCommandContext context)
    {
        var api = context.Api;
        var active = context.Session.ActiveDocument();
        var rows = new List<Dictionary<string, string>>();
        var hidden = 0;
        foreach (var document in api.CallArray(context.Session.Application, "ISldWorks", "GetDocuments"))
        {
            if (!api.CallBool(document, "IModelDoc2", "get_Visible"))
            {
                hidden++;
                continue;
            }

            rows.Add(new Dictionary<string, string>
            {
                ["title"] = api.CallString(document, "IModelDoc2", "GetTitle"),
                ["path"] = api.CallString(document, "IModelDoc2", "GetPathName"),
                ["kind"] = KindName(api.CallInt(document, "IModelDoc2", "GetType")),
                ["dirty"] = api.CallBool(document, "IModelDoc2", "GetSaveFlag") ? "是" : "否",
                ["active"] = ReferenceEquals(document, active) ? "是" : "否",
            });
        }

        var lines = rows.Select(row => $"· {row["title"]}（{row["kind"]}{(row["dirty"] == "是" ? "，未保存" : string.Empty)}{(row["active"] == "是" ? "，活动" : string.Empty)}）{row["path"]}");
        return QuickOutcome.Ok($"文档列表：开着 {rows.Count} 个窗口，另有 {hidden} 个带着加载、没有窗口。" + Environment.NewLine + string.Join(Environment.NewLine, lines), rows);
    }

    private static QuickOutcome Open(QuickCommandContext context, string path, int type)
    {
        var api = context.Api;
        if (!File.Exists(path))
            return QuickOutcome.Fail($"打开文档：文件不存在 {path}。");
        object?[] args = [path, type, OpenSilent, string.Empty, 0, 0];
        if (api.Call(context.Session.Application, "ISldWorks", "OpenDoc6", args) is not { } document)
            return QuickOutcome.Fail($"打开文档：SolidWorks 没打开 {path}（错误码 {args[4]}）。");
        return Activate(context, api.CallString(document, "IModelDoc2", "GetTitle"));
    }

    private static QuickOutcome Activate(QuickCommandContext context, string name)
    {
        var api = context.Api;
        object?[] args = [name, false, ActivateSilent, 0];
        if (api.Call(context.Session.Application, "ISldWorks", "ActivateDoc3", args) is null)
            return QuickOutcome.Fail($"切换文档：切不到「{name}」（错误码 {args[3]}），先看 strenua.doc.list。");
        var active = context.Session.ActiveDocument();
        return QuickOutcome.Ok($"活动文档：{(active is null ? "（无）" : api.CallString(active, "IModelDoc2", "GetTitle"))}。");
    }
}
