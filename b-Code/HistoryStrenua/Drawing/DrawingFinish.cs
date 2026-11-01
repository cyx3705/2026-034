using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>REQ-DQ-02：全页标注完成后重读显示几何，移动文字消除后续步骤新引入的冲突；不改尺寸值和附着。</summary>
internal static class DrawingFinish
{
    public static QuickCommand Command { get; } = new(
        "drawing-finish", StrenuaIdentity.Domain + ".drawing.finish", "整理标注",
        "当前页标注完成后，按整页轮廓、注解和图框避让线性尺寸，再按真实标注占位排版；孔标注不挪，未解冲突明确报告，不保存。",
        "不用点视图：标注后按一下，线性尺寸先沿线滑、必要时在视图外换层（同一条尺寸链整条一起换，不拆开）；不改数值、公差和附着。按全部注解真实占位排版，再复核避让。遵守避障开关；坐标尺寸、孔标注不移动（孔标注压字、引线交叉留给人或 AI 用「挪标注」修，「注解重叠」会列出来）。仍有冲突则列出标注名称，请检查后保存。",
        context => Run(context, HoleScan.ActiveDrawing(context)),
        Row: 2, Page: "onekey");

    internal static QuickOutcome Run(QuickCommandContext context, object drawing)
    {
        if (!context.Options.Clearance) return QuickOutcome.Ok("整理标注：避障关闭，未移动标注。");
        context.SetState("整理标注");
        // 1.32.2（用户定）：排版前视图还贴得近，第一遍只看本视图；视图让开后第二遍才看整页。
        var first = Clear(context, drawing, ownViewOnly: true);
        // 用户定：超出图框优先调位置。先保持比例排、把出框的标注拉回来；视图本身或标注实在回不来才降比例重来。
        var arranged = DrawingArrange.Run(context, DrawingSheet.Read(context, drawing, "整理标注"), keepIso: true, keepScale: true);
        var last = Clear(context, drawing);
        if (last.Outside.Count > 0)
        {
            context.Report($"整理标注：{string.Join("、", last.Outside)} 保持比例放不进图框，比例降一档再排。");
            arranged = DrawingArrange.Run(context, DrawingSheet.Read(context, drawing, "整理标注"), keepIso: true);
            last = Clear(context, drawing);
        }

        var message = $"整理标注：避让移动 {first.Moved + last.Moved} 次，剩余 {last.Stuck.Count} 个文字冲突（孔标注不挪，引线交叉、过长见「注解重叠」）。"
            + (last.Stuck.Count > 0 ? "\n· " + string.Join("、", last.Stuck) : string.Empty)
            + (last.Outside.Count > 0 ? "\n· 还在图框外：" + string.Join("、", last.Outside) : string.Empty)
            + "\n" + arranged.Message;
        return last.Stuck.Count == 0 && last.Outside.Count == 0 && arranged.Success ? QuickOutcome.Ok(message) : QuickOutcome.Fail(message);
    }

    private sealed record Shape(object Annotation, string Name, string ViewName, SheetRect View, bool Movable, bool Callout,
        IReadOnlyList<SheetSegment> Lines, IReadOnlyList<TextBox> Texts);

    private static (int Moved, List<string> Stuck, List<string> Outside) Clear(QuickCommandContext context, object drawing, bool ownViewOnly = false)
    {
        var api = context.Api;
        var shapes = new List<Shape>();
        // 同一条尺寸链的另外几段（shapes 下标）：1.30.0 用户定，换行要整条一起走，否则链就断了。
        var chainPeers = new Dictionary<int, List<int>>();
        var frameLines = DrawingSheet.FrameLines(api, drawing).ToList();
        var geometry = new Dictionary<string, List<SheetSegment>>();
        foreach (var view in HoleScan.SheetViews(api, drawing))
        {
            var name = DrawingSheet.Name(api, view);
            var rect = DrawingSheet.Outline(api, view);
            ScannedView? scanned = null;
            if (api.Call(view, "IView", "get_ReferencedDocument") is not null && !HoleScan.Frame(context, view).Axonometric)
            {
                var scan = HoleScan.Scan(context, "整理标注", withLines: true, view: view, quiet: true);
                scanned = scan;
                var own = geometry[name] = [.. scan.Lines, .. scan.CurveSegments];
                foreach (var hole in scan.Candidates) own.AddRange(ClearancePlanner.Octagon(new SheetPoint(hole.X, hole.Y), hole.Radius));
            }
            foreach (var annotation in api.CallArray(view, "IView", "GetAnnotations"))
            {
                var (lines, texts) = Clearance.DisplayGeometry(api, annotation);
                var annotationType = api.CallInt(annotation, "IAnnotation", "GetType");
                var display = annotationType == 4
                    ? api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") : null;
                var callout = display is not null && api.CallBool(display, "IDisplayDimension", "IsHoleCallout");
                var noteText = annotationType == 6 && api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is { } note
                    ? api.CallString(note, "INote", "GetText") : string.Empty;
                // 框架一键的“采用 xx 铝型材”是普通注释，不能留在轴测图边线上；它与尺寸一样可沿图纸移动，
                // 引线会跟随注释。其他用户普通注释保持原位。
                // 1.24.0（用户定）：孔标注不挪——整页逐个候选试挪又慢又会把它推到远处、引线交叉；交错就留着，由人或 AI 用「挪标注」修。
                var movable = display is not null && !callout && HolePositionPlanner.IsLinear(api.CallInt(display, "IDisplayDimension", "get_Type2"))
                    || noteText.Contains("铝型材", StringComparison.Ordinal);
                if (movable && !callout && display is not null && api.Call(display, "IDisplayDimension", "GetDimension2", 0) is { } dimension)
                {
                    var mm = Convert.ToDouble(api.Call(dimension, "IDimension", "get_SystemValue")) * 1000;
                    // 只去掉精确整数/一位小数多显示的零；47.207 之类模型真实尺寸绝不四舍五入成 47.2。
                    if (ExactPrecision(mm) is { } precision)
                    {
                        api.Call(display, "IDisplayDimension", "SetPrecision3", precision,
                            api.CallInt(display, "IDisplayDimension", "GetAlternatePrecision2"),
                            api.CallInt(display, "IDisplayDimension", "GetPrimaryTolPrecision2"),
                            api.CallInt(display, "IDisplayDimension", "GetAlternateTolPrecision2"));
                        (lines, texts) = Clearance.DisplayGeometry(api, annotation);
                    }
                }
                shapes.Add(new Shape(annotation, name + "/" + api.CallString(annotation, "IAnnotation", "GetName"), name, rect, movable, callout, lines, texts));
            }

            if (scanned is not null)
                ReadChains(api, scanned, name, shapes, chainPeers);
        }
        var keepouts = DrawingSheet.KeepOutRects(api, drawing).Select(r => new TextBox(r.Left, r.Bottom, r.Width, r.Height)).ToList();
        var frame = DrawingSheet.FrameRect(api, drawing);
        if (frame is null) throw new QuickCommandException("整理标注：读不到图框，未移动标注。");
        var moved = 0;
        Obstacles ObstaclesFor(int i) => ObstaclesWithout([i]);
        Obstacles ObstaclesWithout(IReadOnlyCollection<int> skip)
        {
            var owner = ownViewOnly ? shapes[skip.First()].ViewName : null;
            var others = shapes.Where((s, j) => !skip.Contains(j) && (owner is null || s.ViewName == owner)).ToList();
            var views = geometry.Where(view => owner is null || view.Key == owner).SelectMany(view => view.Value);
            return new(frameLines.Concat(views).Concat(others.SelectMany(s => s.Lines)).ToList(), keepouts.Concat(others.SelectMany(s => s.Texts)).ToList());
        }

        int Hits(Shape s, Obstacles obstacles) => ClearancePlanner.Hits(s.Texts, obstacles)
            + (AnnotationPlacementPlanner.Within(s.Texts, frame.Value) ? 0 : 100);
        // 已离开尺寸链的放不下的字（只算这一遍；下一遍读回时它和链已不同行，也不在 chainPeers 里）。字还对着自己的间距、又没有同行的链，也算离开了。
        var left = new HashSet<int>();
        var rowsMoved = new HashSet<int>();
        bool OnChain(int index) => !left.Contains(index) && (chainPeers.ContainsKey(index) || !ClearancePlanner.OverGap(shapes[index].Texts, shapes[index].Lines));
        // 后加的尺寸界线也会压到先前文字，所以整页跑有上限的三轮。每次读回确认变好才保留，避免反复漂移。
        for (var pass = 0; pass < 3; pass++)
        {
            var changed = false;
            bool Seat(int index)
            {
                var shape = shapes[index];
                if (!shape.Movable || shape.Texts.Count != 1
                    || Clearance.SeatDimension(api, shape.Annotation, shape.Lines, shape.Texts,
                        ClearancePlanner.ForSeat(ObstaclesFor(index), shape.Lines, shape.Texts[0].Angle,
                            shapes.Where((other, j) => j != index && other.Movable).Select(other => (other.Lines, other.Texts)))) is not { } seat)
                    return false;
                shapes[index] = shape with { Lines = seat.Lines, Texts = seat.Texts };
                if (seat.Moved)
                {
                    moved++;
                    changed = true;
                }

                return true;
            }

            // 放得下的字被上一层尺寸界线的尾巴挡住（真机底层1 的两个 40）：不把字挪走，整条链往外退一层，一层 4 mm，最多 6 层。
            // 只退这一侧最外层的链，每条链一遍只退一次：里层的链退出去，又会用界线挡住外层，互相追着退到图框边（首版真机）。
            bool MoveRow(int index, TextBox? yieldTo = null)
            {
                var shape = shapes[index];
                if (!shape.Movable || shape.Texts.Count != 1 || ClearancePlanner.Squeezed(shape.Texts, shape.Lines)
                    || ClearancePlanner.Seat(shape.Texts, shape.Lines, Obstacles.Empty) is null)
                    return false;
                var group = (chainPeers.GetValueOrDefault(index) ?? []).Append(index).ToList();
                if (group.Any(rowsMoved.Contains))
                    return false;
                var free = ObstaclesWithout(group);
                var members = group.Select(g => (shapes[g].Lines, shapes[g].Texts)).ToList();
                var (vx, vy) = (-Math.Sin(shape.Texts[0].Angle), Math.Cos(shape.Texts[0].Angle));
                var (centerX, centerY) = ((shape.View.Left + shape.View.Right) / 2, (shape.View.Bottom + shape.View.Top) / 2);
                var sign = (shape.Texts[0].X - centerX) * vx + (shape.Texts[0].Y - centerY) * vy >= 0 ? 1 : -1;
                double Out(TextBox text) => sign * (text.X * vx + text.Y * vy);
                // 放不下的字可能已经离开自己那一行，不算一层。
                var outermost = shapes.Where((other, j) => other.ViewName == shape.ViewName && other.Movable && !group.Contains(j) && other.Texts.Count == 1
                        && !ClearancePlanner.Squeezed(other.Texts, other.Lines)
                        && Math.Abs(other.Texts[0].Angle - shape.Texts[0].Angle) < 1e-3)
                    .All(other => Out(other.Texts[0]) <= group.Max(g => Out(shapes[g].Texts[0])) + 1e-4);
                if (!outermost)
                    return false;
                for (var tier = 1; tier <= 6; tier++)
                {
                    var (dx, dy) = (sign * tier * 0.004 * vx, sign * tier * 0.004 * vy);
                    if (!ClearancePlanner.RowFits(members, dx, dy, free, frame.Value)
                        || (yieldTo is { } text && members.Any(member => ClearancePlanner.StillBlocks(member.Lines, shape.Texts[0].Angle, dx, dy, text))))
                        continue;
                    foreach (var member in group)
                    {
                        var at = api.CallDoubles(shapes[member].Annotation, "IAnnotation", "GetPosition");
                        if (at.Length < 3)
                            continue;
                        api.CallBool(shapes[member].Annotation, "IAnnotation", "SetPosition2", at[0] + dx, at[1] + dy, at[2]);
                        var (lines, texts) = Clearance.DisplayGeometry(api, shapes[member].Annotation);
                        shapes[member] = shapes[member] with { Lines = lines, Texts = texts };
                    }

                    rowsMoved.UnionWith(group);
                    moved++;
                    changed = true;
                    return true;
                }

                return false;
            }

            // 1.32.6（用户定）：先让放得下的字都回到界线之间，再给放不下的字找地方，免得它挤别人。
            for (var i = 0; i < shapes.Count; i++)
            {
                if (!Seat(i))
                    MoveRow(i);
            }

            for (var i = 0; i < shapes.Count; i++)
                Seat(i);
            var placed = new HashSet<int>();
            for (var i = 0; i < shapes.Count; i++)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var shape = shapes[i];
                if (!shape.Movable || !ClearancePlanner.Squeezed(shape.Texts, shape.Lines)
                    || ClearancePlanner.PlaceSqueezed(shape.Texts[0], shape.Lines, shape.View, frame.Value, ObstaclesFor(i), OnChain(i)) is not { } offset)
                    continue;
                placed.Add(i);
                if (offset.X == 0 && offset.Y == 0)
                    continue;
                var position = api.CallDoubles(shape.Annotation, "IAnnotation", "GetPosition");
                if (position.Length < 3 || !api.CallBool(shape.Annotation, "IAnnotation", "SetPosition2", position[0] + offset.X, position[1] + offset.Y, position[2]))
                    continue;
                var (lines, texts) = Clearance.DisplayGeometry(api, shape.Annotation);
                shapes[i] = shape with { Lines = lines, Texts = texts };
                moved++;
                changed = true;
                // 离开了这一行（沿垂直尺寸线的方向挪了）：不再和那条链一起走。
                var (sin, cos) = (Math.Sin(shape.Texts[0].Angle), Math.Cos(shape.Texts[0].Angle));
                if (Math.Abs(-offset.X * sin + offset.Y * cos) > 1e-6)
                {
                    foreach (var peer in chainPeers.GetValueOrDefault(i) ?? [])
                        chainPeers[peer].Remove(i);
                    chainPeers.Remove(i);
                    left.Add(i);
                }
            }

            // 1.34.0（用户定，后板 304）：放不下的短尺寸跑到上面后，压着它的最外层长尺寸往外让，字不再飞远。
            foreach (var i in placed)
            {
                var text = shapes[i].Texts[0];
                for (var j = 0; j < shapes.Count; j++)
                {
                    var other = shapes[j];
                    if (j != i && other.Movable && other.ViewName == shapes[i].ViewName && other.Texts.Count == 1 && !ClearancePlanner.Squeezed(other.Texts, other.Lines)
                        && Math.Abs(other.Texts[0].Angle - text.Angle) < 1e-3 && ClearancePlanner.StillBlocks(other.Lines, text.Angle, 0, 0, text))
                        MoveRow(j, text);
                }
            }

            for (var i = 0; i < shapes.Count; i++)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                if (placed.Contains(i)) continue;
                var shape = shapes[i];
                if (shape.Texts.Count == 0) continue;
                // 孔标注、半径尺寸平时不挪；出了图框的例外（用户定：标注不许超出图框）。
                var outside = !AnnotationPlacementPlanner.Within(shape.Texts, frame.Value);
                if (!shape.Movable && !outside) continue;
                var obstacles = ObstaclesFor(i);
                if (Seat(i) || MoveRow(i)) continue;
                var hits = Hits(shape, obstacles);
                if (hits == 0) continue;
                var position = api.CallDoubles(shape.Annotation, "IAnnotation", "GetPosition");
                if (position.Length < 3) continue;
                var peers = chainPeers.GetValueOrDefault(i) ?? [];
                var horizontal = Math.Abs(Math.Cos(shape.Texts[0].Angle)) > 0.9;
                // 只有离开这一行才带上同链的另外几段，它们只平移垂直于尺寸线的那一截；沿线滑不影响链。
                SheetPoint Across(SheetPoint offset) => horizontal ? new SheetPoint(0, offset.Y) : new SheetPoint(offset.X, 0);
                bool Leaves(SheetPoint offset) => peers.Count > 0 && Math.Abs(horizontal ? offset.Y : offset.X) > 1e-9;
                var chainObstacles = peers.Count > 0 ? ObstaclesWithout(peers.Append(i).ToList()) : obstacles;
                var clean = AnnotationPlacementPlanner.Offsets(shape.Texts, shape.View, shape.Callout).Where(offset =>
                {
                    var moved = ClearancePlanner.Shift(shape.Texts, offset.X, offset.Y);
                    if (!Leaves(offset))
                        return AnnotationPlacementPlanner.Within(moved, frame.Value) && ClearancePlanner.Hits(moved, obstacles) == 0;
                    var across = Across(offset);
                    return AnnotationPlacementPlanner.Within(moved, frame.Value) && ClearancePlanner.Hits(moved, chainObstacles) == 0
                        && peers.All(peer =>
                        {
                            var shifted = ClearancePlanner.Shift(shapes[peer].Texts, across.X, across.Y);
                            return AnnotationPlacementPlanner.Within(shifted, frame.Value) && ClearancePlanner.Hits(shifted, chainObstacles) == 0;
                        });
                });
                // 找不到全空的地方：出框的宁可压字也要回到框内。
                foreach (var offset in clean.Concat(AnnotationPlacementPlanner.Inward(shape.Texts, frame.Value)))
                {
                    var accepted = false;
                    var carried = new List<(int Index, double[] Position)>();
                    try
                    {
                        if (!api.CallBool(shape.Annotation, "IAnnotation", "SetPosition2", position[0] + offset.X, position[1] + offset.Y, position[2])) continue;
                        if (Leaves(offset))
                        {
                            var across = Across(offset);
                            foreach (var peer in peers)
                            {
                                var at = api.CallDoubles(shapes[peer].Annotation, "IAnnotation", "GetPosition");
                                if (at.Length < 3) continue;
                                carried.Add((peer, at));
                                api.CallBool(shapes[peer].Annotation, "IAnnotation", "SetPosition2", at[0] + across.X, at[1] + across.Y, at[2]);
                            }
                        }

                        var (lines, texts) = Clearance.DisplayGeometry(api, shape.Annotation);
                        var after = shape with { Lines = lines, Texts = texts };
                        // SW 移动孔标注时会换引线端、重排公差文字；预测平移不等于实际显示，必须读回。
                        var crossing = lines.Count(line => obstacles.Texts.Any(text => ClearancePlanner.Hits(text, line)));
                        var beforeCrossing = shape.Lines.Count(line => obstacles.Texts.Any(text => ClearancePlanner.Hits(text, line)));
                        if (texts.Count > 0 && Hits(after, obstacles) < hits && crossing <= beforeCrossing)
                        {
                            shapes[i] = after; moved++; changed = true; accepted = true;
                            foreach (var (peer, _) in carried)
                            {
                                var (peerLines, peerTexts) = Clearance.DisplayGeometry(api, shapes[peer].Annotation);
                                shapes[peer] = shapes[peer] with { Lines = peerLines, Texts = peerTexts };
                            }
                        }
                    }
                    finally
                    {
                        // 中途取消、COM 失败或试探没改善都要恢复原位置；失败不能继续用过期几何。
                        if (!accepted && !Convert.ToBoolean(api.CallForCleanup(shape.Annotation, "IAnnotation", "SetPosition2", position[0], position[1], position[2])))
                            throw new QuickCommandException("整理标注：无法还原「" + shape.Name + "」的试探位置，请检查此标注。");
                        if (!accepted)
                        {
                            foreach (var (peer, at) in carried)
                                api.CallForCleanup(shapes[peer].Annotation, "IAnnotation", "SetPosition2", at[0], at[1], at[2]);
                        }
                    }
                    if (accepted) break;
                }
            }
            if (!changed) break;
        }
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        api.Call(drawing, "IModelDoc2", "GraphicsRedraw2");
        var outsideNames = shapes.Where(s => s.Texts.Count > 0 && !AnnotationPlacementPlanner.Within(s.Texts, frame.Value)).Select(s => s.Name).ToList();
        foreach (var view in HoleScan.SheetViews(api, drawing))
        {
            var box = DrawingSheet.Outline(api, view);
            if (box.Left < frame.Value.Left || box.Right > frame.Value.Right || box.Bottom < frame.Value.Bottom || box.Top > frame.Value.Top)
                outsideNames.Add("视图「" + DrawingSheet.Name(api, view) + "」");
        }

        return (moved, shapes.Where((s, i) => s.Movable && s.Texts.Count > 0 && Hits(s, ObstaclesFor(i)) > 0 && ClearancePlanner.Seat(s.Texts, s.Lines, ObstaclesFor(i)) is not 0.0
            && !(ClearancePlanner.Squeezed(s.Texts, s.Lines) && ClearancePlanner.PlaceSqueezed(s.Texts[0], s.Lines, s.View, frame.Value, ObstaclesFor(i), OnChain(i)) is { X: 0, Y: 0 })).Select(s => s.Name).ToList(), outsideNames);
    }

    /// <summary>这个视图里同一行首尾相接的尺寸记进 <paramref name="peers"/>（shapes 下标 → 同链另外几段）。</summary>
    private static void ReadChains(SolidWorksApi api, ScannedView scan, string viewName, List<Shape> shapes, Dictionary<int, List<int>> peers)
    {
        var dimensions = DimensionScan.Read(api, scan);
        var check = new CheckView(viewName, string.Empty, scan.Geometry.Frame, scan.Lines, scan.Candidates, scan.Arcs, dimensions.Select(item => item.Geometry).ToList(), []);
        foreach (var group in DimensionCheckPlanner.ChainGroups(check, ChainCheck.Positions(api, dimensions)))
        {
            var members = group
                .Select(index => shapes.FindIndex(shape => shape.Name == viewName + "/" + api.CallString(dimensions[index].Annotation, "IAnnotation", "GetName")))
                .Where(index => index >= 0)
                .ToList();
            foreach (var member in members)
                peers[member] = members.Where(other => other != member).ToList();
        }
    }

    internal static int? ExactPrecision(double millimeters)
        => !double.IsFinite(millimeters) ? null
            : Math.Abs(millimeters - Math.Round(millimeters)) < 1e-7 ? 0
            : Math.Abs(millimeters - Math.Round(millimeters, 1)) < 1e-7 ? 1 : null;
}
