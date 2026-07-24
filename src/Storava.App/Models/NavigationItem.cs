using MaterialDesignThemes.Wpf;

namespace Storava.App.Models;

/// <summary>A single entry in the navigation rail.</summary>
public sealed class NavigationItem
{
    public NavigationItem(string key, string titleResourceKey, PackIconKind icon, string group)
    {
        Key = key;
        TitleResourceKey = titleResourceKey;
        Icon = icon;
        Group = group;
    }

    public string Key { get; }

    /// <summary>Resource key resolved live via DynamicResource in the item template.</summary>
    public string TitleResourceKey { get; }

    public PackIconKind Icon { get; }

    /// <summary>Group header this item belongs to (localized resource key).</summary>
    public string Group { get; }
}
