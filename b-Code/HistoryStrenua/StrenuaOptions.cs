using System.Text.Json;

namespace HistoryStrenua;

/// <summary>
/// PowerSW 页面上的两个开关（1.7.0，用户定）：避障（默认开）、尺寸链模式（默认关）。
/// 记在模块数据目录的 <see cref="FileName"/> 里，重启后保持上次。
/// </summary>
/// <remarks>
/// 读不到、读坏了都回默认值，不让一个设置文件拦住指令；写不进去只影响下次启动的初值，本轮照样生效。
/// </remarks>
internal sealed class StrenuaOptions
{
    public const string FileName = "options.json";

    private readonly object _gate = new();
    private readonly string? _path;
    private bool _clearance = true;
    private bool _chain;

    /// <param name="path">存档文件；null 表示只在内存里（离线测试用）。</param>
    public StrenuaOptions(string? path = null)
    {
        _path = path;
        if (path is null || !File.Exists(path))
            return;
        try
        {
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path));
            if (saved is not null)
                (_clearance, _chain) = (saved.Clearance, saved.Chain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
    }

    /// <summary>避障：孔标注、孔位尺寸加完后把压在别的孔相关注解上的文字挪开。</summary>
    public bool Clearance
    {
        get { lock (_gate) return _clearance; }
    }

    /// <summary>尺寸链模式：孔位尺寸每个方向全部孔排成一条链（基准→第一个→下一个…），不分种、不用阵列写法。</summary>
    public bool Chain
    {
        get { lock (_gate) return _chain; }
    }

    /// <summary>改一个开关并存档。</summary>
    /// <returns>存档失败时的原因；成功为 null。</returns>
    public string? Set(StrenuaOption option, bool value)
    {
        Saved snapshot;
        lock (_gate)
        {
            if (option == StrenuaOption.Clearance)
                _clearance = value;
            else
                _chain = value;
            snapshot = new Saved(_clearance, _chain);
        }

        if (_path is null)
            return null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(snapshot));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    public bool Get(StrenuaOption option) => option == StrenuaOption.Clearance ? Clearance : Chain;

    /// <summary>缺的项按默认值：将来加开关时旧存档照样读。</summary>
    private sealed record Saved(bool Clearance = true, bool Chain = false);
}

/// <summary>页面开关。</summary>
internal enum StrenuaOption
{
    Clearance,
    Chain,
}
