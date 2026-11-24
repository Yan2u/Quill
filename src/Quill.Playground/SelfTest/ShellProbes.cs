using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.Statistics;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Playground.Samples;
using Quill.Shell;
using Quill.Shell.Pages;
using Quill.Shell.Sidebar;
using Windows.Graphics.DirectX;

namespace Quill.Playground.SelfTest;

/// <summary>
/// The sidebar of the application (13-app-shell.md), hosted without the window: the Files panel's tree and
/// list over a temporary folder (what they show, reading folders when they expand, wrapping and cutting
/// long names, following changes on disk and keeping expanded folders), the view switch, the empty
/// state, and wrapping in the outline.
/// </summary>
internal static class ShellProbes
{
    private const double PanelWidth = 220;
    private const string LongName = "a file with quite a long name that cannot possibly fit on a single line of the sidebar.md";

    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        string folder = Directory.CreateTempSubdirectory("quill-shell-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "notes"));
            File.WriteAllText(Path.Combine(folder, "notes", "inner.md"), string.Empty);
            Directory.CreateDirectory(Path.Combine(folder, ".git"));
            File.WriteAllText(Path.Combine(folder, "picture.png"), string.Empty);
            File.WriteAllText(Path.Combine(folder, "b.txt"), string.Empty);
            File.WriteAllText(Path.Combine(folder, "a.md"), string.Empty);
            File.WriteAllText(Path.Combine(folder, LongName), string.Empty);

            using var files = new FilesPanel { Width = PanelWidth, Height = 500, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            context.Stage.Children.Add(files);
            await Task.Delay(50);

            CheckEmpty(context, files);
            await CheckTreeAsync(context, files, folder);
            await CheckListAsync(context, files);
            await CheckFollowsDiskAsync(context, files, folder);
            CheckFollowsDocument(context, files, folder);
            context.Stage.Children.Clear();
            await CheckLongTreeAsync(context);
            await CheckSidebarAsync(context);
            await CheckOutlineAsync(context);
            CheckWindowChrome(context);
            await CheckNavSvgIconAsync(context);
            await CheckZeroSizeEditorAsync(context);
        }
        finally
        {
            context.Stage.Children.Clear();
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void CheckEmpty(SelfTestContext context, FilesPanel files)
    {
        context.Check(files.RootFolder is null && files.Roots.Count == 0 && Text(files, "EmptyText") is { Visibility: Visibility.Visible, Text.Length: > 0 },
            "files: without a folder or a saved document the panel says what to do");
    }

    private static async Task CheckTreeAsync(SelfTestContext context, FilesPanel files, string folder)
    {
        files.OpenFolder(folder);
        await Task.Delay(120);
        var names = files.Roots.Select(static entry => entry.Name).ToList();
        context.Check(names.SequenceEqual(["notes", LongName, "a.md", "b.txt"], StringComparer.Ordinal),
            "files: the tree shows the folder first, then Markdown files, without hidden folders and other files", string.Join(" | ", names));

        var notes = files.Roots[0];
        bool unread = notes.IsFolder && notes.HasUnrealizedChildren && notes.Children.Count == 0;
        FilesPanel.Realize(notes);
        context.Check(unread && !notes.HasUnrealizedChildren && notes.Children.Select(static entry => entry.Name).SequenceEqual(["inner.md"]),
            "files: a folder reads its children when it expands");

        // The long name wraps inside the panel instead of scrolling sideways.
        var row = Descendants(files.FindName("Tree") as DependencyObject).OfType<TextBlock>().FirstOrDefault(static text => text.Text == LongName);
        context.Check(row is not null && row.ActualWidth <= PanelWidth && row.ActualHeight > row.FontSize * 2,
            "files: a long name in the tree wraps within the panel", row is null ? "not found" : $"{row.ActualWidth:F0} x {row.ActualHeight:F0}");

        string? invoked = null;
        EventHandler<string> handler = (_, path) => invoked = path;
        files.FileInvoked += handler;
        files.Invoke(notes);
        bool folderIgnored = invoked is null;
        files.Invoke(files.Roots.First(static entry => entry.Name == "a.md"));
        files.FileInvoked -= handler;
        context.Check(folderIgnored && invoked == Path.Combine(folder, "a.md"), "files: invoking a file asks to open it, invoking a folder does not");
    }

    private static async Task CheckListAsync(SelfTestContext context, FilesPanel files)
    {
        files.View = FilesView.List;
        await Task.Delay(120);
        var list = files.FindName("List") as ListView;
        var tree = files.FindName("Tree") as TreeView;
        context.Check(list is { Visibility: Visibility.Visible } && tree is { Visibility: Visibility.Collapsed }, "files: the view switch swaps the tree for the list");
        context.Check(files.Files.Select(static entry => entry.Name).Contains("a.md") && files.Files.All(static entry => !entry.IsFolder && entry.Name != "inner.md"),
            "files: the list shows the Markdown files of the folder itself", string.Join(" | ", files.Files.Select(static entry => entry.Name)));

        var row = list is null ? null : Descendants(list).OfType<TextBlock>().FirstOrDefault(static text => text.Text == LongName);
        context.Check(row is { TextTrimming: TextTrimming.CharacterEllipsis, TextWrapping: TextWrapping.NoWrap, IsTextTrimmed: true } && row.ActualWidth <= PanelWidth,
            "files: a long name in the list is cut short within the panel", row is null ? "not found" : $"{row.ActualWidth:F0} wide, trimmed {row.IsTextTrimmed}");
        files.View = FilesView.Tree;
        await Task.Delay(50);
    }

    private static async Task CheckFollowsDiskAsync(SelfTestContext context, FilesPanel files, string folder)
    {
        var notes = files.Roots[0];
        FilesPanel.Realize(notes);
        notes.IsExpanded = true;
        File.WriteAllText(Path.Combine(folder, "added.md"), string.Empty);
        bool seen = false;
        for (int i = 0; i < 40 && !seen; i++)
        {
            await Task.Delay(50);
            seen = files.Roots.Any(static entry => entry.Name == "added.md");
        }

        var again = files.Roots.FirstOrDefault(static entry => entry.Name == "notes");
        context.Check(seen, "files: a file added on disk appears");
        context.Check(again is { IsExpanded: true, HasUnrealizedChildren: false } && again.Children.Count == 1, "files: expanded folders stay expanded when the folder is read again");
    }

    private static void CheckFollowsDocument(SelfTestContext context, FilesPanel files, string folder)
    {
        string notes = Path.Combine(folder, "notes");
        files.ShowDocument(Path.Combine(notes, "inner.md"));
        bool keepsOpened = files.RootFolder == folder && files.OpenedFolder == folder;
        context.Check(keepsOpened, "files: an opened folder stays the root when a document elsewhere is shown");

        using var other = new FilesPanel();
        other.ShowDocument(Path.Combine(notes, "inner.md"));
        context.Check(other.RootFolder == notes && other.OpenedFolder is null && other.Roots.Select(static entry => entry.Name).SequenceEqual(["inner.md"]),
            "files: without an opened folder the root is the document's folder");

        using var inactive = new FilesPanel { IsActive = false };
        inactive.ShowDocument(Path.Combine(notes, "inner.md"));
        bool skippedWhileInactive = inactive.RootFolder == notes && inactive.Roots.Count == 0;
        inactive.IsActive = true;
        bool loadedWhenActive = inactive.Roots.Select(static entry => entry.Name).SequenceEqual(["inner.md"]);
        context.Check(skippedWhileInactive && loadedWhenActive,
            "files: inactive panel defers folder scans until activated");
    }

    /// <summary>
    /// A long tree (user report, 2026-11-03): showing a document from the tree, and reading the folder again
    /// (a save's temporary file), keep the entries, the scroll position and the expanded folders, and mark
    /// the document; containers reused for files after scrolling past folders show no chevron.
    /// </summary>
    private static async Task CheckLongTreeAsync(SelfTestContext context)
    {
        string folder = Directory.CreateTempSubdirectory("quill-tree-").FullName;
        try
        {
            for (int i = 0; i < 30; i++)
            {
                string sub = Directory.CreateDirectory(Path.Combine(folder, $"folder {i:00}")).FullName;
                File.WriteAllText(Path.Combine(sub, "inside.md"), string.Empty);
            }

            for (int i = 0; i < 30; i++)
            {
                File.WriteAllText(Path.Combine(folder, $"note {i:00}.md"), string.Empty);
            }

            using var files = new FilesPanel { Width = PanelWidth, Height = 300, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            context.Stage.Children.Add(files);
            files.OpenFolder(folder);
            var first = files.Roots[0];
            FilesPanel.Realize(first);
            first.IsExpanded = true;
            await Task.Delay(150);
            var tree = (TreeView)files.FindName("Tree");
            var scroller = Descendants(tree).OfType<ScrollViewer>().First();
            scroller.ChangeView(null, scroller.ScrollableHeight, null, disableAnimation: true);
            await Task.Delay(200);

            var glyphs = Descendants(tree).OfType<TreeViewItem>()
                .Where(static item => item.DataContext is FileEntry { IsFolder: false } && item.ActualHeight > 0)
                .Where(static item => item.GlyphOpacity > 0)
                .Select(static item => ((FileEntry)item.DataContext).Name)
                .ToList();
            context.Check(glyphs.Count == 0, "files: after scrolling past folders, no file shows a chevron", string.Join(", ", glyphs));


            double offset = scroller.VerticalOffset;
            var entries = files.Roots.ToList();
            string shown = Path.Combine(folder, "note 25.md");
            files.ShowDocument(shown);
            await Task.Delay(150);
            context.Check(files.Roots.SequenceEqual(entries) && Math.Abs(scroller.VerticalOffset - offset) < 1 && first.IsExpanded
                && tree.SelectedItem is FileEntry { Name: "note 25.md" },
                "files: showing a document from the tree keeps the tree as it is and selects the document",
                $"same entries {files.Roots.SequenceEqual(entries)}, offset {offset:F0} -> {scroller.VerticalOffset:F0}, selected {(tree.SelectedItem as FileEntry)?.Name}");

            // A save writes a temporary file and renames it: the folder is read again.
            File.WriteAllText(shown + ".quill-save", "x");
            File.Move(shown + ".quill-save", shown, overwrite: true);
            File.WriteAllText(Path.Combine(folder, "note 99.md"), string.Empty);
            bool seen = false;
            for (int i = 0; i < 40 && !seen; i++)
            {
                await Task.Delay(50);
                seen = files.Roots.Any(static entry => entry.Name == "note 99.md");
            }

            await Task.Delay(100);
            context.Check(seen && entries.All(files.Roots.Contains) && Math.Abs(scroller.VerticalOffset - offset) < 1 && first.IsExpanded
                && tree.SelectedItem is FileEntry { Name: "note 25.md" },
                "files: reading the folder again keeps the entries, the scroll position and the selection",
                $"seen {seen}, offset {offset:F0} -> {scroller.VerticalOffset:F0}, selected {(tree.SelectedItem as FileEntry)?.Name}");

            // A folder with nothing to show loses its chevron once read.
            string empty = Directory.CreateDirectory(Path.Combine(folder, "folder 00", "empty")).FullName;
            scroller.ChangeView(null, 0, null, disableAnimation: true);
            files.Refresh();
            await Task.Delay(150);
            var emptyEntry = first.Children.FirstOrDefault(static entry => entry.IsFolder);
            if (emptyEntry is not null)
            {
                emptyEntry.IsExpanded = true;
                await Task.Delay(100);
            }

            var emptyItem = Descendants(tree).OfType<TreeViewItem>().FirstOrDefault(item => ReferenceEquals(item.DataContext, emptyEntry));
            context.Check(emptyEntry is { HasUnrealizedChildren: false, Children.Count: 0 } && emptyItem is { GlyphOpacity: 0 },
                "files: an empty folder shows no chevron once expanded", $"{emptyEntry?.Name}, opacity {emptyItem?.GlyphOpacity}");
            Directory.Delete(empty);
        }
        finally
        {
            context.Stage.Children.Clear();
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task CheckSidebarAsync(SelfTestContext context)
    {
        using var files = new FilesPanel();
        var outline = new OutlinePanel();
        var sidebar = new SidebarPanel(outline, files) { Width = PanelWidth, Height = 400, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        context.Stage.Children.Add(sidebar);
        await Task.Delay(80);
        bool startsOnOutline = sidebar.Tab == SidebarTab.Outline && sidebar.ShowsOutline;
        int changes = 0;
        sidebar.TabChanged += (_, _) => changes++;
        sidebar.Tab = SidebarTab.Files;
        await Task.Delay(50);
        context.Check(startsOnOutline && sidebar.Tab == SidebarTab.Files && !sidebar.ShowsOutline && changes == 1, "sidebar: the tabs swap the outline for the files");
        context.Stage.Children.Clear();
    }

    private static async Task CheckOutlineAsync(SelfTestContext context)
    {
        var outline = new OutlinePanel { Width = PanelWidth, Height = 300, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        context.Stage.Children.Add(outline);
        await Task.Delay(50);
        string heading = "A heading long enough that it has to wrap onto further lines in a narrow sidebar";
        outline.Update([new OutlineEntry(new BlockId(1), 1, heading)]);
        await Task.Delay(120);
        var row = Descendants(outline).OfType<TextBlock>().FirstOrDefault(text => text.Text == heading);
        context.Check(row is not null && row.ActualWidth <= PanelWidth && row.ActualHeight > row.FontSize * 2,
            "outline: a long heading wraps within the panel", row is null ? "not found" : $"{row.ActualWidth:F0} x {row.ActualHeight:F0}");
        context.Stage.Children.Clear();
    }

    private static void CheckWindowChrome(SelfTestContext context)
    {
        var normal = WindowChrome.TryGetTitleBarInsets(0, 241, 1.75);
        bool normalOk = normal is var (left, right) && left.Value == 0 && Math.Abs(right.Value - 241 / 1.75) < 1e-6;
        string? chromeDetail = null;
        if (normal is { } applied)
        {
            var leftColumn = new ColumnDefinition { Width = applied.Left };
            var rightColumn = new ColumnDefinition { Width = applied.Right };
            // WinUI stores GridLength.Value in single-precision float inside XAML properties.
            bool assigned = leftColumn.Width.GridUnitType == GridUnitType.Pixel
                && rightColumn.Width.GridUnitType == GridUnitType.Pixel
                && Math.Abs(leftColumn.Width.Value - applied.Left.Value) < 1e-3
                && Math.Abs(rightColumn.Width.Value - applied.Right.Value) < 1e-3;
            normalOk &= assigned;
            chromeDetail = $"left={leftColumn.Width.Value:F6}, right={rightColumn.Width.Value:F6} (expected {applied.Right.Value:F6})";
        }

        bool guardsOk = WindowChrome.TryGetTitleBarInsets(-48, 0, 1.75) is null
            && WindowChrome.TryGetTitleBarInsets(0, -48, 1.75) is null
            && WindowChrome.TryGetTitleBarInsets(0, 241, 1.75, isMinimized: true) is null
            && WindowChrome.TryGetTitleBarInsets(0, 241, 0) is null
            && WindowChrome.TryGetTitleBarInsets(0, 241, -1) is null
            && WindowChrome.TryGetTitleBarInsets(0, 241, double.NaN) is null;
        context.Check(normalOk && guardsOk,
            "chrome: positive title bar insets convert to GridLength and negative or minimized insets are ignored",
            chromeDetail);

        bool navOk = WindowChrome.TryGetNarrowNavigation(1200) == false
            && WindowChrome.TryGetNarrowNavigation(600) == true
            && WindowChrome.TryGetNarrowNavigation(0) is null
            && WindowChrome.TryGetNarrowNavigation(1200, isMinimized: true) is null;
        context.Check(navOk, "chrome: zero-width and minimized navigation checks keep the current mode");

        var workArea = new Windows.Graphics.RectInt32(0, 0, 1920, 1040);
        var tinyWorkArea = new Windows.Graphics.RectInt32(0, 0, 400, 300);
        bool sizeOk = WindowChrome.TryGetRestoredWindowSize(1280, 820, workArea) is { Width: 1280, Height: 820 }
            && WindowChrome.TryGetRestoredWindowSize(2400, 1400, workArea) is { Width: 1920, Height: 1040 }
            && WindowChrome.TryGetRestoredWindowSize(200, 150, workArea) is { Width: 480, Height: 360 }
            && WindowChrome.TryGetRestoredWindowSize(300, 200, tinyWorkArea) is { Width: 400, Height: 300 }
            && WindowChrome.TryGetRestoredWindowSize(null, 820, workArea) is null
            && WindowChrome.TryGetRestoredWindowSize(0, 820, workArea) is null
            && WindowChrome.TryGetRestoredWindowSize(-100, 820, workArea) is null
            && WindowChrome.TryGetRestoredWindowSize(1280, 820, default) is null;
        context.Check(sizeOk, "chrome: restored window size clamps to work area and ignores missing or empty inputs");

        bool menuVisibilityOk = WindowChrome.EditorMenuBarVisibility(ShellPage.Editor) == Visibility.Visible
            && WindowChrome.EditorMenuBarVisibility(ShellPage.Open) == Visibility.Collapsed
            && WindowChrome.EditorMenuBarVisibility(ShellPage.Export) == Visibility.Collapsed
            && WindowChrome.EditorMenuBarVisibility(ShellPage.Theme) == Visibility.Collapsed
            && WindowChrome.EditorMenuBarVisibility(ShellPage.Settings) == Visibility.Collapsed
            && WindowChrome.EditorMenuBarVisibility(ShellPage.About) == Visibility.Collapsed;
        context.Check(menuVisibilityOk, "chrome: editor menu bar shows only on the editor page");
    }

    private static async Task CheckZeroSizeEditorAsync(SelfTestContext context)
    {
        var box = new MarkdownBox
        {
            Width = 560,
            Height = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        context.Stage.Children.Add(box);
        await Task.Delay(50);

        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import("---\n\n[toc]\n\n# Heading\n\nParagraph\n\n| a | b |\n| - | - |\n| 1 | 2 |", ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.BlockCaret(document.Id, 0) }, ids);
        await Task.Delay(60);

        box.Width = 0;
        box.Height = 0;
        box.UpdateLayout();
        await Task.Delay(60);

        box.Width = 560;
        box.Height = 420;
        box.UpdateLayout();
        await Task.Delay(80);

        var realized = box.Document?.Geometry.Realized.Where(view => !box.Document.Geometry.IsDetached(view)).ToList() ?? [];
        var errors = realized.Where(static view => view.LastDrawError is not null).ToList();
        context.Check(realized.Count > 0 && errors.Count == 0,
            "editor: resizing to 0x0 and back leaves every realized block drawn without error",
            $"realized {realized.Count}, errors {errors.Count}");
        context.Stage.Children.Clear();
    }

    private static async Task CheckNavSvgIconAsync(SelfTestContext context)
    {
        string svgPath = Path.Combine(RepositoryPaths.Root ?? AppContext.BaseDirectory, "src", "Quill", "Assets", "Quill-nav.svg");
        if (!File.Exists(svgPath))
        {
            context.Check(false, "shell: Quill-nav.svg asset exists", svgPath);
            return;
        }

        int opened = 0;
        int failed = 0;
        (NavigationView View, SvgImageSource Svg) MakeRail(ElementTheme theme, Windows.UI.Color backdrop)
        {
            var svg = new SvgImageSource(new Uri(svgPath));
            svg.Opened += (_, _) => opened++;
            svg.OpenFailed += (_, _) => failed++;

            var home = new NavigationViewItem
            {
                Content = "Home",
                Tag = "Editor",
                IsSelected = true,
                Icon = new ImageIcon { Source = svg, Width = 20, Height = 20 },
            };
            home.Resources["NavigationViewItemOnLeftIconBoxHeight"] = 20d;

            var nav = new NavigationView
            {
                Width = 64,
                Height = 260,
                RequestedTheme = theme,
                Background = new SolidColorBrush(backdrop),
                PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
                IsPaneOpen = false,
                IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
                IsPaneToggleButtonVisible = false,
                IsSettingsVisible = false,
            };
            nav.MenuItems.Add(home);
            nav.MenuItems.Add(new NavigationViewItem { Content = "Open", Icon = new FontIcon { Glyph = "\uE8E5" } });
            nav.MenuItems.Add(new NavigationViewItem { Content = "Theme", Icon = new FontIcon { Glyph = "\uE790" } });
            return (nav, svg);
        }

        var light = MakeRail(ElementTheme.Light, Windows.UI.Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3));
        var dark = MakeRail(ElementTheme.Dark, Windows.UI.Color.FromArgb(0xFF, 0x20, 0x20, 0x20));
        var preview = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Colors.White),
        };
        preview.Children.Add(light.View);
        preview.Children.Add(dark.View);
        context.Stage.Children.Add(preview);

        for (int i = 0; i < 40 && opened + failed < 2; i++)
        {
            await Task.Delay(50);
        }

        await Task.Delay(80);
        preview.UpdateLayout();

        var capture = new RenderTargetBitmap();
        await capture.RenderAsync(preview);
        byte[] pixels = (await capture.GetPixelsAsync()).ToArray();
        int pw = capture.PixelWidth;
        int ph = capture.PixelHeight;

        if (pw > 0 && ph > 0)
        {
            string outPath = Path.Combine(RepositoryPaths.Temp("nav-preview"), "nav-quill-svg-preview.png");
            var device = CanvasDevice.GetSharedDevice();
            using var bitmap = CanvasBitmap.CreateFromBytes(device, pixels, pw, ph, DirectXPixelFormat.B8G8R8A8UIntNormalized);
            await bitmap.SaveAsync(outPath, CanvasBitmapFileFormat.Png);
        }

        // Check that pixels inside the Light Home icon region contain shaded slate-blue color gradient tones
        // rather than a missing image or solid #000000 fallback from an unsupported SVG filter.
        int blueShadedPixels = 0;
        int solidBlackPixels = 0;
        int iconLeft = Math.Max(0, pw / 16);
        int iconRight = Math.Min(pw / 2, pw * 5 / 12);
        int iconTop = 0;
        int iconBottom = Math.Min(ph, ph / 3);
        for (int y = iconTop; y < iconBottom; y++)
        {
            for (int x = iconLeft; x < iconRight; x++)
            {
                int idx = (y * pw + x) * 4;
                byte b = pixels[idx];
                byte g = pixels[idx + 1];
                byte r = pixels[idx + 2];
                if (b > r + 4 && r > 40 && r < 235)
                {
                    blueShadedPixels++;
                }
                else if (r < 5 && g < 5 && b < 5)
                {
                    solidBlackPixels++;
                }
            }
        }

        context.Check(
            opened == 2 && failed == 0 && pw > 0 && blueShadedPixels >= 20 && solidBlackPixels == 0,
            "shell: NavigationView Home renders Quill-nav.svg with SvgImageSource cleanly",
            $"opened={opened}, failed={failed}, capture={pw}x{ph}, shaded={blueShadedPixels}, black={solidBlackPixels}");
        context.Stage.Children.Clear();
    }

    private static TextBlock? Text(FilesPanel files, string name) => files.FindName(name) as TextBlock;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject? root)
    {
        if (root is null)
        {
            yield break;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }
}
