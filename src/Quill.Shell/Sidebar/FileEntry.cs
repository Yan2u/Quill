using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Quill.Shell.Sidebar;

/// <summary>A file or folder shown by the Files panel. A folder reads its children when it is first expanded.</summary>
public sealed partial class FileEntry : INotifyPropertyChanged
{
    private bool _hasUnrealizedChildren;
    private bool _isExpanded;

    public FileEntry(string path, bool isFolder)
    {
        Path = path;
        IsFolder = isFolder;
        Name = System.IO.Path.GetFileName(path);
        _hasUnrealizedChildren = isFolder;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; }

    public string Name { get; }

    public bool IsFolder { get; }

    /// <summary>Folder or document, in Segoe Fluent Icons.</summary>
    public string Glyph => IsFolder ? "\uE8B7" : "\uE8A5";

    public ObservableCollection<FileEntry> Children { get; } = [];

    /// <summary>A folder whose children were not read yet: the tree shows its chevron and asks when it expands.</summary>
    public bool HasUnrealizedChildren
    {
        get => _hasUnrealizedChildren;
        set
        {
            if (_hasUnrealizedChildren != value)
            {
                _hasUnrealizedChildren = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasUnrealizedChildren)));
            }
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }
}
