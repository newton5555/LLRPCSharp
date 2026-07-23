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
        bool hasAssistLine = assistProvider is not null;
        bool assistRendered = false;

        try
        {
            Console.TreatControlCAsInput = true;
            var buffer = new StringBuilder();
            int cursor = 0;
            int historyIndex = _history.Count;
            int tabIndex = -1;
            string? preTabPrefix = null;

            var assist = GetAssist(assistProvider, buffer.ToString(), cursor);
            Redraw(prompt, buffer, cursor, assist, assistRendered, hasAssistLine);
            assistRendered = hasAssistLine;

            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);

                if (key.Key != ConsoleKey.Tab)
                {
                    tabIndex = -1;
                    preTabPrefix = null;
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
                }
                else if (key.Key == ConsoleKey.Tab && assist.Candidates.Count > 0)
                {
                    if (preTabPrefix is null)
                    {
                        preTabPrefix = buffer.ToString()[..cursor];
                    }

                    tabIndex = (tabIndex + 1) % assist.Candidates.Count;
                    string candidate = assist.Candidates[tabIndex];

                    string[] tokens = preTabPrefix.Split(' ');
                    if (tokens.Length > 0 && !preTabPrefix.EndsWith(' '))
                    {
                        tokens[^1] = candidate;
                        buffer.Clear();
                        buffer.Append(string.Join(" ", tokens));
                    }
                    else
                    {
                        buffer.Clear();
                        buffer.Append(preTabPrefix + candidate);
                    }

                    cursor = buffer.Length;
                }
                else if (key.Key == ConsoleKey.Backspace && cursor > 0)
                {
                    buffer.Remove(--cursor, 1);
                }
                else if (key.Key == ConsoleKey.Delete && cursor < buffer.Length)
                {
                    buffer.Remove(cursor, 1);
                }
                else if (key.Key == ConsoleKey.LeftArrow && cursor > 0)
                {
                    cursor--;
                }
                else if (key.Key == ConsoleKey.RightArrow)
                {
                    if (cursor == buffer.Length && !string.IsNullOrEmpty(assist.GhostSuffix))
                    {
                        buffer.Append(assist.GhostSuffix);
                        cursor = buffer.Length;
                    }
                    else if (cursor < buffer.Length)
                    {
                        cursor++;
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
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    buffer.Insert(cursor++, key.KeyChar);
                }

                assist = GetAssist(assistProvider, buffer.ToString(), cursor);
                Redraw(prompt, buffer, cursor, assist, assistRendered, hasAssistLine);
                assistRendered = hasAssistLine;
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

    private static void Redraw(
        string prompt,
        StringBuilder buffer,
        int cursor,
        Commands.InputAssist assist,
        bool clearAssistLine,
        bool renderAssistLine)
    {
        // Hide cursor during redrawing to eliminate flickering
        Console.Write("\u001b[?25l");

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
                ? "Tab/→ accept suggestion · Esc clears"
                : assist.Hint;

            Console.Write("\n\r\u001b[2K");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  └─ " + hint);
            Console.ResetColor();
            Console.Write("\u001b[1A\r");
        }

        int targetColumn = rawPrompt.Length + cursor;
        if (targetColumn > 0)
        {
            Console.Write($"\u001b[{targetColumn}C");
        }

        // Restore visible cursor
        Console.Write("\u001b[?25h");
    }

    private static void CommitLine(string prompt, StringBuilder buffer, bool assistRendered)
    {
        Console.Write("\u001b[?25l");
        ClearEditor(assistRendered);
        string rawPrompt = CleanMarkup(prompt);
        Console.Write(rawPrompt);
        Console.Write(buffer.ToString());
        Console.WriteLine();
        Console.Write("\u001b[?25h");
    }

    private static void ClearEditor(bool hasAssistLine)
    {
        Console.Write("\r\u001b[2K");
        if (hasAssistLine)
        {
            Console.Write("\u001b[1B\r\u001b[2K\u001b[1A\r");
        }
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
}
