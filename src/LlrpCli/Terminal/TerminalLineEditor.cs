using System.Text;
using Spectre.Console;

namespace LlrpCli.Terminal;

public sealed record LineReadResult(string? Text, bool Cancelled = false);

public sealed class TerminalLineEditor : IDisposable
{
    private const int MaximumHistoryEntries = 500;
    private readonly List<string> _history;
    private readonly string _historyPath;

    public TerminalLineEditor(string? historyPath = null)
    {
        _historyPath = historyPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LLRPCSharp", "cli_history.txt");
        _history = LoadHistory(_historyPath)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .TakeLast(MaximumHistoryEntries)
            .ToList();
    }

    public LineReadResult ReadLine(string prompt, Func<string, int, Commands.InputAssist>? assistProvider = null)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Write(prompt);
            return new LineReadResult(Console.ReadLine());
        }

        bool previousControlMode = Console.TreatControlCAsInput;
        bool assistRendered = false;

        try
        {
            Console.TreatControlCAsInput = true;
            var buffer = new StringBuilder();
            int cursor = 0;
            int historyIndex = _history.Count;
            CompletionState? completionState = null;

            var assist = GetAssist(assistProvider, buffer.ToString(), cursor);
            bool renderAssistLine = ShouldRenderAssistLine(assist);
            Redraw(prompt, buffer, cursor, assist, assistRendered, renderAssistLine);
            assistRendered = renderAssistLine;

            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                bool redraw = false;

                if (key.Key != ConsoleKey.Tab)
                {
                    completionState = null;
                }

                if (key.Key == ConsoleKey.Enter)
                {
                    CommitLine(prompt, buffer, assistRendered);
                    string text = buffer.ToString();
                    Remember(text);
                    return new LineReadResult(text);
                }

                if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    ClearEditor(assistRendered);
                    Console.Write(CleanMarkup(prompt));
                    Console.WriteLine("^C");
                    return new LineReadResult(string.Empty, Cancelled: true);
                }

                if (key.Key == ConsoleKey.Escape)
                {
                    buffer.Clear();
                    cursor = 0;
                    redraw = true;
                }
                else if (key.Key == ConsoleKey.Tab && assist.Candidates.Count > 0)
                {
                    bool reverse = key.Modifiers.HasFlag(ConsoleModifiers.Shift);
                    if (!reverse && cursor == buffer.Length && !string.IsNullOrEmpty(assist.GhostSuffix))
                    {
                        buffer.Insert(cursor, assist.GhostSuffix);
                        cursor += assist.GhostSuffix.Length;
                        completionState = null;
                    }
                    else
                    {
                        completionState = Complete(buffer, ref cursor, assist.Candidates, completionState, reverse);
                    }
                    redraw = true;
                }
                else if (key.Key == ConsoleKey.Backspace && cursor > 0)
                {
                    buffer.Remove(--cursor, 1);
                    redraw = true;
                }
                else if (key.Key == ConsoleKey.Delete && cursor < buffer.Length)
                {
                    buffer.Remove(cursor, 1);
                    redraw = true;
                }
                else if (key.Key == ConsoleKey.LeftArrow && cursor > 0)
                {
                    cursor--;
                    redraw = true;
                }
                else if (key.Key == ConsoleKey.RightArrow)
                {
                    if (cursor == buffer.Length && !string.IsNullOrEmpty(assist.GhostSuffix))
                    {
                        buffer.Append(assist.GhostSuffix);
                        cursor = buffer.Length;
                        redraw = true;
                    }
                    else if (cursor < buffer.Length)
                    {
                        cursor++;
                        redraw = true;
                    }
                }
                else if (key.Key == ConsoleKey.UpArrow && _history.Count > 0)
                {
                    if (historyIndex > 0)
                    {
                        historyIndex--;
                        buffer.Clear();
                        buffer.Append(_history[historyIndex]);
                        cursor = buffer.Length;
                        redraw = true;
                    }
                }
                else if (key.Key == ConsoleKey.DownArrow && _history.Count > 0)
                {
                    if (historyIndex < _history.Count - 1)
                    {
                        historyIndex++;
                        buffer.Clear();
                        buffer.Append(_history[historyIndex]);
                        cursor = buffer.Length;
                    }
                    else if (historyIndex == _history.Count - 1)
                    {
                        historyIndex = _history.Count;
                        buffer.Clear();
                        cursor = 0;
                        redraw = true;
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    buffer.Insert(cursor++, key.KeyChar);
                    redraw = true;
                }

                if (!redraw)
                {
                    continue;
                }

                assist = GetAssist(assistProvider, buffer.ToString(), cursor);
                renderAssistLine = ShouldRenderAssistLine(assist);
                Redraw(prompt, buffer, cursor, assist, assistRendered, renderAssistLine);
                assistRendered = renderAssistLine;
            }
        }
        finally
        {
            Console.TreatControlCAsInput = previousControlMode;
        }
    }

    private static Commands.InputAssist GetAssist(
        Func<string, int, Commands.InputAssist>? assistProvider,
        string text,
        int cursor)
    {
        if (assistProvider is null)
        {
            return Commands.InputAssist.Empty;
        }

        try
        {
            return assistProvider(text, cursor);
        }
        catch
        {
            return Commands.InputAssist.Empty;
        }
    }

    private static bool ShouldRenderAssistLine(Commands.InputAssist assist)
    {
        return !string.IsNullOrWhiteSpace(assist.Hint);
    }

    private static void Redraw(
        string prompt,
        StringBuilder buffer,
        int cursor,
        Commands.InputAssist assist,
        bool clearAssistLine,
        bool renderAssistLine)
    {
        ClearEditor(clearAssistLine);

        string rawPrompt = CleanMarkup(prompt);
        Console.Write(rawPrompt);
        Console.Write(buffer.ToString());

        string ghost = cursor == buffer.Length ? assist.GhostSuffix : string.Empty;
        if (!string.IsNullOrEmpty(ghost))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(ghost);
            Console.ResetColor();
        }

        if (renderAssistLine)
        {
            string hint = string.IsNullOrWhiteSpace(assist.Hint)
                ? "Tab/→ accepts · Shift+Tab reverses · Esc clears"
                : assist.Hint;

            int windowWidth = 80;
            try
            {
                if (!Console.IsOutputRedirected && Console.WindowWidth > 10)
                {
                    windowWidth = Console.WindowWidth;
                }
            }
            catch { }

            int maxHintLen = Math.Max(10, windowWidth - 8);
            if (hint.Length > maxHintLen)
            {
                hint = string.Concat(hint.AsSpan(0, maxHintLen - 3), "...");
            }

            Console.Write("\n\r\u001b[2K");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  └─ " + hint);
            Console.ResetColor();
            Console.Write("\u001b[1A\r");
        }

        Console.Write("\r");
        int targetColumn = rawPrompt.Length + cursor;
        if (targetColumn > 0)
        {
            Console.Write($"\u001b[{targetColumn}C");
        }
    }

    private static void CommitLine(string prompt, StringBuilder buffer, bool assistRendered)
    {
        ClearEditor(assistRendered);
        string rawPrompt = CleanMarkup(prompt);
        Console.Write(rawPrompt);
        Console.Write(buffer.ToString());
        Console.WriteLine();
    }

    private static void ClearEditor(bool hasAssistLine)
    {
        Console.Write("\r\u001b[2K");
        if (hasAssistLine)
        {
            Console.Write("\u001b[1B\r\u001b[2K\u001b[1A\r");
        }
    }

    private static CompletionState? Complete(
        StringBuilder buffer,
        ref int cursor,
        IReadOnlyList<string> candidates,
        CompletionState? state,
        bool reverse)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        if (state is not null)
        {
            int direction = reverse ? -1 : 1;
            int index = (state.Index + direction + state.Candidates.Count) % state.Candidates.Count;
            ReplaceRange(buffer, state.TokenStart, cursor, state.Candidates[index]);
            cursor = state.TokenStart + state.Candidates[index].Length;
            return state with { Index = index };
        }

        int tokenStart = cursor;
        while (tokenStart > 0 && !char.IsWhiteSpace(buffer[tokenStart - 1]))
        {
            tokenStart--;
        }

        if (candidates.Count == 1)
        {
            ReplaceRange(buffer, tokenStart, cursor, candidates[0]);
            cursor = tokenStart + candidates[0].Length;
            return null;
        }

        string commonPrefix = LongestCommonPrefix(candidates);
        int currentLength = cursor - tokenStart;
        if (!reverse && commonPrefix.Length > currentLength)
        {
            ReplaceRange(buffer, tokenStart, cursor, commonPrefix);
            cursor = tokenStart + commonPrefix.Length;
            return new CompletionState(candidates, tokenStart, -1);
        }

        int selected = reverse ? candidates.Count - 1 : 0;
        ReplaceRange(buffer, tokenStart, cursor, candidates[selected]);
        cursor = tokenStart + candidates[selected].Length;
        return new CompletionState(candidates, tokenStart, selected);
    }

    private static string LongestCommonPrefix(IReadOnlyList<string> values)
    {
        string prefix = values[0];
        foreach (string value in values.Skip(1))
        {
            int length = 0;
            while (length < prefix.Length && length < value.Length &&
                   char.ToUpperInvariant(prefix[length]) == char.ToUpperInvariant(value[length]))
            {
                length++;
            }

            prefix = prefix[..length];
            if (prefix.Length == 0)
            {
                break;
            }
        }

        return prefix;
    }

    private static void ReplaceRange(StringBuilder buffer, int start, int end, string value)
    {
        buffer.Remove(start, end - start);
        buffer.Insert(start, value);
    }

    private void Remember(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _history.RemoveAll(item => item.Equals(text, StringComparison.Ordinal));
        _history.Add(text);

        if (_history.Count > MaximumHistoryEntries)
        {
            _history.RemoveAt(0);
        }

        SaveHistory(_historyPath, _history);
    }

    private static List<string> LoadHistory(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return File.ReadAllLines(path).ToList();
            }
        }
        catch { }

        return [];
    }

    private static void SaveHistory(string path, List<string> history)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllLines(path, history);
        }
        catch { }
    }

    private static string CleanMarkup(string text)
    {
        var sb = new StringBuilder();
        bool inTag = false;
        foreach (char c in text)
        {
            if (c == '[')
            {
                inTag = true;
            }
            else if (c == ']')
            {
                inTag = false;
            }
            else if (!inTag)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public void Dispose() { }

    private sealed record CompletionState(IReadOnlyList<string> Candidates, int TokenStart, int Index);
}
