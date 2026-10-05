namespace HistoryStrenua;

/// <summary>
/// 快捷指令「技术要求」（1.9.0，出图类，从「新建工程图」拆出）：把技术要求注释放进当前工程图的图框里，
/// 先放标题栏正上方、再左下角、再找空地；图框里已经有了就不再加。
/// </summary>
/// <remarks>
/// 内容取模块数据目录的 <see cref="TechnicalNoteFile"/>，没有就用用户手工图上的那 8 条（<see cref="DefaultTechnicalNote"/>）并写出这个文件方便改；
/// 字体字高随模板（<c>InsertNote</c> 用文档的注释样式）。放哪见 <see cref="TechNotePlacement"/>（1.13.0）：只许标题栏上方或左侧，
/// 「避障」开时压到视图就挪视图让地方，关着直接放标题栏正上方。要换成 SW 模板里的某一份，用「技术要求」类的模板表格（<see cref="TechApply"/>）。
/// </remarks>
internal static class DrawingNote
{
    public static QuickCommand Command { get; } = new(
        // 1.12.0 挪进新开的「技术要求」类（用户定；下一步在这一类里接 SolidWorks 的技术要求模板做选择），一键出图照旧调它。
        Key: "tech-note",
        CommandName: StrenuaIdentity.Domain + ".tech.note",
        Title: "技术要求",
        Summary: "把技术要求放进当前工程图的标题栏上方或左侧（压到视图就挪视图），内容取「技术要求.txt」，图框里已有就不再加。",
        Usage: "在工程图里按：插一条技术要求注释（内容取模块数据目录的「技术要求.txt」，没有就用默认那 8 条并写出这个文件，改它就能换内容；字体字高随模板）。只放标题栏正上方或标题栏左侧（先上方后左侧）：「避障」开时压到视图（连同尺寸）就把视图往上或往左挪开让出地方，关着直接放标题栏正上方。图框里已经有带「技术要求」的注释就不再加（模板摆在图框外备用的那条不算）；要换成 SW 模板里的某一份，用下面的模板表格。",
        Run: context => Run(context, DrawingSheet.Read(context, HoleScan.ActiveDrawing(context), "技术要求")));

    /// <summary>技术要求的文件名（模块数据目录里，用户可改，UTF-8）。</summary>
    public const string TechnicalNoteFile = "技术要求.txt";

    /// <summary>用户的标准技术要求（2026-025 台面2机器手工图原文，含 SolidWorks 段落格式标记，字体字高随模板）。</summary>
    public const string DefaultTechnicalNote =
        "        技术要求\n"
        + "<PARA  indent=0 findent=0 indentStep=10 number=off bullet=off paraSpace=0.001 lineSpace=0.001>1、未注公差参照附表执行；\n"
        + "2、未注沉头孔与螺纹孔位置公差为±0.2mm，定位销孔位置公差±0.02mm;\n"
        + "3、图形尺寸为表面处理后尺寸；\n"
        + "4、锐边倒钝，去毛刺，飞边;\n"
        + "5、未注倒角C1，未注圆角R1，螺纹孔及沉孔顶部倒角C0.5~C1;\n"
        + "6、零件表面不应有划痕、擦伤等损伤零件表面的缺陷；\n"
        + "7、加工的螺纹表面不应有黑皮、磕碰、乱扣和毛刺等缺陷；\n"
        + "8、未注尺寸参考3D数模。";

    internal static QuickOutcome Run(QuickCommandContext context, DrawingSheet sheet)
    {
        var api = context.Api;
        if (sheet.Note is not null)
            return QuickOutcome.Ok("技术要求：图框里已经有一条带「技术要求」的注释，没有再加。");

        context.SetState("加技术要求");
        var (content, source) = TechnicalNoteText(context.Options.DataDirectory);
        api.Call(sheet.Drawing, "IModelDoc2", "ClearSelection2", true);
        var note = api.Call(sheet.Drawing, "IModelDoc2", "InsertNote", content);
        if (note is null || api.Call(note, "INote", "GetAnnotation") is not { } annotation)
            return QuickOutcome.Fail("技术要求：SolidWorks 没有接受注释，没有加上。");

        // 1.13.0（用户定硬性要求）：只放标题栏上方或左侧，避障开时压到视图就挪视图让地方（与模板表格同一套）。
        var (where, _) = TechNotePlacement.Place(context, sheet.Drawing, note, annotation, preferred: null);
        return QuickOutcome.Ok($"技术要求：已放进图纸，{where}（{source}）。");
    }

    /// <summary>技术要求的内容与出处说明。</summary>
    internal static (string Content, string Source) TechnicalNoteText(string? dataDirectory)
    {
        if (dataDirectory is null)
            return (DefaultTechnicalNote, "默认 8 条");
        var path = Path.Combine(dataDirectory, TechnicalNoteFile);
        try
        {
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path, System.Text.Encoding.UTF8).Replace("\r\n", "\n").TrimEnd('\n', ' ');
                if (text.Length > 0)
                    return (text, $"取自 {path}");
            }

            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(path, DefaultTechnicalNote.Replace("\n", Environment.NewLine) + Environment.NewLine, new System.Text.UTF8Encoding(false));
            return (DefaultTechnicalNote, $"默认 8 条，已写到 {path}，改它就能换内容");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (DefaultTechnicalNote, "默认 8 条");
        }
    }
}
