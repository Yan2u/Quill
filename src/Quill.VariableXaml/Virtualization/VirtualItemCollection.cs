using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Quill.VariableXaml.Model;

namespace Quill.VariableXaml.Virtualization;

/// <summary>
/// The items source behind a <c>vx:VirtualStack</c>: the virtual child nodes, one per item.
/// </summary>
/// <remarks>
/// Structural edits raise normal collection notifications so ItemsRepeater can shift realized
/// items. Content-only edits use <see cref="ReplaceSilently"/>: the realized element (if any) is
/// patched directly, so ItemsRepeater never recycles it and its state (focus, selection) survives.
/// </remarks>
internal sealed class VirtualItemCollection : ObservableCollection<VNode>
{
    public void ReplaceSilently(int index, VNode item) => Items[index] = item;

    /// <summary>Replaces all items with a single Reset notification.</summary>
    public void ResetTo(IEnumerable<VNode> items)
    {
        Items.Clear();
        foreach (VNode item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
