using System.Diagnostics.CodeAnalysis;

namespace Quill.Settings.Explorer;

/// <summary>Result of a passive Markdown file icon synchronization check.</summary>
public sealed record MarkdownFileIconSyncResult(
    IReadOnlyList<string> MatchedProgIds,
    IReadOnlyList<string> UpdatedProgIds,
    bool Notified);

/// <summary>
/// Passive hook for Markdown file association icons on Windows 11.
/// When the user explicitly selects Quill as the default app for a supported Markdown extension,
/// Windows creates or binds a ProgID (such as <c>Applications\Quill.exe</c>, <c>md_auto_file</c>, or
/// <c>Quill.md</c>) whose <c>shell\open\command</c> launches <c>Quill.exe</c>. This hook writes
/// <c>DefaultIcon</c> (pointing to <c>Assets\Quill-Markdown.ico</c>) and <c>shell\open\Icon</c> under
/// <c>HKCU\Software\Classes\&lt;ProgID&gt;</c> and notifies Shell only when values changed.
/// It never modifies <c>UserChoice</c>, extension defaults, or associations owned by other applications.
/// </summary>
public sealed class MarkdownFileIconHook(IRegistry registry, Func<string, bool>? fileExists = null)
{
    public const string ExecutableName = "Quill.exe";
    public const string MarkdownIconFileName = "Quill-Markdown.ico";

    public static IReadOnlyList<string> Extensions { get; } = [".md", ".markdown", ".mdown", ".mkd"];

    private const string UserClasses = @"HKEY_CURRENT_USER\Software\Classes";
    private const string ClassesRoot = @"HKEY_CLASSES_ROOT";
    private const string UserFileExts = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";

    private static readonly MarkdownFileIconSyncResult EmptyResult = new([], [], false);

    private readonly Func<string, bool> _fileExists = fileExists ?? File.Exists;

    /// <summary>
    /// Checks the active ProgID of each supported Markdown extension and updates its icon in
    /// <c>HKCU\Software\Classes</c> if Windows currently routes that extension to <paramref name="executablePath"/>.
    /// </summary>
    public MarkdownFileIconSyncResult SyncIfDefault(string? executablePath, string? iconPath)
    {
        if (!TryNormalizePath(executablePath, out string? fullExePath)
            || !TryNormalizePath(iconPath, out string? fullIconPath)
            || !string.Equals(Path.GetFileName(fullExePath), ExecutableName, StringComparison.OrdinalIgnoreCase)
            || !_fileExists(fullIconPath))
        {
            return EmptyResult;
        }

        string desiredDefaultIcon = $"\"{fullIconPath}\",0";
        string desiredOpenIcon = $"\"{fullExePath}\",0";
        string desiredCommand = $"\"{fullExePath}\" \"%1\"";

        var seenProgIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedProgIds = new List<string>();
        var updatedProgIds = new List<string>();

        foreach (string extension in Extensions)
        {
            string? resolved = ResolveActiveProgId(extension);
            if (resolved is null)
            {
                continue;
            }

            string progId = ResolveCurVer(resolved);
            if (!seenProgIds.Add(progId))
            {
                continue;
            }

            string userProgIdKey = $@"{UserClasses}\{progId}";
            string? userCommand = registry.GetValue($@"{userProgIdKey}\shell\open\command", null);
            string? effectiveCommand = userCommand ?? registry.GetValue($@"{ClassesRoot}\{progId}\shell\open\command", null);

            if (TryExtractExecutablePath(effectiveCommand) is not { } rawCommandExe
                || !TryNormalizePath(rawCommandExe, out string? fullCommandExe))
            {
                continue;
            }

            bool exactMatch = string.Equals(fullCommandExe, fullExePath, StringComparison.OrdinalIgnoreCase);
            bool staleQuillMatch = !exactMatch
                && string.Equals(Path.GetFileName(fullCommandExe), ExecutableName, StringComparison.OrdinalIgnoreCase)
                && !_fileExists(fullCommandExe);

            if (!exactMatch && !staleQuillMatch)
            {
                continue;
            }

            matchedProgIds.Add(progId);

            bool changed = false;
            changed |= SetIfDifferent($@"{userProgIdKey}\DefaultIcon", null, desiredDefaultIcon);
            changed |= SetIfDifferent($@"{userProgIdKey}\shell\open", "Icon", desiredOpenIcon);
            if (staleQuillMatch && userCommand is not null)
            {
                changed |= SetIfDifferent($@"{userProgIdKey}\shell\open\command", null, desiredCommand);
            }

            if (changed)
            {
                updatedProgIds.Add(progId);
            }
        }

        bool notified = false;
        if (updatedProgIds.Count > 0)
        {
            registry.NotifyAssociationsChanged();
            notified = true;
        }

        return new MarkdownFileIconSyncResult(matchedProgIds, updatedProgIds, notified);
    }

    /// <summary>Extracts the executable path from a Windows Shell <c>shell\open\command</c> command line.</summary>
    public static string? TryExtractExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        ReadOnlySpan<char> span = command.AsSpan().TrimStart();
        if (span.Length == 0)
        {
            return null;
        }

        if (span[0] == '"')
        {
            int closingQuote = span[1..].IndexOf('"');
            if (closingQuote <= 0)
            {
                return null;
            }

            string quoted = span.Slice(1, closingQuote).Trim().ToString();
            return quoted.Length > 0 ? quoted : null;
        }

        // Unquoted paths may contain spaces (e.g. C:\Program Files\Quill\Quill.exe "%1").
        // Match the first ".exe" followed by end-of-string or whitespace.
        int searchFrom = 0;
        while (searchFrom < span.Length)
        {
            int rel = span[searchFrom..].IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (rel < 0)
            {
                break;
            }

            int afterExe = searchFrom + rel + 4;
            if (afterExe == span.Length || char.IsWhiteSpace(span[afterExe]))
            {
                string exe = span[..afterExe].Trim().ToString();
                return exe.Length > 0 ? exe : null;
            }

            searchFrom = afterExe;
        }

        int space = -1;
        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsWhiteSpace(span[i]))
            {
                space = i;
                break;
            }
        }

        string token = (space < 0 ? span : span[..space]).Trim().ToString();
        return token.Length > 0 ? token : null;
    }

    private string? ResolveActiveProgId(string extension)
    {
        // Windows 11 24H2+ writes UserChoiceLatest\ProgId (with ProgId value); earlier versions use UserChoice.
        // Once UserChoiceLatest or UserChoice names a ProgID, do not fall back to generic defaults.
        string? userChoiceLatest = NonEmpty(registry.GetValue($@"{UserFileExts}\{extension}\UserChoiceLatest\ProgId", "ProgId"))
            ?? NonEmpty(registry.GetValue($@"{UserFileExts}\{extension}\UserChoiceLatest", "ProgId"));
        if (userChoiceLatest is not null)
        {
            return userChoiceLatest;
        }

        string? userChoice = NonEmpty(registry.GetValue($@"{UserFileExts}\{extension}\UserChoice", "ProgId"));
        if (userChoice is not null)
        {
            return userChoice;
        }

        return NonEmpty(registry.GetValue($@"{UserClasses}\{extension}", null))
            ?? NonEmpty(registry.GetValue($@"{ClassesRoot}\{extension}", null));
    }

    private string ResolveCurVer(string progId)
    {
        string current = progId;
        for (int hop = 0; hop < 2; hop++)
        {
            string? next = NonEmpty(registry.GetValue($@"{UserClasses}\{current}\CurVer", null))
                ?? NonEmpty(registry.GetValue($@"{ClassesRoot}\{current}\CurVer", null));
            if (next is null || string.Equals(next, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = next;
        }

        return current;
    }

    private bool SetIfDifferent(string key, string? name, string desiredValue)
    {
        string? existing = registry.GetValue(key, name);
        if (string.Equals(existing, desiredValue, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        registry.SetValue(key, name, desiredValue);
        return true;
    }

    private static bool TryNormalizePath(string? path, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            if (!Path.IsPathFullyQualified(expanded))
            {
                return false;
            }

            normalized = Path.GetFullPath(expanded);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
