namespace Quill.Settings.Insertion;

/// <summary>What to do with dropped items: a folder to show, files to open, and what to insert at the drop point.</summary>
public sealed record DropPlan(string? FolderToOpen, IReadOnlyList<string> FilesToOpen, IReadOnlyList<InsertedItem> Inserted)
{
    public static DropPlan Nothing { get; } = new(null, [], []);

    public bool IsEmpty => FolderToOpen is null && FilesToOpen.Count == 0 && Inserted.Count == 0;
}
