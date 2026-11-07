using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>一份技术要求模板：名字（文件名去扩展名）、文件、注释全文（含 SolidWorks 段落格式标记）、条数。</summary>
internal sealed record TechTemplate(string Name, string Path, string Text, int Items)
{
    /// <summary>表格「内容」列：去掉标题与格式标记，各条连成一行。</summary>
    public string Summary => TechTemplates.Summary(Text);
}

/// <summary>
/// 技术要求模板（1.13.0，用户定）：取 2026-025 实验室测绘 z 级目录里 SolidWorks 设计库的「通用技术要求」注释文件（<c>*.sldnotestl</c>），
/// 页面「技术要求」类的表格一行一份，点哪份插哪份（<see cref="TechApply"/>）。
/// </summary>
/// <remarks>
/// <para>
/// 1.41.0：z 级文件夹只在源码区机器上、从不迁移，Strenua 跟着 SolidWorks 走、可能在别的机器上，
/// 所以模板一律经 <c>diana.z.tree</c> / <c>diana.z.read</c> 按逻辑地址 <see cref="Address"/> 读，不认本机路径；
/// 正文按（路径、大小、修改时间）缓存，没变的不重读。传本机目录只给测试用。
/// 同目录的「热处理表面处理技术要求」用户说还用不到，不列。
/// </para>
/// <para>
/// 文件是 SolidWorks 的注释样式：开头一张缩略图，后面是 MFC 序列化的对象，注释文字是其中一个 Unicode CString
/// （<c>FF FE FF</c> + 长度 + UTF-16LE）。离线从里面取出带「技术要求」的那一串，插注释时原文交给 <c>InsertNote</c>
/// ——和 1.9.0 的「技术要求.txt」同一条路，字体字高随图纸模板。
/// </para>
/// </remarks>
internal static class TechTemplates
{
    /// <summary>用户的 SW 模板配置（z 级目录）里「通用技术要求」那一格，z 逻辑地址。</summary>
    public const string Address = "2026-025/z-SW模板配置/09 设计库/注释库/通用技术要求";

    public const string Extension = ".sldnotestl";

    private const int ChunkBytes = 1024 * 1024;
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    /// <summary>宿主总线；模块装载时设上。没有总线就读不到 z（返回空）。</summary>
    public static ICommandBus? Bus { get; set; }

    private static readonly byte[] UnicodeMarker = [0xFF, 0xFE, 0xFF];
    private static readonly byte[] Heading = Encoding.Unicode.GetBytes("技术要求");
    private static readonly Regex ItemLine = new(@"^\s*\d+\s*[、.．]", RegexOptions.Compiled);
    private static readonly Regex FormatTag = new(@"<[^>]*>", RegexOptions.Compiled);

    /// <summary>
    /// 全部模板，按文件名排；读不出文字的跳过。<paramref name="folder"/> 省略时经 diana.z 读 <see cref="Address"/>，
    /// 给了就读本机目录（测试）。读不到返回空，原因见 <see cref="LastError"/>。
    /// </summary>
    public static IReadOnlyList<TechTemplate> Load(string? folder = null)
        => folder is null ? LoadFromZ() : LoadFromDirectory(folder);

    /// <summary>最近一次经 diana.z 读模板失败的原因；成功时为 null。</summary>
    public static string? LastError { get; private set; }

    /// <summary>读不到模板时给人看的一句话。</summary>
    public static string Missing => $"找不到技术要求模板（{Address} 下没有 {Extension}）" + (LastError is null ? "" : "：" + LastError);

    private static IReadOnlyList<TechTemplate> LoadFromZ()
    {
        try
        {
            var bus = Bus ?? throw new InvalidOperationException("没有命令总线");
            var tree = Invoke(bus, $"diana.z.tree path={CommandParser.QuoteArg(Address)} depth=1 limit=1000");
            var result = new List<TechTemplate>();
            foreach (var entry in tree.GetProperty("entries").EnumerateArray()
                         .Where(e => e.GetProperty("kind").GetString() == "file")
                         .OrderBy(e => e.GetProperty("path").GetString(), StringComparer.Ordinal))
            {
                var address = entry.GetProperty("path").GetString()!;
                if (!address.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                    continue;
                var key = $"{address}|{entry.GetProperty("bytes").GetInt64()}|{entry.GetProperty("modified").GetString()}";
                if (!Cache.TryGetValue(key, out var bytes))
                {
                    bytes = ReadZ(bus, address);
                    foreach (var stale in Cache.Keys.Where(k => k.StartsWith(address + "|", StringComparison.Ordinal)))
                        Cache.TryRemove(stale, out _);
                    Cache[key] = bytes;
                }

                var text = ParseNote(bytes);
                if (text is not null)
                    result.Add(new TechTemplate(System.IO.Path.GetFileNameWithoutExtension(address), address, text, CountItems(text)));
            }

            LastError = null;
            return result;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or KeyNotFoundException or FormatException)
        {
            LastError = ex.Message;
            return [];
        }
    }

    private static byte[] ReadZ(ICommandBus bus, string address)
    {
        using var buffer = new MemoryStream();
        var offset = 0;
        for (var guard = 0; guard < 256; guard++)
        {
            var data = Invoke(bus, $"diana.z.read path={CommandParser.QuoteArg(address)} offset={offset} length={ChunkBytes} encoding=base64");
            buffer.Write(Convert.FromBase64String(data.GetProperty("content").GetString() ?? ""));
            offset = data.GetProperty("nextOffset").GetInt32();
            if (data.GetProperty("eof").GetBoolean())
                break;
        }

        return buffer.ToArray();
    }

    private static JsonElement Invoke(ICommandBus bus, string command)
    {
        var result = bus.InvokeAsync(command, "").GetAwaiter().GetResult();
        if (!result.Success)
            throw new InvalidOperationException($"{command.Split(' ')[0]} 失败（需要源码区的 HistoryDiana 3.0+）：{result.Message}");
        if (result.Data is null)
            throw new InvalidOperationException($"{command.Split(' ')[0]} 没有返回数据");
        return result.Data is JsonElement element ? element : JsonSerializer.SerializeToElement(result.Data);
    }

    private static IReadOnlyList<TechTemplate> LoadFromDirectory(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        var result = new List<TechTemplate>();
        foreach (var path in Directory.EnumerateFiles(folder, "*" + Extension).OrderBy(p => p, StringComparer.Ordinal))
        {
            string? text;
            try
            {
                text = ParseNote(File.ReadAllBytes(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (text is not null)
                result.Add(new TechTemplate(System.IO.Path.GetFileNameWithoutExtension(path), path, text, CountItems(text)));
        }

        return result;
    }

    /// <summary>按名字找（不分大小写）；没有返回 null。</summary>
    public static TechTemplate? Find(string name, string? folder = null)
        => Load(folder).FirstOrDefault(template => string.Equals(template.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 从注释样式文件里取注释全文：含「技术要求」的那个 Unicode CString；换行统一成 <c>\n</c>，去掉末尾空行。没有返回 null。
    /// </summary>
    public static string? ParseNote(byte[] bytes)
    {
        var heading = bytes.AsSpan().IndexOf(Heading);
        if (heading < 0)
            return null;
        var marker = bytes.AsSpan(0, heading).LastIndexOf(UnicodeMarker);
        if (marker < 0)
            return null;
        var position = marker + UnicodeMarker.Length;
        if (!TryReadLength(bytes, ref position, out var length) || position + 2L * length > bytes.Length || position + 2L * length <= heading)
            return null;
        var text = Encoding.Unicode.GetString(bytes, position, 2 * length).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n', ' ', '\0');
        return text.Contains("技术要求", StringComparison.Ordinal) ? text : null;
    }

    /// <summary>编号的条数（「1、」「2、」……）。</summary>
    public static int CountItems(string text)
        => text.Split('\n').Count(line => ItemLine.IsMatch(FormatTag.Replace(line, string.Empty)));

    /// <summary>去掉标题行与格式标记，各条用空格连起来。</summary>
    public static string Summary(string text)
        => string.Join(" ", text.Split('\n')
            .Select(line => FormatTag.Replace(line, string.Empty).Trim())
            .Where(line => line.Length > 0 && line != "技术要求"));

    /// <summary>MFC CString 的长度：一个字节；0xFF 时接两个字节；0xFFFF 时再接四个字节。</summary>
    private static bool TryReadLength(byte[] bytes, ref int position, out int length)
    {
        length = 0;
        if (position >= bytes.Length)
            return false;
        length = bytes[position++];
        if (length < 0xFF)
            return true;
        if (position + 2 > bytes.Length)
            return false;
        length = BitConverter.ToUInt16(bytes, position);
        position += 2;
        if (length < 0xFFFF)
            return true;
        if (position + 4 > bytes.Length)
            return false;
        var wide = BitConverter.ToUInt32(bytes, position);
        position += 4;
        if (wide > int.MaxValue / 2)
            return false;
        length = (int)wide;
        return true;
    }
}
