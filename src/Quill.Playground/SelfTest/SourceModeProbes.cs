using System.Diagnostics;
using Microsoft.UI.Xaml;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Input;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Playground.Samples;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M6.1, layer 2: the source mode (11-source-mode.md) in a <see cref="MarkdownBox"/>, driven through its
/// entry points: Ctrl+/ both ways with the caret following the text, lines without block spacing,
/// highlighting (Markdown and fenced code in Light+), typing in the source, saving from it.
/// </summary>
internal static class SourceModeProbes
{
    private const string Sample = "# Title\n\nFirst paragraph with **bold**.\n\n```cs\nvar x = 1;\n```\n\nLast paragraph.";

    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var box = new MarkdownBox
        {
            Width = 640,
            Height = 480,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Clipboard = new MemoryClipboard(),
            RequestedTheme = ElementTheme.Light,
        };
        context.Stage.Children.Add(box);
        await Task.Delay(50);

        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(Sample, ids).Document;
        var paragraph = document.Children[1];
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(paragraph.Id, 6) }, ids);
        await Task.Delay(50);
        var original = box.State.Document;

        // Ctrl+/ enters the source mode: one line view per line, the caret on the same text.
        box.HandleKey(0xBF, KeyModifiers.Ctrl);
        await Task.Delay(50);
        var lines = Realized<MdSourceLine>(box);
        int caret = SourceMode.OffsetOf(box.State.Document, box.State.Selection.Head);
        context.Check(box.IsSourceMode && lines.Count == 9 && Sample.IndexOf("First", StringComparison.Ordinal) + 6 == caret,
            "source: Ctrl+/ shows one line per source line, the caret on the same text (11)", $"source {box.IsSourceMode}, {lines.Count} lines, caret {caret}");

        var first = box.Document!.Geometry.Bounds(lines[0]);
        var second = box.Document.Geometry.Bounds(lines[1]);
        context.Check(box.Document.IsSource && Math.Abs(second.Y - first.Bottom) < 0.5, "source: lines follow one another without block spacing", $"line 1 {first}, line 2 {second}");

        // Highlighting: the heading and the C# keyword get their Light+ colors.
        for (int i = 0; i < 50 && box.Highlighting is { IsComplete: false }; i++)
        {
            await Task.Delay(20);
        }

        var highlighting = box.Highlighting!;
        var heading = highlighting.SpansOf(lines[0].BlockId);
        var code = highlighting.SpansOf(lines[5].BlockId);
        bool headingColored = heading is { Count: > 0 } && heading.Any(span => span.Foreground != highlighting.DefaultForeground);
        bool keywordBlue = code is { Count: > 0 } && code[0].Foreground == 0xFF0000FF;
        context.Check(highlighting.IsComplete && headingColored && keywordBlue, "source: Markdown and the fenced C# are highlighted with Light+ (Q85)",
            $"complete {highlighting.IsComplete}; heading runs {heading?.Count}; first code run {code?.FirstOrDefault().Foreground:X8}");

        // Typing in the source edits the line; the highlighting follows within the key.
        var times = new List<double>();
        foreach (char c in " typed")
        {
            var watch = Stopwatch.StartNew();
            box.HandleCharacter(c);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        string text = SourceText.ToText(box.State.Document);
        context.Check(text.Contains("First  typedparagraph", StringComparison.Ordinal) || text.Contains("First  typed", StringComparison.Ordinal),
            "source: typed characters go into the source line", text.Split('\n')[2]);
        context.Info($"source: keystroke median {times[times.Count / 2]:F2} ms, max {times[^1]:F2} ms");

        // Saving from the source writes the source text and clears the modified state.
        string folder = RepositoryPaths.Temp("m6-1");
        string path = Path.Combine(folder, "source.md");
        bool modified = box.IsModified;
        var snapshot = box.PrepareSave();
        await MarkdownFile.SaveAsync(snapshot.Document, path);
        box.MarkSaved(snapshot);
        string saved = await File.ReadAllTextAsync(path);
        context.Check(modified && !box.IsModified && saved == text, "source: saving writes the source text as it is and clears the modified state", saved.Replace("\n", "|"));

        // Ctrl+/ back: the edited paragraph is new, the other blocks are the same objects; still saved.
        box.HandleKey(0xBF, KeyModifiers.Ctrl);
        await Task.Delay(50);
        var back = box.State.Document;
        bool kept = ReferenceEquals(back.Children[0], original.Children[0]) && ReferenceEquals(back.Children[^1], original.Children[^1]) && !ReferenceEquals(back.Children[1], original.Children[1]);
        context.Check(!box.IsSourceMode && kept && !box.IsModified && Realized<MdSourceLine>(box).Count == 0 && MarkdownSerializer.Serialize(back).TrimEnd('\n') == text.TrimEnd('\n'),
            "source: Ctrl+/ back keeps the unchanged blocks and the saved state; the edit is in the document (06)", $"kept {kept}, modified {box.IsModified}");

        // The source edit is an undo item of the editing mode.
        box.HandleKey(0x5A, KeyModifiers.Ctrl);
        await Task.Delay(30);
        context.Check(MarkdownSerializer.Serialize(box.State.Document).TrimEnd('\n') == Sample && box.IsModified, "source: undo in the editing mode reverts the source edit", MarkdownSerializer.Serialize(box.State.Document).Replace("\n", "|"));
        context.Stage.Children.Clear();
    }

    private static List<T> Realized<T>(MarkdownBox box)
        where T : MdBlockView =>
        box.Document!.Geometry.Realized.Where(view => !box.Document.Geometry.IsDetached(view)).OfType<T>().ToList();
}
