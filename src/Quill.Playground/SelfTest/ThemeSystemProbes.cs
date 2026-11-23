using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.WinUI.Theming;
using Quill.Settings;
using Quill.Settings.Themes;
using Quill.Shell.Settings;
using Quill.Shell.Themes;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M7.4, the theme system (15-themes.md), over a theme folder and settings in a temporary folder: the slot table
/// covers every slot of the style sheet and reads back what it writes; the catalog copies, saves, reloads and
/// deletes (not a theme in use); the theme list shows, searches, copies and deletes; the configuration page is
/// read-only for a built-in theme, and for a user theme changes the preview at once (no new projection), saves,
/// resets and searches.
/// </summary>
internal static class ThemeSystemProbes
{
    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        string folder = Directory.CreateTempSubdirectory("quill-themes-").FullName;
        try
        {
            CheckSlots(context);
            var settings = new SettingsStore(Path.Combine(folder, "settings.json"));
            var catalog = new ThemeCatalog(new ThemeStore(Path.Combine(folder, "themes")));
            var copy = CheckCatalog(context, catalog, settings, folder);
            await CheckListAsync(context, catalog, settings);
            await CheckEditorAsync(context, catalog, copy);
            await CheckMemoryReclamationAsync(context, catalog, settings, copy);
        }
        finally
        {
            context.Stage.Children.Clear();
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void CheckSlots(SelfTestContext context)
    {
        var properties = typeof(MdStyleSheet).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.SetMethod is not null && property.Name != "EqualityContract")
            .Select(static property => property.Name)
            .ToList();
        var covered = StyleSlots.All.Select(static slot => slot.Name.StartsWith("HeadingScale", StringComparison.Ordinal) ? "HeadingScales" : slot.Name).ToHashSet();
        var missing = properties.Where(name => !covered.Contains(name)).ToList();
        context.Check(missing.Count == 0, "themes: the slot table covers every style slot", string.Join(", ", missing));
        context.Check(StyleSlots.All.Select(static slot => slot.Name).Distinct().Count() == StyleSlots.All.Count, "themes: slot names are unique");

        var broken = new List<string>();
        foreach (var sheet in new[] { MdStyleSheet.Light, MdStyleSheet.Dark })
        {
            foreach (var slot in StyleSlots.All)
            {
                string value = slot.Get(sheet);
                if (slot.Set(sheet, value) is not { } back || slot.Get(back) != value)
                {
                    broken.Add($"{slot.Name}={value}");
                }
            }
        }

        context.Check(broken.Count == 0, "themes: every slot reads back the value it writes", string.Join(", ", broken));

        var differences = StyleSlots.Differences(MdStyleSheet.Light, MdStyleSheet.Dark);
        var rebuilt = StyleSlots.Apply(MdStyleSheet.Light, differences);
        context.Check(StyleSlots.Differences(MdStyleSheet.Light, MdStyleSheet.Light).Count == 0 && differences.Count > 10
            && StyleSlots.All.All(slot => slot.Get(rebuilt) == slot.Get(MdStyleSheet.Dark)),
            "themes: a theme is its base plus the slots that differ", $"{differences.Count} slots differ");

        var ignored = StyleSlots.Apply(MdStyleSheet.Light, new Dictionary<string, string> { ["Future"] = "1", ["FontSize"] = "huge", ["PageBackground"] = "#123456" });
        context.Check(StyleSlot.FormatColor(ignored.PageBackground) == "#123456" && ignored.FontSize == MdStyleSheet.Light.FontSize,
            "themes: unknown slots and values that are not valid are left out");

        var expectedCodeBlockPadding = new Thickness(12, 6, 12, 6);
        context.Check(MdStyleSheet.Light.CodeBlockPadding == expectedCodeBlockPadding && MdStyleSheet.Dark.CodeBlockPadding == expectedCodeBlockPadding,
            "themes: Light and Dark default CodeBlockPadding is 12,6,12,6");

        var codePaddingSlot = StyleSlots.Find("CodePadding");
        var codeForegroundSlot = StyleSlots.Find("CodeForeground");
        var updatedPadding = codePaddingSlot?.Set(MdStyleSheet.Light, "4,3,4,3");
        var rejectedPadding = codePaddingSlot?.Set(MdStyleSheet.Light, "50");
        var updatedForeground = codeForegroundSlot?.Set(MdStyleSheet.Light, "#C7254E");
        var clearedForeground = updatedForeground is { } fg ? codeForegroundSlot?.Set(fg, string.Empty) : null;
        context.Check(updatedPadding is { CodePadding.Left: 4, CodePadding.Top: 3, CodePadding.Right: 4, CodePadding.Bottom: 3 }
            && rejectedPadding is null
            && updatedForeground?.CodeForeground == Windows.UI.Color.FromArgb(0xFF, 0xC7, 0x25, 0x4E)
            && clearedForeground is { CodeForeground: null },
            "themes: CodePadding validates <= 40 and CodeForeground accepts optional color or empty");

        var hc = MdStyleSheet.HighContrast();
        bool tokensOk = StyleSlot.FormatColor(MdStyleSheet.Light.PageBackground) == "#FFFFFF"
            && StyleSlot.FormatColor(MdStyleSheet.Light.Foreground) == "#1F2328"
            && StyleSlot.FormatColor(MdStyleSheet.Light.LinkForeground) == "#0969DA"
            && StyleSlot.FormatColor(MdStyleSheet.Light.CodeBlockBackground) == "#F6F8FA"
            && MdStyleSheet.Light.BlockBoldWeight == 600
            && MdStyleSheet.Light.HeadingScales.SequenceEqual([2.0f, 1.5f, 1.25f, 1.0f, 0.875f, 0.85f])
            && StyleSlot.FormatColor(MdStyleSheet.Dark.PageBackground) == "#212830"
            && StyleSlot.FormatColor(MdStyleSheet.Dark.Foreground) == "#D1D7E0"
            && StyleSlot.FormatColor(MdStyleSheet.Dark.LinkForeground) == "#478BE6"
            && StyleSlot.FormatColor(MdStyleSheet.Dark.CodeBlockBackground) == "#262C36"
            && hc.HighlightForeground == hc.Foreground;
        context.Check(tokensOk, "themes: default Light, Dark, and HighContrast match MIT Primer / github-markdown-css tokens");
    }

    private static ThemeInfo CheckCatalog(SelfTestContext context, ThemeCatalog catalog, SettingsStore settings, string folder)
    {
        context.Check(catalog.Themes.Select(static theme => theme.Id).SequenceEqual(["github", "github-dark-dimmed"]) && catalog.Themes.All(static theme => theme.IsBuiltIn),
            "themes: the built-in themes come first and are read-only");
        var copy = catalog.Copy("github-dark-dimmed");
        var second = catalog.Copy("github-dark-dimmed");
        context.Check(copy is { Name: "Github Dark Dimmed copy", IsBuiltIn: false, BasedOn: "github-dark-dimmed" } && second.Name == "Github Dark Dimmed copy 2"
            && File.Exists(Path.Combine(folder, "themes", copy.Id + ".json")),
            "themes: copying makes a user theme named after the original", $"{copy.Name}, {second.Name}");

        var edited = catalog.Save(copy.Id, "Warm", "Sepia tones", copy.Sheet with { PageBackground = Windows.UI.Color.FromArgb(0xFF, 0xF4, 0xEC, 0xD8), FontSize = 18 });
        var reloaded = new ThemeCatalog(new ThemeStore(Path.Combine(folder, "themes"))).Find(copy.Id);
        context.Check(reloaded is { Name: "Warm", Description: "Sepia tones" } && reloaded.Sheet.FontSize == 18 && StyleSlot.FormatColor(reloaded.Sheet.PageBackground) == "#F4ECD8"
            && reloaded.Sheet.Foreground == MdStyleSheet.Dark.Foreground && edited.Modified >= edited.Created,
            "themes: a saved theme reads back the same");

        settings.Update(s => s with { DarkTheme = copy.Id });
        context.Check(!catalog.CanDelete(copy.Id, settings.Current) && catalog.CanDelete(second.Id, settings.Current) && !catalog.CanDelete("github", settings.Current),
            "themes: a theme in use and built-in themes cannot be deleted");
        catalog.Delete(second.Id);
        context.Check(catalog.Find(second.Id) is null && !File.Exists(Path.Combine(folder, "themes", second.Id + ".json")), "themes: deleting removes the theme and its file");
        return catalog.Find(copy.Id)!;
    }

    private static async Task CheckListAsync(SelfTestContext context, ThemeCatalog catalog, SettingsStore settings)
    {
        var page = new ThemesPage(catalog, settings) { Width = 1100, Height = 800, ConfirmDelete = static _ => Task.FromResult(true) };
        ThemeInfo? opened = null;
        page.ThemeOpened += (_, theme) => opened = theme;
        context.Stage.Children.Add(page);
        await Task.Delay(300);
        var cards = page.Cards;
        context.Check(cards.Count == 3 && cards.Select(static card => card.Theme.Name).SequenceEqual(["Github", "Github Dark Dimmed", "Warm"]),
            "themes: the list shows a card per theme", string.Join(", ", cards.Select(static card => card.Theme.Name)));
        bool twoColumns = cards.Count >= 2 && Math.Abs(Top(cards[0]) - Top(cards[1])) < 1 && Left(cards[1]) > Left(cards[0]) + 100;
        context.Check(twoColumns, "themes: a wide page shows two columns");

        var warm = cards.First(static card => card.Theme.Name == "Warm");
        context.Check(warm.Preview.Box.Document?.EffectiveStyleSheet.FontSize == 18 && cards[0].DeleteButton.Visibility == Visibility.Collapsed && !warm.DeleteButton.IsEnabled,
            "themes: a card renders its theme; built-in themes and themes in use cannot be deleted");

        page.Query = "sepia";
        context.Check(page.Cards.Select(static card => card.Theme.Name).SequenceEqual(["Warm"]), "themes: search filters by name and description");
        page.Query = string.Empty;
        var warmAfterFilter = page.Cards.FirstOrDefault(card => card.Theme.Id == warm.Theme.Id);
        context.Check(ReferenceEquals(warm, warmAfterFilter), "themes: search filtering reuses theme cards without rebuilding them");

        Invoke(page.Cards[0].CopyButton);
        await Task.Delay(100);
        var github = page.Cards.FirstOrDefault(static card => card.Theme.Name == "Github copy");
        context.Check(github is not null && github.DeleteButton.IsEnabled, "themes: Copy adds a user theme to the list");
        if (github is not null)
        {
            Invoke(github.DeleteButton);
            await Task.Delay(100);
        }

        context.Check(page.Cards.All(static card => card.Theme.Name != "Github copy"), "themes: Delete removes a user theme after asking");

        Invoke(page.Cards[0].OpenButton);
        context.Check(opened?.Id == "github", "themes: a card opens its theme");

        page.Width = 600;
        await Task.Delay(200);
        context.Check(page.Cards.Count >= 2 && Left(page.Cards[0]) == Left(page.Cards[1]), "themes: a narrow page shows one column");
        context.Stage.Children.Clear();
        await Task.Delay(100);
        context.Check(page.Cards.Count == 0, "themes: unloading the theme list releases theme cards");
    }

    private static async Task CheckEditorAsync(SelfTestContext context, ThemeCatalog catalog, ThemeInfo user)
    {
        var builtIn = new ThemeEditorPage(catalog, catalog.Find("github")!) { Width = 1100, Height = 800 };
        context.Stage.Children.Add(builtIn);
        await Task.Delay(300);
        bool readOnly = builtIn.SaveButton.Visibility == Visibility.Collapsed && builtIn.CopyButton.Visibility == Visibility.Visible && !builtIn.NameBox.IsEnabled;
        context.Check(readOnly, "themes: a built-in theme is read-only and can be copied");
        context.Stage.Children.Clear();

        var page = new ThemeEditorPage(catalog, user) { Width = 1100, Height = 800 };
        context.Stage.Children.Add(page);
        await Task.Delay(400);
        context.Check(page.SharedColorPicker is null, "themes: no ColorPicker is created until a color swatch is opened");
        bool openedBg = page.OpenColorSlot("PageBackground");
        await Task.Delay(80);
        var firstPicker = page.SharedColorPicker;
        if (firstPicker is not null)
        {
            firstPicker.Color = Windows.UI.Color.FromArgb(0xFF, 0x11, 0x22, 0x33);
            await Task.Delay(40);
        }

        bool updatedBg = StyleSlot.FormatColor(page.Sheet.PageBackground) == "#112233";
        bool openedFg = page.OpenColorSlot("Foreground");
        await Task.Delay(80);
        context.Check(openedBg && openedFg && firstPicker is not null && ReferenceEquals(firstPicker, page.SharedColorPicker) && updatedBg,
            "themes: color swatches lazily reuse one shared ColorPicker and update the edited sheet");

        var box = page.Preview.Box;
        var projection = box.CurrentProjection;
        var slot = StyleSlots.Find("FontSize")!;
        page.Change(slot, "20");
        await Task.Delay(100);
        context.Check(box.Document?.EffectiveStyleSheet.FontSize == 20 && ReferenceEquals(projection, box.CurrentProjection) && page.HasChanges && page.SaveButton.IsEnabled,
            "themes: a change shows in the preview at once, without a new projection");

        page.Reset();
        context.Check(page.Sheet.FontSize == 18 && !page.HasChanges && box.Document?.EffectiveStyleSheet.FontSize == 18, "themes: Reset goes back to what was saved");

        page.Change(StyleSlots.Find("LinkUnderline")!, "true");
        page.NameBox.Text = "Warm 2";
        await Task.Delay(60);
        page.Save();
        var saved = catalog.Find(user.Id);
        context.Check(saved is { Name: "Warm 2" } && saved.Sheet.LinkUnderline && saved.Sheet.FontSize == 18 && !page.HasChanges, "themes: Save writes the theme");

        page.Query = "quote";
        var shown = page.Rows.Where(static row => row.Row.Visibility == Visibility.Visible).Select(static row => row.Slot.Name).ToList();
        context.Check(shown.Count > 0 && shown.All(static name => name.Contains("Quote", StringComparison.Ordinal))
            && page.HeaderOf(ThemeGroup.Quotes).Visibility == Visibility.Visible && page.HeaderOf(ThemeGroup.Tables).Visibility == Visibility.Collapsed,
            "themes: search shows the matching slots and their groups", string.Join(", ", shown));
        page.Query = string.Empty;
        context.Stage.Children.Clear();
    }

    private static async Task CheckMemoryReclamationAsync(SelfTestContext context, ThemeCatalog catalog, SettingsStore settings, ThemeInfo user)
    {
        var themesRef = await MountAndDetachThemesPageAsync(context, catalog, settings);
        var editorRef = await MountAndDetachThemeEditorPageAsync(context, catalog, user);
        var settingsRef = await MountAndDetachSettingsPageAsync(context, settings);

        await Task.Delay(150);
        CanvasDevice.GetSharedDevice().Trim();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        await Task.Delay(50);
        CanvasDevice.GetSharedDevice().Trim();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        bool themesCollected = !themesRef.TryGetTarget(out _);
        bool editorCollected = !editorRef.TryGetTarget(out _);
        bool settingsCollected = !settingsRef.TryGetTarget(out _);
        context.Check(themesCollected && editorCollected && settingsCollected,
            "themes: detached ThemesPage, ThemeEditorPage, and SettingsPage are reclaimed by GC",
            $"themesAlive={!themesCollected}, editorAlive={!editorCollected}, settingsAlive={!settingsCollected}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference<object>> MountAndDetachThemesPageAsync(SelfTestContext context, ThemeCatalog catalog, SettingsStore settings)
    {
        var page = new ThemesPage(catalog, settings) { Width = 1000, Height = 700 };
        context.Stage.Children.Add(page);
        await Task.Delay(250);
        context.Stage.Children.Clear();
        return new WeakReference<object>(page);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference<object>> MountAndDetachThemeEditorPageAsync(SelfTestContext context, ThemeCatalog catalog, ThemeInfo user)
    {
        var page = new ThemeEditorPage(catalog, user) { Width = 1000, Height = 700 };
        context.Stage.Children.Add(page);
        await Task.Delay(250);
        page.OpenColorSlot("PageBackground");
        await Task.Delay(80);
        context.Stage.Children.Clear();
        return new WeakReference<object>(page);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference<object>> MountAndDetachSettingsPageAsync(SelfTestContext context, SettingsStore settings)
    {
        ThemeChoice[] themes = [new("github", "Github"), new("github-dark-dimmed", "Github Dark Dimmed")];
        var page = new SettingsPage(settings, themes) { Width = 1000, Height = 640 };
        context.Stage.Children.Add(page);
        await Task.Delay(150);
        context.Stage.Children.Clear();
        return new WeakReference<object>(page);
    }

    private static double Top(FrameworkElement element) => element.TransformToVisual(null).TransformPoint(default).Y;

    private static double Left(FrameworkElement element) => element.TransformToVisual(null).TransformPoint(default).X;

    private static void Invoke(Button button) =>
        (new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)
            as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)?.Invoke();
}
