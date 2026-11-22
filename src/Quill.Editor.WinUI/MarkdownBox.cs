using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Input;
using Quill.Editor.Projection;
using Quill.Editor.Services;
using Quill.Editor.State;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Editor.WinUI.Layout;
using Quill.Editor.WinUI.Rendering;
using Quill.Editor.WinUI.Search;
using Quill.Editor.WinUI.Theming;
using Quill.VariableXaml;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Quill.Editor.WinUI;

/// <summary>
/// The editor surface (07: InputAdapter): it holds the keyboard focus, translates keys through the
/// key map, pointer gestures through <see cref="PointerGestures"/> and clipboard actions through an
/// <see cref="IClipboardService"/> into commands, and never edits anything itself. After every
/// command it projects, reconciles, lays out and scrolls the caret into view, in that order (07, rule 9).
/// The <c>Handle*</c> methods are what the event handlers call; layer-2 tests call them directly
/// instead of simulating system input (08).
/// </summary>
public sealed partial class MarkdownBox : UserControl
{
    private readonly VariableXamlRoot _root = new() { DeferUpdates = false, IsTabStop = false };
    private EditorSession? _session;
    private EditorState? _projected;
    private PointerGestures _gestures = new(SystemClock.Instance);
    private char? _highSurrogate;
    private TextInputController? _textInput;
    private CoreTextServiceContext? _ownedTextService;
    private Action<string>? _textInputTrace;
    private bool _pendingViewUpdate;
    private bool _pendingHighlightingSync;
    private bool _pendingScrollIntoView;
    private bool _pendingSearchSync;
    private bool _pendingChanged;
    private RenderCache? _renders;
    private ImageLoader? _images;
    private string? _documentDirectory;

    // The source mode (11-source-mode.md): the editing session waits while a source session edits.
    private EditorSession? _editing;
    private SourceSession? _sourceSession;
    private bool _savedInSource;
    private SourceHighlighting? _highlighting;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _highlightTimer;
    private long _lastCommand;
    private MdStyleSheet _lightStyleSheet = MdStyleSheet.Light;
    private MdStyleSheet _darkStyleSheet = MdStyleSheet.Dark;
    private MdViewOptions _viewOptions = MdViewOptions.Default;
    private EditorOptions _options = EditorOptions.Default;
    private MdDocument? _boundDocument;

    public MarkdownBox()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        Rendering.Formulas.MathTypesetter.WarmUp();
        Rendering.CodeHighlighting.WarmUp(Quill.Highlighting.HighlightTheme.Light);
        MarkdownElements.Configure(_root);
        Content = _root;
        Search = new SearchController(this);
        Layout = new XamlLayoutService(() => _root.Content as MdDocument, () => CurrentProjection, () => State.Document, EnsureCurrent);
        GotFocus += (_, _) =>
        {
            SetFocus(true);
            _textInput?.FocusEnter();
        };
        LostFocus += (_, _) =>
        {
            _textInput?.FocusLeave();
            SetFocus(false);
        };
        Loaded += (_, _) => OnLoaded();
        Unloaded += (_, _) => OnUnloaded();
        KeyDown += OnKeyDown;
        CharacterReceived += OnCharacterReceived;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) =>
        {
            Run(_gestures.Cancel(State));
            ClearCursor();
        };
        PointerExited += (_, _) => SetHover(null);
        ContextRequested += OnContextRequested;
        InitializeDrops();
        InitializeCursor();
    }

    /// <summary>Ctrl+F (false) or Ctrl+H (true): the host's find panel opens (12-find-replace.md).</summary>
    public event EventHandler<bool>? FindRequested;

    /// <summary>Find and replace in this editor (12-find-replace.md).</summary>
    public SearchController Search { get; }

    /// <summary>Raised for a Ctrl+click on a link the editor does not follow itself (a URL or a file, Q38).</summary>
    public event EventHandler<LinkTarget>? LinkActivated;

    /// <summary>Raised after every command that ran, once the view is up to date.</summary>
    public event EventHandler? Changed;

    public EditorSession Session => _session ?? throw new InvalidOperationException("No document is loaded.");

    public EditorState State => Session.State;

    public bool HasDocument => _session is not null;

    /// <summary>Whether the document differs from the one loaded or last saved (in the source mode, as written in the source).</summary>
    public bool IsModified => _session is null ? false
        : _sourceSession is null || _savedInSource ? _session.IsModified
        : _editing!.IsModified || _session.IsModified;

    /// <summary>The context menu last opened (Q73); null before the first.</summary>
    public CommandBarFlyout? ContextMenuFlyout { get; private set; }

    public XamlLayoutService Layout { get; }

    /// <summary>The projection the view shows.</summary>
    public ProjectionResult? CurrentProjection { get; private set; }

    public KeyMap KeyMap { get; set; } = KeyMap.Default;

    public IClipboardService Clipboard { get; set; } = new SystemClipboard();

    /// <summary>
    /// A preview that only shows its document (15-themes.md: theme previews): it takes no focus, keys, clicks,
    /// context menu or drops, and scrolls with the wheel. Set before the box is shown.
    /// </summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            if (_isReadOnly == value)
            {
                return;
            }

            _isReadOnly = value;
            IsTabStop = !value;
            AllowDrop = !value;
            if (value)
            {
                ClearCursor();
                DetachTextService();
            }
            else if (IsLoaded || TextServiceFactory is not null)
            {
                AttachTextService();
            }
        }
    }

    private bool _isReadOnly;

    /// <summary>The rendered document root; null before the first render.</summary>
    public MdDocument? Document => _root.Content as MdDocument;

    /// <summary>
    /// The options of editing (14-settings.md: brackets, copying): for the documents loaded after, and the
    /// one shown (and the editing session waiting behind the source mode) at once.
    /// </summary>
    public EditorOptions Options
    {
        get => _options;
        set
        {
            _options = value;
            _session?.SetOptions(value);
            _editing?.SetOptions(value);
        }
    }

    /// <summary>The editor theme while the app is light (14-settings.md); kept across documents and modes.</summary>
    public MdStyleSheet LightStyleSheet
    {
        get => _lightStyleSheet;
        set
        {
            _lightStyleSheet = value;
            if (Document is { } document)
            {
                document.LightStyleSheet = value;
            }
        }
    }

    /// <summary>The editor theme while the app is dark.</summary>
    public MdStyleSheet DarkStyleSheet
    {
        get => _darkStyleSheet;
        set
        {
            _darkStyleSheet = value;
            if (Document is { } document)
            {
                document.DarkStyleSheet = value;
            }
        }
    }

    /// <summary>How the document shows apart from its theme (code wrapping); kept across documents and modes.</summary>
    public MdViewOptions ViewOptions
    {
        get => _viewOptions;
        set
        {
            _viewOptions = value;
            if (Document is { } document)
            {
                document.ViewOptions = value;
            }
        }
    }

    /// <summary>The root the projection is reconciled into.</summary>
    public VariableXamlRoot Root => _root;

    /// <summary>The input method connection (07); null before a document is loaded.</summary>
    public TextInputController? TextInput => _textInput;

    /// <summary>
    /// Creates the text service for the controller; by default a CoreTextEditContext. Tests set a
    /// scripted service before <see cref="Load"/> (08: input methods are simulated in tests).
    /// </summary>
    public Func<TextInputController, ITextServiceContext>? TextServiceFactory { get; set; }

    /// <summary>Why the text service could not be created, if it could not (the fallbacks of Q6 apply).</summary>
    public string? TextServiceError { get; private set; }

    /// <summary>The active CoreText service owned by this control, for identity probes.</summary>
    internal ITextServiceContext? ActiveTextService => _ownedTextService;

    /// <summary>Receives the text service events, for diagnosis.</summary>
    public Action<string>? TextInputTrace
    {
        get => _textInputTrace;
        set
        {
            _textInputTrace = value;
            if (_textInput is not null)
            {
                _textInput.Trace = value;
            }
        }
    }

    /// <summary>Runs image decoding and other render work; by default on the thread pool, completed on the UI thread (05).</summary>
    public IRenderScheduler? RenderScheduler { get; set; }

    /// <summary>Asynchronous render results of the document (images).</summary>
    public RenderCache? Renders => _renders;

    /// <summary>The folder of the document's file: relative image paths and links resolve against it (Q38).</summary>
    public string? DocumentDirectory
    {
        get => _documentDirectory;
        set
        {
            _documentDirectory = value;
            if (_images is not null)
            {
                _images.DocumentDirectory = value;
                _renders?.Clear();
                if (Document is { } document)
                {
                    document.Images = _images;
                }
            }
        }
    }

    /// <summary>Starts editing a document. <paramref name="ids"/> must be the source its blocks came from.</summary>
    public void Load(EditorState state, IBlockIdSource ids, IClock? clock = null, EditorOptions? options = null)
    {
        clock ??= SystemClock.Instance;
        _editing = null;
        _sourceSession = null;
        _savedInSource = false;
        Attach(new EditorSession(state, new EditorServices { Ids = ids, Clock = clock, Layout = Layout, Options = options ?? Options }));
    }

    /// <summary>Whether the source mode is on (11-source-mode.md).</summary>
    public bool IsSourceMode => _sourceSession is not null;

    /// <summary>The highlighting of the source mode; null in the editing mode.</summary>
    public SourceHighlighting? Highlighting => _highlighting;

    /// <summary>
    /// Ctrl+/: switches between the editing and the source mode (06, 11-source-mode.md). Entering commits
    /// the paragraph at the caret and shows the source text; leaving rebuilds the document, keeping the
    /// unchanged blocks, with every source edit an undo item. The caret follows the text both ways.
    /// </summary>
    public void ToggleSourceMode()
    {
        if (!HasDocument || State.Composition is not null)
        {
            return;
        }

        if (_sourceSession is null)
        {
            _editing = Session;
            _sourceSession = _editing.EnterSource();
            _savedInSource = false;
            Attach(new EditorSession(_sourceSession.Source, _editing.Services));
        }
        else
        {
            var source = Session;
            var editing = _editing!;
            bool savedHere = _savedInSource && !source.IsModified;
            editing.LeaveSource(_sourceSession, source);
            if (savedHere)
            {
                editing.MarkSaved(new SaveSnapshot(editing.State.Document, editing.History.Position));
            }

            _sourceSession = null;
            _editing = null;
            Attach(editing);
        }

        Layout.ScrollPositionIntoView(State.Selection.Head);
        _textInput?.NotifyClientChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows a session: renders its document and connects the input method to it.</summary>
    private void Attach(EditorSession session)
    {
        var clock = session.Services.Clock;
        _session = session;
        _gestures = new PointerGestures(clock) { MultiClickTime = TimeSpan.FromMilliseconds(GetDoubleClickTime()) };
        _projected = null;
        StopHighlightTimer();
        DetachDocument();
        _root.Reset();
        Layout.ClearDetached();
        DisposeRenders();
        _renders = new RenderCache(RenderScheduler ?? new DispatcherRenderScheduler(DispatcherQueue), () => Session.State.Document);
        _renders.ResultReady += OnRenderReady;
        _images = new ImageLoader(_renders) { DocumentDirectory = _documentDirectory };
        Refresh();
        _highlighting = SourceText.IsSource(session.State.Document) && Document is { } shown
            ? new SourceHighlighting(ThemeOf(shown), session.State.Document)
            : null;
        if (Document is { } document)
        {
            BindDocument(document);
        }

        bool reuseOwned = TextServiceFactory is null && !_isReadOnly && _ownedTextService is not null;
        bool wasFocused = reuseOwned && _textInput is { IsFocused: true };
        string? previousKnown = reuseOwned && _textInput is { IsAttached: true } existing ? existing.KnownText : null;
        if (reuseOwned)
        {
            _textInput?.DisconnectWithoutDetachingContext();
        }
        else
        {
            _textInput?.Detach();
        }

        _pendingViewUpdate = false;
        _pendingHighlightingSync = false;
        _pendingScrollIntoView = false;
        _pendingSearchSync = false;
        _pendingChanged = false;

        var client = new EditorTextInput(session, Execute);
        _textInput = new TextInputController(client, action => DispatcherQueue.TryEnqueue(() => action()), TextInputBounds)
        {
            Trace = _textInputTrace,
            BeforeSynchronize = FlushPendingViewUpdate,
        };
        if (!_isReadOnly && (IsLoaded || TextServiceFactory is not null))
        {
            AttachTextService(previousKnown, wasFocused);
        }

        RunHighlighting(TimeSpan.FromMilliseconds(8));
        Search.OnDocumentReplaced();
    }

    private void OnLoaded()
    {
        Rendering.CodeHighlighting.Loaded -= OnGrammarsLoaded;
        Rendering.CodeHighlighting.Loaded += OnGrammarsLoaded;
        if (_session is not null && Document is null)
        {
            RestoreDocumentView();
        }

        AttachTextService();
    }

    private void OnUnloaded()
    {
        Rendering.CodeHighlighting.Loaded -= OnGrammarsLoaded;
        DetachTextService();
        StopHighlightTimer();
        Search.DetachTimer();
        DetachDocument();
        _root.Reset();
        Layout.ClearDetached();
        DisposeRenders();
        DisposeCursors();
        _projected = null;
    }

    private void RestoreDocumentView()
    {
        if (_session is null)
        {
            return;
        }

        _projected = null;
        if (_renders is null)
        {
            _renders = new RenderCache(RenderScheduler ?? new DispatcherRenderScheduler(DispatcherQueue), () => Session.State.Document);
            _renders.ResultReady += OnRenderReady;
            _images = new ImageLoader(_renders) { DocumentDirectory = _documentDirectory };
        }

        Refresh();
        if (Document is { } document)
        {
            BindDocument(document);
        }

        if (_highlighting is not null)
        {
            RunHighlighting(TimeSpan.FromMilliseconds(8));
        }
    }

    private void BindDocument(MdDocument document)
    {
        DetachDocument();
        _boundDocument = document;
        document.LightStyleSheet = _lightStyleSheet;
        document.DarkStyleSheet = _darkStyleSheet;
        document.ViewOptions = _viewOptions;
        document.Images = _images;
        document.Highlighting = _highlighting;
        document.Matches = Search;
        document.StyleSheetChanged += OnDocumentStyleSheetChanged;
        document.Adorners.TableAction += OnDocumentTableAction;
        document.ScrollViewer.ViewChanged += OnDocumentViewChanged;
    }

    private void DetachDocument()
    {
        if (_boundDocument is { } document)
        {
            document.StyleSheetChanged -= OnDocumentStyleSheetChanged;
            document.Adorners.TableAction -= OnDocumentTableAction;
            document.ScrollViewer.ViewChanged -= OnDocumentViewChanged;
            _boundDocument = null;
        }
    }

    private void DisposeRenders()
    {
        if (_renders is { } renders)
        {
            renders.ResultReady -= OnRenderReady;
            renders.Dispose();
            _renders = null;
        }

        _images = null;
    }

    private void StopHighlightTimer()
    {
        if (_highlightTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnHighlightTimerTick;
            _highlightTimer = null;
        }
    }

    private void OnDocumentStyleSheetChanged(object? sender, EventArgs e)
    {
        if (sender is MdDocument document)
        {
            OnStyleSheetChanged(document);
        }
    }

    private async void OnDocumentTableAction(object? sender, Command? command) => await RunTableActionAsync(command);

    private void OnDocumentViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => ViewportChanged?.Invoke(this, EventArgs.Empty);

    private void OnHighlightTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) =>
        RunHighlighting(TimeSpan.FromMilliseconds(Stopwatch.GetElapsedTime(_lastCommand).TotalMilliseconds > 300 ? 12 : 3));

    private static global::Quill.Highlighting.HighlightTheme ThemeOf(MdDocument document)
    {
        var background = document.EffectiveStyleSheet.PageBackground;
        return background.R * 0.299 + background.G * 0.587 + background.B * 0.114 < 128 ? global::Quill.Highlighting.HighlightTheme.Dark : global::Quill.Highlighting.HighlightTheme.Light;
    }

    private void OnStyleSheetChanged(MdDocument document)
    {
        if (_highlighting is not null && ReferenceEquals(document, Document))
        {
            _highlighting.SetTheme(ThemeOf(document));
            RunHighlighting(TimeSpan.FromMilliseconds(8));
        }
    }

    /// <summary>
    /// Analyzes the source lines within a budget and redraws the lines whose colors changed; what is
    /// left goes on in small slices on the dispatcher, so typing is never held up (11-source-mode.md).
    /// </summary>
    private void RunHighlighting(TimeSpan budget)
    {
        if (_highlighting is null)
        {
            return;
        }

        foreach (string key in _highlighting.Run(budget))
        {
            if (Document?.Geometry.Find(key) is MdTextBlockView view)
            {
                view.RefreshContent();
            }
        }

        if (_highlighting.IsComplete)
        {
            StopHighlightTimer();
            return;
        }

        // The lines in view that the analysis has not reached get provisional colors now.
        if (Document is { } document)
        {
            var shown = document.Geometry.Realized.OfType<MdSourceLine>().Where(line => !document.Geometry.IsDetached(line)).Select(static line => line.BlockId).ToList();
            foreach (string key in _highlighting.Preview(shown))
            {
                if (document.Geometry.Find(key) is MdTextBlockView view)
                {
                    view.RefreshContent();
                }
            }
        }

        if (_highlightTimer is null)
        {
            _highlightTimer = DispatcherQueue.CreateTimer();
            _highlightTimer.Interval = TimeSpan.FromMilliseconds(16);

            // Large slices while the keyboard rests, small ones while typing.
            _highlightTimer.Tick += OnHighlightTimerTick;
        }

        _highlightTimer.Start();
    }

    /// <summary>Runs a command and brings the view up to date: render, layout, caret into view (07, rule 9).</summary>
    public void Execute(Command command)
    {
        var before = State;
        long totalStart = Stopwatch.GetTimestamp();
        long start = totalStart;
        _lastCommand = start;
        Session.Execute(command);

        bool documentChanged = !ReferenceEquals(before.Document, State.Document);
        bool selectionOrDocChanged = documentChanged || before.Selection != State.Selection;
        bool stateChanged = !ReferenceEquals(before, State);

        if (_textInput is { IsUpdating: true })
        {
            // CoreText runs inside an ALPC batch on the UI thread: defer XAML layout, scrolling,
            // and host window updates until Synchronize() runs on the dispatcher (after callback return).
            _pendingViewUpdate = true;
            _pendingHighlightingSync |= documentChanged;
            _pendingScrollIntoView |= selectionOrDocChanged;
            _pendingSearchSync |= stateChanged;
            _pendingChanged = true;
            return;
        }

        ApplyPostExecute(
            command.GetType().Name,
            totalStart,
            documentChanged || _pendingHighlightingSync,
            selectionOrDocChanged || _pendingScrollIntoView,
            stateChanged || _pendingSearchSync,
            notifyInput: true,
            invokeChanged: true);
    }

    private void FlushPendingViewUpdate()
    {
        if (!_pendingViewUpdate)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        ApplyPostExecute(
            "DeferredImeSync",
            now,
            _pendingHighlightingSync,
            _pendingScrollIntoView,
            _pendingSearchSync,
            notifyInput: false,
            invokeChanged: _pendingChanged);
    }

    private void ApplyPostExecute(string commandName, long commandStart, bool highlightingSync, bool scrollIntoView, bool searchSync, bool notifyInput, bool invokeChanged)
    {
        _pendingViewUpdate = false;
        _pendingHighlightingSync = false;
        _pendingScrollIntoView = false;
        _pendingSearchSync = false;
        _pendingChanged = false;

        if (_highlighting is not null && highlightingSync)
        {
            _highlighting.Sync(State.Document);
            RunHighlighting(TimeSpan.FromMilliseconds(1));
        }

        var commandTime = Stopwatch.GetElapsedTime(commandStart);
        var (projection, reconcile, layout) = RefreshTimed();

        // Only edits and selection changes bring the caret into view, not hover or focus changes.
        long start = Stopwatch.GetTimestamp();
        if (scrollIntoView)
        {
            Layout.ScrollPositionIntoView(State.Selection.Head);
        }

        var scroll = Stopwatch.GetElapsedTime(start);

        if (searchSync)
        {
            Search.OnStateChanged();
        }

        // Last, once the view is laid out: the text service may ask for layout right away (07, rule 9).
        start = Stopwatch.GetTimestamp();
        if (notifyInput)
        {
            _textInput?.NotifyClientChanged();
        }

        LastUpdate = new EditorUpdateReport
        {
            Command = commandName,
            CommandTime = commandTime,
            ProjectionTime = projection,
            ReconcileTime = reconcile,
            LayoutTime = layout,
            ScrollTime = scroll,
            InputTime = Stopwatch.GetElapsedTime(start),
        };
        if (invokeChanged)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The phases of the last <see cref="Execute"/> (09, rule 7).</summary>
    public EditorUpdateReport? LastUpdate { get; private set; }

    /// <summary>A key press: the chord's binding, if any. Returns whether the key was used.</summary>
    public bool HandleKey(int virtualKey, KeyModifiers modifiers)
    {
        // During a composition every key belongs to the input method.
        if (!HasDocument || State.Composition is not null || VirtualKeys.ToChord(virtualKey, modifiers) is not { } chord)
        {
            return false;
        }

        if (KeyMap.Find(chord) is not { } binding)
        {
            return false;
        }

        if (binding is CommandBinding { Command: SplitBlock })
        {
            // The service may report the line break afterwards: an acknowledgement (07, rule 5).
            _textInput?.RecordSemanticKey("\r");
        }

        _ = RunBindingAsync(binding, refocus: false);
        return true;
    }

    /// <summary>
    /// Runs what a key or a menu entry is bound to: a command, a clipboard action, a dialog or a view
    /// action. A menu gives the focus back to the editor afterwards.
    /// </summary>
    public async Task RunBindingAsync(KeyBinding binding, bool refocus = true)
    {
        if (!HasDocument)
        {
            return;
        }

        switch (binding)
        {
            case CommandBinding command:
                Execute(command.Command);
                break;
            case ClipboardBinding clipboard:
                await RunClipboardAsync(clipboard.Action);
                break;
            case DialogBinding dialog:
                await RunDialogAsync(dialog.Dialog);
                break;
            case ViewBinding { Action: ViewAction.RevealSelection }:
                Layout.ScrollPositionIntoView(State.Selection.Head);
                break;
            case ViewBinding { Action: ViewAction.ToggleSourceMode }:
                ToggleSourceMode();
                break;
            case ViewBinding { Action: ViewAction.Find or ViewAction.Replace } find:
                FindRequested?.Invoke(this, find.Action == ViewAction.Replace);
                return;
            case ViewBinding { Action: ViewAction.FindNext or ViewAction.FindPrevious } next:
                if (Search.Query is null)
                {
                    FindRequested?.Invoke(this, false);
                    return;
                }

                Search.FindNext(next.Action == ViewAction.FindNext);
                break;
        }

        if (refocus)
        {
            Focus(FocusState.Programmatic);
        }
    }

    /// <summary>The Edit, Paragraph and Format menus for the current state (M6.0), for the window's menu bar.</summary>
    public MenuContext MenuContext() => new(State, Session.History, Clipboard.CanPaste, KeyMap, IsSourceMode, Session.Services.Options.CopyLineWithoutSelection);

    /// <summary>
    /// A typed character that no input method composed (Q66: the second text path). Control
    /// characters belong to the key map; a surrogate pair arrives as two characters.
    /// </summary>
    public bool HandleCharacter(char character)
    {
        if (!HasDocument || char.IsControl(character))
        {
            return false;
        }

        if (_textInput is { IsAttached: true } input)
        {
            // Through the controller, which recognizes text CoreText already delivered (Q66).
            return input.OnCharacterReceived(character);
        }

        if (char.IsHighSurrogate(character))
        {
            _highSurrogate = character;
            return true;
        }

        string text = char.IsLowSurrogate(character) && _highSurrogate is { } high ? $"{high}{character}" : character.ToString();
        _highSurrogate = null;
        Execute(new InsertText(text));
        return true;
    }

    /// <summary>A press at a point in this control's coordinates.</summary>
    public void HandlePointerPressed(Point point, KeyModifiers modifiers)
    {
        if (!HasDocument)
        {
            return;
        }

        var page = ToPage(point);
        if (ClickAdornment(new Point(page.X, page.Y)))
        {
            return;
        }

        var hit = Layout.HitTest(page);
        Run(_gestures.Pressed(new PointerInput(point.X, point.Y, modifiers), hit, State));
    }

    public void HandlePointerMoved(Point point, KeyModifiers modifiers)
    {
        if (!HasDocument)
        {
            return;
        }

        if (_gestures.IsDown)
        {
            Run(_gestures.Moved(new PointerInput(point.X, point.Y, modifiers), () => Layout.HitTest(ToPage(point))));
            return;
        }

        SetHover(HoverAt(ToPage(point)));
    }

    public void HandlePointerReleased(Point point, KeyModifiers modifiers)
    {
        if (!HasDocument)
        {
            return;
        }

        Run(_gestures.Released(new PointerInput(point.X, point.Y, modifiers), State, out var target));
        if (target is LinkTarget { IsAnchor: false } link)
        {
            LinkActivated?.Invoke(this, link);
        }
    }

    /// <summary>
    /// Shows a dialog and returns the command it produced, or null when cancelled. By default the
    /// view's own dialogs (Ctrl+T: <see cref="InsertTableDialog"/>); tests replace it so no dialog opens (08).
    /// </summary>
    public Func<EditorDialog, Task<Command?>>? DialogHandler { get; set; }

    /// <summary>Asks through a dialog, then runs the command it produced; the focus comes back to the editor.</summary>
    public async Task RunDialogAsync(EditorDialog dialog)
    {
        // Tables, links and images are written as text in the source mode.
        if (!HasDocument || IsSourceMode)
        {
            return;
        }

        var command = await (DialogHandler ?? ShowDialogAsync)(dialog);
        if (command is not null && HasDocument)
        {
            Execute(command);
        }

        Focus(FocusState.Programmatic);
    }

    private async Task<Command?> ShowDialogAsync(EditorDialog dialog)
    {
        switch (dialog)
        {
            case EditorDialog.InsertLink:
                return new InsertLink(await ClipboardUrlAsync());
            case EditorDialog.InsertImage:
                return new InsertImage(await ClipboardUrlAsync());
            case EditorDialog.InsertLocalImage when Insertions is not null:
                return await Insertions.PickImagesAsync() is { Length: > 0 } images ? new Paste(new ClipboardContent(images, images)) : null;
            case EditorDialog.InsertTable when XamlRoot is not null:
                var ask = new InsertTableDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
                return await ask.AskAsync() is { } size ? new InsertTable(size.Columns, size.Rows) : null;
            default:
                return null;
        }
    }

    /// <summary>A URL on the clipboard, for Ctrl+K and Ctrl+Shift+I (confirmed with the user): one line that is an absolute URL.</summary>
    private async Task<string?> ClipboardUrlAsync()
    {
        try
        {
            string? text = (await Clipboard.GetAsync())?.PlainText.Trim();
            return text is { Length: > 0 } && !text.Contains('\n') && Uri.TryCreate(text, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https" or "mailto" or "ftp" or "file"
                ? text
                : null;
        }
        catch (Exception)
        {
            // The clipboard is held by another program: insert without a URL.
            return null;
        }
    }

    /// <summary>Completes a clipboard action with <see cref="Clipboard"/> (04).</summary>
    public async Task RunClipboardAsync(ClipboardAction action)
    {
        switch (action)
        {
            case ClipboardAction.Copy:
                await SetClipboardAsync(Session.Copy());
                break;
            case ClipboardAction.CopyAsMarkdown:
                await SetClipboardAsync(Session.CopyAsMarkdown() with { Markdown = null });
                break;
            case ClipboardAction.CopyAsPlainText:
                await SetClipboardAsync(Session.CopyAsPlainText());
                break;
            case ClipboardAction.Cut:
                if (await SetClipboardAsync(Session.Copy()))
                {
                    Execute(new Cut());
                }

                break;
            case ClipboardAction.Paste:
                if (await Clipboard.GetAsync() is { } pasted)
                {
                    Execute(new Paste(pasted));
                }
                else
                {
                    await PasteItemsAsync();
                }

                break;
            case ClipboardAction.PasteAsPlainText when await Clipboard.GetAsync() is { } content:
                Execute(new PasteAsPlainText(content.PlainText));
                break;
        }
    }

    /// <summary>Puts copied content on the clipboard; nothing copied (a caret, without copying lines) leaves the clipboard as it was.</summary>
    private async Task<bool> SetClipboardAsync(ClipboardContent content)
    {
        if (content is { Markdown: null, PlainText.Length: 0 })
        {
            return false;
        }

        await Clipboard.SetAsync(content);
        return true;
    }

    /// <summary>
    /// Prepares a save (06): commits the paragraph at the caret, through <see cref="Execute"/> so the view
    /// follows, and returns the snapshot to write. Call <see cref="MarkSaved"/> once it is written.
    /// </summary>
    public SaveSnapshot PrepareSave()
    {
        Execute(new CommitParagraph());
        return Session.PrepareSave();
    }

    /// <summary>How far below the viewport's top a block must reach to count as the first in view.</summary>
    private const double ReadingLine = 24;

    /// <summary>The view scrolled (the outline follows the scroll position, M6.5).</summary>
    public event EventHandler? ViewportChanged;

    /// <summary>
    /// The first leaf block in view: the top-most realized text block reaching below a line a little under
    /// the viewport's top (a block whose last pixels peek in at the top does not count), or null.
    /// </summary>
    public BlockId? FirstVisibleBlock()
    {
        if (Document?.Geometry is not { } geometry)
        {
            return null;
        }

        var viewport = Layout.GetViewport();
        MdBlockView? first = null;
        double top = double.MaxValue;
        foreach (var view in geometry.Realized)
        {
            if (view is not MdTextBlockView || geometry.IsDetached(view))
            {
                continue;
            }

            var bounds = geometry.Bounds(view);
            if (bounds.Bottom > viewport.Y + ReadingLine && bounds.Y < top)
            {
                top = bounds.Y;
                first = view;
            }
        }

        return first is null ? null : BlockGeometry.ParseKey(first.BlockId);
    }

    /// <summary>Goes to a heading (the outline, M6.5): the caret at its start, the heading at the top of the view.</summary>
    public void GoToHeading(BlockId heading)
    {
        if (!HasDocument || DocumentLookup.Find(State.Document, heading) is null)
        {
            return;
        }

        Execute(new SetSelection(TextSelection.Caret(new TextPosition(heading, 0))));
        Layout.ScrollToTop(heading);
        Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// The file changed on disk (M6.4): its bytes are imported with this session's IDs and replace the
    /// document as one undo item; the result counts as saved. Leaves the source mode first.
    /// </summary>
    public void Reload(byte[] bytes, Quill.Editor.Markdown.TextEncodingChoice? encoding = null)
    {
        if (IsSourceMode)
        {
            ToggleSourceMode();
        }

        var reloaded = Quill.Editor.Markdown.MarkdownImporter.ImportFile(bytes, Session.Services.Ids, encoding).Document;
        Execute(new ReloadDocument(reloaded));
        MarkSaved(Session.PrepareSave());
    }

    /// <summary>The file is gone (deleted or renamed away): the document counts as modified, so saving writes it again.</summary>
    public void MarkUnsaved()
    {
        Session.MarkSaved(new SaveSnapshot(State.Document, -1));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The snapshot was written; raises <see cref="Changed"/> so the title can drop its marker.</summary>
    public void MarkSaved(SaveSnapshot snapshot)
    {
        Session.MarkSaved(snapshot);
        _savedInSource |= IsSourceMode;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the context menu (Q73). <paramref name="point"/> is a right click in this control's
    /// coordinates: outside the selection the caret moves there first; null (the menu key, Shift+F10)
    /// opens it at the caret.
    /// </summary>
    public CommandBarFlyout? HandleContextRequested(Point? point)
    {
        if (!HasDocument)
        {
            return null;
        }

        if (point is { } at && ContextMenu.SelectionFor(Layout.HitTest(ToPage(at)), State) is { } select)
        {
            Execute(select);
        }

        var anchor = point ?? CaretPoint();
        var (primary, secondary) = EditorMenus.Context(MenuContext());
        var menu = EditorContextMenu.Create(primary, secondary, entry => _ = RunMenuItemAsync(entry));
        ContextMenuFlyout = menu;
        if (XamlRoot is not null)
        {
            menu.ShowAt(this, new FlyoutShowOptions { Position = anchor, ShowMode = FlyoutShowMode.Standard });
        }

        return menu;
    }

    /// <summary>Runs a menu entry, then gives the focus back to the editor.</summary>
    public async Task RunMenuItemAsync(MenuEntry entry)
    {
        if (entry.Binding is { } binding)
        {
            await RunBindingAsync(binding);
        }
    }

    /// <summary>Projects the current state if it changed, reconciles it and completes layout.</summary>
    public void Refresh() => RefreshTimed();

    /// <summary>Refreshes realized block placeholders and adorners for the current interface language.</summary>
    public void RefreshLanguage() => Document?.RefreshLanguage();

    private (TimeSpan Projection, TimeSpan Reconcile, TimeSpan Layout) RefreshTimed()
    {
        TimeSpan projection = default, reconcile = default;
        long start = Stopwatch.GetTimestamp();
        if (_session is not null && !ReferenceEquals(_projected, _session.State))
        {
            CurrentProjection = _session.Project();
            projection = Stopwatch.GetElapsedTime(start);
            start = Stopwatch.GetTimestamp();
            _root.Render(CurrentProjection.Root);
            _root.Flush();
            _projected = _session.State;

            // The reconcile may have added, removed or reused block views.
            Document?.Geometry.Invalidate();
            reconcile = Stopwatch.GetElapsedTime(start);
        }

        start = Stopwatch.GetTimestamp();
        _root.UpdateLayout();
        return (projection, reconcile, Stopwatch.GetElapsedTime(start));
    }

    /// <summary>
    /// The element under a point that shows a hover flyout (10, Q29): the element at the hit position,
    /// if the point lies on its text.
    /// </summary>
    private string? HoverAt(LayoutPoint point)
    {
        var hit = Layout.HitTest(point);
        if (HoverTargets.At(State, hit) is not { } target
            || !HoverTargets.TryParse(target, out var block, out var range)
            || CurrentProjection?.Map[block] is not { } map
            || Document?.Geometry.Find(block) is not MdTextBlockView { TextLayout: { } layout } view)
        {
            return null;
        }

        var origin = Document.Geometry.TextOrigin(view);
        var local = new Point(point.X - origin.X, point.Y - origin.Y);
        var regions = layout.Regions(map.ToDisplay(range.Start, Affinity.Forward), map.ToDisplay(range.End, Affinity.Backward));
        return regions.Any(region => region.Contains(local)) ? target : null;
    }

    private void SetHover(string? target)
    {
        if (HasDocument && State.Interaction.HoverTarget != target)
        {
            Execute(new SetInteraction(State.Interaction with { HoverTarget = target }));
        }
    }

    /// <summary>
    /// A press on an adornment that runs a command of its own (10): a task checkbox, a footnote
    /// backlink (Q72), a TOC entry. Returns whether one was hit.
    /// </summary>
    private bool ClickAdornment(Point page)
    {
        if (Document?.Geometry is not { } geometry)
        {
            return false;
        }

        foreach (var view in geometry.Realized)
        {
            if (geometry.IsDetached(view))
            {
                continue;
            }

            var bounds = geometry.Bounds(view);
            if (!bounds.Contains(page))
            {
                continue;
            }

            var local = new Point(page.X - bounds.X, page.Y - bounds.Y);
            switch (view)
            {
                case MdListItem { CheckboxBounds: { } box } item when box.Contains(local) && BlockGeometry.ParseKey(item.BlockId) is { } id:
                    Execute(new ToggleTaskItem(id));
                    return true;
                case MdDefinitionSlot slot when slot.BacklinkAt(local) is { } index && Footnote(slot) is { } label:
                    Execute(new GoToFootnoteReference(label, index));
                    return true;
                case MdToc toc when toc.EntryAt(local) is { } key && BlockGeometry.ParseKey(key) is { } heading:
                    Execute(new SetSelection(TextSelection.Caret(new TextPosition(heading, 0))));
                    return true;
            }
        }

        return false;
    }

    /// <summary>The label of the footnote definition a slot belongs to.</summary>
    private string? Footnote(MdDefinitionSlot slot) =>
        BlockGeometry.ParseKey(slot.BlockId) is { } id
            && State.Document.Children.OfType<FootnoteDefinition>().FirstOrDefault(definition => definition.Children.Any(child => child.Id == id)) is { } definition
            ? definition.Name.Text
            : null;

    /// <summary>A table toolbar action (Q76, Q77): a command, or "copy table" when null; the caret stays in the editor.</summary>
    public async Task RunTableActionAsync(Command? command)
    {
        if (!HasDocument)
        {
            return;
        }

        if (command is null)
        {
            if (Session.CopyTable() is { } table)
            {
                await Clipboard.SetAsync(table);
            }
        }
        else
        {
            Execute(command);
        }

        Focus(FocusState.Programmatic);
    }

    private void OnRenderReady(BlockId block)
    {
        if (Document?.Geometry.Find(block) is MdTextBlockView view)
        {
            view.RefreshContent();
        }
    }

    /// <summary>The grammars loaded in the background: the code blocks shown plain meanwhile color now.</summary>
    private void OnGrammarsLoaded()
    {
        foreach (var view in Document?.Geometry.Realized.OfType<MdCodeBlock>() ?? [])
        {
            view.RefreshContent();
        }
    }

    /// <summary>Connects the controller to its text service, once per editable control.</summary>
    private void AttachTextService() => AttachTextService(previousKnownText: null, alreadyFocused: false);

    private void AttachTextService(string? previousKnownText, bool alreadyFocused)
    {
        if (_isReadOnly || _textInput is null || _textInput.IsAttached)
        {
            return;
        }

        try
        {
            if (TextServiceFactory is { } factory)
            {
                if (_ownedTextService is not null)
                {
                    _ownedTextService.Detach();
                    _ownedTextService = null;
                }

                var service = factory(_textInput);
                if (!_textInput.IsAttached)
                {
                    _textInput.Attach(service);
                }
            }
            else if (_ownedTextService is { } owned)
            {
                owned.SetController(_textInput);
                _textInput.Attach(owned, previousKnownText, alreadyFocused);
            }
            else
            {
                _ownedTextService = CoreTextServiceContext.Create(_textInput);
            }

            TextServiceError = null;
            if (!_textInput.IsFocused && FocusState != FocusState.Unfocused)
            {
                _textInput.FocusEnter();
            }
        }
        catch (Exception exception)
        {
            TextServiceError = $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    private void DetachTextService()
    {
        _pendingViewUpdate = false;
        _pendingHighlightingSync = false;
        _pendingScrollIntoView = false;
        _pendingSearchSync = false;
        _pendingChanged = false;
        _textInput?.Detach();
        _ownedTextService?.Detach();
        _ownedTextService = null;
    }

    /// <summary>
    /// The caret (from the start of the composition on the same line, so the candidate window stays
    /// put) and this control, in physical screen pixels (07, rule 6).
    /// </summary>
    private TextInputLayoutBounds? TextInputBounds()
    {
        if (!HasDocument || XamlRoot is null || Document?.Page is not { } page)
        {
            return null;
        }

        var caret = Layout.GetCaretRect(State.Selection.Head);
        if (State.Composition is { } composition && DocumentLookup.Find(State.Document, composition.Block) is not null)
        {
            var start = Layout.GetCaretRect(new TextPosition(composition.Block, composition.Start));
            if (Math.Abs(start.Y - caret.Y) < 0.5 && start.X <= caret.X)
            {
                caret = new LayoutRect(start.X, start.Y, Math.Max(1, caret.X - start.X), caret.Height);
            }
        }

        double scale = XamlRoot.RasterizationScale;
        var origin = ClientOrigin();
        var caretTopLeft = page.TransformToVisual(null).TransformPoint(new Point(caret.X, caret.Y));
        var controlTopLeft = TransformToVisual(null).TransformPoint(default);
        return new TextInputLayoutBounds(
            new Rect(origin.X + caretTopLeft.X * scale, origin.Y + caretTopLeft.Y * scale, Math.Max(1, caret.Width) * scale, caret.Height * scale),
            new Rect(origin.X + controlTopLeft.X * scale, origin.Y + controlTopLeft.Y * scale, ActualWidth * scale, ActualHeight * scale));
    }

    private Point ClientOrigin()
    {
        if (XamlRoot?.ContentIslandEnvironment is not { } environment)
        {
            return default;
        }

        var point = new NativePoint();
        ClientToScreen(Microsoft.UI.Win32Interop.GetWindowFromWindowId(environment.AppWindowId), ref point);
        return new Point(point.X, point.Y);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hwnd, ref NativePoint point);

    private static KeyModifiers CurrentModifiers()
    {
        static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        return (Down(VirtualKey.Control) ? KeyModifiers.Ctrl : KeyModifiers.None)
            | (Down(VirtualKey.Shift) ? KeyModifiers.Shift : KeyModifiers.None)
            | (Down(VirtualKey.Menu) ? KeyModifiers.Alt : KeyModifiers.None);
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDoubleClickTime();

    private void EnsureCurrent()
    {
        if (_session is not null && !ReferenceEquals(_projected, _session.State))
        {
            Refresh();
        }
    }

    private LayoutPoint ToPage(Point point)
    {
        var page = Document?.Page;
        var mapped = page is null ? point : TransformToVisual(page).TransformPoint(point);
        return new LayoutPoint(mapped.X, mapped.Y);
    }

    private void Run(IEnumerable<Command> commands)
    {
        foreach (var command in commands)
        {
            Execute(command);
        }
    }

    private void SetFocus(bool focused)
    {
        if (HasDocument && State.Interaction.HasFocus != focused)
        {
            Session.Execute(new SetInteraction(State.Interaction with { HasFocus = focused }));
            Refresh();
        }
    }

    private Point CaretPoint()
    {
        var caret = Layout.GetCaretRect(State.Selection.Head);
        return Document?.Page is { } page
            ? page.TransformToVisual(this).TransformPoint(new Point(caret.X, caret.Y + caret.Height))
            : default;
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (IsReadOnly)
        {
            return;
        }

        Focus(FocusState.Pointer);
        HandleContextRequested(e.TryGetPosition(this, out var point) ? point : null);
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e) => e.Handled = !IsReadOnly && HandleKey((int)e.Key, CurrentModifiers());

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        var modifiers = CurrentModifiers();

        // Ctrl alone makes control characters; Ctrl+Alt is AltGr, which types characters.
        if (!IsReadOnly && (!modifiers.HasFlag(KeyModifiers.Ctrl) || modifiers.HasFlag(KeyModifiers.Alt)))
        {
            e.Handled = HandleCharacter(e.Character);
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (IsReadOnly || !point.Properties.IsLeftButtonPressed || IsInControl(e.OriginalSource as DependencyObject))
        {
            return;
        }

        Focus(FocusState.Pointer);
        CapturePointer(e.Pointer);
        HandlePointerPressed(point.Position, Modifiers(e.KeyModifiers));
        e.Handled = true;
    }

    /// <summary>Toggles the task of a list item: its native checkbox was clicked (10).</summary>
    internal void ToggleTask(string blockKey)
    {
        if (BlockGeometry.ParseKey(blockKey) is { } id)
        {
            Execute(new ToggleTaskItem(id));
        }
    }

    /// <summary>
    /// Whether a press starts inside a control that handles the pointer itself, such as the scroll bar of a
    /// wide formula: the editor leaves it alone, or capturing the pointer would end the control's drag.
    /// </summary>
    internal bool IsInControl(DependencyObject? source)
    {
        for (var element = source; element is not null && !ReferenceEquals(element, this); element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
        {
            if (element is Microsoft.UI.Xaml.Controls.Primitives.ScrollBar or Microsoft.UI.Xaml.Controls.Primitives.ButtonBase or TextBox)
            {
                return true;
            }
        }

        return false;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_gestures.IsDown)
        {
            HandlePointerMoved(e.GetCurrentPoint(this).Position, Modifiers(e.KeyModifiers));
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_gestures.IsDown)
        {
            HandlePointerReleased(e.GetCurrentPoint(this).Position, Modifiers(e.KeyModifiers));
            ReleasePointerCapture(e.Pointer);
            UpdateCursor(e.GetCurrentPoint(this).Position, e.OriginalSource as DependencyObject);
            e.Handled = true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private static KeyModifiers Modifiers(VirtualKeyModifiers modifiers) =>
        (modifiers.HasFlag(VirtualKeyModifiers.Control) ? KeyModifiers.Ctrl : KeyModifiers.None)
        | (modifiers.HasFlag(VirtualKeyModifiers.Shift) ? KeyModifiers.Shift : KeyModifiers.None)
        | (modifiers.HasFlag(VirtualKeyModifiers.Menu) ? KeyModifiers.Alt : KeyModifiers.None);
}
