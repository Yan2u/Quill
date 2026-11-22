using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.VariableXaml.Markup;
using Quill.VariableXaml.Metadata;
using Quill.VariableXaml.Model;
using Quill.VariableXaml.Reconciliation;
using Windows.Foundation;

namespace Quill.VariableXaml;

/// <summary>An event attribute written in markup (<c>Click="Increment"</c>) fired.</summary>
public sealed class MarkupEventArgs(string handlerName, object? sender, object? originalArgs) : EventArgs
{
    public string HandlerName { get; } = handlerName;

    public object? Sender { get; } = sender;

    public object? OriginalArgs { get; } = originalArgs;
}

/// <summary>Aggregated state of the <c>vx:VirtualStack</c> elements currently mounted.</summary>
public readonly record struct VirtualizationSummary(int Stacks, int Items, int Realized, int CreatedTotal, int ReusedTotal);

/// <summary>
/// A content control that hosts a live XAML tree and keeps it in sync with changing markup
/// (<see cref="Xaml"/>) or a changing virtual tree (<see cref="Render"/>) by patching, not reloading.
/// </summary>
/// <remarks>
/// Pipeline per update: (incremental) parse → diff against the previous virtual tree → minimal
/// mutations of the live objects → WinUI's own incremental layout and rendering.
/// Elements whose description did not change keep their instances, so focus, selection,
/// scroll offsets and IME state inside them survive edits elsewhere in the tree.
/// Updates are coalesced: several changes in one dispatcher turn produce a single update.
/// </remarks>
public sealed partial class VariableXamlRoot : ContentControl
{
    public static readonly DependencyProperty XamlProperty = DependencyProperty.Register(
        nameof(Xaml),
        typeof(string),
        typeof(VariableXamlRoot),
        new PropertyMetadata(null, (sender, _) => ((VariableXamlRoot)sender).OnXamlChanged()));

    private readonly Reconciler _reconciler;
    private readonly IncrementalMarkupParser _parser;
    private MarkupDocument? _document;
    private LiveNode? _root;
    private PendingUpdate? _pending;
    private bool _flushQueued;
    private long _sequence;

    public VariableXamlRoot()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsTabStop = false;
        _reconciler = new Reconciler(this, XamlSchema.Default, RaiseMarkupEvent);
        _parser = new IncrementalMarkupParser(new XamlMarkupParser(XamlSchema.Default.IsWhitespaceSignificant));
    }

    /// <summary>Raised after every update, including failed ones.</summary>
    public event TypedEventHandler<VariableXamlRoot, UpdateReport>? Updated;

    /// <summary>Raised when an event attribute written in markup fires.</summary>
    public event TypedEventHandler<VariableXamlRoot, MarkupEventArgs>? MarkupEvent;

    /// <summary>XAML markup for the content. Setting it schedules an update.</summary>
    public string? Xaml
    {
        get => (string?)GetValue(XamlProperty);
        set => SetValue(XamlProperty, value);
    }

    public UpdateStrategy Strategy { get; set; } = UpdateStrategy.Incremental;

    /// <summary>Re-parse only the element enclosing a text edit (markup input only).</summary>
    public bool UseIncrementalParsing { get; set; } = true;

    /// <summary>Create new subtrees with one XamlReader.Load (true) or through the object model (false).</summary>
    public bool UseXamlReaderForNewContent
    {
        get => _reconciler.MountWithXamlReader;
        set => _reconciler.MountWithXamlReader = value;
    }

    /// <summary>
    /// Creates the layout of each <c>vx:VirtualStack</c> mounted from now on (for example one that
    /// caches item heights); null for a StackLayout configured by the Spacing and Orientation attributes.
    /// </summary>
    public Func<VirtualizingLayout>? VirtualLayoutFactory
    {
        get => _reconciler.VirtualLayoutFactory;
        set => _reconciler.VirtualLayoutFactory = value;
    }

    /// <summary>Coalesce changes into one update per dispatcher turn (true) or apply each change immediately.</summary>
    public bool DeferUpdates { get; set; } = true;

    /// <summary>Run a synchronous layout pass after each update and report its duration.</summary>
    public bool MeasureLayout { get; set; }

    public UpdateReport? LastReport { get; private set; }

    /// <summary>The parsed markup document, when the content comes from <see cref="Xaml"/>.</summary>
    public MarkupDocument? Document => _document;

    /// <summary>The virtual tree currently applied.</summary>
    public VNode? CurrentTree => _root?.Node;

    /// <summary>Replaces the content description with a virtual tree built in code.</summary>
    public void Render(VNode? tree)
    {
        _pending = new PendingUpdate(null, tree);
        Schedule();
    }

    /// <summary>Applies a pending update now instead of waiting for the dispatcher.</summary>
    public void Flush()
    {
        if (_pending is { } pending)
        {
            _pending = null;
            RunUpdate(pending);
        }
    }

    /// <summary>Drops the live tree; the next update mounts everything from scratch.</summary>
    public void Reset()
    {
        if (_root is not null)
        {
            _reconciler.BeginUpdate();
            _reconciler.UnmountRoot(_root);
            _reconciler.EndUpdate();
            _root = null;
        }

        _document = null;
        Content = null;
    }

    public VirtualizationSummary GetVirtualizationSummary()
    {
        int items = 0;
        int realized = 0;
        int created = 0;
        int reused = 0;
        foreach (var state in _reconciler.VirtualStates)
        {
            items += state.Items.Count;
            realized += state.RealizedCount;
            created += state.MountedTotal;
            reused += state.ReusedTotal;
        }

        return new VirtualizationSummary(_reconciler.VirtualStates.Count, items, realized, created, reused);
    }

    private void OnXamlChanged()
    {
        _pending = new PendingUpdate(Xaml ?? string.Empty, null);
        Schedule();
    }

    private void Schedule()
    {
        if (!DeferUpdates)
        {
            Flush();
            return;
        }

        if (!_flushQueued)
        {
            _flushQueued = true;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                _flushQueued = false;
                Flush();
            });
        }
    }

    private void RunUpdate(PendingUpdate pending)
    {
        long sequence = ++_sequence;
        var stopwatch = Stopwatch.StartNew();
        VNode? tree = pending.Tree;
        ParseOutcome? outcome = null;
        if (pending.Markup is { } markup)
        {
            try
            {
                outcome = string.IsNullOrWhiteSpace(markup)
                    ? null
                    : _parser.Parse(markup, _document, UseIncrementalParsing && Strategy == UpdateStrategy.Incremental);
            }
            catch (XamlMarkupException exception)
            {
                Publish(new UpdateReport
                {
                    Sequence = sequence,
                    Strategy = Strategy,
                    DocumentLength = markup.Length,
                    ParseTime = stopwatch.Elapsed,
                    Error = exception,
                });
                return;
            }

            _document = outcome?.Document;
            tree = _document?.Root;
        }

        TimeSpan parseTime = stopwatch.Elapsed;
        stopwatch.Restart();
        _reconciler.BeginUpdate();
        Exception? error = null;
        try
        {
            Apply(tree);
        }
        catch (Exception exception)
        {
            // The live tree may be half-patched; rebuild it from scratch to get back in sync.
            error = exception;
            RecoverFromFailure(tree);
        }

        TimeSpan reconcileTime = stopwatch.Elapsed;
        TimeSpan layoutTime = TimeSpan.Zero;
        if (MeasureLayout && error is null)
        {
            stopwatch.Restart();
            UpdateLayout();
            layoutTime = stopwatch.Elapsed;
        }

        (UpdateStatistics statistics, IReadOnlyList<string> diagnostics, IReadOnlyList<string> trace) = _reconciler.EndUpdate();
        Publish(new UpdateReport
        {
            Sequence = sequence,
            Strategy = Strategy,
            ParseKind = outcome?.Kind,
            ParsedLength = outcome?.ParsedLength ?? 0,
            DocumentLength = pending.Markup?.Length ?? 0,
            ReparsedElement = outcome?.ReparsedElement,
            TreeNodeCount = tree?.Count ?? 0,
            ParseTime = parseTime,
            ReconcileTime = reconcileTime,
            LayoutTime = layoutTime,
            Statistics = statistics,
            Diagnostics = diagnostics,
            Trace = trace,
            Error = error,
        });
    }

    private void Apply(VNode? tree)
    {
        if (tree is null)
        {
            if (_root is not null)
            {
                _reconciler.UnmountRoot(_root);
                _root = null;
                Content = null;
            }

            return;
        }

        if (_root is null || Strategy == UpdateStrategy.FullReload)
        {
            if (_root is not null)
            {
                _reconciler.UnmountRoot(_root);
                Content = null;
            }

            _root = _reconciler.MountRoot(tree);
            Content = _root.Instance;
            return;
        }

        LiveNode patched = _reconciler.PatchRoot(_root, tree);
        if (!ReferenceEquals(patched, _root))
        {
            Content = patched.Instance;
        }

        _root = patched;
    }

    private void RecoverFromFailure(VNode? tree)
    {
        _root = null;
        Content = null;
        if (tree is null)
        {
            return;
        }

        try
        {
            _root = _reconciler.MountRoot(tree);
            Content = _root.Instance;
        }
        catch (Exception exception)
        {
            _reconciler.Report($"Recovery mount failed: {exception.Message}");
        }
    }

    private void Publish(UpdateReport report)
    {
        LastReport = report;
        Updated?.Invoke(this, report);
    }

    private void RaiseMarkupEvent(string handlerName, object? sender, object? args) =>
        MarkupEvent?.Invoke(this, new MarkupEventArgs(handlerName, sender, args));

    private sealed record PendingUpdate(string? Markup, VNode? Tree);
}
