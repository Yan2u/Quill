namespace Quill.Settings.Insertion;

/// <summary>The kinds of things inserted for dropped or pasted items.</summary>
public enum InsertedKind
{
    /// <summary>A link to a file or folder: <c>[name](file:///…)</c> (Q108).</summary>
    Link,

    /// <summary>A local image, by the image rule.</summary>
    LocalImage,

    /// <summary>An online image, by the image rule.</summary>
    OnlineImage,
}

/// <summary>One thing to insert: a local path, or an online URL for <see cref="InsertedKind.OnlineImage"/>.</summary>
public sealed record InsertedItem(InsertedKind Kind, string Source);
