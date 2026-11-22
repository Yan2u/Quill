using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// The child blocks of a container: <see cref="Panel.Children"/> past the container's own drawing
/// canvas and decorations (a task checkbox), so the reconciler sees exactly the blocks of the VNode.
/// </summary>
internal sealed class BlockChildren(UIElementCollection children, Func<int> internalCount) : IList<UIElement>
{
    private int Offset => internalCount();

    public int Count => children.Count - Offset;

    public bool IsReadOnly => false;

    public UIElement this[int index]
    {
        get => children[index + Offset];
        set
        {
            children.RemoveAt(index + Offset);
            children.Insert(index + Offset, value);
        }
    }

    public void Add(UIElement item) => children.Add(item);

    public void Insert(int index, UIElement item) => children.Insert(index + Offset, item);

    public void RemoveAt(int index) => children.RemoveAt(index + Offset);

    public bool Remove(UIElement item)
    {
        int index = IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    public void Clear()
    {
        while (children.Count > Offset)
        {
            children.RemoveAt(children.Count - 1);
        }
    }

    public int IndexOf(UIElement item)
    {
        for (int i = Offset; i < children.Count; i++)
        {
            if (ReferenceEquals(children[i], item))
            {
                return i - Offset;
            }
        }

        return -1;
    }

    public bool Contains(UIElement item) => IndexOf(item) >= 0;

    public void CopyTo(UIElement[] array, int arrayIndex)
    {
        for (int i = Offset; i < children.Count; i++)
        {
            array[arrayIndex++] = children[i];
        }
    }

    public IEnumerator<UIElement> GetEnumerator()
    {
        for (int i = Offset; i < children.Count; i++)
        {
            yield return children[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
