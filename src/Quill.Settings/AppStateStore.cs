using System.Text.Json.Nodes;

namespace Quill.Settings;

/// <summary>The application state in <c>state.json</c> (14-settings.md).</summary>
public sealed class AppStateStore : JsonStore<AppState>
{
    public const int CurrentVersion = 1;

    public AppStateStore(string path)
        : base(path)
    {
        Load();
    }

    public static string DefaultPath => Path.Combine(AppDataFolder.Path, "state.json");

    protected override int Version => CurrentVersion;

    protected override AppState Normalize(AppState value) => value.Normalize();

    protected override AppState Read(JsonObject json)
    {
        var d = AppState.Default;
        return new AppState
        {
            SidebarVisible = JsonFields.Bool(json, "sidebarVisible", d.SidebarVisible),
            SidebarWidth = JsonFields.Double(json, "sidebarWidth", d.SidebarWidth),
            SidebarTab = JsonFields.Enum(json, "sidebarTab", d.SidebarTab),
            FilesView = JsonFields.Enum(json, "filesView", d.FilesView),
            NavigationExpanded = JsonFields.Bool(json, "navigationExpanded", d.NavigationExpanded),
            WindowWidth = JsonFields.NullableInt(json, "windowWidth", d.WindowWidth),
            WindowHeight = JsonFields.NullableInt(json, "windowHeight", d.WindowHeight),
            WindowMaximized = JsonFields.Bool(json, "windowMaximized", d.WindowMaximized),
            LastFolder = JsonFields.String(json, "lastFolder", d.LastFolder),
            LastFile = JsonFields.String(json, "lastFile", d.LastFile),
        };
    }

    protected override void Write(AppState value, JsonObject json)
    {
        json["sidebarVisible"] = value.SidebarVisible;
        json["sidebarWidth"] = value.SidebarWidth;
        JsonFields.Set(json, "sidebarTab", value.SidebarTab);
        JsonFields.Set(json, "filesView", value.FilesView);
        json["navigationExpanded"] = value.NavigationExpanded;
        json["windowWidth"] = value.WindowWidth;
        json["windowHeight"] = value.WindowHeight;
        json["windowMaximized"] = value.WindowMaximized;
        json["lastFolder"] = value.LastFolder;
        json["lastFile"] = value.LastFile;
    }
}
