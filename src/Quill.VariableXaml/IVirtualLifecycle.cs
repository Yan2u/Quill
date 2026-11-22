namespace Quill.VariableXaml;

/// <summary>
/// Optional lifecycle hooks for controls managed by <see cref="VariableXamlRoot"/>.
/// <see cref="OnPooled"/> runs when a virtual stack moves an element into its recycle pool;
/// <see cref="OnUnmounted"/> runs when the reconciler permanently removes an element from the live tree.
/// </summary>
public interface IVirtualLifecycle
{
    void OnPooled() { }

    void OnUnmounted();
}
