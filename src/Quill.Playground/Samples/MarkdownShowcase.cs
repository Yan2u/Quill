namespace Quill.Playground.Samples;

/// <summary>A document with every element the editor renders, for the Editor page and the rendering probes.</summary>
internal static class MarkdownShowcase
{
    public const string Text = """
        ---
        title: Quill showcase
        tags: [editor, preview]
        ---

        # Quill showcase

        A paragraph with **bold**, *italic*, ***both***, ~~strikethrough~~, ==highlight==, `inline code`, H~2~O and x^2^,
        a [link](https://example.com "Example"), an autolink <https://commonmark.org>, <kbd>Ctrl</kbd>+<kbd>S</kbd>, <u>underline</u>,
        an entity &copy;, an emoji :smile:, inline math $e^{i\pi} + 1 = 0$ and a footnote reference[^1].
        A soft break follows
        on the next line, and a hard break
        ends this line. 中文与 English 混排，标点“引号”与 emoji 👍🏽 也在这里。

        ## Lists

        - First item
        - Second item with a longer text that wraps onto a second line when the window is narrow enough to wrap it
          - Nested item
            - Third level
        - [ ] Open task
        - [x] Done task

        1. One
        2. Two

           A headless paragraph inside the second item.
        3. Three

        ### Quote and code

        > A block quote with **strong** text.
        >
        > > A nested quote.

        ```csharp
        // A fenced code block.
        static int Add(int a, int b) => a + b;
        ```

        $$
        \int_0^1 x^2 \, dx = \frac{1}{3}
        $$

        | Left | Center | Right |
        | :--- | :----: | ----: |
        | a | b | c |
        | a longer cell that wraps | `code` | **3** |

        ---

        [toc]

        <div class="note">An HTML block stays source.</div>

        ![An image](images/sample.png)

        [^1]: The footnote text with *emphasis*.

        [quill]: https://example.com/quill "Quill"
        """;
}
