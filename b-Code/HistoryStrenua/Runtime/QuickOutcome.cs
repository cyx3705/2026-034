namespace HistoryStrenua;

/// <summary>一次执行的结论。<see cref="Success"/> 为 false 时总线回执失败。</summary>
/// <param name="Success">成没成。</param>
/// <param name="Message">回执文字（进控制台）。</param>
/// <param name="Data">成功回执带的数据（1.20.0，细粒度读回指令的行）；快捷指令都是 null。</param>
internal sealed record QuickOutcome(bool Success, string Message, object? Data = null)
{
    public static QuickOutcome Ok(string message) => new(true, message);

    public static QuickOutcome Ok(string message, object data) => new(true, message, data);

    public static QuickOutcome Fail(string message) => new(false, message);

    /// <summary>
    /// 成功但图上什么都没动（没有可标的东西、都已标过）。1.24.0：多步、多视图的汇总把这类合成一行，回执不再一行一行「没有…」。
    /// </summary>
    public static QuickOutcome Nothing(string message) => new(true, message) { Idle = true };

    /// <summary>见 <see cref="Nothing"/>。</summary>
    public bool Idle { get; init; }
}
