using System.Globalization;
using Storava.Application.Common;

namespace Storava.App.Models;

/// <summary>Display model for a drive tile on the dashboard.</summary>
public sealed class DriveCardModel
{
    public DriveCardModel(DriveSnapshot snapshot, CultureInfo culture)
    {
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
