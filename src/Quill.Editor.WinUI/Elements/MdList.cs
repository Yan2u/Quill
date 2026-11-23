using Microsoft.UI.Xaml;
namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A list. Its items are separated by the list spacing, smaller than the block spacing (10); each
/// item draws its own marker.
/// </summary>
public sealed partial class MdList : MdContainerView
{
    private int _level = 1;
    private bool _isOrdered;

    public MdList()
        : base(draws: true)
    {
    }

    /// <summary>The nesting level, from 1.</summary>
    public int Level
    {
        get => _level;
        set => SetAndRemeasure(ref _level, value);
    }

    public bool IsOrdered
    {
        get => _isOrdered;
        set => SetAndRemeasure(ref _isOrdered, value);
    }

    protected override double ChildSpacing(UIElement upper, UIElement lower) => Sheet.ListItemSpacing;
}
