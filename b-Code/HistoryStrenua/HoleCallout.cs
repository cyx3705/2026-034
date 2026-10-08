using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「孔标注」：点一个工程图视图，视图里看得见的孔（含腰型孔）每种加一个孔标注。
/// </summary>
/// <remarks>
/// 选视图与认孔见 <see cref="HoleScan"/>。每种孔（同一特征、同一孔径）只标一个：SolidWorks 的孔标注
/// 自己会写上「N×」，每个都标就是同一行字重复 N 遍。某种孔里已经有一个带标注，整种跳过，连点两次不会出双份。
/// 腰型孔选它一端的圆弧加孔标注，与圆孔同一个 <c>AddHoleCallout2</c>，规格由 SolidWorks 一次写全。
/// 最后做标注避障（1.6.0，<see cref="Clearance.ClearCallouts"/>）：视图里孔标注的文字压在别的孔相关注解上就换角位，
/// 已有的孔标注也算。1.7.0 起受页面「避障」开关控制（默认开）。
/// 避障之前看对面（1.14.1，<see cref="MarkSuffixes"/>）：对面的同种孔与这一面同位置，孔标注后面写「(含对面)」。
/// 跨视图不重复（1.15.0，用户定，<see cref="HoleCoveragePlanner"/>）：同一页上视线平行的视图（顶视图对底视图）已标过的种不再标；
/// 柱形沉头孔的沉孔在这一面背面时，留给看得见沉孔的那个视图标，没有那样的视图才标在这里、后缀写「(反面)」。
/// </remarks>
internal static class HoleCallout
{
    public static QuickCommand Command { get; } = new(
        Key: "hole-callout",
        CommandName: StrenuaIdentity.Domain + ".hole.callout",
        Title: "孔标注",
        Summary: "点一个工程图视图，视图里每种孔（含腰型孔）各标一次孔标注，已有标注的种跳过。",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内），该视图里每种孔（含腰型孔）标一次（数量由 SolidWorks 的 N× 带出）；已有标注的种跳过，同一页上别的视图（如顶视图对底视图）已标过的种也跳过。柱形沉头孔的沉孔在这个视图背面时，这一页有看得见沉孔的视图就留给那个视图标，没有才标在这里、后面写「(反面)」。零件对面（背面）的同种孔与这一面位置一一相同时，这种的孔标注后面写「(含对面)」（SolidWorks 的 N× 把两面都数进去了，视图只看得到这一面）；对面位置不同时回执提示要从对面再开一个视图。「避障」开关开着时最后避障：孔标注文字压在别的孔的尺寸、中心符号线等线条上就换到孔的另一个角。",
        Run: context => Run(context, null));

    /// <summary>首次落位时文字中心放在折点左边多远（图纸上 20 mm），只是让文字先落在左边。</summary>
    private const double InitialTextOffset = 0.02;

    /// <summary>对准折点最多挪几次：挪动后 SolidWorks 可能换引线接的那一侧，要回读再挪。</summary>
    private const int AlignPasses = 3;

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="view">直接处理这个视图（全流程用）；null 时取选中的或等用户点选。</param>
    internal static QuickOutcome Run(QuickCommandContext context, object? view)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "孔标注", view: view);
        var (document, viewName) = (scan.Document, scan.ViewName);

        // 1.15.0：同一页视线平行的别的视图标过的种跳过；沉孔在背面的种优先留给看得见沉孔的视图。
        var covering = HoleCoverage.Read(context, scan);
        var part = ReadPart(context, scan);
        var normal = scan.Geometry.Frame.Normal;
        var plan = HoleCalloutPlanner.Plan(scan.Candidates, ExistingCalloutCenters(api, scan.Geometry, scan.View),
            kind => HoleCoveragePlanner.Callout(kind, covering, HoleCoveragePlanner.Counterbore(kind, part), normal));
        if (plan.HoleCount == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里没有正对图纸的孔，没有加标注。");

        context.SetState("加标注");
        context.Report(plan.Targets.Count == 0
            ? $"孔标注：视图「{viewName}」认出 {plan.Summary}，都已有孔标注或归别的视图标" + (context.Options.Clearance ? "，只做避障。" : "。")
            : $"孔标注：视图「{viewName}」认出 {plan.Summary}，开始为 {plan.Targets.Count} 种各加一个标注。");
        var added = 0;
        var failed = 0;
        var clearance = default(ClearanceResult);
        var opposite = default(OppositeMarks);
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);
        try
        {
            foreach (var target in plan.Targets)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                api.Call(document, "IModelDoc2", "ClearSelection2", true);
                // 先按「文字中心在折点左边一段」落下，再按实际下划线把折点对准——文字多宽要生成后才知道。
                var callout = api.CallBool(scan.View, "IView", "SelectEntity", scan.Edges[target.EdgeIndex], false)
                    ? api.Call(document, "IDrawingDoc", "AddHoleCallout2", target.Placement.X - InitialTextOffset, target.Placement.Y, 0.0)
                    : null;
                if (callout is null)
                {
                    failed++;
                    continue;
                }

                added++;
                if (api.Call(callout, "IDisplayDimension", "GetAnnotation") is { } annotation)
                    AlignShoulder(api, annotation, target.Placement, textLeft: true);
            }

            context.SetState("对面孔");
            opposite = MarkSuffixes(context, scan, part);

            if (context.Options.Clearance)
            {
                context.SetState("避障");
                clearance = Clearance.ClearCallouts(context, scan, HoleCalloutPlanner.Recognize(scan.Candidates));
            }
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var message = $"视图「{viewName}」：{plan.Summary}，新加 {added} 个孔标注"
            + (plan.AlreadyAnnotated > 0 ? $"，{plan.AlreadyAnnotated} 种已有标注跳过" : string.Empty)
            + Elsewhere(plan)
            + (failed > 0 ? $"，{failed} 个 SolidWorks 没有接受" : string.Empty)
            + opposite.Describe()
            + clearance.Describe("孔标注")
            + "。";
        return added == 0 && plan.Targets.Count > 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    // swDimensionTextParts_e.swDimensionTextSuffix / swDocumentTypes_e.swDocPART
    private const int TextSuffix = 2;
    private const int DocumentPart = 1;

    /// <summary>后缀做了什么：「(含对面)」（1.14.1）写上的、已有的、去掉的种数，对面位置不同的种数与孔数；「(反面)」（1.15.0）写上的种数。</summary>
    internal readonly record struct OppositeMarks(int Marked, int Already, int Cleared, int DifferentKinds, int DifferentHoles, int BackMarked = 0)
    {
        public string Describe()
            => (Marked > 0 ? $"，{Marked} 种对面同位置的写了「(含对面)」" : string.Empty)
               + (Already > 0 ? $"，{Already} 种已写「(含对面)」" : string.Empty)
               + (Cleared > 0 ? $"，{Cleared} 种对面已不同位置、去掉了「(含对面)」" : string.Empty)
               + (BackMarked > 0 ? $"，{BackMarked} 种沉孔在背面、写了「(反面)」" : string.Empty)
               + (DifferentKinds > 0
                   ? $"；{DifferentKinds} 种孔对面另有 {DifferentHoles} 个、位置与这一面不同，这个视图看不全，要从对面再开一个视图（「投影视图」会两边都加）"
                   : string.Empty);
    }

    /// <summary>回执里「别的视图已标」「留给看得见沉孔的视图」那一句（1.15.0）。</summary>
    private static string Elsewhere(HoleCalloutPlan plan)
    {
        var skipped = plan.Elsewhere ?? [];
        string Views(CalloutPlace place) => string.Join("、", skipped.Where(item => item.Place == place).Select(item => $"「{item.View}」").Distinct());
        var done = skipped.Count(item => item.Place == CalloutPlace.Elsewhere);
        var deferred = skipped.Count(item => item.Place == CalloutPlace.Deferred);
        return (done > 0 ? $"，{done} 种别的视图（{Views(CalloutPlace.Elsewhere)}）已标过跳过" : string.Empty)
               + (deferred > 0 ? $"，{deferred} 种沉孔在这个视图背面、留给看得见沉孔的视图（{Views(CalloutPlace.Deferred)}）标" : string.Empty);
    }

    /// <summary>视图引用的零件（1.15.0 起孔标注读一次，判沉孔朝向与对面孔共用）；装配视图不读模型，为 null。</summary>
    private static PartGeometry? ReadPart(QuickCommandContext context, ScannedView scan)
    {
        var api = context.Api;
        var model = api.Call(scan.View, "IView", "get_ReferencedDocument");
        return model is null || api.CallInt(model, "IModelDoc2", "GetType") != DocumentPart ? null : PartScan.Read(context, model);
    }

    /// <summary>
    /// 孔标注的后缀：对面孔（1.14.1，用户定，判法见 <see cref="OppositePlanner"/>）——视图里每种孔，对面的同种孔与这一面一一同位置，
    /// 就在这种的孔标注后缀写「(含对面)」，不再同位置了就去掉；沉孔在背面（1.15.0，用户定）——柱形沉头孔的沉孔在这个视图背面，
    /// 后缀写「(反面)」（沉孔那一行在孔标注最后，后缀正好接在它后面）。只看零件视图（装配视图不读模型，跳过）。
    /// </summary>
    /// <remarks>
    /// 写在孔标注的<b>后缀</b>（<c>SetText(swDimensionTextSuffix)</c>）：真机上它接在整段文字最后（「⌀3.30 ↓10.10 (含对面)」），
    /// 不动孔标注里的变量（改上方那行要把「6 x」等写死）。<c>GetText(后缀)</c> 读回总是空的，有没有写过看显示数据里的文字。
    /// 两样都要时写「 (反面) (含对面)」，整段后缀一起写。文字变长后引线折点会跟着动，按写之前的折点再对一次（<see cref="AlignShoulder"/>）。
    /// </remarks>
    private static OppositeMarks MarkSuffixes(QuickCommandContext context, ScannedView scan, PartGeometry? part)
    {
        if (part is null)
            return default;

        var api = context.Api;
        var normal = scan.Geometry.Frame.Normal;
        var scale = scan.Geometry.Scale;
        var kinds = HoleCalloutPlanner.GroupKinds(HoleCalloutPlanner.Recognize(scan.Candidates).Where(hole => hole.Slot < 0));
        var callouts = Callouts(api, scan);
        int marked = 0, already = 0, cleared = 0, differentKinds = 0, differentHoles = 0, backMarked = 0;
        foreach (var kind in kinds)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var mine = callouts.Where(c => kind.Any(hole => HoleCalloutPlanner.SameCenter(c.Center, new SheetPoint(hole.X, hole.Y)))).ToList();
            if (mine.Count == 0)
                continue;
            var count = mine.Select(c => OppositePlanner.CalloutCount(Texts(api, c.Annotation))).FirstOrDefault(n => n is not null);
            var (result, back) = OppositePlanner.Judge(part, normal, kind[0].Kind, kind[0].Radius / scale, count);
            if (result == OppositeResult.Different)
            {
                differentKinds++;
                differentHoles += back;
            }

            var want = result == OppositeResult.Same;
            var wantBack = HoleCoveragePlanner.IsBack(HoleCoveragePlanner.Counterbore(kind, part), normal);
            foreach (var callout in mine)
            {
                var texts = Texts(api, callout.Annotation).ToList();
                var (has, hasBack) = (OppositePlanner.HasMarker(texts), HoleCoveragePlanner.HasBackMarker(texts));
                if (has == want && hasBack == wantBack)
                {
                    if (want)
                        already++;
                    continue;
                }

                var shoulder = ShoulderTowards(api, callout.Annotation, callout.Center);
                var suffix = (wantBack ? HoleCoveragePlanner.BackSuffix : string.Empty) + (want ? OppositePlanner.Suffix : string.Empty);
                api.Call(callout.Display, "IDisplayDimension", "SetText", TextSuffix, suffix);
                if (shoulder is { } s)
                    AlignShoulder(api, callout.Annotation, s.Point, s.TextLeft);
                if (want && !has)
                    marked++;
                else if (want)
                    already++;
                else if (has)
                    cleared++;
                if (wantBack && !hasBack)
                    backMarked++;
            }
        }

        return new OppositeMarks(marked, already, cleared, differentKinds, differentHoles, backMarked);
    }

    /// <summary>视图里的孔标注：注解、显示尺寸、所指孔心。</summary>
    private static List<(object Annotation, object Display, SheetPoint Center)> Callouts(SolidWorksApi api, ScannedView scan)
    {
        var callouts = new List<(object, object, SheetPoint)>();
        var dimension = api.Call(scan.View, "IView", "GetFirstDisplayDimension5");
        while (dimension is not null)
        {
            if (api.CallBool(dimension, "IDisplayDimension", "IsHoleCallout")
                && api.Call(dimension, "IDisplayDimension", "GetAnnotation") is { } annotation
                && AttachedCircleCenters(api, scan.Geometry, annotation).Cast<SheetPoint?>().FirstOrDefault() is { } center)
                callouts.Add((annotation, dimension, center));
            dimension = api.Call(dimension, "IDisplayDimension", "GetNext5");
        }

        return callouts;
    }

    /// <summary>注解显示出来的各段文字。</summary>
    private static IEnumerable<string> Texts(SolidWorksApi api, object annotation)
    {
        if (api.Call(annotation, "IAnnotation", "GetDisplayData") is not { } data)
            yield break;
        var count = api.CallInt(data, "IDisplayData", "GetTextCount");
        for (var i = 0; i < count; i++)
            yield return api.CallString(data, "IDisplayData", "GetTextAtIndex", i);
    }

    /// <summary>
    /// 孔标注现在的引线折点：下划线两端里离孔心近的那一头（引线从那头斜下去），文字在另一侧。读不到下划线返回 null。
    /// </summary>
    private static (SheetPoint Point, bool TextLeft)? ShoulderTowards(SolidWorksApi api, object annotation, SheetPoint hole)
    {
        if (Shoulder(api, annotation, textLeft: true) is not { } right || Shoulder(api, annotation, textLeft: false) is not { } left)
            return null;
        return Math.Abs(right.X - hole.X) <= Math.Abs(left.X - hole.X) ? (right, true) : (left, false);
    }

    /// <summary>
    /// 平移整个孔标注，让引线折点（文字在左时是下划线右端，在右时是左端）落在 <paramref name="target"/>。
    /// 读不到下划线就保持原位——标注已经加上，只是位置不理想。
    /// </summary>
    internal static void AlignShoulder(SolidWorksApi api, object annotation, SheetPoint target, bool textLeft)
    {
        for (var pass = 0; pass < AlignPasses; pass++)
        {
            if (Shoulder(api, annotation, textLeft) is not { } shoulder)
                return;
            var dx = target.X - shoulder.X;
            var dy = target.Y - shoulder.Y;
            if (Math.Abs(dx) < HoleCalloutPlanner.AlignTolerance && Math.Abs(dy) < HoleCalloutPlanner.AlignTolerance)
                return;
            var position = api.CallDoubles(annotation, "IAnnotation", "GetPosition");
            if (position.Length < 3
                || !api.CallBool(annotation, "IAnnotation", "SetPosition2", position[0] + dx, position[1] + dy, position[2]))
                return;
        }
    }

    /// <summary>从显示数据里读出孔标注的下划线端点（<paramref name="textLeft"/> 取右端，否则左端）。</summary>
    internal static SheetPoint? Shoulder(SolidWorksApi api, object annotation, bool textLeft)
    {
        if (api.Call(annotation, "IAnnotation", "GetDisplayData") is not { } data)
            return null;
        var count = api.CallInt(data, "IDisplayData", "GetLineCount");
        var lines = new List<SheetSegment>(count);
        for (var i = 0; i < count; i++)
        {
            // [颜色, 线型, 线样式, 线宽, 起点 xyz, 终点 xyz]
            var line = api.CallDoubles(data, "IDisplayData", "GetLineAtIndex3", i);
            if (line.Length >= 10)
                lines.Add(new SheetSegment(line[4], line[5], line[7], line[8]));
        }

        return HoleCalloutPlanner.ShoulderEnd(lines, textLeft);
    }

    /// <summary>视图里已有孔标注各自指着的孔心。</summary>
    private static List<SheetPoint> ExistingCalloutCenters(SolidWorksApi api, HoleScan.ViewGeometry geometry, object view)
    {
        var centers = new List<SheetPoint>();
        var dimension = api.Call(view, "IView", "GetFirstDisplayDimension5");
        while (dimension is not null)
        {
            if (api.CallBool(dimension, "IDisplayDimension", "IsHoleCallout")
                && api.Call(dimension, "IDisplayDimension", "GetAnnotation") is { } annotation)
                centers.AddRange(AttachedCircleCenters(api, geometry, annotation));

            dimension = api.Call(dimension, "IDisplayDimension", "GetNext5");
        }

        return centers;
    }

    /// <summary>一个注解所附着的圆边的圆心（图纸坐标）。悬空注解的附着对象读回 null，跳过。</summary>
    internal static IEnumerable<SheetPoint> AttachedCircleCenters(SolidWorksApi api, HoleScan.ViewGeometry geometry, object annotation)
    {
        // 不按附着类型码筛（1.7.0）：真机上坐标尺寸刚建好时类型码读回 [1,1]，过后再读成空数组，对象却仍是孔边，
        // 按类型码筛就认不出它连着孔，重标时删不掉。每个附着对象都试着当圆边读，不是圆边（或已失效）的读回 null。
        foreach (var entity in api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3"))
        {
            if (entity is not null && geometry.TryReadCircleCenter(entity) is { } center)
                yield return center;
        }
    }
}
