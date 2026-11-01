using System.Globalization;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>一条指令的状态；<paramref name="Data"/> 是上次回执带的数据（<c>strenua.quick.last</c> 取回用）。</summary>
internal sealed record QuickCommandStatus(string State, string Result, DateTime? FinishedAt, object? Data = null);

/// <summary>每条指令的状态、最近几次回执与上次耗时，给 <c>strenua.quick.list / last / perf</c>。线程安全。</summary>
internal sealed class QuickRunLog
{
    /// <summary>每条指令留最近几次回执（真机批量出图：下一张一跑，上一张的回执就没了）。</summary>
    private const int HistorySize = 20;

    private readonly object _gate = new();
    private readonly Dictionary<string, QuickCommandStatus> _status = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<QuickCommandStatus>> _history = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _profiles = new(StringComparer.Ordinal);

    public QuickCommandStatus StatusOf(string key)
    {
        lock (_gate)
            return _status.TryGetValue(key, out var status) ? status : new QuickCommandStatus("就绪", string.Empty, null);
    }

    public void SetState(string key, string state)
    {
        lock (_gate)
            _status[key] = new QuickCommandStatus(state, string.Empty, null);
    }

    public void Finish(string key, string state, string result, object? data = null)
    {
        lock (_gate)
        {
            var status = new QuickCommandStatus(state, result, DateTime.Now, data);
            _status[key] = status;
            if (!_history.TryGetValue(key, out var list))
                _history[key] = list = [];
            list.Insert(0, status);
            if (list.Count > HistorySize)
                list.RemoveAt(list.Count - 1);
        }
    }

    public void SaveProfile(string key, string text)
    {
        lock (_gate)
            _profiles[key] = text;
    }

    /// <summary>取回一条指令上次执行的耗时明细。</summary>
    public CommandResult Perf(string key)
    {
        lock (_gate)
            return _profiles.TryGetValue(key, out var text) ? CommandResult.Ok(text) : CommandResult.Fail($"key={key} 这次启动后还没有耗时记录。");
    }

    /// <summary>往前第 <paramref name="back"/> 次（0 = 最近一次）的结论，带状态与时间抬头；没有回失败。</summary>
    public CommandResult Last(string key, int back)
    {
        lock (_gate)
        {
            if (back > 0)
            {
                if (!_history.TryGetValue(key, out var list) || back >= list.Count)
                    return CommandResult.Fail($"key={key} 没有往前第 {back} 次的回执（留了 {(list?.Count ?? 0)} 次）。");
                return Reply(list[back]);
            }

            if (!_status.TryGetValue(key, out var status) || status.FinishedAt is null)
                return CommandResult.Fail($"key={key} 这次启动后还没执行过。");
            return Reply(status);
        }
    }

    private static CommandResult Reply(QuickCommandStatus status)
    {
        var head = $"[{status.State} {FormatTime(status.FinishedAt)}] ";
        return status.Data is null ? CommandResult.Ok(head + status.Result) : CommandResult.Ok(head + status.Result, status.Data);
    }

    internal static string FormatTime(DateTime? time)
        => time?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;
}
