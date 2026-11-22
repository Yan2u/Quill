using System.Text.RegularExpressions;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.Statistics;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Editor.WinUI.Localization;
using Quill.Editor.WinUI.Search;
using Quill.Editor.WinUI.Theming;
using Quill.Playground.Samples;
using Quill.Settings;
using Quill.Settings.Themes;
using Quill.Shell.Pages;
using Quill.Shell.Settings;
using Quill.Shell.Sidebar;
using Quill.Shell.Themes;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Runtime localization and zero-restart hot-switching probes: resource table parity,
/// live switching across Settings, Menus, Find, Sidebar, Open/Export, Themes, ThemeEditor and MdToc.
/// </summary>
internal static partial class LocalizationProbes
{
    [GeneratedRegex(@"\{(\d+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        string folder = Directory.CreateTempSubdirectory("quill-l10n-").FullName;
        UiLanguage original = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.English);
            CheckResourceTables(context);
            await CheckSettingsAndMenusAsync(context, folder);
            await CheckPanelsAndPagesAsync(context, folder);
            await CheckAboutPageAsync(context);
            await CheckThemesAndTocAsync(context, folder);
        }
        finally
        {
            UiStrings.SetLanguage(original);
            context.Stage.Children.Clear();
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void CheckResourceTables(SelfTestContext context)
    {
        var en = UiStrings.Table(UiLanguage.English);
        var zh = UiStrings.Table(UiLanguage.Chinese);
        bool sameCount = en.Count == zh.Count && en.Count > 450;
        var missingInZh = en.Keys.Where(k => !zh.ContainsKey(k)).ToList();
        var extraInZh = zh.Keys.Where(k => !en.ContainsKey(k)).ToList();
        var emptyValues = en.Concat(zh).Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key).ToList();
        var placeholderMismatches = en.Keys
            .Where(zh.ContainsKey)
            .Where(k => !Placeholders(en[k]).SequenceEqual(Placeholders(zh[k])))
            .ToList();

        context.Check(
            sameCount && missingInZh.Count == 0 && extraInZh.Count == 0 && emptyValues.Count == 0 && placeholderMismatches.Count == 0,
            "l10n: en.json and zh-CN.json have identical keys, non-empty values and matching placeholders",
            $"en={en.Count}, zh={zh.Count}, missing={missingInZh.Count}, extra={extraInZh.Count}, empty={emptyValues.Count}, placeholders={placeholderMismatches.Count}");
    }

    private static async Task CheckSettingsAndMenusAsync(SelfTestContext context, string folder)
    {
        var store = new SettingsStore(Path.Combine(folder, "settings.json"));
        ThemeChoice[] themes = [new("github", "Github"), new("github-dark-dimmed", "Github Dark Dimmed")];
        var settingsPage = new SettingsPage(store, themes) { Width = 1000, Height = 640 };

        var bar = new MenuBar();
        var editor = new MarkdownBox { Width = 600, Height = 300 };
        var ids = new SequentialBlockIdSource();
        var doc = MarkdownImporter.Import("# Heading\n\nParagraph", ids).Document;
        editor.Load(EditorState.AtStart(doc), ids);

        var menus = new EditorMenuBar(bar, editor);
        var sidebarMenuItem = new ToggleMenuFlyoutItem { Text = UiStrings.Get("view.sidebar") };
        void SyncSidebarItem(object? _, EventArgs __) => sidebarMenuItem.Text = UiStrings.Get("view.sidebar");
        UiStrings.LanguageChanged += SyncSidebarItem;

        var findPanel = new FindPanel(editor);

        try
        {
            var host = new StackPanel();
            host.Children.Add(bar);
            host.Children.Add(findPanel);
            host.Children.Add(editor);
            host.Children.Add(settingsPage);
            context.Stage.Children.Add(host);
            await Task.Delay(120);

            menus.Refresh();
            menus.Append("View", sidebarMenuItem);
            findPanel.Open(replace: true);
            findPanel.FindBox.Text = "zzzz_not_found_probe";
            await Task.Delay(60);

            ComboBox? languageBox = Control<ComboBox>(settingsPage, UiStrings.Get("settings.language.name"));
            context.Check(languageBox is { SelectedIndex: 0 }, "l10n: settings starts in English");
            if (languageBox is null)
            {
                return;
            }

            // Switch to Chinese live via SettingsPage ComboBox.
            languageBox.SelectedIndex = 1;
            await Task.Delay(80);

            string zhAutoSave = UiStrings.Get("settings.autoSave.name");
            string zhViewSidebar = UiStrings.Get("view.sidebar");
            string zhEditTop = UiStrings.Get("menu.top.edit");
            string zhViewTop = UiStrings.Get("menu.top.view");
            string zhNoResults = UiStrings.Get("find.noResults");
            string zhFindPlaceholder = UiStrings.Get("find.placeholder");

            bool nonAsciiZh = ContainsNonAscii(zhAutoSave) && ContainsNonAscii(zhViewSidebar) && ContainsNonAscii(zhEditTop) && ContainsNonAscii(zhNoResults);
            context.Check(
                store.Current.Language == DisplayLanguage.Chinese
                && UiStrings.Language == UiLanguage.Chinese
                && nonAsciiZh,
                "l10n: choosing Chinese updates settings store and active UiStrings immediately");

            settingsPage.Query = zhAutoSave;
            await Task.Delay(60);
            var zhShown = settingsPage.Entries.Where(e => e.Card.Visibility == Visibility.Visible).Select(e => e.Name).ToList();
            settingsPage.Query = "Auto save";
            await Task.Delay(60);
            var enShownUnderZh = settingsPage.Entries.Where(e => e.Card.Visibility == Visibility.Visible).ToList();
            settingsPage.Query = string.Empty;
            await Task.Delay(40);

            context.Check(
                zhShown.Contains(zhAutoSave, StringComparer.Ordinal) && enShownUnderZh.Count == 0,
                "l10n: Chinese settings search matches Chinese labels while English query hides unmatched entries",
                string.Join(", ", zhShown));

            var viewMenu = menus.Items.ElementAtOrDefault(3);
            bool menuSwitched = menus.Items.Count == 4
                && menus.Items[0].Title == zhEditTop
                && viewMenu?.Title == zhViewTop
                && viewMenu.Items.LastOrDefault() is ToggleMenuFlyoutItem last
                && ReferenceEquals(last, sidebarMenuItem)
                && last.Text == zhViewSidebar;
            context.Check(
                menuSwitched,
                "l10n: EditorMenuBar switches to Chinese and preserves appended View > Sidebar item",
                $"edit={menus.Items.FirstOrDefault()?.Title}, view={viewMenu?.Title}, extra={sidebarMenuItem.Text}");

            context.Check(
                findPanel.FindBox.PlaceholderText == zhFindPlaceholder && findPanel.Status == zhNoResults,
                "l10n: FindPanel placeholder and no-results status update live in Chinese",
                $"placeholder={findPanel.FindBox.PlaceholderText}, status={findPanel.Status}");

            // Switch back to English cleanly.
            languageBox.SelectedIndex = 0;
            await Task.Delay(80);
            context.Check(
                store.Current.Language == DisplayLanguage.English
                && UiStrings.Language == UiLanguage.English
                && menus.Items[0].Title == "Edit"
                && sidebarMenuItem.Text == "Sidebar"
                && findPanel.Status == "No results",
                "l10n: switching back to English restores English menus, sidebar item and FindPanel immediately");

            // Switching language while the menu bar is collapsed keeps live updates active once restored.
            bar.Visibility = Visibility.Collapsed;
            await Task.Delay(30);
            languageBox.SelectedIndex = 1;
            await Task.Delay(80);
            bar.Visibility = Visibility.Visible;
            await Task.Delay(40);
            context.Check(
                menus.Items[0].Title == zhEditTop && menus.Items[3].Title == zhViewTop,
                "l10n: collapsed EditorMenuBar updates titles live on language switch",
                $"edit={menus.Items[0].Title}, view={menus.Items[3].Title}");

            languageBox.SelectedIndex = 0;
            await Task.Delay(60);
        }
        finally
        {
            UiStrings.LanguageChanged -= SyncSidebarItem;
            context.Stage.Children.Clear();
        }
    }

    private static async Task CheckPanelsAndPagesAsync(SelfTestContext context, string folder)
    {
        var emptyOutline = new OutlinePanel { Height = 60 };
        var outline = new OutlinePanel();
        var ids = new SequentialBlockIdSource();
        outline.Update([new OutlineEntry(ids.NextId(), 1, string.Empty)]);

        using var files = new FilesPanel();
        var sidebar = new SidebarPanel(outline, files) { Width = 240, Height = 260 };
        var openPage = new OpenPage { Width = 600, Height = 300 };
        var exportPage = new ExportPage { Width = 600, Height = 300 };

        var host = new StackPanel();
        host.Children.Add(emptyOutline);
        host.Children.Add(sidebar);
        host.Children.Add(openPage);
        host.Children.Add(exportPage);
        context.Stage.Children.Add(host);
        await Task.Delay(120);

        UiStrings.SetLanguage(UiLanguage.Chinese);
        await Task.Delay(80);

        var emptyLabel = Descendants(emptyOutline).OfType<TextBlock>().FirstOrDefault(t => t.Opacity < 0.8);
        var openTitle = (TextBlock?)openPage.FindName("TitleText");
        var exportTitle = (TextBlock?)exportPage.FindName("TitleText");
        var outlineTree = Descendants(outline).OfType<TreeView>().FirstOrDefault();
        string? emptyHeadingNode = outlineTree?.RootNodes.FirstOrDefault()?.Content as string;
        var tabLabels = Descendants(sidebar).OfType<SegmentedItem>().Select(static item => item.Content as string).ToList();

        sidebar.Tab = SidebarTab.Files;
        await Task.Delay(80);
        var openFolderText = (TextBlock?)files.FindName("OpenFolderText");
        var filesEmptyText = (TextBlock?)files.FindName("EmptyText");

        bool chinesePanelsOk =
            emptyLabel?.Text == UiStrings.Get("sidebar.outline.empty")
            && tabLabels.SequenceEqual([UiStrings.Get("sidebar.tab.outline"), UiStrings.Get("sidebar.tab.files")])
            && emptyHeadingNode == UiStrings.Get("sidebar.outline.emptyHeading")
            && openFolderText?.Text == UiStrings.Get("sidebar.files.openFolder")
            && filesEmptyText?.Text == UiStrings.Get("sidebar.files.noFolder")
            && openTitle?.Text == UiStrings.Get("open.title")
            && exportTitle?.Text == UiStrings.Get("export.title")
            && ContainsNonAscii(openTitle?.Text)
            && ContainsNonAscii(exportTitle?.Text);

        context.Check(
            chinesePanelsOk,
            "l10n: Sidebar, Outline, Files, Open and Export pages switch to Chinese live",
            $"emptyOutline={emptyLabel?.Text}, tabs=[{string.Join(",", tabLabels)}], outline={emptyHeadingNode}, filesBtn={openFolderText?.Text}, open={openTitle?.Text}, export={exportTitle?.Text}");

        UiStrings.SetLanguage(UiLanguage.English);
        await Task.Delay(60);
        sidebar.Tab = SidebarTab.Outline;
        await Task.Delay(60);
        context.Check(
            openTitle?.Text == "Open"
            && exportTitle?.Text == "Export as..."
            && openFolderText?.Text == "Open folder"
            && (outlineTree?.RootNodes.FirstOrDefault()?.Content as string) == "(empty heading)",
            "l10n: Sidebar, Outline, Files, Open and Export pages revert to English cleanly");

        context.Stage.Children.Clear();
    }

    private static async Task CheckThemesAndTocAsync(SelfTestContext context, string folder)
    {
        var store = new SettingsStore(Path.Combine(folder, "theme-settings.json"));
        var catalog = new ThemeCatalog(new ThemeStore(Path.Combine(folder, "Themes")));
        var themesPage = new ThemesPage(catalog, store) { Width = 960, Height = 500 };
        var editorPage = new ThemeEditorPage(catalog, catalog.Find(BuiltInThemes.GithubId)!) { Width = 1000, Height = 640 };
        var toc = new MdToc { DetachedContext = BlockContext.Default, Width = 400 };

        var host = new StackPanel();
        host.Children.Add(themesPage);
        host.Children.Add(editorPage);
        host.Children.Add(toc);
        context.Stage.Children.Add(host);
        await Task.Delay(100);

        UiStrings.SetLanguage(UiLanguage.Chinese);
        await Task.Delay(80);

        string quoteQuery = UiStrings.Get("theme.group.Quotes");
        string zhQuoteSlot = UiStrings.Get("theme.slot.QuoteBar");
        editorPage.Query = quoteQuery;
        await Task.Delay(50);

        bool quoteVisible = editorPage.HeaderOf(ThemeGroup.Quotes).Visibility == Visibility.Visible;
        var visibleSlots = editorPage.Rows
            .Where(row => row.Row.Visibility == Visibility.Visible)
            .Select(row => row.Slot.DisplayName)
            .ToList();
        var quoteCards = themesPage.Cards;

        context.Check(
            ContainsNonAscii(quoteQuery)
            && quoteVisible
            && visibleSlots.Contains(zhQuoteSlot, StringComparer.Ordinal)
            && editorPage.CopyButton.Content as string == UiStrings.Get("themeEditor.copy")
            && editorPage.SaveButton.Content as string == UiStrings.Get("themeEditor.save")
            && quoteCards.Any(card => card.Theme.Id == "github")
            && UiStrings.Get("editor.tocEmpty") != "Table of Contents",
            "l10n: ThemesPage, ThemeEditorPage Chinese search and MdToc localize cleanly",
            $"query={quoteQuery}, quoteRows={visibleSlots.Count}");

        UiStrings.SetLanguage(UiLanguage.English);
        await Task.Delay(50);
        editorPage.Query = string.Empty;
        context.Check(
            editorPage.HeaderOf(ThemeGroup.Quotes).Text == "Quotes"
            && editorPage.CopyButton.Content as string == "Copy",
            "l10n: ThemeEditorPage reverts to English cleanly");

        context.Stage.Children.Clear();
    }

    private static async Task CheckAboutPageAsync(SelfTestContext context)
    {
        string iconPath = Path.Combine(RepositoryPaths.Root ?? AppContext.BaseDirectory, "src", "Quill", "Assets", "Quill-256.png");
        var page = new AboutPage("0.1.0", iconPath) { Width = 960, Height = 760 };
        context.Stage.Children.Add(page);
        await Task.Delay(80);

        List<string> Texts() => Descendants(page).OfType<TextBlock>().Select(static block => block.Text).ToList();
        List<string> CardHeaders() => Descendants(page).OfType<SettingsCard>().Select(static card => card.Header as string ?? string.Empty).ToList();
        var uris = Descendants(page).OfType<HyperlinkButton>()
            .Select(static button => button.NavigateUri?.AbsoluteUri?.TrimEnd('/'))
            .Where(static uri => uri is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var enTexts = Texts();
        var enHeaders = CardHeaders();
        bool allProjectsListed = AboutPage.OpenSourceProjects.All(entry =>
            enTexts.Contains(entry.Project, StringComparer.Ordinal)
            && uris.Contains(entry.Website.TrimEnd('/')));
        bool enOk =
            enTexts.Contains("About", StringComparer.Ordinal)
            && enTexts.Contains("Version 0.1.0", StringComparer.Ordinal)
            && enTexts.Contains("Links", StringComparer.Ordinal)
            && enTexts.Contains("Special thanks to the open-source projects below:", StringComparer.Ordinal)
            && enTexts.Contains("Project", StringComparer.Ordinal)
            && enTexts.Contains("Website", StringComparer.Ordinal)
            && enHeaders.SequenceEqual(["Project homepage", "Developer homepage"])
            && uris.Contains(AboutPage.ProjectHomepageUrl)
            && uris.Contains(AboutPage.DeveloperHomepageUrl)
            && allProjectsListed
            && Descendants(page).OfType<Image>().Any();
        context.Check(enOk, "l10n: AboutPage renders English strings, links and all open-source rows", $"projects={AboutPage.OpenSourceProjects.Count}, uris={uris.Count}");

        UiStrings.SetLanguage(UiLanguage.Chinese);
        await Task.Delay(60);

        var zhTexts = Texts();
        var zhHeaders = CardHeaders();
        bool zhOk =
            zhTexts.Contains(UiStrings.Get("about.title"), StringComparer.Ordinal)
            && zhTexts.Contains(UiStrings.Format("about.version", "0.1.0"), StringComparer.Ordinal)
            && zhTexts.Contains(UiStrings.Get("about.linksHeading"), StringComparer.Ordinal)
            && zhTexts.Contains(UiStrings.Get("about.openSourceHeading"), StringComparer.Ordinal)
            && zhTexts.Contains(UiStrings.Get("about.column.project"), StringComparer.Ordinal)
            && zhTexts.Contains(UiStrings.Get("about.column.website"), StringComparer.Ordinal)
            && zhHeaders.SequenceEqual([UiStrings.Get("about.projectHomepage"), UiStrings.Get("about.developerHomepage")])
            && ContainsNonAscii(UiStrings.Get("about.openSourceHeading"));
        context.Check(zhOk, "l10n: AboutPage updates all headings, link cards and table headers to Chinese live");

        UiStrings.SetLanguage(UiLanguage.English);
        await Task.Delay(60);
        context.Check(
            Texts().Contains("Special thanks to the open-source projects below:", StringComparer.Ordinal)
            && CardHeaders().SequenceEqual(["Project homepage", "Developer homepage"]),
            "l10n: AboutPage reverts to English cleanly");

        context.Stage.Children.Clear();

        await CheckAboutLayoutAsync(context, iconPath);
    }

    private static async Task CheckAboutLayoutAsync(SelfTestContext context, string iconPath)
    {
        static (StackPanel Root, double LeftGap, double RightGap) MeasureLayout(AboutPage p)
        {
            var sv = Descendants(p).OfType<ScrollViewer>().First();
            var host = (Panel)sv.Content;
            var root = (StackPanel)host.Children[0];
            Point origin = root.TransformToVisual(p).TransformPoint(new Point(0, 0));
            double leftGap = origin.X;
            double rightGap = p.ActualWidth - (leftGap + root.ActualWidth);
            return (root, leftGap, rightGap);
        }

        var standalonePage = new AboutPage("0.1.0", iconPath);
        var wrapper = new Border
        {
            Width = 1400,
            Height = 760,
            Background = new SolidColorBrush(Colors.White),
            RequestedTheme = ElementTheme.Light,
            Child = standalonePage,
        };
        var canvas = new Canvas { Width = 1400, Height = 760, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        canvas.Children.Add(wrapper);
        context.Stage.Children.Add(canvas);
        wrapper.UpdateLayout();
        await Task.Delay(120);
        wrapper.UpdateLayout();
        var (wideRoot, wideLeft, wideRight) = MeasureLayout(standalonePage);
        context.Check(
            wideRoot.ActualWidth is >= 959.0 and <= 960.5 && Math.Abs(wideLeft - wideRight) < 2.0,
            "l10n: AboutPage centers symmetrically at 1400px viewport width",
            $"root={wideRoot.ActualWidth:F1}, leftGap={wideLeft:F1}, rightGap={wideRight:F1}");

        wrapper.Width = 700;
        canvas.Width = 700;
        wrapper.UpdateLayout();
        await Task.Delay(80);
        wrapper.UpdateLayout();
        var (narrowRoot, narrowLeft, narrowRight) = MeasureLayout(standalonePage);
        context.Check(
            narrowRoot.ActualWidth <= 700.5 && Math.Abs(narrowLeft - narrowRight) < 2.0,
            "l10n: AboutPage fits and centers cleanly at 700px narrow viewport width",
            $"root={narrowRoot.ActualWidth:F1}, leftGap={narrowLeft:F1}, rightGap={narrowRight:F1}");

        context.Stage.Children.Clear();

        var pageHost = new Grid
        {
            ChildrenTransitions = [new EntranceThemeTransition { FromHorizontalOffset = 0, FromVerticalOffset = 28, IsStaggeringEnabled = false }],
        };
        var contentCard = new Grid
        {
            CornerRadius = new CornerRadius(8, 0, 0, 0),
            BorderThickness = new Thickness(1, 1, 0, 0),
            Background = new SolidColorBrush(Colors.White),
            Children = { pageHost },
        };
        var navView = new NavigationView
        {
            Width = 1400,
            Height = 760,
            RequestedTheme = ElementTheme.Light,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsPaneOpen = false,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsPaneToggleButtonVisible = false,
            IsSettingsVisible = false,
            IsTitleBarAutoPaddingEnabled = false,
            Content = contentCard,
        };
        navView.Resources["NavigationViewContentMargin"] = new Thickness(0);
        navView.Resources["NavigationViewContentGridBorderThickness"] = new Thickness(0);
        navView.MenuItems.Add(new NavigationViewItem { Content = "Home", Icon = new FontIcon { Glyph = "\uE80F" } });
        navView.FooterMenuItems.Add(new NavigationViewItem { Content = "About", Icon = new FontIcon { Glyph = "\uE946" } });

        var navCanvas = new Canvas { Width = 1400, Height = 760, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        navCanvas.Children.Add(navView);
        context.Stage.Children.Add(navCanvas);
        navView.UpdateLayout();
        await Task.Delay(80);

        var navAbout = new AboutPage("0.1.0", iconPath);
        pageHost.Children.Add(navAbout);
        await Task.Delay(450);
        navView.UpdateLayout();
        var (navRoot, navLeft, navRight) = MeasureLayout(navAbout);
        context.Check(
            navRoot.ActualWidth is >= 959.0 and <= 960.5 && Math.Abs(navLeft - navRight) < 2.0,
            "l10n: AboutPage centers symmetrically inside NavigationView content card replica",
            $"root={navRoot.ActualWidth:F1}, leftGap={navLeft:F1}, rightGap={navRight:F1}");

        context.Stage.Children.Clear();
    }

    private static List<string> Placeholders(string text) =>
        [.. PlaceholderRegex().Matches(text).Select(static m => m.Value).OrderBy(static x => x, StringComparer.Ordinal)];

    private static bool ContainsNonAscii(string? text) =>
        !string.IsNullOrEmpty(text) && text.Any(static c => c > 0x7F);

    private static T? Control<T>(SettingsPage page, string name)
        where T : DependencyObject
    {
        var card = page.Entries.First(entry => entry.Name == name).Card as SettingsCard;
        return card?.Content switch
        {
            T control => control,
            DependencyObject content => Descendants(content).OfType<T>().FirstOrDefault(),
            _ => null,
        };
    }

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
