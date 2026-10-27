using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Quill.Playground.VariableXaml.Metadata;

/// <summary>
/// Uniform, index-based access to the child lists of XAML objects (UIElementCollection,
/// InlineCollection, BlockCollection, ItemCollection, RowDefinitionCollection, ...).
/// </summary>
internal interface IChildCollection
{
    int Count { get; }

    /// <summary>True when <see cref="Move"/> keeps the element in the tree (no Unloaded/Loaded, focus kept).</summary>
    bool HasNativeMove { get; }

    object? GetAt(int index);

    void SetAt(int index, object? item);

    void Insert(int index, object? item);

    void RemoveAt(int index);

    void Move(int oldIndex, int newIndex);

    void Clear();
}

internal static class ChildCollection
{
    /// <summary>Wraps a live collection object, or returns null if it is not an IList&lt;T&gt;.</summary>
    public static IChildCollection? Wrap(object? collection)
    {
        if (collection is null)
        {
            return null;
        }

        if (collection is UIElementCollection elements)
        {
            return new UIElementCollectionAdapter(elements);
        }

        Type? itemType = XamlMemberInfo.FindListItemType(collection.GetType());
        return itemType is null
            ? null
            : (IChildCollection)Activator.CreateInstance(typeof(ListAdapter<>).MakeGenericType(itemType), collection)!;
    }

    private sealed class UIElementCollectionAdapter(UIElementCollection elements) : IChildCollection
    {
        public int Count => elements.Count;

        public bool HasNativeMove => true;

        public object? GetAt(int index) => elements[index];

        // Not elements[index] = x: indexer replacement can leave stale internal parent state
        // (microsoft-ui-reactor hit COMExceptions reusing such elements), so remove, then insert.
        public void SetAt(int index, object? item)
        {
            elements.RemoveAt(index);
            elements.Insert(index, (UIElement)item!);
        }

        public void Insert(int index, object? item) => elements.Insert(index, (UIElement)item!);

        public void RemoveAt(int index) => elements.RemoveAt(index);

        // UIElementCollection.Move keeps the element in the live tree: focus, selection and IME state survive.
        public void Move(int oldIndex, int newIndex) => elements.Move((uint)oldIndex, (uint)newIndex);

        public void Clear() => elements.Clear();
    }

    private sealed class ListAdapter<T>(object list) : IChildCollection
    {
        private readonly IList<T> _list = (IList<T>)list;

        public int Count => _list.Count;

        public bool HasNativeMove => false;

        public object? GetAt(int index) => _list[index];

        public void SetAt(int index, object? item) => _list[index] = (T)item!;

        public void Insert(int index, object? item) => _list.Insert(index, (T)item!);

        public void RemoveAt(int index) => _list.RemoveAt(index);

        public void Move(int oldIndex, int newIndex)
        {
            T item = _list[oldIndex];
            _list.RemoveAt(oldIndex);
            _list.Insert(newIndex, item);
        }

        public void Clear() => _list.Clear();
    }
}
