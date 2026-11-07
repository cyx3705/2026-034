using System.Text.Json;
using System.Text.RegularExpressions;
using HistoryVulcan.Core.Commands;

/// <summary>
/// 测试用的「源码区」：只答 diana.z.tree / diana.z.read，把 z 地址映射到本机目录。
/// 生产里这两条由 HistoryDiana 3.0+ 在源码区机器上答；这里让模板读取走完整的 z 路径。
/// </summary>
internal sealed partial class LocalZBus(string addressPrefix, string localRoot) : ICommandBus
{
    /// <summary>本机上 2026-025 的 SW 模板配置（z 级）。不在就跳过真实文件那部分。</summary>
    public const string TemplateRoot = @"C:\OneHistory\HistoryClio\2026-025-实验室测绘\z-SW模板配置";

    /// <summary>「通用技术要求」在本机的目录。</summary>
    public static string TemplateFolder => Path.Combine(TemplateRoot, "09 设计库", "注释库", "通用技术要求");

    /// <summary>对着本机真实模板目录答 z 指令的总线。</summary>
    public static LocalZBus ForTemplates() => new("2026-025/z-SW模板配置", TemplateRoot);

    public int Reads { get; private set; }

    public Task<CommandResult> ExecuteAsync(string text, string source, CancellationToken cancellation = default)
        => InvokeAsync(text, source, cancellation);

    public Task<CommandResult> InvokeAsync(string text, string source, CancellationToken cancellation = default)
    {
        var name = text.Split(' ')[0];
        var match = PathArgument().Match(text);
        var address = match.Groups[1].Success ? Regex.Unescape(match.Groups[1].Value) : match.Groups[2].Value;
        if (!address.StartsWith(addressPrefix + "/", StringComparison.Ordinal))
            return Task.FromResult(CommandResult.Fail("地址越界：" + address));
        var local = Path.Combine(localRoot, address[(addressPrefix.Length + 1)..].Replace('/', Path.DirectorySeparatorChar));

        if (name == "diana.z.tree")
        {
            if (!Directory.Exists(local))
                return Task.FromResult(CommandResult.Fail("不是文件夹：" + address));
            var entries = Directory.GetFiles(local).OrderBy(p => p, StringComparer.Ordinal).Select(p => new
            {
                path = address + "/" + Path.GetFileName(p),
                kind = "file",
                bytes = new FileInfo(p).Length,
                modified = File.GetLastWriteTimeUtc(p).Ticks.ToString(),
            }).ToList();
            return Task.FromResult(CommandResult.Ok("tree", JsonSerializer.SerializeToElement(new { path = address, truncated = false, entries })));
        }

        if (name == "diana.z.read")
        {
            Reads++;
            var bytes = File.ReadAllBytes(local);
            return Task.FromResult(CommandResult.Ok("read", JsonSerializer.SerializeToElement(new
            {
                content = Convert.ToBase64String(bytes),
                nextOffset = bytes.Length,
                eof = true,
            })));
        }

        return Task.FromResult(CommandResult.Fail("未知指令 " + name));
    }

    public bool RequestConfirmation(string prompt) => false;

    [GeneratedRegex("path=(?:\"((?:[^\"\\\\]|\\\\.)*)\"|(\\S+))")]
    private static partial Regex PathArgument();
}
