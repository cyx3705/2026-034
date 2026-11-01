using System.Text.Json;

namespace HistoryStrenua;

/// <summary>
/// PowerSW 页面上的开关（1.7.0，用户定）：避障（默认开）、尺寸链模式（默认关）；1.13.0 加默认技术要求模板（表格「设置」列）；
/// 1.14.0 加 AI 填写技术要求（默认关，只在「技术要求」类下面显示）。
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
    /// <summary>没设过默认时用这份（与 1.9.0 起一键出图用的用户手工图那 8 条最接近）。</summary>
    public const string InitialTechDefault = "机加件-钢材";

    private string _techDefault = StrenuaOptions.InitialTechDefault;
    private bool _techAi;
    private PropertyDefaults _propDefaults = new();

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
                (_clearance, _chain, _techDefault, _techAi) = (saved.Clearance, saved.Chain,
                    string.IsNullOrWhiteSpace(saved.TechDefault) ? StrenuaOptions.InitialTechDefault : saved.TechDefault, saved.TechAi);
            if (saved?.PropDefaults is { } defaults)
                _propDefaults = defaults;
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

    /// <summary>尺寸链模式：孔位尺寸每个方向一组 SolidWorks 尺寸链（坐标尺寸，0 点在零件左侧 / 上侧直边），不分种、不用阵列写法。</summary>
    public bool Chain
    {
        get { lock (_gate) return _chain; }
    }

    /// <summary>
    /// AI 填写技术要求（1.14.0，默认关）：开着时模板表格点哪份、一键类插技术要求，都改由 AI 选基础再微调（<see cref="TechAi"/>）。
    /// </summary>
    public bool TechAi
    {
        get { lock (_gate) return _techAi; }
    }

    /// <summary>默认技术要求模板的名字（1.13.0）：一键类插这份。</summary>
    public string TechDefault
    {
        get { lock (_gate) return _techDefault; }
    }

    /// <summary>属性默认值（1.33.0）：页面属性类的初值，一键类自动写入零件。</summary>
    public PropertyDefaults PropDefaults
    {
        get { lock (_gate) return _propDefaults; }
    }

    /// <summary>改属性默认值并存档。</summary>
    /// <returns>存档失败时的原因；成功为 null。</returns>
    public string? SetPropDefaults(PropertyDefaults defaults)
    {
        Saved snapshot;
        lock (_gate)
        {
            _propDefaults = defaults;
            snapshot = new Saved(_clearance, _chain, _techDefault, _techAi, _propDefaults);
        }

        return Save(snapshot);
    }

    /// <summary>改一个开关并存档。</summary>
    /// <returns>存档失败时的原因；成功为 null。</returns>
    public string? Set(StrenuaOption option, bool value)
    {
        Saved snapshot;
        lock (_gate)
        {
            switch (option)
            {
                case StrenuaOption.Clearance:
                    _clearance = value;
                    break;
                case StrenuaOption.Chain:
                    _chain = value;
                    break;
                default:
                    _techAi = value;
                    break;
            }

            snapshot = new Saved(_clearance, _chain, _techDefault, _techAi, _propDefaults);
        }

        return Save(snapshot);
    }

    /// <summary>改默认技术要求模板并存档。</summary>
    /// <returns>存档失败时的原因；成功为 null。</returns>
    public string? SetTechDefault(string name)
    {
        Saved snapshot;
        lock (_gate)
        {
            _techDefault = name;
            snapshot = new Saved(_clearance, _chain, _techDefault, _techAi, _propDefaults);
        }

        return Save(snapshot);
    }

    private string? Save(Saved snapshot)
    {
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

    public bool Get(StrenuaOption option) => option switch
    {
        StrenuaOption.Clearance => Clearance,
        StrenuaOption.Chain => Chain,
        _ => TechAi,
    };

    /// <summary>模块数据目录（存档文件所在目录，1.9.0 起技术要求文本、图纸截图也放这里）；只在内存里时为 null。</summary>
    public string? DataDirectory => _path is null ? null : Path.GetDirectoryName(_path);

    /// <summary>缺的项按默认值：将来加开关时旧存档照样读。</summary>
    private sealed record Saved(bool Clearance = true, bool Chain = false, string? TechDefault = null, bool TechAi = false, PropertyDefaults? PropDefaults = null);
}

/// <summary>页面开关。</summary>
internal enum StrenuaOption
{
    Clearance,
    Chain,
    TechAi,
}

/// <summary>记到本机的属性默认值，存选择框里的字（日期可以是「今天」，写入时才算成当日）；null 表示不改。</summary>
internal sealed record PropertyDefaults(
    string? Material = null,
    string? Surface = null,
    string? Heat = null,
    string? Date = null,
    string? Designer = null)
{
    /// <summary>按参数名取值（页面输入框的初值）。</summary>
    public string? ValueOf(string parameter) => parameter switch
    {
        "material" => Material,
        "surface" => Surface,
        "heat" => Heat,
        "date" => Date,
        "designer" => Designer,
        _ => null,
    };
}
