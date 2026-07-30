using System.IO;
using System.Xml.Linq;
using Storava.Application.Abstractions;

namespace Storava.App.Tests;

/// <summary>
/// Guards the string dictionaries. A duplicate key throws only at runtime when WPF merges the
/// dictionary, and a key present in one language but not the other shows up as a raw key in the
/// UI — both are caught here instead.
/// </summary>
public class LocalizationResourceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ResourcePath(string file)
    {
        // Walk up from the test output directory to the repository root.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "Storava.App", "Resources", "Localization", file);
    }

    private static List<string> KeysOf(string file)
    {
        var document = XDocument.Load(ResourcePath(file));
        return document.Root!.Elements()
            .Select(e => e.Attribute(X + "Key")?.Value)
            .Where(k => k is not null)
            .Select(k => k!)
            .ToList();
    }

    [Theory]
    [InlineData("Strings.en.xaml")]
    [InlineData("Strings.fa.xaml")]
    public void Dictionary_HasNoDuplicateKeys(string file)
    {
        var keys = KeysOf(file);
        var duplicates = keys.GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"{file} has duplicate keys: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void Dictionaries_CoverTheSameKeys()
    {
        var english = KeysOf("Strings.en.xaml").ToHashSet(StringComparer.Ordinal);
        var persian = KeysOf("Strings.fa.xaml").ToHashSet(StringComparer.Ordinal);

        var missingInPersian = english.Except(persian, StringComparer.Ordinal).ToList();
        var missingInEnglish = persian.Except(english, StringComparer.Ordinal).ToList();

        Assert.True(missingInPersian.Count == 0, $"Missing Persian strings: {string.Join(", ", missingInPersian)}");
        Assert.True(missingInEnglish.Count == 0, $"Missing English strings: {string.Join(", ", missingInEnglish)}");
    }

    [Theory]
    [InlineData("Strings.en.xaml")]
    [InlineData("Strings.fa.xaml")]
    public void Dictionary_DefinesTheActiveFont(string file)
    {
        // The localization service identifies the active dictionary by this key.
        Assert.Contains("App.FontFamily", KeysOf(file));
    }

    /// <summary>
    /// The storage rows build their keys from the enum name, so adding a store to the enum without a
    /// string leaves a row reading "Str.Settings.Storage.Item.Whatever" in the window. Composed keys
    /// cannot be found by searching the dictionaries, so they are checked here instead.
    /// </summary>
    [Theory]
    [InlineData("Strings.en.xaml")]
    [InlineData("Strings.fa.xaml")]
    public void Dictionary_NamesEveryStorageStore(string file)
    {
        var keys = KeysOf(file).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var kind in Enum.GetNames<AppStorageKind>())
        {
            foreach (var suffix in new[] { "", ".Body" })
            {
                string key = $"Str.Settings.Storage.Item.{kind}{suffix}";
                if (!keys.Contains(key))
                    missing.Add(key);
            }
        }

        // Only the stores this application refuses to empty carry a reason.
        foreach (var kind in new[] { AppStorageKind.Secrets, AppStorageKind.Agent, AppStorageKind.AccountServer })
        {
            string key = $"Str.Settings.Storage.Item.{kind}.Kept";
            if (!keys.Contains(key))
                missing.Add(key);
        }

        Assert.True(missing.Count == 0, $"{file} is missing: {string.Join(", ", missing)}");
    }
}
