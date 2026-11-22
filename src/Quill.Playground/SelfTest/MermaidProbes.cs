using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Editor.WinUI.Rendering.Diagrams;
using Quill.Editor.WinUI.Theming;
using Quill.Playground.Samples;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M6.3, layer 2: Mermaid diagrams rendered by mermaid-rs-renderer (quill_native.dll) and drawn as SVG
/// with Win2D. A source that does not render reports a failure; a block shows only the diagram while not
/// focused, the source with the diagram below it when focused, keeps the diagram while an edit renders,
/// and renders again in the dark theme. Pictures are saved for a look by eye.
/// </summary>
internal static class MermaidProbes
{
    private const string Flowchart = "flowchart LR\n  A[Start] --> B{Choice}\n  B -->|yes| C[中文节点]\n  B -->|no| D[End]";

    public static async Task RunAsync(SelfTestContext context)
    {
        await CheckRendererAsync(context);
        await CheckBlocksAsync(context);
    }

    private static async Task CheckRendererAsync(SelfTestContext context)
    {
        var first = MermaidDiagrams.Render(Flowchart, dark: false);
        var diagram = first as MermaidDiagram;
        context.Check(diagram is { Width: > 50, Height: > 20 }, "mermaid: a flowchart renders", diagram is null ? (first as MermaidFailure)?.Message : $"{diagram.Width:F0}x{diagram.Height:F0}, {MermaidDiagrams.LastRenderTime.TotalMilliseconds:F1} ms");
        MermaidDiagrams.Render(Flowchart, dark: false);
        context.Info($"mermaid: a second render takes {MermaidDiagrams.LastRenderTime.TotalMilliseconds:F1} ms");

        var sequence = MermaidDiagrams.Render("sequenceDiagram\n  Alice->>Bob: Hello\n  Bob-->>Alice: Hi", dark: false);
        context.Check(sequence is MermaidDiagram, "mermaid: a sequence diagram renders", (sequence as MermaidFailure)?.Message);
        context.Check(MermaidDiagrams.Render("this is not mermaid", dark: false) is MermaidFailure, "mermaid: a source that does not render is a failure");

        if (diagram is null)
        {
            return;
        }

        // Drawn by Direct2D's SVG support: ink on the page.
        var device = CanvasDevice.GetSharedDevice();
        using var target = new CanvasRenderTarget(device, diagram.Width, diagram.Height, 96);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Colors.White);
            session.DrawSvg(diagram.Document, new Windows.Foundation.Size(diagram.Width, diagram.Height));
        }

        var pixels = target.GetPixelColors();
        int inked = pixels.Count(static pixel => pixel.R < 200 || pixel.G < 200 || pixel.B < 200);
        context.Check(inked > pixels.Length / 100, "mermaid: the SVG draws with Win2D", $"{inked} of {pixels.Length} pixels");
        string file = Path.Combine(RepositoryPaths.Temp("m6-3"), "mermaid-flowchart.png");
        await target.SaveAsync(file);
        context.Info($"mermaid picture: {file}");
    }

    private static async Task CheckBlocksAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var box = new MarkdownBox
        {
            Width = 640,
            Height = 600,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Clipboard = new MemoryClipboard(),
        };
        context.Stage.Children.Add(box);
        await Task.Delay(50);

        Load(box, "first\n\n```mermaid\n" + Flowchart + "\n```\n\n```mermaid\nnot a diagram\n```");
        var block = await WaitForAsync(box, static block => block.Diagram is not null);
        context.Check(block is { ShowsDiagramOnly: true }, "mermaid: an unfocused block shows its diagram");
        if (block?.Diagram is { } diagram)
        {
            var padding = block.TextPadding;
            double expected = diagram.Height * Math.Min(1, (block.ActualWidth - padding.Left - padding.Right) / diagram.Width) + padding.Top + padding.Bottom;
            context.Check(Math.Abs(block.ActualHeight - expected) < 1.5, "mermaid: the block is as tall as the diagram", $"{block.ActualHeight:F1} vs {expected:F1}");
        }

        var broken = await WaitForAsync(box, static block => block.DiagramError is not null);
        context.Check(broken is { Diagram: null, TextLayout: not null }, "mermaid: a source that does not render shows as code", broken?.DiagramError);

        string folder = RepositoryPaths.Temp("m6-3");
        await EditorRenderProbes.WaitForDrawsAsync(box);
        await EditorRenderProbes.SaveAsync(box, Path.Combine(folder, "mermaid-block-light.png"));

        // Focused: the source, with the diagram below it.
        var id = ((CodeFence)box.State.Document.Children[1]).Code.Id;
        double unfocused = block?.ActualHeight ?? 0;
        box.Execute(new Quill.Editor.Commands.SetSelection(TextSelection.Caret(new TextPosition(id, 0))));
        await Task.Delay(80);
        block = FindAll(box).FirstOrDefault();
        context.Check(block is { IsFocused: true, ShowsDiagramOnly: false, Diagram: not null } && block.ActualHeight > unfocused + 20, "mermaid: a focused block shows its source above the diagram", $"{block?.ActualHeight:F0} vs {unfocused:F0}");
        await EditorRenderProbes.WaitForDrawsAsync(box);
        await EditorRenderProbes.SaveAsync(box, Path.Combine(folder, "mermaid-block-focused.png"));

        // An edit keeps the diagram until the new one arrives.
        int renders = MermaidDiagrams.Renders;
        box.Execute(new Quill.Editor.Commands.InsertText("%% note\n"));
        await Task.Delay(1);
        context.Check(FindAll(box).FirstOrDefault() is { Diagram: not null }, "mermaid: the diagram stays while an edit renders");
        await WaitForAsync(box, _ => MermaidDiagrams.Renders > renders);
        await Task.Delay(50);
        context.Check(MermaidDiagrams.Renders > renders && FindAll(box).FirstOrDefault() is { Diagram: not null }, "mermaid: an edit renders the diagram again");

        // The dark theme renders its own diagram.
        renders = MermaidDiagrams.Renders;
        box.Document!.StyleSheet = MdStyleSheet.Dark;
        await WaitForAsync(box, _ => MermaidDiagrams.Renders > renders);
        await Task.Delay(50);
        context.Check(MermaidDiagrams.Renders > renders && FindAll(box).FirstOrDefault() is { Diagram: not null }, "mermaid: switching to dark renders the diagram in the dark theme");
        await EditorRenderProbes.WaitForDrawsAsync(box);
        await EditorRenderProbes.SaveAsync(box, Path.Combine(folder, "mermaid-block-dark.png"));
        context.Info($"mermaid block pictures in {folder}");
        box.Document.StyleSheet = null;
        context.Stage.Children.Clear();
    }

    /// <summary>Waits up to two seconds for a realized Mermaid block meeting a condition.</summary>
    private static async Task<MdCodeBlock?> WaitForAsync(MarkdownBox box, Func<MdCodeBlock, bool> condition)
    {
        for (int i = 0; i < 100; i++)
        {
            if (FindAll(box).FirstOrDefault(condition) is { } found)
            {
                return found;
            }

            await Task.Delay(20);
        }

        return null;
    }

    private static void Load(MarkdownBox box, string markdown)
    {
        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(markdown, ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(document)) }, ids);
    }

    private static List<MdCodeBlock> FindAll(MarkdownBox box) =>
        box.Document!.Geometry.Realized.Where(view => !box.Document.Geometry.IsDetached(view)).OfType<MdCodeBlock>().ToList();
}
