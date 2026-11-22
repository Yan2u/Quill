using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Inlines;
using Quill.Editor.Projection;
using Quill.Editor.State;

namespace Quill.Editor.Statistics;

/// <summary>Counts of a text: words, characters (without whitespace) and non-empty lines.</summary>
public readonly record struct TextCounts(int Words, int Characters, int Lines)
{
    /// <summary>Reading time in minutes at <paramref name="wordsPerMinute"/> (a setting, Q105), at least 1 for any text.</summary>
    public int MinutesAt(int wordsPerMinute) => Words == 0 ? 0 : Math.Max(1, (int)Math.Round(Words / (double)Math.Max(1, wordsPerMinute)));

    public static TextCounts operator +(TextCounts left, TextCounts right) =>
        new(left.Words + right.Words, left.Characters + right.Characters, left.Lines + right.Lines);
}

/// <summary>
/// Word count (M6.5, Typora's status bar): of the displayed text, so Markdown syntax (markers, link
/// destinations, image sources) is not counted. A word is a run of letters and digits (with apostrophes
/// and hyphens inside it); each CJK character (Han, kana, Hangul) is one word. Characters are those that
/// are not whitespace; lines are the non-empty lines of the text. Code and math sources count; front
/// matter and a code block's language do not〔默认〕. Counts are kept per leaf, so counting again after an
/// edit counts only the leaves it changed.
/// </summary>
public static class WordStatistics
{
    /// <summary>The default reading speed for the reading time (Q105, the setting's default).</summary>
    public const int DefaultWordsPerMinute = 382;

    private static readonly ConditionalWeakTable<LeafBlock, StrongBox<TextCounts>> Leaves = [];

    /// <summary>The counts of the whole document.</summary>
    public static TextCounts Of(Document document)
    {
        var references = ReferenceIndex.Of(document);
        var total = default(TextCounts);
        foreach (var leaf in Navigation.Leaves(document))
        {
            if (Counted(leaf))
            {
                total += Leaves.GetValue(leaf, key => new StrongBox<TextCounts>(Count(TextOf(key, references)))).Value;
            }
        }

        return total;
    }

    /// <summary>The counts of the selected text, or null for a caret.</summary>
    public static TextCounts? OfSelection(EditorState state)
    {
        if (state.Selection.IsCollapsed)
        {
            return null;
        }

        var document = state.Document;
        var (start, end) = PositionOrder.Ordered(document, state.Selection);
        var references = ReferenceIndex.Of(document);
        var total = default(TextCounts);
        bool started = false;
        foreach (var leaf in Navigation.Leaves(document))
        {
            var leafEnd = new TextPosition(leaf.Id, leaf.Text.Length);
            if (!started)
            {
                if (PositionOrder.Compare(document, start, leafEnd) > 0)
                {
                    continue;
                }

                started = true;
            }

            bool endsHere = end is TextPosition last && last.Block == leaf.Id;
            if (!endsHere && PositionOrder.Compare(document, new TextPosition(leaf.Id, 0), end) >= 0)
            {
                break;
            }

            int from = start is TextPosition a && a.Block == leaf.Id ? a.Offset : 0;
            int to = endsHere ? ((TextPosition)end).Offset : leaf.Text.Length;
            if (Counted(leaf) && to > from)
            {
                // The selected source of the leaf, its syntax stripped as for the whole leaf.
                total += Count(TextOf(leaf with { Text = leaf.Text[from..to] }, references));
            }

            if (endsHere)
            {
                break;
            }
        }

        return total;
    }

    /// <summary>Counts a text.</summary>
    public static TextCounts Count(string text)
    {
        int words = 0, characters = 0, lines = 0;
        bool inWord = false, lineHasText = false;
        for (int i = 0; i < text.Length;)
        {
            var rune = Rune.GetRuneAt(text, i);
            i += rune.Utf16SequenceLength;
            if (rune.Value == '\n')
            {
                lines += lineHasText ? 1 : 0;
                lineHasText = false;
                inWord = false;
                continue;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                inWord = false;
                continue;
            }

            characters++;
            lineHasText = true;
            if (IsCjk(rune))
            {
                words++;
                inWord = false;
            }
            else if (Rune.IsLetterOrDigit(rune) || rune.Value == '_' || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark)
            {
                words += inWord ? 0 : 1;
                inWord = true;
            }
            else if (!(inWord && rune.Value is '\'' or '’' or '-' && i < text.Length && char.IsLetterOrDigit(text[i])))
            {
                inWord = false;
            }
        }

        lines += lineHasText ? 1 : 0;
        return new TextCounts(words, characters, lines);
    }

    private static bool Counted(LeafBlock leaf) => leaf is not (FrontMatter or CodeLanguage);

    private static string TextOf(LeafBlock leaf, ReferenceIndex references) =>
        leaf is TextLeafBlock ? PlainText.Of(leaf, references) : leaf.Text;

    private static bool IsCjk(Rune rune) => rune.Value is
        (>= 0x4E00 and <= 0x9FFF) or (>= 0x3400 and <= 0x4DBF) or (>= 0x20000 and <= 0x2FA1F) or (>= 0xF900 and <= 0xFAFF)
        or (>= 0x3040 and <= 0x30FF) or (>= 0x31F0 and <= 0x31FF) or (>= 0xAC00 and <= 0xD7AF) or (>= 0x1100 and <= 0x11FF);
}
