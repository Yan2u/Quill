using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Theming;

namespace Quill.Shell.Themes;

/// <summary>
/// A document rendered with a theme (15-themes.md): a read-only editor showing a sample document in the
/// theme's style sheet, whatever the app's theme. Changing the sheet does not project the document again.
/// </summary>
public sealed partial class ThemePreview : UserControl
{
    /// <summary>The short sample of a theme card: a heading, a paragraph, a code block and a quote.</summary>
    public const string CardSample = "# Heading\n\nA paragraph with **bold**, *italic*, `code` and a [link](https://example.com).\n\n```js\nconst answer = 42;\n```\n\n> A quote, set apart from the text.";

    /// <summary>The sample of the configuration page: an element of each kind a theme styles.</summary>
    public const string FullSample = """
        # Heading 1

        A paragraph with **bold**, *italic*, ~~strikethrough~~, ==highlight==, `inline code`, <kbd>Ctrl</kbd>+<kbd>S</kbd>, a [link](https://example.com), a footnote[^1], $e^{i\pi}+1=0$ and H~2~O.

        ## Heading 2

        > A quote, set apart from the text.
        > It may take several lines.

        ### Heading 3

        - An item
        - Another item
          1. A numbered item
          2. And another
        - [x] A task that is done
        - [ ] A task to do

        #### Heading 4

        ```csharp
        // A code block
        var greeting = $"Hello, {name}!";
        Console.WriteLine(greeting);
        ```

        $$
        \int_0^1 x^2 \, dx = \frac{1}{3}
        $$

        | Column | Another column | Number |
        | ------ | -------------- | -----: |
        | A cell | Some text      |     42 |
        | Another | More text     |      7 |
        | Third  | Even more      |    128 |

        ---

        ##### Heading 5

        ###### Heading 6

        [^1]: The footnote's text.

        The end of the sample.
        """;

    private readonly MarkdownBox _box = new() { IsReadOnly = true };
    private readonly string _sample;
    private bool _loaded;

    public ThemePreview(string sample)
    {
        _sample = sample;
        Content = _box;
        Loaded += (_, _) =>
        {
            if (!_loaded)
            {
                _loaded = true;
                var ids = new SequentialBlockIdSource();
                var document = MarkdownImporter.Import(_sample, ids).Document;
                // The caret at the end of the last paragraph (plain text, nothing to activate), and hidden.
                _box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(LastCaret(document)) }, ids);
                _box.Execute(new Quill.Editor.Commands.SetInteraction(_box.State.Interaction with { HasFocus = false }));
            }
        };
    }

    /// <summary>The end of the last leaf of the sample (its last paragraph).</summary>
    private static TextPosition LastCaret(Document document)
    {
        Block block = document.Children[^1];
        while (block is ContainerBlock container && container.Children.Count > 0)
        {
            block = container.Children[^1];
        }

        return new TextPosition(block.Id, block is LeafBlock leaf ? leaf.Text.Length : 0);
    }

    /// <summary>The editor showing the sample, for tests.</summary>
    internal MarkdownBox Box => _box;

    /// <summary>The theme shown: in a light and in a dark app alike.</summary>
    public MdStyleSheet Sheet
    {
        get => _box.LightStyleSheet;
        set
        {
            _box.LightStyleSheet = value;
            _box.DarkStyleSheet = value;
        }
    }

    /// <summary>Whether the preview scrolls (the configuration page) or is cut at its height (cards).</summary>
    public bool CanScroll
    {
        set => IsHitTestVisible = value;
    }
}
