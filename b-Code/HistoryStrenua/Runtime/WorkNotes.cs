using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 笔记区（1.28.0，用户定）：按页面上的类分区记下来，经 MCP 的每条回执最上面都带「通用 + 本类」两区，下次干活先看到。
/// </summary>
/// <remarks>
/// 1.38.0（用户定：笔记只记指令本身很少的注意点，不记项目内容）：每条不超过 <see cref="NoteLimit"/> 字、每区不超过 <see cref="AreaLimit"/> 条，超了直接拒，
/// 逼着写成「哪条指令、什么情况、怎么做」一句话；某个零件、某张图的事写进项目自己的文档，不进这里。
/// </remarks>
/// <remarks>
/// 写入走 <c>strenua.note.write</c>，存模块数据目录的 <see cref="FileName"/>，不碰 SolidWorks（没开 SW 也能记）。
/// 人点按钮的回执不带笔记：笔记是写给 AI 的，控制台里每条都带只是噪声。
/// </remarks>
/// <param name="directory">存档目录；null 表示只在内存里（离线测试用）。</param>
internal sealed class WorkNotes(string? directory)
{
    public const string FileName = "notes.json";

    /// <summary>不属于哪一类的笔记。</summary>
    public const string Common = "common";

    /// <summary>一条笔记最多几个字。</summary>
    internal const int NoteLimit = 100;

    /// <summary>一个分区最多几条。</summary>
    internal const int AreaLimit = 6;

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();
    private readonly string? _path = directory is null ? null : Path.Combine(directory, FileName);
    private Dictionary<string, List<string>>? _notes;

    /// <summary>可写的分区：通用 + 页面上的类。</summary>
    public static IReadOnlyList<string> Areas { get; } = [Common, .. QuickClasses.Order];

    /// <summary>分区的中文名。</summary>
    public static string Title(string area) => area == Common ? "通用" : QuickClasses.Title(area);

    /// <summary>认分区：英文名或中文类名，认不出返回 null。</summary>
    public static string? Area(string? text)
        => Areas.FirstOrDefault(area => area.Equals(text?.Trim(), StringComparison.OrdinalIgnoreCase) || Title(area) == text?.Trim());

    /// <summary>一个分区的笔记。</summary>
    public IReadOnlyList<string> Read(string area)
    {
        lock (_gate)
            return Load().TryGetValue(area, out var list) ? [.. list] : [];
    }

    /// <summary>新记一条，或按序号（从 1 起）改掉 / 删掉一条。</summary>
    /// <param name="area">分区。</param>
    /// <param name="text">笔记；<see cref="QuickArgs.None"/> 配合 <paramref name="replace"/> 是删。</param>
    /// <param name="replace">要改的序号；null 是新记。</param>
    /// <returns>失败原因；成功为 null。</returns>
    public string? Write(string area, string text, int? replace)
    {
        lock (_gate)
        {
            var notes = Load();
            if (!notes.TryGetValue(area, out var list))
                notes[area] = list = [];
            if (!text.Equals(QuickArgs.None, StringComparison.OrdinalIgnoreCase) && text.Length > NoteLimit)
                return $"一条笔记不超过 {NoteLimit} 字（这条 {text.Length} 字）：只写哪条指令、什么情况、怎么做；项目和图纸的细节不记在这里。";
            if (replace is null && list.Count >= AreaLimit)
                return $"{Title(area)}区已有 {list.Count} 条（上限 {AreaLimit}）：先用 replace= 合并或删掉旧的。";
            if (replace is { } number)
            {
                if (number < 1 || number > list.Count)
                    return $"{Title(area)}区没有第 {number} 条（共 {list.Count} 条）。";
                if (text.Equals(QuickArgs.None, StringComparison.OrdinalIgnoreCase))
                    list.RemoveAt(number - 1);
                else
                    list[number - 1] = text;
            }
            else
            {
                list.Add(text);
            }

            return Save(notes);
        }
    }

    /// <summary>回执上方的笔记区：通用 + 本类，都没有就是空串。</summary>
    public string Section(string area)
    {
        var text = new StringBuilder();
        foreach (var name in area == Common ? [Common] : new[] { Common, area })
        {
            var list = Read(name);
            if (list.Count == 0)
                continue;
            text.AppendLine($"【笔记区·{Title(name)}】");
            for (var i = 0; i < list.Count; i++)
                text.AppendLine($"{i + 1}. {list[i]}");
        }

        return text.Length == 0 ? string.Empty : text.AppendLine("【笔记区完】").ToString();
    }

    /// <summary>把笔记区放到回执最上面，成败与数据不变。</summary>
    public CommandResult Prepend(string area, CommandResult result)
    {
        var section = Section(area);
        if (section.Length == 0)
            return result;
        var text = section + result.Message;
        return result.Success ? CommandResult.Ok(text, result.Data) : CommandResult.Fail(text);
    }

    private Dictionary<string, List<string>> Load()
    {
        if (_notes is not null)
            return _notes;
        _notes = [];
        if (_path is null || !File.Exists(_path))
            return _notes;
        try
        {
            _notes = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return _notes;
    }

    private string? Save(Dictionary<string, List<string>> notes)
    {
        if (_path is null)
            return null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(notes, Format));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "没存进本机：" + ex.Message;
        }
    }
}
