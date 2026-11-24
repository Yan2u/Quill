using System.Text;

namespace Quill.Settings.Taskbar;

/// <summary>The intent parsed from Quill's command-line arguments.</summary>
public enum LaunchActionKind
{
    Default,
    NewWindow,
    OpenFile,
    OpenFolder,
    OpenPath,
    VerifyStartup,
}

/// <summary>A parsed command-line launch request.</summary>
public sealed record LaunchRequest(LaunchActionKind Kind, string? TargetPath = null);

/// <summary>
/// Builds, quotes, splits, and parses command-line arguments used by taskbar Jump List entries,
/// secondary-instance forwarding, and file launches.
/// </summary>
public static class LaunchArguments
{
    public const string NewWindowFlag = "--new-window";
    public const string OpenFileFlag = "--open-file";
    public const string OpenFolderFlag = "--open-folder";
    public const string VerifyStartupFlag = "--verify-startup";

    public static string ForNewWindow() => NewWindowFlag;

    public static string ForOpenFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return $"{OpenFileFlag} {Quote(filePath)}";
    }

    public static string ForOpenFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        return $"{OpenFolderFlag} {Quote(folderPath)}";
    }

    public static LaunchRequest Parse(IReadOnlyList<string> args, bool includesExecutable = true)
    {
        ArgumentNullException.ThrowIfNull(args);

        int start = includesExecutable ? 1 : 0;
        if (start >= args.Count)
        {
            return new LaunchRequest(LaunchActionKind.Default);
        }

        for (int i = start; i < args.Count; i++)
        {
            if (string.Equals(args[i], VerifyStartupFlag, StringComparison.Ordinal))
            {
                return i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1])
                    ? new LaunchRequest(LaunchActionKind.VerifyStartup, args[i + 1])
                    : new LaunchRequest(LaunchActionKind.Default);
            }
        }

        for (int i = start; i < args.Count; i++)
        {
            string arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (string.Equals(arg, NewWindowFlag, StringComparison.Ordinal))
            {
                return new LaunchRequest(LaunchActionKind.NewWindow);
            }

            if (string.Equals(arg, OpenFileFlag, StringComparison.Ordinal))
            {
                return i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1])
                    ? new LaunchRequest(LaunchActionKind.OpenFile, args[i + 1])
                    : new LaunchRequest(LaunchActionKind.Default);
            }

            if (string.Equals(arg, OpenFolderFlag, StringComparison.Ordinal))
            {
                return i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1])
                    ? new LaunchRequest(LaunchActionKind.OpenFolder, args[i + 1])
                    : new LaunchRequest(LaunchActionKind.Default);
            }

            return new LaunchRequest(LaunchActionKind.OpenPath, arg);
        }

        return new LaunchRequest(LaunchActionKind.Default);
    }

    public static LaunchRequest ParseCommandLine(string? commandLine) =>
        Parse(SplitCommandLine(commandLine), includesExecutable: false);

    /// <summary>
    /// Quotes an argument using Windows <c>CommandLineToArgvW</c> escaping rules so trailing backslashes
    /// (such as drive roots like <c>C:\</c>) and embedded quotes survive round-tripping.
    /// </summary>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        int backslashes = 0;

        foreach (char ch in value)
        {
            if (ch == '\\')
            {
                backslashes++;
            }
            else if (ch == '"')
            {
                sb.Append('\\', (backslashes * 2) + 1);
                sb.Append('"');
                backslashes = 0;
            }
            else
            {
                if (backslashes > 0)
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                }

                sb.Append(ch);
            }
        }

        if (backslashes > 0)
        {
            sb.Append('\\', backslashes * 2);
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// Splits a command-line string according to Windows <c>CommandLineToArgvW</c> parsing rules.
    /// </summary>
    public static IReadOnlyList<string> SplitCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        var args = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;
        int i = 0;

        while (i < commandLine.Length)
        {
            char ch = commandLine[i];
            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (hasToken)
                {
                    args.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                i++;
                continue;
            }

            hasToken = true;
            if (ch == '\\')
            {
                int backslashes = 0;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    current.Append('\\', backslashes / 2);
                    if ((backslashes & 1) == 1)
                    {
                        current.Append('"');
                    }
                    else if (inQuotes && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    i++;
                }
                else
                {
                    current.Append('\\', backslashes);
                }
            }
            else if (ch == '"')
            {
                if (inQuotes && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
                {
                    current.Append('"');
                    i += 2;
                }
                else
                {
                    inQuotes = !inQuotes;
                    i++;
                }
            }
            else
            {
                current.Append(ch);
                i++;
            }
        }

        if (hasToken)
        {
            args.Add(current.ToString());
        }

        return args;
    }

    public static string GetFileDisplayName(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string name = Path.GetFileName(filePath);
        return string.IsNullOrEmpty(name) ? filePath : name;
    }

    public static string GetFolderDisplayName(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        string trimmed = Path.TrimEndingDirectorySeparator(folderPath.Trim());
        string name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
