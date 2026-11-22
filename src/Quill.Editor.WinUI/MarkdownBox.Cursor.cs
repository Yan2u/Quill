using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Quill.Editor.WinUI.Layout;
using Windows.Foundation;

namespace Quill.Editor.WinUI;

public sealed partial class MarkdownBox
{
    private InputSystemCursor? _editCursor;
    private InputSystemCursor? _arrowCursor;
    private InputSystemCursorShape? _activeCursor;

    /// <summary>The cursor shape last applied by the editor, for tests.</summary>
    internal InputSystemCursorShape? ActiveCursorShape => _activeCursor;

    /// <summary>The shape currently assigned to <see cref="UIElement.ProtectedCursor"/>, for tests.</summary>
    internal InputSystemCursorShape? ProtectedCursorShape =>
        ProtectedCursor is InputSystemCursor cursor ? cursor.CursorShape : null;

    private void InitializeCursor()
    {
        AddHandler(PointerEnteredEvent, new PointerEventHandler(OnCursorPointerMoved), handledEventsToo: true);
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnCursorPointerMoved), handledEventsToo: true);
        AddHandler(PointerExitedEvent, new PointerEventHandler(OnCursorPointerExited), handledEventsToo: true);
    }

    private void OnCursorPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_gestures.IsDown)
        {
            SetCursorShape(InputSystemCursorShape.IBeam);
            return;
        }

        UpdateCursor(e.GetCurrentPoint(this).Position, e.OriginalSource as DependencyObject);
    }

    private void OnCursorPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_gestures.IsDown)
        {
            ClearCursor();
        }
    }

    /// <summary>
    /// Updates the pointer cursor: an I-beam inside the document content column and the default
    /// arrow in outer margins or over interactive chrome controls.
    /// </summary>
    internal void UpdateCursor(Point point, DependencyObject? source = null)
    {
        if (IsReadOnly || !HasDocument)
        {
            ClearCursor();
            return;
        }

        bool edit = !IsInInteractiveChrome(source) && IsInContentArea(point);
        SetCursorShape(edit ? InputSystemCursorShape.IBeam : InputSystemCursorShape.Arrow);
    }

    /// <summary>Whether <paramref name="point"/> lies within the document content column horizontally.</summary>
    internal bool IsInContentArea(Point point)
    {
        if (IsReadOnly
            || !HasDocument
            || Document?.Page is not { ActualWidth: > 0 } page
            || XamlRoot is null
            || !ReferenceEquals(page.XamlRoot, XamlRoot))
        {
            return false;
        }

        if (point.X < 0 || point.X > ActualWidth || point.Y < 0 || point.Y > ActualHeight)
        {
            return false;
        }

        var mapped = TransformToVisual(page).TransformPoint(point);
        double left = page.Padding.Left;
        double right = page.ActualWidth - page.Padding.Right;
        return right > left && mapped.X >= left && mapped.X <= right;
    }

    private bool IsInInteractiveChrome(DependencyObject? source)
    {
        for (var element = source; element is not null && !ReferenceEquals(element, this); element = VisualTreeHelper.GetParent(element))
        {
            if (element is ScrollBar or ButtonBase or TextBox or AdornerLayer)
            {
                return true;
            }
        }

        return false;
    }

    private void SetCursorShape(InputSystemCursorShape shape)
    {
        if (_activeCursor == shape)
        {
            return;
        }

        _activeCursor = shape;
        ProtectedCursor = shape == InputSystemCursorShape.IBeam
            ? (_editCursor ??= InputSystemCursor.Create(InputSystemCursorShape.IBeam))
            : (_arrowCursor ??= InputSystemCursor.Create(InputSystemCursorShape.Arrow));
    }

    /// <summary>Releases any editor cursor override so surrounding chrome controls the pointer.</summary>
    internal void ClearCursor()
    {
        if (_activeCursor is null && ProtectedCursor is null)
        {
            return;
        }

        _activeCursor = null;
        ProtectedCursor = null;
    }

    private void DisposeCursors()
    {
        ClearCursor();
        _editCursor?.Dispose();
        _editCursor = null;
        _arrowCursor?.Dispose();
        _arrowCursor = null;
    }
}
