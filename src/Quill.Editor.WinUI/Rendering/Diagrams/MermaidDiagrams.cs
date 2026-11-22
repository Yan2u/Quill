using System.Diagnostics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Svg;
using Quill.Editor.Documents;
using Quill.Editor.Services;

namespace Quill.Editor.WinUI.Rendering.Diagrams;

/// <summary>A rendered Mermaid diagram: the SVG document Direct2D draws, its size in DIPs.</summary>
internal sealed record MermaidDiagram(CanvasSvgDocument Document, float Width, float Height) : IDisposable
{
    public void Dispose() => Document.Dispose();
}

/// <summary>A Mermaid source that does not render, with the renderer's message.</summary>
internal sealed record MermaidFailure(string Message);

/// <summary>
/// Mermaid diagrams of code blocks (M6.3, Q87: mermaid-rs-renderer through quill_native.dll). Rendering
/// runs in the background through the document's <see cref="RenderCache"/>, kept by theme and source and
/// dropped when the requesting block changed meanwhile (05). The SVG loads into a Win2D document on the
/// shared device.
/// </summary>
internal static class MermaidDiagrams
{
    public const string LightKind = "mermaid";
    public const string DarkKind = "mermaid-dark";

    /// <summary>How long a block asking again waits before rendering.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<BlockId, string> Latest = new();
    private static int _renders;
    private static int _skipped;

    /// <summary>How many diagrams were rendered in all, for tests.</summary>
    public static int Renders => Volatile.Read(ref _renders);

    /// <summary>The time of the last render, for measurements.</summary>
    public static TimeSpan LastRenderTime { get; private set; }

    /// <summary>
    /// The result for a source shown by a block: a <see cref="MermaidDiagram"/>, a <see cref="MermaidFailure"/>,
    /// or null while it renders (the request is made then).
    /// </summary>
    public static object? Get(RenderCache cache, string source, bool dark, BlockId block)
    {
        string kind = KindOf(dark);
        if (cache.TryGet(kind, source, out var result))
        {
            return result;
        }

        // Debounced while typing: a block asking again (its source changed) waits a moment, and gives up
        // when it asked for yet another source meanwhile; the cache drops such stale results anyway.
        bool again = Latest.TryGetValue(block, out _);
        Latest[block] = source;
        cache.Request(block, kind, source, async _ =>
        {
            if (again)
            {
                await Task.Delay(Debounce).ConfigureAwait(false);
                if (!Latest.TryGetValue(block, out var latest) || latest != source)
                {
                    Interlocked.Increment(ref _skipped);
                    return null;
                }
            }

            return await Task.Run<object?>(() => Render(source, dark)).ConfigureAwait(false);
        });
        return null;
    }

    /// <summary>How many debounced requests gave up because a newer source came, for tests.</summary>
    public static int Skipped => Volatile.Read(ref _skipped);

    /// <summary>The render cache kind of diagrams in a theme.</summary>
    public static string KindOf(bool dark) => dark ? DarkKind : LightKind;

    /// <summary>Renders at once, on any thread.</summary>
    public static object Render(string source, bool dark)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            var (svg, width, height, error) = MermaidNative.Render(source, dark);
            if (svg is null || width <= 0 || height <= 0)
            {
                return new MermaidFailure(error ?? Localization.UiStrings.Get("editor.diagramEmpty"));
            }

            var document = CanvasSvgDocument.LoadFromXml(CanvasDevice.GetSharedDevice(), svg);
            return new MermaidDiagram(document, width, height);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return new MermaidFailure(exception.Message);
        }
        finally
        {
            Interlocked.Increment(ref _renders);
            LastRenderTime = Stopwatch.GetElapsedTime(start);
        }
    }
}
