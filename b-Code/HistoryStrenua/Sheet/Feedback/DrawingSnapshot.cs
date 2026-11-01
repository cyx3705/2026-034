namespace HistoryStrenua;

/// <summary>把当前图纸页存成 PNG（<see cref="SnapshotCheck"/>、AI 技术要求、回执降级共用）。</summary>
/// <remarks>
/// <para>给 AI 用的验收眼睛：AI 经 MCP 调完出图、标注指令后调它，再读这张图就能自己看结果（宿主的界面截图只截得到宿主自己的窗口）。
/// 人也能用它快速留一张图。</para>
/// <para>用打印捕获另存当前纸面（<c>SaveAs3</c> + 副本 + 静默，不改原文档的路径与保存状态），不含图框外备用注释。图片放模块数据目录的
/// <see cref="Folder"/> 里，只留最近 <see cref="Keep"/> 张。不改图，只改了窗口的缩放。</para>
/// </remarks>
internal static class DrawingSnapshot
{
    public const string Folder = "snapshots";

    public const int Keep = 30;

    private const int CurrentVersion = 0;
    private const int SilentCopy = 1 | 2;

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
        var file = Path.Combine(directory, $"{SafeName(title)}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");

        context.SetState("截图");
        api.Call(document, "IModelDoc2", "ViewZoomtofit2");
        var extension = api.Call(document, "IModelDoc2", "get_Extension")
            ?? throw new QuickCommandException("SolidWorks 没有返回文档扩展接口。");
        object?[] arguments = [file, CurrentVersion, SilentCopy, null, null, 0, 0];
        // REQ-DQ-01：SW 2025 的 PNG 共用 TIFF 导出选项。屏幕捕获把图框外备用技术要求也拍进去，
        // AI 会拿它们当成已采用的条目；打印捕获只输出当前图纸。选项是应用级，成功/失败都必须还原。
        // swTiffScreenOrPrintCapture=6（1=打印）、PrintDPI=9、ImageType=7（0=黑白）。
        var application = context.Session.Application;
        var integers = new Dictionary<int, int>();
        var toggles = new Dictionary<int, bool>();
        bool saved;
        try
        {
            foreach (var (key, value) in new[] { (6, 1), (9, 200), (7, 0) })
            {
                integers[key] = api.CallInt(application, "ISldWorks", "GetUserPreferenceIntegerValue", key);
                api.Call(application, "ISldWorks", "SetUserPreferenceIntegerValue", key, value);
            }
            // swTiffPrintScaleToFit / PrintAllSheets / PrintUseSheetSize。
            foreach (var (key, value) in new[] { (28, true), (405, false), (406, true) })
            {
                toggles[key] = api.CallBool(application, "ISldWorks", "GetUserPreferenceToggle", key);
                api.Call(application, "ISldWorks", "SetUserPreferenceToggle", key, value);
            }
            saved = api.CallBool(extension, "IModelDocExtension", "SaveAs3", arguments);
        }
        finally
        {
            foreach (var pair in integers)
                api.CallForCleanup(application, "ISldWorks", "SetUserPreferenceIntegerValue", pair.Key, pair.Value);
            foreach (var pair in toggles)
                api.CallForCleanup(application, "ISldWorks", "SetUserPreferenceToggle", pair.Key, pair.Value);
        }
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

    /// <summary>只留最近 <see cref="Keep"/> 张（1.21.0 起图纸 SVG 也放这里，按扩展名各留各的）。</summary>
    internal static void Prune(string directory, string pattern = "*.png")
    {
        try
        {
            foreach (var old in new DirectoryInfo(directory).GetFiles(pattern).OrderByDescending(f => f.LastWriteTimeUtc).Skip(Keep))
                old.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
