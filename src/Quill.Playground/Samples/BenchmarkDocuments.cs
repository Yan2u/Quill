using System.Text;

namespace Quill.Playground.Samples;

/// <summary>
/// The benchmark documents of 09 in <c>.agents/artifacts/benchmarks</c>: long English documents of
/// 2000 and 10000 blocks, a mostly Chinese one, one dense with formulas, one dense with tables and one
/// with large code blocks. Generated from fixed seeds, so every run measures the same text; written once
/// and then reused (delete the folder to regenerate).
/// </summary>
internal static class BenchmarkDocuments
{
    public static readonly (string Name, Func<string> Generate)[] All =
    [
        ("long-2000.md", () => Long(2000, seed: 1)),
        ("long-10000.md", () => Long(10000, seed: 2)),
        ("chinese.md", () => Chinese(2000, seed: 3)),
        ("math.md", () => Math(1200, seed: 4)),
        ("tables.md", () => Tables(150, seed: 5)),
        ("code.md", () => Code(seed: 6)),
    ];

    private static readonly string[] Words =
    [
        "editor", "block", "source", "markdown", "caret", "layout", "render", "document", "table", "paragraph",
        "heading", "inline", "syntax", "virtual", "measure", "window", "theme", "input", "commit", "undo",
        "history", "projection", "reconcile", "the", "a", "of", "and", "to", "in", "is", "with", "for", "on",
    ];

    private static readonly string[] Hanzi =
    [
        "编辑器", "文档", "段落", "标题", "表格", "光标", "输入法", "渲染", "排版", "源码", "撤销", "历史",
        "的", "了", "在", "是", "我们", "可以", "一个", "这个", "中文", "测试", "性能", "保存", "打开", "窗口",
    ];

    /// <summary>The folder of the documents, written on first use; null outside the repository.</summary>
    public static string? EnsureWritten()
    {
        if (RepositoryPaths.Root is not { } root)
        {
            return null;
        }

        string folder = Path.Combine(root, ".agents", "artifacts", "benchmarks");
        Directory.CreateDirectory(folder);
        foreach (var (name, generate) in All)
        {
            string path = Path.Combine(folder, name);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, generate(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }

        return folder;
    }

    private static string Long(int blocks, int seed)
    {
        var random = new Random(seed);
        var text = new StringBuilder();
        for (int i = 0; i < blocks; i++)
        {
            switch (i % 20)
            {
                case 0:
                    text.Append(new string('#', 1 + random.Next(3))).Append(' ').Append(Sentence(random, 4)).Append("\n\n");
                    break;
                case 5:
                    for (int item = 0; item < 4; item++)
                    {
                        text.Append("- ").Append(Sentence(random, 8)).Append('\n');
                    }

                    text.Append('\n');
                    break;
                case 9:
                    text.Append("> ").Append(Sentence(random, 30)).Append("\n\n");
                    break;
                case 14:
                    text.Append("```csharp\n");
                    for (int line = 0; line < 6; line++)
                    {
                        text.Append($"var value{line} = Compute({line}, \"{Words[random.Next(Words.Length)]}\");\n");
                    }

                    text.Append("```\n\n");
                    break;
                default:
                    text.Append(Paragraph(random)).Append("\n\n");
                    break;
            }
        }

        return text.ToString();
    }

    private static string Chinese(int blocks, int seed)
    {
        var random = new Random(seed);
        var text = new StringBuilder();
        for (int i = 0; i < blocks; i++)
        {
            if (i % 15 == 0)
            {
                text.Append("## ").Append(Han(random, 4)).Append("\n\n");
                continue;
            }

            if (i % 15 == 7)
            {
                for (int item = 0; item < 3; item++)
                {
                    text.Append("1. ").Append(Han(random, 10)).Append("。\n");
                }

                text.Append('\n');
                continue;
            }

            int sentences = 2 + random.Next(4);
            for (int s = 0; s < sentences; s++)
            {
                text.Append(Han(random, 8 + random.Next(16)));
                text.Append(random.Next(6) == 0 ? $"（**{Han(random, 2)}** 与 `code`）" : string.Empty);
                text.Append(s % 2 == 0 ? "，" : "。");
            }

            text.Append("\n\n");
        }

        return text.ToString();
    }

    private static string Math(int blocks, int seed)
    {
        var random = new Random(seed);
        var text = new StringBuilder();
        for (int i = 0; i < blocks; i++)
        {
            if (i % 4 == 3)
            {
                text.Append("$$\n\\int_0^{").Append(random.Next(9) + 1).Append("} x^{").Append(random.Next(5) + 2)
                    .Append("} \\, dx = \\frac{a_").Append(i).Append("}{b} + \\sum_{k=1}^{n} k^2\n$$\n\n");
                continue;
            }

            text.Append(Sentence(random, 6)).Append(" $e^{i\\pi} + ").Append(random.Next(10)).Append(" = 0$ and ")
                .Append(Sentence(random, 6)).Append(" $\\sqrt{x_").Append(i).Append("}$ ").Append(Sentence(random, 8)).Append(".\n\n");
        }

        return text.ToString();
    }

    private static string Tables(int tables, int seed)
    {
        var random = new Random(seed);
        var text = new StringBuilder();
        for (int t = 0; t < tables; t++)
        {
            text.Append("### Table ").Append(t + 1).Append("\n\n");
            int columns = 3 + random.Next(4);
            text.Append('|');
            for (int c = 0; c < columns; c++)
            {
                text.Append(' ').Append(Words[random.Next(Words.Length)]).Append(" |");
            }

            text.Append("\n|");
            for (int c = 0; c < columns; c++)
            {
                text.Append(c % 3 == 1 ? " :---: |" : " --- |");
            }

            text.Append('\n');
            for (int r = 0; r < 12; r++)
            {
                text.Append('|');
                for (int c = 0; c < columns; c++)
                {
                    text.Append(' ').Append(c == 0 ? $"**{r}**" : Sentence(random, 1 + random.Next(3))).Append(" |");
                }

                text.Append('\n');
            }

            text.Append('\n').Append(Paragraph(random)).Append("\n\n");
        }

        return text.ToString();
    }

    private static string Code(int seed)
    {
        var random = new Random(seed);
        var text = new StringBuilder();
        for (int block = 0; block < 4; block++)
        {
            text.Append("## Listing ").Append(block + 1).Append("\n\n").Append(Paragraph(random)).Append("\n\n```csharp\n");
            for (int line = 0; line < 2500; line++)
            {
                text.Append(new string(' ', 4 * (line % 4))).Append($"result[{line}] = Process(input, {random.Next(1000)}, \"{Words[random.Next(Words.Length)]}\"); // {Sentence(random, 3)}\n");
            }

            text.Append("```\n\n");
        }

        return text.ToString();
    }

    private static string Paragraph(Random random)
    {
        var text = new StringBuilder();
        int sentences = 2 + random.Next(4);
        for (int s = 0; s < sentences; s++)
        {
            text.Append(char.ToUpperInvariant(Words[random.Next(Words.Length)][0]));
            text.Append(Sentence(random, 8 + random.Next(12))[1..]);
            text.Append(random.Next(5) switch
            {
                0 => $" with **{Words[random.Next(Words.Length)]}**",
                1 => $" and *{Words[random.Next(Words.Length)]}*",
                2 => $" in `{Words[random.Next(Words.Length)]}()`",
                3 => $" ([link](https://example.com/{random.Next(100)}))",
                _ => string.Empty,
            });
            text.Append(". ");
        }

        return text.ToString().TrimEnd();
    }

    private static string Sentence(Random random, int words)
    {
        var text = new StringBuilder();
        for (int w = 0; w < words; w++)
        {
            text.Append(w == 0 ? string.Empty : " ").Append(Words[random.Next(Words.Length)]);
        }

        return text.ToString();
    }

    private static string Han(Random random, int words)
    {
        var text = new StringBuilder();
        for (int w = 0; w < words; w++)
        {
            text.Append(Hanzi[random.Next(Hanzi.Length)]);
        }

        return text.ToString();
    }
}
