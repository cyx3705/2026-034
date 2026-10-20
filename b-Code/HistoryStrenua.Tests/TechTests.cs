using System.Text.Json;
using HistoryStrenua;
using HistoryVulcan.Core.Commands;

/// <summary>离线测试：要求类（Tech/）。</summary>
internal static partial class Tests
{
    static void TestTechAiParse()
    {
        // 用户模板的真实形状（机加件-钢材）：缩进的标题、第一条前面一串 PARA 格式标记、「N、」编号、句末中英文分号混用。
        const string para = "<PARA  indent=0 findent=0 indentStep=10 number=off bullet=off paraSpace=0.001 lineSpace=0.001>";
        var text = "        技术要求\n" + para + "1、未注公差参照附表执行；\n2、未注沉头孔与螺纹孔位置公差为±0.2mm，定位销孔位置公差±0.02mm;\n3、镀件表面涂防锈油；\n4、未注尺寸参考3D数模。\n";
        var items = TechAiEdit.Parse(text);
        Equal("        技术要求", items.Heading);
        Equal(para, items.Prefix);
        Equal(4, items.Items.Count);
        Equal("未注沉头孔与螺纹孔位置公差为±0.2mm，定位销孔位置公差±0.02mm;", items.Items[1]);
        // 原样拼回：标点、格式标记一个字都不动（克制的前提）。
        Equal(text.TrimEnd('\n'), items.Text);
        // 没有格式标记的模板（焊接结构件）、两位数编号、续行接到上一条。
        var weld = TechAiEdit.Parse("        技术要求\n1、甲；\n2、乙\n  乙的续行\n10、丙。");
        Equal(string.Empty, weld.Prefix);
        Equal("甲；,乙\n  乙的续行,丙。", string.Join(",", weld.Items));
        Equal("        技术要求\n1、甲；\n2、乙\n  乙的续行\n3、丙。", weld.Text);
        Equal("1：未注公差参照附表执行；", TechAiEdit.Numbered(items)[0]);
        Equal("乙 乙的续行", TechAiEdit.Plain(weld.Items[1]));

        if (Directory.Exists(TechTemplates.Folder))
        {
            foreach (var template in TechTemplates.Load())
            {
                var parsed = TechAiEdit.Parse(template.Text);
                Equal(template.Items, parsed.Items.Count);
                Equal(template.Text, parsed.Text);
            }
        }
    }

    static void TestTechAiApply()
    {
        var based = new TechItems("技术要求", "<P>", ["一", "二", "三", "四", "五"]);
        // 按原编号删除；任何新增条目都忽略。
        var outcome = TechAiEdit.Apply(based, [2, 4], [(1, "甲"), (0, "乙"), (4, "丙")]);
        Equal("一,三,五", string.Join(",", outcome.Result.Items));
        Equal("技术要求\n<P>1、一\n2、三\n3、五", outcome.Result.Text);
        Equal("2:二,4:四", string.Join(",", outcome.Deleted.Select(d => $"{d.Number}:{d.Text}")));
        Equal(3, outcome.Dropped);

        // 最多删 3 条，新增全部拒绝；编号不存在和重复删除计入拒绝数。
        var greedy = TechAiEdit.Apply(based, [1, 2, 3, 4, 9, 0, 1], [(5, "a"), (5, "b")]);
        Equal(3, greedy.Deleted.Count);
        Equal(6, greedy.Dropped);
        Equal("四,五", string.Join(",", greedy.Result.Items));
    }

    static void TestTechAiAnswers()
    {
        string[] names = ["机加件-钢材", "钣金-焊接"];
        // 选基础：带围栏、带用量脚注也读得出；null 与「无」都是「没有合适的」；不在候选里的算答错。
        var fenced = "```json\n{\"base\": \"钣金-焊接\", \"reason\": \"折弯件带焊缝\"}\n```\n\n— qwen/qwen-vl-max · 用量 1+2=3 tokens · 1.0s";
        Equal((true, (string?)"钣金-焊接", "折弯件带焊缝"), TechAiEdit.ReadChoice(fenced, names));
        Equal((true, (string?)null, "都不合适"), TechAiEdit.ReadChoice("{\"base\": null, \"reason\": \"都不合适\"}", names));
        Equal(true, TechAiEdit.ReadChoice("{\"base\": \"无\"}", names).Valid);
        Equal((false, (string?)"焊接结构件", string.Empty), TechAiEdit.ReadChoice("{\"base\": \"焊接结构件\"}", names));
        Equal(false, TechAiEdit.ReadChoice("我觉得是钣金", names).Valid);
        Equal("机加件-钢材", TechAiEdit.ReadChoice("{\"base\": \"「机加件-钢材」\"}", names).Base!);

        // 删除编号可写成字符串；任何 add/items 建议都被拒绝。
        var based = new TechItems("技术要求", string.Empty, ["一", "二", "三"]);
        var read = TechAiEdit.ReadEdit("{\"delete\": [\"3\", \"x\"], \"add\": [{\"after\": 1, \"text\": \"甲\"}, {\"text\": \"乙\"}, 5], \"reason\": \"图上没有螺纹\"}", based);
        True(read is not null, "应读出微调");
        Equal("一,二", string.Join(",", read!.Value.Outcome.Result.Items));
        Equal(4, read.Value.Outcome.Dropped);
        Equal("图上没有螺纹", read.Value.Reason);
        var items = TechAiEdit.ReadEdit("{\"items\": [\"乱加的要求\"]}", based);
        Equal("一,二,三", string.Join(",", items!.Value.Outcome.Result.Items));
        Equal(1, items.Value.Outcome.Dropped);
        Equal<object?>(null, TechAiEdit.ReadEdit("没有 JSON", based));
    }

    static void TestTechAiCommand()
    {
        // 经总线发给 Apollo 的那一行：解析回来参数原样（引号、换行、等号都编码过），带图片走识图供应商。
        var system = "只输出 JSON：{\"base\": \"…\"}";
        var prompt = "候选：[\"机加件-钢材\"]。\n图纸信息 JSON：{\"图纸\":{\"比例\":\"1:2\"}}";
        var image = @"C:\Users\x\AppData\Roaming\HistoryVulcan\ModuleData\HistoryStrenua\snapshots\零件 1-20261006.png";
        var parsed = CommandParser.Parse(TechAi.BuildCommand(system, prompt, image));
        Equal("apollo.chat.ask", parsed.Name);
        Equal(image, parsed.Named["images"]);
        Equal(system, parsed.Named["system"]);
        Equal(prompt.Replace("\n", " "), parsed.Named["prompt"]);
        True(!parsed.Named.ContainsKey("provider"), "不写 provider：带图片时 Apollo 自己走识图供应商");
        Equal("module:HistoryStrenua", TechAi.CommandSource);

        // 回执说明：选择的基础与删除项。
        var based = new TechItems("技术要求", string.Empty, ["一", "螺纹嵌钢丝牙套；", "三"]);
        var outcome = TechAiEdit.Apply(based, [2, 7], [(3, "焊后去应力")]);
        var text = TechAi.Describe("机加件-有色金属", "铝合金机加件", outcome, "图上没有螺纹孔");
        Equal("选择「机加件-有色金属」（铝合金机加件）；删 第 2 条「螺纹嵌钢丝牙套；」；AI 另给的 2 处超出上限或不合规，没采用；AI 说明：图上没有螺纹孔", text);
        Equal("选择「机加件-钢材」；没有删除条目", TechAi.Describe("机加件-钢材", string.Empty, TechAiEdit.Apply(based, [], []), string.Empty));

        // 按钮、开关的说明写清上限与退路。
        True(TechAi.Command.Usage.Contains($"最多删除 {TechAiEdit.MaxDelete} 条", StringComparison.Ordinal), TechAi.Command.Usage);
        True(StrenuaPage.TechAiSummary.Contains("AI 没成退回", StringComparison.Ordinal), StrenuaPage.TechAiSummary);
        Equal("tech", TechAi.Command.CommandClass);
    }

    static void TestTechTemplateParse()
    {
        var text = "        技术要求\r\n<PARA  indent=0 findent=0 indentStep=10 number=off bullet=off paraSpace=0.001 lineSpace=0.001>1、未注倒角C1；\r\n2、去毛刺±0.02。\r\n";
        var parsed = TechTemplates.ParseNote(NoteStyleBytes(text));
        Equal("        技术要求\n<PARA  indent=0 findent=0 indentStep=10 number=off bullet=off paraSpace=0.001 lineSpace=0.001>1、未注倒角C1；\n2、去毛刺±0.02。", parsed);
        Equal(2, TechTemplates.CountItems(parsed!));
        Equal("1、未注倒角C1； 2、去毛刺±0.02。", TechTemplates.Summary(parsed!));
        // 一个字节的长度（短文字）也认。
        Equal("技术要求\n1、甲", TechTemplates.ParseNote(NoteStyleBytes("技术要求\r\n1、甲", shortLength: true)));
        // 没有「技术要求」的不是技术要求。
        Equal<string?>(null, TechTemplates.ParseNote(NoteStyleBytes("1、甲\r\n2、乙")));
        // 长度坏了（超出文件）不读。
        var broken = NoteStyleBytes(text);
        var at = broken.Length - 4 - System.Text.Encoding.Unicode.GetByteCount(text) - 2;
        broken[at] = 0xFF;
        broken[at + 1] = 0x7F;
        Equal<string?>(null, TechTemplates.ParseNote(broken));

        var directory = Path.Combine(Path.GetTempPath(), "strenua-tech-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "钣金.sldnotestl"), NoteStyleBytes(text));
            File.WriteAllBytes(Path.Combine(directory, "坏的.sldnotestl"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(directory, "别的.sldnotefvt"), NoteStyleBytes(text));
            var all = TechTemplates.Load(directory);
            Equal("钣金", string.Join(",", all.Select(t => t.Name)));
            Equal(2, all[0].Items);
            var row = TechApply.Rows("别的", directory).Single();
            Equal("钣金", row["name"]);
            Equal("设为默认", row["setting"]);
            Equal("默认", TechApply.Rows("钣金", directory).Single()["setting"]);
            Equal("2", row["items"]);
            Equal("1、未注倒角C1； 2、去毛刺±0.02。", row["content"]);
            True(TechTemplates.Find(" 钣金 ", directory) is not null && TechTemplates.Find("没有", directory) is null, "按名字找");
            Equal(0, TechTemplates.Load(Path.Combine(directory, "不存在")).Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static void TestTechTemplateFolder()
    {
        // 用户 z 级目录里的「通用技术要求」10 份（z 级目录长期存在；不在本机就跳过这一条的实际文件部分）。
        if (!Directory.Exists(TechTemplates.Folder))
        {
            Console.WriteLine($"  （跳过：本机没有 {TechTemplates.Folder}）");
            return;
        }

        var all = TechTemplates.Load();
        Equal(10, all.Count);
        Equal(Directory.GetFiles(TechTemplates.Folder, "*" + TechTemplates.Extension).Length, all.Count);
        True(all.All(t => t.Text.Contains("技术要求", StringComparison.Ordinal) && t.Items >= 5), "每份都读出了技术要求正文");
        var steel = all.Single(t => t.Name == "机加件-钢材");
        Equal(9, steel.Items);
        True(steel.Text.StartsWith("        技术要求\n<PARA", StringComparison.Ordinal) && steel.Text.EndsWith("9、未注尺寸参考3D数模。", StringComparison.Ordinal), steel.Text);
        Equal(14, all.Single(t => t.Name == "焊接基座-带门").Items);
        Equal(6, all.Single(t => t.Name == "装配图技术要求").Items);
        True(all.Single(t => t.Name == "铝型材-防护网").Text.Contains("30×30喷塑防护网", StringComparison.Ordinal), "× 这类非 ASCII 字符照原样");
    }

    static void TestTechNoteSlots()
    {
        var a3 = SheetSpace.Standard(0.420, 0.297);
        var title = a3.TitleBlock;
        var gap = DrawingPlanner.Gap;
        var above = TechNotePlanner.Anchor(a3, NoteSlot.AboveTitle, 0.100, 0.050);
        Near(title.Right - gap, above.Right);
        Near(title.Top + gap, above.Bottom);
        var left = TechNotePlanner.Anchor(a3, NoteSlot.LeftOfTitle, 0.100, 0.050);
        Near(title.Left - gap, left.Right);
        Near(a3.Frame.Bottom + gap, left.Bottom);

        // 空图：放标题栏正上方，不挪任何东西。
        var empty = TechNotePlanner.Place(a3, [], 0.100, 0.050, null, avoid: true);
        True(empty.Free && empty.Slot == NoteSlot.AboveTitle && empty.Moves.Count == 0, "空图放上方");
        Equal(new SheetPoint(above.Left, above.Top), empty.TopLeft);

        // 上方压着视图、左侧空着：新加的先找空着的那处，不挪视图。
        var overAbove = new PlanView("主视图", null, ViewAxis.Free, [new SheetRect(0.300, 0.060, 0.400, 0.140)]);
        var plan = TechNotePlanner.Place(a3, [overAbove], 0.100, 0.050, null, avoid: true);
        True(plan.Free && plan.Slot == NoteSlot.LeftOfTitle && plan.Moves.Count == 0, "上方压着就放左侧");

        // 避障关：直接放上方，不躲不挪。
        var off = TechNotePlanner.Place(a3, [overAbove], 0.100, 0.050, null, avoid: false);
        True(off.Slot == NoteSlot.AboveTitle && off.Moves.Count == 0, "避障关直接放上方");
        Equal(new SheetPoint(above.Left, above.Top), off.TopLeft);

        // 图纸格式注解（「其余」粗糙度）压着上方：在上方这一处往上让，不跑到别处。
        var roughness = a3.With([new SheetRect(0.380, 0.050, 0.410, 0.060)]);
        var shifted = TechNotePlanner.Spot(roughness, NoteSlot.AboveTitle, 0.100, 0.050)!.Value;
        True(shifted.Bottom > 0.060 && Math.Abs(shifted.Right - above.Right) < 1e-12, "躲图纸格式注解只在本处往上让");

        // 已有的技术要求在哪一处。
        Equal(NoteSlot.LeftOfTitle, TechNotePlanner.SlotOf(a3, TechNotePlanner.Anchor(a3, NoteSlot.LeftOfTitle, 0.080, 0.030)));
        Equal(NoteSlot.AboveTitle, TechNotePlanner.SlotOf(a3, TechNotePlanner.Anchor(a3, NoteSlot.AboveTitle, 0.120, 0.060)));

        // 建图、排版估位置也只用这两处：上方压着给左侧；两处都压着说明不空，位置仍是两处之一（不跑到左下角之类的地方）。
        var (topLeft, free) = DrawingPlanner.NotePlace(a3, 0.100, 0.050, [overAbove.Rects[0]]);
        True(free && Math.Abs(topLeft.X - left.Left) < 1e-12 && Math.Abs(topLeft.Y - left.Top) < 1e-12, "估位置：上方压着给左侧");
        var (crowdedAt, crowdedFree) = DrawingPlanner.NotePlace(a3, 0.100, 0.050, [a3.Frame]);
        True(!crowdedFree && (crowdedAt == new SheetPoint(above.Left, above.Top) || crowdedAt == new SheetPoint(left.Left, left.Top)), "估位置：满了也只在两处之一");
    }

    static void TestTechNoteMakeRoom()
    {
        var a3 = SheetSpace.Standard(0.420, 0.297);
        var keep = TechNotePlanner.Anchor(a3, NoteSlot.AboveTitle, 0.100, 0.050).Inflate(DrawingPlanner.Gap / 2);
        var left = TechNotePlanner.Anchor(a3, NoteSlot.LeftOfTitle, 0.100, 0.050).Inflate(DrawingPlanner.Gap / 2);

        // 两处都压着：上方那个视图往上挪（比往左挪得少），连同它的尺寸一起；挪完不压技术要求、不压别的视图。
        var main = new PlanView("主视图", null, ViewAxis.Free, [new SheetRect(0.300, 0.060, 0.400, 0.140), new SheetRect(0.290, 0.060, 0.300, 0.140)]);
        var other = new PlanView("左视图", null, ViewAxis.Free, [new SheetRect(0.050, 0.030, 0.200, 0.120)]);
        var plan = TechNotePlanner.Place(a3, [main, other], 0.100, 0.050, null, avoid: true);
        True(plan.Free && plan.Slot == NoteSlot.AboveTitle, plan.Problem);
        Equal(1, plan.Moves.Count);
        Equal("主视图", plan.Moves[0].Name);
        True(plan.Moves[0].Dx == 0 && plan.Moves[0].Dy > 0.044 && plan.Moves[0].Dy < 0.048, $"往上挪约 45 mm，实际 {plan.Moves[0].Dy}");
        var moved = main.Rects.Select(r => new SheetRect(r.Left, r.Bottom + plan.Moves[0].Dy, r.Right, r.Top + plan.Moves[0].Dy)).ToList();
        True(!moved.Any(keep.Overlaps) && !moved.Any(other.Rects[0].Overlaps) && moved.All(r => r.Within(a3.Frame)), "挪完不压");

        // 对齐的下视图压着上方：自己只能上下挪（往上撞主视图），就挪它的父视图（主视图带着它往左）。
        var parent = new PlanView("主视图", null, ViewAxis.Free, [new SheetRect(0.250, 0.140, 0.410, 0.250)]);
        var below = new PlanView("下视图", "主视图", ViewAxis.Vertical, [new SheetRect(0.300, 0.060, 0.400, 0.130)]);
        var blocker = new PlanView("局部", null, ViewAxis.Free, [new SheetRect(0.120, 0.010, 0.230, 0.060)]);
        var aligned = TechNotePlanner.Place(a3, [parent, below, blocker], 0.100, 0.050, NoteSlot.AboveTitle, avoid: true);
        True(aligned.Free && aligned.Slot == NoteSlot.AboveTitle, aligned.Problem);
        Equal("主视图", string.Join(",", aligned.Moves.Select(m => m.Name)));
        True(aligned.Moves[0].Dy == 0 && aligned.Moves[0].Dx < 0, "主视图带着下视图往左挪");
        var belowMoved = below.Rects[0];
        belowMoved = new SheetRect(belowMoved.Left + aligned.Moves[0].Dx, belowMoved.Bottom, belowMoved.Right + aligned.Moves[0].Dx, belowMoved.Top);
        True(!belowMoved.Overlaps(keep), "跟着走的下视图让开了");

        // 只能左右挪的右视图挡着、往左会撞主视图：不能往上（对齐约束），挪父视图。
        var mainRow = new PlanView("主视图", null, ViewAxis.Free, [new SheetRect(0.150, 0.060, 0.280, 0.140)]);
        var right = new PlanView("右视图", "主视图", ViewAxis.Horizontal, [new SheetRect(0.300, 0.060, 0.360, 0.140)]);
        var rowPlan = TechNotePlanner.Place(a3, [mainRow, right, blocker], 0.100, 0.050, NoteSlot.AboveTitle, avoid: true);
        True(rowPlan.Free && rowPlan.Moves.All(m => m.Name == "主视图" || m.Dy == 0), "对齐子视图不往对齐以外的方向挪");

        // 整页都是视图、挪不开：放原处（上方），说明压着谁。
        var full = new PlanView("大视图", null, ViewAxis.Free, [new SheetRect(0.006, 0.006, 0.414, 0.291)]);
        var stuck = TechNotePlanner.Place(a3, [full], 0.100, 0.050, null, avoid: true);
        True(!stuck.Free && stuck.Slot == NoteSlot.AboveTitle && stuck.Moves.Count == 0 && stuck.Problem.Contains("大视图", StringComparison.Ordinal), stuck.Problem);
        True(!left.Overlaps(keep), "两处本身不重叠");
    }

    static void TestTechNoteReplace()
    {
        var a3 = SheetSpace.Standard(0.420, 0.297);
        // 原来在左侧：换一份（更大）仍放左侧，即使上方空着。
        var bigger = TechNotePlanner.Place(a3, [], 0.140, 0.070, NoteSlot.LeftOfTitle, avoid: true);
        True(bigger.Free && bigger.Slot == NoteSlot.LeftOfTitle && bigger.Moves.Count == 0, "原处空着就放原处");
        var rect = new SheetRect(bigger.TopLeft.X, bigger.TopLeft.Y - 0.070, bigger.TopLeft.X + 0.140, bigger.TopLeft.Y);
        Near(a3.TitleBlock.Left - DrawingPlanner.Gap, rect.Right);

        // 原处被视图压着：先在原处挪视图，不跑去另一处。
        var view = new PlanView("主视图", null, ViewAxis.Free, [new SheetRect(0.150, 0.050, 0.220, 0.120)]);
        var moved = TechNotePlanner.Place(a3, [view], 0.140, 0.070, NoteSlot.LeftOfTitle, avoid: true);
        True(moved.Free && moved.Slot == NoteSlot.LeftOfTitle && moved.Moves.Count == 1, "原处挪得开就在原处");
        // 新加的同样情况：上方空着就放上方，不挪视图。
        var fresh = TechNotePlanner.Place(a3, [view], 0.140, 0.070, null, avoid: true);
        True(fresh.Free && fresh.Slot == NoteSlot.AboveTitle && fresh.Moves.Count == 0, "新加的先找空着的");

        // 原处太宽放不下（比标题栏左边的空还宽）：换到上方。
        var wide = TechNotePlanner.Place(a3, [], 0.240, 0.040, NoteSlot.LeftOfTitle, avoid: true);
        True(wide.Free && wide.Slot == NoteSlot.AboveTitle, wide.Problem);
    }
}
