using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Input;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.Statistics;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Playground.Samples;
using Quill.Shell.Sidebar;
using Quill.VariableXaml;
using Windows.Foundation;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Opt-in memory leak diagnostic probes for repeated up/down scrolling and Win2D canvas teardown
/// (<c>-Only Scroll</c>). Inspects private bytes, managed heap, GDI/USER/kernel handles, visual tree
/// vs detached object retention via weak references, and isolated <see cref="CanvasVirtualControl"/> /
/// <see cref="CanvasRenderTarget"/> behavior across Debug and Release builds.
/// </summary>
internal static class ScrollMemoryProbes
{
    private const int ScrollStepsPerDirection = 16;
    private const int CanvasLifecycleIterations = 200;
    private const int PersistentCanvasCount = 10;
    private const int PersistentRedrawRounds = 150;
    private const uint SafetyGdiLimit = 7000;
    private const double SafetyPrivateMbLimit = 1800;

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(nint hProcess, uint uiFlags);

    public static async Task RunAsync(SelfTestContext context)
    {
        Log(context, $"configuration: {(Debugger.IsAttached ? "debugger attached, " : string.Empty)}{BuildConfiguration()}");
        Log(context, $"stage bounds: {context.Stage.XamlRoot.Size.Width:0}x{context.Stage.XamlRoot.Size.Height:0} DIP, scale={context.Stage.XamlRoot.RasterizationScale:0.##}");

        string? folder = BenchmarkDocuments.EnsureWritten();

        // Warm up once before measuring baselines so one-time initialization (KaTeX fonts,
        // TextMate grammars, D2D shared device) does not skew isolated measurements.
        await WarmUpEditorAsync(context);

        // Order from cleanest/isolated micro-benchmarks to heaviest document workloads so earlier
        // leaks do not contaminate baselines or exhaust process GDI handles prematurely.
        await RunCanvasRedrawExperimentAsync(context, "Exp F2a (win2d-redraw-direct)", useRenderTarget: false);
        await RunCanvasRedrawExperimentAsync(context, "Exp F2b (win2d-redraw-rendertarget)", useRenderTarget: true);

        await RunPlainGridBaselineAsync(context, "Exp F1e (plain-grid-baseline)");
        await RunCanvasLifecycleExperimentAsync(context, "Exp F1d (win2d-canvas-full-cleanup-before-remove)", CanvasCleanupMode.FullCleanupNullField, cleanupBeforeRemove: true);
        await RunCanvasLifecycleExperimentAsync(context, "Exp F1f (win2d-canvas-full-cleanup-after-remove)", CanvasCleanupMode.FullCleanupNullField, cleanupBeforeRemove: false);
        await RunCanvasLifecycleExperimentAsync(context, "Exp F1b (win2d-canvas-remove-keep-field)", CanvasCleanupMode.UnsubscribeAndRemoveFromVisualTree, cleanupBeforeRemove: false);
        await RunCanvasLifecycleExperimentAsync(context, "Exp F1c (win2d-canvas-unsub-only)", CanvasCleanupMode.UnsubscribeOnly, cleanupBeforeRemove: false);
        await RunCanvasLifecycleExperimentAsync(context, "Exp F1a (win2d-canvas-no-cleanup)", CanvasCleanupMode.None, cleanupBeforeRemove: false);

        string pureParagraphs = BuildPureParagraphsMarkdown(1000);
        await RunDocumentExperimentAsync(context, "Exp A (pure-paragraphs-1000)", pureParagraphs, path: null, cycles: 4, sourceMode: false);

        if (folder is not null)
        {
            await RunDocumentExperimentAsync(context, "Exp E (source-mode-long-2000)", markdown: null, path: Path.Combine(folder, "long-2000.md"), cycles: 4, sourceMode: true);
            await RunDocumentExperimentAsync(context, "Exp D (math.md)", markdown: null, path: Path.Combine(folder, "math.md"), cycles: 3, sourceMode: false);
            await RunDocumentExperimentAsync(context, "Exp B (long-2000.md)", markdown: null, path: Path.Combine(folder, "long-2000.md"), cycles: 4, sourceMode: false);
            await RunDocumentExperimentAsync(context, "Exp C (tables.md)", markdown: null, path: Path.Combine(folder, "tables.md"), cycles: 3, sourceMode: false);
        }
        else
        {
            Log(context, "benchmark folder missing; Exp B..E skipped");
        }
    }

    private static async Task WarmUpEditorAsync(SelfTestContext context)
    {
        await RunBoxSessionAsync(context, "warmup", "# Warmup\n\nParagraph for JIT and Win2D warmup.\n", path: null, cycles: 1, sourceMode: false, logCycles: false);
        context.Stage.Children.Clear();
        await FramesAsync(2);
        ForceFullGc();
        CanvasDevice.GetSharedDevice().Trim();
        ForceFullGc();
    }

    private static async Task RunDocumentExperimentAsync(
        SelfTestContext context,
        string label,
        string? markdown,
        string? path,
        int cycles,
        bool sourceMode)
    {
        MemorySample baseline = CaptureMemory(forceGc: true);
        Log(context, $"{label} baseline: {baseline}");

        BoxSessionResult result = await RunBoxSessionAsync(context, label, markdown, path, cycles, sourceMode, logCycles: true);

        // Box was removed from Stage and its async state machine has returned here,
        // so no Debug-build local inside RunBoxSessionAsync still roots the editor.
        await FramesAsync(2);
        CanvasDevice.GetSharedDevice().Trim();
        MemorySample postUnload = CaptureMemory(forceGc: true);
        CanvasDevice.GetSharedDevice().Trim();
        postUnload = CaptureMemory(forceGc: true);
        TrackedSummary postUnloadTracked = result.Tracker.Summarize(liveInTree: null);
        bool boxAlive = result.BoxRef.TryGetTarget(out _);
        Log(context, $"{label} post-unload-after-gc-trim: {postUnload} | leaked {postUnloadTracked} | boxAlive={boxAlive}");

        long gdiDelta = (long)postUnload.GdiObjects - baseline.GdiObjects;
        context.Check(
            result.ScrollableHeight > 0
                && result.TotalDraws > 0
                && result.DrawErrorCount == 0
                && !result.AbortedEarly
                && !boxAlive
                && result.EndDetachedAlive == 0
                && postUnloadTracked.Top.Alive == 0
                && postUnloadTracked.Nested.Alive == 0
                && postUnloadTracked.Canvas.Alive == 0,
            $"scroll memory: {label} completed cleanly",
            $"cycles={cycles}, abortedEarly={result.AbortedEarly}, scrollable={result.ScrollableHeight:0}, draws={result.TotalDraws}, drawErrors={result.DrawErrorCount}, boxAlive={boxAlive}, endDetached={result.EndDetachedAlive}, postTop={postUnloadTracked.Top.Alive}, postNested={postUnloadTracked.Nested.Alive}, postCanvas={postUnloadTracked.Canvas.Alive}, postUnloadPrivDelta={postUnload.PrivateMB - baseline.PrivateMB:+0.0;-0.0;0.0}MB, postUnloadGdiDelta={gdiDelta:+0;-0;0}");
    }

    private static async Task<BoxSessionResult> RunBoxSessionAsync(
        SelfTestContext context,
        string label,
        string? markdown,
        string? path,
        int cycles,
        bool sourceMode,
        bool logCycles)
    {
        context.Stage.Children.Clear();
        var stageSize = context.Stage.XamlRoot.Size;
        var box = new MarkdownBox
        {
            Width = Math.Min(660, stageSize.Width),
            Height = Math.Min(480, stageSize.Height - 20),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Clipboard = new MemoryClipboard(),
        };
        var boxRef = new WeakReference<object>(box);
        context.Stage.Children.Add(box);
        await FramesAsync(2);

        var ids = new SequentialBlockIdSource();
        Document document = path is not null
            ? (await MarkdownFile.OpenAsync(path, ids)).Document
            : MarkdownImporter.Import(markdown!, ids).Document;

        var tracker = new ObjectTracker();
        box.Load(EditorState.AtStart(document), ids);
        box.UpdateLayout();
        await FramesAsync(3);

        if (sourceMode)
        {
            box.ToggleSourceMode();
            var watch = Stopwatch.StartNew();
            while (box.Highlighting is { IsComplete: false } && watch.Elapsed < TimeSpan.FromSeconds(15))
            {
                await Task.Delay(50);
            }

            box.UpdateLayout();
            await FramesAsync(2);
        }

        MdDocument mdDoc = box.Document ?? throw new InvalidOperationException($"{label}: Document did not project.");
        ScrollViewer scroller = mdDoc.ScrollViewer;
        var root = (VariableXamlRoot)box.Content;

        // Ensure scrollable layout has materialized.
        for (int attempt = 0; attempt < 4 && scroller.ScrollableHeight <= 0; attempt++)
        {
            box.UpdateLayout();
            await FramesAsync(2);
        }

        double scrollableHeight = scroller.ScrollableHeight;
        LogAfterLoad(context, label, tracker, box, mdDoc.Body, root, scrollableHeight, logCycles);

        bool abortedEarly = false;
        for (int cycle = 1; cycle <= cycles && !abortedEarly; cycle++)
        {
            for (int step = 1; step <= ScrollStepsPerDirection; step++)
            {
                double offset = scrollableHeight * step / ScrollStepsPerDirection;
                scroller.ChangeView(null, offset, null, disableAnimation: true);
                box.UpdateLayout();
                await FramesAsync(2);
                TrackTree(tracker, box, mdDoc.Body);

                MemorySample stepMem = CaptureMemory(forceGc: false);
                if (stepMem.GdiObjects >= SafetyGdiLimit || stepMem.PrivateMB >= SafetyPrivateMbLimit)
                {
                    if (logCycles)
                    {
                        Log(context, $"{label} safety-stop down at cycle {cycle}/{cycles} step {step}: {stepMem}");
                    }

                    abortedEarly = true;
                    break;
                }
            }

            for (int step = ScrollStepsPerDirection - 1; step >= 0 && !abortedEarly; step--)
            {
                double offset = scrollableHeight * step / ScrollStepsPerDirection;
                scroller.ChangeView(null, offset, null, disableAnimation: true);
                box.UpdateLayout();
                await FramesAsync(2);
                TrackTree(tracker, box, mdDoc.Body);

                MemorySample stepMem = CaptureMemory(forceGc: false);
                if (stepMem.GdiObjects >= SafetyGdiLimit || stepMem.PrivateMB >= SafetyPrivateMbLimit)
                {
                    if (logCycles)
                    {
                        Log(context, $"{label} safety-stop up at cycle {cycle}/{cycles} step {step}: {stepMem}");
                    }

                    abortedEarly = true;
                    break;
                }
            }

            if (logCycles && !abortedEarly)
            {
                LogCycle(context, label, cycle, cycles, tracker, box, mdDoc.Body, root);
            }
        }

        (long totalDraws, int drawErrorCount) = CollectDrawStats(tracker);

        int endDetachedAlive = 0;
        if (logCycles)
        {
            (MemorySample endBeforeGc, TrackedSummary endBeforeTracked) = MeasureEndState(tracker, box, mdDoc.Body, forceGc: false, trim: false);
            Log(context, $"{label} end-before-gc: {endBeforeGc} | {endBeforeTracked} | draws={totalDraws}");

            (MemorySample endAfterGc, TrackedSummary endAfterTracked) = MeasureEndState(tracker, box, mdDoc.Body, forceGc: true, trim: false);
            Log(context, $"{label} end-after-gc: {endAfterGc} | {endAfterTracked}");

            (MemorySample endAfterTrim, TrackedSummary endAfterTrimTracked) = MeasureEndState(tracker, box, mdDoc.Body, forceGc: true, trim: true);
            endDetachedAlive = endAfterTrimTracked.Top.DetachedAlive
                + endAfterTrimTracked.Nested.DetachedAlive
                + endAfterTrimTracked.Canvas.DetachedAlive;
            Log(context, $"{label} end-after-trim: {endAfterTrim} | {endAfterTrimTracked}");
        }

        context.Stage.Children.Clear();
        return new BoxSessionResult(tracker, boxRef, scrollableHeight, totalDraws, drawErrorCount, abortedEarly, endDetachedAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TrackTree(ObjectTracker tracker, DependencyObject root, UIElement? body) =>
        _ = tracker.ScanTree(root, body);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LogAfterLoad(
        SelfTestContext context,
        string label,
        ObjectTracker tracker,
        DependencyObject root,
        UIElement? body,
        VariableXamlRoot xamlRoot,
        double scrollableHeight,
        bool logCycles)
    {
        TreeScanResult scan = tracker.ScanTree(root, body);
        if (!logCycles)
        {
            return;
        }

        MemorySample mem = CaptureMemory(forceGc: false);
        VirtualizationSummary vsum = xamlRoot.GetVirtualizationSummary();
        TrackedSummary tracked = tracker.Summarize(scan.LiveObjects);
        Log(context, $"{label} after-load: {mem} | scrollable={scrollableHeight:0} | {FormatVirt(vsum, scan)} | {tracked}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LogCycle(
        SelfTestContext context,
        string label,
        int cycle,
        int cycles,
        ObjectTracker tracker,
        DependencyObject root,
        UIElement? body,
        VariableXamlRoot xamlRoot)
    {
        TreeScanResult scan = tracker.ScanTree(root, body);
        MemorySample mem = CaptureMemory(forceGc: false);
        VirtualizationSummary vsum = xamlRoot.GetVirtualizationSummary();
        TrackedSummary tracked = tracker.Summarize(scan.LiveObjects);
        Log(context, $"{label} cycle {cycle}/{cycles}: {mem} | {FormatVirt(vsum, scan)} | {tracked}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (long TotalDraws, int DrawErrorCount) CollectDrawStats(ObjectTracker tracker)
    {
        long totalDraws = 0;
        int drawErrorCount = 0;
        foreach (MdBlockView view in tracker.LiveBlocksSnapshot())
        {
            totalDraws += view.DrawCount;
            if (view.LastDrawError is not null)
            {
                drawErrorCount++;
            }
        }

        return (totalDraws, drawErrorCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (MemorySample Mem, TrackedSummary Tracked) MeasureEndState(
        ObjectTracker tracker,
        DependencyObject root,
        UIElement? body,
        bool forceGc,
        bool trim)
    {
        if (trim)
        {
            CanvasDevice.GetSharedDevice().Trim();
        }

        MemorySample mem = CaptureMemory(forceGc);
        TreeScanResult scan = tracker.ScanTree(root, body);
        TrackedSummary tracked = tracker.Summarize(scan.LiveObjects);
        return (mem, tracked);
    }

    private static async Task RunPlainGridBaselineAsync(SelfTestContext context, string label)
    {
        context.Stage.Children.Clear();
        double maxWidth = Math.Min(600, context.Stage.XamlRoot.Size.Width);
        var container = new Grid
        {
            Width = maxWidth,
            Height = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        context.Stage.Children.Add(container);
        await FramesAsync(2);

        ForceFullGc();
        CanvasDevice.GetSharedDevice().Trim();
        MemorySample baseline = CaptureMemory(forceGc: true);
        Log(context, $"{label} baseline: {baseline}");

        var hostRefs = new List<WeakReference<object>>(CanvasLifecycleIterations);
        var textRefs = new List<WeakReference<object>>(CanvasLifecycleIterations);

        for (int i = 1; i <= CanvasLifecycleIterations; i++)
        {
            await AttachDrawAndRemovePlainGridAsync(container, maxWidth, hostRefs, textRefs);
            if (i == CanvasLifecycleIterations / 2 || i == CanvasLifecycleIterations)
            {
                MemorySample sample = CaptureMemory(forceGc: false);
                Log(context, $"{label} iter {i}/{CanvasLifecycleIterations}: {sample} | alive grids={CountAlive(hostRefs)}/{i}, texts={CountAlive(textRefs)}/{i}");
            }
        }

        context.Stage.Children.Clear();
        await FramesAsync(2);
        MemorySample afterGc = CaptureMemory(forceGc: true);
        Log(context, $"{label} after-gc: {afterGc} | alive grids={CountAlive(hostRefs)}/{CanvasLifecycleIterations}, texts={CountAlive(textRefs)}/{CanvasLifecycleIterations}");

        CanvasDevice.GetSharedDevice().Trim();
        MemorySample afterTrim = CaptureMemory(forceGc: true);
        int finalGrids = CountAlive(hostRefs);
        int finalTexts = CountAlive(textRefs);
        Log(context, $"{label} after-trim: {afterTrim} | alive grids={finalGrids}/{CanvasLifecycleIterations}, texts={finalTexts}/{CanvasLifecycleIterations}");

        long gdiDelta = (long)afterTrim.GdiObjects - baseline.GdiObjects;
        context.Check(
            hostRefs.Count == CanvasLifecycleIterations && textRefs.Count == CanvasLifecycleIterations,
            $"scroll memory: {label} completed",
            $"gridsAlive={finalGrids}/{CanvasLifecycleIterations}, textsAlive={finalTexts}/{CanvasLifecycleIterations}, privDelta={afterTrim.PrivateMB - baseline.PrivateMB:+0.0;-0.0;0.0}MB, gdiDelta={gdiDelta:+0;-0;0}");
    }

    private static async Task AttachDrawAndRemovePlainGridAsync(
        Grid container,
        double width,
        List<WeakReference<object>> hostRefs,
        List<WeakReference<object>> textRefs)
    {
        var grid = new Grid { Width = width, Height = 80 };
        var text = new TextBlock { Text = "Baseline" };
        grid.Children.Add(text);
        hostRefs.Add(new WeakReference<object>(grid));
        textRefs.Add(new WeakReference<object>(text));

        container.Children.Add(grid);
        container.UpdateLayout();
        await FramesAsync(1);
        container.Children.Remove(grid);
        container.UpdateLayout();
    }

    private static async Task RunCanvasLifecycleExperimentAsync(
        SelfTestContext context,
        string label,
        CanvasCleanupMode cleanupMode,
        bool cleanupBeforeRemove)
    {
        context.Stage.Children.Clear();
        double maxWidth = Math.Min(600, context.Stage.XamlRoot.Size.Width);
        var container = new Grid
        {
            Width = maxWidth,
            Height = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        context.Stage.Children.Add(container);
        await FramesAsync(2);

        ForceFullGc();
        CanvasDevice.GetSharedDevice().Trim();
        MemorySample baseline = CaptureMemory(forceGc: true);
        Log(context, $"{label} baseline: {baseline}");

        var hostRefs = new List<WeakReference<object>>(CanvasLifecycleIterations);
        var canvasRefs = new List<WeakReference<object>>(CanvasLifecycleIterations);
        bool abortedEarly = false;

        for (int i = 1; i <= CanvasLifecycleIterations; i++)
        {
            await AttachDrawAndRemoveCanvasHostAsync(container, maxWidth, cleanupMode, cleanupBeforeRemove, hostRefs, canvasRefs);
            MemorySample sample = CaptureMemory(forceGc: false);
            if (sample.GdiObjects >= SafetyGdiLimit || sample.PrivateMB >= SafetyPrivateMbLimit)
            {
                Log(context, $"{label} safety-stop at iter {i}/{CanvasLifecycleIterations}: {sample}");
                abortedEarly = true;
                break;
            }

            if (i == CanvasLifecycleIterations / 2 || i == CanvasLifecycleIterations)
            {
                int hostAlive = CountAlive(hostRefs);
                int canvasAlive = CountAlive(canvasRefs);
                Log(context, $"{label} iter {i}/{CanvasLifecycleIterations}: {sample} | alive hosts={hostAlive}/{i}, canvases={canvasAlive}/{i}");
            }
        }

        int totalCreated = hostRefs.Count;
        context.Stage.Children.Clear();
        await FramesAsync(2);
        MemorySample afterGc = CaptureMemory(forceGc: true);
        Log(context, $"{label} after-gc: {afterGc} | alive hosts={CountAlive(hostRefs)}/{totalCreated}, canvases={CountAlive(canvasRefs)}/{totalCreated}");

        CanvasDevice.GetSharedDevice().Trim();
        MemorySample afterTrim = CaptureMemory(forceGc: true);
        int finalHosts = CountAlive(hostRefs);
        int finalCanvases = CountAlive(canvasRefs);
        Log(context, $"{label} after-trim: {afterTrim} | alive hosts={finalHosts}/{totalCreated}, canvases={finalCanvases}/{totalCreated}");

        long gdiDelta = (long)afterTrim.GdiObjects - baseline.GdiObjects;
        context.Check(
            totalCreated > 0 && canvasRefs.Count == totalCreated,
            $"scroll memory: {label} completed",
            $"abortedEarly={abortedEarly}, hostsAlive={finalHosts}/{totalCreated}, canvasesAlive={finalCanvases}/{totalCreated}, privDelta={afterTrim.PrivateMB - baseline.PrivateMB:+0.0;-0.0;0.0}MB, gdiDelta={gdiDelta:+0;-0;0}");
    }

    private static async Task AttachDrawAndRemoveCanvasHostAsync(
        Grid container,
        double width,
        CanvasCleanupMode cleanupMode,
        bool cleanupBeforeRemove,
        List<WeakReference<object>> hostRefs,
        List<WeakReference<object>> canvasRefs)
    {
        var probe = new CanvasHostProbe(width, 80);
        hostRefs.Add(new WeakReference<object>(probe));
        canvasRefs.Add(new WeakReference<object>(probe.Canvas!));

        container.Children.Add(probe);
        container.UpdateLayout();
        await FramesAsync(1);
        if (cleanupBeforeRemove)
        {
            probe.Cleanup(cleanupMode);
        }

        container.Children.Remove(probe);
        container.UpdateLayout();
        if (!cleanupBeforeRemove)
        {
            probe.Cleanup(cleanupMode);
        }
    }

    private static async Task RunCanvasRedrawExperimentAsync(SelfTestContext context, string label, bool useRenderTarget)
    {
        context.Stage.Children.Clear();
        double width = Math.Min(600, context.Stage.XamlRoot.Size.Width);
        var stack = new StackPanel
        {
            Width = width,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var controls = new List<CanvasVirtualControl>(PersistentCanvasCount);
        var handlers = new List<TypedEventHandler<CanvasVirtualControl, CanvasRegionsInvalidatedEventArgs>>(PersistentCanvasCount);
        long drawCalls = 0;

        for (int i = 0; i < PersistentCanvasCount; i++)
        {
            var canvas = new CanvasVirtualControl { Width = width, Height = 40 };
            TypedEventHandler<CanvasVirtualControl, CanvasRegionsInvalidatedEventArgs> handler = (sender, args) =>
            {
                float dpi = sender.Dpi;
                float scale = dpi / 96f;
                int maxPixels = (int)sender.Device.MaximumBitmapSizeInPixels;
                foreach (Rect region in args.InvalidatedRegions)
                {
                    if (region.Width <= 0 || region.Height <= 0)
                    {
                        continue;
                    }

                    drawCalls++;
                    using CanvasDrawingSession session = sender.CreateDrawingSession(region);
                    if (useRenderTarget)
                    {
                        int pxLeft = (int)Math.Floor(region.X * scale);
                        int pxTop = (int)Math.Floor(region.Y * scale);
                        int pxRight = (int)Math.Ceiling((region.X + region.Width) * scale);
                        int pxBottom = (int)Math.Ceiling((region.Y + region.Height) * scale);
                        int pxWidth = Math.Max(1, pxRight - pxLeft);
                        int pxHeight = Math.Max(1, pxBottom - pxTop);
                        if (pxWidth <= maxPixels && pxHeight <= maxPixels)
                        {
                            float dipLeft = pxLeft / scale;
                            float dipTop = pxTop / scale;
                            using var target = new CanvasRenderTarget(sender, pxWidth / scale, pxHeight / scale, dpi);
                            using (CanvasDrawingSession ds = target.CreateDrawingSession())
                            {
                                if (pxLeft != 0 || pxTop != 0)
                                {
                                    ds.Transform = System.Numerics.Matrix3x2.CreateTranslation(-dipLeft, -dipTop);
                                }

                                DrawSampleBands(ds, region);
                            }

                            var destination = new Rect(dipLeft, dipTop, target.Size.Width, target.Size.Height);
                            session.Antialiasing = CanvasAntialiasing.Aliased;
                            session.Blend = CanvasBlend.Copy;
                            session.DrawImage(target, destination, target.Bounds, 1f, CanvasImageInterpolation.NearestNeighbor);
                            continue;
                        }
                    }

                    DrawSampleBands(session, region);
                }
            };
            canvas.RegionsInvalidated += handler;
            controls.Add(canvas);
            handlers.Add(handler);
            stack.Children.Add(canvas);
        }

        context.Stage.Children.Add(stack);
        stack.UpdateLayout();
        await FramesAsync(2);

        ForceFullGc();
        CanvasDevice.GetSharedDevice().Trim();
        MemorySample baseline = CaptureMemory(forceGc: true);
        Log(context, $"{label} baseline: {baseline} | initialDraws={drawCalls}");

        for (int round = 1; round <= PersistentRedrawRounds; round++)
        {
            foreach (CanvasVirtualControl canvas in controls)
            {
                canvas.Invalidate();
            }

            await FramesAsync(1);
            if (round == PersistentRedrawRounds / 3 || round == (PersistentRedrawRounds * 2) / 3 || round == PersistentRedrawRounds)
            {
                MemorySample sample = CaptureMemory(forceGc: false);
                Log(context, $"{label} round {round}/{PersistentRedrawRounds}: {sample} | draws={drawCalls}");
            }
        }

        MemorySample afterGc = CaptureMemory(forceGc: true);
        Log(context, $"{label} after-gc: {afterGc}");
        CanvasDevice.GetSharedDevice().Trim();
        MemorySample afterTrim = CaptureMemory(forceGc: true);
        Log(context, $"{label} after-trim: {afterTrim}");

        for (int i = 0; i < controls.Count; i++)
        {
            controls[i].RegionsInvalidated -= handlers[i];
            controls[i].RemoveFromVisualTree();
        }

        context.Stage.Children.Clear();
        long gdiDelta = (long)afterTrim.GdiObjects - baseline.GdiObjects;
        context.Check(
            drawCalls >= PersistentCanvasCount * PersistentRedrawRounds,
            $"scroll memory: {label} completed",
            $"draws={drawCalls}, privDeltaAfterTrim={afterTrim.PrivateMB - baseline.PrivateMB:+0.0;-0.0;0.0}MB, gdiDelta={gdiDelta:+0;-0;0}");
    }

    private static void DrawSampleBands(CanvasDrawingSession session, Rect region)
    {
        session.Clear(Colors.White);
        session.FillRectangle(new Rect(4, 4, Math.Max(1, region.Width - 8), Math.Max(1, region.Height - 8)), Colors.LightSteelBlue);
    }

    private static string BuildPureParagraphsMarkdown(int count)
    {
        var lines = new List<string>(count * 2);
        for (int i = 1; i <= count; i++)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"Paragraph {i}: Repeated scrolling stability probe checking text rendering and virtualization. " +
                $"Second sentence with ordinary prose words to wrap onto multiple lines inside 660 DIP viewport width."));
        }

        return string.Join("\n\n", lines);
    }

    private static void Log(SelfTestContext context, string message)
    {
        context.Info(message);
        if (context.TracePath is { Length: > 0 } tracePath)
        {
            if (Path.GetDirectoryName(tracePath) is { } directory)
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(tracePath, message + Environment.NewLine);
        }
    }

    private static MemorySample CaptureMemory(bool forceGc)
    {
        if (forceGc)
        {
            ForceFullGc();
        }

        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        return new MemorySample(
            process.PrivateMemorySize64 / (1024.0 * 1024.0),
            process.WorkingSet64 / (1024.0 * 1024.0),
            GC.GetTotalMemory(false) / (1024.0 * 1024.0),
            GetGuiResources(process.Handle, 0),
            GetGuiResources(process.Handle, 1),
            process.HandleCount,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }

    private static void ForceFullGc()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    private static int CountAlive(List<WeakReference<object>> refs)
    {
        int alive = 0;
        foreach (WeakReference<object> weak in refs)
        {
            if (weak.TryGetTarget(out _))
            {
                alive++;
            }
        }

        return alive;
    }

    private static string FormatVirt(VirtualizationSummary vsum, TreeScanResult scan) =>
        $"items={vsum.Items}, realized={vsum.Realized}, created={vsum.CreatedTotal}, reused={vsum.ReusedTotal}, repeaterKids={scan.RepeaterChildren}, tree(top={scan.TreeTop},nested={scan.TreeNested},canvas={scan.TreeCanvas})";

    private static Task FramesAsync(int count)
    {
        var done = new TaskCompletionSource();
        int seen = 0;
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            if (++seen >= count)
            {
                CompositionTarget.Rendering -= handler;
                done.TrySetResult();
            }
        };
        CompositionTarget.Rendering += handler;
        return done.Task;
    }

    private static string BuildConfiguration()
    {
#if DEBUG
        return "Debug build";
#else
        return "Release build";
#endif
    }

    private enum CanvasCleanupMode
    {
        None,
        UnsubscribeOnly,
        UnsubscribeAndRemoveFromVisualTree,
        FullCleanupNullField,
    }

    /// <summary>
    /// Mirrors <see cref="MdBlockView"/>'s Win2D ownership pattern: the host holds a
    /// <see cref="CanvasVirtualControl"/> whose <c>RegionsInvalidated</c> handler points back to <c>this</c>.
    /// </summary>
    private sealed class CanvasHostProbe : Grid
    {
        private CanvasVirtualControl? _canvas;

        public CanvasHostProbe(double width, double height)
        {
            Width = width;
            Height = height;
            _canvas = new CanvasVirtualControl { Width = width, Height = height };
            _canvas.RegionsInvalidated += OnRegionsInvalidated;
            Children.Add(_canvas);
        }

        public CanvasVirtualControl? Canvas => _canvas;

        public void Cleanup(CanvasCleanupMode mode)
        {
            if (_canvas is null || mode == CanvasCleanupMode.None)
            {
                return;
            }

            _canvas.RegionsInvalidated -= OnRegionsInvalidated;
            if (mode == CanvasCleanupMode.UnsubscribeAndRemoveFromVisualTree)
            {
                _canvas.RemoveFromVisualTree();
            }
            else if (mode == CanvasCleanupMode.FullCleanupNullField)
            {
                _canvas.RemoveFromVisualTree();
                Children.Clear();
                _canvas = null;
            }
        }

        private void OnRegionsInvalidated(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
        {
            foreach (Rect region in args.InvalidatedRegions)
            {
                if (region.Width <= 0 || region.Height <= 0)
                {
                    continue;
                }

                using CanvasDrawingSession ds = sender.CreateDrawingSession(region);
                DrawSampleBands(ds, region);
            }
        }
    }

    private sealed class Sentinel
    {
        public static readonly Sentinel Instance = new();
    }

    private sealed class WeakBucket
    {
        private readonly ConditionalWeakTable<object, Sentinel> _seen = new();
        private readonly List<WeakReference<object>> _refs = [];

        public void Track(object instance)
        {
            if (!_seen.TryGetValue(instance, out _))
            {
                _seen.Add(instance, Sentinel.Instance);
                _refs.Add(new WeakReference<object>(instance));
            }
        }

        public List<T> LiveInstances<T>() where T : class
        {
            var result = new List<T>();
            foreach (WeakReference<object> weak in _refs)
            {
                if (weak.TryGetTarget(out object? target) && target is T typed)
                {
                    result.Add(typed);
                }
            }

            return result;
        }

        public BucketSummary Summarize(HashSet<object>? liveInTree)
        {
            int alive = 0;
            int detachedAlive = 0;
            foreach (WeakReference<object> weak in _refs)
            {
                if (weak.TryGetTarget(out object? target))
                {
                    alive++;
                    if (liveInTree is not null && !liveInTree.Contains(target))
                    {
                        detachedAlive++;
                    }
                }
            }

            return new BucketSummary(_refs.Count, alive, detachedAlive);
        }
    }

    private sealed class ObjectTracker
    {
        public WeakBucket TopBlocks { get; } = new();

        public WeakBucket NestedBlocks { get; } = new();

        public WeakBucket Canvases { get; } = new();

        public TreeScanResult ScanTree(DependencyObject root, UIElement? body)
        {
            var liveObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
            int treeTop = 0;
            int treeNested = 0;
            int treeCanvas = 0;

            void Walk(DependencyObject current, bool insideBlock)
            {
                bool nextInsideBlock = insideBlock;
                if (current is MdBlockView block)
                {
                    liveObjects.Add(block);
                    if (insideBlock)
                    {
                        treeNested++;
                        NestedBlocks.Track(block);
                    }
                    else
                    {
                        treeTop++;
                        TopBlocks.Track(block);
                    }

                    nextInsideBlock = true;
                }
                else if (current is CanvasVirtualControl canvas)
                {
                    liveObjects.Add(canvas);
                    treeCanvas++;
                    Canvases.Track(canvas);
                }

                int childCount = VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < childCount; i++)
                {
                    Walk(VisualTreeHelper.GetChild(current, i), nextInsideBlock);
                }
            }

            Walk(root, insideBlock: false);
            int repeaterChildren = body is null ? 0 : VisualTreeHelper.GetChildrenCount(body);
            return new TreeScanResult(treeTop, treeNested, treeCanvas, repeaterChildren, liveObjects);
        }

        public TrackedSummary Summarize(HashSet<object>? liveInTree) =>
            new(TopBlocks.Summarize(liveInTree), NestedBlocks.Summarize(liveInTree), Canvases.Summarize(liveInTree));

        public List<MdBlockView> LiveBlocksSnapshot()
        {
            var blocks = TopBlocks.LiveInstances<MdBlockView>();
            blocks.AddRange(NestedBlocks.LiveInstances<MdBlockView>());
            return blocks;
        }
    }

    private readonly record struct MemorySample(
        double PrivateMB,
        double WorkingSetMB,
        double ManagedMB,
        uint GdiObjects,
        uint UserObjects,
        int HandleCount,
        int Gen0,
        int Gen1,
        int Gen2)
    {
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture,
                $"priv={PrivateMB:0.0}MB, ws={WorkingSetMB:0.0}MB, managed={ManagedMB:0.0}MB, gdi={GdiObjects}, user={UserObjects}, handles={HandleCount}, gc={Gen0}/{Gen1}/{Gen2}");
    }

    private readonly record struct BucketSummary(int Seen, int Alive, int DetachedAlive)
    {
        public override string ToString() => $"{Seen}/{Alive}/{DetachedAlive}";
    }

    private readonly record struct TrackedSummary(BucketSummary Top, BucketSummary Nested, BucketSummary Canvas)
    {
        public override string ToString() =>
            $"weak(seen/alive/detached: top={Top}, nested={Nested}, canvas={Canvas})";
    }

    private readonly record struct TreeScanResult(int TreeTop, int TreeNested, int TreeCanvas, int RepeaterChildren, HashSet<object>? LiveObjects);

    private sealed record BoxSessionResult(
        ObjectTracker Tracker,
        WeakReference<object> BoxRef,
        double ScrollableHeight,
        long TotalDraws,
        int DrawErrorCount,
        bool AbortedEarly,
        int EndDetachedAlive);

    public static async Task RunCrashReproAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        double maxWidth = Math.Min(900, context.Stage.XamlRoot.Size.Width);
        double maxHeight = Math.Min(650, context.Stage.XamlRoot.Size.Height);
        string? reproPath = Environment.GetEnvironmentVariable("QUILL_SCROLL_REPRO_PATH");
        bool usingRealFile = !string.IsNullOrWhiteSpace(reproPath) && File.Exists(reproPath);
        Log(context, $"scroll crash repro: source={(usingRealFile ? reproPath : "<synthetic mixed document>")}, viewport={maxWidth:0}x{maxHeight:0}");

        var outline = new OutlinePanel();
        var box = new MarkdownBox();
        var host = new Grid
        {
            Width = maxWidth,
            Height = maxHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(outline, 0);
        Grid.SetColumn(box, 1);
        host.Children.Add(outline);
        host.Children.Add(box);
        context.Stage.Children.Add(host);
        await FramesAsync(2);

        var ids = new SequentialBlockIdSource();
        Document document = usingRealFile
            ? (await MarkdownFile.OpenAsync(reproPath!, ids)).Document
            : MarkdownImporter.Import(BuildMixedCrashReproMarkdown(), ids).Document;

        var tracker = new ObjectTracker();
        box.Load(EditorState.AtStart(document), ids);
        box.DocumentDirectory = usingRealFile ? Path.GetDirectoryName(reproPath!) : null;
        outline.Update(DocumentOutline.Of(box.State.Document));
        box.ViewportChanged += (_, _) =>
        {
            outline.SetCurrent(box.FirstVisibleBlock() is { } visible
                ? DocumentOutline.CurrentHeading(box.State.Document, outline.Entries, visible)
                : null);
        };
        box.Execute(new SetInteraction(box.State.Interaction with { HasFocus = true }));
        box.UpdateLayout();
        await FramesAsync(3);

        MdDocument mdDoc = box.Document ?? throw new InvalidOperationException("scroll crash repro: Document did not project.");
        ScrollViewer scroller = mdDoc.ScrollViewer;
        for (int attempt = 0; attempt < 4 && scroller.ScrollableHeight <= 0; attempt++)
        {
            box.UpdateLayout();
            await FramesAsync(2);
        }

        double scrollableHeight = scroller.ScrollableHeight;
        Log(context, $"scroll crash repro: loaded blocks={box.State.Document.Children.Count}, scrollableHeight={scrollableHeight:0}, headings={outline.Entries.Count}");
        TrackTree(tracker, box, mdDoc.Body);

        // Cycles 1-5: 48 fine scrollbar-like drag steps down then up; alternate UpdateLayout to hit both sync & async layout.
        for (int cycle = 1; cycle <= 5; cycle++)
        {
            bool syncLayout = cycle % 2 == 1;
            for (int step = 1; step <= 48; step++)
            {
                double offset = scroller.ScrollableHeight * step / 48.0;
                scroller.ChangeView(null, offset, null, disableAnimation: true);
                if (syncLayout)
                {
                    box.UpdateLayout();
                }

                await FramesAsync(1);
                TrackTree(tracker, box, mdDoc.Body);
            }

            for (int step = 47; step >= 0; step--)
            {
                double offset = scroller.ScrollableHeight * step / 48.0;
                scroller.ChangeView(null, offset, null, disableAnimation: true);
                if (syncLayout)
                {
                    box.UpdateLayout();
                }

                await FramesAsync(1);
                TrackTree(tracker, box, mdDoc.Body);
            }

            Log(context, $"scroll crash repro: drag cycle {cycle}/5 complete (mem={CaptureMemory(forceGc: false)})");
        }

        // Cycles 6-8: rapid top <-> middle <-> bottom jumps triggering abrupt realization switches.
        double[] anchors = [0, 1.0, 0.33, 0.85, 0.0, 0.95, 0.15, 0.72, 1.0, 0.0];
        for (int cycle = 6; cycle <= 8; cycle++)
        {
            foreach (double ratio in anchors)
            {
                double h = scroller.ScrollableHeight;
                scroller.ChangeView(null, h * ratio, null, disableAnimation: true);
                if (cycle % 2 == 0)
                {
                    box.UpdateLayout();
                }

                await FramesAsync(1);
                TrackTree(tracker, box, mdDoc.Body);
            }

            Log(context, $"scroll crash repro: jump cycle {cycle}/8 complete (mem={CaptureMemory(forceGc: false)})");
        }

        // Cycles 9-10: activate a TableToolbar inside AdornerLayer while scrolling rapidly.
        TableCell? firstCell = box.State.Document.Children
            .OfType<Table>()
            .FirstOrDefault()?.Children
            .OfType<TableRow>()
            .FirstOrDefault()?.Children
            .OfType<TableCell>()
            .FirstOrDefault();
        if (firstCell is not null)
        {
            box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(firstCell.Id, 0))));
            box.UpdateLayout();
            await FramesAsync(2);
        }

        for (int cycle = 9; cycle <= 10; cycle++)
        {
            for (int step = 1; step <= 24; step++)
            {
                scroller.ChangeView(null, scroller.ScrollableHeight * step / 24.0, null, disableAnimation: true);
                await FramesAsync(1);
                TrackTree(tracker, box, mdDoc.Body);
            }

            for (int step = 23; step >= 0; step--)
            {
                scroller.ChangeView(null, scroller.ScrollableHeight * step / 24.0, null, disableAnimation: true);
                await FramesAsync(1);
                TrackTree(tracker, box, mdDoc.Body);
            }

            Log(context, $"scroll crash repro: active-table cycle {cycle}/10 complete (mem={CaptureMemory(forceGc: false)})");
        }

        (long totalDraws, int drawErrorCount) = CollectDrawStats(tracker);
        context.Stage.Children.Clear();
        await FramesAsync(2);

        context.Check(scrollableHeight > 0 && drawErrorCount == 0,
            "scroll crash repro: 10 drag/jump/active-table cycles completed cleanly",
            $"scrollable={scrollableHeight:0}, draws={totalDraws}, drawErrors={drawErrorCount}");
    }

    private static string BuildMixedCrashReproMarkdown()
    {
        var sb = new StringBuilder();
        (int Rows, int Cols, bool Compact)[] tableSpecs =
        [
            (3, 2, true), (9, 4, false), (3, 2, true), (12, 3, false), (4, 2, true), (10, 3, false), (3, 2, true), (14, 3, false), (12, 3, false),
            (7, 4, false), (3, 2, true), (10, 4, false), (7, 3, false), (4, 2, true), (5, 4, false), (17, 4, false), (13, 3, false),
        ];

        for (int s = 0; s < 31; s++)
        {
            int level = (s % 3) + 1;
            sb.Append('#', level).Append($" Section {s + 1}: LLM Infra Study Module\n\n");
            sb.Append($"Overview paragraph for section {s + 1} covering **CUDA kernels**, *KV cache paging*, `cudaMemcpyAsync`, and tensor parallelism trade-offs.\n\n");

            if (s < tableSpecs.Length)
            {
                (int rows, int cols, bool compact) = tableSpecs[s];
                sb.Append('|');
                for (int c = 0; c < cols; c++)
                {
                    sb.Append(compact ? $" K{c + 1} |" : $" Col {c + 1} |");
                }

                sb.Append("\n|");
                for (int c = 0; c < cols; c++)
                {
                    sb.Append(c % 2 == 0 ? " :--- |" : " --- |");
                }

                sb.Append('\n');
                for (int r = 0; r < rows - 2; r++)
                {
                    sb.Append('|');
                    for (int c = 0; c < cols; c++)
                    {
                        if (compact)
                        {
                            sb.Append(c == 0 ? $" **T{r + 1}** |" : $" V{r + 1} |");
                        }
                        else
                        {
                            sb.Append(c == 0 ? $" **Topic {r + 1}** |" : $" Detail entry {s}-{r}-{c} with wrapping text for cell sizing |");
                        }
                    }

                    sb.Append('\n');
                }

                sb.Append('\n');
            }

            if (s < 30)
            {
                for (int li = 0; li < 3 + (s % 3); li++)
                {
                    if (s < 15 && li == 0)
                    {
                        sb.Append(s % 2 == 0 ? "- [ ] " : "- [x] ").Append($"Task item {s + 1}.{li + 1} review operator fusion\n");
                    }
                    else
                    {
                        sb.Append($"- Bullet point {s + 1}.{li + 1} checking memory coalescing and occupancy\n");
                    }
                }

                sb.Append('\n');
            }

            if (s % 3 == 0 && s / 3 < 10)
            {
                sb.Append("```cpp\n");
                sb.Append($"__global__ void kernel_{s}(float* out, const float* in, int n) {{\n");
                sb.Append("    int idx = blockIdx.x * blockDim.x + threadIdx.x;\n");
                sb.Append("    if (idx < n) out[idx] = in[idx] * 2.0f;\n");
                sb.Append("}\n```\n\n");
            }

            if (s == 0)
            {
                sb.Append("> Key takeaway: benchmark prefill latency separately from decode bandwidth.\n\n");
            }
        }

        return sb.ToString();
    }
}
