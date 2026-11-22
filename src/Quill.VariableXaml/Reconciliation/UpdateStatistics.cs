namespace Quill.VariableXaml.Reconciliation;

/// <summary>
/// Work counters for one update. "Nodes" include text nodes; created/removed counts include
/// every node of a mounted or unmounted subtree.
/// </summary>
public sealed class UpdateStatistics
{
    /// <summary>Nodes whose attributes/content were compared because their hash changed.</summary>
    public int NodesPatched { get; internal set; }

    /// <summary>Subtrees skipped because they were reference-equal or had an equal hash.</summary>
    public int SubtreesSkipped { get; internal set; }

    public int NodesCreated { get; internal set; }

    public int NodesRemoved { get; internal set; }

    /// <summary>Existing children moved within their collection (keyed or matched by content).</summary>
    public int NodesMoved { get; internal set; }

    /// <summary>Nodes recreated because their type, key or opaque content changed.</summary>
    public int NodesReplaced { get; internal set; }

    public int PropertiesSet { get; internal set; }

    public int PropertiesCleared { get; internal set; }

    public int TextUpdates { get; internal set; }

    /// <summary>Calls to XamlReader.Load (subtree mounts, opaque values, markup-extension probes).</summary>
    public int XamlReaderLoads { get; internal set; }

    public override string ToString() =>
        $"patched {NodesPatched}, skipped {SubtreesSkipped}, created {NodesCreated}, removed {NodesRemoved}, " +
        $"moved {NodesMoved}, replaced {NodesReplaced}, props set {PropertiesSet}, cleared {PropertiesCleared}, " +
        $"text {TextUpdates}, XamlReader {XamlReaderLoads}";
}
