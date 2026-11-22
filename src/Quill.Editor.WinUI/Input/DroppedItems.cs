namespace Quill.Editor.WinUI.Input;

/// <summary>Files, web addresses and image data dropped on the editor or found on the clipboard (14-settings.md).</summary>
public sealed record DroppedItems(IReadOnlyList<string> Paths, IReadOnlyList<Uri> Urls, byte[]? Image = null)
{
    public bool IsEmpty => Paths.Count == 0 && Urls.Count == 0 && Image is null;
}
