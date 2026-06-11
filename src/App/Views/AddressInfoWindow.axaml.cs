using Avalonia.Controls;
using Avalonia.Interactivity;
using PalladiumWallet.App.ViewModels;

namespace PalladiumWallet.App.Views;

public partial class AddressInfoWindow : Window
{
    public AddressInfoWindow()
    {
        InitializeComponent();
    }

    public AddressInfoWindow(AddressInfo info) : this()
    {
        DataContext = info;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
