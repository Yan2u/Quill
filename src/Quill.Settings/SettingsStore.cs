using System.Text.Json.Nodes;

namespace Quill.Settings;

/// <summary>The settings in <c>settings.json</c> (14-settings.md); one store for every window of the process.</summary>
public sealed class SettingsStore : JsonStore<AppSettings>
{
    public const int CurrentVersion = 1;

    public SettingsStore(string path)
        : base(path)
    {
        Load();
    }

    public static string DefaultPath => Path.Combine(AppDataFolder.Path, "settings.json");

    protected override int Version => CurrentVersion;

    protected override AppSettings Normalize(AppSettings value) => value.Normalize();

    protected override AppSettings Read(JsonObject json)
    {
        var d = AppSettings.Default;
        return new AppSettings
        {
            Startup = JsonFields.Enum(json, "startup", d.Startup),
            StartupFolder = JsonFields.String(json, "startupFolder", d.StartupFolder),
            DefaultExtension = JsonFields.Enum(json, "defaultExtension", d.DefaultExtension),
            CustomExtension = JsonFields.String(json, "customExtension", d.CustomExtension),
            AutoSave = JsonFields.Bool(json, "autoSave", d.AutoSave),
            AutoSaveOnSwitch = JsonFields.Bool(json, "autoSaveOnSwitch", d.AutoSaveOnSwitch),
            DropFolder = JsonFields.Enum(json, "dropFolder", d.DropFolder),
            DropMarkdown = JsonFields.Enum(json, "dropMarkdown", d.DropMarkdown),
            DropOtherFile = JsonFields.Enum(json, "dropOtherFile", d.DropOtherFile),
            MatchBrackets = JsonFields.Bool(json, "matchBrackets", d.MatchBrackets),
            CopyMarkdownSource = JsonFields.Bool(json, "copyMarkdownSource", d.CopyMarkdownSource),
            CopyLineWithoutSelection = JsonFields.Bool(json, "copyLineWithoutSelection", d.CopyLineWithoutSelection),
            DefaultLineBreak = JsonFields.Enum(json, "defaultLineBreak", d.DefaultLineBreak),
            SpellCheckLanguage = JsonFields.String(json, "spellCheckLanguage", d.SpellCheckLanguage) ?? d.SpellCheckLanguage,
            ImageRule = JsonFields.Enum(json, "imageRule", d.ImageRule),
            ImageFolder = JsonFields.String(json, "imageFolder", d.ImageFolder),
            ApplyImageRuleToLocal = JsonFields.Bool(json, "applyImageRuleToLocal", d.ApplyImageRuleToLocal),
            ApplyImageRuleToOnline = JsonFields.Bool(json, "applyImageRuleToOnline", d.ApplyImageRuleToOnline),
            EscapeImageUrls = JsonFields.Bool(json, "escapeImageUrls", d.EscapeImageUrls),
            CodeLineNumbers = JsonFields.Bool(json, "codeLineNumbers", d.CodeLineNumbers),
            WrapCode = JsonFields.Bool(json, "wrapCode", d.WrapCode),
            CodeBlockMath = JsonFields.Bool(json, "codeBlockMath", d.CodeBlockMath),
            ShowStatusBar = JsonFields.Bool(json, "showStatusBar", d.ShowStatusBar),
            ReadingSpeed = JsonFields.Int(json, "readingSpeed", d.ReadingSpeed),
            AppTheme = JsonFields.Enum(json, "appTheme", d.AppTheme),
            LightTheme = JsonFields.String(json, "lightTheme", d.LightTheme) ?? d.LightTheme,
            DarkTheme = JsonFields.String(json, "darkTheme", d.DarkTheme) ?? d.DarkTheme,
            Language = JsonFields.Enum(json, "language", d.Language),
        };
    }

    protected override void Write(AppSettings value, JsonObject json)
    {
        JsonFields.Set(json, "startup", value.Startup);
        json["startupFolder"] = value.StartupFolder;
        JsonFields.Set(json, "defaultExtension", value.DefaultExtension);
        json["customExtension"] = value.CustomExtension;
        json["autoSave"] = value.AutoSave;
        json["autoSaveOnSwitch"] = value.AutoSaveOnSwitch;
        JsonFields.Set(json, "dropFolder", value.DropFolder);
        JsonFields.Set(json, "dropMarkdown", value.DropMarkdown);
        JsonFields.Set(json, "dropOtherFile", value.DropOtherFile);
        json["matchBrackets"] = value.MatchBrackets;
        json["copyMarkdownSource"] = value.CopyMarkdownSource;
        json["copyLineWithoutSelection"] = value.CopyLineWithoutSelection;
        JsonFields.Set(json, "defaultLineBreak", value.DefaultLineBreak);
        json["spellCheckLanguage"] = value.SpellCheckLanguage;
        JsonFields.Set(json, "imageRule", value.ImageRule);
        json["imageFolder"] = value.ImageFolder;
        json["applyImageRuleToLocal"] = value.ApplyImageRuleToLocal;
        json["applyImageRuleToOnline"] = value.ApplyImageRuleToOnline;
        json["escapeImageUrls"] = value.EscapeImageUrls;
        json["codeLineNumbers"] = value.CodeLineNumbers;
        json["wrapCode"] = value.WrapCode;
        json["codeBlockMath"] = value.CodeBlockMath;
        json["showStatusBar"] = value.ShowStatusBar;
        json["readingSpeed"] = value.ReadingSpeed;
        JsonFields.Set(json, "appTheme", value.AppTheme);
        json["lightTheme"] = value.LightTheme;
        json["darkTheme"] = value.DarkTheme;
        JsonFields.Set(json, "language", value.Language);
    }
}
