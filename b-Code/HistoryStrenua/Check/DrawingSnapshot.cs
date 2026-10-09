namespace HistoryStrenua;

/// <summary>
/// 快捷指令「图纸截图」（1.9.0，检查类）：把当前工程图的当前图纸页整页存成一张 PNG，回执给出文件路径。
/// </summary>
/// <remarks>
/// <para>给 AI 用的验收眼睛：AI 经 MCP 调完出图、标注指令后调它，再读这张图就能自己看结果（宿主的界面截图只截得到宿主自己的窗口）。
/// 人也能用它快速留一张图。</para>
/// <para>先「缩放到整页」再另存一份副本（<c>SaveAs3</c> + 副本 + 静默，不改原文档的路径与保存状态）。图片放模块数据目录的
/// <see cref="Folder"/> 里，只留最近 <see cref="Keep"/> 张。不改图，只改了窗口的缩放。</para>
/// </remarks>
internal static class DrawingSnapshot
{
    public static QuickCommand Command { get; } = new(
        Key: "snapshot",
        CommandName: StrenuaIdentity.Domain + ".check.snapshot",
        Title: "图纸截图",
        Summary: "把当前工程图的当前图纸页整页存成 PNG 并回执文件路径（给 AI 看图验收用），不改图。",
        Usage: "不用点视图：把当前工程图当前图纸页缩放到整页后另存一张 PNG（副本，不改原图、不改保存状态），存在模块数据目录的 snapshots 里（只留最近 30 张），控制台回执给出文件路径。AI 经 MCP 调完出图、标注指令后调它，读这张图就能自己验收。",
        Run: Run);

    public const string Folder = "snapshots";

    public const int Keep = 30;

    // swSaveAsVersion_e.swSaveAsCurrentVersion / swSaveAsOptions_e.swSaveAsOptions_Silent | swSaveAsOptions_Copy
    private const int CurrentVersion = 0;
    private const int SilentCopy = 1 | 2;

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var document = HoleScan.ActiveDrawing(context);
        var title = context.Api.CallString(document, "IModelDoc2", "GetTitle");
        var file = Save(context, document);
        return QuickOutcome.Ok($"图纸截图：「{title}」当前图纸页已存成图片 {file}");
    }

    /// <summary>
    /// 把工程图当前图纸页存成 PNG，返回文件路径（1.14.0 起「AI 填写技术要求」也用它给识图模型看图）。
    /// </summary>
    /// <exception cref="QuickCommandException">SolidWorks 没存出来。</exception>
    internal static string Save(QuickCommandContext context, object document)
    {
        var api = context.Api;
        var title = api.CallString(document, "IModelDoc2", "GetTitle");
        var directory = Path.Combine(context.Options.DataDirectory ?? Path.GetTempPath(), Folder);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"{SafeName(title)}-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        context.SetState("截图");
        api.Call(document, "IModelDoc2", "ViewZoomtofit2");
        var extension = api.Call(document, "IModelDoc2", "get_Extension")
            ?? throw new QuickCommandException("SolidWorks 没有返回文档扩展接口。");
        object?[] arguments = [file, CurrentVersion, SilentCopy, null, null, 0, 0];
        var saved = api.CallBool(extension, "IModelDocExtension", "SaveAs3", arguments);
        if (!saved || !File.Exists(file))
            throw new QuickCommandException($"图纸截图：SolidWorks 没能把「{title}」存成图片（错误码 {arguments[5]}）。");

        Prune(directory);
        return file;
    }

    /// <summary>文件名里不能有的字符换成「_」。</summary>
    internal static string SafeName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
    }

    /// <summary>只留最近 <see cref="Keep"/> 张。</summary>
    private static void Prune(string directory)
    {
        try
        {
            foreach (var old in new DirectoryInfo(directory).GetFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc).Skip(Keep))
                old.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
