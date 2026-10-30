using HistoryStrenua.SolidWorks;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 文档类（1.25.0）：保存、关闭工程图。只碰工程图：零件和总装里可能有用户没存的改动，这里不替用户存，也不丢。
/// </summary>
internal static class DocSave
{
    public static QuickCommand SaveCommand { get; } = new(
        Key: "doc-save",
        CommandName: StrenuaIdentity.Domain + ".doc.save",
        Title: "保存工程图",
        Summary: "保存当前工程图；新图默认存到引用模型旁边、与模型同名，已有同名文件要 overwrite 才覆盖；读回磁盘核对。",
        Usage: "只存工程图。「路径」空着：存过的存回原处，新图存到引用模型旁边、与模型同名。目标已有别的文件时要「覆盖」填是。存完核对文件在、未保存标记已清。不存它引用的零件。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("path", "路径", "另存为的完整路径（.SLDDRW）；省略见说明", false),
            new QuickParameter("overwrite", "覆盖", "是 = 目标已有别的文件时覆盖；省略为否", false, 2),
        ],
        Example: StrenuaIdentity.Domain + ".doc.save",
        Bind: arguments =>
        {
            var path = QuickArgs.Optional(arguments, "path");
            if (path is not null && DocOpen.TypeOf(path) != DocumentDrawing)
                throw new QuickArgumentException($"path={path} 不是 .SLDDRW。");
            var overwrite = QuickArgs.Flag(arguments, "overwrite");
            return context => Save(context, path, overwrite);
        },
        SelfReport: true);

    public static QuickCommand CloseCommand { get; } = new(
        Key: "doc-close",
        CommandName: StrenuaIdentity.Domain + ".doc.close",
        Title: "关工程图",
        Summary: "关当前或点名的工程图；有未保存改动默认拒绝，discard 填是才丢弃。",
        Usage: "「文档」空着关当前活动文档，填名字关那一个（文档列表的 title）。只关工程图。有未保存的改动时拒绝，「丢弃」填是才丢掉改动关闭。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("name", "文档", "文档列表的 title 列；省略关当前活动文档", false),
            new QuickParameter("discard", "丢弃", "是 = 丢掉未保存的改动；省略为否", false, 2),
        ],
        Example: StrenuaIdentity.Domain + ".doc.close",
        Bind: arguments =>
        {
            var name = QuickArgs.Optional(arguments, "name");
            var discard = QuickArgs.Flag(arguments, "discard");
            return context => Close(context, name, discard);
        },
        SelfReport: true);

    private const int DocumentDrawing = 3;
    private const int SaveSilent = 1;

    private static QuickOutcome Save(QuickCommandContext context, string? path, bool overwrite)
    {
        var api = context.Api;
        var drawing = HoleScan.ActiveDrawing(context);
        var current = api.CallString(drawing, "IModelDoc2", "GetPathName");
        var target = path ?? (current.Length > 0 ? current : DefaultPath(api, drawing));
        if (target is null)
            return QuickOutcome.Fail("保存工程图：新图没有引用模型，定不了默认路径，请给 path=。");
        var same = string.Equals(target, current, StringComparison.OrdinalIgnoreCase);
        if (!same && File.Exists(target) && !overwrite)
            return QuickOutcome.Fail($"保存工程图：{target} 已存在，要覆盖请给 overwrite=是。");

        object?[] args = same ? [SaveSilent, 0, 0] : [target, 0, SaveSilent, null, 0, 0];
        var saved = same
            ? api.CallBool(drawing, "IModelDoc2", "Save3", args)
            : api.Call(drawing, "IModelDoc2", "get_Extension") is { } extension && api.CallBool(extension, "IModelDocExtension", "SaveAs", args);
        if (!saved || !File.Exists(target) || api.CallBool(drawing, "IModelDoc2", "GetSaveFlag"))
            return QuickOutcome.Fail($"保存工程图：没存上 {target}（错误码 {args[^2]}）。");
        return QuickOutcome.Ok($"保存工程图：已存 {target}。");
    }

    /// <summary>新图的默认路径：第一个有引用模型的视图，模型旁边、同名。</summary>
    private static string? DefaultPath(SolidWorksApi api, object drawing)
    {
        foreach (var view in HoleScan.SheetViews(api, drawing))
        {
            if (api.Call(view, "IView", "get_ReferencedDocument") is { } model
                && api.CallString(model, "IModelDoc2", "GetPathName") is { Length: > 0 } modelPath)
                return Path.ChangeExtension(modelPath, ".SLDDRW");
        }

        return null;
    }

    private static QuickOutcome Close(QuickCommandContext context, string? name, bool discard)
    {
        var api = context.Api;
        var application = context.Session.Application;
        var document = name is null
            ? context.Session.ActiveDocument()
            : api.CallArray(application, "ISldWorks", "GetDocuments").FirstOrDefault(item => api.CallString(item, "IModelDoc2", "GetTitle") == name);
        if (document is null)
            return QuickOutcome.Fail($"关工程图：没有{(name is null ? "活动文档" : $"「{name}」")}。");
        var title = api.CallString(document, "IModelDoc2", "GetTitle");
        if (api.CallInt(document, "IModelDoc2", "GetType") != DocumentDrawing)
            return QuickOutcome.Fail($"关工程图：「{title}」不是工程图，这条只关工程图。");
        var dirty = api.CallBool(document, "IModelDoc2", "GetSaveFlag") || api.CallString(document, "IModelDoc2", "GetPathName").Length == 0;
        if (dirty && !discard)
            return QuickOutcome.Fail($"关工程图：「{title}」有未保存的改动，没关。先 strenua.doc.save，或给 discard=是 丢弃。");
        api.Call(application, "ISldWorks", "CloseDoc", title);
        var active = context.Session.ActiveDocument();
        return QuickOutcome.Ok($"关工程图：「{title}」已关{(dirty ? "（改动已丢弃）" : string.Empty)}。活动文档：{(active is null ? "（无）" : api.CallString(active, "IModelDoc2", "GetTitle"))}。");
    }
}
