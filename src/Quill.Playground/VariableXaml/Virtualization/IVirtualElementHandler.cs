using Quill.Playground.VariableXaml.Model;
using Quill.Playground.VariableXaml.Reconciliation;

namespace Quill.Playground.VariableXaml.Virtualization;

/// <summary>Marker for per-node state owned by a virtual element handler.</summary>
internal interface IVirtualElementState
{
}

/// <summary>
/// Implements a VariableXaml virtual element (an element in the <c>vx:</c> namespace) whose
/// children are not materialized one-to-one, e.g. <c>vx:VirtualStack</c>.
/// </summary>
internal interface IVirtualElementHandler
{
    LiveNode Mount(Reconciler reconciler, VNode node, LiveNode? parent);

    void Patch(Reconciler reconciler, LiveNode live, VNode next);

    void Unmount(Reconciler reconciler, LiveNode live);
}
