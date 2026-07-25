using System.Globalization;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;
using Storava.Domain.Enums;

namespace Storava.App.Converters;

/// <summary>Maps an item type to a folder/file icon.</summary>
public sealed class ItemTypeToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ItemType.Folder ? PackIconKind.FolderOutline : PackIconKind.FileOutline;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
