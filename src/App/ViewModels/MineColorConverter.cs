using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PalladiumWallet.App.ViewModels;

/// <summary>
/// bool → brush: the wallet's own addresses ("our" inputs/outputs) are
/// highlighted in green, the others use the default text color.
/// </summary>
public sealed class MineColorConverter : IValueConverter
{
    public static readonly MineColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Brushes.MediumSeaGreen : AvaloniaProperty.UnsetValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
