using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 建模类（1.38.0，用户定：SolidWorks MCP 是玩具，建模装配并进 Strenua）：新建零件、一次画完一张草图。
/// </summary>
/// <remarks>
/// 只作用于活动零件；新建零件会成为活动文档。草图一条指令画完并退出（AI 不逐条线来回），之后用 <c>model.extrude</c> 拉伸或切除。
/// 坐标一律零件 mm，草图坐标与零件坐标的对应写在回执里（标准基准面）。
/// </remarks>
internal static class ModelSketch
{
    public static QuickCommand NewCommand { get; } = new(
        Key: "model-new",
        CommandName: StrenuaIdentity.Domain + ".model.new",
        Title: "新建零件",
        Summary: "用默认模板新建零件或装配体并存到给定路径，成为活动文档；已有同名文件要 overwrite=是。",
        Usage: "「路径」填 .SLDPRT 或 .SLDASM 的完整路径。用 SolidWorks 选项里的默认模板，建完立即存盘（文件在才能被总装引用）。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("path", "路径", "新文件完整路径（.SLDPRT / .SLDASM）", true),
            new QuickParameter("overwrite", "覆盖", "已有同名文件时覆盖：是 / 否（默认否）", false, 2),
        ],
        Example: StrenuaIdentity.Domain + @".model.new path=D:\项目\b-Module-SW\XX-01-01底板.SLDPRT",
        Bind: arguments =>
        {
            var path = QuickArgs.Name(arguments, "path");
            var type = Path.GetExtension(path).ToUpperInvariant() switch
            {
                ".SLDPRT" => SwConst.DocumentPart,
                ".SLDASM" => SwConst.DocumentAssembly,
                _ => throw new QuickArgumentException($"path={path} 要以 .SLDPRT 或 .SLDASM 结尾。"),
            };
            var overwrite = QuickArgs.Flag(arguments, "overwrite");
            return context => New(context, path, type, overwrite);
        },
        SelfReport: true);

    public static QuickCommand SketchCommand { get; } = new(
        Key: "model-sketch",
        CommandName: StrenuaIdentity.Domain + ".model.sketch",
        Title: "画草图",
        Summary: "在基准面或零件表面上一次画完一张草图（矩形、圆、多孔、多边形、腰型槽），退出草图并回草图名。",
        Usage: "「平面」写 前视 / 上视 / 右视、基准面名，或 face:x,y,z（零件表面上的一点，mm）。"
               + "「图形」分号隔开：rect:中心x,中心y,宽,高；box:x1,y1,x2,y2；circle:x,y,直径；circles:直径,x1,y1,x2,y2,…；poly:x1,y1,x2,y2,…（自动闭合）；slot:x1,y1,x2,y2,宽。草图 mm。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("plane", "平面", "前视 / 上视 / 右视 / 基准面名 / face:x,y,z", true),
            new QuickParameter("shapes", "图形", "rect / box / circle / circles / poly / slot，分号隔开", true, 1),
        ],
        Example: StrenuaIdentity.Domain + ".model.sketch plane=上视 shapes=rect:0,0,200,120;circles:6.6,-80,-40,80,-40,-80,40,80,40",
        Bind: arguments =>
        {
            var plane = QuickArgs.Name(arguments, "plane");
            var face = plane.StartsWith("face:", StringComparison.OrdinalIgnoreCase) ? ModelSpec.Point(plane[5..], "plane=face") : ((double, double, double)?)null;
            var shapes = ModelSpec.Shapes(QuickArgs.Name(arguments, "shapes"));
            return context => Sketch(context, plane, face, shapes);
        });

    private const int DefaultTemplatePart = 8;
    private const int DefaultTemplateAssembly = 9;

    private static QuickOutcome New(QuickCommandContext context, string path, int type, bool overwrite)
    {
        var api = context.Api;
        var application = context.Session.Application;
        if (File.Exists(path) && !overwrite)
            return QuickOutcome.Fail($"新建：{path} 已有文件。要覆盖给 overwrite=是，要改它用 strenua.doc.open。");
        var template = api.CallString(application, "ISldWorks", "GetUserPreferenceStringValue", type == SwConst.DocumentPart ? DefaultTemplatePart : DefaultTemplateAssembly);
        if (template.Length == 0 || !File.Exists(template))
            throw new QuickCommandException($"SolidWorks 选项里的默认{(type == SwConst.DocumentPart ? "零件" : "装配体")}模板不存在（{template}），先在「选项 → 默认模板」里设好。");
        var document = api.Call(application, "ISldWorks", "NewDocument", template, 0, 0.0, 0.0)
                       ?? throw new QuickCommandException($"SolidWorks 没用模板 {template} 建出新文档。");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        object?[] save = [path, 0, SwConst.SaveSilent, null, 0, 0];
        var extension = api.Call(document, "IModelDoc2", "get_Extension")!;
        if (!api.CallBool(extension, "IModelDocExtension", "SaveAs", save) || !File.Exists(path))
            return QuickOutcome.Fail($"新建：文档建好了，但存到 {path} 没成（错误码 {save[4]}），新文档还开着、没存。");
        return QuickOutcome.Ok($"新建：{path} 已建好并存盘，是活动文档（模板 {Path.GetFileName(template)}）。");
    }

    private static QuickOutcome Sketch(QuickCommandContext context, string plane, (double X, double Y, double Z)? face, IReadOnlyList<SketchShape> shapes)
    {
        var api = context.Api;
        var part = ModelAccess.ActivePart(context);
        api.ClearSelection(part);
        string where;
        if (face is { } point)
        {
            var extension = api.Call(part, "IModelDoc2", "get_Extension")!;
            if (!api.CallBool(extension, "IModelDocExtension", "SelectByID2", string.Empty, "FACE", point.X / 1000, point.Y / 1000, point.Z / 1000, false, 0, null, 0))
                return QuickOutcome.Fail($"画草图：在 {ModelAccess.Format(point)} 没选到零件表面（点要正好落在面上，先 strenua.model.info 看包围盒）。");
            where = $"表面 {ModelAccess.Format(point)}（草图坐标轴由 SolidWorks 按这张面定，画完看回执的草图原点再核对）";
        }
        else
        {
            var feature = PlaneFeature(api, part, plane)
                          ?? throw new QuickArgumentException($"plane={plane}：没有这个基准面（写 前视 / 上视 / 右视 或特征树里的基准面名）。");
            api.Call(feature, "IFeature", "Select2", false, 0);
            where = ModelSpec.StandardPlane(plane) is { } standard
                ? $"{plane}基准面（{ModelSpec.PlaneAxes(standard)}）"
                : $"基准面「{api.CallString(feature, "IFeature", "get_Name")}」";
        }

        var sketches = api.Call(part, "IModelDoc2", "get_SketchManager")!;
        api.Call(sketches, "ISketchManager", "InsertSketch", true);
        // 不加几何关系、不吸附：坐标给多少画多少。
        api.Call(sketches, "ISketchManager", "set_AddToDB", true);
        var drawn = 0;
        try
        {
            foreach (var shape in shapes)
                drawn += Draw(api, sketches, shape) ? 1 : 0;
        }
        finally
        {
            api.Call(sketches, "ISketchManager", "set_AddToDB", false);
            api.Call(sketches, "ISketchManager", "InsertSketch", true);
        }

        var sketch = api.Call(part, "IModelDoc2", "FeatureByPositionReverse", 0);
        var name = sketch is null ? "?" : api.CallString(sketch, "IFeature", "get_Name");
        api.ClearSelection(part);
        var message = $"画草图：在{where}上画了 {drawn}/{shapes.Count} 个图形，草图「{name}」。下一步 strenua.model.extrude sketch={name}。";
        return drawn == shapes.Count ? QuickOutcome.Ok(message) : QuickOutcome.Fail(message + " 有图形没画上（尺寸太小或重叠）。");
    }

    /// <summary>基准面特征：标准三面按特征树里前三个基准面认（不认名字，中英文 SolidWorks 都行），其余按名字。</summary>
    internal static object? PlaneFeature(SolidWorksApi api, object part, string plane)
    {
        var standard = ModelSpec.StandardPlane(plane);
        var index = 0;
        for (var feature = api.Call(part, "IModelDoc2", "FirstFeature"); feature is not null; feature = api.Call(feature, "IFeature", "GetNextFeature"))
        {
            if (api.CallString(feature, "IFeature", "GetTypeName2") != "RefPlane")
                continue;
            if (standard is { } wanted ? index++ == wanted : string.Equals(api.CallString(feature, "IFeature", "get_Name"), plane, StringComparison.OrdinalIgnoreCase))
                return feature;
        }

        return null;
    }

    private static bool Draw(SolidWorksApi api, object sketches, SketchShape shape)
    {
        const double k = 0.001;
        switch (shape)
        {
            case SketchShape.Rect r:
                return api.Call(sketches, "ISketchManager", "CreateCenterRectangle", r.X * k, r.Y * k, 0.0, (r.X + r.Width / 2) * k, (r.Y + r.Height / 2) * k, 0.0) is not null;
            case SketchShape.Box b:
                return api.Call(sketches, "ISketchManager", "CreateCornerRectangle", b.X1 * k, b.Y1 * k, 0.0, b.X2 * k, b.Y2 * k, 0.0) is not null;
            case SketchShape.Circle c:
                return api.Call(sketches, "ISketchManager", "CreateCircleByRadius", c.X * k, c.Y * k, 0.0, c.Diameter / 2 * k) is not null;
            case SketchShape.Polygon p:
                var ok = true;
                for (var i = 0; i < p.Points.Count; i++)
                {
                    var (a, b2) = (p.Points[i], p.Points[(i + 1) % p.Points.Count]);
                    ok &= api.Call(sketches, "ISketchManager", "CreateLine", a.X * k, a.Y * k, 0.0, b2.X * k, b2.Y * k, 0.0) is not null;
                }

                return ok;
            case SketchShape.Slot s:
                // 直槽：两端圆心 + 宽；第三点给宽度方向上的一点（swSketchSlotCreationType 0 = 直槽，长度类型 0 = 圆心到圆心）。
                var (dx, dy) = (s.X2 - s.X1, s.Y2 - s.Y1);
                var length = Math.Sqrt(dx * dx + dy * dy);
                var (nx, ny) = (-dy / length * s.Width / 2, dx / length * s.Width / 2);
                return api.Call(sketches, "ISketchManager", "CreateSketchSlot", 0, 0, s.Width * k,
                    s.X1 * k, s.Y1 * k, 0.0, s.X2 * k, s.Y2 * k, 0.0, (s.X2 + nx) * k, (s.Y2 + ny) * k, 0.0, 1, false) is not null;
            default:
                return false;
        }
    }
}
