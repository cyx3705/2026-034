using System.Text;
using System.Text.Json.Nodes;

namespace HistoryStrenua;

/// <summary>
/// 蚂蚁工场在线接口（1.37.0）。路径、请求体照抄官方 SolidWorks 在线版插件；全链路不要登录票据。
/// </summary>
/// <remarks>
/// 回包多数是 <c>{code, isSuccess, message, data}</c>，<c>api/product/search</c> 直接是分页对象。
/// 分类树一小时内只取一次（7 万字、很少变）。网络错与业务失败都抛 <see cref="QuickCommandException"/>，回执里写清是哪一步。
/// </remarks>
internal static class MymoooClient
{
    private const string Base = "https://www.mymooo.com/";

    private static readonly HttpClient Http = CreateClient();

    private static readonly object TreeGate = new();
    private static (JsonNode Tree, DateTime At)? _tree;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(Base), Timeout = TimeSpan.FromMinutes(12) };
        // 插件的请求头；服务器不认登录，但按它的样子来最稳。
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SolidWorksPlug");
        return client;
    }

    /// <summary>分类树根（「工厂自动化零件」），子节点在 <c>categories</c>。</summary>
    public static async Task<JsonNode> TreeAsync(CancellationToken cancellation)
    {
        lock (TreeGate)
        {
            if (_tree is { } cached && DateTime.UtcNow - cached.At < TimeSpan.FromHours(1))
                return cached.Tree;
        }

        var tree = await PostAsync("api/ProductClass/ProductClassTree", null, "分类树", cancellation).ConfigureAwait(false);
        lock (TreeGate)
            _tree = (tree, DateTime.UtcNow);
        return tree;
    }

    /// <summary>分类的筛选面（<c>productClassParams</c>）。</summary>
    public static Task<JsonNode> FacetsAsync(long classId, CancellationToken cancellation)
        => PostAsync("api/ProductClassParam/Search", new JsonObject { ["ClassId"] = classId, ["ProductClassParams"] = new JsonArray() }, "筛选项", cancellation);

    /// <summary>分类下的商品一页（<c>total</c>、<c>rows</c>）。</summary>
    public static Task<JsonNode> ProductsAsync(long classId, JsonArray criteria, int page, int size, CancellationToken cancellation)
        => PostAsync("api/ProductClassParam/SearchProduct", new JsonObject
        {
            ["Filter"] = new JsonObject { ["ClassId"] = classId, ["ProductClassParams"] = criteria },
            ["PageIndex"] = page,
            ["PageSize"] = size,
        }, "商品列表", cancellation);

    /// <summary>关键字搜商品（不分分类）。</summary>
    public static Task<JsonNode> SearchAsync(string keyword, int size, CancellationToken cancellation)
        => PostAsync("api/product/search", new JsonObject { ["Keyword"] = keyword, ["From"] = 0, ["Size"] = size, ["IncludeProductSize"] = true }, "搜索", cancellation, envelope: false);

    /// <summary>选型一步：回 <c>number</c>、<c>productTypes</c>、<c>productParameters</c>、<c>productNumbers</c>。给了料号就按料号直选。</summary>
    public static JsonNode Select(JsonObject request, string? number, CancellationToken cancellation)
        => Wait(PostAsync("api/Product/Selection?productCode=" + Uri.EscapeDataString(number ?? string.Empty), request.DeepClone(), "选型", cancellation));

    /// <summary>
    /// 取 3D：GetUrl 换 orderid → GetViewFile 要 STP 的文件信息 → 下载到 <paramref name="folder"/>。返回下载的文件路径。
    /// </summary>
    public static string Download(long productId, string number, string folder, CancellationToken cancellation)
    {
        var order = Wait(PostAsync("threedapi/Threed/GetUrl", new JsonObject { ["ProductId"] = productId, ["Number"] = number }, "取 3D 订单", cancellation)).ToString();
        var file = Wait(SendAsync(HttpMethod.Get, $"threedapi/Threed/GetViewFile?orderid={Uri.EscapeDataString(order)}&fileType=STP", null, "取 3D 文件", cancellation, true));
        var name = MymoooRules.Text(file, "fileName");
        var mesh = MymoooRules.Text(file, "meshFile");
        if (name.Length == 0 || mesh.Length == 0)
            throw new QuickCommandException($"蚂蚁工场：{number} 没有可下载的 3D 文件。");

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, MymoooRules.FileName(name));
        using var response = Wait(Http.GetAsync("threedapi/Threed/DownLoad3dFile?file=" + Uri.EscapeDataString(mesh), HttpCompletionOption.ResponseHeadersRead, cancellation));
        if (!response.IsSuccessStatusCode)
            throw new QuickCommandException($"蚂蚁工场：下载 3D 文件失败（HTTP {(int)response.StatusCode}）。");
        using (var target = File.Create(path))
            Wait(response.Content.CopyToAsync(target, cancellation));
        return path;
    }

    private static Task<JsonNode> PostAsync(string path, JsonNode? body, string step, CancellationToken cancellation, bool envelope = true)
        => SendAsync(HttpMethod.Post, path, body, step, cancellation, envelope);

    private static async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonNode? body, string step, CancellationToken cancellation, bool envelope)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null
                ? method == HttpMethod.Post ? new StringContent(string.Empty, Encoding.UTF8, "application/x-www-form-urlencoded") : null
                : new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        string text;
        try
        {
            using var response = await Http.SendAsync(request, cancellation).ConfigureAwait(false);
            text = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new QuickCommandException($"蚂蚁工场{step}：HTTP {(int)response.StatusCode}。");
        }
        catch (HttpRequestException ex)
        {
            throw new QuickCommandException($"蚂蚁工场{step}：连不上 {Base}（{ex.Message}）。");
        }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new QuickCommandException($"蚂蚁工场{step}：超时。");
        }

        var node = JsonNode.Parse(text) ?? throw new QuickCommandException($"蚂蚁工场{step}：回包是空的。");
        if (!envelope)
            return node;
        if (node["isSuccess"]?.GetValue<bool>() != true || node["data"] is not { } data)
        {
            var message = MymoooRules.Text(node, "message") is { Length: > 0 } m ? m : MymoooRules.Text(node, "errorMessage");
            throw new QuickCommandException($"蚂蚁工场{step}没成：{(message.Length > 0 ? message : "没有说原因")}。");
        }

        return data;
    }

    /// <summary>快捷指令跑在 STA 线程上（没有同步上下文），这里直接等。</summary>
    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private static void Wait(Task task) => task.GetAwaiter().GetResult();
}
