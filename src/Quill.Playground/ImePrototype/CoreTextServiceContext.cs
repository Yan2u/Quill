using Windows.UI.Text.Core;

namespace Quill.Playground.ImePrototype;

/// <summary>
/// The real text service: a <see cref="CoreTextEditContext"/> whose events are forwarded to an
/// <see cref="ImeInputController"/>. Microsoft documents CoreTextServicesManager for desktop apps
/// as "supported in Insider builds only"; Q6 verifies it in an unpackaged C# WinUI 3 app on Windows 11.
/// </summary>
internal sealed class CoreTextServiceContext : ITextServiceContext
{
    private readonly CoreTextEditContext _context;
    private readonly ImeInputController _controller;
    private readonly ImeEventLog _log;

    private CoreTextServiceContext(CoreTextEditContext context, ImeInputController controller, ImeEventLog log)
    {
        _context = context;
        _controller = controller;
        _log = log;
        _context.TextRequested += OnTextRequested;
        _context.SelectionRequested += OnSelectionRequested;
        _context.LayoutRequested += OnLayoutRequested;
        _context.TextUpdating += OnTextUpdating;
        _context.SelectionUpdating += OnSelectionUpdating;
        _context.FormatUpdating += OnFormatUpdating;
        _context.CompositionStarted += OnCompositionStarted;
        _context.CompositionCompleted += OnCompositionCompleted;
        _context.FocusRemoved += OnFocusRemoved;
        _context.NotifyFocusLeaveCompleted += OnNotifyFocusLeaveCompleted;
    }

    /// <summary>Creates the context on the current (UI) thread and attaches the controller to it.</summary>
    public static CoreTextServiceContext Create(ImeInputController controller, ImeEventLog log)
    {
        var manager = CoreTextServicesManager.GetForCurrentView();
        var context = manager.CreateEditContext();
        context.Name = "Quill IME lab";
        context.InputScope = CoreTextInputScope.Text;
        context.InputPaneDisplayPolicy = CoreTextInputPaneDisplayPolicy.Automatic;
        var service = new CoreTextServiceContext(context, controller, log);
        controller.Attach(service);
        log.Write($"CoreTextEditContext created (input language {manager.InputLanguage?.LanguageTag ?? "unknown"})");
        return service;
    }

    public void NotifyFocusEnter() => _context.NotifyFocusEnter();

    public void NotifyFocusLeave() => _context.NotifyFocusLeave();

    public void NotifyTextChanged(CoreTextRange modifiedRange, int newLength, CoreTextRange newSelection) =>
        _context.NotifyTextChanged(modifiedRange, newLength, newSelection);

    public void NotifySelectionChanged(CoreTextRange selection) => _context.NotifySelectionChanged(selection);

    public void NotifyLayoutChanged() => _context.NotifyLayoutChanged();

    public void Detach()
    {
        _context.TextRequested -= OnTextRequested;
        _context.SelectionRequested -= OnSelectionRequested;
        _context.LayoutRequested -= OnLayoutRequested;
        _context.TextUpdating -= OnTextUpdating;
        _context.SelectionUpdating -= OnSelectionUpdating;
        _context.FormatUpdating -= OnFormatUpdating;
        _context.CompositionStarted -= OnCompositionStarted;
        _context.CompositionCompleted -= OnCompositionCompleted;
        _context.FocusRemoved -= OnFocusRemoved;
        _context.NotifyFocusLeaveCompleted -= OnNotifyFocusLeaveCompleted;
    }

    private void OnTextRequested(CoreTextEditContext sender, CoreTextTextRequestedEventArgs args)
    {
        var request = args.Request;
        request.Text = _controller.OnTextRequested(request.Range);
    }

    private void OnSelectionRequested(CoreTextEditContext sender, CoreTextSelectionRequestedEventArgs args) =>
        args.Request.Selection = _controller.OnSelectionRequested();

    private void OnLayoutRequested(CoreTextEditContext sender, CoreTextLayoutRequestedEventArgs args)
    {
        if (_controller.OnLayoutRequested() is { } bounds)
        {
            var layout = args.Request.LayoutBounds;
            layout.TextBounds = bounds.Text;
            layout.ControlBounds = bounds.Control;
        }
    }

    private void OnTextUpdating(CoreTextEditContext sender, CoreTextTextUpdatingEventArgs args) =>
        args.Result = _controller.OnTextUpdating(args.Range, args.Text, args.NewSelection, args.IsCanceled);

    private void OnSelectionUpdating(CoreTextEditContext sender, CoreTextSelectionUpdatingEventArgs args) =>
        args.Result = args.IsCanceled || !_controller.OnSelectionUpdating(args.Selection)
            ? CoreTextSelectionUpdatingResult.Failed
            : CoreTextSelectionUpdatingResult.Succeeded;

    private void OnFormatUpdating(CoreTextEditContext sender, CoreTextFormatUpdatingEventArgs args) =>
        args.Result = _controller.OnFormatUpdating(args.Range, args.Reason, args.UnderlineType, args.IsCanceled);

    private void OnCompositionStarted(CoreTextEditContext sender, CoreTextCompositionStartedEventArgs args) =>
        _controller.OnCompositionStarted();

    private void OnCompositionCompleted(CoreTextEditContext sender, CoreTextCompositionCompletedEventArgs args)
    {
        var segments = args.CompositionSegments.Select(static segment =>
            $"{segment.PreconversionString}=>[{segment.Range.StartCaretPosition},{segment.Range.EndCaretPosition})");
        _log.Write($"  segments: {string.Join(", ", segments)}");
        _controller.OnCompositionCompleted(args.IsCanceled);
    }

    private void OnFocusRemoved(CoreTextEditContext sender, object args) => _controller.OnFocusRemoved();

    private void OnNotifyFocusLeaveCompleted(CoreTextEditContext sender, object args) => _log.Write("NotifyFocusLeaveCompleted");
}
