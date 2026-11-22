using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Quill.Settings;
using Quill.Shell.Settings;

namespace Quill.Playground.SelfTest;

/// <summary>
/// The settings page (14-settings.md), hosted without the window over a store in a temporary folder: the
/// sections and their order, search (by name and description, hiding the list and the highlight), the
/// section list scrolling to a section and following the scroll, controls writing the store and showing
/// changes made elsewhere (another window), auto-save locking "auto save when switching files", the custom
/// extension's validation, the startup folder's picker, themes, and the narrow layout.
/// </summary>
internal static class SettingsProbes
{
    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        string folder = Directory.CreateTempSubdirectory("quill-settings-").FullName;
        try
        {
            var store = new SettingsStore(Path.Combine(folder, "settings.json"));
            ThemeChoice[] themes = [new("github", "Github"), new("github-dark-dimmed", "Github Dark Dimmed")];
            var page = new SettingsPage(store, themes) { Width = 1000, Height = 640, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            context.Stage.Children.Add(page);
            await Task.Delay(120);

            CheckSections(context, page);
            await CheckSearchAsync(context, page);
            CheckControls(context, page, store);
            await CheckExtensionAsync(context, page, store);
            await CheckStartupAsync(context, page, store);
            CheckAppearance(context, page, store);
            await CheckNavigationAsync(context, page);
            await CheckNarrowAsync(context, page);
        }
        finally
        {
            context.Stage.Children.Clear();
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void CheckSections(SelfTestContext context, SettingsPage page)
    {
        var titles = page.SectionList.Items.OfType<ListViewItem>().Select(static item => item.Tag).OfType<SettingsSection>().ToList();
        context.Check(titles.SequenceEqual(Enum.GetValues<SettingsSection>()) && page.HighlightedSection == SettingsSection.Files,
            "settings: the list shows the six sections in order, the first highlighted", string.Join(", ", titles));

        // Every section has settings, and each setting's card is in its section, below the section's title.
        bool ordered = true;
        foreach (var section in Enum.GetValues<SettingsSection>())
        {
            double header = Top(page, page.HeaderOf(section));
            var cards = page.Entries.Where(entry => entry.Section == section).Select(entry => Top(page, entry.Card)).ToList();
            ordered &= cards.Count > 0 && cards.All(top => top > header);
        }

        context.Check(ordered, "settings: every section has settings under its title");

        var unavailable = page.Entries.Where(static entry => !entry.IsAvailable).ToList();
        // Every setting is available since M7.3; language and spell checking say in their descriptions that they do nothing yet.
        context.Check(unavailable.Count == 0 && page.Entries.All(static entry => entry.Card.IsEnabled),
            "settings: every setting is available", string.Join(", ", unavailable.Select(static entry => entry.Name)));
    }

    private static async Task CheckSearchAsync(SelfTestContext context, SettingsPage page)
    {
        page.Query = "auto save";
        await Task.Delay(60);
        var shown = Shown(page);
        context.Check(shown.SequenceEqual(["Auto save", "Auto save when switching files"])
            && page.HeaderOf(SettingsSection.Files).Visibility == Visibility.Visible
            && Enum.GetValues<SettingsSection>().Skip(1).All(section => page.HeaderOf(section).Visibility == Visibility.Collapsed),
            "settings: search shows the matching settings and their section's title only", string.Join(", ", shown));
        context.Check(!page.SectionList.IsEnabled && page.HighlightedSection is null, "settings: while searching the section list is off and nothing is highlighted");

        page.Query = "words per minute";
        await Task.Delay(60);
        context.Check(Shown(page).SequenceEqual(["Reading speed"]), "settings: search matches descriptions", string.Join(", ", Shown(page)));

        page.Query = "no such setting";
        await Task.Delay(60);
        context.Check(Shown(page).Count == 0 && page.NoMatchText.Visibility == Visibility.Visible, "settings: a search without matches says so");

        page.Query = string.Empty;
        await Task.Delay(60);
        context.Check(Shown(page).Count == page.Entries.Count && page.SectionList.IsEnabled && page.HighlightedSection is not null && page.NoMatchText.Visibility == Visibility.Collapsed,
            "settings: clearing the search shows everything and the list again");
    }

    private static void CheckControls(SelfTestContext context, SettingsPage page, SettingsStore store)
    {
        var autoSave = Control<ToggleSwitch>(page, "Auto save");
        var onSwitch = Control<ToggleSwitch>(page, "Auto save when switching files");
        context.Check(autoSave is { IsOn: false } && onSwitch is { IsOn: false, IsEnabled: true }, "settings: the toggles show the defaults");
        if (autoSave is null || onSwitch is null)
        {
            return;
        }

        autoSave.IsOn = true;
        context.Check(store.Current.AutoSave && onSwitch is { IsOn: true, IsEnabled: false } && !store.Current.AutoSaveOnSwitch,
            "settings: auto save is saved at once, and turns switching files on and read-only");
        autoSave.IsOn = false;
        context.Check(!store.Current.AutoSave && onSwitch is { IsOn: false, IsEnabled: true }, "settings: without auto save, switching files can be chosen again");
        onSwitch.IsOn = true;
        context.Check(store.Current.AutoSaveOnSwitch, "settings: auto save when switching files is saved");

        // A change from another window shows here.
        store.Update(static settings => settings with { ShowStatusBar = false, DefaultLineBreak = LineBreakChoice.Lf });
        context.Check(Control<ToggleSwitch>(page, "Show status bar") is { IsOn: false } && Control<ComboBox>(page, "Default line ending") is { SelectedIndex: 0 },
            "settings: a change made elsewhere shows on the page");

        var wrap = Control<ToggleSwitch>(page, "Wrap lines in code blocks");
        if (wrap is not null)
        {
            wrap.IsOn = false;
        }

        context.Check(!store.Current.WrapCode, "settings: code wrapping is saved");
    }

    private static async Task CheckExtensionAsync(SelfTestContext context, SettingsPage page, SettingsStore store)
    {
        var box = Control<ComboBox>(page, "Default file extension");
        var custom = page.CustomExtensionBox;
        context.Check(box is { SelectedIndex: 0 } && custom is { Visibility: Visibility.Collapsed }, "settings: the extension is .md, the custom box hidden");
        if (box is null || custom is null)
        {
            return;
        }

        box.SelectedIndex = 3;
        context.Check(custom.Visibility == Visibility.Visible && store.Current.DefaultExtension == FileExtensionChoice.Md,
            "settings: choosing a custom extension shows its box, nothing is saved until it is valid");
        // A text box raises TextChanged after the text changed, on the dispatcher.
        custom.Text = "mdx";
        await Task.Delay(40);
        var card = (SettingsCard)page.Entries.First(static entry => entry.Name == "Default file extension").Card;
        context.Check(store.Current.DefaultExtension == FileExtensionChoice.Md && card.Description is string error && error.Contains("starts with a dot", StringComparison.Ordinal),
            "settings: an extension without a dot is not saved and the card says why", card.Description?.ToString());
        custom.Text = ".mdx";
        await Task.Delay(40);
        context.Check(store.Current is { DefaultExtension: FileExtensionChoice.Custom, CustomExtension: ".mdx", FileExtension: ".mdx" },
            "settings: a valid custom extension is saved");
        box.SelectedIndex = 2;
        await Task.Delay(40);
        context.Check(store.Current.FileExtension == ".txt" && custom.Visibility == Visibility.Collapsed, "settings: choosing Text (.txt) hides the custom box");
    }

    private static async Task CheckStartupAsync(SelfTestContext context, SettingsPage page, SettingsStore store)
    {
        var box = page.StartupBox;
        if (box is null)
        {
            context.Check(false, "settings: the startup box exists");
            return;
        }

        page.FolderPicker = static () => Task.FromResult<string?>(null);
        box.SelectedIndex = 3;
        await Task.Delay(40);
        context.Check(store.Current.Startup == StartupAction.NewFile && box.SelectedIndex == 0,
            "settings: cancelling the folder picker keeps the previous startup choice", $"{store.Current.Startup}, index {box.SelectedIndex}");

        string chosen = Path.Combine(Path.GetTempPath(), "quill notes");
        page.FolderPicker = () => Task.FromResult<string?>(chosen);
        box.SelectedIndex = 3;
        await Task.Delay(40);
        context.Check(store.Current is { Startup: StartupAction.Folder } && store.Current.StartupFolder == chosen && box.SelectedIndex == 3 && box.Items[3] as string == "quill notes",
            "settings: a chosen startup folder is saved and shows by its name only", box.Items[3]?.ToString());

        box.SelectedIndex = 1;
        context.Check(store.Current.Startup == StartupAction.LastFolder, "settings: choosing the last folder is saved");
    }

    private static void CheckAppearance(SelfTestContext context, SettingsPage page, SettingsStore store)
    {
        var light = Control<ComboBox>(page, "Light theme");
        context.Check(light is { SelectedIndex: 0, Items.Count: 2 } && light.Items[0] as string == "Github", "settings: the light theme lists the themes, Github chosen");
        if (light is not null)
        {
            light.SelectedIndex = 1;
        }

        context.Check(store.Current.LightTheme == "github-dark-dimmed", "settings: any theme can be the light theme");

        store.Update(static settings => settings with { DarkTheme = "deleted-theme" });
        context.Check(Control<ComboBox>(page, "Dark theme") is { SelectedIndex: 1 }, "settings: a theme that no longer exists shows the default it falls back to");

        var speed = page.ReadingSpeedBox;
        if (speed is not null)
        {
            speed.Value = 240;
        }

        context.Check(store.Current.ReadingSpeed == 240, "settings: the reading speed is saved");

        bool requested = false;
        page.ThemesRequested += (_, _) => requested = true;
        // The card's Click calls the same handler; clicking needs input, which the tests do not simulate (08).
        page.OnThemesClick();
        context.Check(page.ThemesCard is { IsClickEnabled: true, IsEnabled: true } && requested, "settings: theme configuration is clickable and asks for the theme list");
        store.Update(static settings => settings with { AppTheme = AppTheme.Dark });
        context.Check(Control<ComboBox>(page, "App theme") is { SelectedIndex: 1 }, "settings: the app theme shows the stored choice");
    }

    private static async Task CheckNavigationAsync(SelfTestContext context, SettingsPage page)
    {
        await Task.Delay(100);
        page.NavigateTo(SettingsSection.Markdown);
        await Task.Delay(120);
        double top = Top(page, page.HeaderOf(SettingsSection.Markdown)) - page.Scroller.VerticalOffset;
        context.Check(page.HighlightedSection == SettingsSection.Markdown && top is >= 0 and < 48,
            "settings: a section in the list scrolls to its title", $"title at {top:F0}, offset {page.Scroller.VerticalOffset:F0}");

        // Scrolling by hand moves the highlight to the section in view.
        page.Scroller.ChangeView(null, Top(page, page.HeaderOf(SettingsSection.Images)) - 4, null, disableAnimation: true);
        await Task.Delay(120);
        context.Check(page.HighlightedSection == SettingsSection.Images, "settings: scrolling highlights the section in view", page.HighlightedSection?.ToString());

        page.Scroller.ChangeView(null, 0, null, disableAnimation: true);
        await Task.Delay(120);
        context.Check(page.HighlightedSection == SettingsSection.Files, "settings: at the top the first section is highlighted", page.HighlightedSection?.ToString());

        page.Scroller.ChangeView(null, page.Scroller.ScrollableHeight, null, disableAnimation: true);
        await Task.Delay(120);
        context.Check(page.HighlightedSection == SettingsSection.General, "settings: at the end the last section is highlighted", page.HighlightedSection?.ToString());
    }

    private static async Task CheckNarrowAsync(SelfTestContext context, SettingsPage page)
    {
        page.Width = 560;
        await Task.Delay(80);
        var card = page.Entries[0].Card;
        context.Check(page.SectionList.Visibility == Visibility.Collapsed && card.ActualWidth > 400,
            "settings: a narrow page hides the section list and gives the settings the width", $"card {card.ActualWidth:F0}");
        page.Width = 1000;
        await Task.Delay(80);
        context.Check(page.SectionList.Visibility == Visibility.Visible, "settings: a wide page shows the section list again");
        page.Width = 0;
        await Task.Delay(50);
        context.Check(page.SectionList.Visibility == Visibility.Visible, "settings: a zero-width layout pass while minimized keeps the wide layout");
        page.Width = 1000;
        await Task.Delay(50);
    }

    /// <summary>The names of the settings shown.</summary>
    private static List<string> Shown(SettingsPage page) =>
        page.Entries.Where(static entry => entry.Card.Visibility == Visibility.Visible).Select(static entry => entry.Name).ToList();

    /// <summary>The control of a setting: the card's content, or the first control of that kind in it.</summary>
    private static T? Control<T>(SettingsPage page, string name)
        where T : DependencyObject
    {
        var card = page.Entries.First(entry => entry.Name == name).Card as SettingsCard;
        return card?.Content switch
        {
            T control => control,
            DependencyObject content => Descendants(content).OfType<T>().FirstOrDefault() ?? Children(content).OfType<T>().FirstOrDefault(),
            _ => null,
        };
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject root) =>
        root is Panel panel ? panel.Children.SelectMany(static child => Children(child).Prepend(child)) : [];

    private static double Top(SettingsPage page, FrameworkElement element) =>
        element.TransformToVisual((UIElement)page.Scroller.Content).TransformPoint(default).Y;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
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
