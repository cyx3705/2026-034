using System.Text.Json.Nodes;
using HistoryStrenua;

/// <summary>蚂蚁工场选型（1.37.0）：料号、参数配对与选项匹配。样本取自 2026-11-02 实测的接口回包。</summary>
internal static partial class Tests
{
    static void TestMymoooRules()
    {
        // 料号：方括号段还在就没选全；span 标签去掉给人读。
        True(!MymoooRules.Complete("SBH1-M8-L<span>[10,12,14]</span>-<span>[S11,S21]</span>"), "待选段未定");
        True(!MymoooRules.Complete("PDXZ-D10-L<span>[15-800/1]</span>"), "区间段未定");
        True(!MymoooRules.Complete(""), "型式没定时料号是空的");
        True(MymoooRules.Complete("SBH1-M8-L20-S11"), "选全");
        Equal("PDXZ-D10-L[15-800/1]", MymoooRules.Plain("PDXZ-D10-L<span>[15-800/1]</span>"));

        // 配对：顺序保留，中英文分隔都认；写了不是 名=值 的拒。
        var pairs = MymoooRules.Pairs("M=8，L=20;XN = S11", "params");
        Equal("M=8|L=20|XN=S11", string.Join("|", pairs.Select(pair => $"{pair.Key}={pair.Value}")));
        Equal(0, MymoooRules.Pairs(null, "params").Count);
        Throws(() => MymoooRules.Pairs("M8", "params"), "缺等号");
        Throws(() => MymoooRules.Pairs("M=", "params"), "缺值");

        Equal("A_B_C", MymoooRules.FileName("A/B:C"));

        // 枚举参数：按显示值给 optionId，数值按大小认；对不上为 null。
        var parameters = JsonNode.Parse("""
            [{"paramId":245304,"paramCode":"M","paramName":"M","paramType":"M","paramValues":[
                {"optionId":3689839,"optionValue":"8","optionText":"8","toValue":""},
                {"optionId":3689840,"optionValue":"10","optionText":"10","toValue":""}]},
             {"paramId":245310,"paramCode":"CZ","paramName":"材质","paramType":"N","paramValues":[
                {"optionId":3689936,"optionValue":"SUS 304","optionText":"SUS 304","toValue":""}]},
             {"paramId":1,"paramCode":"L","paramName":"L","paramType":"M","paramValues":[
                {"optionId":0,"optionValue":"15","optionText":"","toValue":"800"}]}]
            """)!.AsArray();
        var m = MymoooRules.Parameter(parameters, "m")!;
        Equal("3689839", MymoooRules.SelectData(m, "8"));
        Equal("3689839", MymoooRules.SelectData(m, "8.0"));
        Equal<string?>(null, MymoooRules.SelectData(m, "12"));
        Equal("CZ", MymoooRules.Text(MymoooRules.Parameter(parameters, "材质"), "paramCode"));
        Equal("CZ 材质", MymoooRules.Label(MymoooRules.Parameter(parameters, "材质")!));

        // 区间参数：区间内原样给数，区间外（服务器会静默不收）为 null。
        var length = MymoooRules.Parameter(parameters, "L")!;
        Equal("123", MymoooRules.SelectData(length, "123"));
        Equal<string?>(null, MymoooRules.SelectData(length, "3000"));
        True(MymoooRules.Choices(length).StartsWith("15~800"), MymoooRules.Choices(length));
        Equal("8 / 10", MymoooRules.Choices(m));

        Equal("螺栓长度L：6~260mm；主体材质：SUS304", MymoooRules.Description("螺栓长度L：6~260mm<br />主体材质：SUS304"));
        Equal("全长L：12~192mm；外径D:8~80mm", MymoooRules.Description("全长L：12~192mm；\n外径D:8~80mm"));
    }

    static void TestMymoooBadArguments()
    {
        var insert = QuickCommands.All.Single(command => command.CommandName == "strenua.mymooo.insert");
        var registry = Registry();
        True(registry.TryGet(insert.CommandName, out var descriptor), "未注册 mymooo.insert");
        Throws(() => insert.Bind!(new HistoryVulcan.Core.Commands.CommandContext(descriptor!, new Dictionary<string, string> { ["product"] = "螺栓" }, "test", null, default)), "商品号不是数");
        Throws(() => insert.Bind!(new HistoryVulcan.Core.Commands.CommandContext(descriptor!, new Dictionary<string, string> { ["product"] = "1", ["params"] = "M8" }, "test", null, default)), "参数缺等号");
        True(registry.TryGet("strenua.mymooo.category", out var category) && category!.Readonly, "category 只读");
        True(registry.TryGet("strenua.mymooo.product", out var product) && product!.Readonly, "product 只读");
        True(!QuickClasses.Hidden.Contains("mymooo") || QuickCommands.All.Where(command => command.CommandClass == "mymooo").All(command => command.PageClass == "mymooo"), "蚂蚁工场不上页面");
    }
}
