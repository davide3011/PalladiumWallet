using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PalladiumWallet.App.ViewModels;

/// <summary>
/// bool → pennello: gli indirizzi del wallet (input/output "nostri") sono
/// evidenziati in verde, gli altri usano il colore di testo predefinito.
/// </summary>
public sealed class MineColorConverter : IValueConverter
{
    public static readonly MineColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Brushes.MediumSeaGreen : AvaloniaProperty.UnsetValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
