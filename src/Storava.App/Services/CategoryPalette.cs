using System.Windows.Media;
using Storava.Domain.Enums;

namespace Storava.App.Services;

/// <summary>
/// Stable colours for categories and risk levels, for the places that need a <see cref="Color"/>
/// rather than a brush — the charts, which draw their own geometry.
/// <para>
/// Nothing here produces a brush for a tag any more. A brush built in code is frozen at the theme
/// in force when it was made, so tags resolve their colours from the palette dictionary instead
/// and follow when the user switches. Leaving a second way to colour a risk here would be an
/// invitation to drift back.
/// </para>
/// </summary>
public static class CategoryPalette
{
    private static readonly Dictionary<StorageCategory, Color> CategoryColors = new()
    {
        [StorageCategory.PersonalFiles] = FromHex("#3B82F6"),
        [StorageCategory.Applications] = FromHex("#8B5CF6"),
        [StorageCategory.WindowsSystem] = FromHex("#64748B"),
        [StorageCategory.TemporaryFiles] = FromHex("#F59E0B"),
        [StorageCategory.DeveloperTools] = FromHex("#0EA5E9"),
        [StorageCategory.PackageCaches] = FromHex("#0FB5AE"),
        [StorageCategory.BuildArtifacts] = FromHex("#14B8A6"),
        [StorageCategory.Docker] = FromHex("#2496ED"),
        [StorageCategory.Wsl] = FromHex("#4C1D95"),
        [StorageCategory.VirtualMachines] = FromHex("#7C3AED"),
        [StorageCategory.AiModels] = FromHex("#EC4899"),
        [StorageCategory.Sdks] = FromHex("#06B6D4"),
        [StorageCategory.IdeCaches] = FromHex("#22D3EE"),
        [StorageCategory.BrowserCaches] = FromHex("#FB923C"),
        [StorageCategory.GameLibraries] = FromHex("#A3E635"),
        [StorageCategory.Downloads] = FromHex("#FACC15"),
        [StorageCategory.Media] = FromHex("#F472B6"),
        [StorageCategory.Backups] = FromHex("#34D399"),
        [StorageCategory.Archives] = FromHex("#A78BFA"),
        [StorageCategory.Logs] = FromHex("#94A3B8"),
        [StorageCategory.Unknown] = FromHex("#475569")
    };

    private static readonly Dictionary<RiskLevel, Color> RiskColors = new()
    {
        [RiskLevel.Low] = FromHex("#22C55E"),
        [RiskLevel.Medium] = FromHex("#F59E0B"),
        [RiskLevel.High] = FromHex("#EF4444"),
        [RiskLevel.Protected] = FromHex("#64748B"),
        [RiskLevel.Unknown] = FromHex("#94A3B8")
    };

    public static Color ForCategory(StorageCategory category) =>
        CategoryColors.TryGetValue(category, out var color) ? color : CategoryColors[StorageCategory.Unknown];

    public static Color ForRisk(RiskLevel risk) =>
        RiskColors.TryGetValue(risk, out var color) ? color : RiskColors[RiskLevel.Unknown];

    private static Color FromHex(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
}
