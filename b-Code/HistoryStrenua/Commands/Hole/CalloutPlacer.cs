namespace HistoryStrenua;

/// <summary>
/// 快捷指令「孔标注选位」：整页标完以后，给每个孔标注重新挑位置，必要时换挂到同种里更合适的孔上。规划见 <see cref="CalloutPlacePlanner"/>。
/// </summary>
/// <remarks>
/// 孔标注只在这里排一次（用户定）：放在一键流程最后，视图和别的标注都已定、不再动，障碍是整页所有视图的所有标注；在此之前别的避障都不看孔标注。
/// 这里读一遍、算好、每个只写一次，不做真挪试错。
/// </remarks>
internal static class CalloutPlacer
{
    public static QuickCommand Command { get; } = new(
        Key: "hole-place",
        CommandName: StrenuaIdentity.Domain + ".hole.place",
        Title: "孔标注选位",
        Summary: "当前图纸页全部孔标注按整页的线、字和图框重新挑位置，可换挂到同种的另一个孔，不保存。",
        Usage: "不用点视图：标完尺寸后按。每个孔标注在同种孔四周找字不压东西、引线短且不穿尺寸的位置，比现在好才挪，所以重复按不会乱动。「(反面)」「(含对面)」换挂后自动补回。切成两段的孔（直径尺寸）只挪不换孔。",
        Run: context => Run(context, DrawingAccess.ActiveDrawing(context)));



    /// <summary>新位置至少比现在好这么多分（约等于引线短 5 mm）才动。</summary>
    private const double WorthMoving = 5;

    private sealed class Item
    {
        public required ScannedView Scan { get; init; }
        public required object Annotation { get; set; }
        public object? Display { get; set; }
        public required int Type { get; init; }
        public required bool Target { get; init; }
        public required bool Retarget { get; init; }
        public SheetPoint? Center { get; set; }
        public List<SheetSegment> Lines { get; set; } = [];
        public List<TextBox> Texts { get; set; } = [];
        public bool Pending { get; set; }
    }

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="drawing">工程图。</param>
    /// <param name="onlyView">只排这个视图的孔标注（单独按「孔标注」时）；障碍仍是整页。null 排整页。</param>
    internal static QuickOutcome Run(QuickCommandContext context, object drawing, string? onlyView = null)
    {
        var api = context.Api;
        context.SetState("孔标注选位");
        var viewLines = new List<SheetSegment>();
        var allHoles = new List<HoleEdge>();
        var blocks = DrawingSheet.KeepOutRects(api, drawing).ToList();
        var items = new List<Item>();
        foreach (var view in DrawingAccess.SheetViews(api, drawing))
        {
            if (api.Call(view, "IView", "get_ReferencedDocument") is null)
                continue;
            if (DrawingAccess.Frame(context, view).Axonometric)
            {
                blocks.Add(DrawingSheet.Outline(api, view));
                continue;
            }

            var scan = HoleScan.Scan(context, "孔标注选位", withLines: true, view: view, quiet: true);
            viewLines.AddRange(scan.Lines);
            viewLines.AddRange(scan.CurveSegments);
            foreach (var hole in scan.Candidates)
                viewLines.AddRange(ClearancePlanner.Octagon(new SheetPoint(hole.X, hole.Y), hole.Radius));
            allHoles.AddRange(HoleCalloutPlanner.Recognize(scan.Candidates));
            items.AddRange(ReadItems(context, scan, view));
        }

        if (DrawingSheet.FrameRect(api, drawing) is not { } frame)
            return QuickOutcome.Fail("孔标注选位：读不到图纸大小。");
        var targets = items.Where(item => item.Target && (onlyView is null || item.Scan.ViewName == onlyView)).OrderByDescending(item => item.Texts.Sum(text => text.Width)).ToList();
        if (targets.Count == 0)
            return QuickOutcome.Nothing("孔标注选位：当前图纸页没有孔标注。");

        int moved = 0, rehung = 0, stayed = 0, refused = 0;
        var parts = new Dictionary<ScannedView, PartGeometry?>();
        // 还没排到的孔标注不算障碍，否则先排的要躲它们马上就要离开的位置。
        foreach (var item in targets)
            item.Pending = true;
        foreach (var item in targets)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            item.Pending = false;
            var field = Field(items, item, viewLines, blocks, frame) with { Holes = allHoles };
            var holes = KindOf(item);
            if (holes.Count == 0 || CalloutEdit.Shoulder(api, item.Annotation, textLeft: true) is not { } shoulder)
            {
                stayed++;
                continue;
            }

            var current = CalloutPlacePlanner.Cost(item.Texts, LeaderPlanner.Leaders(item.Lines), field);
            if (CalloutPlacePlanner.Choose(item.Texts, shoulder.Y, holes, field) is not { } pick || pick.Cost > current - WorthMoving)
            {
                stayed++;
                continue;
            }

            var center = new SheetPoint(pick.Hole.X, pick.Hole.Y);
            if (item.Center is { } now && HoleCalloutPlanner.SameCenter(now, center))
            {
                var (dx, dy) = ClearancePlanner.CalloutShift(item.Texts, shoulder.Y, pick.Spot);
                var position = api.CallDoubles(item.Annotation, "IAnnotation", "GetPosition");
                if (position.Length >= 3 && api.CallBool(item.Annotation, "IAnnotation", "SetPosition2", position[0] + dx, position[1] + dy, position[2]))
                {
                    CalloutEdit.AlignShoulder(api, item.Annotation, pick.Spot.Shoulder, pick.Spot.TextLeft);
                    var after = api.CallDoubles(item.Annotation, "IAnnotation", "GetPosition");
                    if (after.Length >= 2 && Math.Abs(after[0] - position[0]) + Math.Abs(after[1] - position[1]) > 1e-4)
                        moved++;
                    else
                        stayed++;
                }
            }
            else
            {
                // 先加新的、成了再删旧的：SolidWorks 不接受时图上还留着原来那个。
                _ = api.Call(drawing, "IDrawingDoc", "ActivateView", item.Scan.ViewName);
                if (CalloutEdit.Insert(api, item.Scan, pick.Hole.Index, pick.Spot) is not { } fresh)
                {
                    refused++;
                    continue;
                }

                DimensionEdit.Discard(api, item.Scan, item.Display!);
                item.Annotation = fresh;
                item.Display = api.Call(fresh, "IAnnotation", "GetSpecificAnnotation");
                item.Center = center;
                // 新标注回到 SolidWorks 默认文字，马上补回数量、沉孔与后缀，后面的标注才躲得准。
                if (!parts.TryGetValue(item.Scan, out var part))
                    parts[item.Scan] = part = CalloutEdit.ReadPart(context, item.Scan);
                CalloutEdit.MarkSuffixes(context, item.Scan, part);
                CalloutEdit.AlignShoulder(api, fresh, pick.Spot.Shoulder, pick.Spot.TextLeft);
                rehung++;
            }

            (item.Lines, item.Texts) = Clearance.DisplayGeometry(api, item.Annotation);
        }

        api.ClearSelection(drawing);
        api.Redraw(drawing);
        var outside = targets.Where(item => !AnnotationPlacementPlanner.Within(item.Texts, frame)).ToList();
        if (outside.Count > 0)
            return QuickOutcome.Fail($"孔标注选位：{targets.Count} 个孔标注，挪了 {moved} 个，换挂 {rehung} 个，但 {outside.Count} 个找不到位置、还在图框外："
                + string.Join("、", outside.Select(item => api.CallString(item.Annotation, "IAnnotation", "GetName"))) + "。");
        return moved + rehung + refused == 0
            ? QuickOutcome.Nothing($"孔标注选位：{targets.Count} 个孔标注都已在合适的位置。")
            : QuickOutcome.Ok($"孔标注选位：{targets.Count} 个孔标注，挪了 {moved} 个，换挂到同种别的孔 {rehung} 个，{stayed} 个原地不动"
                + (refused > 0 ? $"，{refused} 个换挂时 SolidWorks 没接受、留在原处" : string.Empty) + "。");
    }

    private static IEnumerable<Item> ReadItems(QuickCommandContext context, ScannedView scan, object view)
    {
        var api = context.Api;
        var holes = HoleCalloutPlanner.Recognize(scan.Candidates);
        foreach (var annotation in api.CallArray(view, "IView", "GetAnnotations"))
        {
            if (annotation is null)
                continue;
            Item? item = null;
            try
            {
                var type = api.CallInt(annotation, "IAnnotation", "GetType");
                var display = type == SwConst.AnnotationDimension ? api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") : null;
                var callout = display is not null && api.CallBool(display, "IDisplayDimension", "IsHoleCallout");
                SheetPoint? center = null;
                var split = false;
                if (display is not null)
                {
                    center = CalloutEdit.AttachedCircleCenters(api, scan.Geometry, annotation).Cast<SheetPoint?>().FirstOrDefault();
                    split = !callout && center is { } c && api.CallInt(display, "IDisplayDimension", "get_Type2") == SwConst.DiameterDimension
                        && holes.Any(hole => hole.Split && HoleCalloutPlanner.SameCenter(c, new SheetPoint(hole.X, hole.Y)));
                }

                var (lines, texts) = Clearance.DisplayGeometry(api, annotation);
                item = new Item
                {
                    Scan = scan, Annotation = annotation, Display = display, Type = type,
                    Target = (callout || split) && center is not null && texts.Count > 0,
                    Retarget = callout, Center = center, Lines = lines, Texts = texts,
                };
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
            {
                // 对象已失效的注解跳过，同注解重叠。
            }

            if (item is not null)
                yield return item;
        }
    }

    /// <summary>这个标注能挂的孔：孔标注是同种全部孔（腰型孔、切段孔只留现在这个）。</summary>
    private static List<HoleEdge> KindOf(Item item)
    {
        if (item.Center is not { } center)
            return [];
        var holes = HoleCalloutPlanner.Recognize(item.Scan.Candidates);
        var own = holes.Where(hole => HoleCalloutPlanner.SameCenter(center, new SheetPoint(hole.X, hole.Y))).ToList();
        if (own.Count == 0 || !item.Retarget || own[0].Slot >= 0 || own[0].Split)
            return own;
        return HoleCalloutPlanner.GroupKinds(holes).First(kind => kind.Any(hole => hole.Index == own[0].Index))
            .Where(hole => hole.Slot < 0 && !hole.Split).ToList();
    }

    private static CalloutField Field(List<Item> items, Item self, List<SheetSegment> viewLines, List<SheetRect> blocks, SheetRect frame)
    {
        var marks = new List<SheetSegment>();
        var dimensions = new List<SheetSegment>();
        var leaders = new List<SheetSegment>();
        var texts = new List<TextBox>();
        foreach (var other in items.Where(other => !ReferenceEquals(other, self) && !other.Pending))
        {
            texts.AddRange(other.Texts);
            if (other.Type is SwConst.AnnotationCenterMark or SwConst.AnnotationCenterLine)
            {
                marks.AddRange(other.Lines);
            }
            else if (other.Target)
            {
                var own = LeaderPlanner.Leaders(other.Lines);
                leaders.AddRange(own);
                dimensions.AddRange(other.Lines.Except(own));
            }
            else
            {
                dimensions.AddRange(other.Lines);
            }
        }

        return new CalloutField(viewLines, marks, dimensions, leaders, texts, blocks, frame);
    }
}
