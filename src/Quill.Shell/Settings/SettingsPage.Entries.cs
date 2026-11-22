using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.WinUI.Localization;
using Quill.Settings;

namespace Quill.Shell.Settings;

/// <summary>
/// The settings of each section (14-settings.md). All strings resolve from <see cref="UiStrings"/>
/// so live language changes refresh every card, description, search entry and choice list in place.
/// </summary>
public sealed partial class SettingsPage
{
    private const bool EditingFeatures = true;
    private const bool FileFeatures = true;

    private static readonly (FileExtensionChoice Choice, string Key)[] Extensions =
    [
        (FileExtensionChoice.Md, "settings.extension.opt.Md"),
        (FileExtensionChoice.Markdown, "settings.extension.opt.Markdown"),
        (FileExtensionChoice.Txt, "settings.extension.opt.Txt"),
        (FileExtensionChoice.Custom, "settings.extension.opt.Custom"),
    ];

    private static readonly (string Tag, string Key)[] SpellCheckLanguages =
    [
        (AppSettings.AutoSpellCheckLanguage, "settings.spell.auto"),
        (AppSettings.NoSpellCheckLanguage, "settings.spell.off"),
        ("en-US", "settings.spell.en-US"),
        ("en-GB", "settings.spell.en-GB"),
        ("zh-CN", "settings.spell.zh-CN"),
        ("zh-TW", "settings.spell.zh-TW"),
        ("ja-JP", "settings.spell.ja-JP"),
        ("ko-KR", "settings.spell.ko-KR"),
        ("fr-FR", "settings.spell.fr-FR"),
        ("de-DE", "settings.spell.de-DE"),
        ("es-ES", "settings.spell.es-ES"),
        ("ru-RU", "settings.spell.ru-RU"),
    ];

    /// <summary>The box of the startup action, for tests.</summary>
    internal ComboBox? StartupBox { get; private set; }

    /// <summary>The box of the custom extension, for tests.</summary>
    internal TextBox? CustomExtensionBox { get; private set; }

    /// <summary>The box of the reading speed, for tests.</summary>
    internal NumberBox? ReadingSpeedBox { get; private set; }

    /// <summary>The state of File Explorer's New menu, and its buttons, for tests.</summary>
    internal TextBlock? NewMenuStatus { get; private set; }

    internal Button? AddButton { get; private set; }

    internal Button? RemoveButton { get; private set; }

    /// <summary>File Explorer's New menu; without it (tests of other settings) the card shows no state.</summary>
    public Quill.Settings.Explorer.ExplorerNewMenu? NewMenu
    {
        get => _newMenu;
        set
        {
            _newMenu = value;
            UpdateNewMenu();
        }
    }

    private Quill.Settings.Explorer.ExplorerNewMenu? _newMenu;
    private bool _newMenuError;

    /// <summary>Adds or removes the New menu entry, then shows its state; a registry that refuses is reported on the card.</summary>
    private void ChangeNewMenu(bool add)
    {
        try
        {
            _newMenuError = false;
            if (add)
            {
                _newMenu?.Add();
            }
            else
            {
                _newMenu?.Remove();
            }

            UpdateNewMenu();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _newMenuError = true;
            UpdateNewMenu();
            if (NewMenuStatus is not null)
            {
                NewMenuStatus.Text = UiStrings.Get("settings.newMenu.error");
                ToolTipService.SetToolTip(NewMenuStatus, error.Message);
            }
        }
    }

    private void UpdateNewMenu()
    {
        if (NewMenuStatus is null || AddButton is null || RemoveButton is null)
        {
            return;
        }

        if (_newMenuError)
        {
            NewMenuStatus.Text = UiStrings.Get("settings.newMenu.error");
            return;
        }

        var state = _newMenu?.State;
        NewMenuStatus.Text = state switch
        {
            Quill.Settings.Explorer.NewMenuState.AddedByQuill => UiStrings.Get("settings.newMenu.added"),
            Quill.Settings.Explorer.NewMenuState.AddedElsewhere => UiStrings.Get("settings.newMenu.addedElsewhere"),
            Quill.Settings.Explorer.NewMenuState.NotAdded => UiStrings.Get("settings.newMenu.notAdded"),
            _ => string.Empty,
        };
        ToolTipService.SetToolTip(NewMenuStatus, null);
        AddButton.IsEnabled = state == Quill.Settings.Explorer.NewMenuState.NotAdded;
        RemoveButton.IsEnabled = state == Quill.Settings.Explorer.NewMenuState.AddedByQuill;
    }

    /// <summary>The card that opens the theme list, for tests.</summary>
    internal SettingsCard? ThemesCard { get; private set; }

    /// <summary>The theme configuration card was clicked.</summary>
    internal void OnThemesClick() => ThemesRequested?.Invoke(this, EventArgs.Empty);

    private void BuildEntries()
    {
        AddFiles();
        AddEditing();
        AddImages();
        AddMarkdown();
        AddAppearance();
        AddGeneral();
    }

    private void AddFiles()
    {
        AddStartup();
        AddExtension();
        Add(SettingsSection.Files, "\uE74E", "settings.autoSave.name", "settings.autoSave.desc",
            Toggle("settings.autoSave.name", static s => s.AutoSave, static (s, on) => s with { AutoSave = on }));

        // On and read-only while auto-save is on (Q104).
        var onSwitch = Toggle("settings.autoSaveSwitch.name", static s => s.SavesOnSwitch, static (s, on) => s with { AutoSaveOnSwitch = on });
        _refreshers.Add(settings => onSwitch.IsEnabled = !settings.AutoSave);
        Add(SettingsSection.Files, "\uE8AB", "settings.autoSaveSwitch.name", "settings.autoSaveSwitch.desc", onSwitch);

        Add(SettingsSection.Files, "\uE8B7", "settings.dropFolder.name", "settings.dropFolder.desc",
            Choice("settings.dropFolder.name", [(FolderDropAction.Open, "settings.drop.open"), (FolderDropAction.InsertLink, "settings.drop.insertLink")],
                static s => s.DropFolder, static (s, v) => s with { DropFolder = v }), FileFeatures);
        Add(SettingsSection.Files, "\uE8A5", "settings.dropMarkdown.name", "settings.dropMarkdown.desc",
            Choice("settings.dropMarkdown.name", [(MarkdownDropAction.Open, "settings.drop.open"), (MarkdownDropAction.InsertLink, "settings.drop.insertLink")],
                static s => s.DropMarkdown, static (s, v) => s with { DropMarkdown = v }), FileFeatures);
        Add(SettingsSection.Files, "\uE7C3", "settings.dropOther.name", "settings.dropOther.desc",
            Choice("settings.dropOther.name", [(OtherFileDropAction.InsertLink, "settings.drop.insertLink"), (OtherFileDropAction.None, "settings.drop.doNothing")],
                static s => s.DropOtherFile, static (s, v) => s with { DropOtherFile = v }), FileFeatures);
    }

    /// <summary>
    /// The startup action. Choosing a folder asks for it: cancelling keeps the previous choice, choosing shows
    /// the folder's name only (14-settings.md). Browse picks another folder.
    /// </summary>
    private void AddStartup()
    {
        var box = new ComboBox { Width = ControlWidth };
        LocalizeComboBox(box, "settings.startup.name", () =>
        [
            UiStrings.Get("settings.startup.opt.0"),
            UiStrings.Get("settings.startup.opt.1"),
            UiStrings.Get("settings.startup.opt.2"),
            _store.Current.StartupFolder is { } folder ? FolderName(folder) : UiStrings.Get("settings.startup.opt.3"),
        ]);
        var browse = BrowseButton("settings.startup.browse");
        StartupBox = box;
        _refreshers.Add(settings =>
        {
            box.SelectedIndex = (int)settings.Startup;
            if (box.Items.Count > 3)
            {
                box.Items[3] = settings.StartupFolder is { } folder ? FolderName(folder) : UiStrings.Get("settings.startup.opt.3");
            }

            box.SelectedIndex = (int)settings.Startup;
            ToolTipService.SetToolTip(box, settings.Startup == StartupAction.Folder ? settings.StartupFolder : null);
            browse.Visibility = settings.Startup == StartupAction.Folder ? Visibility.Visible : Visibility.Collapsed;
        });
        box.SelectionChanged += async (_, _) =>
        {
            if (_refreshing || box.SelectedIndex < 0)
            {
                return;
            }

            var action = (StartupAction)box.SelectedIndex;
            if (action == StartupAction.Folder && _store.Current.Startup != StartupAction.Folder)
            {
                await ChooseStartupFolderAsync();
                return;
            }

            Change(settings => settings with { Startup = action });
        };
        browse.Click += async (_, _) => await ChooseStartupFolderAsync();
        Add(SettingsSection.Files, "\uE7E8", "settings.startup.name", "settings.startup.desc", Row(browse, box));
    }

    private async Task ChooseStartupFolderAsync()
    {
        string? folder = FolderPicker is null ? null : await FolderPicker();
        if (folder is null)
        {
            // Cancelled: the previous choice stays.
            Refresh();
            return;
        }

        _store.Update(settings => settings with { Startup = StartupAction.Folder, StartupFolder = folder });
        Refresh();
    }

    /// <summary>The save dialog's extension; "Custom" shows a box whose text is saved once it is a valid extension.</summary>
    private void AddExtension()
    {
        var box = new ComboBox { Width = ControlWidth };
        LocalizeComboBox(box, "settings.extension.name", () => Extensions.Select(static option => UiStrings.Get(option.Key)));
        var custom = TextField("settings.extension.customField", ".md");
        CustomExtensionBox = custom;
        SettingsCard? card = null;
        SettingEntry? entry = null;
        bool invalidShown = false;
        void ShowCustom(bool show, bool valid)
        {
            custom.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            invalidShown = show && !valid;
            string shown = invalidShown ? UiStrings.Get("settings.extension.invalid") : UiStrings.Get("settings.extension.desc");
            if (card is not null)
            {
                card.Description = shown;
            }

            if (entry is not null)
            {
                entry.Description = shown;
            }
        }

        _refreshers.Add(settings =>
        {
            box.SelectedIndex = (int)settings.DefaultExtension;
            custom.Text = settings.CustomExtension ?? string.Empty;
            ShowCustom(settings.DefaultExtension == FileExtensionChoice.Custom, valid: true);
        });
        box.SelectionChanged += (_, _) =>
        {
            if (_refreshing || box.SelectedIndex < 0)
            {
                return;
            }

            var choice = (FileExtensionChoice)box.SelectedIndex;
            if (choice == FileExtensionChoice.Custom)
            {
                // Saved once the box holds a valid extension.
                bool valid = AppSettings.IsValidExtension(custom.Text);
                ShowCustom(true, valid || custom.Text.Length == 0);
                if (valid)
                {
                    Change(settings => settings with { DefaultExtension = choice, CustomExtension = custom.Text });
                }

                return;
            }

            ShowCustom(false, true);
            Change(settings => settings with { DefaultExtension = choice });
        };
        custom.TextChanged += (_, _) =>
        {
            if (_refreshing)
            {
                return;
            }

            bool valid = AppSettings.IsValidExtension(custom.Text);
            ShowCustom(true, valid);
            if (valid)
            {
                Change(settings => settings with { DefaultExtension = FileExtensionChoice.Custom, CustomExtension = custom.Text });
            }
        };
        card = Add(SettingsSection.Files, "\uE8A5", "settings.extension.name", "settings.extension.desc", Row(custom, box),
            descriptionOverride: () => invalidShown ? UiStrings.Get("settings.extension.invalid") : UiStrings.Get("settings.extension.desc"));
        entry = _entries[^1];
    }

    private void AddEditing()
    {
        Add(SettingsSection.Editing, "\uE943", "settings.matchBrackets.name", "settings.matchBrackets.desc",
            Toggle("settings.matchBrackets.name", static s => s.MatchBrackets, static (s, on) => s with { MatchBrackets = on }), EditingFeatures);
        Add(SettingsSection.Editing, "\uE8C8", "settings.copySource.name", "settings.copySource.desc",
            Toggle("settings.copySource.name", static s => s.CopyMarkdownSource, static (s, on) => s with { CopyMarkdownSource = on }), EditingFeatures);
        Add(SettingsSection.Editing, "\uE8C6", "settings.copyLine.name", "settings.copyLine.desc",
            Toggle("settings.copyLine.name", static s => s.CopyLineWithoutSelection, static (s, on) => s with { CopyLineWithoutSelection = on }), EditingFeatures);
        Add(SettingsSection.Editing, "\uE751", "settings.lineEnding.name", "settings.lineEnding.desc",
            Choice("settings.lineEnding.name", [(LineBreakChoice.Lf, "settings.lineEnding.lf"), (LineBreakChoice.CrLf, "settings.lineEnding.crlf")],
                static s => s.DefaultLineBreak, static (s, v) => s with { DefaultLineBreak = v }));
        Add(SettingsSection.Editing, "\uE8D2", "settings.spellCheck.name", "settings.spellCheck.desc",
            Choice("settings.spellCheck.name", SpellCheckLanguages.Select(static l => (l.Tag, l.Key)).ToList(),
                static s => s.SpellCheckLanguage, static (s, v) => s with { SpellCheckLanguage = v }));
    }

    private void AddImages()
    {
        var rule = Choice("settings.imageRule.name",
            [
                (ImageInsertRule.None, "settings.imageRule.none"),
                (ImageInsertRule.CopyToCurrentFolder, "settings.imageRule.current"),
                (ImageInsertRule.CopyToAssets, "settings.imageRule.assets"),
                (ImageInsertRule.CopyToFileAssets, "settings.imageRule.fileAssets"),
                (ImageInsertRule.CopyToFolder, "settings.imageRule.folder"),
            ],
            static s => s.ImageRule, static (s, v) => s with { ImageRule = v });
        var folder = TextField("settings.imageRule.folderField", "./images");
        var browse = BrowseButton("settings.imageRule.browse");
        _refreshers.Add(settings =>
        {
            folder.Text = settings.ImageFolder ?? string.Empty;
            bool shown = settings.ImageRule == ImageInsertRule.CopyToFolder;
            folder.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            browse.Visibility = folder.Visibility;
        });
        folder.LostFocus += (_, _) => Change(settings => settings with { ImageFolder = folder.Text.Trim() is { Length: > 0 } text ? text : null });
        browse.Click += async (_, _) =>
        {
            if (FolderPicker is not null && await FolderPicker() is { } chosen)
            {
                _store.Update(settings => settings with { ImageFolder = chosen });
            }
        };
        Add(SettingsSection.Images, "\uE91B", "settings.imageRule.name", "settings.imageRule.desc",
            new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { rule, Row(folder, browse) } }, FileFeatures);
        Add(SettingsSection.Images, "\uE8B7", "settings.imageLocal.name", "settings.imageLocal.desc",
            Toggle("settings.imageLocal.name", static s => s.ApplyImageRuleToLocal, static (s, on) => s with { ApplyImageRuleToLocal = on }), FileFeatures);
        Add(SettingsSection.Images, "\uE774", "settings.imageOnline.name", "settings.imageOnline.desc",
            Toggle("settings.imageOnline.name", static s => s.ApplyImageRuleToOnline, static (s, on) => s with { ApplyImageRuleToOnline = on }), FileFeatures);
        Add(SettingsSection.Images, "\uE71B", "settings.imageEscape.name", "settings.imageEscape.desc",
            Toggle("settings.imageEscape.name", static s => s.EscapeImageUrls, static (s, on) => s with { EscapeImageUrls = on }), FileFeatures);
    }

    private void AddMarkdown()
    {
        Add(SettingsSection.Markdown, "\uE8FD", "settings.codeLineNumbers.name", "settings.codeLineNumbers.desc",
            Toggle("settings.codeLineNumbers.name", static s => s.CodeLineNumbers, static (s, on) => s with { CodeLineNumbers = on }), EditingFeatures);
        Add(SettingsSection.Markdown, "\uE751", "settings.wrapCode.name", "settings.wrapCode.desc",
            Toggle("settings.wrapCode.name", static s => s.WrapCode, static (s, on) => s with { WrapCode = on }));
        Add(SettingsSection.Markdown, "\uE8EF", "settings.codeBlockMath.name", "settings.codeBlockMath.desc",
            Toggle("settings.codeBlockMath.name", static s => s.CodeBlockMath, static (s, on) => s with { CodeBlockMath = on }), EditingFeatures);
    }

    private void AddAppearance()
    {
        Add(SettingsSection.Appearance, "\uE890", "settings.statusBar.name", "settings.statusBar.desc",
            Toggle("settings.statusBar.name", static s => s.ShowStatusBar, static (s, on) => s with { ShowStatusBar = on }));
        AddReadingSpeed();
        Add(SettingsSection.Appearance, "\uE790", "settings.appTheme.name", "settings.appTheme.desc",
            Choice("settings.appTheme.name", [(AppTheme.Light, "settings.appTheme.light"), (AppTheme.Dark, "settings.appTheme.dark"), (AppTheme.System, "settings.appTheme.system")],
                static s => s.AppTheme, static (s, v) => s with { AppTheme = v }));
        Add(SettingsSection.Appearance, "\uE706", "settings.lightTheme.name", "settings.lightTheme.desc",
            ThemeChoiceBox("settings.lightTheme.name", static s => s.LightTheme, AppSettings.DefaultLightTheme, static (s, v) => s with { LightTheme = v }));
        Add(SettingsSection.Appearance, "\uE708", "settings.darkTheme.name", "settings.darkTheme.desc",
            ThemeChoiceBox("settings.darkTheme.name", static s => s.DarkTheme, AppSettings.DefaultDarkTheme, static (s, v) => s with { DarkTheme = v }));
        var themes = Add(SettingsSection.Appearance, "\uE771", "settings.themesConfig.name", "settings.themesConfig.desc", null);
        themes.IsClickEnabled = true;
        themes.ActionIcon = new FontIcon { Glyph = "\uE974" };
        themes.Click += (_, _) => OnThemesClick();
        ThemesCard = themes;
    }

    /// <summary>An integer, or one of the presets (Q105).</summary>
    private void AddReadingSpeed()
    {
        var number = new NumberBox
        {
            Width = 120,
            Minimum = AppSettings.MinReadingSpeed,
            Maximum = AppSettings.MaxReadingSpeed,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
            NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter { FractionDigits = 0, IsGrouped = false, NumberRounder = new Windows.Globalization.NumberFormatting.IncrementNumberRounder { Increment = 1 } },
        };
        _localizers.Add(() => AutomationProperties.SetName(number, UiStrings.Get("settings.readingSpeed.name")));
        ReadingSpeedBox = number;
        _refreshers.Add(settings => number.Value = settings.ReadingSpeed);
        number.ValueChanged += (_, args) =>
        {
            if (_refreshing)
            {
                return;
            }

            if (double.IsNaN(args.NewValue))
            {
                Refresh();
                return;
            }

            int speed = (int)Math.Round(args.NewValue);
            Change(settings => settings with { ReadingSpeed = speed });
        };

        var presets = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        var presetItems = new List<(int Speed, string Key, MenuFlyoutItem Item)>();
        foreach (var (speed, key) in AppSettings.ReadingSpeedPresets.Zip(["settings.readingSpeed.slow", "settings.readingSpeed.medium", "settings.readingSpeed.fast"]))
        {
            var item = new MenuFlyoutItem();
            item.Click += (_, _) => _store.Update(settings => settings with { ReadingSpeed = speed });
            presets.Items.Add(item);
            presetItems.Add((speed, key, item));
        }

        var button = new DropDownButton { Flyout = presets };
        var unit = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _localizers.Add(() =>
        {
            button.Content = UiStrings.Get("settings.readingSpeed.presets");
            AutomationProperties.SetName(button, UiStrings.Get("settings.readingSpeed.presetsName"));
            unit.Text = UiStrings.Get("settings.readingSpeed.unit");
            foreach (var (speed, key, item) in presetItems)
            {
                item.Text = $"{UiStrings.Get(key)} ({speed})";
            }
        });
        Add(SettingsSection.Appearance, "\uE916", "settings.readingSpeed.name", "settings.readingSpeed.desc",
            Row(button, number, unit));
    }

    /// <summary>
    /// The editor themes, all of them (15-themes.md); a theme that no longer exists shows the default it falls
    /// back to. The list follows <see cref="Themes"/>.
    /// </summary>
    private ComboBox ThemeChoiceBox(string nameKey, Func<AppSettings, string> get, string fallback, Func<AppSettings, string, AppSettings> set)
    {
        var box = new ComboBox { Width = ControlWidth };
        _localizers.Add(() => AutomationProperties.SetName(box, UiStrings.Get(nameKey)));
        void Fill()
        {
            box.Items.Clear();
            foreach (var theme in Themes)
            {
                box.Items.Add(theme.Name);
            }
        }

        Fill();
        _themeLists.Add(Fill);
        box.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && box.SelectedIndex >= 0 && box.SelectedIndex < Themes.Count)
            {
                string id = Themes[box.SelectedIndex].Id;
                Change(settings => set(settings, id));
            }
        };
        _refreshers.Add(settings =>
        {
            string id = get(settings);
            int index = Themes.ToList().FindIndex(theme => theme.Id == id);
            box.SelectedIndex = index >= 0 ? index : Themes.ToList().FindIndex(theme => theme.Id == fallback);
        });
        return box;
    }

    private readonly List<Action> _themeLists = [];

    /// <summary>The themes changed (a copy, a deletion, a new name): the lists show them, the choices stay.</summary>
    public void SetThemes(IReadOnlyList<ThemeChoice> themes)
    {
        Themes = themes;
        _refreshing = true;
        try
        {
            foreach (var fill in _themeLists)
            {
                fill();
            }
        }
        finally
        {
            _refreshing = false;
        }

        Refresh();
    }

    private void AddGeneral()
    {
        Add(SettingsSection.General, "\uE774", "settings.language.name", "settings.language.desc",
            Choice("settings.language.name", [(DisplayLanguage.English, "settings.language.en"), (DisplayLanguage.Chinese, "settings.language.zh")],
                static s => s.Language, static (s, v) => s with { Language = v }));

        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var add = new Button();
        var remove = new Button();
        _localizers.Add(() =>
        {
            add.Content = UiStrings.Get("settings.newMenu.add");
            AutomationProperties.SetName(add, UiStrings.Get("settings.newMenu.addName"));
            remove.Content = UiStrings.Get("settings.newMenu.remove");
            AutomationProperties.SetName(remove, UiStrings.Get("settings.newMenu.removeName"));
        });
        NewMenuStatus = status;
        AddButton = add;
        RemoveButton = remove;
        add.Click += (_, _) => ChangeNewMenu(add: true);
        remove.Click += (_, _) => ChangeNewMenu(add: false);
        Add(SettingsSection.General, "\uEC50", "settings.newMenu.name", "settings.newMenu.desc",
            Row(status, add, remove), FileFeatures);
        UpdateNewMenu();
    }
}
