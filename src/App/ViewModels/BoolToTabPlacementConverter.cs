using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace PalladiumWallet.App.ViewModels;

/// <summary>
/// true (mobile) → Dock.Bottom — tab strip in basso, standard Android.
/// false (desktop) → Dock.Top — comportamento predefinito Avalonia.
/// </summary>
public sealed class BoolToTabPlacementConverter : IValueConverter
{
    public static readonly BoolToTabPlacementConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Dock.Bottom : Dock.Top;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
