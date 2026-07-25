using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Storava.Application.Common;

namespace Storava.App.Models;

/// <summary>Display model for a drive tile. Observable so selection state can be reflected live.</summary>
public sealed partial class DriveCardModel : ObservableObject
{
    /// <summary>True when this drive is the current scan target.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public DriveCardModel(DriveSnapshot snapshot, CultureInfo culture)
    {
        Root = snapshot.Name;
        Name = snapshot.VolumeLabel is { Length: > 0 } label
            ? $"{snapshot.Name.TrimEnd('\\')} · {label}"
            : snapshot.Name.TrimEnd('\\');
        Format = snapshot.DriveFormat;
        UsedText = snapshot.UsedSpace.Humanize(culture);
        TotalText = snapshot.TotalSize.Humanize(culture);
        FreeText = snapshot.FreeSpace.Humanize(culture);
        UsedFraction = snapshot.UsedFraction;
        UsedPercent = (int)Math.Round(snapshot.UsedFraction * 100);
        IsReady = snapshot.IsReady;
        // Bar turns amber above 80% and red above 92% usage.
        BarColor = snapshot.UsedFraction switch
        {
            >= 0.92 => "#EF4444",
            >= 0.80 => "#F59E0B",
            _ => "#0FB5AE"
        };
    }

    /// <summary>The drive's root path, e.g. "C:\".</summary>
    public string Root { get; }

    public string Name { get; }
    public string Format { get; }
    public string UsedText { get; }
    public string TotalText { get; }
    public string FreeText { get; }
    public double UsedFraction { get; }
    public int UsedPercent { get; }
    public bool IsReady { get; }
    public string BarColor { get; }
}
