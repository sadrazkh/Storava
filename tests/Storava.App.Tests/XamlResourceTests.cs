using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MaterialDesignThemes.Wpf;

namespace Storava.App.Tests;

/// <summary>
/// XAML resource lookups and icon names are not checked by the compiler: an unknown
/// <see cref="PackIconKind"/> throws when the page is first shown, and a missing string key
/// renders as the raw key. Both only appear on the page that happens to be opened, so they are
/// verified here across every view instead.
/// </summary>
public class XamlResourceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!;
    }

    private static string AppDirectory() => Path.Combine(RepositoryRoot().FullName, "src", "Storava.App");

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(AppDirectory(), "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void EveryIconNameResolvesToAKnownKind()
    {
        // Matches both the attribute form (Kind="X") and the style form (Property="Kind" Value="X").
        var attribute = new Regex(@"\bKind=""(?<name>[A-Za-z0-9_]+)""", RegexOptions.Compiled);
        var setter = new Regex(@"Property=""Kind""\s+Value=""(?<name>[A-Za-z0-9_]+)""", RegexOptions.Compiled);

        var unknown = new List<string>();

        foreach (string file in XamlFiles())
        {
            string content = File.ReadAllText(file);
            var names = attribute.Matches(content).Concat(setter.Matches(content))
                .Select(m => m.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal);

            foreach (string name in names)
            {
                if (!Enum.TryParse<PackIconKind>(name, ignoreCase: false, out _))
                    unknown.Add($"{Path.GetFileName(file)}: {name}");
            }
        }

        Assert.True(unknown.Count == 0, $"Unknown PackIconKind values: {string.Join(", ", unknown)}");
    }

    [Fact]
    public void EveryLocalizedResourceKeyUsedInXamlExists()
    {
        var defined = LocalizationKeys();
        var reference = new Regex(@"\{DynamicResource (?<key>Str\.[A-Za-z0-9_.]+)\}", RegexOptions.Compiled);

        var missing = new List<string>();

        foreach (string file in XamlFiles())
        {
            foreach (Match match in reference.Matches(File.ReadAllText(file)))
            {
                string key = match.Groups["key"].Value;
                if (!defined.Contains(key))
                    missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }

        Assert.True(missing.Count == 0, $"Undefined string keys: {string.Join(", ", missing.Distinct(StringComparer.Ordinal))}");
    }

    private static HashSet<string> LocalizationKeys()
    {
        string path = Path.Combine(AppDirectory(), "Resources", "Localization", "Strings.en.xaml");
        var document = XDocument.Load(path);
        return document.Root!.Elements()
            .Select(e => e.Attribute(X + "Key")?.Value)
            .Where(k => k is not null)
            .Select(k => k!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
