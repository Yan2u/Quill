using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Text;
using Quill.Editor.WinUI.Theming;
using Quill.Playground.Samples;

namespace Quill.Playground.SelfTest;

/// <summary>
/// The default themes (based on MIT-licensed Primer primitives and github-markdown-css): lines as tall
/// as the CSS line height, the rule under the first heading levels, table stripes and cell backgrounds,
/// collapsed margins between blocks, the native task checkbox (switching themes without projecting again
/// is checked by the render probes).
/// </summary>
internal static class ThemeProbes
{
    private const string Sample = "# Title\n\n###### Small\n\none line\n\n| a | b |\n| - | - |\n| 1 | 2 |\n| 3 | 4 |\n| 5 | 6 |";

    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var stageSize = context.Stage.XamlRoot.Size;
        var box = new MarkdownBox
        {
            Width = Math.Min(660, stageSize.Width),
            Height = Math.Min(480, stageSize.Height - 20),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        context.Stage.Children.Add(box);
        await Task.Delay(50);

        var family = CanvasFontSet.GetSystemFontSet().GetMatchingFonts(MdStyleSheet.Light.FontFamily, Microsoft.UI.Text.FontWeights.Normal, Windows.UI.Text.FontStretch.Normal, Windows.UI.Text.FontStyle.Normal);
        context.Check(family.Fonts.Count > 0, "theme: the body font is installed", MdStyleSheet.Light.FontFamily);

        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(Sample, ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(document)) }, ids);
        box.Document!.StyleSheet = MdStyleSheet.Light;
        await Task.Delay(80);
        CheckTheme(context, box, MdStyleSheet.Light, "light");

        box.Document.StyleSheet = MdStyleSheet.Dark;
        await Task.Delay(80);
        CheckTheme(context, box, MdStyleSheet.Dark, "dark");

        box.Document.StyleSheet = MdStyleSheet.Light;
        Load(box, "first\n\n# Heading\n\nsecond\n\n---\n\nthird\n\n- [ ] open\n- [x] done");
        await Task.Delay(80);
        CheckSpacing(context, box, MdStyleSheet.Light);
        await CheckTaskAsync(context, box);
        await CheckSpecDrawsAsync(context, box);
        await CheckStableLinesAsync(context, box);
        await CheckInlineCodePaddingAsync(context, box);
        await CheckThemeSettingsAsync(context, box);
        context.Stage.Children.Clear();
    }

    private static async Task CheckInlineCodePaddingAsync(SelfTestContext context, MarkdownBox box)
    {
        box.Document!.StyleSheet = MdStyleSheet.Light;
        Load(box, "Text with `Hblgpqy` and `parse_config` inline code.\n\n```csharp\nint x = 1;\n```");
        await Task.Delay(100);

        var paragraph = Realized(box).OfType<MdParagraph>().FirstOrDefault();
        var defaultBox = (paragraph?.TextLayout as BlockTextLayout)?.FirstCodeBox();
        double defaultTopGap = defaultBox is { } d0 ? (d0.BaselineY - d0.InkAbove) - d0.Box.Y : -1;
        double defaultBottomGap = defaultBox is { } d1 ? d1.Box.Bottom - (d1.BaselineY + d1.InkBelow) : -1;

        var custom = MdStyleSheet.Light with
        {
            CodePadding = new Thickness(4, 5, 4, 3),
            CodeForeground = Windows.UI.Color.FromArgb(0xFF, 0xC7, 0x25, 0x4E),
        };
        box.Document.StyleSheet = custom;
        await Task.Delay(80);
        paragraph = Realized(box).OfType<MdParagraph>().FirstOrDefault();
        var customBox = (paragraph?.TextLayout as BlockTextLayout)?.FirstCodeBox();
        double customTopGap = customBox is { } c0 ? (c0.BaselineY - c0.InkAbove) - c0.Box.Y : -1;
        double customBottomGap = customBox is { } c1 ? c1.Box.Bottom - (c1.BaselineY + c1.InkBelow) : -1;

        context.Check(defaultBox is not null
            && Math.Abs(defaultTopGap - MdStyleSheet.Light.CodePadding.Top) < 0.5
            && Math.Abs(defaultBottomGap - MdStyleSheet.Light.CodePadding.Bottom) < 0.5
            && Math.Abs(customTopGap - custom.CodePadding.Top) < 0.5
            && Math.Abs(customBottomGap - custom.CodePadding.Bottom) < 0.5,
            "theme: inline code box centers around glyph ink and honors CodePadding",
            $"default top/bottom=({defaultTopGap:F2},{defaultBottomGap:F2}), custom=({customTopGap:F2},{customBottomGap:F2})");
    }

    /// <summary>
    /// The editor uses the "light theme" while the app is light and the "dark theme" while it is dark (05,
    /// 14-settings.md); either may be any theme, and the choice stays across documents. No new projection.
    /// </summary>
    private static async Task CheckThemeSettingsAsync(SelfTestContext context, MarkdownBox box)
    {
        box.Document!.StyleSheet = null;
        box.RequestedTheme = ElementTheme.Light;
        await Task.Delay(60);
        context.Check(ReferenceEquals(box.Document.EffectiveStyleSheet, MdStyleSheet.Light), "theme: a light app uses the light theme, Github by default");

        var projection = box.CurrentProjection;
        box.LightStyleSheet = BuiltInThemes.GithubDarkDimmed.Sheet;
        await Task.Delay(60);
        context.Check(ReferenceEquals(box.Document.EffectiveStyleSheet, MdStyleSheet.Dark) && ReferenceEquals(projection, box.CurrentProjection),
            "theme: any theme can be the light theme; changing it does not project again");

        box.RequestedTheme = ElementTheme.Dark;
        box.DarkStyleSheet = BuiltInThemes.Github.Sheet;
        await Task.Delay(60);
        context.Check(ReferenceEquals(box.Document.EffectiveStyleSheet, MdStyleSheet.Light), "theme: a dark app uses the dark theme");

        Load(box, "another document");
        await Task.Delay(60);
        context.Check(ReferenceEquals(box.Document!.EffectiveStyleSheet, MdStyleSheet.Light), "theme: the chosen themes stay for the next document");

        box.LightStyleSheet = MdStyleSheet.Light;
        box.DarkStyleSheet = MdStyleSheet.Dark;
        box.RequestedTheme = ElementTheme.Default;
        await Task.Delay(60);
    }

    /// <summary>A real document (a spec with CJK text, lists, code spans) draws every realized block without errors.</summary>
    private static async Task CheckSpecDrawsAsync(SelfTestContext context, MarkdownBox box)
    {
        string path = Path.Combine(RepositoryPaths.Root ?? ".", ".agents", "specs", "07-input-and-ime.md");
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var sheet in new[] { MdStyleSheet.Light, MdStyleSheet.Dark })
        {
            box.Document!.StyleSheet = sheet;
            Load(box, File.ReadAllText(path));
            await Task.Delay(300);
            var views = Realized(box);
            var failed = views.Where(static view => view.LastDrawError is not null).ToList();
            var geometry = box.Document.Geometry;
            double visibleHeight = Math.Min(box.ActualHeight, context.Stage.ActualHeight);
            var undrawn = views.Where(view => view is MdTextBlockView && view.DrawCount == 0 && view.ActualHeight > 0 && geometry.Bounds(view).Bottom < visibleHeight).ToList();
            if (undrawn.Count > 0)
            {
                context.Info("undrawn: " + string.Join(", ", undrawn.Take(8).Select(view => $"{view.GetType().Name}@{geometry.Bounds(view).Y:0} h{view.ActualHeight:0} in {view.Parent?.GetType().Name}")));
            }
            context.Check(views.Count > 5 && failed.Count == 0 && undrawn.Count == 0, "theme: every block of a real document draws",
                $"{views.Count} blocks; errors: {string.Join(" | ", failed.Take(2).Select(static view => $"{view.GetType().Name}: {view.LastDrawError!.GetType().Name} {view.LastDrawError.Message}"))}; undrawn {undrawn.Count}");
        }
    }

    /// <summary>The caret entering a paragraph shows its markers (monospace): its lines must not move.</summary>
    private static async Task CheckStableLinesAsync(SelfTestContext context, MarkdownBox box)
    {
        box.Document!.StyleSheet = MdStyleSheet.Light;
        Load(box, "first\n\n中文段落，含有 **粗体**、`代码` 和 [链接](https://example.com)，足够长以便折成两行或更多行的文字内容，继续写一些文字。");
        await Task.Delay(100);
        var paragraph = Realized(box).OfType<MdParagraph>().ElementAtOrDefault(1);
        var before = (paragraph?.ActualHeight ?? 0, paragraph?.FirstBaseline ?? 0, paragraph?.TextLayout?.LineCount ?? 0);
        var second = box.State.Document.Children[1].Id;
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(second, 1))));
        await Task.Delay(100);
        paragraph = Realized(box).OfType<MdParagraph>().ElementAtOrDefault(1);
        var after = (paragraph?.ActualHeight ?? 0, paragraph?.FirstBaseline ?? 0, paragraph?.TextLayout?.LineCount ?? 0);
        double pitch = MdStyleSheet.Light.FontSize * MdStyleSheet.Light.LineHeight;
        bool sameLines = before.Item3 != after.Item3 || Math.Abs(before.Item1 - after.Item1) < 0.5;
        context.Check(paragraph is not null && Math.Abs(before.Item2 - after.Item2) < 0.5 && sameLines
            && Math.Abs(after.Item1 - after.Item3 * pitch) < 1,
            "theme: the caret entering a paragraph with markers does not move its lines", $"height/baseline/lines {before} -> {after}, pitch {pitch}");
    }

    private static void Load(MarkdownBox box, string markdown)
    {
        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(markdown, ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(document)) }, ids);
    }

    private static List<MdBlockView> Realized(MarkdownBox box) =>
        box.Document!.Geometry.Realized.Where(view => !box.Document.Geometry.IsDetached(view)).ToList();

    private static void CheckSpacing(SelfTestContext context, MarkdownBox box, MdStyleSheet sheet)
    {
        var geometry = box.Document!.Geometry;
        var top = Realized(box).Where(static view => view.Parent is not MdBlockView).OrderBy(view => geometry.Bounds(view).Y).ToList();
        var gaps = top.Zip(top.Skip(1), (upper, lower) => (Upper: upper.GetType().Name, Lower: lower.GetType().Name,
            Gap: geometry.Bounds(lower).Y - geometry.Bounds(upper).Bottom)).ToList();
        double Gap(string upper, string lower) => gaps.FirstOrDefault(gap => gap.Upper == upper && gap.Lower == lower).Gap;
        bool ok = Math.Abs(Gap("MdParagraph", "MdHeading") - sheet.HeadingSpacing) < 0.5
            && Math.Abs(Gap("MdHeading", "MdParagraph") - sheet.HeadingSpacing) < 0.5
            && Math.Abs(Gap("MdThematicBreak", "MdParagraph") - sheet.RuleSpacing) < 0.5
            && Math.Abs(Gap("MdParagraph", "MdList") - sheet.BlockSpacing) < 0.5;
        context.Check(ok, "theme: adjacent block margins collapse to the larger", string.Join(", ", gaps.Select(static gap => $"{gap.Upper}/{gap.Lower} {gap.Gap:0.#}")));
    }

    private static async Task CheckTaskAsync(SelfTestContext context, MarkdownBox box)
    {
        var items = Realized(box).OfType<MdListItem>().ToList();
        var boxes = items.Select(static item => Descendants(item).OfType<Microsoft.UI.Xaml.Controls.CheckBox>().FirstOrDefault()).ToList();
        context.Check(boxes.Count == 2 && boxes[0] is { IsChecked: false } && boxes[1] is { IsChecked: true } && box.IsInControl(boxes[0]),
            "theme: task items show native checkboxes in their state, left to the checkbox on press");
        if (boxes.Count == 2 && boxes[0] is { } open)
        {
            ((Microsoft.UI.Xaml.Automation.Provider.IToggleProvider)new Microsoft.UI.Xaml.Automation.Peers.CheckBoxAutomationPeer(open)).Toggle();
            await Task.Delay(80);
            string markdown = MarkdownSerializer.Serialize(box.State.Document);
            var now = Realized(box).OfType<MdListItem>().Select(static item => Descendants(item).OfType<Microsoft.UI.Xaml.Controls.CheckBox>().FirstOrDefault()?.IsChecked).ToList();
            context.Check(markdown.Contains("- [x] open", StringComparison.Ordinal) && now.Count == 2 && now[0] == true,
                "theme: clicking a checkbox toggles the task through a command", markdown.Replace("\n", "|"));
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static void CheckTheme(SelfTestContext context, MarkdownBox box, MdStyleSheet sheet, string name)
    {
        var views = box.Document!.Geometry.Realized.Where(view => !box.Document.Geometry.IsDetached(view)).ToList();
        var paragraph = views.OfType<MdParagraph>().FirstOrDefault();
        double expected = sheet.FontSize * sheet.LineHeight;
        double line = paragraph?.TextLayout?.Height ?? 0;
        context.Check(Math.Abs(line - expected) < 1, $"theme ({name}): a line is as tall as the CSS line height", $"{line:0.##} vs {expected:0.##}");

        var headings = views.OfType<MdHeading>().ToList();
        var title = headings.FirstOrDefault(static heading => heading.Level == 1);
        var small = headings.FirstOrDefault(static heading => heading.Level == 6);
        context.Check(title is not null && Math.Abs(title.TextPadding.Bottom - (sheet.HeadingRuleGap + sheet.HeadingRuleThickness)) < 0.01 && small is { TextPadding.Bottom: 0 },
            $"theme ({name}): a level 1 heading has its rule below, a level 6 heading none");

        var rows = views.OfType<MdTableRow>().ToList();
        var surfaces = rows.Select(static row => row.Blocks.OfType<MdBlockView>().First().Context.Surface).ToList();
        var body = sheet.TableCellBackground ?? sheet.PageBackground;
        var stripe = sheet.TableStripeBackground ?? body;
        bool ok = surfaces.Count == 4 && surfaces[0] == sheet.TableHeaderBackground && surfaces[1] == body && surfaces[2] == stripe && surfaces[3] == body;
        context.Check(ok, $"theme ({name}): header, body and striped rows have their backgrounds", string.Join(", ", surfaces));
    }
}
