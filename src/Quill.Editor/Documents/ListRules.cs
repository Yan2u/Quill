namespace Quill.Editor.Documents;

/// <summary>
/// How the source writes the numbers of an ordered list (02-document-model.md, Q26).
/// </summary>
public enum ListNumbering
{
    /// <summary>Each item is one more than the previous, starting from the first item's number.</summary>
    Increasing,

    /// <summary>Every item has the same number, such as "1. 1. 1.".</summary>
    Same,

    /// <summary>Neither.</summary>
    Other,
}

/// <summary>
/// List rules shared by import, export and editing (02-document-model.md).
/// </summary>
public static class ListRules
{
    /// <summary>
    /// A list is loose when it was loose on import or holds a headless item anywhere in its tree
    /// (Q15, Q27).
    /// </summary>
    public static bool IsLoose(ListBlock list) => (list.Syntax?.WasLoose ?? false) || HasHeadlessItem(list);

    /// <summary>Whether any item in the list tree has a block other than a sub-list after its first child.</summary>
    public static bool HasHeadlessItem(ListBlock list)
    {
        foreach (var item in list.Children.OfType<ListItem>())
        {
            for (int index = 0; index < item.Children.Count; index++)
            {
                if (item.Children[index] is ListBlock sublist)
                {
                    if (HasHeadlessItem(sublist))
                    {
                        return true;
                    }
                }
                else if (index > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Sibling lists that reopen as one list when written with their own marker characters: the
    /// second follows the first with only empty paragraphs between them, and both have the same type
    /// and marker character. With <see cref="EditorOptions.SwitchMarkerForAdjacentLists"/> off, the
    /// view shows a hint for these (Q25).
    /// </summary>
    public static IReadOnlyList<(ListBlock First, ListBlock Second)> FindMergingAdjacentLists(ContainerBlock root)
    {
        var pairs = new List<(ListBlock, ListBlock)>();
        Collect(root, pairs);
        return pairs;

        static void Collect(ContainerBlock container, List<(ListBlock, ListBlock)> pairs)
        {
            Block? previous = null;
            foreach (var child in container.Children)
            {
                if (child is Paragraph { IsEmpty: true })
                {
                    continue;
                }

                if (previous is ListBlock first && child is ListBlock second
                    && first.IsOrdered == second.IsOrdered && first.MarkerChar == second.MarkerChar
                    && second.Syntax?.SeparateFrom != first.Id)
                {
                    pairs.Add((first, second));
                }

                if (child is ContainerBlock inner)
                {
                    Collect(inner, pairs);
                }

                previous = child;
            }
        }
    }

    /// <summary>
    /// Classifies the written numbers of an ordered list. A single item counts as increasing when its
    /// number is 1 and as same otherwise (Q26).
    /// </summary>
    public static ListNumbering ClassifyNumbering(ListBlock list)
    {
        var numbers = list.Children.OfType<ListItem>().Select(static item => item.Number ?? 1).ToList();
        if (numbers.Count == 0)
        {
            return ListNumbering.Increasing;
        }

        if (numbers.Count == 1)
        {
            return numbers[0] == 1 ? ListNumbering.Increasing : ListNumbering.Same;
        }

        bool increasing = true;
        bool same = true;
        for (int index = 1; index < numbers.Count; index++)
        {
            increasing &= numbers[index] == numbers[0] + index;
            same &= numbers[index] == numbers[0];
        }

        return increasing ? ListNumbering.Increasing : same ? ListNumbering.Same : ListNumbering.Other;
    }
}
