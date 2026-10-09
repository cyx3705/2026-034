using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 找工程图模板（1.9.0）：SolidWorks「选项 → 文件位置 → 文件模板」里各目录下的 <c>*.drwdot</c>，按文件名认图幅并排好
/// （见 <see cref="DrawingTemplate.Rank"/>）；一个都认不出时退回 SolidWorks 的默认工程图模板。
/// </summary>
internal static class DrawingTemplates
{
    // swUserPreferenceStringValue_e
    private const int FileLocationsDocumentTemplates = 6;
    private const int DefaultTemplateDrawing = 10;

    public static (IReadOnlyList<DrawingTemplate> Ranked, string? Fallback) Find(SolidWorksApi api, object application)
    {
        var folders = api.CallString(application, "ISldWorks", "GetUserPreferenceStringValue", FileLocationsDocumentTemplates)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var found = new List<DrawingTemplate>();
        foreach (var folder in folders)
        {
            try
            {
                if (!Directory.Exists(folder))
                    continue;
                foreach (var file in Directory.EnumerateFiles(folder, "*.drwdot"))
                {
                    // SolidWorks 打开过的模板旁边会留「~$」开头的锁文件，不是模板。
                    if (Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal))
                        continue;
                    if (DrawingTemplate.FromFile(Path.GetFullPath(file)) is { } template)
                        found.Add(template);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        var fallback = api.CallString(application, "ISldWorks", "GetUserPreferenceStringValue", DefaultTemplateDrawing);
        return (DrawingTemplate.Rank(found), string.IsNullOrWhiteSpace(fallback) || !File.Exists(fallback) ? null : Path.GetFullPath(fallback));
    }
}
