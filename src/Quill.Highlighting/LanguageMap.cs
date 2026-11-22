namespace Quill.Highlighting;

/// <summary>
/// The language of a fenced code block from its info string (11-source-mode.md): the first word,
/// lower-cased, through a table of common names and aliases, then the grammar's language IDs and file
/// extensions. The table is data and can grow.
/// </summary>
public static class LanguageMap
{
    /// <summary>Aliases of the language IDs of TextMateSharp.Grammars.</summary>
    public static IReadOnlyDictionary<string, string> Aliases { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["c#"] = "csharp",
        ["cs"] = "csharp",
        ["f#"] = "fsharp",
        ["fs"] = "fsharp",
        ["c++"] = "cpp",
        ["cc"] = "cpp",
        ["cxx"] = "cpp",
        ["h"] = "c",
        ["hpp"] = "cpp",
        ["cuda"] = "cuda-cpp",
        ["js"] = "javascript",
        ["mjs"] = "javascript",
        ["node"] = "javascript",
        ["jsx"] = "javascriptreact",
        ["ts"] = "typescript",
        ["tsx"] = "typescriptreact",
        ["py"] = "python",
        ["python3"] = "python",
        ["rb"] = "ruby",
        ["rs"] = "rust",
        ["golang"] = "go",
        ["sh"] = "shellscript",
        ["bash"] = "shellscript",
        ["zsh"] = "shellscript",
        ["shell"] = "shellscript",
        ["console"] = "shellscript",
        ["ps"] = "powershell",
        ["ps1"] = "powershell",
        ["pwsh"] = "powershell",
        ["bat"] = "bat",
        ["cmd"] = "bat",
        ["batch"] = "bat",
        ["yml"] = "yaml",
        ["md"] = "markdown",
        ["htm"] = "html",
        ["xhtml"] = "html",
        ["xaml"] = "xml",
        ["svg"] = "xml",
        ["csproj"] = "xml",
        ["plist"] = "xml",
        ["objc"] = "objective-c",
        ["objective-c++"] = "objective-cpp",
        ["objc++"] = "objective-cpp",
        ["docker"] = "dockerfile",
        ["make"] = "makefile",
        ["mk"] = "makefile",
        ["tex"] = "latex",
        ["vb.net"] = "vb",
        ["vbnet"] = "vb",
        ["asm"] = "x86asm",
        ["nasm"] = "x86asm",
        ["pl"] = "perl",
        ["raku"] = "perl6",
        ["jl"] = "julia",
        ["hbs"] = "handlebars",
        ["toml"] = "ini",
        ["cfg"] = "ini",
        ["conf"] = "ini",
        ["patch"] = "diff",
        ["pug"] = "jade",
        ["cshtml"] = "razor",
        ["jsonc"] = "jsonc",
        ["json5"] = "jsonc",
    };

    /// <summary>The language ID an info string names, or null for none (an empty info string).</summary>
    public static string? LanguageOf(string info)
    {
        string word = info.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        // "{.python}" and "language-python" are written for other renderers.
        word = word.Trim('{', '}').TrimStart('.');
        if (word.StartsWith("language-", StringComparison.OrdinalIgnoreCase))
        {
            word = word["language-".Length..];
        }

        return word.Length == 0 ? null : Aliases.TryGetValue(word, out var id) ? id : word.ToLowerInvariant();
    }
}
