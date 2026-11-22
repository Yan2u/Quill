using Windows.UI.Text.Core;

namespace Quill.Editor.WinUI.Input;

/// <summary>
/// The real text service: a <see cref="CoreTextEditContext"/> whose events go to a
/// <see cref="TextInputController"/>. Microsoft documents CoreTextServicesManager for desktop apps as
/// "supported in Insider builds only"; Q6 verified it in an unpackaged C# WinUI 3 app on Windows 11,
/// and the fallbacks (a hidden TextBox proxy, or TSF's ITextStoreACP) remain possible.
/// </summary>
public sealed class CoreTextServiceContext : ITextServiceContext
{
    private readonly CoreTextEditContext _context;
    private TextInputController? _controller;
    private bool _detached;

    private CoreTextServiceContext(CoreTextEditContext context, TextInputController controller)
    {
        _context = context;
        _controller = controller;
        _context.TextRequested += OnTextRequested;
        _context.SelectionRequested += OnSelectionRequested;
        _context.LayoutRequested += OnLayoutRequested;
        _context.TextUpdating += OnTextUpdating;
        _context.SelectionUpdating += OnSelectionUpdating;
        _context.FormatUpdating += OnFormatUpdating;
        _context.CompositionStarted += OnCompositionStarted;
        _context.CompositionCompleted += OnCompositionCompleted;
        _context.FocusRemoved += OnFocusRemoved;
    }

    internal void SetController(TextInputController? controller) => _controller = controller;

    /// <summary>Creates the context on the current (UI) thread and attaches the controller to it.</summary>
    public static CoreTextServiceContext Create(TextInputController controller, string name = "Quill")
    {
        var context = CoreTextServicesManager.GetForCurrentView().CreateEditContext();
        context.Name = name;
        context.InputScope = CoreTextInputScope.Text;
        context.InputPaneDisplayPolicy = CoreTextInputPaneDisplayPolicy.Automatic;
        var service = new CoreTextServiceContext(context, controller);
        controller.Attach(service);
        return service;
    }

    public void NotifyFocusEnter()
    {
        if (!_detached)
        {
            _context.NotifyFocusEnter();
        }
    }

    public void NotifyFocusLeave()
    {
        if (!_detached)
        {
            _context.NotifyFocusLeave();
        }
    }

    public void NotifyTextChanged(CoreTextRange modifiedRange, int newLength, CoreTextRange newSelection)
    {
        if (!_detached)
        {
            _context.NotifyTextChanged(modifiedRange, newLength, newSelection);
        }
    }

    public void NotifySelectionChanged(CoreTextRange selection)
    {
        if (!_detached)
        {
            _context.NotifySelectionChanged(selection);
        }
    }

    public void NotifyLayoutChanged()
    {
        if (!_detached)
        {
            _context.NotifyLayoutChanged();
        }
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        _controller = null;
        _context.TextRequested -= OnTextRequested;
        _context.SelectionRequested -= OnSelectionRequested;
        _context.LayoutRequested -= OnLayoutRequested;
        _context.TextUpdating -= OnTextUpdating;
        _context.SelectionUpdating -= OnSelectionUpdating;
        _context.FormatUpdating -= OnFormatUpdating;
        _context.CompositionStarted -= OnCompositionStarted;
        _context.CompositionCompleted -= OnCompositionCompleted;
        _context.FocusRemoved -= OnFocusRemoved;
    }

    private void OnTextRequested(CoreTextEditContext sender, CoreTextTextRequestedEventArgs args)
    {
        if (_controller is { } controller)
        {
            var request = args.Request;
            request.Text = controller.OnTextRequested(request.Range);
        }
    }

    private void OnSelectionRequested(CoreTextEditContext sender, CoreTextSelectionRequestedEventArgs args)
    {
        if (_controller is { } controller)
        {
            args.Request.Selection = controller.OnSelectionRequested();
        }
    }

    private void OnLayoutRequested(CoreTextEditContext sender, CoreTextLayoutRequestedEventArgs args)
    {
        if (_controller?.OnLayoutRequested() is { } bounds)
        {
            var layout = args.Request.LayoutBounds;
            layout.TextBounds = bounds.Text;
            layout.ControlBounds = bounds.Control;
        }
    }

    private void OnTextUpdating(CoreTextEditContext sender, CoreTextTextUpdatingEventArgs args) =>
        args.Result = _controller is { } controller
            ? controller.OnTextUpdating(args.Range, args.Text, args.NewSelection, args.IsCanceled)
            : CoreTextTextUpdatingResult.Failed;

    private void OnSelectionUpdating(CoreTextEditContext sender, CoreTextSelectionUpdatingEventArgs args) =>
        args.Result = args.IsCanceled || _controller is not { } controller || !controller.OnSelectionUpdating(args.Selection)
            ? CoreTextSelectionUpdatingResult.Failed
            : CoreTextSelectionUpdatingResult.Succeeded;

    private void OnFormatUpdating(CoreTextEditContext sender, CoreTextFormatUpdatingEventArgs args) =>
        args.Result = _controller is { } controller
            ? controller.OnFormatUpdating(args.Range, args.Reason, args.UnderlineType, args.IsCanceled)
            : CoreTextFormatUpdatingResult.Failed;

    private void OnCompositionStarted(CoreTextEditContext sender, CoreTextCompositionStartedEventArgs args) =>
        _controller?.OnCompositionStarted();

    private void OnCompositionCompleted(CoreTextEditContext sender, CoreTextCompositionCompletedEventArgs args) =>
        _controller?.OnCompositionCompleted();

    private void OnFocusRemoved(CoreTextEditContext sender, object args) => _controller?.OnFocusRemoved();
}
