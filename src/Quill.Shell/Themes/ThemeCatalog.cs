using Quill.Editor.WinUI.Theming;
using Quill.Settings;
using Quill.Settings.Themes;

namespace Quill.Shell.Themes;

/// <summary>
/// The themes of the app (15-themes.md): the built-in ones, read-only, then the user's from the
/// <see cref="ThemeStore"/>, by name. A user theme is its built-in base with its stored values applied
/// (<see cref="StyleSlots"/>); a base that does not exist falls back to Github. New themes come only from
/// copying; a theme in use (the light or dark theme of the settings) cannot be deleted. Every window shares
/// one catalog; <see cref="Changed"/> tells them to show the themes again (no new projection, 05).
/// </summary>
public sealed class ThemeCatalog
{
    private readonly ThemeStore _store;
    private readonly Func<DateTimeOffset> _now;
    private List<ThemeInfo> _themes = [];

    public ThemeCatalog(ThemeStore store, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _now = now ?? (() => DateTimeOffset.Now);
        Reload();
    }

    /// <summary>Raised after a theme was added, saved or deleted.</summary>
    public event EventHandler? Changed;

    /// <summary>Built-in themes first, then the user's by name.</summary>
    public IReadOnlyList<ThemeInfo> Themes => _themes;

    public ThemeInfo? Find(string? id) => _themes.FirstOrDefault(theme => theme.Id == id);

    /// <summary>The sheet of a theme, or of <paramref name="fallback"/> when there is no such theme.</summary>
    public MdStyleSheet Resolve(string? id, BuiltInTheme fallback) => Find(id)?.Sheet ?? fallback.Sheet;

    /// <summary>Whether a theme may be deleted: a user theme that is neither the light nor the dark theme.</summary>
    public bool CanDelete(string id, AppSettings settings) =>
        Find(id) is { IsBuiltIn: false } && id != settings.LightTheme && id != settings.DarkTheme;

    /// <summary>Copies a theme into a new user theme named "{name} copy" (with a number when taken), and saves it.</summary>
    public ThemeInfo Copy(string id)
    {
        var source = Find(id) ?? throw new ArgumentException($"No theme {id}", nameof(id));
        var basis = BuiltInThemes.Find(source.BasedOn) ?? BuiltInThemes.Github;
        var now = _now();
        var file = new ThemeFile
        {
            Id = ThemeStore.NewId(),
            Name = ThemeStore.CopyName(source.Name, _themes.Select(static theme => theme.Name)),
            Description = source.IsBuiltIn ? BuiltInThemes.Find(source.Id)?.LocalizedDescription ?? source.Description : source.Description,
            Created = now,
            Modified = now,
            BasedOn = basis.Id,
            Values = StyleSlots.Differences(basis.Sheet, source.Sheet),
        };
        _store.Save(file);
        Reload();
        Changed?.Invoke(this, EventArgs.Empty);
        return Find(file.Id)!;
    }

    /// <summary>Saves a user theme's name, description and sheet (as its differences from its base).</summary>
    public ThemeInfo Save(string id, string name, string description, MdStyleSheet sheet)
    {
        if (Find(id) is not { IsBuiltIn: false } theme)
        {
            throw new InvalidOperationException($"Theme {id} cannot be changed.");
        }

        var basis = BuiltInThemes.Find(theme.BasedOn) ?? BuiltInThemes.Github;
        _store.Save(new ThemeFile
        {
            Id = id,
            Name = name.Trim().Length > 0 ? name.Trim() : theme.Name,
            Description = description.Trim(),
            Created = theme.Created,
            Modified = _now(),
            BasedOn = basis.Id,
            Values = StyleSlots.Differences(basis.Sheet, sheet),
        });
        Reload();
        Changed?.Invoke(this, EventArgs.Empty);
        return Find(id)!;
    }

    public void Delete(string id)
    {
        if (Find(id) is { IsBuiltIn: false })
        {
            _store.Delete(id);
            Reload();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Reads the user themes again.</summary>
    public void Reload()
    {
        var themes = BuiltInThemes.All
            .Select(static theme => new ThemeInfo(theme.Id, theme.Name, theme.Description, DateTimeOffset.MinValue, DateTimeOffset.MinValue, true, theme.Id, theme.Sheet))
            .ToList();
        foreach (var file in _store.LoadAll())
        {
            if (BuiltInThemes.Find(file.Id) is not null)
            {
                continue;
            }

            var basis = BuiltInThemes.Find(file.BasedOn) ?? BuiltInThemes.Github;
            themes.Add(new ThemeInfo(file.Id, file.Name, file.Description, file.Created, file.Modified, false, basis.Id, StyleSlots.Apply(basis.Sheet, file.Values)));
        }

        _themes = themes;
    }
}
