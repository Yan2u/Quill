using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Editor.WinUI.Text;
using Quill.Playground.Samples;
using Windows.Foundation;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Images in paragraphs (10, user report 2026-11-03, after Typora): an image alone in its paragraph takes the
/// block with no blank above or below, scaled down to fill the column when wider and centered when narrower,
/// following the column's width; an image among text flows with it, at the left; the source of an active
/// image sits right above it; an online image (served from a local socket, no network) is downloaded and shown.
/// Opt-in "Remote image (network)" loads the URL of the report.
/// </summary>
internal static class ImageLayoutProbes
{
    private const string ReportUrl = "https://picx.zhimg.com/80/v2-9f54b4f4e9ab2a34448f234d36930a07_1440w.webp?source=1def8aca";

    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        string folder = RepositoryPaths.Temp("images");
        await SaveAsync(Path.Combine(folder, "wide.png"), 2000, 400);
        await SaveAsync(Path.Combine(folder, "narrow.png"), 120, 60);
        var box = new MarkdownBox
        {
            Width = 600,
            Height = 700,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Clipboard = new MemoryClipboard(),
            DocumentDirectory = folder,
        };
        context.Stage.Children.Add(box);
        await Task.Delay(50);

        await CheckWideAsync(context, box);
        await CheckNarrowAsync(context, box);
        await CheckInlineAsync(context, box);
        await CheckActiveAsync(context, box);
        await CheckRemoteAsync(context, box, await File.ReadAllBytesAsync(Path.Combine(folder, "narrow.png")));
        context.Stage.Children.Clear();
    }

    /// <summary>Opt-in: the online image of the report (needs the network).</summary>
    public static async Task RunNetworkAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var box = new MarkdownBox { Width = 600, Height = 700, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        context.Stage.Children.Add(box);
        Load(box, $"![zhimg]({ReportUrl})");
        var images = await WaitForImageAsync(box, 0, tries: 300);
        context.Check(images.Count > 0, "image: the online WebP image of the report is downloaded and shown", images.Count > 0 ? $"{images[^1].Width:F0} x {images[^1].Height:F0}" : "not shown");
        context.Stage.Children.Clear();
    }

    private static async Task CheckWideAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "![wide](wide.png)\n\nafter");
        var images = await WaitForImageAsync(box, 0);
        var paragraph = Paragraph(box, 0);
        double column = Column(paragraph);
        var image = images.LastOrDefault();
        double expected = column * 400 / 2000;
        context.Check(images.Count > 0 && Math.Abs(image.Width - column) < 1.5 && Math.Abs(image.Height - expected) < 1.5,
            "image: a wide image alone in its paragraph is scaled to fill the column", $"{image.Width:F1} x {image.Height:F1}, column {column:F1}");
        context.Check(paragraph is not null && Math.Abs(paragraph.ActualHeight - paragraph.TextPadding.Top - paragraph.TextPadding.Bottom - image.Height) < 2 && Math.Abs(image.Y - paragraph.TextPadding.Top) < 1.5,
            "image: no blank above or below an image alone in its paragraph", $"block {paragraph?.ActualHeight:F1}, image at {image.Y:F1} height {image.Height:F1}");

        box.Width = 400;
        await Task.Delay(150);
        box.Root.UpdateLayout();
        images = await WaitForImageAsync(box, 0);
        paragraph = Paragraph(box, 0);
        double narrower = Column(paragraph);
        image = images.LastOrDefault();
        context.Check(narrower < column && Math.Abs(image.Width - narrower) < 1.5 && Math.Abs(image.Height - narrower * 0.2) < 1.5,
            "image: the image follows the column when the window narrows", $"{image.Width:F1} x {image.Height:F1}, column {narrower:F1}");
        box.Width = 600;
        await Task.Delay(100);
    }

    private static async Task CheckNarrowAsync(SelfTestContext context, MarkdownBox box)
    {
        // Another paragraph after it keeps the caret out of the image: the image shows inactive.
        Load(box, "![narrow](narrow.png)\n\nafter");
        var images = await WaitForImageAsync(box, 0);
        var paragraph = Paragraph(box, 0);
        double column = Column(paragraph);
        var image = images.LastOrDefault();
        double center = paragraph is null ? 0 : paragraph.TextPadding.Left + column / 2;
        context.Check(images.Count > 0 && Math.Abs(image.Width - 120) < 0.5 && Math.Abs(image.X + image.Width / 2 - center) < 1.5,
            "image: a narrow image alone in its paragraph is centered at its own size", $"at {image.X:F1}, center {image.X + image.Width / 2:F1} vs {center:F1}");
        context.Check(paragraph is not null && Math.Abs(paragraph.ActualHeight - paragraph.TextPadding.Top - paragraph.TextPadding.Bottom - 60) < 2,
            "image: the block of a narrow image is as tall as the image", $"block {paragraph?.ActualHeight:F1}; {Lines(paragraph)}");
    }

    private static async Task CheckInlineAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "123 ![narrow](narrow.png) 123\n\nafter");
        var images = await WaitForImageAsync(box, 0);
        var paragraph = Paragraph(box, 0);
        var image = images.LastOrDefault();
        double left = paragraph?.TextPadding.Left ?? 0;
        context.Check(images.Count > 0 && image.X > left + 10 && image.X < left + 80 && paragraph is not null && paragraph.ActualHeight < 60 + 6,
            "image: an image among text flows with it at the left, the line as tall as the image", $"at {image.X:F1}, block {paragraph?.ActualHeight:F1}");
    }

    private static async Task CheckActiveAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "![narrow](narrow.png)");
        await WaitForImageAsync(box, 0);
        var leaf = box.State.Document.Children[0].Id;
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(leaf, 3))));
        await Task.Delay(120);
        var paragraph = Paragraph(box, 0);
        var layout = paragraph?.TextLayout;
        var images = (layout as BlockTextLayout)?.DrawnImages ?? [];
        var image = images.LastOrDefault();
        var (_, _, top, height) = layout is { LineCount: > 1 } ? layout.Line(layout.LineCount - 2) : default;
        double sourceBottom = (paragraph?.TextPadding.Top ?? 0) + top + height;
        context.Check(images.Count > 0 && layout is { LineCount: > 1 } && Math.Abs(image.Y - sourceBottom) < 1.5,
            "image: the source of an active image sits right above it", $"source ends at {sourceBottom:F1}, image at {image.Y:F1}");
    }

    private static async Task CheckRemoteAsync(SelfTestContext context, MarkdownBox box, byte[] png)
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        var serving = ServeAsync(server, png);
        Load(box, $"![remote](http://127.0.0.1:{port}/pictures/a.webp?source=x)");
        var images = await WaitForImageAsync(box, 0, tries: 150);
        context.Check(images.Count > 0 && Math.Abs(images[^1].Width - 120) < 0.5, "image: an online image is downloaded and shown", images.Count > 0 ? "shown" : "placeholder");
        server.Stop();
        await Task.WhenAny(serving, Task.Delay(500));
    }

    /// <summary>A minimal HTTP server: every request gets the image.</summary>
    private static async Task ServeAsync(TcpListener server, byte[] body)
    {
        try
        {
            while (true)
            {
                using var client = await server.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var buffer = new byte[4096];
                _ = await stream.ReadAsync(buffer);
                byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head);
                await stream.WriteAsync(body);
            }
        }
        catch (Exception error) when (error is SocketException or ObjectDisposedException or IOException or InvalidOperationException)
        {
            // Stopped.
        }
    }

    private static async Task<List<Rect>> WaitForImageAsync(MarkdownBox box, int index, int tries = 60)
    {
        for (int i = 0; i < tries; i++)
        {
            box.Root.UpdateLayout();
            if (Paragraph(box, index)?.TextLayout is BlockTextLayout { DrawnImages.Count: > 0 } layout)
            {
                await Task.Delay(40);
                return [.. ((BlockTextLayout)Paragraph(box, index)!.TextLayout!).DrawnImages];
            }

            await Task.Delay(40);
        }

        return [];
    }

    private static string Lines(MdTextBlockView? view)
    {
        if (view?.TextLayout is not { } layout)
        {
            return "no layout";
        }

        var lines = Enumerable.Range(0, layout.LineCount).Select(i => layout.Line(i)).Select(static line => $"[{line.Start}..{line.End} @{line.Top:F1} h{line.Height:F1}]");
        return $"text '{layout.Text.Replace("\n", "\\n")}' ({layout.Text.Length}), padding {view.TextPadding.Top:F1}/{view.TextPadding.Bottom:F1}, " + string.Join(" ", lines);
    }

    private static MdParagraph? Paragraph(MarkdownBox box, int index) =>
        box.Document?.Geometry.Realized.Where(view => !box.Document.Geometry.IsDetached(view)).OfType<MdParagraph>().ElementAtOrDefault(index);

    private static double Column(MdParagraph? paragraph) =>
        paragraph is null ? 0 : paragraph.ActualWidth - paragraph.TextPadding.Left - paragraph.TextPadding.Right;

    private static async Task SaveAsync(string file, int width, int height)
    {
        using var target = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), width, height, 96);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Colors.SteelBlue);
            session.FillEllipse(width / 2f, height / 2f, width / 4f, height / 4f, Colors.Orange);
        }

        await target.SaveAsync(file);
    }

    private static void Load(MarkdownBox box, string markdown)
    {
        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(markdown, ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(new BoundaryPosition(document.Id, document.Children.Count)) }, ids);
    }
}
