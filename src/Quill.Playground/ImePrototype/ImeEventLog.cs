using System.Diagnostics;
using System.Text;

namespace Quill.Playground.ImePrototype;

/// <summary>
/// A timestamped log of every text service event and key, kept in memory and optionally appended
/// to a file, so a manual IME session (Q6) can be analyzed afterwards.
/// </summary>
internal sealed class ImeEventLog
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<string> _lines = [];

    public ImeEventLog(string? filePath = null)
    {
        FilePath = filePath;
        if (filePath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, $"# Quill IME lab log, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
        }
    }

    public event Action<string>? LineAdded;

    public string? FilePath { get; }

    public IReadOnlyList<string> Lines => _lines;

    public void Write(string message)
    {
        string line = $"{_clock.Elapsed.TotalMilliseconds,10:F1}  {message}";
        _lines.Add(line);
        if (FilePath is not null)
        {
            File.AppendAllText(FilePath, line + Environment.NewLine);
        }

        LineAdded?.Invoke(line);
    }

    /// <summary>Quotes text so control characters and spaces stay visible in the log.</summary>
    public static string Quote(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (char c in text)
        {
            builder.Append(c switch
            {
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                '"' => "\\\"",
                < ' ' or (>= '​' and <= '‏') => $"\\u{(int)c:X4}",
                _ => c.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }
}
