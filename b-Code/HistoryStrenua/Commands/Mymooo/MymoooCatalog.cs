using System.Text;
using System.Text.Json.Nodes;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 蚂蚁工场选型的前两层（1.37.0，用户定：按切面分层查，不按零件逐个做工具）：
/// <c>strenua.mymooo.category</c> 零件种类（分类树 / 关键字），<c>strenua.mymooo.product</c> 某个分类下的筛选项、品牌与商品。
/// 只查在线接口、不碰 SolidWorks，不进执行器；第三层选参数并插入是 <see cref="MymoooInsert"/>。
/// </summary>
internal static class MymoooCatalog
{
    public const string CommandClass = "mymooo";

    /// <summary>商品列表最多翻这么多页（每页 100），分类下的商品实测几十个。</summary>
    private const int MaxPages = 5;

    /// <summary>一次回执最多列这么多商品。</summary>
    private const int MaxRows = 60;

    public static IEnumerable<CommandDescriptor> All()
    {
        yield return new CommandDescriptor
        {
            Name = StrenuaIdentity.Domain + ".mymooo.category",
            Domain = StrenuaIdentity.Domain,
            CommandClass = CommandClass,
            Summary = "蚂蚁工场第 1 层·零件种类：不给参数列大类；class= 分类号列下级、名字片段找分类；keyword= 直接搜商品",
            Example = StrenuaIdentity.Domain + ".mymooo.category class=螺栓",
            Readonly = true,
            Parameters =
            [
                new ParameterSpec { Name = "class", Description = "分类号（列它的下级）或名字片段（找名字含它的分类，带路径）；省略列大类", Required = false, Position = 0 },
                new ParameterSpec { Name = "keyword", Description = "关键字直接搜商品（如 深沟球轴承），回商品号、品牌、所属分类；给了就不看 class", Required = false, Position = 1 },
            ],
            Handler = context => Guard(() => CategoryAsync(context)),
        };

        yield return new CommandDescriptor
        {
            Name = StrenuaIdentity.Domain + ".mymooo.product",
            Domain = StrenuaIdentity.Domain,
            CommandClass = CommandClass,
            Summary = "蚂蚁工场第 2 层·品牌与细分：某分类下的筛选项与商品（商品号、品牌、名称）；filter= 按筛选项收窄，brand= 按品牌",
            Example = StrenuaIdentity.Domain + ".mymooo.product class=31286 filter=LX=直通型",
            Readonly = true,
            Parameters =
            [
                new ParameterSpec { Name = "class", Description = "分类号（strenua.mymooo.category 列出的号，末级或上级都行）", Required = true, Position = 0 },
                new ParameterSpec { Name = "filter", Description = "筛选：筛选项码或名=值，逗号分开（如 LX=直通型,DR=1~20）；值照回执里的写", Required = false, Position = 1 },
                new ParameterSpec { Name = "brand", Description = "只列这个品牌（如 蚂蚁工场、EAIR），按名字片段认", Required = false, Position = 2 },
            ],
            Handler = context => Guard(() => ProductAsync(context)),
        };
    }

    private static async Task<CommandResult> Guard(Func<Task<CommandResult>> work)
    {
        try
        {
            return await work().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is QuickCommandException or QuickArgumentException)
        {
            return CommandResult.Fail(ex.Message);
        }
    }

    private static async Task<CommandResult> CategoryAsync(CommandContext context)
    {
        var cancellation = context.Cancellation;
        if (QuickArgs.Optional(context, "keyword") is { } keyword)
            return await SearchAsync(keyword, cancellation).ConfigureAwait(false);

        var root = await MymoooClient.TreeAsync(cancellation).ConfigureAwait(false);
        var nodes = Flatten(root, []).ToList();
        var key = QuickArgs.Optional(context, "class");
        if (key is null)
            return Children(root, "蚂蚁工场大类");

        if (long.TryParse(key, out var id))
        {
            var found = nodes.FirstOrDefault(item => Id(item.Node) == id);
            return found.Node is null
                ? CommandResult.Fail($"没有分类号 {id}；不给 class 列大类，或给名字片段找。")
                : Children(found.Node, string.Join(" > ", found.Path.Append(Name(found.Node))));
        }

        var matches = nodes.Where(item => Name(item.Node).Contains(key, StringComparison.OrdinalIgnoreCase)).Take(40).ToList();
        if (matches.Count == 0)
            return CommandResult.Fail($"没有名字含「{key}」的分类；换个说法，或用 keyword= 直接搜商品。");
        var text = new StringBuilder($"名字含「{key}」的分类 {matches.Count} 个（号 路径，下级数）：").AppendLine();
        foreach (var (node, path) in matches)
            text.AppendLine($"· {Id(node)} {string.Join(" > ", path.Append(Name(node)))}（{Kids(node).Count}）");
        text.Append("末级（下级 0）用 strenua.mymooo.product class=号 看商品；有下级的用 class=号 往下列。");
        return CommandResult.Ok(text.ToString(), matches.Select(item => Row(item.Node, item.Path)).ToList());
    }

    private static CommandResult Children(JsonNode node, string title)
    {
        var kids = Kids(node);
        if (kids.Count == 0)
            return CommandResult.Ok($"{title}（{Id(node)}）是末级分类：用 strenua.mymooo.product class={Id(node)} 看筛选项和商品。");
        var text = new StringBuilder($"{title} 的下级 {kids.Count} 个（号 名称，下级数）：").AppendLine();
        foreach (var kid in kids)
            text.AppendLine($"· {Id(kid)} {Name(kid)}（{Kids(kid).Count}）");
        text.Append("有下级的用 class=号 往下列；末级（下级 0）用 strenua.mymooo.product class=号。");
        return CommandResult.Ok(text.ToString(), kids.Select(kid => Row(kid, [])).ToList());
    }

    private static async Task<CommandResult> SearchAsync(string keyword, CancellationToken cancellation)
    {
        var result = await MymoooClient.SearchAsync(keyword, 30, cancellation).ConfigureAwait(false);
        var rows = result["rows"] as JsonArray ?? [];
        if (rows.Count == 0)
            return CommandResult.Ok($"搜「{keyword}」没有商品；换个说法，或用 class= 按分类找。");
        var text = new StringBuilder($"搜「{keyword}」共 {MymoooRules.Text(result, "total")} 个商品，列前 {rows.Count} 个（商品号 品牌 名称 [分类号 分类]）：").AppendLine();
        foreach (var row in rows)
            text.AppendLine($"· {Product(row)} [{MymoooRules.Text(row, "classId")} {MymoooRules.Text(row, "className")}]");
        text.Append("选参数并插入：strenua.mymooo.insert product=商品号（先不带 params 看要选哪些参数）。");
        return CommandResult.Ok(text.ToString(), rows.Select(ProductRow).ToList());
    }

    private static async Task<CommandResult> ProductAsync(CommandContext context)
    {
        var cancellation = context.Cancellation;
        var key = QuickArgs.Name(context, "class");
        if (!long.TryParse(key, out var classId))
            throw new QuickArgumentException($"class={key} 要写分类号（strenua.mymooo.category 列出的号）。");
        var filters = MymoooRules.Pairs(QuickArgs.Optional(context, "filter"), "filter");
        var brand = QuickArgs.Optional(context, "brand");

        var facets = await MymoooClient.FacetsAsync(classId, cancellation).ConfigureAwait(false);
        var groups = facets["productClassParams"] as JsonArray ?? [];
        var criteria = new JsonArray();
        foreach (var (name, value) in filters)
        {
            var group = groups.FirstOrDefault(g => string.Equals(MymoooRules.Text(g, "classParamCode"), name, StringComparison.OrdinalIgnoreCase) || MymoooRules.Text(g, "classParamName") == name)
                        ?? throw new QuickArgumentException($"filter：这个分类没有筛选项「{name}」，有：{string.Join("、", groups.Select(FacetLabel))}。");
            var option = (group["parameterValues"] as JsonArray ?? []).FirstOrDefault(v => MymoooRules.Text(v, "paramValue") == value)
                         ?? throw new QuickArgumentException($"filter：「{FacetLabel(group)}」没有值「{value}」，可选：{FacetValues(group)}。");
            criteria.Add(new JsonObject
            {
                ["ProductClassParamId"] = group["productClassParamId"]?.DeepClone(),
                ["ClassParamCode"] = MymoooRules.Text(group, "classParamCode"),
                ["ParameterValues"] = new JsonArray(new JsonObject { ["ClassParamValueId"] = option["classParamValueId"]?.DeepClone(), ["ParamValue"] = value }),
            });
        }

        var products = new List<JsonNode>();
        var total = 0;
        for (var page = 1; page <= MaxPages; page++)
        {
            var result = await MymoooClient.ProductsAsync(classId, (JsonArray)criteria.DeepClone(), page, 100, cancellation).ConfigureAwait(false);
            total = int.TryParse(MymoooRules.Text(result, "total"), out var count) ? count : 0;
            var rows = (result["rows"] as JsonArray ?? []).Where(row => row is not null).Select(row => row!).ToList();
            products.AddRange(rows);
            if (rows.Count < 100 || products.Count >= total)
                break;
        }

        var brands = products.GroupBy(row => MymoooRules.Text(row, "brandName")).Select(g => $"{g.Key} {g.Count()}").ToList();
        if (brand is not null)
            products = products.Where(row => MymoooRules.Text(row, "brandName").Contains(brand, StringComparison.OrdinalIgnoreCase)).ToList();

        var title = MymoooRules.Text(facets, "className");
        var text = new StringBuilder($"{classId} {title}：商品 {total} 个").Append(filters.Count > 0 ? $"（按 {string.Join("、", filters.Select(f => $"{f.Key}={f.Value}"))}）" : string.Empty)
            .Append($"；品牌：{(brands.Count > 0 ? string.Join("、", brands) : "无")}。").AppendLine();
        if (groups.Count > 0)
        {
            text.AppendLine("筛选项（filter= 码=值）：");
            foreach (var group in groups)
                text.AppendLine($"  {FacetLabel(group)}：{FacetValues(group)}");
        }

        text.AppendLine(brand is null ? "商品（商品号 品牌 名称；说明）：" : $"品牌含「{brand}」的商品 {products.Count} 个：");
        foreach (var row in products.Take(MaxRows))
        {
            var description = MymoooRules.Description(MymoooRules.Text(row, "description"));
            text.AppendLine($"· {Product(row)}{(description.Length > 0 ? "；" + description : string.Empty)}{(row["isShow3D"]?.GetValue<bool>() == false ? "（无 3D）" : string.Empty)}");
        }

        if (products.Count > MaxRows)
            text.AppendLine($"…另有 {products.Count - MaxRows} 个，加 filter= 或 brand= 收窄。");
        text.Append("下一步：strenua.mymooo.insert product=商品号，先不带 params 看要选哪些参数。");
        return CommandResult.Ok(text.ToString(), products.Take(MaxRows).Select(ProductRow).ToList());
    }

    private static string FacetLabel(JsonNode? group)
        => $"{MymoooRules.Text(group, "classParamCode")} {MymoooRules.Text(group, "classParamName")}";

    private static string FacetValues(JsonNode? group)
        => string.Join(" / ", (group?["parameterValues"] as JsonArray ?? []).Select(v => MymoooRules.Text(v, "paramValue")));

    private static string Product(JsonNode? row)
        => $"{MymoooRules.Text(row, "productId")} {MymoooRules.Text(row, "brandName")} {MymoooRules.Text(row, "productName")}";

    private static Dictionary<string, string> ProductRow(JsonNode? row) => new()
    {
        ["product"] = MymoooRules.Text(row, "productId"),
        ["brand"] = MymoooRules.Text(row, "brandName"),
        ["name"] = MymoooRules.Text(row, "productName"),
        ["class"] = MymoooRules.Text(row, "classId"),
    };

    private static Dictionary<string, string> Row(JsonNode node, IEnumerable<string> path) => new()
    {
        ["class"] = Id(node).ToString(),
        ["name"] = Name(node),
        ["path"] = string.Join(" > ", path.Append(Name(node))),
        ["children"] = Kids(node).Count.ToString(),
    };

    private static IEnumerable<(JsonNode Node, List<string> Path)> Flatten(JsonNode node, List<string> path)
    {
        foreach (var kid in Kids(node))
        {
            yield return (kid, path);
            foreach (var item in Flatten(kid, [.. path, Name(kid)]))
                yield return item;
        }
    }

    private static List<JsonNode> Kids(JsonNode node)
        => (node["categories"] as JsonArray ?? []).Where(kid => kid is not null).Select(kid => kid!).ToList();

    private static long Id(JsonNode node) => long.TryParse(MymoooRules.Text(node, "classId"), out var id) ? id : 0;

    private static string Name(JsonNode node) => MymoooRules.Text(node, "className").Trim();
}
