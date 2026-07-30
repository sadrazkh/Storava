using System.Globalization;
using Storava.Application.Abstractions;
using Storava.Domain.ValueObjects;

namespace Storava.App.Models;

/// <summary>
/// One of Storava's own stores, as the Settings page shows it.
/// <para>
/// Each row says what it is, how big it is, and — when it cannot be cleared from here — why not. A
/// disabled button with no explanation beside it reads as a broken button.
/// </para>
/// </summary>
public sealed class AppStorageItemModel
{
    public AppStorageItemModel(AppStorageEntry entry, CultureInfo culture, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(localization);

        Kind = entry.Kind;
        Location = entry.Location;
        SizeText = new ByteSize(entry.Bytes).Humanize(culture);
        CanClear = entry.CanClear && entry.Exists;

        Title = localization[$"Str.Settings.Storage.Item.{entry.Kind}"];
        Description = localization[$"Str.Settings.Storage.Item.{entry.Kind}.Body"];

        // Two different reasons a row has no button, and they are not interchangeable: one is a
        // decision this application made, the other is simply that there is nothing there yet.
        WhyNotClearable = entry.CanClear
            ? (entry.Exists ? string.Empty : localization["Str.Settings.Storage.Item.Empty"])
            : localization[$"Str.Settings.Storage.Item.{entry.Kind}.Kept"];

        HasWhyNot = WhyNotClearable.Length > 0 && !WhyNotClearable.StartsWith("Str.", StringComparison.Ordinal);
    }

    public AppStorageKind Kind { get; }

    public string Title { get; }

    public string Description { get; }

    /// <summary>Where it is, so somebody can go and look for themselves.</summary>
    public string Location { get; }

    public string SizeText { get; }

    /// <summary>
    /// False for a store that does not exist yet as well as one this application will not touch:
    /// offering to empty something that is already absent is a button that can only disappoint.
    /// </summary>
    public bool CanClear { get; }

    public string WhyNotClearable { get; }

    public bool HasWhyNot { get; }
}
