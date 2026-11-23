using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Quill.Editor.WinUI.Rendering;
using Quill.Editor.WinUI.Rendering.Diagrams;
using Quill.Editor.WinUI.Rendering.Formulas;
using Quill.Editor.WinUI.Text;
using Quill.Highlighting;
using RaTeX;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A fenced or indented code block: monospace source on the code background, colored in the language
/// its info string names (M6.3, Q87: TextMateSharp, Light+ or Dark+ by the style sheet's
/// <see cref="Theming.MdStyleSheet.CodeTheme"/>). Without a language, a grammar for it, or a code theme
/// (high contrast) the code shows in the plain foreground.
/// A <c>mermaid</c> block shows its diagram (mermaid-rs-renderer, rendered in the background), and with
/// <see cref="MdViewOptions.CodeBlockMath"/> a <c>math</c> block its formula (RaTeX, Q113): only the
/// rendering while not focused; focused, the source with the rendering kept below it (10). Until the
/// first diagram arrives, or when the source does not render, it shows as code with the message below.
/// With <see cref="MdViewOptions.CodeLineNumbers"/> a gutter at the left numbers the lines shown (each
/// logical line on its first visual line); it is part of the padding, so it is not text and a click in it
/// lands at the start of its line. The source layout is kept either way, so the layout service can
/// always query it.
/// </summary>
public partial class MdCodeBlock : MdRawTextView
{
    /// <summary>The room between the line numbers and the code.</summary>
    private const float GutterGap = 12;

    private string _info = string.Empty;
    private CodeHighlighting? _highlighting;
    private IDisposable? _grammarLease;
    private MermaidDiagram? _diagram;
    private DisplayList? _formula;
    private string? _diagramSource;
    private bool _diagramDark;
    private bool _isFenceActive;
    private CanvasTextLayout? _errorLayout;
    private int[]? _lineStarts;
    private string? _lineStartsText;

    /// <summary>The info string of the fence: its language.</summary>
    public string Info
    {
        get => _info;
        set
        {
            value ??= string.Empty;
            if (_info != value)
            {
                _info = value;
                InvalidateText();
            }
        }
    }

    /// <summary>The number of colored spans the text was last built with, for tests.</summary>
    internal int ColoredSpans { get; private set; }

    /// <summary>The diagram shown (the last one rendered for this block while a newer one renders), or null.</summary>
    internal MermaidDiagram? Diagram => _diagram;

    /// <summary>The formula of a math block shown (<see cref="MdViewOptions.CodeBlockMath"/>), or null.</summary>
    internal DisplayList? Formula => _formula;

    /// <summary>The message of a diagram or formula that does not render, or null.</summary>
    internal string? DiagramError { get; private set; }

    /// <summary>Whether only the rendering shows: the code block not active and a diagram or formula to show.</summary>
    internal bool ShowsDiagramOnly => HasRendering && !ShowsSource;

    /// <summary>The line numbers drawn and their baselines in the block, for tests.</summary>
    internal Dictionary<int, float> DrawnLineNumbers { get; } = [];

    /// <summary>Whether the gutter with line numbers is shown, for tests.</summary>
    internal bool ShowsLineNumbers => NumbersLines && !ShowsDiagramOnly;

    /// <summary>The caret is in the code block's language (Q91): the code shows as when focused.</summary>
    internal bool IsFenceActive
    {
        get => _isFenceActive;
        set
        {
            if (_isFenceActive != value)
            {
                _isFenceActive = value;
                OnFocusChanged();
            }
        }
    }

    /// <summary>Whether the source shows (a rendering below it): focused, or the language being edited.</summary>
    private bool ShowsSource => IsFocused || _isFenceActive;

    /// <summary>The info string the colors follow: the fence's own, by default.</summary>
    private protected virtual string HighlightInfo => _info;

    /// <summary>Whether this kind of block can number its lines (front matter does not).</summary>
    private protected virtual bool NumbersLines => ViewOptions.CodeLineNumbers;

    private bool IsMermaid => LanguageMap.LanguageOf(_info) == "mermaid";

    private bool IsMath => ViewOptions.CodeBlockMath && LanguageMap.LanguageOf(_info) == "math";

    private bool HasRendering => _diagram is not null || _formula is not null;

    /// <summary>The size of the rendering before it is scaled to the column.</summary>
    private Size RenderingSize => _diagram is { } diagram ? new Size(diagram.Width, diagram.Height)
        : _formula is { } formula ? new Size(formula.Width * FormulaSize, formula.TotalHeight * FormulaSize)
        : default;

    private float FormulaSize => Sheet.FontSize * Sheet.MathScale;

    private protected override bool ScrollsText => !ViewOptions.WrapCode && !ShowsDiagramOnly;

    private protected override double ScrollBarBottom(Size finalSize) => !HasRendering && _errorLayout is null ? finalSize.Height : CodeHeight();

    private protected override BlockTextFormat Format() => base.Format() with { Wrap = ViewOptions.WrapCode };

    private protected override TextSource BuildSource()
    {
        UpdateRendering();
        var source = Highlight() ?? base.BuildSource();
        ColoredSpans = source.Spans.Length;
        return source;
    }

    /// <summary>The padding of the text: the block's own, with the gutter at the left when lines are numbered.</summary>
    protected sealed override Thickness Padding()
    {
        var padding = BlockPadding();
        return NumbersLines ? new Thickness(padding.Left + GutterWidth(), padding.Top, padding.Right, padding.Bottom) : padding;
    }

    /// <summary>The padding of the block, without the gutter.</summary>
    private protected virtual Thickness BlockPadding() => Sheet.CodeBlockPadding;

    /// <summary>The background of the code (front matter has its own).</summary>
    private protected virtual Windows.UI.Color CodeBackground => Sheet.CodeBlockBackground;

    protected override void OnFocusChanged()
    {
        if (!IsFocused)
        {
            ReleaseEditingHighlighter();
        }

        InvalidateText();
    }

    protected override Size MeasureBlock(Size availableSize)
    {
        var size = base.MeasureBlock(availableSize);
        if (!HasRendering)
        {
            return ErrorLayout(size.Width) is { } error ? new Size(size.Width, size.Height + error.LayoutBounds.Height + BlockPadding().Bottom) : size;
        }

        var padding = BlockPadding();
        var natural = RenderingSize;
        double inner = double.IsInfinity(availableSize.Width) ? natural.Width : Math.Max(1, size.Width - padding.Left - padding.Right);
        double height = natural.Height * RenderingScale(natural, inner) + padding.Top + padding.Bottom;
        double width = double.IsInfinity(availableSize.Width) ? Math.Max(size.Width, natural.Width + padding.Left + padding.Right) : size.Width;
        return new Size(width, ShowsSource ? size.Height + height : height);
    }

    protected override void DrawBlock(CanvasDrawingSession session, Rect region)
    {
        if (!ShowsDiagramOnly)
        {
            base.DrawBlock(session, region);
            if (NumbersLines)
            {
                DrawLineNumbers(session, region);
            }
        }

        if (!HasRendering)
        {
            if (_errorLayout is { } error)
            {
                session.DrawTextLayout(error, new Vector2((float)BlockPadding().Left, (float)(CodeHeight() + BlockPadding().Bottom / 2)), Sheet.WarningForeground);
            }

            return;
        }

        // Centered below the source (focused) or alone, scaled down to the column when wider.
        var padding = BlockPadding();
        var natural = RenderingSize;
        double inner = Math.Max(1, EffectiveWidth - padding.Left - padding.Right);
        float scale = (float)RenderingScale(natural, inner);
        float top = (float)((ShowsDiagramOnly ? 0 : CodeHeight()) + padding.Top);
        float left = (float)(padding.Left + (inner - natural.Width * scale) / 2);
        var transform = session.Transform;
        session.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(left, top) * transform;
        if (_diagram is { } diagram)
        {
            session.DrawSvg(diagram.Document, natural);
        }
        else if (_formula is { } formula)
        {
            MathPainter.Draw(session, formula, Vector2.Zero, FormulaSize, Context.Foreground, Sheet.FontFamily);
        }

        session.Transform = transform;

        // Selected while shown as a rendering (05: rendered objects have their own selected look).
        if (ShowsDiagramOnly && (IsSelected || SelectionEnd > SelectionStart))
        {
            session.FillRectangle(new Rect(left, top, natural.Width * scale, natural.Height * scale), Sheet.BlockSelection);
        }
    }

    protected override void DrawBackground(CanvasDrawingSession session, Size size)
    {
        if (!ShowsDiagramOnly)
        {
            var box = new Rect(0, 0, size.Width, !HasRendering && _errorLayout is null ? size.Height : CodeHeight());
            CodeBox.Draw(session, box, CodeBackground, Sheet);
        }
    }

    /// <summary>The width of the gutter: the digits of the last line number (two at least, so it does not grow at the tenth line) and the gap.</summary>
    private double GutterWidth()
    {
        int digits = Math.Max(2, LineStarts().Length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        using var probe = CreateLabel(new string('0', digits), Format().FontSize, monospace: true);
        return Math.Ceiling(probe.LayoutBounds.Width) + GutterGap;
    }

    /// <summary>Numbers the logical lines in the region, right-aligned in the gutter on their first visual line's baseline.</summary>
    private void DrawLineNumbers(CanvasDrawingSession session, Rect region)
    {
        if (TextLayout is not { } layout)
        {
            return;
        }

        var padding = Padding();
        var starts = LineStarts();
        float size = Format().FontSize;
        for (int index = layout.LineIndexAt(region.Top - padding.Top); index < layout.LineCount; index++)
        {
            var (start, _, top, height) = layout.Line(index);
            if (padding.Top + top > region.Bottom)
            {
                break;
            }

            int line = Array.BinarySearch(starts, start);
            if (line < 0)
            {
                // A wrapped continuation: no number of its own.
                continue;
            }

            using var label = CreateLabel((line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), size, monospace: true);
            float labelBaseline = label.LineMetrics.Length > 0 ? label.LineMetrics[0].Baseline : (float)label.LayoutBounds.Height;
            // The line's baseline is from the line's own top (the empty last line has no character to ask).
            float baseline = top + (height + labelBaseline) / 2;
            if (start < Text.Length)
            {
                var (lineTop, lineBaseline, _) = layout.LineOf(start);
                baseline = lineTop + lineBaseline;
            }

            float x = (float)(padding.Left - GutterGap - label.LayoutBounds.Width);
            var origin = new Vector2(x, (float)padding.Top + baseline - labelBaseline);
            session.DrawTextLayout(label, origin, Sheet.CodeLineNumberForeground);
            DrawnLineNumbers[line + 1] = origin.Y + labelBaseline;
        }
    }

    /// <summary>The offsets where the logical lines of the text start.</summary>
    private int[] LineStarts()
    {
        if (_lineStarts is null || !ReferenceEquals(_lineStartsText, Text))
        {
            var starts = new List<int> { 0 };
            for (int i = Text.IndexOf('\n'); i >= 0; i = Text.IndexOf('\n', i + 1))
            {
                starts.Add(i + 1);
            }

            _lineStarts = [.. starts];
            _lineStartsText = Text;
        }

        return _lineStarts;
    }

    /// <summary>The message of a source that does not render, laid out at the block width; null when there is none.</summary>
    private CanvasTextLayout? ErrorLayout(double width)
    {
        _errorLayout?.Dispose();
        _errorLayout = null;
        if (DiagramError is not { } message)
        {
            return null;
        }

        var padding = BlockPadding();
        using var format = new CanvasTextFormat { FontFamily = Sheet.FontFamily, FontSize = Sheet.FontSize * 0.85f };
        _errorLayout = new CanvasTextLayout(CanvasDevice.GetSharedDevice(), message, format, (float)Math.Max(1, width - padding.Left - padding.Right), 10000);
        return _errorLayout;
    }

    private static double RenderingScale(Size natural, double width) => natural.Width <= 0 ? 1 : Math.Min(1, width / natural.Width);

    /// <summary>The height of the source part: the text and its padding.</summary>
    private double CodeHeight()
    {
        var padding = Padding();
        return (TextLayout?.Height ?? 0) + padding.Top + padding.Bottom + ScrollBarHeight;
    }

    private TextSource? Highlight()
    {
        string info = HighlightInfo;
        if (Sheet.CodeTheme is not { } theme || info.Length == 0)
        {
            ReleaseEditingHighlighter();
            return null;
        }

        if (!IsFocused)
        {
            ReleaseEditingHighlighter();
            return CodeHighlighting.BuildOnce(theme, info, Text);
        }

        _grammarLease ??= GrammarCatalog.AcquireLease();
        EditorMemoryTrimmer.NotifyGrammarUsed();
        if (_highlighting is null || _highlighting.Theme != theme || _highlighting.Info != info)
        {
            _highlighting = CodeHighlighting.Create(theme, info);
        }

        return _highlighting?.Build(Text);
    }

    private void ReleaseEditingHighlighter()
    {
        _highlighting = null;
        if (_grammarLease is not null)
        {
            _grammarLease.Dispose();
            _grammarLease = null;
            EditorMemoryTrimmer.NotifyGrammarUsed();
        }
    }

    private void UpdateRendering()
    {
        if (IsMath)
        {
            ForgetDiagram();
            _formula = MathTypesetter.Typeset(Text, display: true, out string? error);
            DiagramError = error;
            return;
        }

        _formula = null;
        UpdateDiagram();
    }

    private void ForgetDiagram()
    {
        _diagram = null;
        _diagramSource = null;
        DiagramError = null;
    }

    protected override void OnBlockPooled()
    {
        ReleaseEditingHighlighter();
        _errorLayout?.Dispose();
        _errorLayout = null;
        _diagram = null;
        _formula = null;
        _diagramSource = null;
        DiagramError = null;
        base.OnBlockPooled();
    }

    protected override void OnBlockUnmounted()
    {
        _errorLayout?.Dispose();
        _errorLayout = null;
        ReleaseEditingHighlighter();
        _diagram = null;
        _formula = null;
        _diagramSource = null;
        base.OnBlockUnmounted();
    }

    /// <summary>Takes the diagram of the current source, asking for it when it is not rendered yet.</summary>
    private void UpdateDiagram()
    {
        if (!IsMermaid || Context.Images?.Cache is not { } cache || Layout.BlockGeometry.ParseKey(BlockId) is not { } id)
        {
            ForgetDiagram();
            return;
        }

        bool dark = Sheet.CodeTheme == HighlightTheme.Dark;
        switch (MermaidDiagrams.Get(cache, Text, dark, id))
        {
            case MermaidDiagram diagram:
                // The diagram this block showed before is not asked for again: let it go.
                if (_diagramSource is { } previous && previous != Text)
                {
                    cache.Forget(MermaidDiagrams.KindOf(dark), previous);
                }

                _diagram = diagram;
                _diagramSource = Text;
                _diagramDark = dark;
                DiagramError = null;
                break;
            case MermaidFailure failure:
                _diagram = null;
                DiagramError = failure.Message;
                break;
            default:
                // Rendering: the last diagram stays meanwhile, unless it is of the other theme.
                if (_diagramDark != dark)
                {
                    _diagram = null;
                }

                break;
        }
    }
}
