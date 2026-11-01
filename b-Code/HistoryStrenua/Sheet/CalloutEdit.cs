using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>孔标注的共用动作：插入、折点、后缀、连到哪些圆。</summary>
internal static class CalloutEdit
{
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
    internal static OppositeMarks MarkSuffixes(QuickCommandContext context, ScannedView scan, PartGeometry? part)
    {
        var api = context.Api;
        if (part is null)
        {
            AlignGroupCounts(context, scan, HoleCalloutPlanner.GroupKinds(HoleCalloutPlanner.Recognize(scan.Candidates)));
            return default;
        }

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
            if (!want)
                AlignGroupCounts(context, scan, [kind]);
            AddCounterbore(api, scan, kind, mine, part);
            var wantBack = HoleCoveragePlanner.IsBack(HoleCoveragePlanner.Counterbore(kind, part), normal);
            foreach (var callout in mine)
            {
                // 1.34.0：变量 <NUM_INST> 只数这一个特征，只能写死总数。写之前留折点，文字变长引线不跟丢。
                if (want && OppositePlanner.TotalCount(count, back) is { } total)
                {
                    var before = ShoulderTowards(api, callout.Annotation, callout.Center);
                    WriteCount(api, callout.Display, total);
                    if (before is { } b)
                        AlignShoulder(api, callout.Annotation, b.Point, b.TextLeft);
                    if (OppositePlanner.CalloutCount(Texts(api, callout.Annotation)) != total)
                        context.Report($"孔标注的数量没改成 {total}，要手工核对。");
                }

                var texts = Texts(api, callout.Annotation).ToList();
                var (has, hasBack) = (OppositePlanner.HasMarker(texts), HoleCoveragePlanner.HasBackMarker(texts));
                if (has == want && hasBack == wantBack)
                {
                    if (want)
                        already++;
                    continue;
                }

                var shoulder = ShoulderTowards(api, callout.Annotation, callout.Center);
                // 导入特征的后缀里是深度或「贯穿」，不能冲掉；向导孔这里读回是空的。
                var suffix = OppositePlanner.WithoutMarkers(api.CallString(callout.Display, "IDisplayDimension", "GetText", SwConst.TextSuffix))
                    + (wantBack ? HoleCoveragePlanner.BackSuffix : string.Empty) + (want ? OppositePlanner.Suffix : string.Empty);
                api.Call(callout.Display, "IDisplayDimension", "SetText", SwConst.TextSuffix, suffix);
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

        AlignGroupCounts(context, scan, HoleCalloutPlanner.GroupKinds(HoleCalloutPlanner.Recognize(scan.Candidates).Where(hole => hole.Slot >= 0)));
        return new OppositeMarks(marked, already, cleared, differentKinds, differentHoles, backMarked);
    }

    /// <summary>
    /// 每组孔的标注数量写成这一组在这个视图的孔数（腰型孔两端算一个）。一组有几个标注是人分开标的，分法看不出来，不改，「未标尺寸」核对合计。
    /// </summary>
    private static void AlignGroupCounts(QuickCommandContext context, ScannedView scan, IEnumerable<List<HoleEdge>> kinds)
    {
        var api = context.Api;
        var callouts = Callouts(api, scan);
        foreach (var kind in kinds)
        {
            var mine = callouts.Where(c => kind.Any(hole => HoleCalloutPlanner.SameCenter(c.Center, new SheetPoint(hole.X, hole.Y)))).ToList();
            if (mine.Count != 1)
                continue;
            var callout = mine[0];
            var group = HoleCalloutPlanner.Count(kind).Holes;
            if (OppositePlanner.GroupCount(OppositePlanner.CalloutCount(Texts(api, callout.Annotation)), group) is not { } wanted)
                continue;
            var before = ShoulderTowards(api, callout.Annotation, callout.Center);
            WriteCount(api, callout.Display, wanted);
            if (before is { } b)
                AlignShoulder(api, callout.Annotation, b.Point, b.TextLeft);
            if ((OppositePlanner.CalloutCount(Texts(api, callout.Annotation)) ?? 1) != wanted)
                context.Report($"视图「{scan.ViewName}」有个孔标注的数量没改成 {wanted}，要手工核对。");
        }
    }

    /// <summary>
    /// 写孔标注的数量。异形孔向导的数量在模板那一段；导入特征的孔标注其实是直径尺寸，模板和前缀是同一段，
    /// 往模板写会把孔径和深度一起弄丢（真机显示「5 x Ø」），要写前缀。
    /// </summary>
    private static void WriteCount(SolidWorksApi api, object display, int total)
    {
        var template = api.CallString(display, "IDisplayDimension", "GetText", TextCalloutTemplate);
        var part = template == api.CallString(display, "IDisplayDimension", "GetText", SwConst.TextPrefix) ? SwConst.TextPrefix : TextCalloutTemplate;
        api.Call(display, "IDisplayDimension", "SetText", part, OppositePlanner.WithCount(template, total));
    }

    /// <summary>
    /// 导入件的沉头孔：SolidWorks 孔标注只写选中的底孔，没有沉孔那一行，按几何补上（同心、更粗、读得到整圈内凹圆柱面才算，孔口倒角不算）。已写了的不动。
    /// </summary>
    private static void AddCounterbore(SolidWorksApi api, ScannedView scan, IReadOnlyList<HoleEdge> kind, IReadOnlyList<(object Annotation, object Display, SheetPoint Center)> mine, PartGeometry part)
    {
        var hole = kind[0];
        var wider = SplitHole.Merge(scan.Candidates)
            .Where(other => other.Radius > hole.Radius + HoleCalloutPlanner.CenterTolerance && HoleCalloutPlanner.SameCenter(other.X, other.Y, hole.X, hole.Y))
            .Select(other => (double?)other.Radius).Max();
        if (wider is not { } radius || SplitDepths(part, hole, scan.Geometry.Scale, wider).Counterbore is not { } deep)
            return;
        foreach (var callout in mine)
        {
            if (Texts(api, callout.Annotation).Any(text => text.Contains("HOLE-SPOT", StringComparison.Ordinal) || text.Contains("⌴", StringComparison.Ordinal)))
                continue;
            var before = ShoulderTowards(api, callout.Annotation, callout.Center);
            api.Call(callout.Display, "IDisplayDimension", "SetText", TextCalloutBelow, SplitHole.CounterboreLine(radius * 2 / scan.Geometry.Scale, deep));
            if (before is { } b)
                AlignShoulder(api, callout.Annotation, b.Point, b.TextLeft);
        }
    }

    /// <summary>「未标尺寸」用：这个视图里孔标注数量与图上孔数对不上的组，一组一句。</summary>
    internal static List<string> CountFaults(SolidWorksApi api, ScannedView scan)
    {
        var callouts = Callouts(api, scan);
        var faults = new List<string>();
        foreach (var kind in HoleCalloutPlanner.GroupKinds(HoleCalloutPlanner.Recognize(scan.Candidates)))
        {
            var mine = callouts.Where(c => kind.Any(hole => HoleCalloutPlanner.SameCenter(c.Center, new SheetPoint(hole.X, hole.Y)))).ToList();
            if (mine.Count == 0)
                continue;
            var counts = mine.Select(c => OppositePlanner.CalloutCount(Texts(api, c.Annotation))).ToList();
            var group = HoleCalloutPlanner.Count(kind).Holes;
            if (!OppositePlanner.CountsMatch(counts, group))
                faults.Add($"孔标注「{AnnotationRows.Shown(api, mine[0].Annotation)}」写 {counts.Sum(count => count ?? 1)} 个，这种孔在这个视图有 {group} 个");
        }

        return faults;
    }

    // swDimensionTextParts_e：孔标注下面一行。
    internal const int TextCalloutBelow = 4;

    /// <summary>盲孔的深度（通孔为 null）与沉孔深度，从零件圆柱面算；读不到零件都是 null。</summary>
    internal static (double? Depth, double? Counterbore) SplitDepths(PartGeometry? part, HoleEdge hole, double scale, double? wider)
    {
        if (part is null || hole.Axis is not { } axis || part.Holes.FirstOrDefault(axis.Matches) is not { } partHole
            || partHole.RangeAlong(partHole.Axis) is not { } range)
            return (null, null);
        double? depth = range.Max - range.Min;
        if (wider is null)
            return (depth, null);
        var probe = new PartCylinder(partHole.Axis, partHole.Point, wider.Value / scale, true, true, string.Empty, []);
        var wall = SplitHole.Whole(part.Cylinders).FirstOrDefault(cylinder => cylinder.Concave && cylinder.Full
            && Math.Abs(cylinder.Radius - probe.Radius) <= probe.Radius * HoleCalloutPlanner.RadiusTolerance && PartHole.SameAxis(probe, cylinder));
        return (depth, wall is null ? null : wall.AxialMax - wall.AxialMin);
    }

    /// <summary>视图引用的零件（1.15.0 起孔标注读一次，判沉孔朝向与对面孔共用）；装配视图不读模型，为 null。</summary>
    internal static PartGeometry? ReadPart(QuickCommandContext context, ScannedView scan)
    {
        var api = context.Api;
        var model = api.Call(scan.View, "IView", "get_ReferencedDocument");
        return model is null || api.CallInt(model, "IModelDoc2", "GetType") != SwConst.DocumentPart ? null : PartScan.Read(context, model);
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

    /// <summary>
    /// 在指定孔上加一个孔标注并把折点对准（<see cref="DimHole"/>、<see cref="CalloutPlacer"/> 用）。文字先落在目标一侧，SolidWorks 才接对引线那一头。
    /// 失败返回 null。调用前要激活视图。
    /// </summary>
    internal static object? Insert(SolidWorksApi api, ScannedView scan, int edgeIndex, CalloutSpot spot)
    {
        api.ClearSelection(scan.Document);
        var offset = spot.TextLeft ? -InitialTextOffset : InitialTextOffset;
        var callout = DimensionEdit.SelectEdge(api, scan, scan.Edges[edgeIndex], false)
            ? api.Call(scan.Document, "IDrawingDoc", "AddHoleCallout2", spot.Shoulder.X + offset, spot.Shoulder.Y, 0.0)
            : null;
        api.ClearSelection(scan.Document);
        if (callout is null || api.Call(callout, "IDisplayDimension", "GetAnnotation") is not { } annotation)
            return null;
        RestoreFitPrecision(api, callout);
        AlignShoulder(api, annotation, spot.Shoulder, spot.TextLeft);
        return annotation;
    }

    /// <summary>H7 偏差值显示三位小数：默认两位时 ⌀5H7 的 +0.012 显示成 +0.01。</summary>
    internal const int FitPrecision = 3;

    /// <summary>
    /// 同一孔已有配合时，新加的孔标注带着 H7 却回到默认精度（真机：换挂后 +0.012 变 +0.01），补回来。没有配合的不动。
    /// </summary>
    private static void RestoreFitPrecision(SolidWorksApi api, object callout)
    {
        foreach (var variable in api.CallArray(callout, "IDisplayDimension", "GetHoleCalloutVariables"))
        {
            if (variable is not null && api.CallInt(variable, "ICalloutVariable", "get_ToleranceType") == DimensionGeometry.ToleranceFitWithTolerance)
                api.Call(variable, "ICalloutLengthVariable", "set_TolerancePrecision", FitPrecision);
        }
    }

    /// <summary>一个注解所附着的圆边的圆心（图纸坐标）。悬空注解的附着对象读回 null，跳过。</summary>
    internal static IEnumerable<SheetPoint> AttachedCircleCenters(SolidWorksApi api, ViewGeometry geometry, object annotation)
    {
        // 不按附着类型码筛（1.7.0）：真机上坐标尺寸刚建好时类型码读回 [1,1]，过后再读成空数组，对象却仍是孔边，
        // 按类型码筛就认不出它连着孔，重标时删不掉。每个附着对象都试着当圆边读，不是圆边（或已失效）的读回 null。
        foreach (var entity in api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3"))
        {
            if (entity is not null && geometry.TryReadCircleCenter(entity) is { } center)
                yield return center;
        }
    }

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

    // 真机：第 5 段是孔标注模板（<NUM_INST> x <MOD-DIAM> <hw-diam> …）；第 3 段是接在前面的文字，写那里会出「12 x 6 x」。写第 5 段会冲掉后缀，所以先写数量再写后缀。
    internal const int TextCalloutTemplate = 5;

    /// <summary>
    /// 孔标注现在的引线折点：下划线两端里离孔心近的那一头（引线从那头斜下去），文字在另一侧。读不到下划线返回 null。
    /// </summary>
    internal static (SheetPoint Point, bool TextLeft)? ShoulderTowards(SolidWorksApi api, object annotation, SheetPoint hole)
    {
        if (Shoulder(api, annotation, textLeft: true) is not { } right || Shoulder(api, annotation, textLeft: false) is not { } left)
            return null;
        return Math.Abs(right.X - hole.X) <= Math.Abs(left.X - hole.X) ? (right, true) : (left, false);
    }

    /// <summary>注解显示出来的各段文字。</summary>
    internal static IEnumerable<string> Texts(SolidWorksApi api, object annotation)
    {
        if (api.Call(annotation, "IAnnotation", "GetDisplayData") is not { } data)
            yield break;
        var count = api.CallInt(data, "IDisplayData", "GetTextCount");
        for (var i = 0; i < count; i++)
            yield return api.CallString(data, "IDisplayData", "GetTextAtIndex", i);
    }

    /// <summary>视图里的孔标注：注解、显示尺寸、所指孔心。</summary>
    internal static List<(object Annotation, object Display, SheetPoint Center)> Callouts(SolidWorksApi api, ScannedView scan)
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

    /// <summary>首次落位时文字中心放在折点左边多远（图纸上 20 mm），只是让文字先落在左边。</summary>
    internal const double InitialTextOffset = 0.02;

    /// <summary>对准折点最多挪几次：挪动后 SolidWorks 可能换引线接的那一侧，要回读再挪。</summary>
    internal const int AlignPasses = 3;
}
