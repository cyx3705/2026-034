using System.Globalization;
using HistoryStrenua.SolidWorks;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 执行快捷指令：一次只跑一条、可取消，并记下每条指令的状态给 <c>strenua.quick.list</c>。
/// </summary>
/// <remarks>
/// 一次只跑一条，是因为所有快捷指令都作用在用户同一个 SolidWorks 的同一个选择集上——
/// 两条同时跑，一条的 ClearSelection 会把另一条正在等的视图选择清掉。
/// </remarks>
/// <param name="options">页面开关与默认技术要求。</param>
/// <param name="bus">命令总线：「AI 填写技术要求」经它调 HistoryApollo（1.14.0）；离线测试为 null。</param>
internal sealed class QuickCommandRunner(StrenuaOptions options, ICommandBus? bus = null)
{
    /// <summary>页面开关（避障、尺寸链模式）。</summary>
    public StrenuaOptions Options { get; } = options;

    private readonly object _gate = new();
    private readonly Dictionary<string, QuickCommandStatus> _status = new(StringComparer.Ordinal);
    private CancellationTokenSource? _running;
    private string? _runningKey;
    private string? _runningTitle;

    /// <summary>一条指令的状态。</summary>
    internal sealed record QuickCommandStatus(string State, string Result, DateTime? FinishedAt);

    public QuickCommandStatus StatusOf(string key)
    {
        lock (_gate)
            return _status.TryGetValue(key, out var status) ? status : new QuickCommandStatus("就绪", string.Empty, null);
    }

    public async Task<CommandResult> RunAsync(QuickCommand command, CommandContext context)
    {
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_runningKey is not null)
            {
                return CommandResult.Fail($"「{_runningTitle}」还在执行，等它结束或先按取消。");
            }

            cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.Cancellation);
            _running = cancellation;
            _runningKey = command.Key;
            _runningTitle = command.Title;
            _status[command.Key] = new QuickCommandStatus("附着 SolidWorks", string.Empty, null);
        }

        QuickOutcome outcome;
        try
        {
            outcome = await SolidWorksSession.RunAsync(
                session => command.Run(new QuickCommandContext(
                    session,
                    Options,
                    message => context.Progress?.Report(message),
                    state => SetState(command.Key, state),
                    cancellation.Token,
                    bus)),
                cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            outcome = new QuickOutcome(false, $"「{command.Title}」已取消。");
            Finish(command.Key, "已取消", outcome.Message);
            return CommandResult.Fail(outcome.Message);
        }
        catch (QuickCommandException ex)
        {
            outcome = QuickOutcome.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            // 非预期的 COM / 反射错误：把类型带上，否则「参数错误」这类消息根本看不出是哪一步。
            outcome = QuickOutcome.Fail($"「{command.Title}」出错：{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            lock (_gate)
            {
                _running = null;
                _runningKey = null;
                _runningTitle = null;
            }

            cancellation.Dispose();
        }

        Finish(command.Key, outcome.Success ? "完成" : "失败", outcome.Message);
        return outcome.Success ? CommandResult.Ok(outcome.Message) : CommandResult.Fail(outcome.Message);
    }

    /// <summary>取消正在跑的那条。没有在跑的也回成功——点「取消」不该让宿主抢控制台。</summary>
    public CommandResult Cancel()
    {
        lock (_gate)
        {
            if (_running is null)
                return CommandResult.Ok("PowerSW 当前没有正在执行的指令。");
            _running.Cancel();
            return CommandResult.Ok("已请求取消 PowerSW 当前指令。");
        }
    }

    private void SetState(string key, string state)
    {
        lock (_gate)
            _status[key] = new QuickCommandStatus(state, string.Empty, null);
    }

    private void Finish(string key, string state, string result)
    {
        lock (_gate)
            _status[key] = new QuickCommandStatus(state, result, DateTime.Now);
    }

    internal static string FormatTime(DateTime? time)
        => time?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;
}
