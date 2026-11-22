using Quill.VariableXaml.Markup;
using Quill.VariableXaml.Reconciliation;

namespace Quill.VariableXaml;

/// <summary>How the content was brought up to date.</summary>
public enum UpdateStrategy
{
    /// <summary>Diff and patch: unchanged elements keep their instances.</summary>
    Incremental,

    /// <summary>Discard everything and create the whole tree again (the naive baseline).</summary>
    FullReload,
}

/// <summary>What one update did and how long each phase took.</summary>
public sealed class UpdateReport
{
    public long Sequence { get; init; }

    public UpdateStrategy Strategy { get; init; }

    /// <summary>How the markup was parsed; null when the update came from <see cref="VariableXamlRoot.Render"/>.</summary>
    public ParseKind? ParseKind { get; init; }

    /// <summary>Characters actually parsed (the re-parsed element for incremental parses).</summary>
    public int ParsedLength { get; init; }

    public int DocumentLength { get; init; }

    /// <summary>The element type that was re-parsed (incremental) or the root type (full parse).</summary>
    public string? ReparsedElement { get; init; }

    /// <summary>Number of virtual nodes in the new tree.</summary>
    public int TreeNodeCount { get; init; }

    public TimeSpan ParseTime { get; init; }

    public TimeSpan ReconcileTime { get; init; }

    /// <summary>Synchronous layout (UpdateLayout) after the patch; zero when layout timing is off.</summary>
    public TimeSpan LayoutTime { get; init; }

    public UpdateStatistics Statistics { get; init; } = new();

    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    /// <summary>Informational events, e.g. which elements were replaced and why.</summary>
    public IReadOnlyList<string> Trace { get; init; } = [];

    /// <summary>A parse or reconcile failure. The previous content stays on screen.</summary>
    public Exception? Error { get; init; }

    public bool Succeeded => Error is null;

    public TimeSpan TotalTime => ParseTime + ReconcileTime + LayoutTime;
}
