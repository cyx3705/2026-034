namespace HistoryStrenua;

/// <summary>
/// 依次跑几步、某一步没成不中断，最后汇总成一条回执（基础出图、全图倒圆倒角、一键出图共用）。取消照常立即停。
/// </summary>
internal sealed class StepLog(QuickCommandContext context)
{
    /// <summary>回执正文，每步一段。</summary>
    public List<string> Lines { get; } = [];

    /// <summary>没成的步骤名。</summary>
    public List<string> Failures { get; } = [];

    /// <summary>成功但图上没动的步骤名（1.24.0：回执里合成一行）。</summary>
    public List<string> Idle { get; } = [];

    public void Run(string title, Func<QuickOutcome> run)
    {
        context.Cancellation.ThrowIfCancellationRequested();
        QuickOutcome outcome;
        try
        {
            outcome = run();
        }
        catch (QuickCommandException ex)
        {
            outcome = QuickOutcome.Fail($"{title}：{ex.Message}");
        }
        catch (Exception ex) when (QuickError.Unexpected(ex))
        {
            // 1.24.0：COM / 反射错误也只算这一步没成，后面照做（1.23 前对称轴一抛整条一键出图就断了）。
            outcome = QuickOutcome.Fail($"{title}出错：{QuickError.Describe(ex)}");
        }

        if (outcome.Idle)
            Idle.Add(title);
        else
            Lines.Add(outcome.Message);
        if (!outcome.Success)
            Failures.Add(title);
    }

    /// <summary>回执：第一行「<paramref name="name"/>：<paramref name="done"/>。」（有没成的就列出来），下面是各步的回执。</summary>
    public QuickOutcome Finish(string name, string done)
    {
        if (Lines.Count == 0 && Failures.Count == 0 && Idle.Count > 0)
            return QuickOutcome.Nothing($"{name}：{string.Join("、", Idle)}都没有可标的，没有改动。");
        var head = $"{name}：{done}" + (Failures.Count == 0 ? "。" : $"，{Failures.Count} 步没成（{string.Join("、", Failures)}）。");
        var body = Idle.Count > 0 ? Lines.Append($"没有可标的：{string.Join("、", Idle)}。") : Lines;
        var message = head + Environment.NewLine + string.Join(Environment.NewLine, body);
        return Failures.Count == 0 ? QuickOutcome.Ok(message) : QuickOutcome.Fail(message);
    }
}
