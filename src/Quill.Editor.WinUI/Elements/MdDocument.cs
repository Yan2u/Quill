using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Quill.Editor.Projection;
using Quill.Editor.WinUI.Layout;
using Quill.Editor.WinUI.Localization;
using Quill.Editor.WinUI.Theming;
using Windows.UI.ViewManagement;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// The root of the projected tree: a scrolling page with the content column (page slots of the
/// style sheet) holding the document body, a <c>vx:VirtualStack</c> of the top-level blocks. The
/// style sheet follows the app theme (light, dark, high contrast) unless set explicitly; switching
/// it relays out and redraws the realized blocks and never re-projects (05).
/// </summary>
public sealed partial class MdDocument : UserControl, Quill.VariableXaml.IVirtualLifecycle
{
    private IMatchSource? _matches;
    private Rendering.SourceHighlighting? _highlighting;
    private bool _isSource;
    private readonly ScrollViewer _scroller;
    private readonly Grid _page;
    private readonly CaretLayer _caret = new();
    private readonly AdornerLayer _adorners = new();
    private readonly EventHandler<object> _onLayout;
    private UISettings? _settings;
    private HoverInfo? _hover;
    private Rendering.ImageLoader? _images;
    private BlockGeometry? _geometry;
    private SelectionInfo? _selection;
    private MdStyleSheet? _styleSheet;
    private MdStyleSheet _lightStyleSheet = MdStyleSheet.Light;
    private MdStyleSheet _darkStyleSheet = MdStyleSheet.Dark;
    private MdViewOptions _viewOptions = MdViewOptions.Default;
    private MdStyleSheet? _highContrast;
    private MdStyleSheet _effective = MdStyleSheet.Light;
    private UIElement? _body;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _scrollSettleTimer;

    public MdDocument()
    {
        IsTabStop = false;
        _page = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        _scroller = new ScrollViewer
        {
            Content = _page,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsTabStop = false,
        };
        _scroller.ViewChanged += OnScrollerViewChanged;
        Content = _scroller;
        _page.Children.Add(_adorners);
        _page.Children.Add(_caret);

        // LayoutUpdated fires after every layout pass in the window and keeps its handlers alive:
        // listen only while this document is in the tree, or replaced documents pile up.
        _onLayout = (_, _) => UpdateOverlays();
        Loaded += (_, _) => OnDocumentLoaded();
        Unloaded += (_, _) => OnDocumentUnloaded();
        ActualThemeChanged += (_, _) => ApplyStyleSheet();
        ApplyStyleSheet();
    }

    void Quill.VariableXaml.IVirtualLifecycle.OnUnmounted()
    {
        _scroller.ViewChanged -= OnScrollerViewChanged;
        StopScrollSettleTimer();
        IsFastScrolling = false;
        OnDocumentUnloaded();
        Body = null;
        _caret.Release();
        _adorners.Release();
        _images = null;
        _highlighting = null;
        _matches = null;
        _selection = null;
        _hover = null;
        _geometry?.Invalidate();
    }

    private void OnDocumentLoaded()
    {
        _page.LayoutUpdated -= _onLayout;
        _page.LayoutUpdated += _onLayout;
        if (_settings is null)
        {
            _settings = new UISettings();
            // High contrast changes the system colors; the event comes on a background thread.
            _settings.ColorValuesChanged += OnColorValuesChanged;
        }

        UiStrings.LanguageChanged -= OnLanguageChanged;
        UiStrings.LanguageChanged += OnLanguageChanged;
    }

    private void OnDocumentUnloaded()
    {
        StopScrollSettleTimer();
        IsFastScrolling = false;
        UiStrings.LanguageChanged -= OnLanguageChanged;
        _page.LayoutUpdated -= _onLayout;
        if (_settings is not null)
        {
            _settings.ColorValuesChanged -= OnColorValuesChanged;
            _settings = null;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue is { } queue && !queue.HasThreadAccess)
        {
            queue.TryEnqueue(RefreshLanguage);
            return;
        }

        RefreshLanguage();
    }

    public void RefreshLanguage()
    {
        _adorners.RefreshLanguage();
        RefreshBlockLanguage(_page);
        _adorners.Update(Geometry, _hover, _effective);
    }

    private void OnColorValuesChanged(UISettings sender, object args) => DispatcherQueue?.TryEnqueue(() =>
    {
        _highContrast = null;
        ApplyStyleSheet();
    });

    /// <summary>The document body: the virtual stack of top-level blocks.</summary>
    public UIElement? Body
    {
        get => _body;
        set
        {
            if (ReferenceEquals(_body, value))
            {
                return;
            }

            if (_body is not null)
            {
                _page.Children.Remove(_body);
            }

            if (_body is ItemsRepeater old)
            {
                old.ElementPrepared -= OnElementPrepared;
                old.ElementClearing -= OnElementClearing;
                old.ElementIndexChanged -= OnElementIndexChanged;
                if (old.Layout is BlockStackLayout oldBlocks)
                {
                    oldBlocks.Gap = static (_, _) => 0;
                    oldBlocks.Estimate = static (_, _) => 24;
                    oldBlocks.ClearCache();
                }
            }

            if (value is ItemsRepeater repeater)
            {
                // Limit off-screen over-realization so rapid scrolling does not force-measure and allocate
                // several extra viewports of blocks in a single frame.
                repeater.VerticalCacheLength = 0.5;
                repeater.HorizontalCacheLength = 0.0;

                // The realized blocks change as the stack realizes, recycles and moves elements.
                repeater.ElementPrepared += OnElementPrepared;
                repeater.ElementClearing += OnElementClearing;
                repeater.ElementIndexChanged += OnElementIndexChanged;
            }

            Geometry.Invalidate();
            _body = value;
            if (value is not null)
            {
                // Below the caret layer, which stays on top.
                _page.Children.Insert(0, value);
            }

            ApplySpacing();
        }
    }

    /// <summary>The selection as projected; the caret overlay draws it after layout (M5.2).</summary>
    public SelectionInfo? Selection
    {
        get => _selection;
        set
        {
            _selection = value;
            _caret.Update(Geometry, value, _effective);
        }
    }

    /// <summary>The hover flyout as projected (05: an attached node of the view).</summary>
    public HoverInfo? Hover
    {
        get => _hover;
        set
        {
            _hover = value;
            _adorners.Update(Geometry, value, _effective);
        }
    }

    /// <summary>Loads the document's images; null shows every image as its placeholder.</summary>
    internal Rendering.ImageLoader? Images
    {
        get => _images;
        set
        {
            _images = value;
            InvalidateBlocks(_page);
        }
    }

    /// <summary>The highlighting of a source document (11-source-mode.md); null otherwise.</summary>
    internal Rendering.SourceHighlighting? Highlighting
    {
        get => _highlighting;
        set
        {
            _highlighting = value;
            InvalidateBlocks(_page);
        }
    }

    /// <summary>The highlighting the lines use: none in high contrast, where the system colors apply (Q85).</summary>
    internal Rendering.SourceHighlighting? EffectiveHighlighting => ReferenceEquals(_effective, _highContrast) ? null : _highlighting;

    /// <summary>The find matches the blocks draw (12-find-replace.md); null for none.</summary>
    public IMatchSource? Matches
    {
        get => _matches;
        set
        {
            _matches = value;
            InvalidateBlocks(_page);
        }
    }

    /// <summary>Draws the realized text blocks again after the matches changed; nothing is laid out again.</summary>
    public void RedrawMatches() => RedrawMarks(_page);

    /// <summary>A source document (11-source-mode.md): its lines follow one another without block spacing.</summary>
    public bool IsSource
    {
        get => _isSource;
        set
        {
            if (_isSource != value)
            {
                _isSource = value;
                ApplySpacing();
            }
        }
    }

    /// <summary>The hover, heading badge and table toolbar overlay.</summary>
    internal AdornerLayer Adorners => _adorners;

    /// <summary>Synchronizes the caret and adorner overlays with the currently realized blocks.</summary>
    internal void UpdateOverlays()
    {
        Geometry.ClearDetached();
        _caret.Update(Geometry, _selection, _effective);
        _adorners.Update(Geometry, _hover, _effective);
    }

    /// <summary>Where the caret overlay draws the caret, in page coordinates; null when hidden.</summary>
    public Windows.Foundation.Rect? CaretBounds => _caret.CaretBounds;

    /// <summary>The realized blocks and their geometry, shared with the layout service.</summary>
    internal BlockGeometry Geometry => _geometry ??= new BlockGeometry(this);

    /// <summary>An explicit style sheet; null follows the app theme.</summary>
    public MdStyleSheet? StyleSheet
    {
        get => _styleSheet;
        set
        {
            _styleSheet = value;
            ApplyStyleSheet();
        }
    }

    /// <summary>The theme while the app is light (05: the "light theme" setting); any theme may be chosen.</summary>
    public MdStyleSheet LightStyleSheet
    {
        get => _lightStyleSheet;
        set
        {
            _lightStyleSheet = value;
            ApplyStyleSheet();
        }
    }

    /// <summary>The theme while the app is dark (the "dark theme" setting).</summary>
    public MdStyleSheet DarkStyleSheet
    {
        get => _darkStyleSheet;
        set
        {
            _darkStyleSheet = value;
            ApplyStyleSheet();
        }
    }

    /// <summary>The style sheet in use.</summary>
    public MdStyleSheet EffectiveStyleSheet => _effective;

    /// <summary>How the blocks show apart from the theme (code wrapping); a change lays the realized blocks out again.</summary>
    public MdViewOptions ViewOptions
    {
        get => _viewOptions;
        set
        {
            if (!Equals(value, _viewOptions))
            {
                _viewOptions = value;
                InvalidateBlocks(_page);
            }
        }
    }

    public ScrollViewer ScrollViewer => _scroller;

    /// <summary>True while an interactive intermediate scroll pass is in flight.</summary>
    internal bool IsFastScrolling { get; private set; }

    /// <summary>The scrolled content: page coordinates of the layout service are relative to it.</summary>
    internal Grid Page => _page;

    /// <summary>Raised after the effective style sheet changed and the realized blocks were invalidated.</summary>
    public event EventHandler? StyleSheetChanged;

    private void OnScrollerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate)
        {
            IsFastScrolling = true;
            StartScrollSettleTimer();
            return;
        }

        EndFastScrolling();
    }

    private void StartScrollSettleTimer()
    {
        if (DispatcherQueue is not { } queue)
        {
            return;
        }

        if (_scrollSettleTimer is null)
        {
            _scrollSettleTimer = queue.CreateTimer();
            _scrollSettleTimer.Interval = TimeSpan.FromMilliseconds(150);
            _scrollSettleTimer.IsRepeating = false;
            _scrollSettleTimer.Tick += OnScrollSettleTick;
        }

        _scrollSettleTimer.Start();
    }

    private void StopScrollSettleTimer()
    {
        if (_scrollSettleTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnScrollSettleTick;
            _scrollSettleTimer = null;
        }
    }

    private void OnScrollSettleTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) => EndFastScrolling();

    private void EndFastScrolling()
    {
        _scrollSettleTimer?.Stop();
        if (!IsFastScrolling)
        {
            return;
        }

        IsFastScrolling = false;
        if (_body is null)
        {
            return;
        }

        foreach (MdBlockView block in Geometry.Realized)
        {
            if (block.NeedsCrispRedrawAfterFastScroll)
            {
                block.RedrawCrispAfterFastScroll();
            }
        }
    }

    private MdStyleSheet Resolve() =>
        _styleSheet ?? (SystemColors.IsHighContrast() ? _highContrast ??= MdStyleSheet.HighContrast()
            : ActualTheme == ElementTheme.Dark ? _darkStyleSheet
            : _lightStyleSheet);

    private void OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args) => Geometry.Invalidate();

    private void OnElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args) => Geometry.Invalidate();

    private void OnElementIndexChanged(ItemsRepeater sender, ItemsRepeaterElementIndexChangedEventArgs args) => Geometry.Invalidate();

    private void ApplyStyleSheet()
    {
        var sheet = Resolve();
        bool changed = !ReferenceEquals(sheet, _effective);
        _effective = sheet;
        var background = new SolidColorBrush(sheet.PageBackground);
        _scroller.Background = background;
        _page.Background = background;
        _page.MaxWidth = sheet.ContentMaxWidth + 2 * sheet.PagePaddingHorizontal;
        _page.Padding = new Thickness(sheet.PagePaddingHorizontal, sheet.PagePaddingVertical, sheet.PagePaddingHorizontal, sheet.PagePaddingVertical);
        ApplySpacing();
        if (changed)
        {
            InvalidateBlocks(_page);
            StyleSheetChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplySpacing()
    {
        switch (_body)
        {
            case ItemsRepeater { Layout: BlockStackLayout blocks }:
                var sheet = _effective;
                blocks.Gap = _isSource ? static (_, _) => 0 : (upper, lower) => BlockMargins.Between(sheet, upper.Type.LocalName, lower.Type.LocalName);
                blocks.Estimate = (node, width) => BlockHeightEstimator.Estimate(node, width, _effective);
                break;
            case ItemsRepeater { Layout: StackLayout layout }:
                layout.Spacing = _effective.BlockSpacing;
                break;
        }
    }

    private static void RedrawMarks(DependencyObject element)
    {
        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is MdTextBlockView view)
            {
                view.RedrawMarks();
            }

            RedrawMarks(child);
        }
    }

    private static void InvalidateBlocks(DependencyObject element)
    {
        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is MdBlockView block)
            {
                block.InvalidateStyle();
            }

            InvalidateBlocks(child);
        }
    }

    private static void RefreshBlockLanguage(DependencyObject element)
    {
        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            switch (child)
            {
                case MdTextBlockView text:
                    text.RefreshContent();
                    break;
                case MdListItem item:
                    item.RefreshLanguage();
                    break;
                case MdBlockView block:
                    block.InvalidateStyle();
                    break;
            }

            RefreshBlockLanguage(child);
        }
    }
}
