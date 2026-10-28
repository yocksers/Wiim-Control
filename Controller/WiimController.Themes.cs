namespace WiimControl;

sealed partial class WiimController
{
    private List<Theme> _customThemes = [];
    private Theme _activeTheme = ThemeEngine.Dark;
    internal IReadOnlyList<Theme> CustomThemes => _customThemes;

    internal IEnumerable<Theme> AllThemes => ThemeEngine.BuiltIn.Concat(_customThemes);

    internal bool ThemeNameTaken(string name, string? except = null) =>
        AllThemes.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !t.Name.Equals(except, StringComparison.OrdinalIgnoreCase));

    internal string FreeThemeName(string baseName)
    {
        if (!ThemeNameTaken(baseName)) return baseName;
        for (int n = 2; ; n++)
            if (!ThemeNameTaken($"{baseName} {n}")) return $"{baseName} {n}";
    }

    internal void UseTheme(Theme theme)
    {
        _activeTheme = theme;
        ThemeEngine.Apply(theme);
        SaveConfig();
    }

    internal void SaveTheme(Theme theme, string? replacing = null)
    {
        int index = _customThemes.FindIndex(t => t.Name.Equals(replacing ?? theme.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _customThemes[index] = theme;
        else _customThemes.Add(theme);
        UseTheme(theme);
    }

    internal void DeleteTheme(string name)
    {
        _customThemes.RemoveAll(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (_activeTheme.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) UseTheme(ThemeEngine.Dark);
        else SaveConfig();
    }

    private void LoadThemeSetting(string key, string value)
    {
        if (key == "themes") _customThemes = ThemeEngine.ParseThemes(value);
        if (key == "theme" && ThemeEngine.ParseTheme(value) is { } theme) _activeTheme = theme;
    }
}
