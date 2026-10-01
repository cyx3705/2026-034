namespace HistoryStrenua;

/// <summary>模块名、指令域与页面身份的唯一权威源。</summary>
internal static class StrenuaIdentity
{
    /// <summary>模块名：部署槽、manifest 与 <see cref="ModuleInfo.ModuleName"/>。</summary>
    public const string Name = "HistoryStrenua";

    /// <summary>指令域。</summary>
    public const string Domain = "strenua";

    /// <summary>
    /// 页面 owner 与场景 id。Aurora 按指令域反推（<c>"History" + 首字母大写的域</c>），
    /// 与模块名无关；对不上整页被静默拒收，只留一条 Warn。
    /// 本模块恰好两者相同，但依据是域，不是模块名。
    /// </summary>
    public const string PageOwner = "HistoryStrenua";

    /// <summary>用户看到的页面标题。模块叫 Strenua，面向用户的功能名是 PowerSW。</summary>
    public const string PageTitle = "PowerSW";
}
