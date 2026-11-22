namespace Quill.VariableXaml.Metadata;

/// <summary>
/// A parsed markup extension such as <c>{StaticResource Key}</c> or <c>{Binding Path=Name, Mode=OneWay}</c>.
/// </summary>
internal sealed record MarkupExtensionSyntax(string Name, string? PositionalArgument, IReadOnlyDictionary<string, string> NamedArguments)
{
    /// <summary>
    /// Parses one markup extension. Nested extensions in arguments are kept as raw text.
    /// Returns false for literal strings, including the <c>{}</c> escape.
    /// </summary>
    public static bool TryParse(string text, out MarkupExtensionSyntax? extension)
    {
        extension = null;
        string trimmed = text.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[^1] != '}' || trimmed.StartsWith("{}", StringComparison.Ordinal))
        {
            return false;
        }

        string body = trimmed[1..^1].Trim();
        int nameEnd = 0;
        while (nameEnd < body.Length && !char.IsWhiteSpace(body[nameEnd]) && body[nameEnd] != ',')
        {
            nameEnd++;
        }

        string name = body[..nameEnd];
        if (name.Length == 0)
        {
            return false;
        }

        string? positional = null;
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string argument in SplitArguments(body[nameEnd..]))
        {
            int equals = IndexOfTopLevel(argument, '=');
            if (equals > 0)
            {
                named[argument[..equals].Trim()] = argument[(equals + 1)..].Trim();
            }
            else if (positional is null)
            {
                positional = argument.Trim();
            }
        }

        extension = new MarkupExtensionSyntax(name, positional, named);
        return true;
    }

    /// <summary>Removes the <c>{}</c> literal escape from a markup attribute value.</summary>
    public static string Unescape(string text) =>
        text.StartsWith("{}", StringComparison.Ordinal) ? text[2..] : text;

    public static bool LooksLikeExtension(string text) =>
        text.Length > 1 && text[0] == '{' && !text.StartsWith("{}", StringComparison.Ordinal);

    /// <summary>
    /// True when a markup attribute value may be handed to XamlReader.Load: either a literal, or a
    /// well-formed extension from a small allowlist with simple arguments.
    /// </summary>
    /// <remarks>
    /// Malformed extensions can crash WinUI's native XAML parser with an access violation instead
    /// of a XamlParseException (reproduced: <c>Background="{T=he/xmResource Brush&gt;Key}"</c>, see
    /// SelfTest/CrashRepro). Half-typed values are routine in a live editor, so anything outside
    /// this grammar is applied through the object model, which reports a diagnostic instead.
    /// </remarks>
    public static bool IsSafeForXamlReader(string text)
    {
        string trimmed = text.Trim();
        if (!LooksLikeExtension(trimmed))
        {
            return true;
        }

        int position = 0;
        return TryReadSafeExtension(trimmed, ref position, depth: 0) && position == trimmed.Length;
    }

    private static readonly HashSet<string> SafeExtensionNames = new(StringComparer.Ordinal)
    {
        "StaticResource", "ThemeResource", "x:Null", "Binding", "TemplateBinding", "CustomResource", "RelativeSource",
    };

    private static bool TryReadSafeExtension(string text, ref int position, int depth)
    {
        if (depth > 3 || position >= text.Length || text[position] != '{')
        {
            return false;
        }

        position++;
        int nameStart = position;
        while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] is '_' or ':'))
        {
            position++;
        }

        if (!SafeExtensionNames.Contains(text[nameStart..position]))
        {
            return false;
        }

        bool first = true;
        while (true)
        {
            SkipSpaces(text, ref position);
            if (position >= text.Length)
            {
                return false;
            }

            if (text[position] == '}')
            {
                position++;
                return true;
            }

            if (!first)
            {
                if (text[position] != ',')
                {
                    return false;
                }

                position++;
                SkipSpaces(text, ref position);
            }

            first = false;
            int argumentStart = position;
            while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] is '_' or '.'))
            {
                position++;
            }

            if (position < text.Length && text[position] == '=')
            {
                if (position == argumentStart)
                {
                    return false;
                }

                position++;
                SkipSpaces(text, ref position);
                argumentStart = position;
            }

            if (position < text.Length && text[position] == '{')
            {
                if (!TryReadSafeExtension(text, ref position, depth + 1))
                {
                    return false;
                }

                continue;
            }

            while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] is '_' or '.' or '-' or '#' or ':' or '[' or ']' or '(' or ')'))
            {
                position++;
            }

            if (position == argumentStart)
            {
                return false;
            }
        }
    }

    private static void SkipSpaces(string text, ref int position)
    {
        while (position < text.Length && text[position] == ' ')
        {
            position++;
        }
    }

    private static IEnumerable<string> SplitArguments(string text)
    {
        int depth = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    if (text[start..i].Trim().Length > 0)
                    {
                        yield return text[start..i];
                    }

                    start = i + 1;
                    break;
            }
        }

        if (text[start..].Trim().Length > 0)
        {
            yield return text[start..];
        }
    }

    private static int IndexOfTopLevel(string text, char value)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
            }
            else if (text[i] == value && depth == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
