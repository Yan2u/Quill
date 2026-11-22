using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Quill.Shell.Sidebar;

/// <summary>Picks the tree's template by kind: folders expand, files do not.</summary>
public sealed partial class FileEntryTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FolderTemplate { get; set; }

    public DataTemplate? FileTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item is FileEntry { IsFolder: true } ? FolderTemplate : FileTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
