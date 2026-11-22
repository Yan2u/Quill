using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Quill.Playground.ImePrototype;

/// <summary>
/// The minimal custom input surface of Q6: holds keyboard focus (07: the surface, not the block
/// controls), translates semantic keys, places the caret on click, and exposes the active block
/// to the text service through <see cref="ImeInputController"/>. Rendering follows 07, rule 9:
/// apply the change, render, lay out, then notify the text service.
/// </summary>
internal sealed partial class ImeSurface : UserControl
{
    private static readonly Windows.UI.Color PageBackground = Colors.White;

    private readonly StackPanel _blocks = new() { Spacing = 10, Padding = new Thickness(16) };
    private readonly Rectangle _caret = new() { Width = 1.5, Fill = new SolidColorBrush(Colors.Black), IsHitTestVisible = false };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private CoreTextServiceContext? _service;

    public ImeSurface(ImeTextStore store, ImeEventLog log)
    {
        Store = store;
        Log = log;
        Controller = new ImeInputController(store, action => DispatcherQueue.TryEnqueue(() => action()), GetLayoutBounds, log);
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        Background = new SolidColorBrush(PageBackground);
        _overlay.Children.Add(_caret);
        var root = new Grid { Background = new SolidColorBrush(PageBackground) };
        root.Children.Add(_blocks);
        root.Children.Add(_overlay);
        Content = root;

        store.Changed += OnStoreChanged;
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            Controller.Detach();
            _service = null;
        };
        GotFocus += (_, _) =>
        {
            Log.Write("surface GotFocus");
            Controller.FocusEnter();
            _caret.Visibility = Visibility.Visible;
        };
        LostFocus += (_, _) =>
        {
            Log.Write("surface LostFocus");
            Controller.FocusLeave();
            _caret.Visibility = Visibility.Collapsed;
        };
        KeyDown += OnKeyDown;
        CharacterReceived += (_, args) =>
        {
            Log.Write($"CharacterReceived U+{(int)args.Character:X4}");
            args.Handled = Controller.OnCharacterReceived(args.Character);
        };
        PointerPressed += OnPointerPressed;
        Render();
    }

    public ImeTextStore Store { get; }

    public ImeEventLog Log { get; }

    public ImeInputController Controller { get; }

    /// <summary>Why the text service could not be created, if it could not (Q6).</summary>
    public string? ServiceError { get; private set; }

    public bool HasService => _service is not null;

    public ImeBlockView? ActiveView => _blocks.Children.Count > Store.BlockIndex ? _blocks.Children[Store.BlockIndex] as ImeBlockView : null;

    /// <summary>The caret and surface rectangles in physical screen pixels (07, rule 6).</summary>
    public ImeLayoutBounds? GetLayoutBounds()
    {
        if (XamlRoot is null || ActiveView is not { CurrentLayout: { } layout } view)
        {
            return null;
        }

        var (_, head) = Store.Selection;
        var caret = layout.CaretRect(head, backward: false);
        if (Store.CompositionRange is { } composition)
        {
            // Start the text bounds at the composition so the candidate window does not jump while typing.
            var first = layout.CaretRect(composition.Start, backward: false);
            if (Math.Abs(first.Y - caret.Y) < 0.5)
            {
                caret = new Rect(first.X, first.Y, Math.Max(1, caret.X - first.X), caret.Height);
            }
        }

        double scale = XamlRoot.RasterizationScale;
        var origin = ClientOrigin();
        var caretTopLeft = view.TransformToVisual(null).TransformPoint(new Point(caret.X, caret.Y));
        var surfaceTopLeft = TransformToVisual(null).TransformPoint(default);
        return new ImeLayoutBounds(
            new Rect(origin.X + caretTopLeft.X * scale, origin.Y + caretTopLeft.Y * scale, caret.Width * scale, caret.Height * scale),
            new Rect(origin.X + surfaceTopLeft.X * scale, origin.Y + surfaceTopLeft.Y * scale, ActualWidth * scale, ActualHeight * scale));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_service is not null || ServiceError is not null)
        {
            return;
        }

        try
        {
            _service = CoreTextServiceContext.Create(Controller, Log);
        }
        catch (Exception exception)
        {
            ServiceError = $"{exception.GetType().Name}: {exception.Message} (0x{exception.HResult:X8})";
            Log.Write($"CoreTextEditContext unavailable: {ServiceError}");
        }

        PositionCaret();
    }

    private void OnStoreChanged()
    {
        Render();
        UpdateLayout();
        PositionCaret();
        Controller.NotifyClientChanged();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool shift = IsDown(VirtualKey.Shift);
        bool control = IsDown(VirtualKey.Control);
        Log.Write($"KeyDown {e.Key} (original {e.OriginalKey}){(control ? " +Ctrl" : string.Empty)}{(shift ? " +Shift" : string.Empty)} composing={Store.IsComposing}");
        ImeKey? key = e.Key switch
        {
            VirtualKey.Left => ImeKey.Left,
            VirtualKey.Right => ImeKey.Right,
            VirtualKey.Up => ImeKey.Up,
            VirtualKey.Down => ImeKey.Down,
            VirtualKey.Home => ImeKey.Home,
            VirtualKey.End => ImeKey.End,
            VirtualKey.Back => ImeKey.Backspace,
            VirtualKey.Delete => ImeKey.Delete,
            VirtualKey.Enter => ImeKey.Enter,
            VirtualKey.Tab => ImeKey.Tab,
            VirtualKey.Z when control => ImeKey.Undo,
            VirtualKey.Y when control => ImeKey.Redo,
            _ => null,
        };
        if (key is not { } semantic || Store.IsComposing)
        {
            return;
        }

        if (semantic == ImeKey.Enter)
        {
            Controller.RecordSemanticKey("\r");
        }

        e.Handled = Store.ExecuteKey(semantic, shift);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        if (Store.IsComposing)
        {
            return;
        }

        for (int index = 0; index < _blocks.Children.Count; index++)
        {
            if (_blocks.Children[index] is not ImeBlockView { CurrentLayout: { } layout } view)
            {
                continue;
            }

            var point = e.GetCurrentPoint(view).Position;
            if (point.Y < view.ActualHeight + _blocks.Spacing / 2 || index == _blocks.Children.Count - 1)
            {
                Store.PlaceCaret(index, layout.HitTest(new System.Numerics.Vector2((float)point.X, (float)point.Y)));
                break;
            }
        }

        e.Handled = true;
    }

    private void Render()
    {
        while (_blocks.Children.Count < Store.BlockCount)
        {
            _blocks.Children.Add(new ImeBlockView(PageBackground));
        }

        while (_blocks.Children.Count > Store.BlockCount)
        {
            _blocks.Children.RemoveAt(_blocks.Children.Count - 1);
        }

        for (int index = 0; index < Store.BlockCount; index++)
        {
            var view = (ImeBlockView)_blocks.Children[index];
            view.Text = Store.TextOf(index);
            bool active = index == Store.BlockIndex;
            var (anchor, head) = Store.Selection;
            view.Selection = active && anchor != head ? (Math.Min(anchor, head), Math.Max(anchor, head)) : null;
            view.Composition = active ? Store.CompositionRange : null;
            view.Segments = active ? [.. Store.CompositionSegments] : [];
            view.Redraw();
        }
    }

    private void PositionCaret()
    {
        if (ActiveView is not { CurrentLayout: { } layout } view || XamlRoot is null)
        {
            return;
        }

        var rect = layout.CaretRect(Store.Selection.Head, backward: false);
        var topLeft = view.TransformToVisual(_overlay).TransformPoint(new Point(rect.X, rect.Y));
        Canvas.SetLeft(_caret, topLeft.X);
        Canvas.SetTop(_caret, topLeft.Y);
        _caret.Height = rect.Height;
    }

    private Point ClientOrigin()
    {
        var handle = Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        var point = new NativePoint();
        ClientToScreen(handle, ref point);
        return new Point(point.X, point.Y);
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint hwnd, ref NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
